
extends Area2D

var entity_id: int = 0
var item_index: int = 0
var item_count: int = 1


var remaining_time: float = 0.0
var owner_dbid: int = 0
var protect_time: float = 0.0
var _last_protection_display_second: int = -1
var _remaining_expires_at_msec: int = 0
var _protect_expires_at_msec: int = 0
var _protection_timer: Timer = null

@onready var sprite: Sprite2D = $Sprite2D
@onready var name_label: Label = $LabelContainer / NameLabel
@onready var time_label: Label = $LabelContainer / TimeLabel
@onready var label_container: VBoxContainer = $LabelContainer

const PALETTE_SHADER = preload("res://palette_swap.gdshader")
# Share immutable collision resources and identical palette materials across
# item instances. Every drop still has its own Area2D and can be clicked.
static var _shared_click_shapes: Dictionary = {}
static var _shared_palette_materials: Dictionary = {}
const COLOR_INDEX_TO_DEGREES = {
	0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 
	4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0
}

static func get_shared_click_shape(texture_size: Vector2) -> RectangleShape2D:
	var key := Vector2i(int(texture_size.x), int(texture_size.y))
	var cached := _shared_click_shapes.get(key) as RectangleShape2D
	if is_instance_valid(cached):
		return cached
	var shape := RectangleShape2D.new()
	shape.size = texture_size + Vector2(30.0, 30.0)
	_shared_click_shapes[key] = shape
	return shape


static func get_shared_palette_material(color_index: int) -> ShaderMaterial:
	var cached := _shared_palette_materials.get(color_index) as ShaderMaterial
	if is_instance_valid(cached):
		return cached
	var material := ShaderMaterial.new()
	material.shader = PALETTE_SHADER
	material.set_shader_parameter(
		"hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0)
	)
	_shared_palette_materials[color_index] = material
	return material


func _ready() -> void :
	set_process(false)
	if time_label:
		time_label.visible = false
		time_label.add_theme_color_override("font_color", Color(1.0, 1.0, 1.0, 1.0))
		time_label.add_theme_color_override("font_outline_color", Color(0.082, 0.52, 1.0, 1.0))
		time_label.add_theme_font_size_override("font_size", 20)
		time_label.add_theme_constant_override("outline_size", 5)
		time_label.z_index = 100


	self.input_pickable = true


	if label_container: label_container.mouse_filter = Control.MOUSE_FILTER_IGNORE
	if name_label: name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	if time_label: time_label.mouse_filter = Control.MOUSE_FILTER_IGNORE


	if mouse_entered.is_connected(_on_mouse_entered): mouse_entered.disconnect(_on_mouse_entered)
	mouse_entered.connect(_on_mouse_entered)

	if mouse_exited.is_connected(_on_mouse_exited): mouse_exited.disconnect(_on_mouse_exited)
	mouse_exited.connect(_on_mouse_exited)

	if input_event.is_connected(_on_input_event): input_event.disconnect(_on_input_event)
	input_event.connect(_on_input_event)

func _exit_tree() -> void :

	if CursorManager:
		CursorManager.unregister_hover(self)

func initialize_ground_item(data: Dictionary) -> void :
	entity_id = int(data.get("EntityID", 0))
	var extra = data.get("ExtraData", {})
	item_index = int(extra.get("ItemIndex", 0))
	item_count = int(extra.get("Count", 1))


	remaining_time = float(extra.get("RemainingTime", 60.0))


	owner_dbid = int(extra.get("OwnerDBID", 0))
	protect_time = float(extra.get("ProtectTimeSeconds", 0.0))
	var now_msec: = Time.get_ticks_msec()
	_remaining_expires_at_msec = now_msec + int(round(remaining_time * 1000.0)) if remaining_time > 0.0 else 0
	_protect_expires_at_msec = now_msec + int(round(protect_time * 1000.0)) if protect_time > 0.0 else 0
	set_process(false)

	var item_name = str(data.get("Name", "未知物品"))
	name_label.text = item_name


	_update_protection_visuals()
	_schedule_protection_update()

	var icon_id = ItemLoader.get_item_property(item_index, "icon_id", 1) - 1
	sprite.texture = ItemLoader.get_ground(icon_id)


	if sprite.texture:
		var tex_h = sprite.texture.get_height()
		var tex_w = sprite.texture.get_width()


		var offset_y = - tex_h + (tex_h * 0.2)
		sprite.offset = Vector2( - tex_w / 2.0, offset_y)


		if has_node("CollisionShape2D"):
			var col_shape = $CollisionShape2D


			col_shape.shape = get_shared_click_shape(Vector2(tex_w, tex_h))


			col_shape.position = sprite.offset



		if has_node("LabelContainer"):
			var container = get_node("LabelContainer")
			var container_height = container.size.y
			container.position = Vector2(-100, (offset_y - (tex_h / 2.0)) - container_height - 15)


	var color_index: int = int(ItemLoader.get_item_property(item_index, "color_index", 0))
	if color_index > 0:
		# There are no later per-item shader uniform mutations.
		sprite.material = get_shared_palette_material(color_index)


func _on_mouse_entered() -> void :
	_update_protection_visuals()
	if CursorManager:
		CursorManager.register_hover(self, "pick")


func _on_mouse_exited() -> void :
	if CursorManager:
		CursorManager.unregister_hover(self)


func _on_input_event(viewport: Node, event: InputEvent, shape_idx: int) -> void :
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:

		if CursorManager and CursorManager.current_hovered_entity != self:
			return


		get_viewport().set_input_as_handled()
		request_pickup()

func request_pickup() -> void :

	if get_meta("is_requesting", false):
		return
	_sync_deadline_times()

	var player = null
	if GameManager and is_instance_valid(GameManager.current_player):
		player = GameManager.current_player


		if player.get("is_dead") or player.get("is_action_locked") or (player.get("data_model") and player.data_model.get("is_shop_open")):
			return

	# Ground-item pickup may start its own approach navigation. Stop
	# cursor-follow walking first so it cannot immediately repath away.
	if is_instance_valid(player) and player.has_method("cancel_autorun_tracking"):
		player.cancel_autorun_tracking(true)
	set_meta("is_requesting", true)


	if is_instance_valid(player):
		var dist = global_position.distance_to(player.global_position)


		if dist > 50.0:
			player.combat_target = null
			player.is_emoting = false

			if player.has_method("_set_navigation_target"):
				player._set_navigation_target(global_position)
			else:
				player.target_position = global_position
				player.is_moving = true

			if not player.is_moving:
				set_meta("is_requesting", false)
				return

			if player.current_state != "walk":
				player.current_state = "walk"
				if player.has_method("sync_animation"):
					player.sync_animation()

			while is_instance_valid(self) and is_instance_valid(player):
				dist = global_position.distance_to(player.global_position)
				if dist <= 50.0:
					if player.has_method("_stop_movement"):
						player._stop_movement()
					else:
						player.is_moving = false
						player.target_position = player.global_position
					break

				if not player.is_moving:
					set_meta("is_requesting", false)
					return

				await get_tree().process_frame


	if not is_instance_valid(self):
		return


	NetworkManager.send_pickup_item_request(entity_id)


	await get_tree().create_timer(0.5).timeout
	if is_instance_valid(self):
		set_meta("is_requesting", false)


func _update_protection_visuals() -> void :
	_sync_deadline_times()
	if not is_instance_valid(name_label) or not is_instance_valid(time_label):
		return

	if protect_time > 0 and owner_dbid != 0:

		time_label.visible = true
		_update_protection_time_label()


		var my_dbid = 0
		if GameManager:
			if is_instance_valid(GameManager.current_player) and "data_model" in GameManager.current_player:
				my_dbid = GameManager.current_player.data_model.db_id

		if owner_dbid == my_dbid:
			name_label.add_theme_color_override("font_color", Color(0.922, 0.883, 0.0, 1.0))
		else:
			name_label.add_theme_color_override("font_color", Color(1.0, 0.091, 0.056, 1.0))

	else:

		time_label.visible = false
		_last_protection_display_second = -1
		name_label.add_theme_color_override("font_color", Color.WHITE)

func _update_protection_time_label() -> void :
	var displayed_second: int = int(ceil(protect_time))
	if displayed_second != _last_protection_display_second:
		_last_protection_display_second = displayed_second
		time_label.text = str(displayed_second)

func _sync_deadline_times() -> void :
	var now_msec: = Time.get_ticks_msec()
	remaining_time = maxf(0.0, float(_remaining_expires_at_msec - now_msec) / 1000.0) if _remaining_expires_at_msec > 0 else 0.0
	protect_time = maxf(0.0, float(_protect_expires_at_msec - now_msec) / 1000.0) if _protect_expires_at_msec > 0 else 0.0

func _schedule_protection_update() -> void :
	_sync_deadline_times()
	if protect_time <= 0.0 or owner_dbid == 0:
		if is_instance_valid(_protection_timer):
			_protection_timer.stop()
		return

	if not is_instance_valid(_protection_timer):
		_protection_timer = Timer.new()
		_protection_timer.one_shot = true
		_protection_timer.timeout.connect(_on_protection_timer_timeout)
		add_child(_protection_timer)

	var displayed_second: = int(ceil(protect_time))
	var until_next_second: = protect_time - float(displayed_second - 1)
	_protection_timer.start(maxf(0.01, until_next_second))

func _on_protection_timer_timeout() -> void :
	_update_protection_visuals()
	_schedule_protection_update()
