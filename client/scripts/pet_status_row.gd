
extends Control

const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}

@onready var lbl_name = $LblName
@onready var bar_hp = $BarHP
@onready var bar_mp = $BarMP
@onready var bar_exp = $BarExp
@onready var doll_container = $DollContainer
@onready var lbl_hp = $LblHP
@onready var lbl_mp = $LblMP
@onready var lbl_exp = $LblExp

var current_pet_uid: int = -1
var _last_rendered_pet_uid: int = -1
var _last_rendered_evol_id: int = -1

func _ready() -> void :

	gui_input.connect(_on_gui_input)

func update_data(pet_data: Dictionary) -> void :
	var previous_pet_uid: = current_pet_uid
	current_pet_uid = int(pet_data.get("UID", pet_data.get("uid", -1)))
	var pet_changed: = previous_pet_uid != current_pet_uid
	var pet_id = int(pet_data.get("PETID", pet_data.get("pet_id", 0)))
	var evol_id = int(pet_data.get("EvolID", pet_data.get("evol_id", 0)))
	var pet_name = str(pet_data.get("Name", pet_data.get("name", "寵物")))
	var pet_level = int(pet_data.get("Level", pet_data.get("level", 1)))

	if lbl_name: lbl_name.text = pet_name

	var hp = int(pet_data.get("HP", pet_data.get("hp", 0)))
	var max_hp = int(pet_data.get("MaxHP", pet_data.get("max_hp", 1)))
	var mp = int(pet_data.get("MP", pet_data.get("mp", 0)))
	var max_mp = int(pet_data.get("MaxMP", pet_data.get("max_mp", 1)))
	var exp = int(pet_data.get("Exp", pet_data.get("exp", 0)))
	var max_exp = int(pet_data.get("MaxExp", pet_data.get("max_exp", 1)))

	if max_exp <= 0: max_exp = 1
	if max_hp <= 0: max_hp = 1
	if max_mp <= 0: max_mp = 1

	if bar_hp:
		bar_hp.max_value = max_hp
		_set_bar_value(bar_hp, hp, pet_changed)
	if lbl_hp:
		lbl_hp.text = "%d / %d" % [hp, max_hp]

	if bar_mp:
		bar_mp.max_value = max_mp
		_set_bar_value(bar_mp, mp, pet_changed)
	if lbl_mp:
		lbl_mp.text = "%d / %d" % [mp, max_mp]

	if bar_exp:
		bar_exp.max_value = max_exp
		_set_bar_value(bar_exp, exp, pet_changed)
	if lbl_exp:
		lbl_exp.text = "%d / %d" % [exp, max_exp]

	if _last_rendered_pet_uid != current_pet_uid or _last_rendered_evol_id != evol_id:
		_last_rendered_pet_uid = current_pet_uid
		_last_rendered_evol_id = evol_id
		_refresh_doll_container(pet_id, evol_id)

func _set_bar_value(bar: Range, new_value: float, immediate: bool) -> void :
	if immediate and bar.has_method("set_value_immediate"):
		bar.set_value_immediate(new_value)
	else:
		bar.value = new_value

func _refresh_doll_container(pet_id: int, evol_id: int) -> void :
	if not doll_container: return
	for c in doll_container.get_children():
		c.queue_free()

	var evol_cfg = {}
	if PetLoader:
		var cfg = PetLoader.get_pet_config(pet_id)
		if cfg.has("evolutions"):
			evol_cfg = cfg.evolutions.get(str(evol_id), cfg.evolutions.get(evol_id, {}))

	var outlook = str(evol_cfg.get("outlook", ""))
	var frames = MonsterLoader.get_monster_frames(outlook) if outlook != "" and MonsterLoader else null
	var preview: CanvasItem = null
	if frames:
		var sprite = AnimatedSprite2D.new()
		sprite.sprite_frames = frames
		sprite.centered = false
		doll_container.add_child(sprite)
		sprite.frame_changed.connect(_update_preview_sprite_offset.bind(sprite))
		if frames.has_animation("IDLE_1"):
			sprite.play("IDLE_1")
		else:
			sprite.play()
		_update_preview_sprite_offset(sprite)

		var playing_anim = sprite.animation
		if frames.has_animation(playing_anim):
			var min_corner = Vector2(INF, INF)
			var max_corner = Vector2( - INF, - INF)
			for frame_index in frames.get_frame_count(playing_anim):
				var frame_texture = frames.get_frame_texture(playing_anim, frame_index)
				if frame_texture:
					var old_offset = frame_texture.get_meta("old_offset") if frame_texture.has_meta("old_offset") else Vector2.ZERO
					var frame_top_left = - old_offset
					var frame_bottom_right = frame_top_left + frame_texture.get_size()
					min_corner = min_corner.min(frame_top_left)
					max_corner = max_corner.max(frame_bottom_right)
			var preview_bounds = Rect2(min_corner, max_corner - min_corner)
			if preview_bounds.size.x > 0.0 and preview_bounds.size.y > 0.0:
				var preview_scale = minf(1.0, minf(doll_container.size.x / preview_bounds.size.x, doll_container.size.y / preview_bounds.size.y))
				sprite.scale = Vector2.ONE * preview_scale
				sprite.position = doll_container.size / 2.0 - preview_bounds.get_center() * preview_scale
		preview = sprite
	else:
		var tex: Texture2D = ItemLoader.get_icon(375) if ItemLoader else null
		if not tex and ResourceLoader.exists("res://Assets/Items/Photos/Photo0375.png"):
			tex = load("res://Assets/Items/Photos/Photo0375.png")
		if tex:
			var tex_rect = TextureRect.new()
			tex_rect.texture = tex
			tex_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
			tex_rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
			tex_rect.set_anchors_preset(Control.PRESET_FULL_RECT)
			doll_container.add_child(tex_rect)
			preview = tex_rect

	if preview:
		var color_index = int(evol_cfg.get("color", 0))
		if color_index > 0:
			var mat = ShaderMaterial.new()
			mat.shader = PALETTE_SHADER
			mat.set_shader_parameter("effect_type", 0)
			mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
			preview.material = mat

func _update_preview_sprite_offset(sprite: AnimatedSprite2D) -> void :
	if not sprite.sprite_frames or not sprite.sprite_frames.has_animation(sprite.animation): return
	var frame_texture = sprite.sprite_frames.get_frame_texture(sprite.animation, sprite.frame)
	if frame_texture:
		var old_offset = frame_texture.get_meta("old_offset") if frame_texture.has_meta("old_offset") else Vector2.ZERO
		sprite.offset = - old_offset

func _on_gui_input(event: InputEvent) -> void :
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		if current_pet_uid > 0 and GameManager.current_hud and GameManager.current_hud.has_method("show_pet_window"):
			GameManager.current_hud.show_pet_window(current_pet_uid)
