
extends Control


const DOLL_SCENE = preload("res://paper_doll_engine.tscn")

@onready var v_box_list = $Panel / ScrollContainer / VBoxContainer
@onready var label_top1_message = $Panel / Top1MessageLabel
@onready var btn_edit_message = $Panel / EditMessageButton
@onready var label_sort_name = $Panel / TitleLabel
@onready var label_sort_item = $Panel / TitleColumn / ValueLabel
@onready var btn_close = $Panel / CloseButton


@onready var char_preview = $Panel / LabelCharPreview / CharPreview

const RANK_ITEM_SCENE = preload("res://rank_item_row.tscn")

var current_rank_type: int = 1
var _top1_paper_doll: Node2D = null
var _pending_message_rank_type: int = -1

func _ready() -> void :

	if NetworkManager.has_signal("rank_data_received"):
		NetworkManager.rank_data_received.connect(_on_rank_data_received)
	if NetworkManager.has_signal("update_rank_message_response_received"):
		NetworkManager.update_rank_message_response_received.connect(_on_update_rank_message_response_received)

	btn_edit_message.pressed.connect(_on_edit_message_pressed)
	btn_edit_message.hide()
	if btn_close: btn_close.pressed.connect( func(): queue_free())


	request_rank_data(current_rank_type)

func request_rank_data(type: int) -> void :
	current_rank_type = type
	var req = {
		"Type": "RankDataRequest", 
		"RankType": current_rank_type
	}
	NetworkManager.send_secure_packet(req)

func _on_rank_data_received(packet: Dictionary) -> void :
	if packet.get("RankType") == current_rank_type:
		_refresh_ui(packet)

func _on_update_rank_message_response_received(success: bool, message: String) -> void :
	if _pending_message_rank_type < 0:
		return

	var submitted_rank_type = _pending_message_rank_type
	_pending_message_rank_type = -1

	if GameManager and GameManager.current_hud:
		if success:

			if current_rank_type == submitted_rank_type:
				request_rank_data(submitted_rank_type)

			if GameManager.current_hud.has_method("show_system_message_flash"):
				GameManager.current_hud.show_system_message_flash(message)
		else:

			if GameManager.current_hud.has_method("show_rejection_message_box"):
				GameManager.current_hud.show_rejection_message_box(message)

func _refresh_ui(packet: Dictionary) -> void :

	for child in v_box_list.get_children():
		child.queue_free()


	var rank_list = packet.get("RankList", [])
	var is_money = (current_rank_type == 2)
	var is_top_1_me = false

	for item in rank_list:
		var row = RANK_ITEM_SCENE.instantiate() as RankItemRow
		v_box_list.add_child(row)
		row.setup(item["Rank"], item["Name"], item["Value"], item["DBID"], is_money)

		if item["Rank"] == 1 and GameManager.current_player != null:
			var player_data = GameManager.current_player.data_model
			if current_rank_type == 3:
				if item["DBID"] == player_data.club_id and player_data.club_position == 3:
					is_top_1_me = true
			elif item["DBID"] == player_data.db_id:
				is_top_1_me = true


	label_top1_message.text = packet.get("Top1Message", "尚無宣言")
	btn_edit_message.visible = is_top_1_me


	var equips = packet.get("Top1Equips")
	if equips != null and not equips.is_empty():
		_render_top1_doll(equips)
	else:

		if is_instance_valid(_top1_paper_doll):
			_top1_paper_doll.queue_free()
			_top1_paper_doll = null

	if current_rank_type == 1:
		label_sort_name.text = "等級排行"
		label_sort_item.text = "等級"
	elif current_rank_type == 2:
		label_sort_name.text = "財富排行"
		label_sort_item.text = "星幣"
	elif current_rank_type == 4:
		label_sort_name.text = "功勳排行"
		label_sort_item.text = "功勳"


func _render_top1_doll(equips: Dictionary) -> void :
	if not char_preview: return


	if not is_instance_valid(_top1_paper_doll):
		for child in char_preview.get_children():
			child.queue_free()

		_top1_paper_doll = DOLL_SCENE.instantiate()
		char_preview.add_child(_top1_paper_doll)


		_top1_paper_doll.position = Vector2(0, 40)


	var race_idx = int(equips.get("Race", 0))
	var race_name = GameConstants.RACE_ID_MAP.get(race_idx, "Boy")
	var hair_id = int(equips.get("Hair", 0))
	var hair_name = "Hair%03d" % hair_id
	var hair_color = int(equips.get("HairColor", 0))
	var body_color = int(equips.get("EquipColor0", 0))


	var equips_dict = {
		0: int(equips.get("Bonnet", 0)), 
		1: int(equips.get("Left_hand", 0)), 
		2: int(equips.get("Right_hand", 0)), 
		3: int(equips.get("Upper_armor", 0)), 
		4: int(equips.get("Lower_armor", 0)), 
		5: int(equips.get("Weapon", 0)), 
		6: int(equips.get("Shield", 0)), 
		7: int(equips.get("Back", 0))
	}


	if race_idx == 3:
		for part in [1, 2, 3, 4]:
			if equips_dict[part] <= 0: equips_dict[part] = 0


	_top1_paper_doll.build_human_model(race_name, hair_name, hair_color, body_color, equips_dict)


	_top1_paper_doll.update_animation("emote_a", "8", false, 1.0, 1.0)

func _on_edit_message_pressed() -> void :

	if GameManager and GameManager.current_hud:
		if GameManager.current_hud.has_method("show_input_box"):
			GameManager.current_hud.show_input_box(
				"編輯宣言", 
				_on_message_submitted, 
				Callable(), 
				"請輸入 20 字以內的宣言", 
				20
			)

func _on_message_submitted(text: String) -> void :
	if text.is_empty(): return

	_pending_message_rank_type = current_rank_type
	var req = {
		"Type": "UpdateRankMessage", 
		"RankType": _pending_message_rank_type, 
		"Message": text
	}
	NetworkManager.send_secure_packet(req)

func _exit_tree() -> void :

	if NetworkManager.has_signal("rank_data_received") and NetworkManager.rank_data_received.is_connected(_on_rank_data_received):
		NetworkManager.rank_data_received.disconnect(_on_rank_data_received)
	if NetworkManager.has_signal("update_rank_message_response_received") and NetworkManager.update_rank_message_response_received.is_connected(_on_update_rank_message_response_received):
		NetworkManager.update_rank_message_response_received.disconnect(_on_update_rank_message_response_received)
