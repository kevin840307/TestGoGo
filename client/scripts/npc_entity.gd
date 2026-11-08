
extends Area2D

const LEGACY_DIALOG_BUBBLE = preload("res://scripts/legacy_dialog_bubble.gd")
const LEGACY_BUBBLE_SKIN = preload("res://scripts/legacy_bubble_skin.gd")

static var _offset_cache: Dictionary = {}
static var _offset_file_read_count: int = 0
static var _offset_cache_hit_count: int = 0

var entity_id: int = 0
var npc_name: String = ""


@onready var visual_center: Control = $VisualCenter

@onready var sprite: Sprite2D = $Sprite2D
@onready var collision: CollisionShape2D = $CollisionShape2D
@onready var name_label: Label = $VisualCenter / NameLabel


@onready var chat_bubble_bg: NinePatchRect = $VisualCenter / ChatBubbleBG
@onready var chat_bubble_label: Label = $VisualCenter / ChatBubbleBG / ChatBubbleLabel
var bubble_fade_tween: Tween = null


var is_bubble_active: bool = false

var npc_script_name: String = ""
var is_portal: bool = false
var _pending_initialize_data: Dictionary = {}

func _ready() -> void :
	chat_bubble_bg.visible = false
	is_bubble_active = false


	chat_bubble_bg.z_as_relative = false
	chat_bubble_bg.z_index = 100

	name_label.z_as_relative = false
	name_label.z_index = 100


	mouse_entered.connect(_on_mouse_entered)
	mouse_exited.connect(_on_mouse_exited)


	chat_bubble_label.add_theme_color_override("font_color", Color.BLACK)
	chat_bubble_label.add_theme_font_size_override("font_size", 13)
	chat_bubble_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	chat_bubble_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	chat_bubble_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	chat_bubble_label.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS

	if not _pending_initialize_data.is_empty():
		var pending_data := _pending_initialize_data
		_pending_initialize_data = {}
		_apply_npc_data(pending_data)

func initialize_npc(data: Dictionary) -> void :
	# Entity packets can arrive while the new map exists but is not inside the
	# SceneTree yet. In that case @onready members are still null, so defer the
	# visual/UI part until _ready() runs.
	entity_id = int(data.get("EntityID", 0))
	npc_name = str(data.get("Name", "未知NPC"))
	var extra = data.get("ExtraData", {})
	npc_script_name = str(extra.get("Script", ""))
	is_portal = bool(extra.get("IsPortal", npc_script_name.begins_with("Portal_")))

	if not is_node_ready():
		_pending_initialize_data = data.duplicate(true)
		return

	_apply_npc_data(data)

func _apply_npc_data(data: Dictionary) -> void :
	npc_name = str(data.get("Name", "未知NPC"))
	name_label.text = npc_name

	var extra = data.get("ExtraData", {})
	var sprite_name = str(extra.get("Sprite", ""))
	npc_script_name = str(extra.get("Script", ""))
	is_portal = bool(extra.get("IsPortal", npc_script_name.begins_with("Portal_")))

	_load_visuals(sprite_name)

func _load_visuals(sprite_name: String) -> void :
	if sprite_name == "": return

	var tex_path = "res://Assets/NPC/" + sprite_name
	var txt_path = "res://Assets/NPC/" + sprite_name.replace(".png", ".txt")

	if ResourceLoader.exists(tex_path):
		var tex = load(tex_path) as Texture2D
		sprite.texture = tex

		var offset_vector: = _get_visual_offset(txt_path)

		if sprite.texture:
			var tex_w = float(tex.get_width())
			var tex_h = float(tex.get_height())


			sprite.position = Vector2.ZERO
			if offset_vector != Vector2.ZERO:

				sprite.centered = false
				sprite.offset = Vector2( - offset_vector.x, - offset_vector.y)
			else:

				sprite.centered = true
				sprite.offset = Vector2(0, - tex_h / 2.0)


			var rect = RectangleShape2D.new()
			rect.size = Vector2(tex_w, tex_h)
			collision.shape = rect

			if offset_vector != Vector2.ZERO:
				collision.position = Vector2( - offset_vector.x + (tex_w / 2.0), - offset_vector.y + (tex_h / 2.0))
			else:
				collision.position = Vector2(0, - tex_h / 2.0)


			visual_center.position.x = 0
			if offset_vector != Vector2.ZERO:

				visual_center.position.y = - offset_vector.y - 15.0
			else:
				visual_center.position.y = - tex_h - 15.0


			name_label.position = Vector2( - (name_label.size.x / 2.0), - name_label.size.y - 5.0)

static func _get_visual_offset(txt_path: String) -> Vector2:
	if _offset_cache.has(txt_path):
		_offset_cache_hit_count += 1
		return _offset_cache[txt_path] as Vector2

	var offset_vector: = Vector2.ZERO
	if FileAccess.file_exists(txt_path):
		var file: = FileAccess.open(txt_path, FileAccess.READ)
		if file:
			_offset_file_read_count += 1
			var content: = file.get_as_text().strip_edges()
			var parts: = content.split(",")
			if parts.size() >= 2:
				offset_vector = Vector2(float(parts[0]), float(parts[1]))


	_offset_cache[txt_path] = offset_vector
	return offset_vector

static func get_offset_cache_diagnostics() -> Dictionary:
	return {
		"entries": _offset_cache.size(), 
		"file_reads": _offset_file_read_count, 
		"cache_hits": _offset_cache_hit_count, 
	}


func _on_mouse_entered() -> void :
	var player = GameManager.current_player if GameManager else null
	if is_instance_valid(player) and player.get("is_interacting_with_npc"): return

	if CursorManager:
		CursorManager.register_hover(self, "check")

func _on_mouse_exited() -> void :
	if CursorManager:
		CursorManager.unregister_hover(self)

func _exit_tree() -> void :

	if CursorManager:
		CursorManager.unregister_hover(self)

func _input_event(_viewport: Viewport, event: InputEvent, _shape_idx: int) -> void :
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		var player = GameManager.current_player if GameManager else null

		if is_instance_valid(player) and player.get("is_interacting_with_npc"):
			return

		if CursorManager:
			if CursorManager.cursor_sprite.animation == "skill":

				get_viewport().set_input_as_handled()
				if is_instance_valid(player) and "ready_to_cast_skill_id" in player:
					player.ready_to_cast_skill_id = -1
				CursorManager.cancel_skill_state()
				return


			if CursorManager.current_hovered_entity != self:
				return

		get_viewport().set_input_as_handled()
		# Terrain autorun must not resume and override a selected NPC.
		if is_instance_valid(player) and player.has_method("cancel_autorun_tracking"):
			player.cancel_autorun_tracking(true)

		if NetworkManager:
			if NetworkManager.has_method("send_secure_packet"):
				NetworkManager.send_secure_packet({
					"Type": "InteractRequest", 
					"EntityID": entity_id
				})


func show_chat_bubble(text: String, bubble_type: int = 0, margins: Dictionary = {}, rich_text: bool = false) -> void :
	if not chat_bubble_bg or not chat_bubble_label: return

	chat_bubble_bg.texture = LEGACY_BUBBLE_SKIN.get_texture(bubble_type)

	# Leave ordinary messages on the original Label.

	var fixed_bubble_size = Vector2(64, 48)
	if chat_bubble_bg.texture: fixed_bubble_size = chat_bubble_bg.texture.get_size()

	chat_bubble_bg.size = fixed_bubble_size

	var visual_center_x = name_label.position.x + (name_label.size.x / 2.0)
	chat_bubble_bg.position.x = visual_center_x - (chat_bubble_bg.size.x / 2.0)
	chat_bubble_bg.position.y = name_label.position.y - chat_bubble_bg.size.y - 10.0

	var default_margins = {
		0: {"left": 10.0, "top": 8.0, "right": 10.0, "bottom": 23.0}, 
		1: {"left": 18.0, "top": 25.0, "right": 18.0, "bottom": 25.0}, 
		2: {"left": 30.0, "top": 34.0, "right": 30.0, "bottom": 34.0}, 
		3: {"left": 28.0, "top": 20.0, "right": 15.0, "bottom": 34.0}, 
		4: {"left": 32.0, "top": 17.0, "right": 34.0, "bottom": 45.0}, 
		5: {"left": 30.0, "top": 15.0, "right": 30.0, "bottom": 46.0}, 
		6: {"left": 10.0, "top": 10.0, "right": 10.0, "bottom": 10.0}
	}

	var current_margin = default_margins.get(bubble_type, default_margins[0])

	var m_left = margins.get("left", current_margin.get("left", 8.0))
	var m_right = margins.get("right", current_margin.get("right", 8.0))
	var m_top = margins.get("top", current_margin.get("top", 8.0))
	var m_bottom = margins.get("bottom", current_margin.get("bottom", 15.0))

	chat_bubble_label.size = Vector2(fixed_bubble_size.x - m_left - m_right, fixed_bubble_size.y - m_top - m_bottom)
	chat_bubble_label.position = Vector2(m_left, m_top)
	LEGACY_DIALOG_BUBBLE.display(chat_bubble_bg, chat_bubble_label, text, rich_text)

	if bubble_fade_tween and bubble_fade_tween.is_running(): bubble_fade_tween.kill()
	chat_bubble_bg.modulate.a = 1.0
	chat_bubble_bg.visible = true


	is_bubble_active = true


func hide_chat_bubble() -> void :
	if not is_bubble_active: return


	is_bubble_active = false

	if bubble_fade_tween and bubble_fade_tween.is_running(): bubble_fade_tween.kill()
	bubble_fade_tween = create_tween()


	bubble_fade_tween.tween_property(chat_bubble_bg, "modulate:a", 0.0, 0.3)
	bubble_fade_tween.tween_callback( func(): chat_bubble_bg.visible = false)
