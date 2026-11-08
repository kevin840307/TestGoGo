
extends TextureRect

signal shortcut_triggered(slot_index: int, type: String, target_id: int)
signal slot_mouse_entered(type: String, target_id: int, tex: Texture2D, mat: Material)
signal slot_mouse_exited()

@onready var icon_rect = $IconRect
@onready var lbl_key = $LblKey
@onready var lbl_amount = $LblAmount

var slot_index: int = 0
var shortcut_type: String = ""
var target_id: int = 0

func _ready() -> void :
	if not mouse_entered.is_connected(_on_mouse_entered):
		mouse_entered.connect(_on_mouse_entered)
	if not mouse_exited.is_connected(_on_mouse_exited):
		mouse_exited.connect(_on_mouse_exited)

func setup(index: int, key_name: String) -> void :
	slot_index = index
	if lbl_key:
		lbl_key.text = key_name
	clear_slot(false)

func update_amount(amount: int) -> void :
	if shortcut_type == "ITEM" and lbl_amount:
		lbl_amount.text = str(amount)
		if amount <= 0:
			lbl_amount.add_theme_color_override("font_color", Color(1.0, 0.3, 0.3, 1.0))
		else:
			lbl_amount.add_theme_color_override("font_color", Color(1.0, 0.941, 0.176, 1.0))

func clear_slot(from_user: bool = false) -> void :
	shortcut_type = ""
	target_id = 0
	if icon_rect:
		icon_rect.texture = null
		icon_rect.material = null
	if lbl_amount:
		lbl_amount.text = ""

	if from_user and NetworkManager:
		# Keep data_model.shortcuts authoritative until the server ACK arrives.
		# The visual slot may change optimistically, but a rejected request can
		# then be restored from the last acknowledged model.
		NetworkManager.send_update_shortcut_request(slot_index, 0, 0)

func set_slot(type: String, id: int, tex: Texture2D, mat: Material = null, amount: int = -1, from_user: bool = false) -> void :
	shortcut_type = type
	target_id = id
	if icon_rect:
		icon_rect.texture = tex
		icon_rect.material = mat

	if lbl_amount:
		lbl_amount.text = str(amount) if amount > 0 else ""


	if from_user and NetworkManager:
		var type_int = 0
		if type == "ITEM": type_int = 1
		elif type == "SKILL": type_int = 2
		elif type == "PET": type_int = 3
		elif type == "MECHA": type_int = 4
		# Do not update data_model.shortcuts before UpdateShortcutResponse.
		NetworkManager.send_update_shortcut_request(slot_index, type_int, id)


func trigger_slot() -> void :
	if shortcut_type != "" and target_id > 0:
		shortcut_triggered.emit(slot_index, shortcut_type, target_id)

func _gui_input(event: InputEvent) -> void :
	if event is InputEventMouseButton and event.pressed:


		if event.button_index == MOUSE_BUTTON_LEFT:
			if DropItemManager and DropItemManager.is_dragging:
				DropItemManager.cancel_drag()
				accept_event()
				return
			trigger_slot()
			accept_event()


		elif event.button_index == MOUSE_BUTTON_RIGHT:
			if DropItemManager and DropItemManager.is_dragging:
				var drag_data = DropItemManager.drag_item_data
				var tab_type = DropItemManager.drag_tab_type
				var tex = DropItemManager.drag_icon.texture
				var mat = DropItemManager.drag_icon.material

				if tab_type == 100:
					set_slot("SKILL", drag_data.get("skill_id", 0), tex, mat, -1, true)
				elif tab_type == 0:
					var item_idx = drag_data.get("item_index", drag_data.get("item_idx", 0))
					var photo_tex = tex
					if ItemLoader:
						var icon_id = ItemLoader.get_item_property(item_idx, "icon_id", 1) - 1
						var p_tex = ItemLoader.get_photo(icon_id)
						if p_tex: photo_tex = p_tex
					set_slot("ITEM", item_idx, photo_tex, mat, -1, true)
				elif tab_type == 2:
					var pet_uid = int(drag_data.get("uid", drag_data.get("UID", 0)))
					if pet_uid > 0:
						set_slot("PET", pet_uid, tex, mat, -1, true)
				elif tab_type == 4:
					var horse_uid = int(drag_data.get("horse_uid", drag_data.get("HorseUID", -1)))
					if horse_uid != -1:
						set_slot("MECHA", horse_uid, tex, mat, -1, true)
				elif tab_type == 999:
					set_slot(drag_data.get("shortcut_type", ""), drag_data.get("target_id", 0), tex, mat, -1, true)

				if GameManager and GameManager.current_hud and GameManager.current_hud.has_method("update_shortcut_amounts"):
					GameManager.current_hud.update_shortcut_amounts()

				DropItemManager.cancel_drag()
				accept_event()

			elif shortcut_type != "" and target_id > 0:
				if DropItemManager:
					var drag_data = {
						"is_shortcut": true, 
						"shortcut_type": shortcut_type, 
						"target_id": target_id
					}
					DropItemManager.start_drag(drag_data, 999, icon_rect.texture, icon_rect.material)
					clear_slot(true)
					accept_event()

func _on_mouse_entered() -> void :
	if shortcut_type != "" and target_id > 0:
		var tex = icon_rect.texture if icon_rect else null
		var mat = icon_rect.material if icon_rect else null
		slot_mouse_entered.emit(shortcut_type, target_id, tex, mat)

func _on_mouse_exited() -> void :
	slot_mouse_exited.emit()
