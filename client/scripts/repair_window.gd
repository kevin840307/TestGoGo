
extends Control

@onready var shared_inventory: SharedInventoryPanel = $SharedInventoryPanel

var _target_player: CharacterBody2D = null

func _ready() -> void :
	if shared_inventory:

		shared_inventory.show_repair_price = true


		shared_inventory.item_clicked.connect(_on_inventory_item_clicked)
		shared_inventory.tab_changed.connect(_on_inventory_tab_changed)
		shared_inventory.closed.connect(queue_free)


func setup(player: CharacterBody2D, repair_type: int = 1) -> void :
	_target_player = player
	if shared_inventory:


		shared_inventory.setup_player(player)

		var allowed_tabs = []
		var target_tab = SharedInventoryPanel.TabType.EQUIP

		if repair_type == 1:
			allowed_tabs = [SharedInventoryPanel.TabType.EQUIP]
			target_tab = SharedInventoryPanel.TabType.EQUIP
		elif repair_type == 2:
			allowed_tabs = [SharedInventoryPanel.TabType.MECHA]
			target_tab = SharedInventoryPanel.TabType.MECHA

		shared_inventory.setup_mode(SharedInventoryPanel.DisplayMode.SIMPLE_LIST, allowed_tabs)
		shared_inventory._switch_tab(target_tab)

func update_ui() -> void :
	if not is_instance_valid(_target_player) or not _target_player.data_model: return
	if not shared_inventory: return

	var p_data = _target_player.data_model
	var items_to_show = []
	var max_size = 0
	var used_capacity = 0

	var current_tab = shared_inventory.current_tab

	match current_tab:
		SharedInventoryPanel.TabType.EQUIP:
			max_size = p_data.equip_max_size
			for eq in p_data.equip_list:
				if int(eq.get("in_bank", 0)) == 0 and int(eq.get("in_shop", 0)) == 0:
					var can_repair = ItemLoader.get_item_property(eq.get("item_idx", 0), "can_repair", 1)
					if can_repair == 1:
						used_capacity += 1
						items_to_show.append(eq)
			shared_inventory.render_equip_items(items_to_show)

		SharedInventoryPanel.TabType.MECHA:
			max_size = p_data.horse_equip_max_size
			for hq in p_data.horse_equip_list:
				if int(hq.get("in_bank", 0)) == 0 and int(hq.get("in_shop", 0)) == 0:


					used_capacity += 1
					items_to_show.append(hq)

			shared_inventory.render_mecha_equips(items_to_show)

	shared_inventory.update_capacity(current_tab, used_capacity, max_size)

func _on_inventory_tab_changed(_new_tab: int) -> void :
	update_ui()


func _on_inventory_item_clicked(event: InputEvent, item_data: Dictionary, tab: int, _tex: Texture2D, _mat: Material) -> void :

	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		var damage = int(item_data.get("Damage", item_data.get("damage", 0)))
		var item_idx = int(item_data.get("ItemIdx", item_data.get("item_idx", item_data.get("EquipIdx", item_data.get("equip_idx", 0)))))
		var durability = ItemLoader.get_item_property(item_idx, "durability", 0)

		if damage >= durability:
			if GameManager.current_hud:
				GameManager.current_hud.show_rejection_message_flash("此裝備和新的一樣，不需要修理！")
			return

		var uid = 0
		var item_type = ""

		if tab == SharedInventoryPanel.TabType.EQUIP:
			uid = int(item_data.get("ItemID", item_data.get("item_id", 0)))
			item_type = "Equip"
		elif tab == SharedInventoryPanel.TabType.MECHA:
			uid = int(item_data.get("EquipUID", item_data.get("equip_uid", 0)))
			item_type = "HorseEquip"

		if uid > 0 and NetworkManager:
			NetworkManager.send_repair_item_request(item_type, uid)
