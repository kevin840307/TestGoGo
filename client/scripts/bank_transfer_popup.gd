
extends Control

signal amount_confirmed(amount: int)

@onready var row_container = $PopupPanel / ItemRowContainer
@onready var lbl_direction = $PopupPanel / LblDirection
@onready var amount_spinbox = $PopupPanel / AmountSpinBox
@onready var btn_confirm = $PopupPanel / BtnConfirm
@onready var btn_cancel = $PopupPanel / BtnCancel

const INVENTORY_ROW_SCENE = preload("res://inventory_item_row.tscn")

var _max_amount: int = 1

func _ready():
	if btn_confirm:
		btn_confirm.pressed.connect(_on_btn_confirm_pressed)
	if btn_cancel:
		btn_cancel.pressed.connect(_on_btn_cancel_pressed)

func setup(item_data: Dictionary, tex: Texture2D, mat: Material, title_text: String, max_amt: int, need_input: bool = true):
	_max_amount = max_amt
	lbl_direction.text = title_text

	for child in row_container.get_children():
		child.queue_free()

	var row = INVENTORY_ROW_SCENE.instantiate()
	row_container.add_child(row)
	row.size_flags_horizontal = Control.SIZE_EXPAND_FILL


	var item_index = int(item_data.get("item_index", item_data.get("item_idx", item_data.get("equip_idx", item_data.get("pet_id", 0)))))
	var item_name = item_data.get("name", ItemLoader.get_item_property(item_index, "item_name", "未知"))

	row.setup(tex, mat, "", item_name, Color.WHITE, Color(1.0, 0.941, 0.176, 1.0))

	amount_spinbox.visible = need_input

	if need_input:
		amount_spinbox.min_value = 1
		amount_spinbox.max_value = _max_amount if _max_amount > 0 else 1
		amount_spinbox.value = 1

		amount_spinbox.get_line_edit().grab_focus()
	else:
		amount_spinbox.min_value = 1
		amount_spinbox.max_value = _max_amount if _max_amount > 0 else 1
		amount_spinbox.value = _max_amount

func _on_btn_confirm_pressed():
	var amount = int(amount_spinbox.value)
	amount_confirmed.emit(amount)
	queue_free()

func _on_btn_cancel_pressed():
	queue_free()
