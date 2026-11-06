
extends Control
class_name ChatPanel



signal chat_message_sent(channel: int, message: String, bubble_type: int)


@onready var chat_history: RichTextLabel = $ChatHistory
@onready var chat_input: LineEdit = $ChatInput
@onready var chat_background: Control = $ChatBackground
@onready var btn_channel: TextureButton = $BtnChannel
@onready var btn_toggle_emotion: TextureButton = $BtnToggleEmotion
@onready var btn_bubble_type: TextureButton = $BtnBubbleType
@onready var btn_layout_toggle: TextureButton = $BtnLayoutToggle
@onready var btn_filter_private: Button = $BtnFilterPrivate
@onready var btn_filter_general: Button = $BtnFilterGeneral
@onready var btn_filter_world: Button = $BtnFilterWorld
@onready var btn_filter_clan: Button = $BtnFilterClan
@onready var btn_filter_party: Button = $BtnFilterParty
@onready var lbl_system_msg: RichTextLabel = $LblSystemMsg
@onready var chat_section_separator: HSeparator = $ChatSectionSeparator
@onready var filter_vertical_separator: VSeparator = $FilterVerticalSeparator
@onready var filter_horizontal_separator: HSeparator = $FilterHorizontalSeparator


enum Channel{GENERAL, WORLD, CLAN, PARTY, PRIVATE}
const CHANNEL_COLORS = {
	Channel.GENERAL: "#FFFFFF", Channel.WORLD: "#02C237", Channel.CLAN: "#FF9626", 
	Channel.PARTY: "#32BEFF", Channel.PRIVATE: "#FFF582"
}
const CHANNEL_NAMES = {Channel.GENERAL: "附近", Channel.WORLD: "陣營", Channel.CLAN: "戰團", Channel.PARTY: "隊伍", Channel.PRIVATE: "密語"}

var current_channel: Channel = Channel.GENERAL
var current_bubble_type: int = 0
var input_history: Array[String] = []
var history_index: int = -1
var _chat_backlog: Array[Dictionary] = []
const MAX_BACKLOG_SIZE = 100
const HORIZONTAL_SIZE: = Vector2(700.0, 134.0)
const VERTICAL_SIZE: = Vector2(480.0, 676.0)
const HORIZONTAL_CENTER_OFFSET: = 36.0
const BUTTON_NORMAL_MODULATE: = Color(0.9, 0.95, 1.0, 0.92)
const BUTTON_HOVER_MODULATE: = Color(1.08, 1.08, 1.08, 1.0)
const BUTTON_PRESSED_MODULATE: = Color(0.7, 0.88, 1.0, 1.0)
const FILTER_ENABLED_BG_ALPHA: = 0.28
const FILTER_ENABLED_BORDER_ALPHA: = 0.9
const FILTER_DISABLED_TEXT_COLOR: = Color(0.52, 0.59, 0.68, 1.0)
const FILTER_DISABLED_BG_COLOR: = Color(0.035, 0.055, 0.08, 0.72)
const FILTER_DISABLED_BORDER_COLOR: = Color(0.28, 0.34, 0.42, 0.62)

var is_vertical_layout: bool = false

var channel_filters: Dictionary = {
	Channel.GENERAL: true, Channel.WORLD: true, Channel.CLAN: true, 
	Channel.PARTY: true, Channel.PRIVATE: true
}


@onready var bubble_type_textures = {
	0: preload("res://ui/ICON_BUBBLE1.png"), 1: preload("res://ui/ICON_BUBBLE2.png"), 
	2: preload("res://ui/ICON_BUBBLE3.png"), 3: preload("res://ui/ICON_BUBBLE4.png"), 
	4: preload("res://ui/ICON_BUBBLE5.png"), 5: preload("res://ui/ICON_BUBBLE6.png")
}
@onready var channel_textures = {
	Channel.GENERAL: preload("res://ui/ICON_CHANNEL1.png"), Channel.WORLD: preload("res://ui/ICON_CHANNEL2.png"), 
	Channel.CLAN: preload("res://ui/ICON_CHANNEL3.png"), Channel.PARTY: preload("res://ui/ICON_CHANNEL4.png")
}
@onready var layout_textures = {
	false: preload("res://ui/ICON_CHAT_EXPAND.png"), 
	true: preload("res://ui/ICON_CHAT_COLLAPSE.png")
}


func _ready() -> void :
	if chat_input:
		chat_input.text_submitted.connect(_on_chat_input_submitted)
		chat_input.gui_input.connect(_on_chat_input_gui_input)

	if btn_channel:
		btn_channel.pressed.connect(_on_btn_channel_pressed)
		btn_channel.focus_mode = Control.FOCUS_NONE
		_update_channel_ui()

	var filter_buttons = {Channel.GENERAL: btn_filter_general, Channel.WORLD: btn_filter_world, Channel.CLAN: btn_filter_clan, Channel.PARTY: btn_filter_party, Channel.PRIVATE: btn_filter_private}
	for ch in filter_buttons.keys():
		var btn = filter_buttons[ch]
		if btn:
			btn.pressed.connect(_on_filter_button_pressed.bind(ch))
			btn.focus_mode = Control.FOCUS_NONE

	if btn_bubble_type:
		btn_bubble_type.pressed.connect(_on_btn_bubble_type_pressed)
		btn_bubble_type.focus_mode = Control.FOCUS_NONE
		_update_bubble_type_ui()

	if btn_layout_toggle:
		btn_layout_toggle.pressed.connect(_on_btn_layout_toggle_pressed)
		btn_layout_toggle.focus_mode = Control.FOCUS_NONE
		_update_layout_toggle_ui()

	if chat_history:
		chat_history.bbcode_enabled = true
		chat_history.gui_input.connect(_on_chat_history_gui_input)
		var v_scroll = chat_history.get_v_scroll_bar()
		if v_scroll:
			v_scroll.modulate = Color.TRANSPARENT
			v_scroll.mouse_filter = Control.MOUSE_FILTER_IGNORE

	_setup_button_interactions()
	_setup_tooltips()
	_update_filter_buttons_ui()
	_update_channel_ui()
	_apply_horizontal_layout()



func add_chat_message(channel_id: int, sender: String, content: String) -> void :
	_chat_backlog.append({"channel": channel_id, "sender": sender, "content": content})
	_truncate_backlog()
	_rebuild_chat_history_ui()


func add_system_message(content: String) -> void :
	_chat_backlog.append({"channel": -1, "sender": "", "content": content})
	_truncate_backlog()
	_rebuild_chat_history_ui()


func focus_input() -> void :
	if chat_input: chat_input.call_deferred("grab_focus")


func _on_chat_input_submitted(new_text: String) -> void :
	var clean_text = new_text.strip_edges()
	if clean_text == "":
		chat_input.text = ""
		chat_input.call_deferred("grab_focus")
		return

	if input_history.is_empty() or input_history[0] != clean_text:
		input_history.push_front(clean_text)
		if input_history.size() > 5: input_history.pop_back()
	history_index = -1


	chat_message_sent.emit(current_channel, clean_text, current_bubble_type)

	chat_input.text = ""
	chat_input.release_focus()
	chat_input.call_deferred("grab_focus")

func _on_chat_input_gui_input(event: InputEvent) -> void :
	if event is InputEventKey and event.pressed:
		if event.keycode == KEY_UP:
			if input_history.size() > 0:
				history_index = min(history_index + 1, input_history.size() - 1)
				chat_input.text = input_history[history_index]
				chat_input.caret_column = chat_input.text.length()
				chat_input.accept_event()
		elif event.keycode == KEY_DOWN:
			if history_index > 0:
				history_index -= 1
				chat_input.text = input_history[history_index]
				chat_input.caret_column = chat_input.text.length()
				chat_input.accept_event()
			elif history_index == 0:
				history_index = -1
				chat_input.text = ""
				chat_input.accept_event()

func _on_chat_history_gui_input(event: InputEvent) -> void :
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		if chat_input: chat_input.call_deferred("grab_focus")

func _on_btn_channel_pressed() -> void :
	current_channel = (current_channel + 1) % 4 as Channel
	_update_channel_ui()

func _update_channel_ui() -> void :
	if btn_channel and channel_textures.has(current_channel): btn_channel.texture_normal = channel_textures[current_channel]
	if chat_input: chat_input.placeholder_text = "發送訊息至 [" + CHANNEL_NAMES[current_channel] + "] 頻道"

func _on_btn_bubble_type_pressed() -> void :
	current_bubble_type = (current_bubble_type + 1) % 6
	_update_bubble_type_ui()

func _update_bubble_type_ui() -> void :
	if btn_bubble_type and bubble_type_textures.has(current_bubble_type): btn_bubble_type.texture_normal = bubble_type_textures[current_bubble_type]

func _on_filter_button_pressed(channel: Channel) -> void :
	channel_filters[channel] = not channel_filters[channel]
	_update_filter_buttons_ui()
	_rebuild_chat_history_ui()

func _on_btn_layout_toggle_pressed() -> void :
	var was_following: = chat_history.scroll_following if chat_history else true
	var scroll_bar: = chat_history.get_v_scroll_bar() if chat_history else null
	var previous_scroll_value: = scroll_bar.value if scroll_bar else 0.0
	var input_had_focus: = chat_input.has_focus() if chat_input else false

	is_vertical_layout = not is_vertical_layout
	if is_vertical_layout:
		_apply_vertical_layout()
	else:
		_apply_horizontal_layout()
	_update_layout_toggle_ui()

	if chat_history:
		chat_history.scroll_following = was_following
		if not was_following and scroll_bar:
			scroll_bar.set_deferred("value", previous_scroll_value)
	if input_had_focus and chat_input:
		chat_input.call_deferred("grab_focus")

func _update_layout_toggle_ui() -> void :
	if not btn_layout_toggle:
		return
	btn_layout_toggle.texture_normal = layout_textures[is_vertical_layout]
	btn_layout_toggle.tooltip_text = "收合聊天視窗" if is_vertical_layout else "展開聊天視窗"

func _setup_button_interactions() -> void :
	var buttons: Array[BaseButton] = [
		btn_channel, 
		btn_toggle_emotion, 
		btn_bubble_type, 
		btn_layout_toggle, 
		btn_filter_private, 
		btn_filter_general, 
		btn_filter_world, 
		btn_filter_clan, 
		btn_filter_party
	]
	for button in buttons:
		if not button:
			continue
		button.focus_mode = Control.FOCUS_NONE
		button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		button.self_modulate = BUTTON_NORMAL_MODULATE
		button.mouse_entered.connect(_on_chat_button_mouse_entered.bind(button))
		button.mouse_exited.connect(_on_chat_button_mouse_exited.bind(button))
		button.button_down.connect(_on_chat_button_down.bind(button))
		button.button_up.connect(_on_chat_button_up.bind(button))

func _setup_tooltips() -> void :
	btn_channel.tooltip_text = "切換聊天頻道" if btn_channel else ""
	btn_toggle_emotion.tooltip_text = "開啟／關閉表情選單" if btn_toggle_emotion else ""
	btn_bubble_type.tooltip_text = "切換聊天泡泡樣式" if btn_bubble_type else ""
	btn_filter_private.tooltip_text = "顯示／隱藏「密語」" if btn_filter_private else ""
	btn_filter_general.tooltip_text = "顯示／隱藏「附近」頻道" if btn_filter_general else ""
	btn_filter_world.tooltip_text = "顯示／隱藏「陣營」頻道" if btn_filter_world else ""
	btn_filter_clan.tooltip_text = "顯示／隱藏「戰團」頻道" if btn_filter_clan else ""
	btn_filter_party.tooltip_text = "顯示／隱藏「隊伍」頻道" if btn_filter_party else ""

func _on_chat_button_mouse_entered(button: BaseButton) -> void :
	if button and not button.disabled:
		button.self_modulate = BUTTON_HOVER_MODULATE

func _on_chat_button_mouse_exited(button: BaseButton) -> void :
	if button:
		button.self_modulate = BUTTON_NORMAL_MODULATE

func _on_chat_button_down(button: BaseButton) -> void :
	if button and not button.disabled:
		button.self_modulate = BUTTON_PRESSED_MODULATE

func _on_chat_button_up(button: BaseButton) -> void :
	if not button:
		return
	button.self_modulate = BUTTON_HOVER_MODULATE if button.get_global_rect().has_point(get_viewport().get_mouse_position()) else BUTTON_NORMAL_MODULATE

func _apply_horizontal_layout() -> void :
	_set_panel_rect(HORIZONTAL_SIZE)
	_set_control_rect(chat_background, 0.0, 0.0, 700.0, 134.0)
	filter_vertical_separator.visible = true
	filter_horizontal_separator.visible = false
	_set_control_rect(filter_vertical_separator, 76.0, 3.0, 77.0, 130.0)
	_set_control_rect(btn_filter_private, 5.0, 3.0, 70.0, 26.0)
	_set_control_rect(btn_filter_general, 5.0, 29.0, 70.0, 52.0)
	_set_control_rect(btn_filter_world, 5.0, 55.0, 70.0, 78.0)
	_set_control_rect(btn_filter_clan, 5.0, 81.0, 70.0, 104.0)
	_set_control_rect(btn_filter_party, 5.0, 107.0, 70.0, 130.0)
	_set_control_rect(lbl_system_msg, 83.0, 3.0, 642.0, 23.0)
	_set_control_rect(chat_section_separator, 82.0, 25.0, 658.0, 26.0)
	_set_control_rect(btn_layout_toggle, 665.0, 3.0, 694.0, 32.0)
	_set_control_rect(chat_history, 82.0, 29.0, 690.0, 96.0)
	_set_control_rect(btn_channel, 82.0, 101.0, 111.0, 130.0)
	_set_control_rect(chat_input, 117.0, 101.0, 612.0, 130.0)
	_set_control_rect(btn_toggle_emotion, 618.0, 101.0, 648.0, 131.0)
	_set_control_rect(btn_bubble_type, 654.0, 101.0, 684.0, 131.0)

func _apply_vertical_layout() -> void :
	_set_panel_rect(VERTICAL_SIZE)
	_set_control_rect(chat_background, 0.0, 0.0, 480.0, 676.0)
	filter_vertical_separator.visible = false
	filter_horizontal_separator.visible = true
	_set_control_rect(filter_horizontal_separator, 8.0, 29.0, 472.0, 30.0)
	_set_control_rect(btn_filter_general, 8.0, 4.0, 88.0, 27.0)
	_set_control_rect(btn_filter_world, 90.0, 4.0, 170.0, 27.0)
	_set_control_rect(btn_filter_clan, 172.0, 4.0, 252.0, 27.0)
	_set_control_rect(btn_filter_party, 254.0, 4.0, 334.0, 27.0)
	_set_control_rect(btn_filter_private, 336.0, 4.0, 416.0, 27.0)
	_set_control_rect(lbl_system_msg, 8.0, 32.0, 472.0, 55.0)
	_set_control_rect(chat_section_separator, 10.0, 55.0, 470.0, 56.0)
	_set_control_rect(btn_layout_toggle, 443.0, 3.0, 472.0, 32.0)
	_set_control_rect(chat_history, 10.0, 58.0, 470.0, 634.0)
	_set_control_rect(btn_channel, 8.0, 641.0, 37.0, 670.0)
	_set_control_rect(chat_input, 43.0, 641.0, 392.0, 670.0)
	_set_control_rect(btn_toggle_emotion, 398.0, 641.0, 428.0, 671.0)
	_set_control_rect(btn_bubble_type, 434.0, 641.0, 464.0, 671.0)

func _set_panel_rect(panel_size: Vector2) -> void :
	offset_left = HORIZONTAL_CENTER_OFFSET - panel_size.x / 2.0
	offset_right = HORIZONTAL_CENTER_OFFSET + panel_size.x / 2.0
	offset_top = - panel_size.y
	offset_bottom = 0.0

func _set_control_rect(control: Control, left: float, top: float, right: float, bottom: float) -> void :
	if not control:
		return
	control.offset_left = left
	control.offset_top = top
	control.offset_right = right
	control.offset_bottom = bottom

func _update_filter_buttons_ui() -> void :
	var filter_buttons = {Channel.GENERAL: btn_filter_general, Channel.WORLD: btn_filter_world, Channel.CLAN: btn_filter_clan, Channel.PARTY: btn_filter_party, Channel.PRIVATE: btn_filter_private}
	for ch in filter_buttons.keys():
		var btn: Button = filter_buttons[ch]
		if not btn:
			continue
		var is_enabled: bool = channel_filters[ch]
		btn.button_pressed = is_enabled
		btn.text = ("✓ " if is_enabled else "") + CHANNEL_NAMES[ch]
		_apply_filter_button_style(btn, ch, is_enabled)

func _apply_filter_button_style(button: Button, channel: Channel, is_enabled: bool) -> void :
	var channel_color: = Color(CHANNEL_COLORS[channel])
	var normal_style: = _create_filter_button_style(
		Color(channel_color, FILTER_ENABLED_BG_ALPHA) if is_enabled else FILTER_DISABLED_BG_COLOR, 
		Color(channel_color, FILTER_ENABLED_BORDER_ALPHA) if is_enabled else FILTER_DISABLED_BORDER_COLOR
	)
	var hover_style: = _create_filter_button_style(
		Color(channel_color, 0.4) if is_enabled else Color(0.07, 0.1, 0.14, 0.9), 
		Color(channel_color, 1.0) if is_enabled else Color(0.48, 0.56, 0.66, 0.85)
	)
	var pressed_style: = _create_filter_button_style(
		Color(channel_color, 0.52), 
		Color(channel_color, 1.0)
	)

	button.add_theme_color_override("font_color", channel_color if is_enabled else FILTER_DISABLED_TEXT_COLOR)
	button.add_theme_color_override("font_hover_color", channel_color.lightened(0.15) if is_enabled else Color(0.72, 0.79, 0.88, 1.0))
	button.add_theme_color_override("font_pressed_color", channel_color.lightened(0.22))
	button.add_theme_color_override("font_focus_color", channel_color if is_enabled else FILTER_DISABLED_TEXT_COLOR)
	button.add_theme_stylebox_override("normal", normal_style)
	button.add_theme_stylebox_override("hover", hover_style)
	button.add_theme_stylebox_override("pressed", pressed_style)
	button.add_theme_stylebox_override("focus", hover_style)

func _create_filter_button_style(background_color: Color, border_color: Color) -> StyleBoxFlat:
	var style: = StyleBoxFlat.new()
	style.bg_color = background_color
	style.border_color = border_color
	style.set_border_width_all(1)
	style.set_corner_radius_all(5)
	style.content_margin_left = 4.0
	style.content_margin_right = 4.0
	return style

func _rebuild_chat_history_ui() -> void :
	if not chat_history: return
	var was_following = chat_history.scroll_following
	chat_history.clear()
	for msg in _chat_backlog:
		var ch = msg["channel"]
		if ch == -1: chat_history.append_text("[color=#3F9BFF]系統訊息：[/color][color=#FFFFFF]%s[/color]\n" % [msg["content"]])
		elif channel_filters.get(ch, true): chat_history.append_text("[color=%s]%s：%s[/color]\n" % [CHANNEL_COLORS.get(ch, "#FFFFFF"), msg["sender"], msg["content"]])
	chat_history.scroll_following = was_following

func _truncate_backlog() -> void :
	if _chat_backlog.size() > MAX_BACKLOG_SIZE: _chat_backlog.pop_front()
