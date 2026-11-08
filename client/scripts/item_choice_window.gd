extends Panel

var _item_uid: int = 0
var _selection_sent: bool = false

@onready var title_label: Label = $TitleLabel
@onready var hint_label: Label = $HintLabel
@onready var options_container: VBoxContainer = $ScrollContainer/Options
@onready var close_button: Button = $CloseButton


func _ready() -> void:
	if close_button and not close_button.pressed.is_connected(_on_close_pressed):
		close_button.pressed.connect(_on_close_pressed)


func setup(item_uid: int, title: String, options: Array) -> void:
	_item_uid = item_uid
	_selection_sent = false

	if title_label:
		title_label.text = title if not title.is_empty() else "選擇獎勵"

	if hint_label:
		hint_label.text = "請選擇一項獎勵。送出前不會消耗道具。"

	for child in options_container.get_children():
		child.queue_free()

	var valid_count := 0
	for raw_option in options:
		if typeof(raw_option) != TYPE_DICTIONARY:
			continue

		var option: Dictionary = raw_option
		var item_index := int(option.get("ItemIndex", 0))
		var item_name := str(option.get("Name", ""))
		if item_index <= 0 or item_name.is_empty():
			continue

		var button := Button.new()
		button.text = item_name
		button.tooltip_text = "Item ID: %d" % item_index
		button.custom_minimum_size = Vector2(0, 42)
		button.focus_mode = Control.FOCUS_NONE
		button.pressed.connect(_on_option_pressed.bind(item_index))
		options_container.add_child(button)
		valid_count += 1

	if valid_count == 0:
		var empty := Label.new()
		empty.text = "目前沒有可用的獎勵選項。"
		empty.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		empty.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		options_container.add_child(empty)


func _on_option_pressed(item_index: int) -> void:
	if _selection_sent or _item_uid <= 0 or item_index <= 0:
		return

	_selection_sent = true
	for child in options_container.get_children():
		if child is BaseButton:
			child.disabled = true

	if NetworkManager and NetworkManager.has_method("send_item_choice_request"):
		NetworkManager.send_item_choice_request(_item_uid, item_index)
	queue_free()


func _on_close_pressed() -> void:
	if not _selection_sent:
		queue_free()
