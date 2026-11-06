
extends Control

signal buy_confirmed(count: int)

@onready var lbl_title = $Background / LabelTitle
@onready var row_container = $Background / ItemRowContainer
@onready var input_count = $Background / InputCount
@onready var lbl_input = $Background / LabelInput
@onready var lbl_total_price = $Background / LblTotalPrice
@onready var btn_confirm = $Background / BtnConfirm
@onready var btn_cancel = $Background / BtnCancel

const INVENTORY_ROW_SCENE = preload("res://inventory_item_row.tscn")
var _max_count: int = 1
var _unit_price: int = 0

func _ready():
	btn_confirm.pressed.connect(_on_confirm)
	btn_cancel.pressed.connect(queue_free)
	if input_count:
		input_count.text_changed.connect(_on_count_changed)

func setup(item_data: Dictionary, tex: Texture2D, mat: Material, max_count: int, price: int, need_count_input: bool):
	_max_count = max_count
	_unit_price = price

	for child in row_container.get_children(): child.queue_free()
	var row = INVENTORY_ROW_SCENE.instantiate()
	row_container.add_child(row)
	row.size_flags_horizontal = Control.SIZE_EXPAND_FILL


	var item_index = int(item_data.get("ItemIndex", item_data.get("item_index", item_data.get("ItemIdx", item_data.get("item_idx", item_data.get("EquipIdx", item_data.get("equip_idx", 0)))))))
	var item_name = str(item_data.get("Name", item_data.get("name", ItemLoader.get_item_property(item_index, "item_name", "未知"))))

	row.setup(tex, mat, "", item_name, Color.WHITE, Color.YELLOW)

	input_count.visible = need_count_input
	lbl_input.visible = need_count_input
	if need_count_input:
		input_count.text = "1"
		input_count.grab_focus()
	else:
		input_count.text = str(max_count)

	_update_total_price()

func _on_count_changed(_new_text: String):
	_update_total_price()

func _update_total_price():
	var count = int(input_count.text)
	if count > _max_count:
		count = _max_count
		input_count.text = str(count)
	if count < 0: count = 0
	lbl_total_price.text = str(count * _unit_price)

func _on_confirm():
	var count = int(input_count.text)
	if count <= 0: return
	buy_confirmed.emit(count)
	queue_free()
