
extends CanvasLayer

signal ctrl_state_changed(is_pressed: bool)

@onready var cursor_sprite: AnimatedSprite2D = $CursorSprite

var tooltip_label: RichTextLabel = null
var current_hovered_entity: Node = null
var _logical_cursor_state: String = "normal"
var _is_game_window_hovered: bool = false


var hovered_items: Array = []

var _last_ctrl_state: bool = false

func _ready() -> void :
	Input.mouse_mode = Input.MOUSE_MODE_HIDDEN
	cursor_sprite.play("normal")

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
	var visual_state: = "normal" if _is_game_window_hovered else _logical_cursor_state
	if cursor_sprite.animation != visual_state:
		cursor_sprite.play(visual_state)

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
