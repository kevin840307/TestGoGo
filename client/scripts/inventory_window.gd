
extends SharedInventoryPanel
class_name InventoryWindow

signal equip_requested(item_uid: int, target_slot: int)
signal unequip_requested(target_slot: int)

var _is_equip_locked: bool = false

func _ready() -> void :
	super._ready()

	setup_mode(DisplayMode.FULL_INVENTORY, [
		TabType.GENERAL, 
		TabType.EQUIP, 
		TabType.PET, 
		TabType.MECHA
	])


	item_clicked.connect(_on_inventory_item_clicked)
	equip_slot_clicked.connect(_on_equip_slot_clicked)
	tab_changed.connect(_on_tab_changed)


	closed.connect( func():
		if GameManager and GameManager.current_hud:
			GameManager.current_hud._close_window(self)
		else:
			visible = false
	)


func setup(player: CharacterBody2D) -> void :
	setup_player(player)
	update_ui()

func unlock_equip() -> void :
	_is_equip_locked = false

func _on_tab_changed(_new_tab: int) -> void :
	update_ui()

func update_ui() -> void :

	if not is_instance_valid(_target_player) or not "data_model" in _target_player: return
	var data = _target_player.data_model
	if not data: return


	match current_tab:
		TabType.GENERAL:
			var backpack_items = []
			for item in data.inventory:
				if item.get("in_bank", 0) == 0: backpack_items.append(item)

			render_general_items(backpack_items)
			update_capacity(TabType.GENERAL, backpack_items.size(), data.inventory_max_size)

		TabType.EQUIP:
			var bag_equip_items = []
			var equipped_map = {}
			for eq in data.equip_list:
				var part = eq.get("part", 255)
				if part >= 0 and part <= 8:
					equipped_map[part] = eq
				elif part == 255 and eq.get("in_bank", 0) == 0:
					bag_equip_items.append(eq)

			var total_equip_in_bag = 0
			for eq in data.equip_list:
				if eq.get("in_bank", 0) == 0: total_equip_in_bag += 1

			render_equip_items(bag_equip_items)
			render_equipped_slots(equipped_map, data)
			update_capacity(TabType.EQUIP, total_equip_in_bag, data.equip_max_size)

		TabType.PET:
			if has_method("render_pet_items"):
				render_pet_items(data.pet_list)
				update_capacity(TabType.PET, data.pet_list.size(), data.pet_max_size)

		TabType.MECHA:
			var horse_equip_items = []
			for heq in data.horse_equip_list:
				if heq.get("in_bank", 0) == 0 and heq.get("horse_uid", -1) == -1:
					horse_equip_items.append(heq)

			var in_use_horses = []
			var all_horses = []
			for horse in data.horse_list:
				var horse_uid = int(horse.get("horse_uid", 0))
				if data.summoned_horse_uid != -1 and data.summoned_horse_uid == horse_uid:
					in_use_horses.append(horse)
				all_horses.append(horse)

			var battery_items = []
			for inv in data.inventory:
				if inv.get("in_bank", 0) == 0:
					var b_idx = inv.get("item_index", 0)
					if ItemLoader.get_item_property(b_idx, "battery", 0) > 0:
						battery_items.append(inv)
			render_mecha_bodies(in_use_horses, all_horses)
			render_mecha_equips(horse_equip_items)
			render_mecha_batteries(battery_items)
			update_capacity(TabType.MECHA, horse_equip_items.size(), data.horse_equip_max_size)

func _on_inventory_item_clicked(event: InputEvent, item_data: Dictionary, tab: int, tex: Texture2D, mat: Material) -> void :
	if event is InputEventMouseButton and event.pressed:




		if event.button_index == MOUSE_BUTTON_LEFT:

			if DropItemManager and DropItemManager.is_dragging:
				DropItemManager.cancel_drag()
				return

			if _is_equip_locked: return

			if tab == TabType.GENERAL:

				if current_mode == DisplayMode.FULL_INVENTORY:
					var item_index = int(item_data.get("item_index", item_data.get("ItemIdx", item_data.get("ItemIndex", 0))))
					var item_uid = int(item_data.get("item_uid", item_data.get("UID", item_data.get("ItemUID", 0))))

					var category = -1
					if ItemLoader:
						category = ItemLoader.get_item_category(item_index)


					if category == 0:
						if NetworkManager: NetworkManager.send_use_item_request(item_uid)
						return

			elif tab == TabType.EQUIP:
				if not is_instance_valid(_target_player) or not "data_model" in _target_player: return
				var item_index = item_data.get("item_index", item_data.get("item_idx", item_data.get("equip_idx", 0)))

				if _check_equip_conditions(item_index, _target_player.data_model):
					var item_uid = item_data.get("item_id", item_data.get("uid", 0))
					var target_slot = ItemLoader.get_item_property(item_index, "equip_slot", ItemLoader.get_item_property(item_index, "part", 255))

					if target_slot != 255:
						_is_equip_locked = true
						equip_requested.emit(item_uid, target_slot)
						_target_player.data_model.optimistic_equip(item_uid, target_slot)

						if _target_player.has_method("update_paper_doll_part"):
							_target_player.update_paper_doll_part(target_slot, item_index)

						if target_slot == 5 or target_slot == 6:
							if not _target_player.get("is_dead"):
								_target_player.current_dir = "1"
								if _target_player.has_method("get_combat_idle_state"):
									_target_player.current_state = _target_player.get_combat_idle_state()
								else:
									_target_player.current_state = "idle_weapon_melee"
							_target_player.sync_animation()
							if NetworkManager and NetworkManager.is_connected_to_server:
								NetworkManager.send_secure_packet({"Type": "SyncState", "State": _target_player.current_state, "Dir": _target_player.current_dir})

						update_ui()
						var hud = get_node_or_null("/root/GameManager/CanvasLayer")
						if hud == null and GameManager and GameManager.current_hud:
							hud = GameManager.current_hud

						if hud:
							if hud.has_method("update_status_panel"): hud.update_status_panel()
							var char_win = hud.get_node_or_null("WindowsContainer/CharacterWindow")
							if char_win and char_win.visible and char_win.has_method("update_ui"): char_win.update_ui()
						get_tree().create_timer(0.5).timeout.connect(unlock_equip)

			elif tab == TabType.MECHA:
				var item_index = item_data.get("item_index", item_data.get("item_idx", item_data.get("equip_idx", item_data.get("horse_idx", item_data.get("pet_id", 0)))))
				var category = ItemLoader.get_item_category(item_index) if ItemLoader else -1

				if category == 3:
					if GameManager and GameManager.current_hud:
						var hud = GameManager.current_hud
						if hud.has_method("show_horse_preview"):
							hud.show_horse_preview(item_data)




		elif event.button_index == MOUSE_BUTTON_RIGHT:
			var drop_tab_type = tab
			if tab == TabType.MECHA:
				var item_index = item_data.get("item_index", item_data.get("item_idx", item_data.get("equip_idx", item_data.get("horse_idx", item_data.get("pet_id", 0)))))
				var category = ItemLoader.get_item_category(item_index) if ItemLoader else -1
				if category == 3:
					var h_uid = item_data.get("horse_uid", -1)
					if _target_player and "data_model" in _target_player and _target_player.data_model.summoned_horse_uid == h_uid:
						drop_tab_type = -1
					else:
						drop_tab_type = 4
				elif category == 4:
					drop_tab_type = 3
				else:
					drop_tab_type = 0

			if drop_tab_type == -1:
				if GameManager and GameManager.current_hud:
					GameManager.current_hud.show_rejection_message_box("駕駛中的機甲無法丟棄！")
				return

			if DropItemManager:
				if DropItemManager.is_dragging:
					DropItemManager.cancel_drag()
				else:
					DropItemManager.start_drag(item_data, drop_tab_type, tex, mat)

func _on_equip_slot_clicked(target_slot: int) -> void :
	if _is_equip_locked: return
	if not is_instance_valid(_target_player) or not "data_model" in _target_player: return

	_is_equip_locked = true
	unequip_requested.emit(target_slot)

	_target_player.data_model.optimistic_unequip(target_slot)
	if _target_player.has_method("update_paper_doll_part"):
		_target_player.update_paper_doll_part(target_slot, 0)

	if target_slot == 5 or target_slot == 6:
		if not _target_player.get("is_dead"):
			_target_player.current_dir = "1"
			if _target_player.has_method("get_combat_idle_state"):
				_target_player.current_state = _target_player.get_combat_idle_state()
			else:
				_target_player.current_state = "idle_weapon_melee"
		_target_player.sync_animation()

		if NetworkManager and NetworkManager.is_connected_to_server:
			NetworkManager.send_secure_packet({"Type": "SyncState", "State": _target_player.current_state, "Dir": _target_player.current_dir})

	update_ui()

	var hud = null
	if GameManager and GameManager.current_hud:
		hud = GameManager.current_hud

	if hud:
		if hud.has_method("update_status_panel"): hud.update_status_panel()
		var char_win = hud.get_node_or_null("WindowsContainer/CharacterWindow")
		if char_win and char_win.visible and char_win.has_method("update_ui"):
			char_win.update_ui()

	get_tree().create_timer(0.5).timeout.connect(unlock_equip)
