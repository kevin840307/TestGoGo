
extends CanvasLayer

# Ten transparent 32x32 frames, derived from C_NORMAL's fantasy blue/gold
# pointer. Keep this as a regular imported texture; no per-frame drawing.
const AUTORUN_SHEET_PATH = "res://ui/cursor/C_AUTOWALK.spr.png"
const AUTORUN_FRAME_COUNT: int = 10
const AUTORUN_FRAME_SIZE: Vector2i = Vector2i(32, 32)
const AUTORUN_VISUAL_SCALE: float = 1.35

signal ctrl_state_changed(is_pressed: bool)

@onready var cursor_sprite: AnimatedSprite2D = $CursorSprite

var tooltip_label: RichTextLabel = null
var _autorun_status_label: Label = null
var _original_cursor_scale: Vector2 = Vector2.ONE
var _original_cursor_texture_filter: int = CanvasItem.TEXTURE_FILTER_PARENT_NODE
var current_hovered_entity: Node = null
var _logical_cursor_state: String = "normal"
var _is_game_window_hovered: bool = false
var _autorun_enabled: bool = false
var _native_popup_cursor_depth: int = 0
var _popup_cursor_window: Window = null
var _popup_cursor_layer: CanvasLayer = null
var _popup_cursor_sprite: AnimatedSprite2D = null


var hovered_items: Array = []

var _last_ctrl_state: bool = false

func _ready() -> void :
	Input.mouse_mode = Input.MOUSE_MODE_HIDDEN
	_register_autorun_cursor_animation()
	_original_cursor_scale = cursor_sprite.scale
	_original_cursor_texture_filter = cursor_sprite.texture_filter
	cursor_sprite.play("normal")

	# A 40x40 animated arrow alone is easy to miss in a busy scene. This
	# persistent state label is purposefully separate from the sprite,
	# ignores mouse events, and appears ONLY for ordinary terrain pointers.
	_autorun_status_label = Label.new()
	_autorun_status_label.text = "🔒 自動走路 ON\n   右鍵停止"
	_autorun_status_label.visible = false
	_autorun_status_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_autorun_status_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_autorun_status_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_autorun_status_label.add_theme_font_size_override("font_size", 16)
	_autorun_status_label.add_theme_color_override(
		"font_shadow_color", Color(0, 0, 0, 0.85)
	)
	_autorun_status_label.add_theme_constant_override("shadow_offset_x", 1)
	_autorun_status_label.add_theme_constant_override("shadow_offset_y", 1)
	_autorun_status_label.add_theme_color_override(
		"font_color", Color(1.0, 1.0, 0.87, 1.0)
	)
	var autorun_bg := StyleBoxFlat.new()
	autorun_bg.bg_color = Color(0.035, 0.095, 0.14, 0.97)
	autorun_bg.border_color = Color(1.0, 0.85, 0.26, 1.0)
	autorun_bg.set_border_width_all(2)
	autorun_bg.set_corner_radius_all(6)
	autorun_bg.set_content_margin_all(5.0)
	_autorun_status_label.add_theme_stylebox_override("normal", autorun_bg)
	add_child(_autorun_status_label)

	tooltip_label = RichTextLabel.new()
	tooltip_label.bbcode_enabled = true
	tooltip_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	tooltip_label.autowrap_mode = TextServer.AUTOWRAP_OFF
	tooltip_label.fit_content = true
	tooltip_label.add_theme_font_size_override("normal_font_size", 13)
	tooltip_label.add_theme_color_override("default_color", Color.WHITE)
	tooltip_label.add_theme_constant_override("outline_size", 2)
	tooltip_label.visible = false
	add_child(tooltip_label)

	process_mode = Node.PROCESS_MODE_ALWAYS

func _process(_delta: float) -> void :
	var m_pos = cursor_sprite.get_global_mouse_position()
	cursor_sprite.global_position = m_pos
	if (
		is_instance_valid(_popup_cursor_window)
		and is_instance_valid(_popup_cursor_sprite)
		and _popup_cursor_window.visible
	):
		# PopupMenu is its own Window/Viewport. Draw the same in-game pointer
		# inside that viewport so it stays above the dropdown instead of being
		# covered by the popup surface.
		_popup_cursor_sprite.position = _popup_cursor_window.get_mouse_position()
	if is_instance_valid(_autorun_status_label) and _autorun_status_label.visible:
		# A large pointer remains distinct even when idle. Keep the badge
		# inside the viewport when the mouse approaches the bottom/right.
		var viewport_size: Vector2 = get_viewport().get_visible_rect().size
		var badge_size: Vector2 = _autorun_status_label.get_combined_minimum_size()
		var badge_pos: Vector2 = m_pos + Vector2(44.0, 24.0)
		badge_pos.x = clampf(badge_pos.x, 0.0, maxf(0.0, viewport_size.x - badge_size.x - 8.0))
		badge_pos.y = clampf(badge_pos.y, 0.0, maxf(0.0, viewport_size.y - badge_size.y - 8.0))
		_autorun_status_label.global_position = badge_pos

	if current_hovered_entity != null and not is_instance_valid(current_hovered_entity):
		_refresh_cursor()

	if tooltip_label and tooltip_label.visible:
		tooltip_label.global_position = m_pos + Vector2(25, 20)


	var current_ctrl = Input.is_key_pressed(KEY_CTRL)
	if current_ctrl != _last_ctrl_state:
		_last_ctrl_state = current_ctrl
		ctrl_state_changed.emit(current_ctrl)
		_refresh_cursor()


		if _logical_cursor_state == "skill" and is_instance_valid(current_hovered_entity):
			if current_hovered_entity.has_method("_on_mouse_entered"):
				current_hovered_entity._on_mouse_entered()
		elif _logical_cursor_state == "skill" and not is_instance_valid(current_hovered_entity):
			hide_skill_target_tooltip()

func register_hover(entity: Node, state_name: String) -> void :
	if not is_instance_valid(entity):
		return
	unregister_hover(entity)
	hovered_items.append({"node": entity, "state": state_name})
	_refresh_cursor()

func unregister_hover(entity: Node) -> void :
	for i in range(hovered_items.size() - 1, -1, -1):
		if hovered_items[i].get("node") == entity:
			hovered_items.remove_at(i)
	_refresh_cursor()

func refresh_hover_state() -> void :
	_refresh_cursor()

func _refresh_cursor() -> void :

	for i in range(hovered_items.size() - 1, -1, -1):
		var hovered_node = hovered_items[i].get("node")
		if not is_instance_valid(hovered_node) or not hovered_node.is_inside_tree():
			hovered_items.remove_at(i)

	var final_state = "normal"
	var next_hovered_entity: Node = null
	var priorities: Array[String] = ["attack", "check", "player", "pick"]



	if _logical_cursor_state == "skill":
		priorities = ["attack", "player", "skill_target"]

	for state_name in priorities:
		for hovered_data in hovered_items:
			if str(hovered_data.get("state", "")) != state_name:
				continue

			var hovered_node: Node = hovered_data.get("node")
			if state_name == "attack" and hovered_node.get("require_ctrl_to_attack") and not Input.is_key_pressed(KEY_CTRL):
				continue

			final_state = state_name
			next_hovered_entity = hovered_node
			break

		if next_hovered_entity != null:
			break


	current_hovered_entity = next_hovered_entity


	if _logical_cursor_state != "skill":
		set_cursor_state(final_state)

func set_cursor_state(state_name: String) -> void :
	_logical_cursor_state = state_name if cursor_sprite.sprite_frames.has_animation(state_name) else "normal"

	if state_name != "skill":
		hide_skill_target_tooltip()

	_apply_cursor_visual()

# Player controls navigation state; this module owns cursor presentation.
# Existing skill, NPC, attack, pickup and UI cursor priorities win.
func set_autorun_indicator(enabled: bool) -> void:
	if _autorun_enabled == enabled:
		return
	_autorun_enabled = enabled
	_apply_cursor_visual()


func is_autorun_indicator_visible() -> bool:
	return _autorun_enabled and cursor_sprite.animation == "autorun" and not _is_game_window_hovered and not is_native_popup_cursor_active()


func _register_autorun_cursor_animation() -> void:
	# Original cursor sprites are loaded from cursor_manager.tscn. Add the
	# generated animation at runtime so those original imported textures,
	# frame durations and hit points remain exactly as before.
	if not ResourceLoader.exists(AUTORUN_SHEET_PATH):
		push_warning("Auto-walk cursor PNG is missing: " + AUTORUN_SHEET_PATH)
		return
	var atlas: Texture2D = load(AUTORUN_SHEET_PATH) as Texture2D
	if atlas == null or atlas.get_width() != AUTORUN_FRAME_COUNT * AUTORUN_FRAME_SIZE.x or atlas.get_height() != AUTORUN_FRAME_SIZE.y:
		push_warning("Auto-walk cursor sprite sheet must be 320x32 pixels.")
		return

	var frames: SpriteFrames = cursor_sprite.sprite_frames.duplicate(true) as SpriteFrames
	if not frames.has_animation("autorun"):
		frames.add_animation("autorun")
	frames.set_animation_speed("autorun", 8.0)
	frames.set_animation_loop("autorun", true)
	for frame_index in range(AUTORUN_FRAME_COUNT):
		var frame_texture := AtlasTexture.new()
		frame_texture.atlas = atlas
		frame_texture.region = Rect2(
			Vector2(frame_index * AUTORUN_FRAME_SIZE.x, 0),
			Vector2(AUTORUN_FRAME_SIZE)
		)
		frames.add_frame("autorun", frame_texture)
	cursor_sprite.sprite_frames = frames


func show_popup_game_cursor(popup_window: Window) -> void:
	if not is_instance_valid(popup_window):
		return

	if is_instance_valid(_popup_cursor_window) and _popup_cursor_window != popup_window:
		hide_popup_game_cursor(_popup_cursor_window)

	_native_popup_cursor_depth = 1
	_popup_cursor_window = popup_window
	Input.mouse_mode = Input.MOUSE_MODE_HIDDEN

	if is_instance_valid(cursor_sprite):
		cursor_sprite.visible = false
	if is_instance_valid(tooltip_label):
		tooltip_label.visible = false
	if is_instance_valid(_autorun_status_label):
		_autorun_status_label.visible = false

	if is_instance_valid(_popup_cursor_layer):
		_popup_cursor_layer.queue_free()

	_popup_cursor_layer = CanvasLayer.new()
	_popup_cursor_layer.name = "GameCursorOverlay"
	_popup_cursor_layer.layer = 4096
	popup_window.add_child(_popup_cursor_layer)

	_popup_cursor_sprite = AnimatedSprite2D.new()
	_popup_cursor_sprite.name = "GameCursorSprite"
	_popup_cursor_sprite.centered = false
	_popup_cursor_sprite.sprite_frames = cursor_sprite.sprite_frames
	_popup_cursor_sprite.scale = _original_cursor_scale
	_popup_cursor_sprite.texture_filter = _original_cursor_texture_filter
	_popup_cursor_layer.add_child(_popup_cursor_sprite)

	# System dropdowns are UI interactions, so always use the ordinary
	# C_NORMAL-style pointer instead of combat/skill/autorun states.
	var popup_animation := "normal"
	if not _popup_cursor_sprite.sprite_frames.has_animation(popup_animation):
		popup_animation = str(cursor_sprite.animation)
	_popup_cursor_sprite.play(popup_animation)
	_popup_cursor_sprite.position = popup_window.get_mouse_position()


func hide_popup_game_cursor(popup_window: Window = null) -> void:
	if (
		is_instance_valid(popup_window)
		and is_instance_valid(_popup_cursor_window)
		and popup_window != _popup_cursor_window
	):
		return

	_native_popup_cursor_depth = 0
	if is_instance_valid(_popup_cursor_layer):
		_popup_cursor_layer.queue_free()
	_popup_cursor_layer = null
	_popup_cursor_sprite = null
	_popup_cursor_window = null

	Input.mouse_mode = Input.MOUSE_MODE_HIDDEN
	if is_instance_valid(cursor_sprite):
		cursor_sprite.visible = true
	_apply_cursor_visual()


# Backward-compatible wrappers for callers from older scene/script revisions.
func push_native_popup_cursor() -> void:
	_native_popup_cursor_depth += 1

func pop_native_popup_cursor() -> void:
	_native_popup_cursor_depth = maxi(0, _native_popup_cursor_depth - 1)
	if _native_popup_cursor_depth == 0 and not is_instance_valid(_popup_cursor_window):
		Input.mouse_mode = Input.MOUSE_MODE_HIDDEN
		if is_instance_valid(cursor_sprite):
			cursor_sprite.visible = true
		_apply_cursor_visual()

func is_native_popup_cursor_active() -> bool:
	return _native_popup_cursor_depth > 0 or is_instance_valid(_popup_cursor_window)


func set_game_window_hovered(is_hovered: bool) -> void :
	if _is_game_window_hovered == is_hovered:
		return

	_is_game_window_hovered = is_hovered
	if _is_game_window_hovered:
		hide_skill_target_tooltip()
		_apply_cursor_visual()
		return

	_refresh_cursor()
	_apply_cursor_visual()
	if _logical_cursor_state == "skill" and is_instance_valid(current_hovered_entity):
		if current_hovered_entity.has_method("_on_mouse_entered"):
			current_hovered_entity._on_mouse_entered()

func _apply_cursor_visual() -> void :
	var visual_state: String = "normal" if _is_game_window_hovered else _logical_cursor_state
	# Keep all old skill/monster/NPC/player/item pointers ahead of autorun.
	# Only replace the ordinary terrain pointer when walking is latched.
	if _autorun_enabled and visual_state == "normal" and not _is_game_window_hovered:
		if cursor_sprite.sprite_frames.has_animation("autorun"):
			visual_state = "autorun"
	if cursor_sprite.animation != visual_state:
		cursor_sprite.play(visual_state)
	# Scale only locked cursor, preserving all legacy cursor dimensions.
	# Hotspot stays at this uncentered sprite's upper-left corner.
	if visual_state == "autorun":
		cursor_sprite.scale = _original_cursor_scale * AUTORUN_VISUAL_SCALE
		cursor_sprite.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	else:
		cursor_sprite.scale = _original_cursor_scale
		cursor_sprite.texture_filter = _original_cursor_texture_filter
	if is_instance_valid(_autorun_status_label):
		_autorun_status_label.visible = (
			visual_state == "autorun" and not is_native_popup_cursor_active()
		)

func show_skill_target_tooltip(target_name: String, skill_name: String) -> void :
	if not tooltip_label: return
	tooltip_label.text = "向[color=yellow]%s[/color]使用[color=yellow]%s[/color]？" % [target_name, skill_name]
	tooltip_label.visible = true

func hide_skill_target_tooltip() -> void :
	if tooltip_label:
		tooltip_label.visible = false

func cancel_skill_state() -> void :
	hide_skill_target_tooltip()
	set_cursor_state("normal")
	_refresh_cursor()
