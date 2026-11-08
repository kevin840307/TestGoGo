
extends Control


@onready var search_input = $SearchInput
@onready var check_craftable = $CheckCraftable
@onready var recipe_tree = $RecipeTree

@onready var preview_icon = $PreviewIcon
@onready var lbl_item_name = $LblItemName
@onready var lbl_fee = $LblFee
@onready var lbl_success_rate = $LblSuccessRate
@onready var material_container = $MaterialContainer

@onready var spin_batch = $SpinBatch
@onready var btn_craft = $BtnCraft

const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const ITEM_ICON_TINT = preload("res://scripts/item_icon_tint.gd")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}
const AVAILABLE_ITEM_COLOR: = Color8(255, 240, 45)
const NORMAL_FEE_COLOR: = Color.WHITE
const INSUFFICIENT_FEE_COLOR: = Color.RED
const SEARCH_DEBOUNCE_SECONDS: = 0.15
const REQUEST_COOLDOWN_SECONDS: = 0.6
# The old fixed 24-recipe batch forced ~40-60 full frame waits even when
# recipes were cheap. Use a capped time budget plus a hard per-frame limit.
const STATIC_RECIPE_MAX_PER_FRAME: int = 64
const STATIC_RECIPE_FRAME_BUDGET_USEC: int = 2500

var all_recipes: Array = []
var current_selected_recipe: Dictionary = {}
var max_craftable_amount: int = 0
var _search_debounce_timer: Timer
var _request_generation: int = 0
var _request_cooldown_active: bool = false
var _static_tree_built: bool = false
var _tree_items_by_recipe_id: Dictionary = {}
var _recipe_entries: Array[Dictionary] = []
var _availability_refresh_generation: int = 0

func _ready() -> void :
	_search_debounce_timer = Timer.new()
	_search_debounce_timer.one_shot = true
	_search_debounce_timer.wait_time = SEARCH_DEBOUNCE_SECONDS
	add_child(_search_debounce_timer)
	_search_debounce_timer.timeout.connect(_apply_current_filter)
	search_input.text_changed.connect(_on_search_text_changed)
	check_craftable.toggled.connect(_on_filter_changed)
	recipe_tree.item_selected.connect(_on_recipe_selected)
	spin_batch.value_changed.connect(_on_spin_changed)
	btn_craft.pressed.connect(_on_btn_craft_pressed)

	if preview_icon:
		preview_icon.mouse_filter = Control.MOUSE_FILTER_PASS
		preview_icon.mouse_entered.connect(_on_preview_mouse_entered)
		preview_icon.mouse_exited.connect(_on_preview_mouse_exited)

	if NetworkManager.has_signal("craft_item_response_received"):
		NetworkManager.craft_item_response_received.connect(_on_craft_response)

	_clear_right_panel()

func _on_preview_mouse_entered() -> void :
	if current_selected_recipe.is_empty(): return
	var output_id = current_selected_recipe.get("Outputs", [])[0].get("ItemID", 0)
	if output_id <= 0: return

	var tab_type = 0
	if ItemLoader:
		var cat = ItemLoader.get_item_category(output_id)
		if cat == 1: tab_type = 1
		elif cat == 3 or cat == 4 or cat == 5: tab_type = 3

	if GameManager and GameManager.current_hud and GameManager.current_hud.has_node("SharedTooltip"):
		GameManager.current_hud.shared_tooltip.show_item(output_id, tab_type, {})

func _on_preview_mouse_exited() -> void :
	if GameManager and GameManager.current_hud and GameManager.current_hud.has_node("SharedTooltip"):
		GameManager.current_hud.shared_tooltip.hide_tooltip()

func set_recipe_data(recipes: Array) -> void :
	all_recipes = recipes
	if _static_tree_built:
		refresh_availability_async()
	else:
		_update_tree()

static func should_yield_static_recipe_batch(processed_since_yield: int, elapsed_usec: int) -> bool:
	return processed_since_yield >= STATIC_RECIPE_MAX_PER_FRAME or elapsed_usec >= STATIC_RECIPE_FRAME_BUDGET_USEC


static func recipe_preload_progress(completed: int, total: int) -> float:
	return clampf(float(completed) / float(maxi(total, 1)), 0.0, 1.0)


func prepare_static_tree_async(recipes: Array, progress_callback: Callable = Callable()) -> void :
	all_recipes = recipes
	_availability_refresh_generation += 1
	_static_tree_built = false
	_tree_items_by_recipe_id.clear()
	_recipe_entries.clear()
	recipe_tree.clear()
	var root: TreeItem = recipe_tree.create_item()
	recipe_tree.hide_root = true

	var category_labels: Dictionary = {
		"Equipment": "裝備合成",
		"Weapon": "武器合成",
		"Material": "材料合成",
	}
	var categories: Dictionary = {}
	for category: String in ["Equipment", "Weapon", "Material"]:
		var category_item: TreeItem = recipe_tree.create_item(root)
		category_item.set_text(0, category_labels[category])
		category_item.set_selectable(0, false)
		categories[category] = category_item

	var processed: int = 0
	var processed_since_yield: int = 0
	var recipe_index: int = 0
	var total_recipes: int = all_recipes.size()
	var batch_started_usec: int = Time.get_ticks_usec()
	for recipe_value in all_recipes:
		recipe_index += 1
		var recipe: Dictionary = recipe_value
		if str(recipe.get("ActionType", "")) != "Craft":
			continue
		var outputs: Array = recipe.get("Outputs", [])
		if outputs.is_empty():
			continue
		var output_id: int = int(outputs[0].get("ItemID", 0))
		var item_name: String = str(ItemLoader.get_item_property(output_id, "item_name", "未知物品")) if ItemLoader else "未知物品"
		var category: String = str(recipe.get("Category", "Material"))
		if not categories.has(category):
			category = "Material"
		var tree_item: TreeItem = recipe_tree.create_item(categories[category])
		tree_item.set_text(0, item_name)
		tree_item.set_metadata(0, recipe)
		tree_item.set_custom_color(0, Color.GRAY)
		if ItemLoader:
			var icon_id: int = int(ItemLoader.get_item_property(output_id, "icon_id", 1)) - 1
			var icon_texture = ItemLoader.get_photo(icon_id)
			if icon_texture:
				var color_index: int = int(ItemLoader.get_item_property(output_id, "color_index", 0))
				tree_item.set_icon(0, ITEM_ICON_TINT.get_tinted_icon(icon_texture, color_index))
				tree_item.set_icon_max_width(0, 24)
		var recipe_id: int = int(recipe.get("RecipeID", -1))
		_tree_items_by_recipe_id[recipe_id] = tree_item
		_recipe_entries.append({
			"recipe": recipe,
			"item_name": item_name,
			"recipe_id": recipe_id,
		})
		processed += 1
		processed_since_yield += 1
		if should_yield_static_recipe_batch(
			processed_since_yield,
			Time.get_ticks_usec() - batch_started_usec
		):
			# Emit a precise fraction at existing frame boundaries. The UI
			# never reports success/100% until all recipe rows are built.
			if progress_callback.is_valid():
				progress_callback.call(recipe_preload_progress(recipe_index, total_recipes))
			await get_tree().process_frame
			processed_since_yield = 0
			batch_started_usec = Time.get_ticks_usec()

	_static_tree_built = true
	if progress_callback.is_valid():
		progress_callback.call(1.0)

func _build_inventory_count_cache() -> Dictionary:
	var counts: Dictionary = {}
	if not is_instance_valid(GameManager.current_player) or not "data_model" in GameManager.current_player:
		return counts
	for item_value in GameManager.current_player.data_model.inventory:
		var item: Dictionary = item_value
		if int(item.get("in_bank", 0)) != 0 or int(item.get("in_shop", 0)) != 0:
			continue
		var item_id: int = int(item.get("item_index", 0))
		counts[item_id] = int(counts.get(item_id, 0)) + int(item.get("count", 0))
	return counts

func _calculate_max_craftable_cached(recipe: Dictionary, counts: Dictionary, money: int) -> int:
	var max_craft: int = 9999
	var cost: int = int(recipe.get("MoneyCost", 0))
	if cost > 0:
		max_craft = mini(max_craft, int(money / cost))
	# Ingredient dictionaries are read-only here. Most recipes have no
	# blueprint, so avoid recursively cloning all inputs on every availability
	# refresh. When a blueprint is present, copy only the outer array before
	# appending the virtual ingredient.
	var inputs: Array = recipe.get("Inputs", [])
	if int(recipe.get("BlueprintID", 0)) > 0:
		inputs = inputs.duplicate()
		inputs.append({"ItemID": recipe.get("BlueprintID"), "Count": 1})
	for req_value in inputs:
		var req: Dictionary = req_value
		var need: int = int(req.get("Count", 1))
		if need <= 0:
			continue
		var has_count: int = int(counts.get(int(req.get("ItemID", 0)), 0))
		max_craft = mini(max_craft, int(has_count / need))
	return maxi(max_craft, 0)

func refresh_availability_async() -> void :
	if not _static_tree_built:
		# Search/filters rebuild a different Tree. Preserve that filter and
		# never use stale cached TreeItem references from the original layout.
		_apply_current_filter()
		return
	_availability_refresh_generation += 1
	var this_generation: int = _availability_refresh_generation
	var counts: Dictionary = _build_inventory_count_cache()
	var money: int = int(GameManager.current_player.data_model.money) if is_instance_valid(GameManager.current_player) else 0
	var processed: int = 0
	for entry_value in _recipe_entries:
		var entry: Dictionary = entry_value
		var recipe: Dictionary = entry["recipe"]
		var recipe_id: int = int(entry["recipe_id"])
		var tree_item: TreeItem = _tree_items_by_recipe_id.get(recipe_id, null) as TreeItem
		if tree_item == null:
			continue
		var max_can_do: int = _calculate_max_craftable_cached(recipe, counts, money)
		var display_name: String = str(entry["item_name"])
		if max_can_do > 0:
			display_name += " (可製作：%d)" % max_can_do
		tree_item.set_text(0, display_name)
		tree_item.set_custom_color(0, AVAILABLE_ITEM_COLOR if max_can_do > 0 else Color.GRAY)
		processed += 1
		if processed % 64 == 0:
			await get_tree().process_frame
			if this_generation != _availability_refresh_generation or not is_inside_tree():
				return

func _update_tree(filter_text: String = "", only_craftable: bool = false) -> void :
	# Once a filtered/dynamic Tree replaces the static default Tree,
	# references saved during first-open preparation are invalidated.
	_availability_refresh_generation += 1
	_static_tree_built = false
	_tree_items_by_recipe_id.clear()
	_recipe_entries.clear()
	recipe_tree.clear()
	var root = recipe_tree.create_item()
	recipe_tree.hide_root = true

	var visible_recipes: Array[Dictionary] = []
	var category_counts: = {
		"Equipment": 0, 
		"Weapon": 0, 
		"Material": 0
	}


	for recipe in all_recipes:
		if recipe.get("ActionType", "") != "Craft":
			continue

		var output_id = int(recipe.get("Outputs", [])[0].get("ItemID", 0))
		var item_name = ItemLoader.get_item_property(output_id, "item_name", "未知物品") if ItemLoader else "未知物品"
		if filter_text != "" and item_name.find(filter_text) == -1:
			continue

		var max_can_do: = _calculate_max_craftable(recipe)
		if only_craftable and max_can_do <= 0:
			continue

		var category: = str(recipe.get("Category", "Material"))
		if not category_counts.has(category):
			category = "Material"
		category_counts[category] += 1
		visible_recipes.append({
			"recipe": recipe, 
			"output_id": output_id, 
			"item_name": item_name, 
			"max_can_do": max_can_do, 
			"category": category
		})

	if visible_recipes.is_empty():
		var empty_item: TreeItem = recipe_tree.create_item(root)
		empty_item.set_text(0, "沒有符合條件的合成項目")
		empty_item.set_selectable(0, false)
		empty_item.set_custom_color(0, Color(0.65, 0.72, 0.78, 1.0))
		current_selected_recipe = {}
		_clear_right_panel()
		return

	var category_labels: = {
		"Equipment": "裝備合成", 
		"Weapon": "武器合成", 
		"Material": "材料合成"
	}
	var categories: = {}
	for category in ["Equipment", "Weapon", "Material"]:
		if int(category_counts[category]) <= 0:
			continue
		var category_item: TreeItem = recipe_tree.create_item(root)
		category_item.set_text(0, category_labels[category])
		category_item.set_selectable(0, false)
		categories[category] = category_item

	var target_tree_item: TreeItem = null
	for entry in visible_recipes:
		var recipe: Dictionary = entry["recipe"]
		var output_id: int = entry["output_id"]
		var item_name: String = entry["item_name"]
		var max_can_do: int = entry["max_can_do"]
		var parent_item: TreeItem = categories[entry["category"]]
		var tree_item = recipe_tree.create_item(parent_item)
		var display_name: = item_name
		if max_can_do > 0:
			display_name += " (可製作：%d)" % max_can_do
		tree_item.set_text(0, display_name)
		tree_item.set_metadata(0, recipe)

		if ItemLoader:
			var icon_id: = int(ItemLoader.get_item_property(output_id, "icon_id", 1)) - 1
			var icon_texture = ItemLoader.get_photo(icon_id)
			if icon_texture:
				var color_index: = int(ItemLoader.get_item_property(output_id, "color_index", 0))
				tree_item.set_icon(0, ITEM_ICON_TINT.get_tinted_icon(icon_texture, color_index))
				tree_item.set_icon_max_width(0, 24)

		if max_can_do <= 0:
			tree_item.set_custom_color(0, Color.GRAY)
		else:
			tree_item.set_custom_color(0, AVAILABLE_ITEM_COLOR)

		if not current_selected_recipe.is_empty():
			if recipe.get("RecipeID", -1) == current_selected_recipe.get("RecipeID", -2):
				target_tree_item = tree_item

	if target_tree_item:
		target_tree_item.select(0)
		recipe_tree.scroll_to_item(target_tree_item)
	elif not current_selected_recipe.is_empty():
		current_selected_recipe = {}
		_clear_right_panel()

func _on_recipe_selected() -> void :
	var selected_item = recipe_tree.get_selected()
	if not selected_item or selected_item.get_metadata(0) == null: return
	current_selected_recipe = selected_item.get_metadata(0)
	_refresh_right_panel()

func _refresh_right_panel() -> void :
	if current_selected_recipe.is_empty(): return
	btn_craft.disabled = _request_cooldown_active
	var output_id = current_selected_recipe.get("Outputs", [])[0].get("ItemID", 0)

	if ItemLoader:
		lbl_item_name.text = ItemLoader.get_item_property(output_id, "item_name", "未知物品")
		var icon_id = ItemLoader.get_item_property(output_id, "icon_id", 1) - 1
		preview_icon.texture = ItemLoader.get_photo(icon_id)

		var color_index = ItemLoader.get_item_property(output_id, "color_index", 0)
		var effect_type = int(ItemLoader.get_item_property(output_id, "effect_type", 0))
		if color_index > 0 or effect_type > 0:
			var mat = ShaderMaterial.new()
			mat.shader = PALETTE_SHADER
			mat.set_shader_parameter("effect_type", effect_type)
			mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
			preview_icon.material = mat
		else:
			preview_icon.material = null

	lbl_success_rate.text = str(current_selected_recipe.get("SuccessRate", 100)) + "%"

	max_craftable_amount = _calculate_max_craftable(current_selected_recipe)
	if max_craftable_amount > 0:
		spin_batch.max_value = max_craftable_amount
		spin_batch.min_value = 1
		spin_batch.set_value_no_signal(1)
	else:
		spin_batch.min_value = 1
		spin_batch.max_value = 1
		spin_batch.set_value_no_signal(1)
		btn_craft.disabled = true

	_on_spin_changed(spin_batch.value)

func _draw_materials(batch_amount: int = 1) -> void :
	for child in material_container.get_children():
		child.queue_free()

	batch_amount = maxi(batch_amount, 1)
	var inputs = current_selected_recipe.get("Inputs", []).duplicate(true)
	if current_selected_recipe.get("BlueprintID", 0) > 0:
		inputs.append({"ItemID": current_selected_recipe.get("BlueprintID"), "Count": 1})

	for req in inputs:
		var req_id = int(req.get("ItemID", 0))
		var req_count = int(req.get("Count", 1)) * batch_amount
		var has_count = _get_player_item_count(req_id)

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

		tex_rect.mouse_entered.connect(show_tooltip.bind(req_id))
		tex_rect.mouse_exited.connect( func():
			if GameManager.current_hud and GameManager.current_hud.has_node("SharedTooltip"):
				GameManager.current_hud.shared_tooltip.hide_tooltip()
		)

		if ItemLoader:
			var icon_id = ItemLoader.get_item_property(req_id, "icon_id", 1) - 1
			tex_rect.texture = ItemLoader.get_photo(icon_id)
			var color_index = ItemLoader.get_item_property(req_id, "color_index", 0)
			var effect_type = int(ItemLoader.get_item_property(req_id, "effect_type", 0))
			if color_index > 0 or effect_type > 0:
				var mat = ShaderMaterial.new()
				mat.shader = PALETTE_SHADER
				mat.set_shader_parameter("effect_type", effect_type)
				mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
				tex_rect.material = mat
			else:
				tex_rect.material = null

		lbl_count.text = str(int(has_count)) + " / " + str(int(req_count))
		lbl_count.add_theme_font_size_override("font_size", 14)
		lbl_count.add_theme_color_override("font_color", Color(0.0, 0.921, 0.46, 1.0) if has_count >= req_count else Color(1.0, 0.142, 0.173, 1.0))
		lbl_count.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER

		vbox.add_child(tex_rect)
		vbox.add_child(lbl_count)
		material_container.add_child(vbox)

func _calculate_max_craftable(recipe: Dictionary) -> int:
	var counts: Dictionary = _build_inventory_count_cache()
	var money: int = int(GameManager.current_player.data_model.money) if is_instance_valid(GameManager.current_player) else 0
	return _calculate_max_craftable_cached(recipe, counts, money)

func _get_player_item_count(item_id: int) -> int:
	var count = 0
	if GameManager.current_player and "data_model" in GameManager.current_player:
		var inv = GameManager.current_player.data_model.inventory
		for item in inv:
			if item.get("item_index", 0) == item_id and item.get("in_bank", 0) == 0 and item.get("in_shop", 0) == 0:
				count += item.get("count", 0)
	return count

func _on_spin_changed(value: float) -> void :
	var batch_amount: = maxi(int(value), 1)
	var total_fee: = int(current_selected_recipe.get("MoneyCost", 0)) * batch_amount
	lbl_fee.text = str(total_fee) + " 星幣"
	lbl_fee.add_theme_color_override("font_color", NORMAL_FEE_COLOR)
	if is_instance_valid(GameManager.current_player) and "data_model" in GameManager.current_player:
		if int(GameManager.current_player.data_model.money) < total_fee:
			lbl_fee.add_theme_color_override("font_color", INSUFFICIENT_FEE_COLOR)
	_draw_materials(batch_amount)

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
	_update_tree(search_input.text, check_craftable.button_pressed)

func _on_btn_craft_pressed() -> void :
	if current_selected_recipe.is_empty(): return
	var recipe_id = current_selected_recipe.get("RecipeID", 0)
	var amount = int(spin_batch.value)
	btn_craft.disabled = true
	_request_cooldown_active = true
	_request_generation += 1
	var request_generation: = _request_generation
	if NetworkManager:
		NetworkManager.send_craft_item_request(recipe_id, amount)
	_restore_craft_button_after_cooldown(request_generation)

func _restore_craft_button_after_cooldown(request_generation: int) -> void :
	await get_tree().create_timer(REQUEST_COOLDOWN_SECONDS).timeout
	if request_generation != _request_generation or not is_instance_valid(btn_craft):
		return
	_request_cooldown_active = false
	btn_craft.disabled = current_selected_recipe.is_empty() or _calculate_max_craftable(current_selected_recipe) <= 0

func _on_craft_response(success: bool, msg: String, result_items: Array) -> void :
	if not _request_cooldown_active:
		btn_craft.disabled = false
	if success and GameManager.current_player and GameManager.current_player.has_method("play_effect"):
		GameManager.current_player.play_effect("CraftSuccess")
	await get_tree().process_frame
	update_ui()

func _clear_right_panel() -> void :
	lbl_item_name.text = "請選擇合成項目"
	lbl_fee.text = "0 星幣"
	lbl_fee.add_theme_color_override("font_color", NORMAL_FEE_COLOR)
	lbl_success_rate.text = "0.0%"
	preview_icon.texture = null
	preview_icon.material = null
	for child in material_container.get_children():
		child.queue_free()
	spin_batch.min_value = 1
	spin_batch.max_value = 1
	spin_batch.set_value_no_signal(1)
	btn_craft.disabled = true

func update_ui() -> void :
	_search_debounce_timer.stop()
	# Most opens and inventory changes use the default list. Recompute only
	# counts/colors in small batches; don't allocate a fresh 1453-row Tree
	# (and new icons) every time the player toggles the crafting window.
	if _static_tree_built and search_input.text.is_empty() and not check_craftable.button_pressed:
		refresh_availability_async()
	else:
		_apply_current_filter()
	_refresh_right_panel()
