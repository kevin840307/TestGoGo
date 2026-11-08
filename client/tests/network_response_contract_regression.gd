extends SceneTree

# Server-free regression for dynamic Server -> Client packet consumption.
# It feeds packets through the same AES-CBC decode/dispatch path used by the
# live WebSocket client, so fallback fields and signal payload types are tested
# without requiring a running server.
#
# Run:
# godot --headless --path client --script res://tests/network_response_contract_regression.gd

const AES_KEY_TEXT := "Paradise25DGameSecretKey2026Base"
const AES_IV_TEXT := "InitVectorVector"

var _failures: int = 0
var _inventory_seen := false
var _equip_seen := false
var _pet_seen := false
var _horse_seen := false
var _packet_error_seen := false
var _trade_seen := false
var _shop_sync_seen := false
var _shop_view_seen := false
var _shop_board_seen := false


func _check(ok: bool, description: String) -> void:
	if ok:
		print("[NETWORK_RESPONSE_TEST_PASS] ", description)
	else:
		_failures += 1
		push_error("[NETWORK_RESPONSE_TEST_FAIL] " + description)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	await process_frame

	NetworkManager.inventory_synced.connect(_on_inventory_synced)
	NetworkManager.equip_synced.connect(_on_equip_synced)
	NetworkManager.pet_synced.connect(_on_pet_synced)
	NetworkManager.horse_synced.connect(_on_horse_synced)
	NetworkManager.system_message_flash_received.connect(_on_system_message)
	NetworkManager.trade_sync_received.connect(_on_trade_sync)
	NetworkManager.shop_sync_received.connect(_on_shop_sync)
	NetworkManager.shop_view_response_received.connect(_on_shop_view)
	NetworkManager.sync_shop_board_received.connect(_on_shop_board)

	# MaxSize is intentionally omitted: the recovered client must retain the
	# documented fallback while preserving dynamic item dictionaries.
	_feed({
		"Type": "InventorySync",
		"Items": [{
			"ItemUID": 101,
			"ItemIndex": 4051,
			"Count": 20,
			"InBank": 0,
			"InShop": 0,
		}],
	})
	_check(_inventory_seen, "InventorySync is consumed through encrypted dispatcher")

	_feed({
		"Type": "EquipSync",
		"Equips": [{
			"ItemID": 201,
			"ItemIdx": 31548,
			"Damage": 0,
			"Color": 0,
			"Part": 255,
			"InBank": 0,
			"InShop": 0,
		}],
		"MaxSize": 240,
	})
	_check(_equip_seen, "EquipSync preserves dynamic equipment fields")

	_feed({
		"Type": "PetSync",
		"Pets": [{
			"UID": 301,
			"PETID": 45001,
			"Name": "ContractPet",
			"HP": 100,
			"MaxHP": 100,
			"State": 0,
		}],
		"MaxSize": 50,
		"MaxSummon": 3,
	})
	_check(_pet_seen, "PetSync preserves dynamic pet fields")

	_feed({
		"Type": "HorseSync",
		"Horses": [{
			"HorseUID": 401,
			"HorseIdx": 50001,
			"EnergyLeft": 99,
			"Level": 1,
			"Name": "ContractHorse",
			"HorseType": "MS",
		}],
		"HorseEquips": [{
			"EquipUID": 501,
			"EquipIdx": 50293,
			"HorseUID": -1,
			"Part": 0,
			"InBank": 0,
			"InShop": 0,
		}],
		"MaxHorseEquipSize": 100,
	})
	_check(_horse_seen, "HorseSync preserves horse/equipment arrays")

	var general_listing := {
		"TabType": 0,
		"UID": 601,
		"ItemUID": 601,
		"ItemIndex": 4103,
		"Count": 2,
		"Price": 25,
		"Name": "ContractItem",
		"InBank": 0,
		"InShop": 1,
	}
	var pet_listing := {
		"TabType": 2,
		"UID": 701,
		"ItemIndex": 45001,
		"Count": 1,
		"Price": 40,
		"Name": "ContractPet",
		"ExtraData": {
			"UID": 701,
			"PETID": 45001,
			"Name": "ContractPet",
			"State": 0,
		},
	}

	_feed({
		"Type": "TradeSync",
		"MyMoney": 35,
		"OtherMoney": 12,
		"MyItems": [general_listing, pet_listing],
		"OtherItems": [],
		"MyLocked": true,
		"OtherLocked": false,
	})
	_check(_trade_seen, "TradeSync dynamic views reach typed client signal")

	_feed({
		"Type": "ShopSync",
		"Items": [general_listing, pet_listing],
	})
	_check(_shop_sync_seen, "ShopSync dynamic listings reach client signal")

	_feed({
		"Type": "ShopViewResponse",
		"ShopName": "Contract Shop",
		"OwnerDBID": 88,
		"Items": [general_listing, pet_listing],
	})
	_check(_shop_view_seen, "ShopViewResponse preserves dynamic listings")

	_feed({
		"Type": "SyncShopBoard",
		"EntityID": 88,
		"IsOpen": true,
		"ShopName": "Contract Shop",
	})
	_check(_shop_board_seen, "SyncShopBoard preserves public shop state")

	_feed({
		"Type": "LoginResponse",
		"Success": true,
		"Message": "OK",
		"Characters": [
			{"ACCID": 1, "DBID": 0},
			{"ACCID": 1, "DBID": 0},
			{"ACCID": 1, "DBID": 0},
		],
	})
	_check(
		GlobalTempData.character_list.size() == 3,
		"LoginResponse keeps the three-slot character array"
	)

	_feed({
		"Type": "PacketError",
		"RequestType": "BankTransfer",
		"Code": "INVALID_PACKET",
		"Message": "contract-error",
	})
	_check(_packet_error_seen, "typed PacketError reaches the client message path")

	print("[NETWORK_RESPONSE_TEST] ", "ALL PASS" if _failures == 0 else "%d FAILURES" % _failures)
	quit(0 if _failures == 0 else 1)


func _feed(packet: Dictionary) -> void:
	NetworkManager._handle_encrypted_server_message(_encrypt(packet))


func _encrypt(packet: Dictionary) -> String:
	var raw := JSON.stringify(packet).to_utf8_buffer()
	var pad_amount := 16 - (raw.size() % 16)
	for _i in range(pad_amount):
		raw.append(pad_amount)

	var aes := AESContext.new()
	aes.start(
		AESContext.MODE_CBC_ENCRYPT,
		AES_KEY_TEXT.to_utf8_buffer(),
		AES_IV_TEXT.to_utf8_buffer()
	)
	var encrypted := aes.update(raw)
	aes.finish()
	return Marshalls.raw_to_base64(encrypted)


func _on_inventory_synced(items: Array, max_size: int) -> void:
	_inventory_seen = true
	_check(max_size == 200, "InventorySync missing MaxSize uses client fallback 200")
	_check(items.size() == 1, "InventorySync item array length is preserved")
	if items.size() == 1:
		_check(int(items[0].get("ItemUID", 0)) == 101, "InventorySync ItemUID is preserved")
		_check(int(items[0].get("Count", 0)) == 20, "InventorySync Count is preserved")


func _on_equip_synced(equips: Array, max_size: int) -> void:
	_equip_seen = true
	_check(max_size == 240, "EquipSync MaxSize is preserved")
	_check(equips.size() == 1 and int(equips[0].get("ItemIdx", 0)) == 31548,
		"EquipSync dynamic ItemIdx is preserved")


func _on_pet_synced(pets: Array, max_size: int) -> void:
	_pet_seen = true
	_check(max_size == 50, "PetSync MaxSize is preserved")
	_check(pets.size() == 1 and int(pets[0].get("PETID", 0)) == 45001,
		"PetSync dynamic PETID is preserved")


func _on_horse_synced(horses: Array, horse_equips: Array, max_size: int) -> void:
	_horse_seen = true
	_check(max_size == 100, "HorseSync MaxHorseEquipSize is preserved")
	_check(horses.size() == 1 and int(horses[0].get("HorseIdx", 0)) == 50001,
		"HorseSync dynamic HorseIdx is preserved")
	_check(horse_equips.size() == 1 and int(horse_equips[0].get("EquipIdx", 0)) == 50293,
		"HorseSync dynamic EquipIdx is preserved")


func _listing(items: Array, tab_type: int) -> Dictionary:
	for item in items:
		if item is Dictionary and int(item.get("TabType", -1)) == tab_type:
			return item
	return {}


func _on_trade_sync(
		my_money: int,
		other_money: int,
		my_items: Array,
		other_items: Array,
		my_locked: bool,
		other_locked: bool) -> void:
	_trade_seen = true
	_check(my_money == 35 and other_money == 12,
		"TradeSync numeric money values are preserved")
	_check(my_locked and not other_locked,
		"TradeSync lock booleans are preserved")
	_check(my_items.size() == 2 and other_items.is_empty(),
		"TradeSync MyItems/OtherItems perspective is preserved")
	var general := _listing(my_items, 0)
	var pet := _listing(my_items, 2)
	_check(int(general.get("UID", 0)) == 601 and int(general.get("Count", 0)) == 2,
		"TradeSync general listing identity/count are preserved")
	_check(int(pet.get("ItemIndex", 0)) == 45001,
		"TradeSync pet listing identity is preserved")
	_check(int(pet.get("ExtraData", {}).get("PETID", 0)) == 45001,
		"TradeSync nested pet ExtraData is preserved")


func _on_shop_sync(items: Array) -> void:
	_shop_sync_seen = true
	_check(items.size() == 2, "ShopSync keeps heterogeneous listing count")
	var general := _listing(items, 0)
	var pet := _listing(items, 2)
	_check(int(general.get("Price", 0)) == 25 and int(general.get("InShop", 0)) == 1,
		"ShopSync general listing price/source state are preserved")
	_check(int(pet.get("Price", 0)) == 40 and int(pet.get("ExtraData", {}).get("UID", 0)) == 701,
		"ShopSync pet price/nested data are preserved")


func _on_shop_view(shop_name: String, owner_dbid: int, items: Array) -> void:
	_shop_view_seen = true
	_check(shop_name == "Contract Shop" and owner_dbid == 88,
		"ShopViewResponse owner identity is preserved")
	_check(items.size() == 2 and int(_listing(items, 0).get("UID", 0)) == 601,
		"ShopViewResponse listing payload is preserved")


func _on_shop_board(entity_id: int, is_open: bool, shop_name: String) -> void:
	_shop_board_seen = true
	_check(entity_id == 88 and is_open and shop_name == "Contract Shop",
		"SyncShopBoard entity/open/name values are preserved")


func _on_system_message(message: String) -> void:
	if message == "contract-error":
		_packet_error_seen = true
