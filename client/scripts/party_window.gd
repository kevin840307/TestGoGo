
extends Control

@onready var btn_close = $BtnClose


@onready var btn_create = $BtnCreateParty
@onready var btn_leave = $BtnLeaveParty
@onready var btn_disband = $BtnDisbandParty


@onready var scroll_container = $ScrollContainer
@onready var members_container = $ScrollContainer / MembersContainer
@onready var label_member_count = $Background / LabelMemberCount
@onready var empty_state = $EmptyState

const PARTY_MEMBER_ROW_SCENE = preload("res://party_member_row.tscn")
const MAX_PARTY_MEMBERS: = 6
const PARTY_PLAYER_COLORS: Array[Color] = [
	Color8(57, 157, 230, 200), 
	Color8(213, 20, 24, 200), 
	Color8(8, 178, 16, 200), 
	Color8(230, 97, 213, 200), 
	Color8(238, 234, 49, 200), 
	Color8(255, 157, 8, 200)
]

func _ready() -> void :
	if btn_close: btn_close.pressed.connect( func(): visible = false)

	if btn_create: btn_create.pressed.connect(_on_btn_create_pressed)
	if btn_leave: btn_leave.pressed.connect(_on_btn_leave_pressed)
	if btn_disband: btn_disband.pressed.connect(_on_btn_disband_pressed)
	if btn_create: btn_create.tooltip_text = "建立隊伍"
	if btn_leave: btn_leave.tooltip_text = "退出隊伍"

	if NetworkManager:
		NetworkManager.party_sync_received.connect(_on_party_sync_received)
		NetworkManager.party_stat_update_received.connect(_on_party_stat_update_received)

	update_ui()

func update_ui() -> void :
	if not GameManager.current_player or not "data_model" in GameManager.current_player:
		return

	var data = GameManager.current_player.data_model
	var has_party: bool = data.party_id != ""
	var is_leader: bool = data.party_leader_dbid == data.db_id
	var member_count: int = mini(data.party_members.size(), MAX_PARTY_MEMBERS)


	btn_create.disabled = has_party


	btn_leave.disabled = not has_party

	btn_disband.visible = has_party and is_leader
	btn_disband.disabled = not btn_disband.visible

	label_member_count.text = "成員 %d / %d" % [member_count, MAX_PARTY_MEMBERS]
	var show_empty_state: bool = not has_party or member_count == 0
	empty_state.visible = show_empty_state
	scroll_container.visible = not show_empty_state

	_render_member_list(data.party_members, data.party_leader_dbid)

func _on_btn_create_pressed() -> void :
	NetworkManager.send_party_action("Create")

func _on_btn_leave_pressed() -> void :
	NetworkManager.send_party_action("Leave")

func _on_btn_disband_pressed() -> void :
	NetworkManager.send_party_action("Disband")

func _on_party_sync_received(party_id: String, leader_dbid: int, members: Array) -> void :
	update_ui()

func _on_party_stat_update_received(dbid: int, hp: int, max_hp: int, mp: int, max_mp: int, sp: int, max_sp: int, level: int) -> void :
	for child in members_container.get_children():
		if child.has_method("get_dbid") and child.get_dbid() == dbid:
			if child.has_method("update_stats"):
				child.update_stats(hp, max_hp, mp, max_mp, sp, max_sp)
			if level >= 0 and child.has_method("update_level"):
				child.update_level(level)
			break

func _render_member_list(members: Array, leader_dbid: int) -> void :
	for child in members_container.get_children():
		child.queue_free()

	var member_count: = mini(members.size(), MAX_PARTY_MEMBERS)
	for member_index in range(member_count):
		var member = members[member_index]
		if typeof(member) != TYPE_DICTIONARY:
			continue

		var row = PARTY_MEMBER_ROW_SCENE.instantiate()
		members_container.add_child(row)


		var is_leader = (int(member.get("DBID", 0)) == leader_dbid)


		var is_me = false
		if GameManager.current_player and "data_model" in GameManager.current_player:
			is_me = (GameManager.current_player.data_model.db_id == int(member.get("DBID", 0)))

		if row.has_method("setup"):
			row.setup(member, is_leader, is_me, PARTY_PLAYER_COLORS[member_index])
		if row.has_signal("member_clicked"):
			row.member_clicked.connect(_on_member_clicked)

func _on_member_clicked(member_dbid: int, member_name: String) -> void :
	if not GameManager.current_player or not "data_model" in GameManager.current_player:
		return

	var data = GameManager.current_player.data_model
	if data.party_leader_dbid != data.db_id or member_dbid == data.db_id:
		return

	var hud = GameManager.current_hud
	if not hud or not hud.has_method("show_message_box"):
		return

	hud.show_message_box(
		"是否要將%s踢出隊伍?" % member_name, 
		2, 
		func(): NetworkManager.send_party_action("Kick", member_dbid)
	)
