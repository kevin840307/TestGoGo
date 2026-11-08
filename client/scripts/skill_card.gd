class_name SkillCard
extends PanelContainer

signal skill_clicked(skill_id: int)
signal aoe_center_mode_changed(skill_id: int, self_center: bool)

@onready var icon_rect: TextureRect = $Margin / Content / IconCenter / Icon
@onready var name_label: Label = $Margin / Content / NameRow / Name
@onready var level_label: Label = $Margin / Content / NameRow / Level
@onready var type_label: Label = $Margin / Content / Type
@onready var summary_label: Label = $Margin / Content / Summary
@onready var content_row: HBoxContainer = $Margin / Content

var _skill_id: int = 0
var _aoe_center_toggle: CheckButton = null


func _ready() -> void :
	gui_input.connect(_on_gui_input)
	_aoe_center_toggle = CheckButton.new()
	_aoe_center_toggle.name = "AoeSelfCenter"
	_aoe_center_toggle.text = "自身中心"
	_aoe_center_toggle.tooltip_text = "開啟：以自己為範圍中心；關閉：以目前戰鬥目標為中心。"
	_aoe_center_toggle.custom_minimum_size = Vector2(92.0, 0.0)
	_aoe_center_toggle.focus_mode = Control.FOCUS_NONE
	_aoe_center_toggle.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	_aoe_center_toggle.add_theme_font_size_override("font_size", 12)
	_aoe_center_toggle.visible = false
	_aoe_center_toggle.toggled.connect(_on_aoe_center_toggled)
	content_row.add_child(_aoe_center_toggle)


func setup(
	skill_resource: Resource, 
	skill_id: int, 
	skill_level: int, 
	icon_texture: Texture2D, 
	category_color: Color,
	show_aoe_center_toggle: bool = false,
	self_center: bool = false
) -> void :
	_skill_id = skill_id
	icon_rect.texture = icon_texture
	_apply_type_color(category_color)
	if is_instance_valid(_aoe_center_toggle):
		_aoe_center_toggle.visible = show_aoe_center_toggle
		_aoe_center_toggle.set_pressed_no_signal(self_center)

	if not skill_resource:
		name_label.text = "未知技能(%d)" % skill_id
		level_label.text = "Lv.%d" % skill_level
		type_label.text = "未知"
		summary_label.text = ""
		return

	name_label.text = str(skill_resource.get("skill_name"))
	var max_level: = _get_configured_max_level(skill_resource)
	if max_level > 0:
		level_label.text = "Lv. %d / %d" % [skill_level, max_level]
	else:
		level_label.text = "Lv.%d" % skill_level
	var skill_type: = int(skill_resource.get("skill_type"))
	type_label.text = _get_type_name(skill_type)

	var level_data: Resource = null
	if skill_resource.has_method("get_level_data"):
		level_data = skill_resource.call("get_level_data", skill_level) as Resource
	summary_label.text = _build_summary(level_data, skill_type)


func _apply_type_color(category_color: Color) -> void :
	type_label.add_theme_color_override("font_color", category_color.lightened(0.2))

	var base_style: = type_label.get_theme_stylebox("normal")
	if not base_style is StyleBoxFlat:
		return

	var colored_style: = base_style.duplicate() as StyleBoxFlat
	var background_color: = category_color
	background_color.a = 0.22
	var border_color: = category_color
	border_color.a = 0.72
	colored_style.bg_color = background_color
	colored_style.border_color = border_color
	type_label.add_theme_stylebox_override("normal", colored_style)


func _get_configured_max_level(skill_resource: Resource) -> int:
	if skill_resource is SkillData:
		return (skill_resource as SkillData).levels.size()
	return 0


func _get_type_name(skill_type: int) -> String:
	match skill_type:
		0:
			return "單體攻擊"
		1:
			return "範圍攻擊"
		2:
			return "單體治療"
		3:
			return "群體治療"
		4:
			return "狀態增益"
		5:
			return "狀態增益"
		6:
			return "復活"
		7:
			return "特殊"
		_:
			return "未知"


func _build_summary(level_data: Resource, skill_type: int) -> String:
	if not level_data:
		return "等級資料未提供"

	var parts: Array[String] = []
	var cost_hp: = int(level_data.get("cost_hp"))
	var cost_mp: = int(level_data.get("cost_mp"))
	var cost_sp: = int(level_data.get("cost_sp"))
	var cooldown: = int(level_data.get("cooldown"))
	var duration: = int(level_data.get("duration"))

	if cost_hp > 0:
		parts.append("HP %d" % cost_hp)
	if cost_mp > 0:
		parts.append("MP %d" % cost_mp)
	if cost_sp > 0:
		parts.append("SP %d" % cost_sp)
	if cooldown > 0 or skill_type in [0, 1]:
		parts.append("冷卻 %d秒" % cooldown)
	if duration > 0:
		parts.append("持續 %d秒" % duration)

	return "　｜　".join(parts)


func _on_aoe_center_toggled(enabled: bool) -> void:
	if _skill_id <= 0:
		return
	aoe_center_mode_changed.emit(_skill_id, enabled)


func _on_gui_input(event: InputEvent) -> void :
	if not event is InputEventMouseButton or not event.pressed:
		return

	if event.button_index == MOUSE_BUTTON_LEFT:
		if DropItemManager and DropItemManager.is_dragging:
			DropItemManager.cancel_drag()
			accept_event()
			return
		skill_clicked.emit(_skill_id)
		accept_event()
	elif event.button_index == MOUSE_BUTTON_RIGHT:
		if DropItemManager:
			if DropItemManager.is_dragging:
				DropItemManager.cancel_drag()
			else:
				DropItemManager.start_drag({"skill_id": _skill_id}, 100, icon_rect.texture, null)
		accept_event()
