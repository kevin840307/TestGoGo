
extends Control

@onready var quest_list_container: VBoxContainer = $ScrollContainer / QuestListContainer

var target_player: CharacterBody2D = null

func _ready() -> void :
	visible = false
	quest_list_container.add_theme_constant_override("separation", 6)

func setup(player: CharacterBody2D) -> void :
	target_player = player
	update_tracker()

func update_tracker() -> void :
	if not is_instance_valid(target_player) or not "data_model" in target_player:
		visible = false
		return

	var p_data = target_player.data_model
	if not p_data:
		visible = false
		return


	for child in quest_list_container.get_children():
		child.queue_free()

	var active_quests = p_data.player_quests
	var tracked_quest_count = 0

	for q_id in active_quests.keys():
		var user_quest = active_quests[q_id]
		var status = int(user_quest.get("Status", user_quest.get("status", 0)))


		if status == 1 or status == 2:
			var config = QuestLoader.get_quest_config(q_id)
			if config.is_empty(): continue

			_add_tracker_item(q_id, config, user_quest, status)
			tracked_quest_count += 1

	visible = tracked_quest_count > 0

func _add_tracker_item(quest_id: int, config: Dictionary, user_quest: Dictionary, status: int) -> void :
	var card = PanelContainer.new()
	card.mouse_filter = Control.MOUSE_FILTER_PASS
	card.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	var card_style = StyleBoxFlat.new()
	card_style.bg_color = Color(0.035, 0.075, 0.13, 0.76)
	card_style.border_width_left = 1
	card_style.border_width_top = 1
	card_style.border_width_right = 1
	card_style.border_width_bottom = 1
	card_style.border_color = Color(0.298, 0.933, 0.898, 0.18)
	card_style.corner_radius_top_left = 6
	card_style.corner_radius_top_right = 6
	card_style.corner_radius_bottom_right = 6
	card_style.corner_radius_bottom_left = 6
	card_style.content_margin_left = 8.0
	card_style.content_margin_top = 6.0
	card_style.content_margin_right = 8.0
	card_style.content_margin_bottom = 6.0
	card.add_theme_stylebox_override("panel", card_style)
	var card_hover_style = card_style.duplicate() as StyleBoxFlat
	card_hover_style.bg_color = Color(0.07, 0.15, 0.22, 0.86)
	card_hover_style.border_color = Color(0.298, 0.933, 0.898, 0.42)
	var card_pressed_style = card_style.duplicate() as StyleBoxFlat
	card_pressed_style.bg_color = Color(0.1, 0.2, 0.29, 0.92)
	card_pressed_style.border_color = Color(0.298, 0.933, 0.898, 0.62)
	card.mouse_entered.connect(_on_tracker_card_mouse_entered.bind(card, card_hover_style))
	card.mouse_exited.connect(_on_tracker_card_mouse_exited.bind(card, card_style))
	card.gui_input.connect(_on_tracker_card_gui_input.bind(card, card_hover_style, card_pressed_style, quest_id))

	var item_box = VBoxContainer.new()
	item_box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	item_box.add_theme_constant_override("separation", 3)
	card.add_child(item_box)


	var title_btn = Button.new()
	var title_text = config.get("Title", "未命名任務")
	if status == 2:
		title_text += " [可回報]"
		title_btn.add_theme_color_override("font_color", Color(1.0, 0.94, 0.17))
	else:
		title_btn.add_theme_color_override("font_color", Color(0.429, 0.883, 0.866, 1.0))

	title_btn.text = title_text
	title_btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
	title_btn.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	title_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title_btn.flat = true
	title_btn.focus_mode = Control.FOCUS_NONE
	title_btn.mouse_filter = Control.MOUSE_FILTER_IGNORE
	title_btn.add_theme_font_size_override("font_size", 15)
	title_btn.add_theme_constant_override("outline_size", 2)
	title_btn.add_theme_color_override("font_outline_color", Color(0.118, 0.118, 0.118, 1.0))
	item_box.add_child(title_btn)


	var current_step_idx = int(user_quest.get("CurrentStep", user_quest.get("current_step", 0)))
	var step_progress = int(user_quest.get("StepProgress", user_quest.get("step_progress", 0)))
	var steps = config.get("Steps", [])

	if current_step_idx < steps.size():
		var step_cfg = steps[current_step_idx]
		var step_desc = step_cfg.get("StepDesc", "")
		var target_count = int(step_cfg.get("TargetCount", 1))

		var lbl_step = Label.new()
		var step_text = step_desc
		if target_count > 1:
			step_text += " (%d/%d)" % [step_progress, target_count]

		lbl_step.text = step_text
		lbl_step.add_theme_font_size_override("font_size", 14)
		lbl_step.add_theme_constant_override("outline_size", 2)
		lbl_step.add_theme_color_override("font_color", Color(1.0, 1.0, 1.0, 1.0))
		lbl_step.add_theme_color_override("font_outline_color", Color(0.118, 0.118, 0.118, 1.0))
		lbl_step.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		lbl_step.mouse_filter = Control.MOUSE_FILTER_IGNORE
		item_box.add_child(lbl_step)

	quest_list_container.add_child(card)

func _on_tracker_card_mouse_entered(card: PanelContainer, hover_style: StyleBoxFlat) -> void :
	card.add_theme_stylebox_override("panel", hover_style)

func _on_tracker_card_mouse_exited(card: PanelContainer, normal_style: StyleBoxFlat) -> void :
	card.add_theme_stylebox_override("panel", normal_style)

func _on_tracker_card_gui_input(event: InputEvent, card: PanelContainer, hover_style: StyleBoxFlat, pressed_style: StyleBoxFlat, quest_id: int) -> void :
	if not event is InputEventMouseButton or event.button_index != MOUSE_BUTTON_LEFT:
		return
	card.accept_event()
	if event.pressed:
		card.add_theme_stylebox_override("panel", pressed_style)
	else:
		card.add_theme_stylebox_override("panel", hover_style)
		_open_quest_detail(quest_id)

func _open_quest_detail(quest_id: int) -> void :
	var hud = GameManager.current_hud if GameManager else null
	if hud and hud.get("quest_window"):
		hud._toggle_window(hud.quest_window)
		if hud.quest_window.has_method("_show_quest_detail"):
			hud.quest_window.selected_quest_id = quest_id
			hud.quest_window._show_quest_detail(quest_id)
