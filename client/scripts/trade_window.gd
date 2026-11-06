
extends Control

@onready var lbl_my_name = $LabelMyName
@onready var lbl_other_name = $LabelOtherName
@onready var btn_lock = $BtnLock
@onready var btn_cancel = $BtnCancel
@onready var list_other_items = $PanelLeft / ScrollContainer_Other / ListOtherItems
@onready var list_my_items = $PanelLeft / ScrollContainer_MyTrade / ListMyItems
@onready var lbl_other_money = $PanelLeft / LblOtherMoney
@onready var lbl_my_money = $PanelLeft / LblMyMoney
@onready var btn_edit_money = $BtnEditMoney
@onready var bg_ready_me = $PanelLeft / BgReadyMe
@onready var bg_ready_other = $PanelLeft / BgReadyOther


@onready var shared_inventory: SharedInventoryPanel = $PanelRight

const INVENTORY_ROW_SCENE = preload("res://inventory_item_row.tscn")
const TRANSFER_POPUP_SCENE = preload("res://bank_transfer_popup.tscn")
const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}
var _palette_mat_cache: Dictionary = {}

var target_player: CharacterBody2D = null
var _other_offered_items: Array = []
var _my_offered_items: Array = []
var _my_offered_money: int = 0
var _is_locked: bool = false

func _ready() -> void :
	if btn_lock: btn_lock.pressed.connect(_on_btn_lock_pressed)
	if btn_cancel: btn_cancel.pressed.connect(_on_btn_cancel_pressed)
	if btn_edit_money: btn_edit_money.pressed.connect(_on_btn_edit_money_pressed)


	if bg_ready_me: bg_ready_me.visible = false
	if bg_ready_other: bg_ready_other.visible = false


	if shared_inventory:
		shared_inventory.setup_mode(SharedInventoryPanel.DisplayMode.SIMPLE_LIST, [
			SharedInventoryPanel.TabType.GENERAL, 
			SharedInventoryPanel.TabType.EQUIP, 
			SharedInventoryPanel.TabType.PET, 
			SharedInventoryPanel.TabType.MECHA
		])
		shared_inventory.item_clicked.connect(_on_my_inventory_clicked)
		shared_inventory.tab_changed.connect(_on_inventory_tab_changed)
		shared_inventory.closed.connect(_on_btn_cancel_pressed)

	if NetworkManager:
		NetworkManager.trade_sync_received.connect(_on_trade_sync_received)
		NetworkManager.trade_final_confirm_req_received.connect(_on_trade_final_confirm_req)
		NetworkManager.trade_cancel_received.connect(_on_trade_cancelled)
		NetworkManager.trade_success_received.connect(_on_trade_success)

func setup(player: CharacterBody2D, other_name: String) -> void :
	target_player = player
	if lbl_my_name: lbl_my_name.text = player.data_model.character_name if player.data_model else "我"
	if lbl_other_name: lbl_other_name.text = other_name
	_is_locked = false
	_my_offered_money = 0

	if bg_ready_me: bg_ready_me.visible = false
	if bg_ready_other: bg_ready_other.visible = false


	if shared_inventory: shared_inventory._switch_tab(SharedInventoryPanel.TabType.GENERAL)


func _on_inventory_tab_changed(_new_tab: int) -> void :
	_update_inventory_ui()

func _update_inventory_ui() -> void :
	if not is_instance_valid(target_player) or not target_player.data_model: return
	if not shared_inventory: return

	var p_data = target_player.data_model
	var items_to_show = []
	var max_size = 0
	var used_capacity = 0

	var current_tab = shared_inventory.current_tab

	match current_tab:
		SharedInventoryPanel.TabType.GENERAL:
			max_size = p_data.inventory_max_size
			for item in p_data.inventory:
				if int(item.get("in_bank", 0)) == 0:
					used_capacity += 1
					var uid = int(item.get("ItemUID", item.get("item_uid", 0)))
					var count = int(item.get("Count", item.get("count", 1)))
					var remain_count = count - _get_my_offered_count(current_tab, uid)

					if remain_count > 0:
						var copy_item = item.duplicate()
						copy_item["Count"] = remain_count
						copy_item["count"] = remain_count
						items_to_show.append(copy_item)

			shared_inventory.render_general_items(items_to_show)

		SharedInventoryPanel.TabType.EQUIP:
			max_size = p_data.equip_max_size
			for eq in p_data.equip_list:
				if int(eq.get("in_bank", eq.get("InBank", 0))) == 0 and int(eq.get("part", eq.get("Part", 255))) == 255:
					used_capacity += 1
					var uid = int(eq.get("ItemID", eq.get("item_id", eq.get("EquipUID", 0))))

					if _get_my_offered_count(current_tab, uid) == 0:
						items_to_show.append(eq)

			shared_inventory.render_equip_items(items_to_show)

		SharedInventoryPanel.TabType.PET:
			max_size = p_data.pet_max_size
			for pt in p_data.pet_list:
				used_capacity += 1
				var uid = int(pt.get("UID", pt.get("uid", 0)))
				if _get_my_offered_count(current_tab, uid) == 0:
					items_to_show.append(pt)

			if shared_inventory.has_method("render_pet_items"):
				shared_inventory.render_pet_items(items_to_show)
			else:
				shared_inventory.render_general_items(items_to_show)

		SharedInventoryPanel.TabType.MECHA:
			max_size = p_data.horse_equip_max_size
			for hq in p_data.horse_equip_list:
				if int(hq.get("in_bank", 0)) == 0 and int(hq.get("horse_uid", -1)) == -1:
					used_capacity += 1
					var uid = int(hq.get("EquipUID", hq.get("equip_uid", 0)))
					if _get_my_offered_count(current_tab, uid) == 0:
						items_to_show.append(hq)

			shared_inventory.render_mecha_equips(items_to_show)

	shared_inventory.update_capacity(current_tab, used_capacity, max_size)


func _on_my_inventory_clicked(event: InputEvent, item_data: Dictionary, tab: int, tex: Texture2D, mat: Material) -> void :
	if _is_locked: return
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		var uid = int(item_data.get("ItemUID", item_data.get("item_uid", item_data.get("ItemID", item_data.get("item_id", item_data.get("EquipUID", item_data.get("equip_uid", item_data.get("UID", item_data.get("uid", 0)))))))))
		var idx = int(item_data.get("ItemIndex", item_data.get("item_index", item_data.get("ItemIdx", item_data.get("item_idx", item_data.get("EquipIdx", item_data.get("equip_idx", item_data.get("PetID", item_data.get("pet_id", 0)))))))))

		if tab == SharedInventoryPanel.TabType.GENERAL:
			var max_can_offer = int(item_data.get("Count", item_data.get("count", 1)))
			if max_can_offer <= 0: return

			var popup = TRANSFER_POPUP_SCENE.instantiate()
			add_child(popup)

			popup.setup(item_data, tex, mat, "放入交易數量", max_can_offer, true)
			popup.amount_confirmed.connect(_on_trade_item_amount_confirmed.bind(tab, uid, idx))
		else:
			NetworkManager.send_trade_action("TradeAddItem", {"TabType": tab, "UID": uid, "ItemIndex": idx, "Count": 1})

func _on_trade_item_amount_confirmed(amount: int, tab: int, uid: int, idx: int) -> void :
	NetworkManager.send_trade_action("TradeAddItem", {"TabType": tab, "UID": uid, "ItemIndex": idx, "Count": amount})

func _on_my_trade_item_clicked(event: InputEvent, item_data: Dictionary) -> void :
	if _is_locked: return
	if event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_LEFT or event.button_index == MOUSE_BUTTON_RIGHT:
			var uid = int(item_data.get("UID", 0))
			var tab = int(item_data.get("TabType", 0))
			NetworkManager.send_trade_action("TradeRemoveItem", {"TabType": tab, "UID": uid})


func _on_btn_edit_money_pressed() -> void :
	if _is_locked: return
	if not GameManager.current_hud or not GameManager.current_hud.has_method("show_spin_box"): return

	var available_money: = 0
	if is_instance_valid(target_player) and target_player.data_model:
		available_money = maxi(int(target_player.data_model.money), 0)
	var max_trade_money: = mini(available_money, 9999999999)
	var initial_money: = clampi(_my_offered_money, 0, max_trade_money)

	GameManager.current_hud.show_spin_box(
		"交易金額", 
		func(amount: int):
			if _is_locked: return
			if is_instance_valid(target_player) and target_player.data_model:
				if amount > target_player.data_model.money:
					amount = target_player.data_model.money
			NetworkManager.send_trade_action("TradeSetMoney", {"Money": amount}), 
		Callable(), 
		0, 
		max_trade_money, 
		initial_money
	)


func _on_trade_sync_received(my_money: int, other_money: int, my_items: Array, other_items: Array, my_locked: bool, other_locked: bool) -> void :
	_other_offered_items = other_items
	_my_offered_items = my_items
	_my_offered_money = my_money
	_is_locked = my_locked

	if lbl_my_money: lbl_my_money.text = str(my_money)
	if lbl_other_money: lbl_other_money.text = str(other_money)


	if bg_ready_me: bg_ready_me.visible = my_locked
	if bg_ready_other: bg_ready_other.visible = other_locked


	if my_locked:
		if btn_edit_money: btn_edit_money.disabled = true
		if btn_lock:
			btn_lock.disabled = true
			btn_lock.modulate = Color(0.5, 0.5, 0.5)
	else:
		if btn_edit_money: btn_edit_money.disabled = false
		if btn_lock:
			btn_lock.disabled = false
			btn_lock.modulate = Color.WHITE

	_render_trade_list(list_my_items, my_items, true)
	_render_trade_list(list_other_items, other_items, false)


	_update_inventory_ui()

func _render_trade_list(container: Control, items: Array, is_mine: bool) -> void :
	for child in container.get_children(): child.queue_free()
	for item in items:
		var row = INVENTORY_ROW_SCENE.instantiate()
		container.add_child(row)

		var idx = int(item.get("ItemIndex", 0))
		var count = int(item.get("Count", 1))
		var tab = int(item.get("TabType", 0))

		var i_name = ItemLoader.get_item_property(idx, "item_name", "未知")
		var icon_id = int(ItemLoader.get_item_property(idx, "icon_id", 1)) - 1
		var tex = ItemLoader.get_icon(icon_id)
		var color_idx = int(item.get("Color", item.get("color", ItemLoader.get_item_property(idx, "color_index", 0))))
		var effect_type = int(item.get("EffectType", item.get("effect_type", ItemLoader.get_item_property(idx, "effect_type", 0))))

		var top_txt = ""
		var tooltip_data = item

		if tab == SharedInventoryPanel.TabType.GENERAL:

			color_idx = int(ItemLoader.get_item_property(idx, "color_index", 0))
			tooltip_data = item.duplicate()
			tooltip_data.erase("Color")
			tooltip_data.erase("color")
			top_txt = "x " + str(count)
		elif tab == SharedInventoryPanel.TabType.EQUIP or tab == SharedInventoryPanel.TabType.MECHA:
			var req_lvl = ItemLoader.get_item_property(idx, "req_level", 0)
			top_txt = "Lv." + str(req_lvl)
			var upgrade = int(item.get("Upgrade", item.get("upgrade", 0)))
			if upgrade > 0: top_txt = "+%d " % upgrade + top_txt
		elif tab == SharedInventoryPanel.TabType.PET:

			var pet_data = item.get("ExtraData", item.get("extra_data", {}))
			if typeof(pet_data) == TYPE_DICTIONARY and not pet_data.is_empty():
				i_name = str(pet_data.get("Name", pet_data.get("name", "未知寵物")))
				var pet_lvl = int(pet_data.get("Level", pet_data.get("level", 1)))
				top_txt = "Lv." + str(pet_lvl)


				if ItemLoader:
					tex = ItemLoader.get_icon(375)

				var evol_id = int(pet_data.get("EvolID", pet_data.get("evol_id", 0)))
				if PetLoader:
					var config = PetLoader.get_pet_config(idx)
					if config and config.has("evolutions"):
						var evol_key = str(evol_id)
						if config.evolutions.has(evol_key):
							var evol_info = config.evolutions[evol_key]
							color_idx = int(evol_info.get("color", 0))

							var outlook = str(evol_info.get("outlook", ""))
							if outlook != "":
								var photo_path = "res://Assets/Pets/Photo/" + outlook + ".png"
								if ResourceLoader.exists(photo_path):
									tex = load(photo_path)

				tooltip_data = pet_data
			else:
				var pet_lvl = int(item.get("Level", item.get("level", 1)))
				top_txt = "Lv." + str(pet_lvl)

		var mat = _get_palette_mat(color_idx, effect_type)

		row.setup(tex, mat, top_txt, i_name, Color.WHITE, Color.YELLOW)

		row.mouse_filter = Control.MOUSE_FILTER_PASS
		row.mouse_entered.connect(_show_item_tooltip.bind(idx, tab, tooltip_data))
		row.mouse_exited.connect(_hide_item_tooltip)

		if is_mine:
			row.gui_input.connect(_on_my_trade_item_clicked.bind(item))

func _get_my_offered_count(tab: int, uid: int) -> int:
	var total = 0
	for item in _my_offered_items:
		if int(item.get("TabType", 0)) == int(tab) and int(item.get("UID", 0)) == uid:
			total += int(item.get("Count", 1))
	return total


func _on_btn_lock_pressed() -> void :
	if _is_locked: return

	if not _check_capacity_and_limits():
		if GameManager.current_hud:
			GameManager.current_hud.show_rejection_message_box("背包剩餘空間不足或物品數量超過上限，無法鎖定交易！")
		return

	NetworkManager.send_trade_action("TradeLock")

func _check_capacity_and_limits() -> bool:
	var p_data = target_player.data_model
	var needed_general = 0
	var needed_equip = 0
	var needed_pet = 0
	var needed_mecha = 0

	var used_general = _count_in_bag(p_data.inventory)
	var used_equip = _count_in_bag_equip(p_data.equip_list)
	var used_pet = p_data.pet_list.size()
	var used_mecha = _count_in_bag_mecha(p_data.horse_equip_list)

	for item in _other_offered_items:
		var tab = int(item.get("TabType", item.get("tab_type", 0)))
		var count = int(item.get("Count", item.get("count", 1)))
		var idx = int(item.get("ItemIndex", item.get("item_index", 0)))

		if tab == 0:
			var current_count = _get_item_count_in_bag(p_data.inventory, idx)
			if current_count + count > 999:
				return false
			if current_count == 0: needed_general += 1
		elif tab == 1: needed_equip += 1
		elif tab == 2: needed_pet += 1
		elif tab == 3: needed_mecha += 1

	if used_general + needed_general > p_data.inventory_max_size: return false
	if used_equip + needed_equip > p_data.equip_max_size: return false
	if used_pet + needed_pet > p_data.pet_max_size: return false
	if used_mecha + needed_mecha > p_data.horse_equip_max_size: return false

	return true

func _count_in_bag(list: Array, bank_key: String = "in_bank") -> int:
	var c = 0
	for item in list:
		if int(item.get(bank_key, 0)) == 0: c += 1
	return c

func _count_in_bag_equip(list: Array) -> int:
	var c = 0
	for eq in list:
		if int(eq.get("in_bank", 0)) == 0 and int(eq.get("part", 255)) == 255: c += 1
	return c

func _count_in_bag_mecha(list: Array) -> int:
	var c = 0
	for hq in list:
		if int(hq.get("in_bank", 0)) == 0 and int(hq.get("horse_uid", -1)) == -1:
			c += 1
	return c

func _get_item_count_in_bag(inventory: Array, item_idx: int) -> int:
	var total = 0
	for item in inventory:
		var is_in_bank = int(item.get("in_bank", item.get("InBank", 0)))
		var idx = int(item.get("item_index", item.get("ItemIndex", 0)))
		if is_in_bank == 0 and idx == item_idx:
			total += int(item.get("count", item.get("Count", 1)))
	return total


func _on_trade_final_confirm_req() -> void :
	var inv_win = GameManager.current_hud.get_node_or_null("WindowsContainer/InventoryWindow")
	if inv_win: inv_win.visible = false

	if GameManager.current_hud and GameManager.current_hud.has_method("show_message_box"):
		GameManager.current_hud.show_message_box(
			"雙方皆已鎖定，請確認交易物品與金額是否正確？", 
			2, 
			func(): NetworkManager.send_trade_action("TradeFinalConfirm"), 
			func(): NetworkManager.send_trade_action("TradeCancel")
		)

func _on_btn_cancel_pressed() -> void :
	_hide_item_tooltip()
	NetworkManager.send_trade_action("TradeCancel")

func _on_trade_cancelled(msg: String) -> void :
	_hide_item_tooltip()
	if GameManager.current_hud: GameManager.current_hud.show_rejection_message_flash(msg)
	queue_free()

func _on_trade_success(msg: String) -> void :
	_hide_item_tooltip()
	if GameManager.current_hud: GameManager.current_hud.show_system_message_flash(msg)
	queue_free()


func _get_palette_mat(color_idx: int, effect_type: int = 0) -> Material:
	if color_idx <= 0 and effect_type <= 0: return null

	var cache_key = str(color_idx) + "_" + str(effect_type)
	if _palette_mat_cache.has(cache_key):
		return _palette_mat_cache[cache_key]

	var mat = ShaderMaterial.new()
	mat.shader = PALETTE_SHADER
	mat.set_shader_parameter("effect_type", effect_type)
	mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_idx, 0.0))
	_palette_mat_cache[cache_key] = mat
	return mat

func _show_item_tooltip(item_index: int, tab_type: int, item_data: Dictionary = {}) -> void :
	if GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):
		GameManager.current_hud.shared_tooltip.show_item(item_index, tab_type, item_data)

func _hide_item_tooltip() -> void :
	if GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):
		GameManager.current_hud.shared_tooltip.hide_tooltip()
