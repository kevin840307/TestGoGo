
extends Control


@onready var panel_bank: SharedInventoryPanel = $PanelBankLeft
@onready var panel_bag: SharedInventoryPanel = $PanelBagRight


const TRANSFER_POPUP_SCENE = preload("res://bank_transfer_popup.tscn")

var target_player: CharacterBody2D = null

func _ready() -> void :

	if panel_bank: panel_bank.closed.connect(_on_close_requested)
	if panel_bag: panel_bag.closed.connect(_on_close_requested)


	var allowed_tabs = [
		SharedInventoryPanel.TabType.GENERAL, 
		SharedInventoryPanel.TabType.EQUIP, 
		SharedInventoryPanel.TabType.MECHA
	]

	if panel_bank:
		panel_bank.setup_mode(SharedInventoryPanel.DisplayMode.SIMPLE_LIST, allowed_tabs)

		panel_bank.item_clicked.connect(_on_item_clicked.bind(true))

		panel_bank.tab_changed.connect(_on_tab_changed_from_bank)

	if panel_bag:
		panel_bag.setup_mode(SharedInventoryPanel.DisplayMode.SIMPLE_LIST, allowed_tabs)

		panel_bag.item_clicked.connect(_on_item_clicked.bind(false))

		panel_bag.tab_changed.connect(_on_tab_changed_from_bag)

func setup(player: CharacterBody2D) -> void :
	target_player = player
	update_ui()

func _on_close_requested() -> void :
	queue_free()


func _on_tab_changed_from_bank(new_tab: int) -> void :
	if panel_bag and panel_bag.current_tab != new_tab:
		panel_bag._switch_tab(new_tab)
	update_ui()

func _on_tab_changed_from_bag(new_tab: int) -> void :
	if panel_bank and panel_bank.current_tab != new_tab:
		panel_bank._switch_tab(new_tab)
	update_ui()


func update_ui() -> void :
	if not is_instance_valid(target_player) or not "data_model" in target_player: return
	var data = target_player.data_model
	if not data or not panel_bank or not panel_bag: return

	var bank_items = []
	var bag_items = []
	var max_size = 50

	var current_tab = panel_bank.current_tab

	match current_tab:
		SharedInventoryPanel.TabType.GENERAL:
			if "inventory" in data:
				max_size = data.inventory_max_size
				for item in data.inventory:
					if item.get("in_bank", 0) == 1: bank_items.append(item)
					else: bag_items.append(item)

			panel_bank.render_general_items(bank_items)
			panel_bag.render_general_items(bag_items)

		SharedInventoryPanel.TabType.EQUIP:
			if "equip_list" in data:
				max_size = data.equip_max_size
				for eq in data.equip_list:
					if eq.get("part", 255) != 255: continue
					if eq.get("in_bank", 0) == 1: bank_items.append(eq)
					else: bag_items.append(eq)

			panel_bank.render_equip_items(bank_items)
			panel_bag.render_equip_items(bag_items)

		SharedInventoryPanel.TabType.MECHA:
			if "horse_equip_list" in data:
				max_size = data.horse_equip_max_size
				for heq in data.horse_equip_list:
					if heq.get("horse_uid", -1) != -1: continue
					if heq.get("in_bank", 0) == 1: bank_items.append(heq)
					else: bag_items.append(heq)

			panel_bank.render_mecha_equips(bank_items)
			panel_bag.render_mecha_equips(bag_items)


	panel_bank.update_capacity(current_tab, bank_items.size(), max_size)
	panel_bag.update_capacity(current_tab, bag_items.size(), max_size)


func _on_item_clicked(event: InputEvent, item_data: Dictionary, tab: int, tex: Texture2D, mat: Material, is_from_bank: bool) -> void :
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		var to_bank = not is_from_bank

		if tab == SharedInventoryPanel.TabType.GENERAL:
			var popup = TRANSFER_POPUP_SCENE.instantiate()
			add_child(popup)

			var direction_text = "提領" if is_from_bank else "存放"
			var max_amount = int(item_data.get("Count", item_data.get("count", 1)))

			popup.setup(item_data, tex, mat, direction_text, max_amount, true)
			popup.amount_confirmed.connect( func(amount: int):
				_process_bank_transfer_general(item_data, amount, to_bank)
			)

		else:

			if not _check_capacity_for_direct_transfer(tab, to_bank): return


			var item_uid = int(item_data.get("item_id", item_data.get("ItemID", item_data.get("ItemUID", item_data.get("item_uid", item_data.get("EquipUID", item_data.get("equip_uid", 0)))))))

			if NetworkManager:
				NetworkManager.send_secure_packet({
					"Type": "BankTransfer", 
					"UID": item_uid, 
					"ToBank": to_bank, 
					"Amount": 1, 
					"TabType": tab
				})


func _process_bank_transfer_general(item_data: Dictionary, amount: int, to_bank: bool) -> void :
	var item_index = int(item_data.get("ItemIndex", item_data.get("item_index", 0)))
	var target_count = _get_target_general_count(item_index, to_bank)





	if target_count == 0:
		var data = target_player.data_model
		var max_size = data.inventory_max_size
		var current_used = _count_items_for_bank(data.inventory, to_bank)
		if current_used >= max_size:
			if GameManager.current_hud: GameManager.current_hud.show_rejection_message_flash("目標區域空間已滿！")
			return


	var item_uid = int(item_data.get("item_id", item_data.get("ItemID", item_data.get("ItemUID", item_data.get("item_uid", 0)))))

	if NetworkManager:
		NetworkManager.send_secure_packet({
			"Type": "BankTransfer", 
			"UID": item_uid, 
			"ToBank": to_bank, 
			"Amount": amount, 
			"TabType": SharedInventoryPanel.TabType.GENERAL
		})

func _get_target_general_count(item_index: int, check_bank: bool) -> int:
	var total = 0
	var data = target_player.data_model
	if not data or not "inventory" in data: return 0
	for item in data.inventory:
		var item_in_bank = (item.get("in_bank", 0) == 1)
		if item_in_bank == check_bank and int(item.get("ItemIndex", item.get("item_index", 0))) == item_index:
			total += int(item.get("Count", item.get("count", 1)))
	return total

func _check_capacity_for_direct_transfer(tab: int, check_bank: bool) -> bool:
	var data = target_player.data_model
	if not data: return false

	var max_size = 0
	var current_used = 0

	if tab == SharedInventoryPanel.TabType.EQUIP:
		max_size = data.equip_max_size
		current_used = _count_items_for_bank(data.equip_list, check_bank, true)
	elif tab == SharedInventoryPanel.TabType.MECHA:
		max_size = data.horse_equip_max_size
		current_used = _count_items_for_bank(data.horse_equip_list, check_bank, false, true)

	if current_used >= max_size:
		if GameManager.current_hud: GameManager.current_hud.show_rejection_message_flash("目標區域空間已滿，無法轉移！")
		return false

	return true

func _count_items_for_bank(list_data: Array, is_bank: bool, ignore_equipped_char: bool = false, ignore_equipped_mecha: bool = false) -> int:
	var count = 0
	for item in list_data:

		if ignore_equipped_char and item.get("part", 255) != 255: continue

		if ignore_equipped_mecha and item.get("horse_uid", -1) != -1: continue

		var item_in_bank = (item.get("in_bank", 0) == 1)
		if item_in_bank == is_bank:
			count += 1
	return count
