extends Panel

@onready var list_rows: VBoxContainer = $ListPanel / ListScroll / ListRows
@onready var detail_text: RichTextLabel = $DetailPanel / DetailText

var _announcements: Array[Dictionary] = []
var _row_buttons: Array[Button] = []
var _selected_index: int = -1

func _ready() -> void :
	set_loading()

func set_loading() -> void :
	_clear_rows()
	detail_text.text = "公告讀取中…"

func show_response(data: Dictionary) -> void :
	if not data.get("Success", false):
		_show_status(str(data.get("Message", "公告讀取失敗，請稍後再試。")))
		return
	var items: Variant = data.get("Announcements", [])
	if not items is Array:
		_show_status("公告資料格式錯誤。")
		return
	set_announcements(items)

func set_announcements(items: Array) -> void :
	_clear_rows()
	for item in items:
		if not item is Dictionary:
			continue
		var announcement: Dictionary = item
		_announcements.append(announcement)
		var row: = Button.new()
		row.custom_minimum_size.y = 36.0
		row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.alignment = HORIZONTAL_ALIGNMENT_LEFT
		row.clip_text = true
		row.focus_mode = Control.FOCUS_NONE
		row.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		row.add_theme_font_size_override("font_size", 16)
		row.text = "  %s    %s" % [str(announcement.get("Date", "")), str(announcement.get("Title", ""))]
		row.tooltip_text = str(announcement.get("Title", ""))
		var index: = _announcements.size() - 1
		row.pressed.connect(_select_announcement.bind(index))
		list_rows.add_child(row)
		_row_buttons.append(row)
		_apply_row_style(row, false)
	if _announcements.is_empty():
		_show_status("目前沒有公告。")
	else:
		_select_announcement(0)

func _select_announcement(index: int) -> void :
	if index < 0 or index >= _announcements.size():
		return
	_selected_index = index
	for i in _row_buttons.size():
		_apply_row_style(_row_buttons[i], i == index)
	detail_text.text = str(_announcements[index].get("Content", ""))
	detail_text.scroll_to_line(0)

func _show_status(message: String) -> void :
	_clear_rows()
	detail_text.text = message

func _clear_rows() -> void :
	_announcements.clear()
	_row_buttons.clear()
	_selected_index = -1
	for child in list_rows.get_children():
		list_rows.remove_child(child)
		child.queue_free()

func _apply_row_style(row: Button, selected: bool) -> void :
	var normal: = StyleBoxFlat.new()
	normal.bg_color = Color(0.23624733, 0.3779558, 0.57560074, 0.9019608) if selected else Color(0.15013191, 0.2537205, 0.40338945, 0.9019608)
	normal.set_corner_radius_all(5)
	var hover: = normal.duplicate() as StyleBoxFlat
	hover.bg_color = Color(0.23624733, 0.3779558, 0.57560074, 0.9019608)
	row.add_theme_stylebox_override("normal", normal)
	row.add_theme_stylebox_override("hover", hover)
	row.add_theme_stylebox_override("pressed", hover)
