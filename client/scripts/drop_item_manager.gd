
extends CanvasLayer

const PET_PERMANENT_DISCARD_SE_ID: = 93
const MECHA_PERMANENT_DISCARD_SE_ID: = 106

var is_dragging: bool = false
var drag_item_data: Dictionary = {}
var drag_tab_type: int = 0

@onready var drag_icon: TextureRect = TextureRect.new()

func _ready() -> void :
	layer = 100
	drag_icon.visible = false
	drag_icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(drag_icon)
	set_process(false)

func start_drag(item_data: Dictionary, tab_type: int, tex: Texture2D, mat: Material) -> void :
	var item_index = int(item_data.get("ItemIndex", item_data.get("item_index", item_data.get("ItemIdx", item_data.get("item_idx", item_data.get("EquipIdx", item_data.get("equip_idx", item_data.get("HorseIdx", item_data.get("horse_idx", 0)))))))))


	is_dragging = true
	drag_item_data = item_data
	drag_tab_type = tab_type

	drag_icon.texture = tex
	drag_icon.material = mat
	drag_icon.visible = true
	set_process(true)

	if CursorManager:
		CursorManager.set_cursor_state("normal")

func _process(_delta: float) -> void :
	if is_dragging and drag_icon.visible:

		var mouse_pos = get_viewport().get_mouse_position()
		drag_icon.position = mouse_pos + Vector2(-20, -20)

func _unhandled_input(event: InputEvent) -> void :
	if not is_dragging: return


	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_RIGHT and event.pressed:
		get_viewport().set_input_as_handled()
		_request_drop_item()
		cancel_drag()


	elif (event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed) or (event is InputEventKey and event.keycode == KEY_ESCAPE and event.pressed):
		cancel_drag()

func _request_drop_item() -> void :
	if drag_tab_type == 100 or drag_tab_type == 999:
		return


	if drag_tab_type != 2 and drag_tab_type != 4:
		var item_index = int(drag_item_data.get("ItemIndex", drag_item_data.get("item_index", drag_item_data.get("ItemIdx", drag_item_data.get("item_idx", drag_item_data.get("EquipIdx", drag_item_data.get("equip_idx", drag_item_data.get("HorseIdx", drag_item_data.get("horse_idx", 0)))))))))
		var can_trade = ItemLoader.get_item_property(item_index, "can_trade", 0)
		if can_trade != 1:
			if GameManager.current_hud:
				GameManager.current_hud.show_rejection_message_box("此物品無法丟棄！")
			return

	var drop_pos = get_parent().get_viewport().get_camera_2d().get_global_mouse_position()


	var item_uid = int(drag_item_data.get("UID", drag_item_data.get("uid", drag_item_data.get("ItemUID", drag_item_data.get("item_uid", drag_item_data.get("ItemID", drag_item_data.get("item_id", drag_item_data.get("EquipUID", drag_item_data.get("equip_uid", drag_item_data.get("HorseUID", drag_item_data.get("horse_uid", 0)))))))))))

	var count = 1


	if NetworkManager:
		if NetworkManager.is_connected_to_server:
			NetworkManager.send_secure_packet({
				"Type": "DropItemRequest", 
				"TabType": drag_tab_type, 
				"ItemUID": item_uid, 
				"Count": count, 
				"DropX": int(drop_pos.x), 
				"DropY": int(drop_pos.y)
			})
			if AudioManager:
				if drag_tab_type == 2:
					AudioManager.play_se(PET_PERMANENT_DISCARD_SE_ID)
				elif drag_tab_type == 4:
					AudioManager.play_se(MECHA_PERMANENT_DISCARD_SE_ID)

func cancel_drag() -> void :
	is_dragging = false

	drag_item_data = {}
	drag_icon.visible = false
	drag_icon.texture = null
	set_process(false)
