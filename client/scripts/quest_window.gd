
extends Control

const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}


@onready var btn_close: BaseButton = $Background / BtnClose
@onready var btn_abandon: Button = $Background / BtnAbandon


@onready var list_available: VBoxContainer = $Background / ScrollAvailable / ListAvailable
@onready var list_in_progress: VBoxContainer = $Background / ScrollInProgress / ListInProgress
@onready var list_completed: VBoxContainer = $Background / ScrollCompleted / ListCompleted
@onready var list_steps: VBoxContainer = $Background / ScrollSteps / ListSteps
@onready var list_rewards: VBoxContainer = $Background / ScrollRewards / ListRewards

var target_player: CharacterBody2D = null
var selected_quest_id: int = -1
var _palette_material_cache: Dictionary = {}

func _ready() -> void :
	if btn_close:
		btn_close.pressed.connect(_close_window)
	if btn_abandon:
		btn_abandon.pressed.connect(_on_btn_abandon_pressed)
		btn_abandon.visible = false
	visibility_changed.connect(_on_visibility_changed)

func setup(player: CharacterBody2D) -> void :
	target_player = player

func update_ui() -> void :
	if not is_instance_valid(target_player) or not "data_model" in target_player: return
	var p_data = target_player.data_model
	if not p_data: return

	_clear_container(list_available)
	_clear_container(list_in_progress)
	_clear_container(list_completed)

	var active_quests = p_data.player_quests


	for q_id in QuestLoader.quest_database.keys():
		var config = QuestLoader.get_quest_config(q_id)
		var user_quest = active_quests.get(q_id, null)

		var status = 0
		if user_quest:
			status = int(user_quest.get("Status", user_quest.get("status", 0)))

		match status:
			0:
				if _can_player_accept_quest(config, p_data):
					_add_quest_row(list_available, q_id, config.get("Title", "未命名任務"), status)
			1, 2:
				var title_str = config.get("Title", "未命名任務")
				if status == 2:
					title_str += " (可回報)"
				_add_quest_row(list_in_progress, q_id, title_str, status)
			3:
				_add_quest_row(list_completed, q_id, config.get("Title", "未命名任務"), status)


	if selected_quest_id > 0:
		_show_quest_detail(selected_quest_id)
	else:
		_clear_container(list_steps)
		_clear_container(list_rewards)
		if btn_abandon: btn_abandon.visible = false

func _add_quest_row(container: VBoxContainer, quest_id: int, title_text: String, status: int) -> void :
	var btn = Button.new()


	var prefix_icon = ""
	match status:
		0:
			prefix_icon = "🔒 "
		1, 2:
			prefix_icon = "📋 "
		3:
			prefix_icon = "✅ "

	btn.text = prefix_icon + title_text

	btn.custom_minimum_size = Vector2(0, 30)
	btn.alignment = HORIZONTAL_ALIGNMENT_CENTER
	btn.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND


	btn.add_theme_font_size_override("font_size", 15)
	if status == 2:
		btn.add_theme_color_override("font_color", Color(1.0, 0.941, 0.176, 1.0))
	else:
		btn.add_theme_color_override("font_color", Color.WHITE)

	var style_normal = StyleBoxFlat.new()
	style_normal.bg_color = Color(0.0, 0.0, 0.0, 0.0)
	btn.add_theme_stylebox_override("normal", style_normal)

	var style_hover = style_normal.duplicate()
	style_hover.bg_color = Color(0.0, 0.0, 0.0, 0.196)
	btn.add_theme_stylebox_override("hover", style_hover)

	btn.pressed.connect( func():
		selected_quest_id = quest_id
		_show_quest_detail(quest_id)
	)

	container.add_child(btn)


func _show_quest_detail(quest_id: int) -> void :
	_hide_item_tooltip()
	_clear_container(list_steps)
	_clear_container(list_rewards)

	var config = QuestLoader.get_quest_config(quest_id)
	if config.is_empty(): return

	var active_quests = target_player.data_model.player_quests
	var user_quest = active_quests.get(quest_id, {})
	var status = int(user_quest.get("Status", user_quest.get("status", 0)))
	var current_step_idx = int(user_quest.get("CurrentStep", user_quest.get("current_step", 0)))
	var step_progress = int(user_quest.get("StepProgress", user_quest.get("step_progress", 0)))


	var steps = config.get("Steps", [])
	for i in range(steps.size()):
		var step_cfg = steps[i]
		var step_desc = step_cfg.get("StepDesc", "流程目標")
		var target_count = int(step_cfg.get("TargetCount", 1))

		var step_box = HBoxContainer.new()
		step_box.custom_minimum_size = Vector2(0, 26)

		var lbl_num = Label.new()
		lbl_num.text = "Step %d. " % (i + 1)
		lbl_num.add_theme_font_size_override("font_size", 15)
		step_box.add_child(lbl_num)

		var lbl_info = Label.new()
		var step_text = step_desc


		if status == 3:
			step_text += " [已完成]"
			lbl_info.add_theme_color_override("font_color", Color(0.162, 0.983, 0.697, 1.0))
		elif status == 1 or status == 2:
			if i < current_step_idx:
				step_text += " [已完成]"
				lbl_info.add_theme_color_override("font_color", Color(0.162, 0.983, 0.697, 1.0))
			elif i == current_step_idx:
				if target_count > 1:
					step_text += " (%d / %d)" % [step_progress, target_count]
				step_text += " [進行中]"
				lbl_info.add_theme_color_override("font_color", Color(1.0, 0.941, 0.176, 1.0))
			else:
				lbl_info.add_theme_color_override("font_color", Color.WHITE)
		else:
			lbl_info.add_theme_color_override("font_color", Color.WHITE)

		lbl_info.text = step_text
		lbl_info.add_theme_font_size_override("font_size", 15)


		lbl_info.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		lbl_info.size_flags_horizontal = Control.SIZE_EXPAND_FILL

		step_box.add_child(lbl_info)

		list_steps.add_child(step_box)


	var rewards = config.get("Rewards", {})
	if not rewards.is_empty():
		var text_rewards: Array[String] = []

		var exp_reward = int(rewards.get("Exp", 0))
		if exp_reward > 0:
			text_rewards.append("經驗值：" + str(exp_reward) + " 點")

		var money_reward = int(rewards.get("Money", 0))
		if money_reward > 0:
			text_rewards.append("星幣：" + str(money_reward) + " 元")

		var custom_texts = rewards.get("CustomTexts", [])
		for text_str in custom_texts:
			text_rewards.append(str(text_str))

		if not text_rewards.is_empty():
			_add_reward_section_title("其他獎勵", Color(1.0, 0.941, 0.176, 1.0))
			for text_str in text_rewards:
				_add_reward_label("• " + text_str)

		var fixed_items: Array = []
		fixed_items.append_array(rewards.get("Items", []))
		fixed_items.append_array(rewards.get("AdditionalDisplayItems", []))
		_add_item_reward_section("獲得", fixed_items, Color(0.95, 0.72, 0.25, 1.0))

		for group in rewards.get("SelectableItemGroups", []):
			var select_count = maxi(1, int(group.get("SelectCount", 1)))
			var title = "自選一種" if select_count == 1 else "選擇其中 %d 項" % select_count
			_add_item_reward_section(title, group.get("Items", []), Color(0.58, 0.48, 1.0, 1.0))

		for group in rewards.get("RandomItemGroups", []):
			var roll_count = maxi(1, int(group.get("RollCount", 1)))
			var title = "隨機獲得" if roll_count == 1 else "隨機獲得其中 %d 項" % roll_count
			_add_item_reward_section(title, group.get("Items", []), Color(0.3, 0.78, 0.96, 1.0))


	if btn_abandon:
		var category = int(config.get("Category", 0))
		btn_abandon.visible = (status == 1 or status == 2) and category != 0

func _add_reward_label(text_str: String) -> void :
	var lbl = Label.new()
	lbl.text = text_str
	lbl.add_theme_font_size_override("font_size", 15)
	lbl.add_theme_color_override("font_color", Color(1.0, 0.941, 0.176, 1.0))
	lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	list_rewards.add_child(lbl)

func _add_reward_section_title(title_text: String, accent_color: Color) -> void :
	var lbl = Label.new()
	lbl.text = title_text
	lbl.custom_minimum_size = Vector2(0, 24)
	lbl.add_theme_font_size_override("font_size", 16)
	lbl.add_theme_color_override("font_color", accent_color)
	list_rewards.add_child(lbl)

func _add_item_reward_section(title_text: String, items: Array, accent_color: Color) -> void :
	if items.is_empty():
		return

	_add_reward_section_title(title_text, accent_color)

	var grid = GridContainer.new()
	grid.columns = 4
	grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_theme_constant_override("h_separation", 10)
	grid.add_theme_constant_override("v_separation", 10)
	list_rewards.add_child(grid)

	for item in items:
		var item_id = int(item.get("ItemID", item.get("item_id", item.get("ItemIndex", item.get("item_idx", 0)))))
		var count = int(item.get("Count", item.get("count", 1)))
		if item_id <= 0 or count <= 0:
			continue
		grid.add_child(_create_reward_item_card(item_id, count, accent_color))

func _create_reward_item_card(item_id: int, count: int, accent_color: Color) -> Control:
	var card = Control.new()
	card.custom_minimum_size = Vector2(92, 82)
	card.mouse_default_cursor_shape = Control.CURSOR_HELP
	card.mouse_filter = Control.MOUSE_FILTER_STOP

	var background = Panel.new()
	background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	background.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var normal_style = StyleBoxFlat.new()
	normal_style.bg_color = Color(0.055, 0.065, 0.09, 0.92)
	normal_style.border_width_left = 1
	normal_style.border_width_top = 1
	normal_style.border_width_right = 1
	normal_style.border_width_bottom = 1
	normal_style.border_color = Color(accent_color, 0.68)
	normal_style.corner_radius_top_left = 7
	normal_style.corner_radius_top_right = 7
	normal_style.corner_radius_bottom_right = 7
	normal_style.corner_radius_bottom_left = 7
	background.add_theme_stylebox_override("panel", normal_style)
	card.add_child(background)

	var photo = TextureRect.new()
	photo.position = Vector2(22, 6)
	photo.size = Vector2(48, 48)
	photo.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	photo.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	photo.mouse_filter = Control.MOUSE_FILTER_IGNORE
	photo.texture = _get_reward_item_texture(item_id)
	var color_index = int(ItemLoader.get_item_property(item_id, "color_index", 0)) if ItemLoader else 0
	var effect_type = int(ItemLoader.get_item_property(item_id, "effect_type", 0)) if ItemLoader else 0
	photo.material = _get_palette_material(color_index, effect_type)
	card.add_child(photo)

	var item_name = Label.new()
	item_name.position = Vector2(4, 62)
	item_name.size = Vector2(84, 18)
	item_name.text = _get_reward_item_name(item_id)
	item_name.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	item_name.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	item_name.add_theme_font_size_override("font_size", 13)
	item_name.add_theme_color_override("font_color", Color(0.92, 0.94, 1.0, 1.0))
	item_name.mouse_filter = Control.MOUSE_FILTER_IGNORE
	card.add_child(item_name)

	if count > 1:
		var count_badge = Label.new()
		count_badge.set_anchors_preset(Control.PRESET_BOTTOM_RIGHT)
		count_badge.offset_left = -52.0
		count_badge.offset_top = -46.0
		count_badge.offset_right = -22.0
		count_badge.offset_bottom = -28.0
		count_badge.text = "×" + str(count)
		count_badge.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		count_badge.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		count_badge.add_theme_font_size_override("font_size", 13)
		count_badge.add_theme_color_override("font_color", Color.WHITE)
		count_badge.add_theme_constant_override("outline_size", 3)
		count_badge.add_theme_color_override("font_outline_color", Color(0.0, 0.0, 0.0, 0.9))
		count_badge.mouse_filter = Control.MOUSE_FILTER_IGNORE
		card.add_child(count_badge)

	card.mouse_entered.connect(_on_reward_card_mouse_entered.bind(card, background, normal_style, accent_color, item_id))
	card.mouse_exited.connect(_on_reward_card_mouse_exited.bind(background, normal_style))
	return card

func _get_reward_item_texture(item_id: int) -> Texture2D:
	if not ItemLoader:
		return null
	var icon_id = int(ItemLoader.get_item_property(item_id, "icon_id", 0))
	var texture: Texture2D = null
	if icon_id > 0:
		texture = ItemLoader.get_photo(icon_id - 1)
		if not texture:
			texture = ItemLoader.get_icon(icon_id)
	return texture

func _get_palette_material(color_index: int, effect_type: int) -> Material:
	if color_index <= 0 and effect_type <= 0:
		return null
	var cache_key = str(color_index) + "_" + str(effect_type)
	if _palette_material_cache.has(cache_key):
		return _palette_material_cache[cache_key]
	var palette_material = ShaderMaterial.new()
	palette_material.shader = PALETTE_SHADER
	palette_material.set_shader_parameter("effect_type", effect_type)
	palette_material.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
	_palette_material_cache[cache_key] = palette_material
	return palette_material

func _get_reward_item_name(item_id: int) -> String:
	if not ItemLoader:
		return "未知物品"
	var item_name = str(ItemLoader.get_item_property(item_id, "item_name", ""))
	return item_name if not item_name.is_empty() else "未知物品"

func _on_reward_card_mouse_entered(_card: Control, background: Panel, normal_style: StyleBoxFlat, accent_color: Color, item_id: int) -> void :
	var hover_style = normal_style.duplicate() as StyleBoxFlat
	hover_style.bg_color = Color(0.1, 0.12, 0.17, 0.98)
	hover_style.border_width_left = 2
	hover_style.border_width_top = 2
	hover_style.border_width_right = 2
	hover_style.border_width_bottom = 2
	hover_style.border_color = accent_color
	background.add_theme_stylebox_override("panel", hover_style)
	_show_item_tooltip(item_id)

func _on_reward_card_mouse_exited(background: Panel, normal_style: StyleBoxFlat) -> void :
	background.add_theme_stylebox_override("panel", normal_style)
	_hide_item_tooltip()

func _show_item_tooltip(item_id: int) -> void :
	if not GameManager.current_hud or not GameManager.current_hud.get("shared_tooltip"):
		return
	var category = ItemLoader.get_item_category(item_id) if ItemLoader else -1
	var tab_type = 0
	if category == 1:
		tab_type = 1
	elif category == 3 or category == 4:
		tab_type = 3
	GameManager.current_hud.shared_tooltip.show_item(item_id, tab_type, {})

func _hide_item_tooltip() -> void :
	if GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):
		GameManager.current_hud.shared_tooltip.hide_tooltip()

func _close_window() -> void :
	_hide_item_tooltip()
	visible = false

func _on_visibility_changed() -> void :
	if not visible:
		_hide_item_tooltip()

func _on_btn_abandon_pressed() -> void :
	if selected_quest_id > 0 and NetworkManager:
		NetworkManager.send_secure_packet({
			"Type": "AbandonQuest", 
			"QuestID": selected_quest_id
		})

func _can_player_accept_quest(config: Dictionary, p_data: PlayerData) -> bool:

	var min_lv = int(config.get("MinLevel", 1))
	var max_lv = int(config.get("MaxLevel", 99))
	if p_data.level < min_lv or p_data.level > max_lv:
		return false


	var req_race = config.get("RequiredRace", [])
	if req_race.size() > 0:
		var match_race = false
		for r in req_race:
			if int(r) == p_data.race_id:
				match_race = true
				break
		if not match_race:
			return false


	var req_career = config.get("RequiredCareer", [])
	if req_career.size() > 0:
		var match_career = false
		for c in req_career:
			if int(c) == p_data.career:
				match_career = true
				break
		if not match_career:
			return false


	var pre_quests = config.get("PreQuestIDs", [])
	if pre_quests.size() > 0:
		for pre_id in pre_quests:
			var pre_q_id = int(pre_id)
			var pre_q_data = p_data.player_quests.get(pre_q_id, null)


			if pre_q_data == null:
				return false


			var pre_status = int(pre_q_data.get("Status", pre_q_data.get("status", 0)))
			if pre_status != 3:
				return false



	return true

func _clear_container(container: VBoxContainer) -> void :
	if not container: return
	for child in container.get_children():
		child.queue_free()
