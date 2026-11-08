extends SceneTree

# godot --headless --path client --script res://tests/aoi_monster_snapshot_order_regression.gd
# Server-free regression for delayed/duplicate WorldReady SpawnEntity versus
# newer MoveEntity while a monster is still queued or deferred.
const ENTITY_SCRIPT = preload("res://scripts/network_entity_manager.gd")
var _failures: int = 0


func _check(ok: bool, description: String) -> void:
	if ok:
		print("[MONSTER_AOI_ORDER_PASS] " + description)
	else:
		_failures += 1
		push_error("[MONSTER_AOI_ORDER_FAIL] " + description)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var manager: Node = ENTITY_SCRIPT.new()
	var monster_id: int = 9004321
	var old_spawn: Dictionary = {
		"Type": "SpawnEntity",
		"EntityID": monster_id,
		"EntityType": 1,
		"Name": "old",
		"LocX": 100.0,
		"LocY": 200.0,
		"ExtraData": {"HP": 100}
	}
	manager.call("_on_entity_spawned", old_spawn)
	var templates: Dictionary = manager.get("_monster_spawn_templates") as Dictionary
	var queued: Dictionary = manager.get("_queued_spawn_ids") as Dictionary
	_check(templates.has(monster_id) and queued.has(monster_id),
		"first monster spawn is cached and queued exactly once")

	manager.call("_on_entity_moved", {
		"Type": "MoveEntity",
		"EntityID": monster_id,
		"X": 456.0,
		"Y": 789.0,
		"SpeedTime": 0.1
	})
	var moved: Dictionary = templates.get(monster_id, {}) as Dictionary
	_check(float(moved.get("LocX", -1)) == 456.0 and
		float(moved.get("LocY", -1)) == 789.0,
		"new MoveEntity updates canonical queued monster coordinates")

	# A late/repeated snapshot can contain an older location, but newer
	# gameplay movement must take precedence while this incarnation exists.
	var replayed: Dictionary = old_spawn.duplicate(true)
	replayed["ExtraData"] = {"HP": 50}
	manager.call("_on_entity_spawned", replayed)
	var preserved: Dictionary = templates.get(monster_id, {}) as Dictionary
	_check(float(preserved.get("LocX", -1)) == 456.0 and
		float(preserved.get("LocY", -1)) == 789.0,
		"repeated SpawnEntity cannot undo newer movement")
	_check(int((preserved.get("ExtraData", {}) as Dictionary).get("HP", -1)) == 50,
		"repeated snapshot can still refresh non-position entity data")

	var spawn_queue: Array = manager.get("_spawn_queue") as Array
	var current: Dictionary = manager.call(
		"_get_current_queued_spawn_data", spawn_queue[0]
	)
	_check(spawn_queue.size() == 1 and queued.size() == 1,
		"repeated SpawnEntity never enqueues a second monster instance")
	_check(float(current.get("LocX", -1)) == 456.0 and
		float(current.get("LocY", -1)) == 789.0,
		"scene creation receives latest server MoveEntity location")

	manager.call("_on_entity_despawned", {"EntityID": monster_id})
	_check(not templates.has(monster_id) and
		not queued.has(monster_id) and
		(manager.get("_spawn_queue") as Array).is_empty(),
		"AOI exit removes both cached and queued stale monster")

	# A *new* SpawnEntity after a Despawn is a fresh entity incarnation.
	manager.call("_on_entity_spawned", {
		"Type": "SpawnEntity",
		"EntityID": monster_id,
		"EntityType": 1,
		"Name": "returned",
		"LocX": 10.0,
		"LocY": 20.0,
		"ExtraData": {"HP": 100}
	})
	var fresh: Dictionary = templates.get(monster_id, {}) as Dictionary
	_check(float(fresh.get("LocX", -1)) == 10.0 and
		float(fresh.get("LocY", -1)) == 20.0,
		"new AOI enter after Despawn uses the new authoritative spawn position")

	manager.call("clear_map_entities")
	_check(templates.is_empty() and queued.is_empty() and
		(manager.get("_spawn_queue") as Array).is_empty(),
		"map switch clears all previous monster and packet queue state")
	manager.free()
	print("[MONSTER_AOI_ORDER_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	quit(0 if _failures == 0 else 1)
