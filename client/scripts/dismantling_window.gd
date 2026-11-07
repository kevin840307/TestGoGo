
extends Control


@onready var search_input = $SearchInput
@onready var check_craftable = $CheckCraftable
@onready var item_tree = $ItemTree

@onready var preview_icon = $PreviewIcon
@onready var lbl_item_name = $LblItemName
@onready var lbl_fee = $LblFee
@onready var lbl_success_rate = $LblSuccessRate
@onready var output_container = $OutputContainer

@onready var lbl_selected_count = $LblSelectedCount
@onready var btn_dismantle = $BtnDismantle

const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const ITEM_ICON_TINT = preload("res://scripts/item_icon_tint.gd")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}
const AVAILABLE_ITEM_COLOR: = Color8(255, 240, 45)
const NORMAL_FEE_COLOR: = Color.WHITE
const INSUFFICIENT_FEE_COLOR: = Color.RED
const SEARCH_DEBOUNCE_SECONDS: = 0.15
const REQUEST_COOLDOWN_SECONDS: = 0.6

var dismantle_recipes: Dictionary = {}
var selected_uids: Array = []
var _search_debounce_timer: Timer
var _request_generation: int = 0
var _request_cooldown_active: bool = false

func _ready() -> void :
	_search_debounce_timer = Timer.new()
	_search_debounce_timer.one_shot = true
	_search_debounce_timer.wait_time = SEARCH_DEBOUNCE_SECONDS
	add_child(_search_debounce_timer)
	_search_debounce_timer.timeout.connect(_apply_current_filter)
	search_input.text_changed.connect(_on_search_text_changed)
	if check_craftable:
		check_craftable.toggled.connect(_on_filter_changed)

	item_tree.select_mode = Tree.SELECT_SINGLE
	item_tree.item_selected.connect(_on_item_selected)

	btn_dismantle.pressed.connect(_on_btn_dismantle_pressed)

	if preview_icon:
		preview_icon.mouse_filter = Control.MOUSE_FILTER_PASS
		preview_icon.mouse_entered.connect(_on_preview_mouse_entered)
		preview_icon.mouse_exited.connect(_on_preview_mouse_exited)

	if NetworkManager.has_signal("dismantle_item_response_received"):
		NetworkManager.dismantle_item_response_received.connect(_on_dismantle_response)

	_clear_right_panel()

func _on_preview_mouse_entered() -> void :
	var first_item = item_tree.get_selected()
	if not first_item: return

	var meta = first_item.get_metadata(0)
	if not meta: return

	var item_index = int(meta.get("item_index", 0))
	var tab_type = 1
	if ItemLoader:
		var cat = ItemLoader.get_item_category(item_index)
		tab_type = 1 if cat == 1 else (3 if cat in [3, 4, 5] else 0)

	if GameManager and GameManager.current_hud and GameManager.current_hud.has_node("SharedTooltip"):
		GameManager.current_hud.shared_tooltip.show_item(item_index, tab_type, meta.get("item_data", {}))

func _on_preview_mouse_exited() -> void :
	if GameManager and GameManager.current_hud and GameManager.current_hud.has_node("SharedTooltip"):
		GameManager.current_hud.shared_tooltip.hide_tooltip()

func set_recipe_data_initial_async(recipes: Array) -> void :
	dismantle_recipes.clear()
	for recipe_value in recipes:
		var recipe: Dictionary = recipe_value
		if str(recipe.get("ActionType", "")) == "Dismantle":
			var inputs: Array = recipe.get("Inputs", [])
			if not inputs.is_empty():
				var input_id: int = int(inputs[0].get("ItemID", 0))
				dismantle_recipes[input_id] = recipe

	var previously_selected_key = null
	var current_sel = item_tree.get_selected()
	if current_sel and current_sel.get_metadata(0):
		var selected_meta: Dictionary = current_sel.get_metadata(0)
		previously_selected_key = _get_selection_key(selected_meta)

	item_tree.clear()
	var root: TreeItem = item_tree.create_item()
	item_tree.hide_root = true

	var owned_items: Dictionary = {}
	if is_instance_valid(GameManager.current_player) and "data_model" in GameManager.current_player:
		var p_data = GameManager.current_player.data_model
		for eq_value in p_data.equip_list:
			var eq: Dictionary = eq_value
			if int(eq.get("in_bank", 0)) == 0 and int(eq.get("in_shop", 0)) == 0 and int(eq.get("part", 255)) == 255:
				var idx: int = int(eq.get("item_idx", 0))
				if dismantle_recipes.has(idx):
					if not owned_items.has(idx):
						owned_items[idx] = []
					owned_items[idx].append({"uid": eq.get("item_id", 0), "item_index": idx, "item_data": eq, "is_equip": true, "owned": true})
		for inv_value in p_data.inventory:
			var inv: Dictionary = inv_value
			if int(inv.get("in_bank", 0)) != 0 or int(inv.get("in_shop", 0)) != 0:
				continue
			var idx: int = int(inv.get("item_index", 0))
			if not dismantle_recipes.has(idx):
				continue
			var recipe: Dictionary = dismantle_recipes[idx]
			var inputs: Array = recipe.get("Inputs", [])
			var req_count: int = int(inputs[0].get("Count", 1)) if not inputs.is_empty() else 1
			if int(inv.get("count", 0)) >= req_count:
				if not owned_items.has(idx):
					owned_items[idx] = []
				owned_items[idx].append({"uid": inv.get("item_uid", 0), "item_index": idx, "item_data": inv, "is_equip": false, "owned": true})

	var category_labels: Dictionary = {
		"Equipment": "可分解裝備",
		"Weapon": "可分解武器",
		"Material": "可分解材料",
	}
	var categories: Dictionary = {}
	for category: String in ["Equipment", "Weapon", "Material"]:
		var category_item: TreeItem = item_tree.create_item(root)
		category_item.set_text(0, category_labels[category])
		category_item.set_selectable(0, false)
		categories[category] = category_item

	var processed: int = 0
	var target_tree_item: TreeItem = null
	var first_available_tree_item: TreeItem = null
	for item_idx_value in dismantle_recipes.keys():
		var item_idx: int = int(item_idx_value)
		var recipe: Dictionary = dismantle_recipes[item_idx]
		var item_name: String = str(ItemLoader.get_item_property(item_idx, "item_name", "未知物品")) if ItemLoader else "未知物品"
		var category: String = str(recipe.get("Category", "Material"))
		if not categories.has(category):
			category = "Material"
		var is_equip: bool = category == "Equipment" or category == "Weapon"
		var rows: Array = owned_items.get(item_idx, [])
		if rows.is_empty():
			rows = [{"uid": 0, "item_index": item_idx, "item_data": {}, "is_equip": is_equip, "owned": false}]

		for metadata_value in rows:
			var metadata: Dictionary = metadata_value
			var display_name: String = item_name
			if bool(metadata.get("owned", false)):
				if bool(metadata.get("is_equip", false)):
					var item_data: Dictionary = metadata.get("item_data", {})
					var upgrade: int = int(item_data.get("Upgrade", item_data.get("upgrade", 0)))
					if upgrade > 0:
						display_name += " +%d" % upgrade
				else:
					var count: int = int(metadata.get("item_data", {}).get("count", 1))
					display_name += " (持有：%d)" % count

			var tree_item: TreeItem = item_tree.create_item(categories[category])
			tree_item.set_text(0, display_name)
			tree_item.set_metadata(0, metadata)
			if ItemLoader:
				var icon_id: int = int(ItemLoader.get_item_property(item_idx, "icon_id", 1)) - 1
				var icon_texture = ItemLoader.get_photo(icon_id)
				if icon_texture:
					var item_data: Dictionary = metadata.get("item_data", {})
					var color_index: int = int(item_data.get("Color", item_data.get("color", ItemLoader.get_item_property(item_idx, "color_index", 0))))
					tree_item.set_icon(0, ITEM_ICON_TINT.get_tinted_icon(icon_texture, color_index))
					tree_item.set_icon_max_width(0, 24)
			var available: bool = bool(metadata.get("owned", false))
			tree_item.set_custom_color(0, AVAILABLE_ITEM_COLOR if available else Color.GRAY)
			if available and first_available_tree_item == null:
				first_available_tree_item = tree_item
			if previously_selected_key != null and _get_selection_key(metadata) == previously_selected_key:
				target_tree_item = tree_item

			processed += 1
			if processed % 8 == 0:
				await get_tree().process_frame

	if target_tree_item:
		item_tree.set_block_signals(true)
		target_tree_item.select(0)
		item_tree.set_block_signals(false)
		item_tree.scroll_to_item(target_tree_item)
		_on_item_selected()
	elif previously_selected_key != null:
		_clear_right_panel()

func set_recipe_data(recipes: Array) -> void :
	dismantle_recipes.clear()
	for recipe in recipes:
		if recipe.get("ActionType", "") == "Dismantle":
			var inputs = recipe.get("Inputs", [])
			if inputs.size() > 0:
				var input_id = int(inputs[0].get("ItemID", 0))
				dismantle_recipes[input_id] = recipe
	_update_tree()


func _update_tree(filter_text: String = "", only_craftable: bool = false) -> void :
	var previously_selected_key = null
	var current_sel = item_tree.get_selected()
	if current_sel and current_sel.get_metadata(0):
		var m = current_sel.get_metadata(0)
		previously_selected_key = _get_selection_key(m)

	item_tree.clear()
	var root = item_tree.create_item()
	item_tree.hide_root = true

	var owned_items = {}

	if is_instance_valid(GameManager.current_player) and "data_model" in GameManager.current_player:
		var p_data = GameManager.current_player.data_model

		for eq in p_data.equip_list:
			if int(eq.get("in_bank", 0)) == 0 and int(eq.get("in_shop", 0)) == 0 and int(eq.get("part", 255)) == 255:
				var idx = int(eq.get("item_idx", 0))
				if dismantle_recipes.has(idx):
					if not owned_items.has(idx): owned_items[idx] = []
					owned_items[idx].append({"uid": eq.get("item_id", 0), "item_index": idx, "item_data": eq, "is_equip": true, "owned": true})

		for inv in p_data.inventory:
			if int(inv.get("in_bank", 0)) == 0 and int(inv.get("in_shop", 0)) == 0:
				var idx = int(inv.get("item_index", 0))
				if dismantle_recipes.has(idx):
					var recipe = dismantle_recipes[idx]
					var req_count = 1
					if recipe.get("Inputs", []).size() > 0:
						req_count = int(recipe.get("Inputs", [])[0].get("Count", 1))

					var current_count = int(inv.get("count", 0))

					if current_count >= req_count:
						if not owned_items.has(idx): owned_items[idx] = []
						owned_items[idx].append({"uid": inv.get("item_uid", 0), "item_index": idx, "item_data": inv, "is_equip": false, "owned": true})

	var visible_entries: Array[Dictionary] = []
	var category_counts: = {
		"Equipment": 0, 
		"Weapon": 0, 
		"Material": 0
	}

	for item_idx in dismantle_recipes.keys():
		var recipe = dismantle_recipes[item_idx]
		var item_name = ItemLoader.get_item_property(item_idx, "item_name", "未知物品") if ItemLoader else "未知物品"

		if filter_text != "" and item_name.find(filter_text) == -1:
			continue

		var cat_str: = str(recipe.get("Category", "Material"))
		if not category_counts.has(cat_str):
			cat_str = "Material"
		var is_equip = (cat_str == "Equipment" or cat_str == "Weapon")

		if owned_items.has(item_idx):
			for d_item in owned_items[item_idx]:
				var display_name = item_name
				if d_item.get("is_equip", false):
					var item_data: Dictionary = d_item.get("item_data", {})
					var upgrade: = int(item_data.get("Upgrade", item_data.get("upgrade", 0)))
					if upgrade > 0:
						display_name += " +%d" % upgrade
				else:
					var count = d_item.get("item_data", {}).get("count", 1)
					display_name += " (持有：%d)" % count
				visible_entries.append({
					"category": cat_str, 
					"item_index": int(item_idx), 
					"display_name": display_name, 
					"metadata": d_item, 
					"available": true
				})
				category_counts[cat_str] += 1
		else:
			if only_craftable:
				continue

			var d_item = {"uid": 0, "item_index": item_idx, "item_data": {}, "is_equip": is_equip, "owned": false}
			visible_entries.append({
				"category": cat_str, 
				"item_index": int(item_idx), 
				"display_name": item_name, 
				"metadata": d_item, 
				"available": false
			})
			category_counts[cat_str] += 1

	if visible_entries.is_empty():
		var empty_item: TreeItem = item_tree.create_item(root)
		empty_item.set_text(0, "沒有符合條件的分解項目")
		empty_item.set_selectable(0, false)
		empty_item.set_custom_color(0, Color(0.65, 0.72, 0.78, 1.0))
		_clear_right_panel()
		return

	var category_labels: = {
		"Equipment": "可分解裝備", 
		"Weapon": "可分解武器", 
		"Material": "可分解材料"
	}
	var categories: = {}
	for category in ["Equipment", "Weapon", "Material"]:
		if int(category_counts[category]) <= 0:
			continue
		var category_item: TreeItem = item_tree.create_item(root)
		category_item.set_text(0, category_labels[category])
		category_item.set_selectable(0, false)
		categories[category] = category_item

	var target_tree_item: TreeItem = null
	var first_available_tree_item: TreeItem = null
	for entry in visible_entries:
		var tree_item: TreeItem = item_tree.create_item(categories[entry["category"]])
		tree_item.set_text(0, entry["display_name"])
		tree_item.set_metadata(0, entry["metadata"])
		var metadata: Dictionary = entry["metadata"]

		if ItemLoader:
			var item_index: = int(entry["item_index"])
			var icon_id: = int(ItemLoader.get_item_property(item_index, "icon_id", 1)) - 1
			var icon_texture = ItemLoader.get_photo(icon_id)
			if icon_texture:
				var item_data: Dictionary = metadata.get("item_data", {})
				var color_index: = int(item_data.get("Color", item_data.get("color", ItemLoader.get_item_property(item_index, "color_index", 0))))
				tree_item.set_icon(0, ITEM_ICON_TINT.get_tinted_icon(icon_texture, color_index))
				tree_item.set_icon_max_width(0, 24)

		if not bool(entry["available"]):
			tree_item.set_custom_color(0, Color.GRAY)
		else:
			tree_item.set_custom_color(0, AVAILABLE_ITEM_COLOR)
			if first_available_tree_item == null:
				first_available_tree_item = tree_item

		var current_key = _get_selection_key(metadata)
		if previously_selected_key != null and current_key == previously_selected_key:
			target_tree_item = tree_item

	if target_tree_item:


		item_tree.set_block_signals(true)
		target_tree_item.select(0)
		item_tree.set_block_signals(false)
		item_tree.scroll_to_item(target_tree_item)
		_on_item_selected()
	elif only_craftable and first_available_tree_item:
		item_tree.set_block_signals(true)
		first_available_tree_item.select(0)
		item_tree.set_block_signals(false)
		item_tree.scroll_to_item(first_available_tree_item)
		_on_item_selected()
	elif previously_selected_key != null:
		_clear_right_panel()

func _get_selection_key(metadata: Dictionary) -> String:
	return "%d:%d:%d:%d" % [
		int(bool(metadata.get("owned", false))), 
		int(bool(metadata.get("is_equip", false))), 
		int(metadata.get("uid", 0)), 
		int(metadata.get("item_index", 0))
	]

func _on_item_selected() -> void :
	selected_uids.clear()
	var total_fee = 0
	var output_sums = {}

	var current_item = item_tree.get_selected()
	var first_meta = null

	if current_item:
		var meta = current_item.get_metadata(0)
		if meta:
			first_meta = meta
			var uid = int(meta.get("uid", 0))
			if bool(meta.get("owned", false)):
				selected_uids.append(uid)

			var item_idx = int(meta.get("item_index", 0))
			var recipe = dismantle_recipes.get(item_idx, {})

			total_fee += int(recipe.get("MoneyCost", 0))

			for out in recipe.get("Outputs", []):
				var out_id = int(out.get("ItemID", 0))
				var out_count = int(out.get("Count", 1))
				output_sums[out_id] = output_sums.get(out_id, 0) + out_count

	_refresh_right_panel(first_meta, output_sums, total_fee)

func _refresh_right_panel(first_meta, output_sums: Dictionary, total_fee: int) -> void :
	if not first_meta:
		_clear_right_panel()
		return

	var has_item = bool(first_meta.get("owned", false))

	btn_dismantle.disabled = not has_item or _request_cooldown_active
	lbl_fee.text = str(total_fee) + " 星幣"
	lbl_fee.add_theme_color_override("font_color", NORMAL_FEE_COLOR)
	if is_instance_valid(GameManager.current_player) and "data_model" in GameManager.current_player:
		if int(GameManager.current_player.data_model.money) < total_fee:
			lbl_fee.add_theme_color_override("font_color", INSUFFICIENT_FEE_COLOR)

	if has_item: lbl_selected_count.text = "1"
	else: lbl_selected_count.text = "尚未擁有此物品"

	lbl_success_rate.text = "100%"

	var item_idx = int(first_meta.get("item_index", 0))
	if ItemLoader:
		lbl_item_name.text = ItemLoader.get_item_property(item_idx, "item_name", "未知物品")
		var icon_id = ItemLoader.get_item_property(item_idx, "icon_id", 1) - 1
		preview_icon.texture = ItemLoader.get_photo(icon_id)

		var item_data = first_meta.get("item_data", {})
		var color_index = int(item_data.get("Color", item_data.get("color", ItemLoader.get_item_property(item_idx, "color_index", 0))))
		var effect_type = int(item_data.get("EffectType", item_data.get("effect_type", ItemLoader.get_item_property(item_idx, "effect_type", 0))))
		if color_index > 0 or effect_type > 0:
			var mat = ShaderMaterial.new()
			mat.shader = PALETTE_SHADER
			mat.set_shader_parameter("effect_type", effect_type)
			mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
			preview_icon.material = mat
		else:
			preview_icon.material = null

	for child in output_container.get_children():
		output_container.remove_child(child)
		child.queue_free()

	for out_id_raw in output_sums.keys():
		var out_id = int(out_id_raw)
		var sum_count = int(output_sums[out_id_raw])

		var vbox = VBoxContainer.new()
		var tex_rect = TextureRect.new()
		var lbl_count = Label.new()

		tex_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		tex_rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		tex_rect.custom_minimum_size = Vector2(40, 40)

		tex_rect.mouse_filter = Control.MOUSE_FILTER_PASS
		var show_tooltip = func(bound_id: int):
			if GameManager.current_hud and GameManager.current_hud.has_node("SharedTooltip"):
				var cat = ItemLoader.get_item_category(bound_id) if ItemLoader else 0
				var tab = 1 if cat == 1 else (3 if cat in [3, 4, 5] else 0)
				GameManager.current_hud.shared_tooltip.show_item(bound_id, tab, {})

		tex_rect.mouse_entered.connect(show_tooltip.bind(out_id))

		tex_rect.mouse_exited.connect( func():
			if GameManager.current_hud and GameManager.current_hud.has_node("SharedTooltip"):
				GameManager.current_hud.shared_tooltip.hide_tooltip()
		)

		if ItemLoader:
			var icon_id = ItemLoader.get_item_property(out_id, "icon_id", 1) - 1
			tex_rect.texture = ItemLoader.get_photo(icon_id)
			var color_index = ItemLoader.get_item_property(out_id, "color_index", 0)
			var effect_type = int(ItemLoader.get_item_property(out_id, "effect_type", 0))
			if color_index > 0 or effect_type > 0:
				var mat = ShaderMaterial.new()
				mat.shader = PALETTE_SHADER
				mat.set_shader_parameter("effect_type", effect_type)
				mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
				tex_rect.material = mat
			else:
				tex_rect.material = null

		lbl_count.text = "+ " + str(sum_count)
		lbl_count.add_theme_font_size_override("font_size", 14)
		lbl_count.add_theme_color_override("font_color", Color(0.0, 0.921, 0.46, 1.0))
		lbl_count.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER

		vbox.add_child(tex_rect)
		vbox.add_child(lbl_count)
		output_container.add_child(vbox)

func _on_search_text_changed(new_text: String) -> void :
	if new_text.is_empty():
		_search_debounce_timer.stop()
		_apply_current_filter()
	else:
		_search_debounce_timer.start()

func _on_filter_changed(_val = null) -> void :
	_search_debounce_timer.stop()
	_apply_current_filter()

func _apply_current_filter() -> void :
	var is_only_owned = check_craftable.button_pressed if check_craftable else false
	_update_tree(search_input.text, is_only_owned)

func _on_btn_dismantle_pressed() -> void :
	if selected_uids.is_empty():
		return
	var current_item = item_tree.get_selected()
	if not current_item:
		return
	var meta = current_item.get_metadata(0)
	if not meta:
		return

	var item_uid: = int(meta.get("uid", 0))
	if not bool(meta.get("owned", false)):
		return

	var item_type: = "Equipment" if bool(meta.get("is_equip", false)) else "Material"
	var item_index: = int(meta.get("item_index", 0))

	if item_type == "Equipment" and GameManager.current_hud and GameManager.current_hud.has_method("show_message_box"):
		var confirmation_message: = _build_dismantle_confirmation(meta)
		GameManager.current_hud.show_message_box(
			confirmation_message, 
			2, 
			func():
				_try_send_dismantle(item_uid, item_type, item_index), 
			Callable()
		)
		return

	_try_send_dismantle(item_uid, item_type, item_index)

func _build_dismantle_confirmation(meta: Dictionary) -> String:
	var item_index: = int(meta.get("item_index", 0))
	var item_name: String = ItemLoader.get_item_property(item_index, "item_name", "未知物品") if ItemLoader else "未知物品"
	var item_data: Dictionary = meta.get("item_data", {})
	var upgrade: = int(item_data.get("Upgrade", item_data.get("upgrade", 0)))
	if upgrade > 0:
		item_name += " +%d" % upgrade

	var recipe: Dictionary = dismantle_recipes.get(item_index, {})
	var output_lines: Array[String] = []
	for output in recipe.get("Outputs", []):
		var output_id: = int(output.get("ItemID", 0))
		var output_count: = int(output.get("Count", 1))
		var output_name: String = ItemLoader.get_item_property(output_id, "item_name", "未知物品") if ItemLoader else "未知物品"
		output_lines.append("%s ×%d" % [output_name, output_count])

	var output_text: = "無" if output_lines.is_empty() else "\n".join(output_lines)
	var fee: = int(recipe.get("MoneyCost", 0))
	return "確定要分解「%s」嗎？\n\n預期獲得：%s\n分解費用：%d 星幣" % [item_name, output_text, fee]

func _try_send_dismantle(item_uid: int, item_type: String, expected_item_index: int) -> void :
	var current_item_data: = _find_current_dismantle_item(item_uid, item_type)
	if current_item_data.is_empty() or int(current_item_data.get("item_index", 0)) != expected_item_index:
		if GameManager.current_hud:
			GameManager.current_hud.show_rejection_message_flash("物品狀態已變更，請重新選擇！")
		update_ui()
		return

	var recipe: Dictionary = dismantle_recipes.get(expected_item_index, {})
	if recipe.is_empty():
		if GameManager.current_hud:
			GameManager.current_hud.show_rejection_message_flash("找不到對應的分解配方！")
		update_ui()
		return

	var total_fee: = int(recipe.get("MoneyCost", 0))
	if GameManager.current_player.data_model.money < total_fee:
		if GameManager.current_hud:
			GameManager.current_hud.show_rejection_message_flash("手續費不足！")
		return

	selected_uids = [item_uid]
	btn_dismantle.disabled = true
	_request_cooldown_active = true
	_request_generation += 1
	var request_generation: = _request_generation
	if NetworkManager:
		NetworkManager.send_dismantle_item_request(selected_uids, item_type)
	_restore_dismantle_button_after_cooldown(request_generation, item_uid, item_type, expected_item_index)

func _restore_dismantle_button_after_cooldown(request_generation: int, item_uid: int, item_type: String, expected_item_index: int) -> void :
	await get_tree().create_timer(REQUEST_COOLDOWN_SECONDS).timeout
	if request_generation != _request_generation or not is_instance_valid(btn_dismantle):
		return
	_request_cooldown_active = false

	var selected_item: TreeItem = item_tree.get_selected()
	if not selected_item:
		btn_dismantle.disabled = true
		return
	var selected_meta = selected_item.get_metadata(0)
	if not selected_meta or not bool(selected_meta.get("owned", false)) or int(selected_meta.get("uid", 0)) != item_uid:
		btn_dismantle.disabled = true
		return

	var current_item_data: = _find_current_dismantle_item(item_uid, item_type)
	btn_dismantle.disabled = current_item_data.is_empty() or int(current_item_data.get("item_index", 0)) != expected_item_index

func _find_current_dismantle_item(item_uid: int, item_type: String) -> Dictionary:
	if not is_instance_valid(GameManager.current_player) or not "data_model" in GameManager.current_player:
		return {}

	var player_data = GameManager.current_player.data_model
	if item_type == "Equipment":
		for equip in player_data.equip_list:
			if int(equip.get("item_id", 0)) != item_uid:
				continue
			if int(equip.get("in_bank", 0)) != 0 or int(equip.get("in_shop", 0)) != 0 or int(equip.get("part", 255)) != 255:
				return {}
			return {
				"item_index": int(equip.get("item_idx", 0)), 
				"item_data": equip
			}
		return {}

	if item_type == "Material":
		for inventory_item in player_data.inventory:
			if int(inventory_item.get("item_uid", 0)) != item_uid:
				continue
			if int(inventory_item.get("in_bank", 0)) != 0 or int(inventory_item.get("in_shop", 0)) != 0:
				return {}
			var item_index: = int(inventory_item.get("item_index", 0))
			var recipe: Dictionary = dismantle_recipes.get(item_index, {})
			if recipe.is_empty():
				return {}
			var inputs: Array = recipe.get("Inputs", [])
			var required_count: = int(inputs[0].get("Count", 1)) if not inputs.is_empty() else 1
			if int(inventory_item.get("count", 0)) < required_count:
				return {}
			return {
				"item_index": item_index, 
				"item_data": inventory_item
			}

	return {}

func _on_dismantle_response(success: bool, msg: String, result_items: Array) -> void :
	if not _request_cooldown_active:
		btn_dismantle.disabled = false
	if success and GameManager.current_player and GameManager.current_player.has_method("play_effect"):
		GameManager.current_player.play_effect("CraftSuccess")
	await get_tree().process_frame
	update_ui()

func _clear_right_panel() -> void :
	lbl_item_name.text = "請選擇分解項目"
	lbl_fee.text = "0 星幣"
	lbl_fee.add_theme_color_override("font_color", NORMAL_FEE_COLOR)
	lbl_success_rate.text = "0.0%"
	lbl_selected_count.text = "0"
	preview_icon.texture = null
	preview_icon.material = null
	for child in output_container.get_children():
		output_container.remove_child(child)
		child.queue_free()
	selected_uids.clear()
	btn_dismantle.disabled = true

func update_ui() -> void :
	_search_debounce_timer.stop()
	_apply_current_filter()
	_on_item_selected()
