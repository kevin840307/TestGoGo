extends Control

enum SkillCategory{ALL, ATTACK, RECOVERY, BUFF, SPECIAL}

const SKILL_CARD_SCENE = preload("res://skill_card.tscn")
const CATEGORY_COLORS: = {
	SkillCategory.ALL: Color("#E4BD55"), 
	SkillCategory.ATTACK: Color("#EF705D"), 
	SkillCategory.RECOVERY: Color("#63D88A"), 
	SkillCategory.BUFF: Color("#55CFD1"), 
	SkillCategory.SPECIAL: Color("#B58AEF"), 
}
const UNKNOWN_SKILL_COLOR: = Color("#8C98A8")

@onready var btn_close: TextureButton = $BtnClose
@onready var skill_count: Label = $SkillCount
@onready var skill_grid: GridContainer = $ContentScroll / SkillGrid
@onready var content_scroll: ScrollContainer = $ContentScroll
@onready var empty_state: VBoxContainer = $EmptyState
@onready var empty_title: Label = $EmptyState / Title
@onready var empty_description: Label = $EmptyState / Description
@onready var category_buttons: Dictionary = {
	SkillCategory.ALL: $CategoryTabs / BtnAll, 
	SkillCategory.ATTACK: $CategoryTabs / BtnAttack, 
	SkillCategory.RECOVERY: $CategoryTabs / BtnRecovery, 
	SkillCategory.BUFF: $CategoryTabs / BtnBuff, 
	SkillCategory.SPECIAL: $CategoryTabs / BtnSpecial, 
}

var target_player: CharacterBody2D = null
var _selected_category: SkillCategory = SkillCategory.ALL
var _button_group: = ButtonGroup.new()


func _ready() -> void :
	if btn_close:
		btn_close.pressed.connect(hide)

	for category in category_buttons:
		var button: = category_buttons[category] as Button
		if not button:
			continue
		button.button_group = _button_group
		_apply_category_button_style(button, CATEGORY_COLORS.get(category, UNKNOWN_SKILL_COLOR) as Color)
		button.pressed.connect(_on_category_pressed.bind(category))

	_update_category_buttons({})


func setup(player: CharacterBody2D) -> void :
	# GameHUD installs this window hidden on world entry. Building SkillCards
	# and loading all referenced .tres/icons here costs ~60ms and memory even
	# when the user never opens the skill book (especially expensive for
	# multiple background clients). GameHUD._toggle_window() already calls
	# update_ui() immediately before each first/open, and SkillSync refreshes
	# only an already visible window. Keep the authoritative model binding.
	target_player = player


func update_ui() -> void :
	_clear_skill_grid()

	if not is_instance_valid(target_player) or not "data_model" in target_player:
		_show_unavailable_state()
		return

	var data = target_player.data_model
	if not data:
		_show_unavailable_state()
		return

	var learned_skills: Array = data.skill_list.duplicate(true) if "skill_list" in data else []
	learned_skills.sort_custom( func(a, b):
		return int(a.get("skill_id", 0)) < int(b.get("skill_id", 0))
	)

	var category_counts: = {
		SkillCategory.ALL: learned_skills.size(), 
		SkillCategory.ATTACK: 0, 
		SkillCategory.RECOVERY: 0, 
		SkillCategory.BUFF: 0, 
		SkillCategory.SPECIAL: 0, 
	}
	var display_skills: Array[Dictionary] = []

	for learned_skill in learned_skills:
		var skill_id: = int(learned_skill.get("skill_id", 0))
		var skill_level: = int(learned_skill.get("level", 1))
		var skill_resource: Resource = SkillLoader.get_skill_data(skill_id) if SkillLoader else null
		var category: = _get_skill_category(skill_resource)

		if category != SkillCategory.ALL:
			category_counts[category] = int(category_counts.get(category, 0)) + 1

		if _selected_category == SkillCategory.ALL or category == _selected_category:
			display_skills.append({
				"skill_id": skill_id, 
				"level": skill_level, 
				"resource": skill_resource, 
			})

	_update_skill_count(learned_skills.size())
	_update_category_buttons(category_counts)

	for skill_entry in display_skills:
		_add_skill_card(skill_entry)

	if display_skills.is_empty():
		_show_empty_state(
			"目前沒有技能" if learned_skills.is_empty() else "此分類沒有技能", 
			_get_empty_description()
		)
	else:
		content_scroll.visible = true
		empty_state.visible = false


func _add_skill_card(skill_entry: Dictionary) -> void :
	var skill_id: = int(skill_entry.get("skill_id", 0))
	var skill_level: = int(skill_entry.get("level", 1))
	var skill_resource: = skill_entry.get("resource") as Resource
	var icon_texture: Texture2D = null

	if skill_resource and SkillLoader:
		icon_texture = SkillLoader.get_icon(int(skill_resource.get("icon_id")))

	var card: = SKILL_CARD_SCENE.instantiate()
	skill_grid.add_child(card)
	card.setup(skill_resource, skill_id, skill_level, icon_texture, _get_skill_label_color(skill_resource))
	card.skill_clicked.connect(_on_skill_clicked)
	card.mouse_entered.connect(_show_skill_tooltip.bind(skill_id, skill_level, icon_texture))
	card.mouse_exited.connect(_hide_skill_tooltip)


func _clear_skill_grid() -> void :
	if not skill_grid:
		return
	for child in skill_grid.get_children():
		child.queue_free()


func _get_skill_category(skill_resource: Resource) -> SkillCategory:
	if not skill_resource:
		return SkillCategory.ALL

	match int(skill_resource.get("skill_type")):
		0, 1:
			return SkillCategory.ATTACK
		2, 3, 6:
			return SkillCategory.RECOVERY
		4, 5:
			return SkillCategory.BUFF
		7:
			return SkillCategory.SPECIAL
		_:
			return SkillCategory.ALL


func _get_skill_label_color(skill_resource: Resource) -> Color:
	var category: = _get_skill_category(skill_resource)
	if category == SkillCategory.ALL:
		return UNKNOWN_SKILL_COLOR
	return CATEGORY_COLORS.get(category, UNKNOWN_SKILL_COLOR) as Color


func _apply_category_button_style(button: Button, category_color: Color) -> void :
	var style_settings: = {
		"normal": [0.62, 0.9, 0.62], 
		"hover": [0.48, 0.94, 0.85], 
		"pressed": [0.67, 0.96, 0.95], 
	}

	for style_name in style_settings:
		var base_style: = button.get_theme_stylebox(style_name)
		if not base_style is StyleBoxFlat:
			continue

		var settings: Array = style_settings[style_name]
		var colored_style: = base_style.duplicate() as StyleBoxFlat
		var background_color: = category_color.darkened(float(settings[0]))
		background_color.a = float(settings[1])
		var border_color: = category_color
		border_color.a = float(settings[2])
		colored_style.bg_color = background_color
		colored_style.border_color = border_color
		button.add_theme_stylebox_override(style_name, colored_style)

	button.add_theme_color_override("font_color", category_color.lightened(0.22))
	button.add_theme_color_override("font_hover_color", category_color.lightened(0.32))
	button.add_theme_color_override("font_pressed_color", category_color.lightened(0.38))


func _on_category_pressed(category: SkillCategory) -> void :
	_selected_category = category
	update_ui()


func _update_category_buttons(counts: Dictionary) -> void :
	var button_labels: = {
		SkillCategory.ALL: "全部", 
		SkillCategory.ATTACK: "攻擊", 
		SkillCategory.RECOVERY: "治療/復活", 
		SkillCategory.BUFF: "狀態", 
		SkillCategory.SPECIAL: "特殊", 
	}

	for category in category_buttons:
		var button: = category_buttons[category] as Button
		if not button:
			continue
		button.text = "%s (%d)" % [button_labels[category], int(counts.get(category, 0))]
		button.set_pressed_no_signal(category == _selected_category)


func _update_skill_count(count: int) -> void :
	if skill_count:
		skill_count.text = "已學技能 %d 個" % count


func _show_unavailable_state() -> void :
	_update_skill_count(0)
	_update_category_buttons({})
	_show_empty_state("無法讀取技能", "目前無法取得角色技能資料")


func _show_empty_state(title: String, description: String) -> void :
	content_scroll.visible = false
	empty_state.visible = true
	empty_title.text = title
	empty_description.text = description


func _get_empty_description() -> String:
	match _selected_category:
		SkillCategory.ALL:
			return "目前尚未學習任何技能"
		SkillCategory.ATTACK:
			return "目前沒有已學習的攻擊技能"
		SkillCategory.RECOVERY:
			return "目前沒有已學習的治療或復活技能"
		SkillCategory.BUFF:
			return "目前沒有已學習的狀態增益技能"
		SkillCategory.SPECIAL:
			return "目前沒有已學習的特殊技能"
		_:
			return "此分類目前沒有已學習的技能"


func _on_skill_clicked(skill_id: int) -> void :
	if is_instance_valid(target_player) and target_player.has_method("prepare_skill"):
		target_player.prepare_skill(skill_id)


func _show_skill_tooltip(skill_id: int, level: int, icon_texture: Texture2D) -> void :
	if GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):
		GameManager.current_hud.shared_tooltip.show_skill(skill_id, level, icon_texture)


func _hide_skill_tooltip() -> void :
	if GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):
		GameManager.current_hud.shared_tooltip.hide_tooltip()
