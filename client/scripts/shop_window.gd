
extends Control

@onready var lbl_shop_name = $PanelLeft / LblShopName
@onready var list_shop_items = $PanelLeft / ScrollContainer / ListShopItems
@onready var btn_action = $PanelLeft / BtnAction
@onready var btn_cancel = $PanelLeft / BtnCancel


@onready var shared_inventory: SharedInventoryPanel = $PanelRight

const INVENTORY_ROW_SCENE = preload("res://inventory_item_row.tscn")
const SHOP_SETUP_POPUP = preload("res://shop_setup_popup.tscn")
const SHOP_BUY_POPUP = preload("res://shop_buy_popup.tscn")
const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}
var _palette_mat_cache: Dictionary = {}

enum Mode{OWNER_SETUP, OWNER_OPEN, CUSTOMER}
var current_mode: Mode = Mode.OWNER_SETUP
var target_player: CharacterBody2D = null
var shop_owner_dbid: int = 0
var shop_items_data: Array = []

func _ready():
	btn_action.pressed.connect(_on_btn_action_pressed)
	if btn_cancel:
		btn_cancel.pressed.connect(_on_btn_cancel_pressed)


	if shared_inventory:

		shared_inventory.closed.connect(_on_btn_cancel_pressed)

		shared_inventory.setup_mode(SharedInventoryPanel.DisplayMode.SIMPLE_LIST, [
			SharedInventoryPanel.TabType.GENERAL, 
			SharedInventoryPanel.TabType.EQUIP, 
			SharedInventoryPanel.TabType.PET, 
			SharedInventoryPanel.TabType.MECHA
		])
		shared_inventory.item_clicked.connect(_on_my_inventory_clicked)
		shared_inventory.tab_changed.connect(_on_inventory_tab_changed)


func setup_as_owner(player: CharacterBody2D, shop_name: String, is_already_open: bool):
	target_player = player
	shop_owner_dbid = player.data_model.db_id if player.data_model else 0
	lbl_shop_name.text = shop_name

	if is_already_open:
		current_mode = Mode.OWNER_OPEN
		btn_action.text = "結束營業"
		if shared_inventory: shared_inventory.visible = false
	else:
		current_mode = Mode.OWNER_SETUP
		btn_action.text = "開始營業"
		if shared_inventory:
			shared_inventory.visible = true
			shared_inventory.modulate.a = 1.0
			shared_inventory.mouse_filter = Control.MOUSE_FILTER_PASS

	update_ui()


func setup_as_customer(shop_name: String, owner_dbid: int, items: Array):
	current_mode = Mode.CUSTOMER
	shop_owner_dbid = owner_dbid
	lbl_shop_name.text = shop_name
	shop_items_data = items
	btn_action.text = "關閉"
	if shared_inventory: shared_inventory.visible = false
	_render_shop_items()

func _on_btn_cancel_pressed():
	_hide_item_tooltip()
	if current_mode == Mode.OWNER_SETUP:
		if NetworkManager: NetworkManager.send_shop_action("CloseShop", {})
		queue_free()
	else:
		queue_free()

func _on_btn_action_pressed():
	_hide_item_tooltip()
	if current_mode == Mode.OWNER_SETUP:
		if shop_items_data.is_empty():
			if GameManager.current_hud and GameManager.current_hud.has_method("show_rejection_message_box"):
				GameManager.current_hud.show_rejection_message_box("請先上架至少一件商品才能開店！")
			return
		var s_name = lbl_shop_name.text if lbl_shop_name.text != "" else "我的商店"
		if NetworkManager:
			NetworkManager.send_shop_action("OpenShop", {"ShopName": s_name})
		queue_free()
		GameManager.current_hud.show_system_message_chat("開啟小商店成功！")
	elif current_mode == Mode.OWNER_OPEN:
		if NetworkManager: NetworkManager.send_shop_action("CloseShop", {})
		queue_free()
	elif current_mode == Mode.CUSTOMER:
		queue_free()


func _on_inventory_tab_changed(_new_tab: int):
	update_ui()

func sync_shop_items(items: Array):
	shop_items_data = items
	update_ui()

func update_ui():
	if current_mode == Mode.CUSTOMER: return
	if not is_instance_valid(target_player) or not target_player.data_model: return

	_render_my_inventory()
	_render_shop_items()


func _render_my_inventory():
	if not shared_inventory: return
	var p_data = target_player.data_model
	var items_to_show = []
	var max_size = 50
	var used_capacity = 0

	match shared_inventory.current_tab:
		SharedInventoryPanel.TabType.GENERAL:
			max_size = p_data.inventory_max_size
			var merged_dict = {}
			for item in p_data.inventory:
				if int(item.get("InBank", item.get("in_bank", 0))) == 0:
					used_capacity += 1


					if int(item.get("InShop", item.get("in_shop", 0))) == 0:
						var idx = int(item.get("ItemIndex", item.get("item_index", item.get("item_idx", 0))))
						var current_count = int(item.get("Count", item.get("count", 1)))
						if merged_dict.has(idx):
							merged_dict[idx]["count"] += current_count
						else:
							merged_dict[idx] = item.duplicate()
							merged_dict[idx]["count"] = current_count

			for item in merged_dict.values():
				if int(item.get("count", 0)) > 0:
					items_to_show.append(item)
			shared_inventory.render_general_items(items_to_show)

		SharedInventoryPanel.TabType.EQUIP:
			max_size = p_data.equip_max_size
			for eq in p_data.equip_list:
				if int(eq.get("InBank", eq.get("in_bank", 0))) == 0:
					used_capacity += 1
					if int(eq.get("Part", eq.get("part", 255))) == 255 and int(eq.get("InShop", eq.get("in_shop", 0))) == 0:
						items_to_show.append(eq)
			shared_inventory.render_equip_items(items_to_show)

		SharedInventoryPanel.TabType.PET:
			max_size = p_data.pet_max_size
			for pt in p_data.pet_list:
				used_capacity += 1
				var state = int(pt.get("state", pt.get("State", 0)))
				var in_shop = int(pt.get("in_shop", pt.get("InShop", 0)))


				if state == 0 and in_shop == 0:
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
					if int(hq.get("in_shop", 0)) == 0:
						items_to_show.append(hq)
			shared_inventory.render_mecha_equips(items_to_show)


	shared_inventory.update_capacity(shared_inventory.current_tab, used_capacity, max_size)

func _render_shop_items():
	for child in list_shop_items.get_children():
		child.queue_free()


	var player_money = 0
	if current_mode == Mode.CUSTOMER and GameManager and is_instance_valid(GameManager.current_player) and "data_model" in GameManager.current_player:
		player_money = GameManager.current_player.data_model.money

	for item_data in shop_items_data:
		var row = INVENTORY_ROW_SCENE.instantiate()
		list_shop_items.add_child(row)
		row.size_flags_horizontal = Control.SIZE_EXPAND_FILL

		var item_idx = int(item_data.get("ItemIndex", item_data.get("item_index", item_data.get("ItemIdx", item_data.get("item_idx", item_data.get("EquipIdx", item_data.get("equip_idx", 0)))))))
		var count = int(item_data.get("Count", item_data.get("count", 1)))
		var price = int(item_data.get("Price", item_data.get("ShopPrice", item_data.get("shop_price", 0))))
		var tab = int(item_data.get("TabType", 0))

		var i_name = item_data.get("Name", item_data.get("name", ""))
		if i_name == "":
			i_name = ItemLoader.get_item_property(item_idx, "name", "未知")
			if i_name == "未知":
				i_name = ItemLoader.get_item_property(item_idx, "item_name", "未知")

		var icon_id = int(ItemLoader.get_item_property(item_idx, "icon_id", 1)) - 1
		var tex = ItemLoader.get_icon(icon_id)

		var color_idx = int(item_data.get("Color", item_data.get("color", ItemLoader.get_item_property(item_idx, "color_index", 0))))
		var effect_type = int(item_data.get("EffectType", item_data.get("effect_type", ItemLoader.get_item_property(item_idx, "effect_type", 0))))

		var top_txt = ""
		var tooltip_data = item_data

		if tab == 0:
			top_txt = "x " + str(count)
		elif tab == 1:
			var upgrade = int(item_data.get("Upgrade", item_data.get("upgrade", 0)))
			top_txt = "+%d" % upgrade if upgrade > 0 else ""
		elif tab == 2:
			var pet_data = item_data.get("ExtraData", item_data.get("extra_data", {}))
			if typeof(pet_data) == TYPE_DICTIONARY and not pet_data.is_empty():
				i_name = str(pet_data.get("Name", pet_data.get("name", "未知寵物")))
				var pet_lvl = int(pet_data.get("Level", pet_data.get("level", 1)))
				top_txt = "Lv." + str(pet_lvl)

				if ItemLoader: tex = ItemLoader.get_icon(375)
				var evol_id = int(pet_data.get("EvolID", pet_data.get("evol_id", 0)))
				if PetLoader:
					var config = PetLoader.get_pet_config(item_idx)
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
				var pet_lvl = int(item_data.get("Level", item_data.get("level", 1)))
				top_txt = "Lv." + str(pet_lvl)
		elif tab == 3:
			pass

		var mat = _get_palette_mat(color_idx, effect_type)


		var price_color = Color.YELLOW
		if current_mode == Mode.CUSTOMER:
			price_color = Color.YELLOW if price <= player_money else Color.WHITE

		row.setup(tex, mat, top_txt, i_name, Color.WHITE, Color.YELLOW)


		if row.has_method("set_price"):
			row.set_price(price, price_color)

		row.mouse_filter = Control.MOUSE_FILTER_PASS
		row.mouse_entered.connect(_show_item_tooltip.bind(item_idx, tab, tooltip_data))
		row.mouse_exited.connect(_hide_item_tooltip)

		row.gui_input.connect(_on_shop_item_clicked.bind(item_data, tex, mat))


func _on_my_inventory_clicked(event: InputEvent, item_data: Dictionary, tab: int, tex: Texture2D, mat: Material):
	if current_mode != Mode.OWNER_SETUP: return
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if has_node("ShopSetupPopup"): return
		var popup = SHOP_SETUP_POPUP.instantiate()
		popup.name = "ShopSetupPopup"
		add_child(popup)
		var max_count = int(item_data.get("Count", item_data.get("count", 1)))


		popup.setup(item_data, tex, mat, max_count, tab == SharedInventoryPanel.TabType.GENERAL)
		popup.setup_confirmed.connect( func(count, price):
			var uid = int(item_data.get("ItemUID", item_data.get("item_uid", item_data.get("ItemID", item_data.get("item_id", item_data.get("EquipUID", item_data.get("equip_uid", item_data.get("UID", item_data.get("uid", 0)))))))))
			if NetworkManager:
				NetworkManager.send_shop_action("AddItem", {"UID": uid, "Count": count, "Price": price, "TabType": tab})
		)

func _on_shop_item_clicked(event: InputEvent, item_data: Dictionary, tex: Texture2D, mat: Material):
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_hide_item_tooltip()

		if current_mode == Mode.OWNER_SETUP:
			var uid = int(item_data.get("ItemUID", item_data.get("item_uid", item_data.get("ItemID", item_data.get("item_id", item_data.get("EquipUID", item_data.get("equip_uid", item_data.get("UID", item_data.get("uid", 0)))))))))
			var tab = int(item_data.get("TabType", 0))
			if NetworkManager:
				NetworkManager.send_shop_action("RemoveItem", {"UID": uid, "TabType": tab})

		elif current_mode == Mode.CUSTOMER:
			var popup = SHOP_BUY_POPUP.instantiate()
			add_child(popup)
			var max_count = int(item_data.get("Count", item_data.get("count", 1)))
			var price = int(item_data.get("Price", item_data.get("ShopPrice", item_data.get("shop_price", 0))))
			var is_general = int(item_data.get("TabType", 0)) == 0
			popup.setup(item_data, tex, mat, max_count, price, is_general)
			popup.buy_confirmed.connect( func(buy_count):
				var uid = int(item_data.get("ItemUID", item_data.get("item_uid", item_data.get("ItemID", item_data.get("item_id", item_data.get("EquipUID", item_data.get("equip_uid", item_data.get("UID", item_data.get("uid", 0)))))))))
				if NetworkManager:
					NetworkManager.send_shop_action("BuyItem", {"TargetDBID": shop_owner_dbid, "UID": uid, "Count": buy_count})
			)

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
