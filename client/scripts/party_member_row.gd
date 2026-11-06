
extends Control

signal member_clicked(dbid: int, member_name: String)

@onready var lbl_name = $LblName
@onready var level_badge = $LevelBadge
@onready var bar_hp = $BarHP
@onready var bar_mp = $BarMP
@onready var bar_sp = $BarSP
@onready var member_color = $MemberColor
@onready var leader_badge = $LeaderBadge
@onready var self_badge = $SelfBadge
@onready var value_hp = $ValueHP
@onready var value_mp = $ValueMP
@onready var value_sp = $ValueSP
@onready var card_background = $CardBackground

var _dbid: int = 0
var _member_name: String = ""
var _card_normal_style: StyleBoxFlat
var _card_hover_style: StyleBoxFlat

func _ready() -> void :
	gui_input.connect(_on_gui_input)
	mouse_entered.connect(_on_mouse_entered)
	mouse_exited.connect(_on_mouse_exited)
	for child in get_children():
		if child is Control:
			child.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_prepare_card_styles()

func get_dbid() -> int:
	return _dbid

func setup(member_data: Dictionary, is_leader: bool, is_me: bool, marker_color: Color = Color8(57, 157, 230, 200)) -> void :
	_dbid = int(member_data.get("DBID", 0))
	_member_name = str(member_data.get("Name", "未知"))

	if lbl_name:
		lbl_name.text = _member_name
	update_level(int(member_data.get("Level", -1)))
	if leader_badge:
		leader_badge.visible = is_leader
	if self_badge:
		self_badge.visible = is_me
	_apply_marker_color(marker_color)


	update_stats(
		int(member_data.get("HP", 0)), int(member_data.get("MaxHP", 100)), 
		int(member_data.get("MP", 0)), int(member_data.get("MaxMP", 50)), 
		int(member_data.get("SP", 0)), int(member_data.get("MaxSP", 100))
	)

func update_level(level: int) -> void :
	if not level_badge:
		return
	level_badge.visible = level > 0
	if not level_badge.visible:
		return
	level_badge.text = "Lv. %d" % level
	_update_name_line_layout()

func _update_name_line_layout() -> void :
	if not lbl_name or not level_badge:
		return
	var font: Font = lbl_name.get_theme_font("font")
	var font_size: int = lbl_name.get_theme_font_size("font_size")
	var name_width: float = font.get_string_size(_member_name, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x
	var badge_x: float = clampf(lbl_name.position.x + name_width + 8.0, lbl_name.position.x + 48.0, 474.0)
	level_badge.position = Vector2(badge_x, level_badge.position.y)
	lbl_name.offset_right = badge_x - 6.0

func _on_gui_input(event: InputEvent) -> void :
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		member_clicked.emit(_dbid, _member_name)
		accept_event()

func _on_mouse_entered() -> void :
	if card_background and _card_hover_style:
		card_background.add_theme_stylebox_override("panel", _card_hover_style)

func _on_mouse_exited() -> void :
	if card_background and _card_normal_style:
		card_background.add_theme_stylebox_override("panel", _card_normal_style)

func update_stats(hp: int, max_hp: int, mp: int, max_mp: int, sp: int, max_sp: int) -> void :
	_apply_bar_value(bar_hp, hp, max_hp)
	_apply_bar_value(bar_mp, mp, max_mp)
	_apply_bar_value(bar_sp, sp, max_sp)
	if value_hp:
		value_hp.text = "%d / %d" % [hp, max_hp]
	if value_mp:
		value_mp.text = "%d / %d" % [mp, max_mp]
	if value_sp:
		value_sp.text = "%d / %d" % [sp, max_sp]


func _apply_bar_value(bar_node: Node, current_val: int, max_val: int) -> void :
	if not is_instance_valid(bar_node): return

	if bar_node is Range:
		bar_node.max_value = maxi(max_val, 1)
		bar_node.value = clampi(current_val, 0, maxi(max_val, 1))

func _apply_marker_color(color: Color) -> void :
	if not member_color:
		return
	var marker_style: = member_color.get_theme_stylebox("panel") as StyleBoxFlat
	if not marker_style:
		return
	var local_style: = marker_style.duplicate() as StyleBoxFlat
	local_style.bg_color = color
	local_style.border_color = Color(color.r, color.g, color.b, 1.0).lightened(0.35)
	member_color.add_theme_stylebox_override("panel", local_style)

func _prepare_card_styles() -> void :
	if not card_background:
		return
	var base_style: = card_background.get_theme_stylebox("panel") as StyleBoxFlat
	if not base_style:
		return
	_card_normal_style = base_style.duplicate() as StyleBoxFlat
	_card_hover_style = base_style.duplicate() as StyleBoxFlat
	_card_hover_style.bg_color = Color(0.105882354, 0.21176471, 0.33333334, 0.96)
	_card_hover_style.border_color = Color(0.29803923, 0.93333334, 0.8980392, 0.62)
	card_background.add_theme_stylebox_override("panel", _card_normal_style)
