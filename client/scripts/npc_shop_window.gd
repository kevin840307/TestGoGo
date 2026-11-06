
extends Control

@onready var lbl_shop_name = $Background / LblShopName
@onready var btn_close = $Background / BtnClose
@onready var list_shop_items = $Background / ScrollContainer / ListShopItems


@onready var shared_inventory: SharedInventoryPanel = $SharedInventoryPanel

const INVENTORY_ROW_SCENE = preload("res://inventory_item_row.tscn")
const SHOP_BUY_POPUP = preload("res://shop_buy_popup.tscn")
const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}

var _shop_id: int = 0
var _target_player: CharacterBody2D = null
var _npc_shop_items: Array = []

func _ready():
	if btn_close:
		btn_close.pressed.connect(_on_close_pressed)


	if shared_inventory:
		shared_inventory.show_sell_price = true
		shared_inventory.setup_mode(SharedInventoryPanel.DisplayMode.SIMPLE_LIST, [
			SharedInventoryPanel.TabType.GENERAL, 
			SharedInventoryPanel.TabType.EQUIP, 
			SharedInventoryPanel.TabType.MECHA
		])
		shared_inventory.item_clicked.connect(_on_my_inventory_clicked)
		shared_inventory.tab_changed.connect(_on_inventory_tab_changed)

func setup(shop_id: int, shop_name: String, items: Array) -> void :
	_shop_id = shop_id
	_npc_shop_items = items

	if lbl_shop_name: lbl_shop_name.text = shop_name

	if GameManager:
		_target_player = GameManager.current_player
		if shared_inventory and _target_player:
			shared_inventory.setup_player(_target_player)
			shared_inventory._switch_tab(SharedInventoryPanel.TabType.GENERAL)

	_render_npc_items(_npc_shop_items)

func _render_npc_items(items: Array) -> void :
	for child in list_shop_items.get_children():
		child.queue_free()

	var player_money = 0
	if is_instance_valid(_target_player) and "data_model" in _target_player:
		player_money = _target_player.data_model.money

	for item_data in items:
		var row = INVENTORY_ROW_SCENE.instantiate()
		list_shop_items.add_child(row)
		row.size_flags_horizontal = Control.SIZE_EXPAND_FILL

		var item_idx = int(item_data.get("ItemIndex", 0))
		var price = int(item_data.get("Price", 0))
		var tab = int(item_data.get("TabType", 0))

		var i_name = "未知物品"
		var icon_id = 0
		var color_idx = 0
		var effect_type = 0

		if ItemLoader:
			i_name = ItemLoader.get_item_property(item_idx, "item_name", "未知物品")
			icon_id = int(ItemLoader.get_item_property(item_idx, "icon_id", 1)) - 1
			color_idx = int(ItemLoader.get_item_property(item_idx, "color_index", 0))
			effect_type = int(ItemLoader.get_item_property(item_idx, "effect_type", 0))

		var tex = ItemLoader.get_icon(icon_id) if ItemLoader else null
		var mat = _get_palette_mat(color_idx, effect_type)


		var top_txt = ""
		if tab == 1 or tab == 3:
			if ItemLoader:
				var req_level = ItemLoader.get_item_property(item_idx, "req_level", 0)
				top_txt = "Lv." + str(req_level)


		var is_affordable = price <= player_money
		var display_color = Color.YELLOW if is_affordable else Color.WHITE

		row.setup(tex, mat, top_txt, i_name, display_color, display_color)

		if row.has_method("set_price"):
			row.set_price(price)

			if "lbl_price" in row and is_instance_valid(row.lbl_price):
				row.lbl_price.add_theme_color_override("font_color", display_color)

		row.mouse_filter = Control.MOUSE_FILTER_PASS
		row.mouse_entered.connect(_show_item_tooltip.bind(item_idx, tab, item_data))
		row.mouse_exited.connect(_hide_item_tooltip)


		if is_affordable:
			row.gui_input.connect(_on_shop_item_clicked.bind(item_data, tex, mat))


func update_ui() -> void :
	if not is_instance_valid(_target_player) or not "data_model" in _target_player: return
	if not shared_inventory: return

	var p_data = _target_player.data_model
	var items_to_show = []
	var max_size = 0
	var used_capacity = 0

	var current_tab = shared_inventory.current_tab

	match current_tab:
		SharedInventoryPanel.TabType.GENERAL:
			if "inventory" in p_data:
				max_size = p_data.inventory_max_size
				for item in p_data.inventory:
					if int(item.get("in_bank", 0)) == 0:
						used_capacity += 1
						items_to_show.append(item)
			shared_inventory.render_general_items(items_to_show)

		SharedInventoryPanel.TabType.EQUIP:
			if "equip_list" in p_data:
				max_size = p_data.equip_max_size
				for eq in p_data.equip_list:

					if int(eq.get("in_bank", 0)) == 0 and int(eq.get("part", 255)) == 255:
						used_capacity += 1
						items_to_show.append(eq)
			shared_inventory.render_equip_items(items_to_show)

		SharedInventoryPanel.TabType.MECHA:
			if "horse_equip_list" in p_data:
				max_size = p_data.horse_equip_max_size
				for hq in p_data.horse_equip_list:

					if int(hq.get("in_bank", 0)) == 0 and int(hq.get("horse_uid", -1)) == -1:
						used_capacity += 1
						items_to_show.append(hq)
			shared_inventory.render_mecha_equips(items_to_show)


	shared_inventory.update_capacity(current_tab, used_capacity, max_size)


	if _npc_shop_items.size() > 0:
		var scroll_node = list_shop_items.get_parent()
		var scroll_v = 0


		if scroll_node is ScrollContainer:
			scroll_v = scroll_node.scroll_vertical

		_render_npc_items(_npc_shop_items)


		if scroll_node is ScrollContainer:
			await get_tree().process_frame

			if is_instance_valid(scroll_node):
				scroll_node.scroll_vertical = scroll_v

func _on_inventory_tab_changed(_new_tab: int) -> void :
	update_ui()


func _on_shop_item_clicked(event: InputEvent, item_data: Dictionary, tex: Texture2D, mat: Material) -> void :
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_hide_item_tooltip()

		var popup = SHOP_BUY_POPUP.instantiate()
		add_child(popup)

		var price = int(item_data.get("Price", 0))
		var tab = int(item_data.get("TabType", 0))
		var item_idx = int(item_data.get("ItemIndex", 0))

		var is_general = (tab == 0)
		var max_buy_count = 9999 if is_general else 1

		popup.setup(item_data, tex, mat, max_buy_count, price, is_general)

		popup.buy_confirmed.connect( func(buy_count):
			if NetworkManager:
				NetworkManager.send_shop_action("BuyNpcItem", {
					"ShopID": _shop_id, 
					"ItemIndex": item_idx, 
					"Count": buy_count
				})
		)


func _on_my_inventory_clicked(event: InputEvent, item_data: Dictionary, tab: int, tex: Texture2D, mat: Material) -> void :
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_hide_item_tooltip()

		var uid = int(item_data.get("ItemUID", item_data.get("item_uid", item_data.get("ItemID", item_data.get("item_id", item_data.get("EquipUID", item_data.get("equip_uid", item_data.get("UID", 0))))))))
		var item_index = int(item_data.get("ItemIndex", item_data.get("item_index", item_data.get("ItemIdx", item_data.get("item_idx", item_data.get("EquipIdx", item_data.get("equip_idx", 0)))))))


		var can_sell = 1
		if ItemLoader:
			can_sell = ItemLoader.get_item_property(item_index, "can_sell", 1)


		if can_sell == 0:
			if GameManager.current_hud:
				GameManager.current_hud.show_rejection_message_box("此物品無法出售。")
			return


		var base_price = 0
		var item_name = item_data.get("Name", item_data.get("name", "未知物品"))

		if ItemLoader:
			base_price = ItemLoader.get_item_property(item_index, "price", 0)
			if item_name == "未知物品":
				item_name = ItemLoader.get_item_property(item_index, "item_name", "未知物品")

		var sell_price = base_price / 2


		if tab == SharedInventoryPanel.TabType.GENERAL:
			var max_count = int(item_data.get("Count", item_data.get("count", 1)))

			var popup = SHOP_BUY_POPUP.instantiate()
			add_child(popup)
			popup.setup(item_data, tex, mat, max_count, sell_price, true)
			popup.input_count.text_submitted.connect( func(_text):
				if is_instance_valid(popup) and popup.btn_confirm and not popup.btn_confirm.disabled:
					popup.btn_confirm.pressed.emit()
			)


			if "lbl_title" in popup and popup.lbl_title:
				popup.lbl_title.text = "出售商品"

			popup.buy_confirmed.connect( func(sell_count):
				if NetworkManager: NetworkManager.send_shop_action("SellNpcItem", {"UID": uid, "TabType": tab, "Count": sell_count})
			)


		else:
			if GameManager.current_hud:
				GameManager.current_hud.show_message_box(
					"確定要出售 [%s] 嗎？\n可獲得： $ %d" % [item_name, sell_price], 
					2, 
					func():
						if NetworkManager:
							NetworkManager.send_shop_action("SellNpcItem", {"UID": uid, "TabType": tab, "Count": 1})
				)


func _get_palette_mat(color_idx: int, effect_type: int = 0) -> Material:
	if color_idx <= 0 and effect_type <= 0: return null
	var mat = ShaderMaterial.new()
	mat.shader = PALETTE_SHADER
	mat.set_shader_parameter("effect_type", effect_type)
	mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_idx, 0.0))
	return mat

func _show_item_tooltip(item_index: int, tab_type: int, item_data: Dictionary = {}) -> void :
	if GameManager and GameManager.current_hud:
		var hud = GameManager.current_hud
		if hud.get("shared_tooltip"):
			hud.shared_tooltip.show_item(item_index, tab_type, item_data)

func _hide_item_tooltip() -> void :
	if GameManager and GameManager.current_hud:
		var hud = GameManager.current_hud
		if hud.get("shared_tooltip"):
			hud.shared_tooltip.hide_tooltip()

func _on_close_pressed() -> void :
	_hide_item_tooltip()
	queue_free()
