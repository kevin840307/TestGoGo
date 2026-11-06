
extends Control

signal setup_confirmed(count: int, price: int)

@onready var row_container = $Background / ItemRowContainer

@onready var input_count: SpinBox = $Background / InputCount
@onready var input_price: SpinBox = $Background / InputPrice
@onready var btn_confirm = $Background / BtnConfirm
@onready var btn_cancel = $Background / BtnCancel

const INVENTORY_ROW_SCENE = preload("res://inventory_item_row.tscn")
var _max_count: int = 1

func _ready():
	btn_confirm.pressed.connect(_on_confirm)
	btn_cancel.pressed.connect(queue_free)

func setup(item_data: Dictionary, tex: Texture2D, mat: Material, max_count: int, need_count_input: bool):
	_max_count = max_count

	for child in row_container.get_children(): child.queue_free()
	var row = INVENTORY_ROW_SCENE.instantiate()
	row_container.add_child(row)
	row.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	var item_index = int(item_data.get("item_index", item_data.get("item_idx", item_data.get("equip_idx", item_data.get("pet_id", item_data.get("PETID", 0))))))
	var item_name = item_data.get("name", ItemLoader.get_item_property(item_index, "item_name", "未知"))
	row.setup(tex, mat, "", item_name, Color.WHITE, Color.YELLOW)

	input_count.visible = need_count_input


	input_count.min_value = 1
	if need_count_input:

		input_count.max_value = min(max_count, 9999)
		input_count.value = 1
	else:
		input_count.max_value = max_count
		input_count.value = max_count


	input_price.min_value = 1
	input_price.max_value = 9999999999
	input_price.value = 100
	input_price.grab_focus()

func _on_confirm():

	var count = int(input_count.value)
	var price = int(input_price.value)

	if count <= 0 or price <= 0: return
	if count > _max_count: count = _max_count

	setup_confirmed.emit(count, price)
	queue_free()
