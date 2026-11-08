extends SceneTree

# Real Godot -> WebSocket -> .NET server -> Godot signal E2E.
# Unlike network_response_contract_regression.gd, this does not inject packets
# into the dispatcher: it uses the production NetworkManager socket and send
# helpers against the isolated local server started by the protocol runner.
#
# Run with an already-started local server:
# SGO_TEST_SERVER_URL=ws://127.0.0.1:5678 godot --headless --path client \
#   --script res://tests/real_server_network_e2e.gd

var _failures: int = 0

var _register_done := false
var _register_ok := false
var _register_message := ""

var _login_done := false
var _login_ok := false
var _login_message := ""

var _create_done := false
var _create_ok := false
var _create_message := ""

var _enter_done := false
var _enter_data: Dictionary = {}

var _world_ready_done := false
var _world_ready_ok := false
var _world_ready_map_id := 0
var _world_ready_message := ""

var _chat_done := false
var _chat_message := ""

var _inventory_generation := 0
var _last_inventory: Array = []
var _ground_spawn_done := false
var _ground_spawn_data: Dictionary = {}
var _pickup_done := false
var _pickup_data: Dictionary = {}
var _change_map_done := false
var _change_map_id := 0

var _bootstrap := {
	"InventorySync": false,
	"EquipSync": false,
	"PetSync": false,
	"HorseSync": false,
	"SkillSync": false,
	"ClubSync": false,
	"SyncBuffs": false,
	"QuestSync": false,
	"FestivalThemeSync": false,
}


func _initialize() -> void:
	call_deferred("_run")


func _check(ok: bool, description: String) -> void:
	if ok:
		print("[REAL_SERVER_NETWORK_PASS] ", description)
	else:
		_failures += 1
		push_error("[REAL_SERVER_NETWORK_FAIL] " + description)


func _run() -> void:
	await process_frame

	var url := OS.get_environment("SGO_TEST_SERVER_URL")
	if url.is_empty():
		url = "ws://127.0.0.1:5678"

	NetworkManager.USE_DDNS = false
	NetworkManager.SERVER_URL = url

	NetworkManager.register_account_response_received.connect(_on_register)
	NetworkManager.login_finished.connect(_on_login)
	NetworkManager.create_character_response_received.connect(_on_create)
	NetworkManager.enter_world_success.connect(_on_enter_world)
	NetworkManager.world_ready_response_received.connect(_on_world_ready)
	NetworkManager.inventory_synced.connect(_on_inventory)
	NetworkManager.equip_synced.connect(_on_equip)
	NetworkManager.pet_synced.connect(_on_pet)
	NetworkManager.horse_synced.connect(_on_horse)
	NetworkManager.skill_synced.connect(_on_skill)
	NetworkManager.club_sync_received.connect(_on_club)
	NetworkManager.buffs_synced.connect(_on_buffs)
	NetworkManager.quest_synced.connect(_on_quests)
	NetworkManager.festival_theme_received.connect(_on_festival)
	NetworkManager.chat_broadcast_received.connect(_on_chat)
	NetworkManager.entity_spawned.connect(_on_entity_spawned)
	NetworkManager.item_pickup_feedback_received.connect(_on_item_pickup_feedback)
	NetworkManager.change_map_received.connect(_on_change_map)

	var token := int(Time.get_ticks_usec() % 1000000000)
	var username := "gde2e%d%d" % [OS.get_process_id(), token]
	var password := "test-password"
	var character_name := "G%09d" % token

	_register_done = false
	NetworkManager.send_register_account_request(
		username,
		password,
		""
	)
	if not await _wait_for(func(): return _register_done, 12.0):
		_check(false, "RegisterAccountResponse arrives through the real Godot WebSocket")
		_finish()
		return
	_check(
		_register_ok,
		"RegisterAccountRequest succeeds: " + _register_message
	)
	if not _register_ok:
		_finish()
		return

	_login_done = false
	NetworkManager.send_login_request(username, password)
	if not await _wait_for(func(): return _login_done, 12.0):
		_check(false, "LoginResponse arrives through the real Godot WebSocket")
		_finish()
		return
	_check(_login_ok, "LoginRequest succeeds: " + _login_message)
	if not _login_ok:
		_finish()
		return

	_check(
		GlobalTempData.character_list.size() == 3,
		"LoginResponse keeps the authoritative three character slots"
	)
	if GlobalTempData.character_list.is_empty():
		_finish()
		return

	var account_id := int(
		GlobalTempData.character_list[0].get("ACCID", 0)
	)
	_check(account_id > 0, "LoginResponse exposes the authenticated ACCID")
	if account_id <= 0:
		_finish()
		return

	_create_done = false
	NetworkManager.send_create_character_request(
		account_id,
		character_name,
		0,
		1,
		1,
		"1234",
		5,
		5,
		5,
		5
	)
	if not await _wait_for(func(): return _create_done, 12.0):
		_check(false, "CreateCharacterResponse arrives through the real Godot WebSocket")
		_finish()
		return
	_check(_create_ok, "CreateCharacterRequest succeeds: " + _create_message)
	if not _create_ok:
		_finish()
		return

	# Refresh the three-slot list exactly as the real character-selection flow
	# does after creation.
	_login_done = false
	NetworkManager.send_login_request(username, password)
	if not await _wait_for(func(): return _login_done, 12.0):
		_check(false, "relogin response arrives after character creation")
		_finish()
		return
	_check(_login_ok, "relogin succeeds after character creation")
	if not _login_ok:
		_finish()
		return

	var character_id := 0
	for slot in GlobalTempData.character_list:
		if slot is Dictionary and str(slot.get("NAME", "")) == character_name:
			character_id = int(slot.get("DBID", 0))
			break
	_check(character_id > 0, "created character is returned by the server")
	if character_id <= 0:
		_finish()
		return

	for key in _bootstrap.keys():
		_bootstrap[key] = false
	_enter_done = false
	_enter_data.clear()
	NetworkManager.send_select_character_request(character_id)

	if not await _wait_for(func(): return _enter_done, 12.0):
		_check(false, "EnterWorldSuccess arrives through production NetworkManager")
		_finish()
		return

	_check(
		int(_enter_data.get("DBID", 0)) == character_id,
		"EnterWorldSuccess identifies the selected character"
	)
	var map_id := int(_enter_data.get("MapID", 0))
	_check(map_id > 0, "EnterWorldSuccess contains a valid map")

	if not await _wait_for(func(): return _bootstrap_complete(), 12.0):
		_check(
			false,
			"all nine authoritative bootstrap sync packets reach Godot signals: " +
			str(_bootstrap)
		)
		_finish()
		return
	_check(
		_bootstrap_complete(),
		"Inventory/Equip/Pet/Horse/Skill/Club/Buff/Quest/Festival bootstrap completes"
	)

	_world_ready_done = false
	NetworkManager.send_world_ready(map_id)
	if not await _wait_for(func(): return _world_ready_done, 12.0):
		_check(false, "WorldReadyResponse arrives through production NetworkManager")
		_finish()
		return
	_check(
		_world_ready_ok and _world_ready_map_id == map_id,
		"WorldReady ACK confirms the selected map: " + _world_ready_message
	)
	if not _world_ready_ok:
		_finish()
		return

	# Exercise one normal post-world client->server->client path using the same
	# public secure send function used by gameplay UI code.
	_chat_done = false
	var expected_chat := "godot-e2e-%d" % token
	NetworkManager.send_secure_packet({
		"Type": "ChatMessage",
		"Channel": 0,
		"Message": expected_chat,
		"BubbleType": 0,
	})
	if not await _wait_for(func(): return _chat_done, 12.0):
		_check(false, "nearby ChatBroadcast returns to the real Godot client")
		_finish()
		return
	_check(
		_chat_message == expected_chat,
		"post-world nearby chat round-trips without payload drift"
	)

	# Ground-item lifecycle through the production Godot NetworkManager:
	# seed -> drop -> leave map -> return -> WorldReady snapshot -> pickup.
	var inventory_before_seed := _inventory_generation
	NetworkManager.send_secure_packet({
		"Type": "ChatMessage",
		"Channel": 0,
		"Message": "/gm item 1001 1",
		"BubbleType": 0,
	})
	if not await _wait_for(
		func(): return _inventory_generation > inventory_before_seed,
		12.0
	):
		_check(false, "GM seed InventorySync reaches Godot before ground lifecycle")
		_finish()
		return

	var ground_item_uid := 0
	for item in _last_inventory:
		if (
			item is Dictionary
			and int(item.get("ItemIndex", 0)) == 1001
			and int(item.get("InBank", 0)) == 0
			and int(item.get("InShop", 0)) == 0
		):
			ground_item_uid = max(
				ground_item_uid,
				int(item.get("ItemUID", 0))
			)
	_check(ground_item_uid > 0, "Godot sees seeded item UID for ground lifecycle")
	if ground_item_uid <= 0:
		_finish()
		return

	var first_x := int(_enter_data.get("LocX", 0))
	var first_y := int(_enter_data.get("LocY", 0))
	_ground_spawn_done = false
	_ground_spawn_data.clear()
	NetworkManager.send_secure_packet({
		"Type": "DropItemRequest",
		"TabType": 0,
		"ItemUID": ground_item_uid,
		"Count": 1,
		"DropX": first_x,
		"DropY": first_y,
	})
	if not await _wait_for(func(): return _ground_spawn_done, 12.0):
		_check(false, "DropItemRequest produces ground SpawnEntity in Godot")
		_finish()
		return

	var ground_entity_id := int(_ground_spawn_data.get("EntityID", 0))
	_check(
		ground_entity_id > 0,
		"Godot receives authoritative ground EntityID before map change"
	)
	if ground_entity_id <= 0:
		_finish()
		return

	_change_map_done = false
	NetworkManager.send_secure_packet({
		"Type": "ChatMessage",
		"Channel": 0,
		"Message": "/gm map 16 %d %d" % [first_x, first_y],
		"BubbleType": 0,
	})
	if not await _wait_for(
		func(): return _change_map_done and _change_map_id == 16,
		12.0
	):
		_check(false, "Godot receives ChangeMap to lifecycle destination")
		_finish()
		return

	_world_ready_done = false
	NetworkManager.send_world_ready(16)
	if not await _wait_for(func(): return _world_ready_done, 12.0):
		_check(false, "destination WorldReady ACK reaches Godot")
		_finish()
		return
	_check(
		_world_ready_ok and _world_ready_map_id == 16,
		"destination WorldReady succeeds in Godot ground lifecycle"
	)

	_change_map_done = false
	NetworkManager.send_secure_packet({
		"Type": "ChatMessage",
		"Channel": 0,
		"Message": "/gm map %d %d %d" % [map_id, first_x, first_y],
		"BubbleType": 0,
	})
	if not await _wait_for(
		func(): return _change_map_done and _change_map_id == map_id,
		12.0
	):
		_check(false, "Godot receives ChangeMap back to original map")
		_finish()
		return

	_ground_spawn_done = false
	_ground_spawn_data.clear()
	_world_ready_done = false
	NetworkManager.send_world_ready(map_id)
	if not await _wait_for(func(): return _world_ready_done, 12.0):
		_check(false, "return-map WorldReady ACK reaches Godot")
		_finish()
		return
	if not await _wait_for(func(): return _ground_spawn_done, 12.0):
		_check(false, "return-map WorldReady rebuilds ground SpawnEntity in Godot")
		_finish()
		return

	_check(
		int(_ground_spawn_data.get("EntityID", 0)) == ground_entity_id
		and bool(_ground_spawn_data.get("IsSnapshot", false)),
		"Godot receives same persisted ground EntityID as a return-map snapshot"
	)

	_pickup_done = false
	_pickup_data.clear()
	NetworkManager.send_pickup_item_request(ground_entity_id)
	if not await _wait_for(func(): return _pickup_done, 12.0):
		_check(false, "Godot receives ItemPickupFeedback after map-return pickup")
		_finish()
		return
	_check(
		int(_pickup_data.get("ItemIndex", 0)) == 1001
		and int(_pickup_data.get("Count", 0)) == 1,
		"Godot can pick persisted ground item after leaving and returning"
	)

	_finish()


func _bootstrap_complete() -> bool:
	for value in _bootstrap.values():
		if not bool(value):
			return false
	return true


func _wait_for(predicate: Callable, timeout_seconds: float) -> bool:
	var deadline := Time.get_ticks_msec() + int(timeout_seconds * 1000.0)
	while Time.get_ticks_msec() < deadline:
		if bool(predicate.call()):
			return true
		await process_frame
	return bool(predicate.call())


func _finish() -> void:
	if NetworkManager.socket:
		NetworkManager.socket.close()
	print(
		"[REAL_SERVER_NETWORK_E2E] ",
		"ALL PASS" if _failures == 0 else "%d FAILURE(S)" % _failures
	)
	quit(0 if _failures == 0 else 1)


func _on_register(success: bool, message: String) -> void:
	_register_ok = success
	_register_message = message
	_register_done = true


func _on_login(success: bool, message: String) -> void:
	_login_ok = success
	_login_message = message
	_login_done = true


func _on_create(success: bool, message: String) -> void:
	_create_ok = success
	_create_message = message
	_create_done = true


func _on_enter_world(data: Dictionary) -> void:
	_enter_data = data.duplicate(true)
	_enter_done = true


func _on_world_ready(success: bool, map_id: int, message: String) -> void:
	_world_ready_ok = success
	_world_ready_map_id = map_id
	_world_ready_message = message
	_world_ready_done = true


func _on_inventory(items: Array, _max_size: int) -> void:
	_last_inventory = items.duplicate(true)
	_inventory_generation += 1
	_bootstrap["InventorySync"] = true


func _on_equip(_items: Array, _max_size: int) -> void:
	_bootstrap["EquipSync"] = true


func _on_pet(_items: Array, _max_size: int) -> void:
	_bootstrap["PetSync"] = true


func _on_horse(_horses: Array, _equips: Array, _max_size: int) -> void:
	_bootstrap["HorseSync"] = true


func _on_skill(_skills: Array) -> void:
	_bootstrap["SkillSync"] = true


func _on_club(_data: Dictionary) -> void:
	_bootstrap["ClubSync"] = true


func _on_buffs(_buffs: Array) -> void:
	_bootstrap["SyncBuffs"] = true


func _on_quests(_quests: Array) -> void:
	_bootstrap["QuestSync"] = true


func _on_festival(_theme: String) -> void:
	_bootstrap["FestivalThemeSync"] = true


func _on_chat(
		_entity_id: int,
		_sender_name: String,
		_race_id: int,
		_channel: int,
		message: String,
		_bubble_type: int) -> void:
	_chat_message = message
	_chat_done = true


func _on_entity_spawned(data: Dictionary) -> void:
	if (
		int(data.get("EntityType", -1)) == 3
		and data.get("ExtraData", {}) is Dictionary
		and int(data.get("ExtraData", {}).get("ItemIndex", 0)) == 1001
	):
		_ground_spawn_data = data.duplicate(true)
		_ground_spawn_done = true


func _on_item_pickup_feedback(data: Dictionary) -> void:
	if int(data.get("ItemIndex", 0)) == 1001:
		_pickup_data = data.duplicate(true)
		_pickup_done = true


func _on_change_map(map_id: int, _target_x: float, _target_y: float) -> void:
	_change_map_id = map_id
	_change_map_done = true
