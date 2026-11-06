
extends Control
class_name ClanWindow


@onready var lbl_name: Label = $SummaryPanel / LblName
@onready var lbl_leader: Label = $SummaryPanel / LblLeader
@onready var lbl_exp: Label = $SummaryPanel / LblExp
@onready var lbl_member_count: Label = $SummaryPanel / LblMemberCount
@onready var lbl_online_count: Label = $SummaryPanel / LblOnlineCount
@onready var lbl_level: Label = $SummaryPanel / LblLevel
@onready var lbl_money: Label = $SummaryPanel / LblMoney
@onready var lbl_donation: Label = $SummaryPanel / LblDonation
@onready var announcement_title: Label = $SummaryPanel / AnnouncementTitle
@onready var announcement_scroll: ScrollContainer = $SummaryPanel / AnnouncementScroll
@onready var lbl_club_message: Label = $SummaryPanel / AnnouncementScroll / LblClubMessage
@onready var btn_edit_club_message: Button = $SummaryPanel / BtnEditClubMessage
@onready var message_editor: Panel = $SummaryPanel / MessageEditor
@onready var message_input: TextEdit = $SummaryPanel / MessageEditor / Input
@onready var message_character_count: Label = $SummaryPanel / MessageEditor / CharacterCount
@onready var message_edit_status: Label = $SummaryPanel / MessageEditor / EditStatus
@onready var btn_clear_club_message: Button = $SummaryPanel / MessageEditor / BtnClear
@onready var btn_cancel_club_message: Button = $SummaryPanel / MessageEditor / BtnCancel
@onready var btn_save_club_message: Button = $SummaryPanel / MessageEditor / BtnSave
@onready var list_member: VBoxContainer = $PanelInfo / ScrollContainer / ListMember
@onready var btn_close: BaseButton = $BtnClose
@onready var panel_info: Control = $PanelInfo
@onready var btn_members_tab: Button = $BtnMembersTab
@onready var btn_tech_tab: Button = $BtnTechTab

@onready var panel_tech: Control = $PanelTech
@onready var lbl_research_status: Label = $PanelTech / LblResearchStatus
@onready var lbl_money_balance: Label = $PanelTech / LblMoneyBalance
@onready var lbl_exp_balance: Label = $PanelTech / LblExpBalance
@onready var input_donate_money: SpinBox = $PanelTech / InputDonateMoney
@onready var btn_donate_money: Button = $PanelTech / BtnDonateMoney
@onready var input_donate_exp: SpinBox = $PanelTech / InputDonateExp
@onready var btn_donate_exp: Button = $PanelTech / BtnDonateExp
@onready var tech_list_container: VBoxContainer = $PanelTech / ScrollContainer / TechList


const CLUB_MEMBER_ROW_SCENE = preload("res://club_member_row.tscn")


const TECH_NAMES = {
	1: "戰團等級", 2: "攻擊", 3: "防禦", 4: "魔攻", 5: "魔防", 
	6: "命中", 7: "迴避", 8: "移速", 9: "攻速", 10: "暴擊率", 11: "暴擊傷害"
}
const TECH_MAX_LEVEL: int = 5
const TECH_COST_MONEY_PER_LEVEL: int = 500000
const TECH_COST_EXP_PER_LEVEL: int = 1000000
const TECH_HOURS_PER_LEVEL: int = 6
const CLUB_MESSAGE_MAX_LENGTH: int = 100
const MEMBER_ROLE_STYLES: = {
	0: {
		"text": "團員", 
		"font_color": Color(0.78, 0.87, 0.95, 1.0), 
		"background_color": Color(0.1, 0.22, 0.34, 0.95), 
		"border_color": Color(0.35, 0.55, 0.72, 0.85)
	}, 
	1: {
		"text": "騎士", 
		"font_color": Color(0.69, 1.0, 0.9, 1.0), 
		"background_color": Color(0.05, 0.31, 0.28, 0.95), 
		"border_color": Color(0.27, 0.82, 0.68, 0.85)
	}, 
	2: {
		"text": "驍將騎士", 
		"font_color": Color(0.9, 0.8, 1.0, 1.0), 
		"background_color": Color(0.28, 0.19, 0.43, 0.95), 
		"border_color": Color(0.67, 0.48, 0.91, 0.85)
	}, 
	3: {
		"text": "團長", 
		"font_color": Color(1.0, 0.89, 0.52, 1.0), 
		"background_color": Color(0.46, 0.35, 0.08, 0.95), 
		"border_color": Color(0.95, 0.76, 0.3, 0.85)
	}
}

var target_player: CharacterBody2D = null


var _active_research_id: int = 0
var _active_research_remaining_sec: float = 0.0
var _last_research_display_state: String = ""
var _last_research_display_second: int = -1
var _last_research_display_id: int = -1
const RESEARCH_REFRESH_INTERVAL_SEC: float = 2.0
var _research_refresh_remaining_sec: float = 0.0
var _tech_levels: Dictionary = {}
var _club_saving: int = 0
var _club_exp: int = 0
var _my_position: int = 0
var _latest_club_message: String = ""
var _message_editor_base: String = ""
var _updating_message_input: bool = false

func _ready() -> void :
	visibility_changed.connect(_on_visibility_changed)
	set_process(is_visible_in_tree())

	if NetworkManager:
		if not NetworkManager.has_signal("club_sync_received"):
			NetworkManager.add_user_signal("club_sync_received")
		if not NetworkManager.is_connected("club_sync_received", _on_club_sync_received):
			NetworkManager.connect("club_sync_received", _on_club_sync_received)

	if btn_close:
		btn_close.pressed.connect(_on_btn_close_pressed)
	if btn_members_tab:
		btn_members_tab.pressed.connect( func(): _show_tab(false))
	if btn_tech_tab:
		btn_tech_tab.pressed.connect( func(): _show_tab(true))
	if btn_edit_club_message:
		btn_edit_club_message.pressed.connect(_on_btn_edit_club_message_pressed)
	if message_input:
		message_input.text_changed.connect(_on_message_input_text_changed)
	if btn_clear_club_message:
		btn_clear_club_message.pressed.connect(_on_btn_clear_club_message_pressed)
	if btn_cancel_club_message:
		btn_cancel_club_message.pressed.connect(_on_btn_cancel_club_message_pressed)
	if btn_save_club_message:
		btn_save_club_message.pressed.connect(_on_btn_save_club_message_pressed)
	_show_tab(false)


	if btn_donate_money:
		btn_donate_money.pressed.connect(_on_btn_donate_money_pressed)
	if btn_donate_exp:
		btn_donate_exp.pressed.connect(_on_btn_donate_exp_pressed)

func _on_visibility_changed() -> void :
	set_process(is_visible_in_tree())

func _process(delta: float) -> void :
	if not visible: return

	if _active_research_id > 0:
		if _active_research_remaining_sec > 0:
			_active_research_remaining_sec = maxf(0.0, _active_research_remaining_sec - delta)
			if _active_research_remaining_sec > 0:
				_update_research_status_display("active", ceili(_active_research_remaining_sec))
			else:
				_research_refresh_remaining_sec = 0.0
		if _active_research_remaining_sec <= 0:
			_update_research_status_display("waiting")
			_research_refresh_remaining_sec -= delta
			if _research_refresh_remaining_sec <= 0 and NetworkManager and NetworkManager.is_connected_to_server:
				NetworkManager.send_secure_packet({"Type": "ClubAction", "Action": "GetInfo"})
				_research_refresh_remaining_sec = RESEARCH_REFRESH_INTERVAL_SEC
	else:
		_update_research_status_display("none")
		_research_refresh_remaining_sec = 0.0

func _update_research_status_display(state: String, total_sec: int = -1) -> void :
	if not lbl_research_status:
		return

	var state_changed: = state != _last_research_display_state
	var second_changed: = total_sec != _last_research_display_second
	var research_changed: = _active_research_id != _last_research_display_id
	if not state_changed and not second_changed and not research_changed:
		return

	if state_changed:
		var color: = Color(0.261, 0.924, 0.882, 1.0) if state == "active" else Color.WHITE
		lbl_research_status.add_theme_color_override("font_color", color)

	match state:
		"active":
			var hours: = total_sec / 3600
			var minutes: = (total_sec % 3600) / 60
			var seconds: = total_sec % 60
			var tech_name: String = TECH_NAMES.get(_active_research_id, "未知科技")
			lbl_research_status.text = "🧪【研發中】%s - %02d:%02d:%02d" % [tech_name, hours, minutes, seconds]
		"waiting":
			lbl_research_status.text = "研發完成，等待伺服器結算..."
		_:
			lbl_research_status.text = "目前無研發中的項目"

	_last_research_display_state = state
	_last_research_display_second = total_sec
	_last_research_display_id = _active_research_id

func _on_btn_close_pressed() -> void :
	_set_message_editor_visible(false)
	if GameManager and GameManager.current_hud and GameManager.current_hud.has_method("_close_window"):
		GameManager.current_hud._close_window(self)
	else:
		visible = false

func _show_tab(show_tech: bool) -> void :
	if panel_info:
		panel_info.visible = not show_tech
	if panel_tech:
		panel_tech.visible = show_tech
	if btn_members_tab:
		btn_members_tab.set_pressed_no_signal( not show_tech)
	if btn_tech_tab:
		btn_tech_tab.set_pressed_no_signal(show_tech)

func _on_btn_edit_club_message_pressed() -> void :
	if _my_position != 3 or not message_editor or not message_input:
		return
	_message_editor_base = _latest_club_message
	_updating_message_input = true
	message_input.text = _latest_club_message
	_updating_message_input = false
	message_edit_status.text = ""
	_update_message_character_count()
	_set_message_editor_visible(true)
	message_input.grab_focus()

func _set_message_editor_visible(is_editing: bool) -> void :
	if message_editor:
		message_editor.visible = is_editing
	if announcement_title:
		announcement_title.visible = not is_editing
	if announcement_scroll:
		announcement_scroll.visible = not is_editing
		if not is_editing:
			announcement_scroll.set_deferred("scroll_vertical", 0)
	if btn_edit_club_message:
		btn_edit_club_message.visible = not is_editing and _my_position == 3

func _on_message_input_text_changed() -> void :
	if _updating_message_input or not message_input:
		return
	if message_input.text.length() > CLUB_MESSAGE_MAX_LENGTH:
		_updating_message_input = true
		message_input.text = message_input.text.substr(0, CLUB_MESSAGE_MAX_LENGTH)
		_updating_message_input = false
		message_edit_status.text = "公告最多只能輸入 100 個字元"
	_update_message_character_count()

func _update_message_character_count() -> void :
	if message_character_count and message_input:
		message_character_count.text = "%d / %d" % [message_input.text.length(), CLUB_MESSAGE_MAX_LENGTH]

func _on_btn_cancel_club_message_pressed() -> void :
	_set_message_editor_visible(false)
	if message_edit_status:
		message_edit_status.text = ""

func _on_btn_save_club_message_pressed() -> void :
	if _my_position != 3 or not message_input:
		return
	var club_message: = message_input.text.strip_edges()
	if club_message.length() > CLUB_MESSAGE_MAX_LENGTH:
		message_edit_status.text = "公告最多只能輸入 100 個字元"
		return
	if club_message == _message_editor_base:
		_on_btn_cancel_club_message_pressed()
		return
	if NetworkManager:
		NetworkManager.send_secure_packet({
			"Type": "ClubAction", 
			"Action": "UpdateMessage", 
			"ClubMsg": club_message
		})
	_on_btn_cancel_club_message_pressed()

func _on_btn_clear_club_message_pressed() -> void :
	if _my_position != 3:
		return
	if GameManager and GameManager.current_hud:
		GameManager.current_hud.show_message_box(
			"確定要清除目前的戰團公告嗎？", 
			2, 
			func():
				if NetworkManager:
					NetworkManager.send_secure_packet({
						"Type": "ClubAction", 
						"Action": "UpdateMessage", 
						"ClubMsg": ""
					})
				_on_btn_cancel_club_message_pressed()
		)

func setup(player: CharacterBody2D) -> void :
	target_player = player

func update_ui() -> void :
	_set_message_editor_visible(false)
	if target_player and "data_model" in target_player and target_player.data_model.club_id <= 0:
		visible = false
		return

	if NetworkManager and NetworkManager.is_connected_to_server:
		NetworkManager.send_secure_packet({"Type": "ClubAction", "Action": "GetInfo"})

func _on_club_sync_received(data: Dictionary) -> void :
	if data.get("ClubID", 0) <= 0:
		visible = false
		if GameManager and GameManager.current_hud:
			GameManager.current_hud.show_rejection_message_box("你尚未加入任何戰團！")
		return


	if lbl_name: lbl_name.text = str(data.get("ClubName", ""))
	if lbl_level: lbl_level.text = "戰團等級｜%d" % int(data.get("ClubLevel", 1))
	if lbl_exp: lbl_exp.text = "戰團經驗｜%s" % _format_amount(int(data.get("ClubExp", 0)))
	if lbl_money: lbl_money.text = "戰團資金｜%s" % _format_amount(int(data.get("ClubSaving", 0)))
	if lbl_money_balance: lbl_money_balance.text = "戰團資金：%s" % _format_amount(int(data.get("ClubSaving", 0)))
	if lbl_exp_balance: lbl_exp_balance.text = "戰團經驗：%s" % _format_amount(int(data.get("ClubExp", 0)))
	if lbl_donation: lbl_donation.text = "個人貢獻｜%s" % _format_amount(int(data.get("MyDonation", 0)))
	var synced_club_message: = str(data.get("ClubMsg", "")).strip_edges()
	var club_message_changed: = synced_club_message != _latest_club_message
	_latest_club_message = synced_club_message
	if lbl_club_message:
		lbl_club_message.text = synced_club_message if not synced_club_message.is_empty() else "目前沒有戰團公告"
	if club_message_changed and announcement_scroll and not message_editor.visible:
		announcement_scroll.set_deferred("scroll_vertical", 0)

	var members = data.get("Members", [])
	if lbl_member_count: lbl_member_count.text = "成員｜%d / %d" % [members.size(), int(data.get("ClubMaxMember", 50))]
	if lbl_online_count:
		var online_count: = 0
		for member in members:
			if member.get("IsOnline", false):
				online_count += 1
		lbl_online_count.text = "在線｜%d" % online_count

	var leader_name = "無"
	for m in members:
		if int(m.get("Position", 0)) == 3:
			leader_name = str(m.get("Name", ""))
			break
	if lbl_leader: lbl_leader.text = "團長｜%s" % leader_name


	_render_member_list(members)


	_active_research_id = int(data.get("ActiveResearchID", 0))
	_active_research_remaining_sec = float(data.get("ActiveResearchRemainingSec", 0))
	_research_refresh_remaining_sec = RESEARCH_REFRESH_INTERVAL_SEC if _active_research_remaining_sec <= 0 else 0.0
	_last_research_display_state = ""
	_last_research_display_second = -1
	_last_research_display_id = -1


	var researches = data.get("Researches", [])
	var my_position = int(data.get("MyPosition", 0))
	_my_position = my_position
	if btn_edit_club_message:
		btn_edit_club_message.visible = _my_position == 3 and not message_editor.visible
	if _my_position != 3 and message_editor:
		_set_message_editor_visible(false)
	elif message_editor and message_editor.visible and synced_club_message != _message_editor_base:
		message_edit_status.text = "公告已由伺服器更新；目前草稿尚未覆蓋"
	var club_level = int(data.get("ClubLevel", 1))
	_club_saving = int(data.get("ClubSaving", 0))
	_club_exp = int(data.get("ClubExp", 0))
	_render_tech_list(researches, my_position, club_level)


	if is_instance_valid(target_player) and "data_model" in target_player:
		var p_data = target_player.data_model

		if input_donate_money: input_donate_money.max_value = max(1, p_data.money)
		if input_donate_exp: input_donate_exp.max_value = max(1, p_data.exp_acc)

func _render_tech_list(researches: Array, my_position: int, club_level: int) -> void :
	if not tech_list_container: return


	var res_dict: Dictionary = {}
	for r in researches:
		res_dict[int(r.get("ResearchID", 0))] = int(r.get("Level", 0))
	_tech_levels = res_dict
	_tech_levels[1] = club_level


	for child in tech_list_container.get_children():
		child.queue_free()

	var is_leader: = my_position == 3
	var is_researching: = _active_research_id > 0


	for i in range(1, 12):
		var current_lv: int = int(_tech_levels.get(i, 0))
		var next_lv: = current_lv + 1
		var is_max_level: = current_lv >= TECH_MAX_LEVEL
		var cost_money: = next_lv * TECH_COST_MONEY_PER_LEVEL
		var cost_exp: = next_lv * TECH_COST_EXP_PER_LEVEL
		var research_hours: = next_lv * TECH_HOURS_PER_LEVEL
		var has_resources: = _club_saving >= cost_money and _club_exp >= cost_exp
		var is_active: = i == _active_research_id

		var row_panel: = PanelContainer.new()
		row_panel.custom_minimum_size.y = 68
		row_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		var row_style: = StyleBoxFlat.new()
		row_style.bg_color = Color(0.078, 0.165, 0.278, 0.92) if i % 2 == 0 else Color(0.063, 0.133, 0.22, 0.92)
		row_style.border_width_left = 1
		row_style.border_width_top = 1
		row_style.border_width_right = 1
		row_style.border_width_bottom = 1
		row_style.border_color = Color(0.294, 0.475, 0.627, 0.45)
		row_style.corner_radius_top_left = 7
		row_style.corner_radius_top_right = 7
		row_style.corner_radius_bottom_left = 7
		row_style.corner_radius_bottom_right = 7
		row_style.content_margin_left = 12
		row_style.content_margin_right = 12
		row_style.content_margin_top = 7
		row_style.content_margin_bottom = 7
		if is_active:
			row_style.border_width_left = 3
			row_style.border_color = Color(0.26, 0.92, 0.88)
		row_panel.add_theme_stylebox_override("panel", row_style)

		var row: = HBoxContainer.new()
		row.add_theme_constant_override("separation", 12)
		row_panel.add_child(row)

		var identity_box: = VBoxContainer.new()
		identity_box.custom_minimum_size.x = 142
		identity_box.add_theme_constant_override("separation", 1)
		row.add_child(identity_box)
		var name_label: = Label.new()
		name_label.text = str(TECH_NAMES.get(i, "未知"))
		name_label.add_theme_font_size_override("font_size", 17)
		name_label.add_theme_color_override("font_color", Color(0.922, 0.961, 1.0))
		identity_box.add_child(name_label)
		var level_label: = Label.new()
		level_label.text = "Lv.%d / %d" % [current_lv, TECH_MAX_LEVEL]
		level_label.add_theme_font_size_override("font_size", 14)
		level_label.add_theme_color_override("font_color", Color(0.72, 0.83, 0.9))
		identity_box.add_child(level_label)

		var effect_box: = VBoxContainer.new()
		effect_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		effect_box.add_theme_constant_override("separation", 1)
		row.add_child(effect_box)
		var effect_heading: = Label.new()
		effect_heading.text = "下一級效果"
		effect_heading.add_theme_font_size_override("font_size", 13)
		effect_heading.add_theme_color_override("font_color", Color(0.6196, 0.7137, 0.8))
		effect_box.add_child(effect_heading)
		var effect_label: = Label.new()
		effect_label.text = "研發進行中" if is_active else _get_next_tech_effect(i, current_lv)
		effect_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		effect_label.clip_text = true
		effect_label.add_theme_font_size_override("font_size", 15)
		effect_label.add_theme_color_override("font_color", Color(0.62, 0.89, 0.86))
		effect_box.add_child(effect_label)

		var cost_box: = VBoxContainer.new()
		cost_box.custom_minimum_size.x = 260
		cost_box.add_theme_constant_override("separation", 1)
		row.add_child(cost_box)
		var cost_heading: = Label.new()
		cost_heading.text = "已達最高等級" if is_max_level else "研發需求｜%d 小時" % research_hours
		cost_heading.add_theme_font_size_override("font_size", 13)
		cost_heading.add_theme_color_override("font_color", Color(0.6196, 0.7137, 0.8))
		cost_box.add_child(cost_heading)
		var cost_label: = Label.new()
		cost_label.text = "無" if is_max_level else "資金 %s　經驗 %s" % [_format_amount(cost_money), _format_amount(cost_exp)]
		cost_label.add_theme_font_size_override("font_size", 14)
		cost_label.add_theme_color_override("font_color", Color(0.839, 0.902, 0.949) if has_resources or is_max_level else Color(1.0, 0.557, 0.588))
		cost_box.add_child(cost_label)

		if is_leader:
			var btn: = Button.new()
			btn.text = "滿級" if is_max_level else ("研發中" if is_active else "研發")
			btn.add_theme_font_size_override("font_size", 15)
			btn.custom_minimum_size = Vector2(74, 36)
			btn.disabled = is_researching or is_max_level or not has_resources
			if not is_max_level and not is_researching and not has_resources:
				btn.tooltip_text = "戰團資金或經驗不足"
			btn.pressed.connect(_on_btn_research_pressed.bind(i))
			row.add_child(btn)
		tech_list_container.add_child(row_panel)

func _get_next_tech_effect(tech_id: int, current_lv: int) -> String:
	if current_lv >= TECH_MAX_LEVEL:
		return "已達最高等級"
	var next_lv: = current_lv + 1
	match tech_id:
		1: return "下級人數 %d" % (next_lv * 10)
		2: return "下級攻擊 +%d" % (next_lv * 10)
		3: return "下級防禦 +%d" % (next_lv * 10)
		4: return "下級魔攻 +%d" % (next_lv * 10)
		5: return "下級魔防 +%d" % (next_lv * 10)
		6: return "下級命中 +%d" % (next_lv * 5)
		7: return "下級迴避 +%d" % (next_lv * 5)
		8: return "下級移速 +%d" % (next_lv * 5)
		9: return "下級攻速 +%d" % (next_lv * 5)
		10: return "下級暴擊率 +%d%%" % (next_lv * 3)
		11: return "下級暴擊傷害 +%d%%" % (next_lv * 5)
	return ""

func _format_amount(amount: int) -> String:
	var digits: = str(amount)
	var formatted: = ""
	while digits.length() > 3:
		formatted = "," + digits.substr(digits.length() - 3) + formatted
		digits = digits.substr(0, digits.length() - 3)
	return digits + formatted


func _on_btn_donate_money_pressed() -> void :
	if not input_donate_money: return

	var amount = int(input_donate_money.value)
	if amount <= 0: return


	if GameManager and GameManager.current_hud:
		GameManager.current_hud.show_message_box(
			"確定要為戰團捐獻 %d 星幣嗎？" % amount, 
			2, 
			func():
				if NetworkManager:
					NetworkManager.send_secure_packet({
						"Type": "ClubAction", 
						"Action": "DonateMoney", 
						"Amount": amount
					})
				input_donate_money.value = 1
		)

func _on_btn_donate_exp_pressed() -> void :
	if not input_donate_exp: return

	var amount = int(input_donate_exp.value)
	if amount <= 0: return


	if GameManager and GameManager.current_hud:
		GameManager.current_hud.show_message_box(
			"確定要為戰團捐獻 %d 點經驗嗎？" % amount, 
			2, 
			func():
				if NetworkManager:
					NetworkManager.send_secure_packet({
						"Type": "ClubAction", 
						"Action": "DonateExp", 
						"Amount": amount
					})
				input_donate_exp.value = 1
		)

func _on_btn_research_pressed(tech_id: int) -> void :
	var tech_name = TECH_NAMES.get(tech_id, "未知科技")
	var next_lv: int = int(_tech_levels.get(tech_id, 0)) + 1
	var cost_money: = next_lv * TECH_COST_MONEY_PER_LEVEL
	var cost_exp: = next_lv * TECH_COST_EXP_PER_LEVEL
	var hours: = next_lv * TECH_HOURS_PER_LEVEL
	if GameManager and GameManager.current_hud:
		GameManager.current_hud.show_message_box(
			"確定要研發【%s】至 Lv.%d 嗎？\n需要戰團資金 %s、經驗 %s，時間 %d 小時。" % [tech_name, next_lv, _format_amount(cost_money), _format_amount(cost_exp), hours], 
			2, 
			func():
				if NetworkManager:
					NetworkManager.send_secure_packet({
						"Type": "ClubAction", 
						"Action": "Research", 
						"ResearchID": tech_id
					})
		)

func _render_member_list(members: Array) -> void :
	if not list_member: return

	for child in list_member.get_children():
		child.queue_free()

	members.sort_custom( func(a, b):
		var online_a = a.get("IsOnline", false)
		var online_b = b.get("IsOnline", false)
		if online_a != online_b:
			return online_a

		var pos_a = int(a.get("Position", 0))
		var pos_b = int(b.get("Position", 0))
		if pos_a != pos_b:
			return pos_a > pos_b

		var level_a = int(a.get("Level", 1))
		var level_b = int(b.get("Level", 1))
		return level_a > level_b
	)

	for m in members:
		var row = CLUB_MEMBER_ROW_SCENE.instantiate()
		var is_online: = bool(m.get("IsOnline", false))
		var member_position: = int(m.get("Position", 0))
		var member_role_style: Dictionary = MEMBER_ROLE_STYLES.get(member_position, MEMBER_ROLE_STYLES[0])
		var is_me: = false
		if is_instance_valid(target_player) and "data_model" in target_player:
			is_me = int(m.get("DBID", 0)) == int(target_player.data_model.db_id)
		var primary_text_color: = Color(0.9216, 0.9608, 1.0, 1.0) if is_online else Color(0.62, 0.68, 0.74, 1.0)
		var secondary_text_color: = Color(0.7412, 0.8039, 0.8902, 1.0) if is_online else Color(0.48, 0.54, 0.6, 1.0)

		if is_online:
			row.tooltip_text = "目前狀態：線上"
		else:
			var last_login = str(m.get("LastLoginDate", "未知"))
			if last_login != "未知":
				last_login = last_login.split(".")[0].replace("T", " ")
			row.tooltip_text = "最後上線時間：" + last_login

		var lbl_m_name = row.get_node_or_null("LblName")
		var lbl_m_lv = row.get_node_or_null("LblLevel")
		var lbl_m_career = row.get_node_or_null("LblCareer")
		var lbl_m_donation = row.get_node_or_null("LblDonation")
		var lbl_m_status = row.get_node_or_null("LblStatus")
		var status_dot = row.get_node_or_null("StatusDot")
		var role_badge = row.get_node_or_null("LeaderBadge")
		var self_badge = row.get_node_or_null("SelfBadge")

		if lbl_m_name:
			lbl_m_name.text = str(m.get("Name", ""))
			lbl_m_name.add_theme_color_override("font_color", primary_text_color)

		if lbl_m_lv:
			lbl_m_lv.text = "Lv." + str(int(m.get("Level", 1)))
			lbl_m_lv.add_theme_color_override("font_color", secondary_text_color)

		if lbl_m_career:
			var career_id = int(m.get("Career", 0))
			var job_id = int(m.get("Job", 0))
			var career_text = "遊民"

			if career_id > 0 and GameConstants.CAREER_NAMES.has(career_id):
				var base_career_name = GameConstants.CAREER_NAMES[career_id]
				if job_id == 1:
					career_text = "見習" + base_career_name
				else:
					career_text = base_career_name

			lbl_m_career.text = career_text
			lbl_m_career.add_theme_color_override("font_color", secondary_text_color)

		if lbl_m_donation:
			lbl_m_donation.text = "%s" % _format_amount(int(m.get("Donation", 0)))

		if lbl_m_status:
			lbl_m_status.text = "在線" if is_online else "離線"
			lbl_m_status.add_theme_color_override(
				"font_color", 
				Color(0.298, 0.933, 0.898, 1.0) if is_online else Color(0.48, 0.54, 0.6, 1.0)
			)

		if status_dot:
			status_dot.modulate = Color.WHITE if is_online else Color(0.58, 0.5, 0.35, 0.82)
		if role_badge:
			role_badge.text = str(member_role_style["text"])
			role_badge.add_theme_color_override("font_color", member_role_style["font_color"])
			var role_style: = role_badge.get_theme_stylebox("normal").duplicate() as StyleBoxFlat
			role_style.bg_color = member_role_style["background_color"]
			role_style.border_color = member_role_style["border_color"]
			role_badge.add_theme_stylebox_override("normal", role_style)
			role_badge.visible = true
		if self_badge:
			self_badge.visible = is_me

		list_member.add_child(row)
