extends SceneTree

# No live server required. Covers concurrent server notifications where
# MoveEntity reaches the Client before its first SpawnEntity.
const PENDING = preload("res://scripts/network_entity_pending_moves.gd")
const MANAGER = preload("res://scripts/network_entity_manager.gd")
var failures: int = 0


func _check(ok: bool, message: String) -> void:
	if ok:
		print("[AOI_PRESPAWN_PASS] " + message)
	else:
		failures += 1
		push_error("[AOI_PRESPAWN_FAIL] " + message)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_bounded_pending_cache()
	_test_manager_packet_ordering()
	print("[AOI_PRESPAWN_RESULT] " + (
		"ALL PASS" if failures == 0 else "FAILURES=%d" % failures
	))
	quit(0 if failures == 0 else 1)


func _test_bounded_pending_cache() -> void:
	var pending = PENDING.new()
	pending.record_move(1001, {"X": 1, "Y": 2, "Dir": "1"}, 1000)
	pending.record_move(1001, {"X": 9, "Y": 10, "Dir": "3"}, 1100)
	_check(pending.pending_count() == 1,
		"multiple moves before first spawn coalesce into one entry")
	var move: Dictionary = pending.consume_spawn_move(1001, 1200)
	_check(float(move.get("X", -1)) == 9.0 and
		float(move.get("Y", -1)) == 10.0 and
		str(move.get("Dir", "")) == "3" and pending.pending_count() == 0,
		"first spawn receives latest coalesced coordinates and facing")

	pending.record_move(1002, {"X": 1, "Y": 2}, 1000)
	_check(pending.consume_spawn_move(1002, 5000).is_empty(),
		"old pending move expires rather than jumping entity on late spawn")

	pending.record_move(1003, {"X": 5, "Y": 6}, 5100)
	pending.mark_despawned(1003, 5101)
	pending.record_move(1003, {"X": 500, "Y": 600}, 5102)
	_check(pending.pending_count() == 0 and
		pending.consume_spawn_move(1003, 5103).is_empty(),
		"despawn clears queued move and ignores delayed post-despawn move")
	pending.record_move(1003, {"X": 7, "Y": 8}, 5104)
	_check(float(pending.consume_spawn_move(1003, 5105).get("X", -1)) == 7.0,
		"new spawn removes tombstone and allows the next incarnation to move")

	for i: int in range(512):
		pending.record_move(5000 + i, {"X": i, "Y": i}, 6000 + i)
	_check(pending.pending_count() <= PENDING.MAX_PENDING,
		"malicious unknown-entity move burst cannot allocate unbounded memory")
	pending.clear()
	_check(pending.pending_count() == 0,
		"map switch clears pre-spawn cache")


func _test_manager_packet_ordering() -> void:
	var manager: Node = MANAGER.new()
	var monster_id: int = 9300801
	manager.call("_on_entity_moved", {
		"Type": "MoveEntity",
		"EntityID": monster_id,
		"X": 320.0, "Y": 480.0, "Dir": "2"
	})
	_check(manager.get("_pending_aoi_moves").pending_count() == 1,
		"unknown monster movement is cached without creating a scene")

	manager.call("_on_entity_spawned", {
		"Type": "SpawnEntity",
		"EntityID": monster_id,
		"EntityType": 1,
		"Name": "arrival",
		"LocX": 100.0, "LocY": 200.0,
		"ExtraData": {"HP": 10}
	})
	var monster_templates: Dictionary = manager.get("_monster_spawn_templates") as Dictionary
	var monster: Dictionary = monster_templates.get(monster_id, {}) as Dictionary
	_check(float(monster.get("LocX", -1)) == 320.0 and
		float(monster.get("LocY", -1)) == 480.0,
		"Move before first Spawn wins without teleporting back")
	_check(int((monster.get("ExtraData", {}) as Dictionary).get("HP", -1)) == 10,
		"latest movement does not discard authoritative spawn metadata")

	var player_id: int = 300801
	manager.call("_on_entity_spawned", {
		"Type": "SpawnEntity",
		"EntityID": player_id,
		"EntityType": 0,
		"Name": "peer",
		"LocX": 12.0, "LocY": 14.0,
		"ExtraData": {}
	})
	manager.call("_on_entity_moved", {
		"Type": "MoveEntity",
		"EntityID": player_id,
		"X": 90.0, "Y": 95.0
	})
	var queued: Array = manager.get("_spawn_queue") as Array
	var player_snapshot: Dictionary = {}
	for entry in queued:
		if int(entry.get("EntityID", 0)) == player_id:
			player_snapshot = entry
			break
	_check(not player_snapshot.is_empty(),
		"peer is queued for scene instantiation rather than duplicated")
	var resolved: Dictionary = manager.call("_get_current_queued_spawn_data", player_snapshot)
	_check(float(resolved.get("LocX", -1)) == 90.0 and
		float(resolved.get("LocY", -1)) == 95.0,
		"queued player also receives most recent position before instantiation")

	manager.call("_on_entity_despawned", {"EntityID": monster_id})
	manager.call("_on_entity_moved", {
		"Type": "MoveEntity", "EntityID": monster_id, "X": 9999, "Y": 9999
	})
	_check(manager.get("_pending_aoi_moves").pending_count() == 0,
		"delayed monster movement after AOI exit is suppressed")
	manager.call("_on_entity_spawned", {
		"Type": "SpawnEntity", "EntityID": monster_id,
		"EntityType": 1, "Name": "reenter",
		"LocX": 60, "LocY": 70, "ExtraData": {}
	})
	monster = monster_templates.get(monster_id, {}) as Dictionary
	_check(float(monster.get("LocX", -1)) == 60.0 and
		float(monster.get("LocY", -1)) == 70.0,
		"fresh AOI re-entry ignores stale movement after previous Despawn")

	# EntityDeath, not just explicit DespawnEntity, terminates a monster
	# incarnation. Late old movement must not spoil the next respawn.
	var dying_id: int = 9300802
	manager.call("_on_entity_spawned", {
		"Type": "SpawnEntity", "EntityID": dying_id,
		"EntityType": 1, "Name": "dying",
		"LocX": 80, "LocY": 85, "ExtraData": {}
	})
	manager.call("_on_entity_death_received", {
		"Type": "EntityDeath", "EntityID": dying_id, "EntityType": 1
	})
	manager.call("_on_entity_moved", {
		"Type": "MoveEntity", "EntityID": dying_id,
		"X": 5000, "Y": 6000
	})
	manager.call("_on_entity_spawned", {
		"Type": "SpawnEntity", "EntityID": dying_id,
		"EntityType": 1, "Name": "respawn",
		"LocX": 44, "LocY": 55, "ExtraData": {}
	})
	var respawn: Dictionary = monster_templates.get(dying_id, {}) as Dictionary
	_check(float(respawn.get("LocX", -1)) == 44.0 and
		float(respawn.get("LocY", -1)) == 55.0,
		"EntityDeath blocks late Move from corrupting next monster respawn")

	manager.call("clear_map_entities")
	_check(manager.get("_pending_aoi_moves").pending_count() == 0 and
		monster_templates.is_empty() and
		(manager.get("_spawn_queue") as Array).is_empty(),
		"map transition clears pending, queued, and known monster data")
	manager.free()
