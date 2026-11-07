
extends Node

var USE_DDNS: bool = false
var SERVER_URL = "ws://127.0.0.1:5678"
var socket = WebSocketPeer.new()
var is_connected_to_server = false

var _is_waiting_logout_confirm = false
var _logout_timeout_timer = 0.0
const LOGOUT_TIMEOUT_MAX = 2.0
const RUNTIME_PREDICTION_INTERVAL: = 0.1
const IN_WORLD_PACKET_LIMIT_PER_FRAME: = 48
const IN_WORLD_PACKET_TIME_BUDGET_USEC: = 4000
const PRE_WORLD_PACKET_LIMIT_PER_FRAME: = 128
const PRE_WORLD_PACKET_TIME_BUDGET_USEC: = 8000
var _runtime_prediction_elapsed: float = 0.0
var _map_backlog_tracking: bool = false
var _map_backlog_transition_id: int = 0
var _map_backlog_target_map: int = 0
var _map_backlog_max_packets: int = 0
var _map_backlog_started_usec: int = 0
var _last_received_packet_type: String = ""
var _last_received_entity_type: int = -1
var _drain_packet_types: Dictionary = {}
var _drain_spawn_entity_types: Dictionary = {}
var _unknown_server_packet_types: Dictionary = {}

var _logout_action: String = "quit"
var latest_skill_sync: Array = []
var has_latest_skill_sync: bool = false




signal register_account_response_received(success: bool, msg: String)
signal login_finished(success: bool, msg: String)
signal character_password_modify_finished(success: bool, msg: String, char_slot: int, new_pwd: String)
signal enter_world_success(server_full_data: Dictionary)
signal create_character_response_received(success: bool, message: String)
signal delete_character_response_received(success: bool, message: String, char_slot: int, deleted_dbid: int)
signal entity_name_color_updated(entity_id: int, color_state: int, pk_status: int, red_man_kill_left: int, party_id: String)

signal inventory_synced(items: Array, max_size: int)
signal equip_synced(equips: Array, max_size: int)
signal pet_synced(pets: Array, max_size: int)
signal horse_synced(horses: Array, equips: Array, max_horse_eq_size: int)
signal skill_synced(skills: Array)
signal buffs_synced(buffs: Array)
signal title_unlocked(title_id: int)

signal entity_spawned(entity_data: Dictionary)
signal entity_despawned(entity_data: Dictionary)
signal entity_moved(entity_data: Dictionary)
signal entity_state_changed(entity_id: int, new_state: String, new_dir: String)

signal equip_response(success: bool, message: String, data: Dictionary)
signal entity_appearance_updated(entity_data: Dictionary)
signal chat_broadcast_received(entity_id: int, sender_name: String, race_id: int, channel: int, message: String, bubble_type: int)
signal pet_idle_speech_received(entity_id: int, message: String)

signal npc_dialog_received(data: Dictionary)
signal npc_dialog_closed()
signal change_map_received(map_id: int, target_x: float, target_y: float)
signal train_travel_started(data: Dictionary)
signal festival_theme_received(theme: String)
signal festival_activity_received(festival: String)
signal monster_target_changed(entity_id: int, has_target: bool)

signal club_sync_received(data: Dictionary)
signal club_invite_received(inviter_dbid: int, inviter_name: String, club_name: String)
signal open_create_club_window_received()

signal system_message_flash_received(message: String)
signal system_message_chat_received(message: String)
signal system_message_broadcast_received(message: String)
signal item_pickup_feedback_received(data: Dictionary)
signal open_bank_window_received()
signal open_repair_window_received(repair_type: int)

signal entity_damage_received(data: Dictionary)
signal entity_death_received(data: Dictionary)
signal entity_heal_received(data: Dictionary)




signal party_sync_received(party_id: String, leader_dbid: int, members: Array)
signal party_invite_received(inviter_dbid: int, inviter_name: String)
signal party_stat_update_received(dbid: int, hp: int, max_hp: int, mp: int, max_mp: int, sp: int, max_sp: int, level: int)




signal trade_invite_received(requester_dbid: int, requester_name: String)
signal trade_start_received(session_id: String, target_name: String)
signal trade_sync_received(my_money: int, other_money: int, my_items: Array, other_items: Array, my_locked: bool, other_locked: bool)
signal trade_cancel_received(message: String)
signal trade_success_received(message: String)
signal trade_final_confirm_req_received()

signal shop_view_response_received(shop_name: String, owner_dbid: int, items: Array)
signal shop_sync_received(items: Array)
signal sync_shop_board_received(entity_id: int, is_open: bool, shop_name: String)

signal npc_shop_view_response_received(shop_id: int, shop_name: String, items: Array)

signal status_synced(entity_id: int, hp: int, mp: int, sp: int)

signal drive_mecha_response_received(success: bool, horse_uid: int, message: String)
signal sync_mecha_energy_received(horse_uid: int, energy_left: int, exp: int, max_exp: int, level: int)

signal skill_cast_received(caster_id: int, caster_type: int, skill_id: int, skill_level: int, target_id: int, target_x: int, target_y: int, aoe_targets: Array)

signal quest_synced(quests: Array)
signal quest_updated(quest_data: Dictionary)

signal open_megaphone_window_received(item_uid: int)

signal mall_list_received(points: int, items: Array, gifts: Array)
signal mall_points_sync_received(points: int)
signal paypal_donation_created_received(success: bool, message: String, approval_url: String)
signal legacy_point_withdrawal_response_received(success: bool, operation: String, message: String, legacy_points: int)

signal open_rank_window_received(rank_type: int)
signal rank_data_received(data: Dictionary)
signal update_rank_message_response_received(success: bool, message: String)
signal announcement_list_received(data: Dictionary)




signal craft_item_response_received(success: bool, message: String, result_items: Array)
signal dismantle_item_response_received(success: bool, message: String, result_items: Array)




signal toggle_auto_saint_response_received(is_on: bool)




var aes: AESContext = AESContext.new()
var aes_key: PackedByteArray = "Paradise25DGameSecretKey2026Base".to_utf8_buffer()
var aes_iv: PackedByteArray = "InitVectorVector".to_utf8_buffer()

func _ready() -> void :


	socket.inbound_buffer_size = 2097152
	socket.outbound_buffer_size = 2097152
	get_tree().set_auto_accept_quit(false)

func _notification(what: int) -> void :
	if what == NOTIFICATION_WM_CLOSE_REQUEST:
		_handle_graceful_quit()

func _handle_graceful_quit() -> void :
	if _is_waiting_logout_confirm: return
	log_client_exit("window_close_requested")

	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_logout_request("quit")
	else:
		print("尚未連線或連線已斷開，直接退出。")
		_force_exit_game()

func log_client_exit(reason: String) -> void :
	var dbid: = 0
	var map_id: = 0
	if GameManager and is_instance_valid(GameManager.current_player):
		var player_data = GameManager.current_player.get("data_model")
		if player_data:
			dbid = player_data.db_id
			map_id = player_data.map_id
	var line: = "[ClientLifecycle] time=%s pid=%d dbid=%d map=%d exit=%s" % [Time.get_datetime_string_from_system(), OS.get_process_id(), dbid, map_id, reason]

	if GameManager and GameManager.has_method("append_lifecycle_diagnostic"):
		GameManager.append_lifecycle_diagnostic(line)

func note_map_loading_complete(transition_id: int, target_map_id: int) -> void :
	_map_backlog_tracking = true
	_map_backlog_transition_id = transition_id
	_map_backlog_target_map = target_map_id
	_map_backlog_max_packets = max(_map_backlog_max_packets, socket.get_available_packet_count())
	if _map_backlog_started_usec == 0:
		_map_backlog_started_usec = Time.get_ticks_usec()
	if GameManager and GameManager.has_method("trace_map_diagnostic"):
		GameManager.trace_map_diagnostic("network_backlog_ready", {
			"target_map": target_map_id, 
			"queued_packets": socket.get_available_packet_count(), 
			"transition_id": transition_id, 
		})
	if socket.get_available_packet_count() == 0:
		_map_backlog_tracking = false
		_map_backlog_transition_id = 0
		_map_backlog_target_map = 0
		_map_backlog_max_packets = 0
		_map_backlog_started_usec = 0

func _force_exit_game(reason: String = "forced_exit") -> void :
	log_client_exit(reason)
	if socket: socket.close()
	get_tree().quit()

func _process(delta: float) -> void :
	socket.poll()
	var state = socket.get_ready_state()

	if state == WebSocketPeer.STATE_OPEN:
		is_connected_to_server = true
		var available_packet_count: = socket.get_available_packet_count()
		if available_packet_count > 0:
			var drain_started_usec: = Time.get_ticks_usec()
			var drained_packets: = 0
			_drain_packet_types.clear()
			_drain_spawn_entity_types.clear()
			var drain_budget_hit: = false
			var is_in_world: = GameManager and is_instance_valid(GameManager.current_player)
			var packet_limit: = IN_WORLD_PACKET_LIMIT_PER_FRAME if is_in_world else PRE_WORLD_PACKET_LIMIT_PER_FRAME
			var time_budget_usec: = IN_WORLD_PACKET_TIME_BUDGET_USEC if is_in_world else PRE_WORLD_PACKET_TIME_BUDGET_USEC
			if _map_backlog_tracking:
				_map_backlog_max_packets = max(_map_backlog_max_packets, available_packet_count)
			while socket.get_available_packet_count() > 0:


				if GameManager.get("is_changing_map"):
					_map_backlog_tracking = true
					_map_backlog_transition_id = GameManager.get_active_map_transition_id()
					_map_backlog_max_packets = max(_map_backlog_max_packets, socket.get_available_packet_count())
					if _map_backlog_started_usec == 0:
						_map_backlog_started_usec = Time.get_ticks_usec()
					break
				if drained_packets >= packet_limit or (drained_packets > 0 and Time.get_ticks_usec() - drain_started_usec >= time_budget_usec):
					drain_budget_hit = true
					break

				var packet_raw_cipher = socket.get_packet().get_string_from_utf8()
				_last_received_packet_type = ""
				_last_received_entity_type = -1
				_handle_encrypted_server_message(packet_raw_cipher)
				drained_packets += 1
				if not _last_received_packet_type.is_empty():
					_drain_packet_types[_last_received_packet_type] = int(_drain_packet_types.get(_last_received_packet_type, 0)) + 1
				if _last_received_packet_type == "SpawnEntity":
					var entity_key: = str(_last_received_entity_type)
					_drain_spawn_entity_types[entity_key] = int(_drain_spawn_entity_types.get(entity_key, 0)) + 1
			if _map_backlog_tracking and drained_packets > 0:
				var remaining_packets: = socket.get_available_packet_count()
				if GameManager and GameManager.has_method("trace_map_diagnostic"):
					GameManager.trace_map_diagnostic("network_drain_frame", {
						"target_map": _map_backlog_target_map, 
						"transition_id": _map_backlog_transition_id, 
						"duration_ms": float(Time.get_ticks_usec() - drain_started_usec) / 1000.0, 
						"drained_packets": drained_packets, 
						"remaining_packets": remaining_packets, 
						"max_queued_packets": _map_backlog_max_packets, 
						"packet_limit": packet_limit, 
						"time_budget_usec": time_budget_usec, 
						"budget_hit": drain_budget_hit, 
						"packet_types": _drain_packet_types.duplicate(), 
						"spawn_entity_types": _drain_spawn_entity_types.duplicate(), 
					})
				if remaining_packets == 0:
					if GameManager and GameManager.has_method("trace_map_diagnostic"):
						GameManager.trace_map_diagnostic("network_backlog_drained", {
							"target_map": _map_backlog_target_map, 
							"transition_id": _map_backlog_transition_id, 
							"backlog_elapsed_ms": float(Time.get_ticks_usec() - _map_backlog_started_usec) / 1000.0, 
							"max_queued_packets": _map_backlog_max_packets, 
						})
					_map_backlog_tracking = false
					_map_backlog_transition_id = 0
					_map_backlog_target_map = 0
					_map_backlog_max_packets = 0
					_map_backlog_started_usec = 0

	elif state == WebSocketPeer.STATE_CLOSED:
		if is_connected_to_server:
			print("❌ 與 C# 伺服器(Port:5678) 斷開連線。")
			is_connected_to_server = false
		if _is_waiting_logout_confirm:
			_is_waiting_logout_confirm = false
			if _logout_action == "login":
				print("伺服器端已中斷 Socket，準備返回登入頁面。")
				if GameManager.has_method("cleanup_and_return_to_login"):
					GameManager.cleanup_and_return_to_login()
			else:
				print("伺服器端已安全收網中斷 Socket，客戶端退出遊戲。")
				_force_exit_game("logout_connection_closed")

	if _is_waiting_logout_confirm:
		_logout_timeout_timer += delta
		if _logout_timeout_timer >= LOGOUT_TIMEOUT_MAX:
			print("⚠️ 等待伺服器登出回應逾時！啟動防卡死安全強制退出程序。")
			_is_waiting_logout_confirm = false
			if _logout_action == "login":
				if socket: socket.close()
				if GameManager.has_method("cleanup_and_return_to_login"):
					GameManager.cleanup_and_return_to_login()
			else:
				_force_exit_game("logout_timeout")

	if is_connected_to_server and GameManager and is_instance_valid(GameManager.current_player):
		var p_data = GameManager.current_player.get("data_model")
		if p_data:
			_runtime_prediction_elapsed += delta
			if _runtime_prediction_elapsed >= RUNTIME_PREDICTION_INTERVAL:
				_update_runtime_predictions(p_data, _runtime_prediction_elapsed)
				_runtime_prediction_elapsed = 0.0
		else:
			_runtime_prediction_elapsed = 0.0
	else:
		_runtime_prediction_elapsed = 0.0

func _update_runtime_predictions(p_data: PlayerData, elapsed: float) -> void :

	if p_data.summoned_horse_uid != -1:
		for h in p_data.horse_list:
			if h.get("horse_uid", -1) == p_data.summoned_horse_uid:
				var exact_en = h.get("exact_energy", float(h.get("energy_left", 0)))
				if exact_en > 0:
					exact_en -= elapsed
					h["exact_energy"] = exact_en
					h["energy_left"] = int(exact_en)
				break


	for p in p_data.pet_list:
		if int(p.get("state", 0)) == 1:
			var exact_feed = p.get("exact_feed", float(p.get("feed_value", 0)))
			if exact_feed > 0:
				exact_feed -= elapsed
				p["exact_feed"] = exact_feed
				p["feed_value"] = int(exact_feed)

func connect_to_server() -> bool:
	if socket.get_ready_state() == WebSocketPeer.STATE_OPEN: return true
	socket.close()


	if USE_DDNS:
		var success = await connect_to_server_via_ddns()
		if not success:
			return false

	if socket.connect_to_url(SERVER_URL) != OK:
		print("❌ 連線至伺服器失敗，URL: ", SERVER_URL)
		return false
	return true

func send_secure_packet(data_dict: Dictionary) -> void :
	if socket.get_ready_state() != WebSocketPeer.STATE_OPEN:
		print("❌ 發送失敗：連線未開啟。")
		return
	var plain_bytes = JSON.stringify(data_dict).to_utf8_buffer()
	var pad_required = 16 - (plain_bytes.size() % 16)
	for i in range(pad_required): plain_bytes.append(pad_required)
	aes.start(AESContext.MODE_CBC_ENCRYPT, aes_key, aes_iv)
	var encrypted_bytes = aes.update(plain_bytes)
	aes.finish()
	socket.send_text(Marshalls.raw_to_base64(encrypted_bytes))

func send_announcement_list_request() -> void :
	send_secure_packet({"Type": "AnnouncementListRequest"})

func send_world_ready(map_id: int) -> void :
	if map_id <= 0:
		return
	send_secure_packet({
		"Type": "WorldReady",
		"MapID": map_id,
	})

func send_login_request(user: String, pass_str: String) -> void :
	var connected = await connect_to_server()
	if not connected:
		login_finished.emit(false, "無法連線至伺服器主機。")
		return
	for i in range(10):
		if socket.get_ready_state() == WebSocketPeer.STATE_OPEN: break
		await get_tree().create_timer(0.1).timeout
	if socket.get_ready_state() != WebSocketPeer.STATE_OPEN:
		login_finished.emit(false, "連線至伺服器逾時！")
		return
	send_secure_packet({"Type": "LoginRequest", "User": user, "Pass": pass_str})




func send_register_account_request(login_name: String, password: String, email: String) -> void :
	var connected = await connect_to_server()
	if not connected:
		register_account_response_received.emit(false, "無法連線至伺服器主機。")
		return

	for i in range(10):
		if socket.get_ready_state() == WebSocketPeer.STATE_OPEN: break
		await get_tree().create_timer(0.1).timeout

	if socket.get_ready_state() != WebSocketPeer.STATE_OPEN:
		register_account_response_received.emit(false, "連線至伺服器逾時！")
		return

	var packet = {
		"Type": "RegisterAccountRequest", 
		"LoginName": login_name, 
		"Password": password, 
		"EMAIL": email
	}
	send_secure_packet(packet)

func send_create_character_request(acc_id: int, char_name: String, race: int, hair: int, hair_color: int, pwd: String, str_pts: int, dex_pts: int, con_pts: int, wis_pts: int) -> void :
	if socket.get_ready_state() != WebSocketPeer.STATE_OPEN:
		create_character_response_received.emit(false, "無法發送創建角色請求，網路已斷線。")
		return
	var packet = {
		"Type": "CreateCharacterRequest", 
		"ACCID": acc_id, 
		"Name": char_name, 
		"Race": race, 
		"Hair": hair, 
		"HairColor": hair_color, 
		"Password": pwd, 
		"Str": str_pts, 
		"Dex": dex_pts, 
		"Con": con_pts, 
		"Wis": wis_pts
	}
	send_secure_packet(packet)

func send_delete_character_request(acc_id: int, dbid: int, pwd: String, char_slot: int) -> void :
	if socket.get_ready_state() != WebSocketPeer.STATE_OPEN:
		delete_character_response_received.emit(false, "網路已斷線，無法發送刪除角色請求。", char_slot, -1)
		return

	var packet = {
		"Type": "DeleteCharacterRequest", 
		"ACCID": acc_id, 
		"DBID": dbid, 
		"Password": pwd, 
		"CharSlot": char_slot
	}
	send_secure_packet(packet)

func send_modify_password_request(char_slot: int, dbid: int, new_pwd: String) -> void :
	if socket.get_ready_state() != WebSocketPeer.STATE_OPEN:
		character_password_modify_finished.emit(false, "與伺服器斷開連線，無法發送請求。", char_slot, new_pwd)
		return
	send_secure_packet({"Type": "ModifyPasswordRequest", "CharSlot": char_slot, "DBID": dbid, "NewPwd": new_pwd})

func send_select_character_request(dbid: int) -> void :
	latest_skill_sync.clear()
	has_latest_skill_sync = false
	if socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({"Type": "SelectCharacterRequest", "DBID": dbid})

func send_equip_request(item_uid: int, target_slot: int) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({"Type": "EquipRequest", "ItemUID": item_uid, "TargetSlot": target_slot})

func send_unequip_request(target_slot: int) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({"Type": "UnequipRequest", "TargetSlot": target_slot})

func send_use_item_request(item_uid: int) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({"Type": "UseItemRequest", "ItemUID": item_uid})

func send_logout_request(action: String = "quit") -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		if not _is_waiting_logout_confirm:
			_logout_action = action
			var packet = {"Type": "LogoutRequest", "IsBackToLogin": (action == "login")}


			if GameManager.current_player and "is_dead" in GameManager.current_player and GameManager.current_player.is_dead:
				packet["DeathTimer"] = int(GameManager.current_player.death_timer)

			send_secure_packet(packet)
			_is_waiting_logout_confirm = true
			_logout_timeout_timer = 0.0
	else:
		if action == "login" and GameManager.has_method("cleanup_and_return_to_login"):
			GameManager.cleanup_and_return_to_login()

func send_sync_position(x: int, y: int) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({"Type": "SyncPosition", "X": x, "Y": y})

func send_train_travel_ready(travel_id: String) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN and not travel_id.is_empty():
		send_secure_packet({"Type": "TrainTravelReady", "TravelID": travel_id})

func send_pickup_item_request(entity_id: int) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({"Type": "PickUpItemRequest", "EntityID": entity_id})

func send_revive_request() -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({"Type": "ReviveRequest"})




func _handle_encrypted_server_message(base64_cipher_text: String) -> void :
	var encrypted_bytes = Marshalls.base64_to_raw(base64_cipher_text)
	if encrypted_bytes.is_empty(): return

	aes.start(AESContext.MODE_CBC_DECRYPT, aes_key, aes_iv)
	var decrypted_bytes = aes.update(encrypted_bytes)
	aes.finish()
	if decrypted_bytes.is_empty(): return

	var pad_amount = decrypted_bytes[decrypted_bytes.size() - 1]
	if pad_amount > 0 and pad_amount <= 16:
		decrypted_bytes = decrypted_bytes.slice(0, decrypted_bytes.size() - pad_amount)

	var json = JSON.new()
	if json.parse(decrypted_bytes.get_string_from_utf8()) != OK: return

	var data = json.get_data()
	var packet_type = data.get("Type", "")
	_last_received_packet_type = str(packet_type)
	_last_received_entity_type = int(data.get("EntityType", -1)) if packet_type == "SpawnEntity" else -1


	match packet_type:
		"LoginResponse":
			var success = data.get("Success", false)
			if success: GlobalTempData.character_list = data.get("Characters", [])
			login_finished.emit(success, data.get("Message", ""))

		"RegisterAccountResponse":
			register_account_response_received.emit(data.get("Success", false), data.get("Message", ""))

		"ForceLogout":
			var msg = data.get("Message", "您的帳號已在其他地方登入！")


			_is_waiting_logout_confirm = false


			if GameManager.has_method("cleanup_and_return_to_login"):
				GameManager.cleanup_and_return_to_login()


			get_tree().create_timer(0.5).timeout.connect( func():
				var main_entry = get_tree().current_scene
				if main_entry and main_entry.has_method("show_error_popup"):
					main_entry.show_error_popup(msg)
			)

		"PositionCorrection":
			if GameManager and is_instance_valid(GameManager.current_player):
				var corrected_position := Vector2(
					float(data.get("X", GameManager.current_player.global_position.x)),
					float(data.get("Y", GameManager.current_player.global_position.y))
				)
				if GameManager.current_player.has_method("apply_position_correction"):
					GameManager.current_player.apply_position_correction(corrected_position)

		"CreateCharacterResponse":
			create_character_response_received.emit(data.get("Success", false), data.get("Message", ""))

		"DeleteCharacterResponse":
			var success = data.get("Success", false)
			var msg = data.get("Message", "")
			var char_slot = int(data.get("CharSlot", -1))
			var deleted_dbid = int(data.get("DeletedDBID", -1))
			delete_character_response_received.emit(success, msg, char_slot, deleted_dbid)

		"ModifyPasswordResponse":
			character_password_modify_finished.emit(data.get("Success", false), data.get("Message", ""), int(data.get("CharSlot", -1)), data.get("NewPwd", ""))

		"LogoutResponse":
			var is_back = data.get("IsBackToLogin", false)
			if is_back or _logout_action == "login":
				_logout_action = "login"
			else:
				log_client_exit("logout_response_quit")
				if GameManager.current_hud and GameManager.current_hud.has_method("execute_safe_quit"):
					GameManager.current_hud.execute_safe_quit()
				else:
					_force_exit_game()

		"UnlockTitle":
			var t_id = int(data.get("TitleID", 0))
			if GameManager.current_player and "data_model" in GameManager.current_player:
				var p_data = GameManager.current_player.data_model
				if t_id not in p_data.unlocked_titles:
					p_data.unlocked_titles.append(t_id)

			title_unlocked.emit(t_id)
			var title_name = PlayerData.TITLE_MAP.get(t_id, "未知稱號")
			system_message_flash_received.emit("獲得新稱號：【" + title_name + "】！")


			if GameManager.current_hud and GameManager.current_hud.has_method("get_node_or_null"):
				var char_win = GameManager.current_hud.get_node_or_null("WindowsContainer/CharacterWindow")
				if char_win and char_win.visible and char_win.has_method("update_ui"):
					char_win.update_ui()

		"EnterWorldSuccess":
			var bootstrap_skills: Array = data.get("SkillList", [])
			if not bootstrap_skills.is_empty():
				latest_skill_sync = bootstrap_skills.duplicate(true)
				has_latest_skill_sync = true
			enter_world_success.emit(data)

		"InventorySync": inventory_synced.emit(data.get("Items", []), int(data.get("MaxSize", 200)))
		"EquipSync": equip_synced.emit(data.get("Equips", []), int(data.get("MaxSize", 200)))
		"PetSync":
			var pets = data.get("Pets", [])
			var max_size = int(data.get("MaxSize", 50))
			if GameManager.current_player and "data_model" in GameManager.current_player:
				GameManager.current_player.data_model.update_pets(pets, max_size)
			pet_synced.emit(pets, max_size)

		"HorseSync":
			var horses = data.get("Horses", [])
			var horse_equips = data.get("HorseEquips", [])
			var max_horse_eq_size = int(data.get("MaxHorseEquipSize", 20))

			if GameManager.current_player and "data_model" in GameManager.current_player:
				GameManager.current_player.data_model.update_horses(horses, horse_equips, max_horse_eq_size)

			horse_synced.emit(horses, horse_equips, max_horse_eq_size)

			if GameManager.current_hud and GameManager.current_hud.has_method("_refresh_inventory_if_open"):
				GameManager.current_hud._refresh_inventory_if_open()

		"SkillSync":
			var skills: Array = data.get("Skills", [])
			latest_skill_sync = skills.duplicate(true)
			has_latest_skill_sync = true
			if GameManager.current_player and "data_model" in GameManager.current_player:
				GameManager.current_player.data_model.update_skills(latest_skill_sync)
			skill_synced.emit(latest_skill_sync)
		"SyncBuffs": buffs_synced.emit(data.get("Buffs", []))
		"SpawnEntity": entity_spawned.emit(data)
		"DespawnEntity": entity_despawned.emit(data)
		"MoveEntity": entity_moved.emit(data)
		"SyncState": entity_state_changed.emit(int(data.get("EntityID", 0)), data.get("State", "idle"), data.get("Dir", "1"))
		"MonsterTarget": monster_target_changed.emit(int(data.get("EntityID", 0)), bool(data.get("HasTarget", false)))
		"EquipResponse": equip_response.emit(data.get("Success", false), data.get("Message", ""), data)
		"UpdateAppearance": entity_appearance_updated.emit(data)
		"UpdateNameColor": entity_name_color_updated.emit(int(data.get("EntityID", 0)), int(data.get("ColorState", 0)), int(data.get("PKStatus", -1)), int(data.get("RedManKillLeft", -1)), str(data.get("PartyID", "")))
		"ChatBroadcast": chat_broadcast_received.emit(int(data.get("EntityID", 0)), data.get("SenderName", ""), int(data.get("Race", 0)), int(data.get("Channel", 0)), data.get("Message", ""), int(data.get("BubbleType", 0)))
		"PetIdleSpeech": pet_idle_speech_received.emit(int(data.get("EntityID", 0)), str(data.get("Message", "")))
		"ChangeMap": change_map_received.emit(int(data.get("MapID", 1)), float(data.get("X", 0)), float(data.get("Y", 0)))
		"TrainTravelStart": train_travel_started.emit(data)
		"FestivalThemeSync":
			festival_theme_received.emit(str(data.get("Theme", FestivalThemeManager.NORMAL)))
			festival_activity_received.emit(str(data.get("Festival", "")))

		"NpcDialogResponse":
			if data.get("DialogType", "") == "Close": npc_dialog_closed.emit()
			else: npc_dialog_received.emit(data)

		"SystemMessageFlash": system_message_flash_received.emit(data.get("Message", ""))
		"SystemMessageChat": system_message_chat_received.emit(data.get("Message", ""))
		"SystemMessageBroadcast": system_message_broadcast_received.emit(data.get("Message", ""))
		"ItemPickupFeedback": item_pickup_feedback_received.emit(data)
		"OpenBankWindow": open_bank_window_received.emit()
		"OpenRepairWindow": open_repair_window_received.emit(int(data.get("RepairType", 1)))

		"TradeInvite": trade_invite_received.emit(int(data.get("RequesterDBID", 0)), data.get("RequesterName", ""))
		"TradeStart":
			var t1 = data.get("TargetName1", "")
			var t2 = data.get("TargetName2", "")
			var other_name = t2 if t1 == GlobalTempData.character_list[0].NAME else t1
			trade_start_received.emit(data.get("SessionID", ""), other_name)
		"TradeSync":
			trade_sync_received.emit(
				int(data.get("MyMoney", 0)), int(data.get("OtherMoney", 0)), 
				data.get("MyItems", []), data.get("OtherItems", []), 
				data.get("MyLocked", false), data.get("OtherLocked", false)
			)
		"TradeCancel": trade_cancel_received.emit(data.get("Message", ""))
		"TradeSuccess": trade_success_received.emit(data.get("Message", ""))
		"TradeFinalConfirmReq": trade_final_confirm_req_received.emit()

		"ShopViewResponse": shop_view_response_received.emit(data.get("ShopName", ""), int(data.get("OwnerDBID", 0)), data.get("Items", []))
		"ShopSync": shop_sync_received.emit(data.get("Items", []))
		"SyncShopBoard": sync_shop_board_received.emit(int(data.get("EntityID", 0)), data.get("IsOpen", false), data.get("ShopName", ""))

		"NpcShopViewResponse":
			npc_shop_view_response_received.emit(int(data.get("ShopID", 0)), str(data.get("ShopName", "")), data.get("Items", []))

		"AllocateStatResponse":
			var success = data.get("Success", false)



			if GameManager.current_player and "data_model" in GameManager.current_player:
				var p_data = GameManager.current_player.data_model


				if data.has("ExpUse"): p_data.exp_use = int(data.get("ExpUse", 0))
				if data.has("STR"): p_data.strength = int(data.get("STR", 0))
				if data.has("DEX"): p_data.dexterity = int(data.get("DEX", 0))
				if data.has("CON"): p_data.constitution = int(data.get("CON", 0))
				if data.has("WIS"): p_data.wisdom = int(data.get("WIS", 0))
				if data.has("MaxHP"): p_data.max_hp = int(data.get("MaxHP", 0))
				if data.has("MaxMP"): p_data.max_mp = int(data.get("MaxMP", 0))
				if data.has("MaxSP"): p_data.max_sp = int(data.get("MaxSP", 0))


				if data.has("Attack"): p_data.attack = int(data.get("Attack", 0))
				if data.has("Defense"): p_data.defense = int(data.get("Defense", 0))
				if data.has("MagicAttack"): p_data.magic_attack = int(data.get("MagicAttack", 0))
				if data.has("MagicDefense"): p_data.magic_defense = int(data.get("MagicDefense", 0))
				if data.has("Hit"): p_data.hit = int(data.get("Hit", 0))
				if data.has("Dodge"): p_data.dodge = int(data.get("Dodge", 0))
				if data.has("CritRate"): p_data.crit_rate = int(data.get("CritRate", 0))
				if data.has("CritDamage"): p_data.crit_damage = int(data.get("CritDamage", 0))
				if data.has("AttackRange"): p_data.attack_range = int(data.get("AttackRange", 80))


				if GameManager.current_hud and GameManager.current_hud.has_method("update_status_panel"):
					GameManager.current_hud.update_status_panel()


				if GameManager.current_hud and GameManager.current_hud.has_method("get_node_or_null"):
					var char_win = GameManager.current_hud.get_node_or_null("WindowsContainer/CharacterWindow")
					if char_win and char_win.visible and char_win.has_method("update_ui"):
						char_win.update_ui()

		"DriveMechaResponse":
			var success = data.get("Success", false)
			var h_uid = int(data.get("HorseUID", -1))
			var msg = data.get("Message", "")


			if success and GameManager.current_player and "data_model" in GameManager.current_player:
				var p_data = GameManager.current_player.data_model

				p_data.summoned_horse_uid = h_uid
				p_data.rider_state["SummonedHorseUID"] = h_uid

				if data.has("MaxHP"): p_data.max_hp = int(data.get("MaxHP", p_data.max_hp))
				if data.has("MaxMP"): p_data.max_mp = int(data.get("MaxMP", p_data.max_mp))
				if data.has("MaxSP"): p_data.max_sp = int(data.get("MaxSP", p_data.max_sp))
				if data.has("Attack"): p_data.attack = int(data.get("Attack", p_data.attack))
				if data.has("Defense"): p_data.defense = int(data.get("Defense", p_data.defense))
				if data.has("MagicAttack"): p_data.magic_attack = int(data.get("MagicAttack", p_data.magic_attack))
				if data.has("MagicDefense"): p_data.magic_defense = int(data.get("MagicDefense", p_data.magic_defense))
				if data.has("Hit"): p_data.hit = int(data.get("Hit", p_data.hit))
				if data.has("Dodge"): p_data.dodge = int(data.get("Dodge", p_data.dodge))
				if data.has("Lucky"): p_data.lucky = int(data.get("Lucky", p_data.lucky))
				if data.has("AttackSpeed"): p_data.attack_speed = int(data.get("AttackSpeed", p_data.attack_speed))
				if data.has("AttackRange"): p_data.attack_range = int(data.get("AttackRange", p_data.attack_range))
				if data.has("MoveSpeed"): p_data.move_speed = int(data.get("MoveSpeed", p_data.move_speed))
				if data.has("CritRate"): p_data.crit_rate = int(data.get("CritRate", p_data.crit_rate))
				if data.has("CritDamage"): p_data.crit_damage = int(data.get("CritDamage", p_data.crit_damage))


				if GameManager.current_hud and GameManager.current_hud.has_method("update_status_panel"):
					GameManager.current_hud.update_status_panel()


				if GameManager.current_hud and GameManager.current_hud.has_method("get_node_or_null"):
					var char_win = GameManager.current_hud.get_node_or_null("WindowsContainer/CharacterWindow")
					if char_win and char_win.visible and char_win.has_method("update_ui"):
						char_win.update_ui()

				if GameManager.current_hud and GameManager.current_hud.has_method("_refresh_inventory_if_open"):
					GameManager.current_hud._refresh_inventory_if_open()

			drive_mecha_response_received.emit(success, h_uid, msg)
			if msg != "": system_message_flash_received.emit(msg)

		"SyncMechaEnergy":
			var h_uid = int(data.get("HorseUID", 0))
			var energy = int(data.get("EnergyLeft", 0))

			var exp = int(data.get("Exp", 0))
			var max_exp = int(data.get("MaxExp", 1))
			var level = int(data.get("Level", 1))


			if GameManager.current_player and "data_model" in GameManager.current_player:
				for h in GameManager.current_player.data_model.horse_list:
					if h.get("horse_uid", -1) == h_uid:
						h["energy_left"] = energy
						h["exact_energy"] = float(energy)
						h["exp"] = exp
						h["max_exp"] = max_exp
						h["level"] = level
						break

			sync_mecha_energy_received.emit(h_uid, energy, exp, max_exp, level)

		"SyncMoney":
			var new_money = int(data.get("Money", 0))
			if GameManager.current_player and "data_model" in GameManager.current_player:
				GameManager.current_player.data_model.money = new_money
			if GameManager.current_hud and GameManager.current_hud.has_method("update_status_panel"):
				GameManager.current_hud.update_status_panel()

		"PlayerStatusSync":
			if GameManager.current_player and "data_model" in GameManager.current_player:
				var p_data = GameManager.current_player.data_model
				if data.has("Career"): p_data.career = int(data.get("Career", 0))
				if data.has("Job"): p_data.job = int(data.get("Job", 0))
				if data.has("HP"):
					var new_hp = int(data.get("HP", 0))
					p_data.hp = new_hp
					if GameManager.current_player.get("is_dead") and new_hp > 0:
						if GameManager.current_player.has_method("revive"):
							GameManager.current_player.revive()
				if data.has("MP"): p_data.mp = int(data.get("MP", 0))
				if data.has("SP"): p_data.sp = int(data.get("SP", 0))
				if data.has("Money"): p_data.money = int(data.get("Money", 0))
				if data.has("Level"):
					var new_level = int(data.get("Level", 1))
					var leveled_up = false
					if p_data.level > 0 and new_level > p_data.level:
						leveled_up = true
						if GameManager.current_player.has_method("show_level_up"):
							GameManager.current_player.show_level_up()
					p_data.level = new_level

					if leveled_up and NetworkEntityManager:
						for entity in NetworkEntityManager.active_entities.values():
							if is_instance_valid(entity) and entity.has_method("update_level_icon"):
								entity.update_level_icon()
				if data.has("Str") and "str_pts" in p_data: p_data.str_pts = int(data.get("Str", 0))
				if data.has("Dex") and "dex_pts" in p_data: p_data.dex_pts = int(data.get("Dex", 0))
				if data.has("ExpAcc"): p_data.exp_acc = int(data.get("ExpAcc", 0))
				if data.has("MaxExp"): p_data.max_exp = int(data.get("MaxExp", 0))


				if data.has("ExpUse"): p_data.exp_use = int(data.get("ExpUse", 0))

				if data.has("ExpDeath"): p_data.exp_death = int(data.get("ExpDeath", 0))

				if data.has("MaxHP"): p_data.max_hp = int(data.get("MaxHP", p_data.max_hp))
				if data.has("MaxMP"): p_data.max_mp = int(data.get("MaxMP", p_data.max_mp))
				if data.has("MaxSP"): p_data.max_sp = int(data.get("MaxSP", p_data.max_sp))
				if data.has("Attack"): p_data.attack = int(data.get("Attack", p_data.attack))
				if data.has("Defense"): p_data.defense = int(data.get("Defense", p_data.defense))
				if data.has("MagicAttack"): p_data.magic_attack = int(data.get("MagicAttack", p_data.magic_attack))
				if data.has("MagicDefense"): p_data.magic_defense = int(data.get("MagicDefense", p_data.magic_defense))
				if data.has("Hit"): p_data.hit = int(data.get("Hit", p_data.hit))
				if data.has("Dodge"): p_data.dodge = int(data.get("Dodge", p_data.dodge))
				if data.has("Lucky"): p_data.lucky = int(data.get("Lucky", p_data.lucky))
				if data.has("AttackSpeed"): p_data.attack_speed = int(data.get("AttackSpeed", p_data.attack_speed))
				if data.has("AttackRange"): p_data.attack_range = int(data.get("AttackRange", p_data.attack_range))
				if data.has("MoveSpeed"): p_data.move_speed = int(data.get("MoveSpeed", p_data.move_speed))
				if data.has("CritRate"): p_data.crit_rate = int(data.get("CritRate", p_data.crit_rate))
				if data.has("CritDamage"): p_data.crit_damage = int(data.get("CritDamage", p_data.crit_damage))


				for member in p_data.party_members:
					if int(member.get("DBID", 0)) == p_data.db_id:
						member["HP"] = p_data.hp
						member["MaxHP"] = p_data.max_hp
						member["MP"] = p_data.mp
						member["MaxMP"] = p_data.max_mp
						member["SP"] = p_data.sp
						member["MaxSP"] = p_data.max_sp
						member["Level"] = p_data.level
						party_stat_update_received.emit(
							p_data.db_id, p_data.hp, p_data.max_hp, 
							p_data.mp, p_data.max_mp, p_data.sp, p_data.max_sp, p_data.level
						)
						break

			if GameManager.current_hud and GameManager.current_hud.has_method("update_status_panel"):
				GameManager.current_hud.update_status_panel()
			if GameManager.current_hud and GameManager.current_hud.has_method("_refresh_inventory_if_open"):
				GameManager.current_hud._refresh_inventory_if_open()


			if GameManager.current_hud and GameManager.current_hud.has_method("get_node_or_null"):
				var char_win = GameManager.current_hud.get_node_or_null("WindowsContainer/CharacterWindow")
				if char_win and char_win.visible and char_win.has_method("update_ui"):
					char_win.update_ui()

		"EntityDamage":
			entity_damage_received.emit(data)

			if int(data.get("TargetType", -1)) == 0:
				var target_id = int(data.get("TargetID", 0))
				var current_hp = int(data.get("CurrentHP", 0))
				var max_hp = int(data.get("MaxHP", 100))

				if GameManager.current_player and "data_model" in GameManager.current_player:
					var p_data = GameManager.current_player.data_model
					for member in p_data.party_members:
						if int(member.get("DBID", 0)) == target_id:
							member["HP"] = current_hp
							member["MaxHP"] = max_hp
							party_stat_update_received.emit(
								target_id, current_hp, max_hp, 
								int(member.get("MP", 0)), int(member.get("MaxMP", 50)), 
								int(member.get("SP", 0)), int(member.get("MaxSP", 100)), 
								int(member.get("Level", -1))
							)
							break

		"ScreenAoeResult":
			var aoe_results: Array = data.get("Results", [])
			var aoe_skill_id: int = int(data.get("SkillID", 0))
			var aoe_skill_level: int = int(data.get("SkillLevel", 1))
			var aoe_attacker_id: int = int(data.get("AttackerID", 0))
			for result in aoe_results:
				if not result is Dictionary:
					continue
				var entity_id: int = int(result.get("EntityID", 0))
				if entity_id <= 0:
					continue

				entity_damage_received.emit({
					"TargetID": entity_id,
					"TargetType": 1,
					"Damage": int(result.get("Damage", 0)),
					"CurrentHP": int(result.get("CurrentHP", 0)),
					"MaxHP": int(result.get("MaxHP", 0)),
					"IsCrit": false,
					"SkillID": aoe_skill_id,
					"SkillLevel": aoe_skill_level,
					"AttackerID": aoe_attacker_id
				})

				if bool(result.get("Died", false)):
					entity_death_received.emit({
						"EntityID": entity_id,
						"EntityType": 1,
						"KillerID": aoe_attacker_id
					})

		"EntityDeath":
			entity_death_received.emit(data)

			if int(data.get("EntityType", -1)) == 0:
				var target_id = int(data.get("EntityID", 0))
				if GameManager.current_player and "data_model" in GameManager.current_player:
					var p_data = GameManager.current_player.data_model
					for member in p_data.party_members:
						if int(member.get("DBID", 0)) == target_id:
							member["HP"] = 0
							party_stat_update_received.emit(
								target_id, 0, int(member.get("MaxHP", 100)), 
								int(member.get("MP", 0)), int(member.get("MaxMP", 50)), 
								int(member.get("SP", 0)), int(member.get("MaxSP", 100)), 
								int(member.get("Level", -1))
							)
							break

		"EntityHeal":
			entity_heal_received.emit(data)


		"PlayEffect":
			var target_entity_id = int(data.get("EntityID", 0))
			var effect_name = str(data.get("EffectName", ""))


			var is_mecha_levelup = (effect_name == "MS_Levelup")

			if effect_name == "Normal_Effect08" or effect_name == "LevelUp" or is_mecha_levelup:

				if GameManager.current_player and "data_model" in GameManager.current_player and GameManager.current_player.data_model.db_id == target_entity_id:
					if GameManager.current_player.has_method("show_level_up"):
						GameManager.current_player.show_level_up(is_mecha_levelup)


				elif NetworkEntityManager:
					if "active_entities" in NetworkEntityManager and NetworkEntityManager.active_entities.has(target_entity_id):
						var net_player = NetworkEntityManager.active_entities[target_entity_id]
						if is_instance_valid(net_player) and net_player.has_method("show_level_up"):
							net_player.show_level_up(is_mecha_levelup)

		"PlayItemEffect":
			var target_entity_id = int(data.get("EntityID", 0))
			var item_index = int(data.get("ItemIndex", 0))

			if GameManager.current_player and "data_model" in GameManager.current_player and GameManager.current_player.data_model.db_id == target_entity_id:
				if GameManager.current_player.has_method("_play_item_effect"):
					GameManager.current_player._play_item_effect(item_index)
			elif NetworkEntityManager:
				if "active_entities" in NetworkEntityManager and NetworkEntityManager.active_entities.has(target_entity_id):
					var net_player = NetworkEntityManager.active_entities[target_entity_id]
					if is_instance_valid(net_player) and net_player.has_method("_play_item_effect"):
						net_player._play_item_effect(item_index)

		"SyncStatus":
			var target_entity_id = int(data.get("EntityID", 0))
			var new_hp = int(data.get("HP", -1))
			var new_mp = int(data.get("MP", -1))
			var new_sp = int(data.get("SP", -1))

			if GameManager.current_player and "data_model" in GameManager.current_player and GameManager.current_player.data_model.db_id == target_entity_id:
				var p_data = GameManager.current_player.data_model
				if new_hp >= 0: p_data.hp = new_hp
				if new_mp >= 0: p_data.mp = new_mp
				if new_sp >= 0: p_data.sp = new_sp


				if data.has("MaxHP"): p_data.max_hp = int(data.get("MaxHP", p_data.max_hp))
				if data.has("MaxMP"): p_data.max_mp = int(data.get("MaxMP", p_data.max_mp))
				if data.has("MaxSP"): p_data.max_sp = int(data.get("MaxSP", p_data.max_sp))
				if data.has("Attack"): p_data.attack = int(data.get("Attack", p_data.attack))
				if data.has("Defense"): p_data.defense = int(data.get("Defense", p_data.defense))
				if data.has("MagicAttack"): p_data.magic_attack = int(data.get("MagicAttack", p_data.magic_attack))
				if data.has("MagicDefense"): p_data.magic_defense = int(data.get("MagicDefense", p_data.magic_defense))
				if data.has("Hit"): p_data.hit = int(data.get("Hit", p_data.hit))
				if data.has("Dodge"): p_data.dodge = int(data.get("Dodge", p_data.dodge))
				if data.has("Lucky"): p_data.lucky = int(data.get("Lucky", p_data.lucky))
				if data.has("AttackSpeed"): p_data.attack_speed = int(data.get("AttackSpeed", p_data.attack_speed))
				if data.has("AttackRange"): p_data.attack_range = int(data.get("AttackRange", p_data.attack_range))
				if data.has("MoveSpeed"): p_data.move_speed = int(data.get("MoveSpeed", p_data.move_speed))
				if data.has("CritRate"): p_data.crit_rate = int(data.get("CritRate", p_data.crit_rate))
				if data.has("CritDamage"): p_data.crit_damage = int(data.get("CritDamage", p_data.crit_damage))

				if GameManager.current_hud and GameManager.current_hud.has_method("update_status_panel"):
					GameManager.current_hud.update_status_panel()


				if GameManager.current_hud and GameManager.current_hud.has_method("get_node_or_null"):
					var char_win = GameManager.current_hud.get_node_or_null("WindowsContainer/CharacterWindow")
					if char_win and char_win.visible and char_win.has_method("update_ui"):
						char_win.update_ui()

				if GameManager.current_player.get("is_dead") and new_hp > 0:
					if GameManager.current_player.has_method("revive"):
						GameManager.current_player.revive()

			elif NetworkEntityManager:
				if "active_entities" in NetworkEntityManager and NetworkEntityManager.active_entities.has(target_entity_id):
					var net_player = NetworkEntityManager.active_entities[target_entity_id]
					if is_instance_valid(net_player):
						if "data_model" in net_player:
							var n_data = net_player.data_model
							if new_hp >= 0: n_data.hp = new_hp
							if new_mp >= 0: n_data.mp = new_mp
							if new_sp >= 0 and "sp" in n_data: n_data.sp = new_sp

							if data.has("MaxHP"): n_data.max_hp = int(data.get("MaxHP", n_data.max_hp))
							if data.has("MaxMP"): n_data.max_mp = int(data.get("MaxMP", n_data.max_mp))
							if data.has("MaxSP"): n_data.max_sp = int(data.get("MaxSP", n_data.max_sp))
							if data.has("Attack"): n_data.attack = int(data.get("Attack", n_data.attack))
							if data.has("Defense"): n_data.defense = int(data.get("Defense", n_data.defense))
							if data.has("MagicAttack"): n_data.magic_attack = int(data.get("MagicAttack", n_data.magic_attack))
							if data.has("MagicDefense"): n_data.magic_defense = int(data.get("MagicDefense", n_data.magic_defense))
							if data.has("Hit"): n_data.hit = int(data.get("Hit", n_data.hit))
							if data.has("Dodge"): n_data.dodge = int(data.get("Dodge", n_data.dodge))
							if data.has("Lucky"): n_data.lucky = int(data.get("Lucky", n_data.lucky))
							if data.has("AttackSpeed"): n_data.attack_speed = int(data.get("AttackSpeed", n_data.attack_speed))
							if data.has("AttackRange"): n_data.attack_range = int(data.get("AttackRange", n_data.attack_range))
							if data.has("MoveSpeed"): n_data.move_speed = int(data.get("MoveSpeed", n_data.move_speed))
							if data.has("CritRate"): n_data.crit_rate = int(data.get("CritRate", n_data.crit_rate))
							if data.has("CritDamage"): n_data.crit_damage = int(data.get("CritDamage", n_data.crit_damage))


							if data.has("Level"):
								var new_level = int(data.get("Level", n_data.level))
								if n_data.level > 0 and new_level > n_data.level:
									if net_player.has_method("show_level_up"):
										net_player.show_level_up()
								n_data.level = new_level

							if new_hp <= 0 and not net_player.get("is_dead"):
								if net_player.has_method("_die"): net_player._die()
							elif new_hp > 0 and net_player.get("is_dead"):
								if net_player.has_method("revive"): net_player.revive()

						elif net_player.has_method("update_hp"):
							if new_hp >= 0:
								net_player.update_hp(new_hp, 0.0)


							if data.has("Level"):
								var new_level = int(data.get("Level", 1))
								var current_level = net_player.get("level") if "level" in net_player else 1
								if current_level > 0 and new_level > current_level:
									if net_player.has_method("show_level_up"):
										net_player.show_level_up()
								if "level" in net_player:
									net_player.level = new_level

			status_synced.emit(target_entity_id, new_hp, new_mp, new_sp)


			if GameManager.current_player and "data_model" in GameManager.current_player:
				var p_data = GameManager.current_player.data_model
				for member in p_data.party_members:
					if int(member.get("DBID", 0)) == target_entity_id:
						if new_hp >= 0: member["HP"] = new_hp
						if new_mp >= 0: member["MP"] = new_mp
						if new_sp >= 0: member["SP"] = new_sp
						if data.has("MaxHP"): member["MaxHP"] = int(data.get("MaxHP", member.get("MaxHP", 100)))
						if data.has("MaxMP"): member["MaxMP"] = int(data.get("MaxMP", member.get("MaxMP", 50)))
						if data.has("MaxSP"): member["MaxSP"] = int(data.get("MaxSP", member.get("MaxSP", 100)))
						if data.has("Level"): member["Level"] = int(data.get("Level", member.get("Level", 1)))

						party_stat_update_received.emit(
							target_entity_id, 
							int(member.get("HP", 0)), int(member.get("MaxHP", 100)), 
							int(member.get("MP", 0)), int(member.get("MaxMP", 50)), 
							int(member.get("SP", 0)), int(member.get("MaxSP", 100)), 
							int(member.get("Level", -1))
						)
						break
		"ClubSync":

			if GameManager.current_player and "data_model" in GameManager.current_player:
				var p_data = GameManager.current_player.data_model
				p_data.club_id = int(data.get("ClubID", 0))
				if data.has("MyPosition"): p_data.club_position = int(data.get("MyPosition", 0))
				if data.has("MyDonation"): p_data.club_donation = int(data.get("MyDonation", 0))
			club_sync_received.emit(data)

		"ClubInvite":
			club_invite_received.emit(int(data.get("InviterDBID", 0)), str(data.get("InviterName", "")), str(data.get("ClubName", "")))

		"OpenCreateClubWindow":
			open_create_club_window_received.emit()

		"PartySync":
			var p_id = str(data.get("PartyID", ""))
			var leader_id = int(data.get("LeaderDBID", 0))
			var members = data.get("Members", [])
			for member in members:
				if typeof(member) == TYPE_DICTIONARY and member.has("Level"):
					member["Level"] = int(member.get("Level", 1))

			if GameManager.current_player and "data_model" in GameManager.current_player:
				GameManager.current_player.data_model.party_id = p_id
				GameManager.current_player.data_model.party_leader_dbid = leader_id
				GameManager.current_player.data_model.party_members = members

			party_sync_received.emit(p_id, leader_id, members)

		"PartyInvite":
			party_invite_received.emit(int(data.get("InviterDBID", 0)), str(data.get("InviterName", "")))

		"PartyStatUpdate":
			var dbid = int(data.get("DBID", 0))
			var hp = int(data.get("HP", 0))
			var max_hp = int(data.get("MaxHP", 100))
			var mp = int(data.get("MP", 0))
			var max_mp = int(data.get("MaxMP", 50))
			var sp = int(data.get("SP", 0))
			var max_sp = int(data.get("MaxSP", 100))
			var level = int(data.get("Level", -1)) if data.has("Level") else -1

			if GameManager.current_player and "data_model" in GameManager.current_player:
				var p_data = GameManager.current_player.data_model


				if p_data.db_id == dbid:
					p_data.hp = hp
					p_data.max_hp = max_hp
					p_data.mp = mp
					p_data.max_mp = max_mp
					p_data.sp = sp
					p_data.max_sp = max_sp
					if level >= 0:
						p_data.level = level



				for member in p_data.party_members:
					if int(member.get("DBID", 0)) == dbid:
						member["HP"] = hp
						member["MaxHP"] = max_hp
						member["MP"] = mp
						member["MaxMP"] = max_mp
						member["SP"] = sp
						member["MaxSP"] = max_sp
						if level >= 0:
							member["Level"] = level
						break

			party_stat_update_received.emit(dbid, hp, max_hp, mp, max_mp, sp, max_sp, level)

		"SkillCast":
			skill_cast_received.emit(
				int(data.get("CasterID", 0)), 
				int(data.get("CasterType", 0)), 
				int(data.get("SkillID", 0)), 
				int(data.get("SkillLevel", 1)), 
				int(data.get("TargetID", 0)), 
				int(data.get("TargetX", 0)), 
				int(data.get("TargetY", 0)), 
				data.get("AoeTargets", [])
			)

		"RevivePrompt":
			var caster_name = str(data.get("CasterName", "未知"))
			var skill_id = int(data.get("SkillID", 0))
			var skill_level = int(data.get("SkillLevel", 1))
			var hp_pct = int(data.get("HPPct", 10))
			var mp_pct = int(data.get("MPPct", 0))

			if GameManager and GameManager.current_hud and GameManager.current_hud.has_method("show_revive_prompt"):
				GameManager.current_hud.show_revive_prompt(caster_name, skill_id, skill_level, hp_pct, mp_pct)

		"QuestSync":
			var quests = data.get("Quests", [])
			if GameManager.current_player and "data_model" in GameManager.current_player:
				GameManager.current_player.data_model.update_quests(quests)
			quest_synced.emit(quests)

			if GameManager.current_hud and GameManager.current_hud.has_method("get_node_or_null"):
				var q_win = GameManager.current_hud.get_node_or_null("WindowsContainer/QuestWindow")
				if q_win and q_win.visible and q_win.has_method("update_ui"):
					q_win.update_ui()

		"QuestUpdate":
			var q_data = data
			if GameManager.current_player and "data_model" in GameManager.current_player:
				var q_id = int(q_data.get("QuestID", 0))
				GameManager.current_player.data_model.player_quests[q_id] = q_data
			quest_updated.emit(q_data)
			if GameManager.current_hud and GameManager.current_hud.has_method("get_node_or_null"):
				var q_win = GameManager.current_hud.get_node_or_null("WindowsContainer/QuestWindow")
				if q_win and q_win.visible and q_win.has_method("update_ui"):
					q_win.update_ui()

		"OpenMegaphoneWindow":
			open_megaphone_window_received.emit(int(data.get("ItemUID", 0)))

		"OpenRankWindow":
			open_rank_window_received.emit(int(data.get("RankType", 1)))

		"RankDataResponse":
			rank_data_received.emit(data)

		"AnnouncementListResponse":
			announcement_list_received.emit(data)

		"UpdateRankMessageResponse":
			update_rank_message_response_received.emit(data.get("Success", false), data.get("Message", ""))

		"MallListResponse":
			var points = int(data.get("Points", 0))
			var items: Array = data.get("Items", [])
			var gifts: Array = data.get("Gifts", [])
			mall_list_received.emit(points, items, gifts)

		"MallPointsSync":
			var points = int(data.get("Points", 0))
			mall_points_sync_received.emit(points)

		"PayPalDonationCreated":
			var success = bool(data.get("Success", false))
			var message = str(data.get("Message", ""))
			var approval_url = str(data.get("ApprovalUrl", ""))
			paypal_donation_created_received.emit(success, message, approval_url)

		"LegacyPointWithdrawalResponse":
			var success = bool(data.get("Success", false))
			var operation = str(data.get("Operation", ""))
			var message = str(data.get("Message", ""))
			var legacy_points = int(data.get("LegacyPoints", 0))
			legacy_point_withdrawal_response_received.emit(success, operation, message, legacy_points)

		"CraftItemResponse":
			var success = data.get("Success", false)
			var msg = data.get("Message", "")
			var result_items = data.get("ResultItems", [])
			craft_item_response_received.emit(success, msg, result_items)


			if msg != "":
				system_message_flash_received.emit(msg)

		"DismantleItemResponse":
			var success = data.get("Success", false)
			var msg = data.get("Message", "")
			var result_items = data.get("ResultItems", [])
			dismantle_item_response_received.emit(success, msg, result_items)

			if msg != "":
				system_message_flash_received.emit(msg)

		"ToggleAutoSaintResponse":
			toggle_auto_saint_response_received.emit(bool(data.get("IsOn", false)))

		"Ping":
			send_secure_packet({"Type": "Pong"})

		"PacketError":
			var request_type: String = str(data.get("RequestType", ""))
			var error_code: String = str(data.get("Code", "UNKNOWN"))
			var error_message: String = str(data.get("Message", "伺服器無法處理此操作"))
			push_warning("[PACKET_ERROR] RequestType=%s Code=%s Message=%s" % [request_type, error_code, error_message])
			system_message_flash_received.emit(error_message)

		_:
			var unknown_type: String = str(packet_type)
			if not _unknown_server_packet_types.has(unknown_type):
				_unknown_server_packet_types[unknown_type] = true
				push_warning("[UNHANDLED_SERVER_PACKET] Type=%s" % unknown_type)

func send_trade_request(target_dbid: int) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({"Type": "TradeRequest", "TargetEntityID": target_dbid})

func send_trade_reply(requester_dbid: int, accept: bool) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({"Type": "TradeReply", "RequesterDBID": requester_dbid, "Accept": accept})

func send_trade_action(action_type: String, extra_data: Dictionary = {}) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		var packet = {"Type": action_type}
		packet.merge(extra_data)
		send_secure_packet(packet)

func send_shop_action(action_type: String, extra_data: Dictionary = {}) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		var packet = {"Type": "ShopAction", "Action": action_type}
		packet.merge(extra_data)
		send_secure_packet(packet)

func send_attack_request(target_id: int, target_type: int = 1) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({
			"Type": "AttackEntity", 
			"TargetEntityID": target_id, 
			"TargetType": target_type
		})

func send_allocate_stat_request(stat_type: String) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({"Type": "AllocateStatRequest", "StatType": stat_type})

func send_party_action(action: String, target_dbid: int = -1) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({
			"Type": "PartyAction", 
			"Action": action, 
			"TargetDBID": target_dbid
		})

func send_drive_mecha_request(horse_uid: int) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({
			"Type": "DriveMechaRequest", 
			"HorseUID": horse_uid
		})

func send_set_title_request(title_id: int) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({
			"Type": "SetTitleRequest", 
			"TitleID": title_id
		})

func send_update_shortcut_request(slot_index: int, shortcut_type: int, target_id: int) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({
			"Type": "UpdateShortcut", 
			"SlotIndex": slot_index, 
			"ShortcutType": shortcut_type, 
			"TargetID": target_id
		})




func send_set_global_pet_mode(mode: int) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({
			"Type": "PetAction", 
			"Action": "SetCombatMode", 
			"CombatMode": mode
		})

func send_pet_action(action: String, pet_uid: int, extra_data: Dictionary = {}) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		var packet = {
			"Type": "PetAction", 
			"Action": action, 
			"PetUID": pet_uid
		}
		packet.merge(extra_data)
		send_secure_packet(packet)

func send_pet_attack_command(target_id: int) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({
			"Type": "PetAction", 
			"Action": "CommandAttack", 
			"TargetEntityID": target_id
		})




func send_direct_transfer(target_dbid: int, amount: int) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({
			"Type": "DirectTransfer", 
			"TargetDBID": target_dbid, 
			"Amount": amount
		})




func send_revive_accept(skill_id: int, skill_level: int) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({
			"Type": "ReviveAccept", 
			"SkillID": skill_id, 
			"SkillLevel": skill_level
		})




func send_repair_item_request(item_type: String, uid: int) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({
			"Type": "RepairItemRequest", 
			"ItemType": item_type, 
			"UID": uid
		})

func send_megaphone_message(item_uid: int, message: String) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({
			"Type": "MegaphoneMessage", 
			"ItemUID": item_uid, 
			"Message": message
		})




func send_mall_action(action: String, args: Dictionary = {}) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		var packet = {"Type": "MallAction", "Action": action}
		packet.merge(args)
		send_secure_packet(packet)

func send_claim_donation_gift(reward_id: int) -> void :
	if reward_id <= 0:
		return
	send_mall_action("ClaimDonationGift", {"RewardID": reward_id})

func connect_to_server_via_ddns() -> bool:
	var host_name = "how-sgoserver.ddns.net"


	var resolver_id = IP.resolve_hostname_queue_item(host_name, IP.TYPE_IPV4)


	while IP.get_resolve_item_status(resolver_id) == IP.RESOLVER_STATUS_WAITING:
		await get_tree().process_frame


	if IP.get_resolve_item_status(resolver_id) == IP.RESOLVER_STATUS_DONE:
		var resolved_ip = IP.get_resolve_item_address(resolver_id)
		if resolved_ip.is_valid_ip_address():
			SERVER_URL = "ws://" + resolved_ip + ":5678"
			return true

	print("❌ [網路層] 嘗試連線至伺服器失敗，無法解析 IP。")
	if GameManager.current_hud and GameManager.current_hud.has_method("show_system_message_flash"):
		GameManager.current_hud.show_system_message_flash("嘗試連線至伺服器失敗.")
	return false




func send_craft_item_request(recipe_id: int, amount: int = 1) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({
			"Type": "CraftItemRequest", 
			"RecipeID": recipe_id, 
			"BatchCount": amount
		})

func send_dismantle_item_request(target_uids: Array, item_type: String) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({
			"Type": "DismantleItemRequest", 
			"ItemUIDs": target_uids, 
			"ItemType": item_type
		})




func send_toggle_auto_saint(is_on: bool, mp_threshold: int = 50, sp_threshold: int = 50) -> void :
	if socket and socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		send_secure_packet({
			"Type": "ToggleAutoSaint", 
			"IsOn": is_on, 
			"MpThreshold": mp_threshold, 
			"SpThreshold": sp_threshold
		})
