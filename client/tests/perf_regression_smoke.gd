extends SceneTree

# Offline, server-free regression smoke tests for the hot asset/queue paths.
# Run: godot --headless --path client --script res://tests/perf_regression_smoke.gd
# Requires a complete imported Godot project (run the editor import once).

var _failures: int = 0

func _check(condition: bool, description: String) -> void:
	if condition:
		print("[PERF_SMOKE_PASS] ", description)
	else:
		_failures += 1
		push_error("[PERF_SMOKE_FAIL] " + description)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	await process_frame

	# Multiple PaperDolls must reuse the exact same cached source frames.
	var body_a: SpriteFrames = DollLoader.get_sprite_frames("B/Standard", "B-Body")
	var body_b: SpriteFrames = DollLoader.get_sprite_frames("B/Standard", "B-Body")
	_check(body_a != null, "player body asset loads")
	_check(body_a != null and body_a == body_b, "PaperDoll source cache reuses SpriteFrames")
	if body_a != null:
		var doll: PaperDollEngine = PaperDollEngine.new()
		var unchanged: SpriteFrames = doll._get_controller_timed_frames(
			body_a, "__PERF_SMOKE_UNKNOWN_CONTROLLER__",
			{"__PERF_SMOKE_UNKNOWN_ACTION__": ["__PERF_SMOKE_NEVER_MATCHES__"]},
			true, true
		)
		_check(unchanged == body_a, "no matching ACT timeline preserves original frames")
		doll.free()

	# Monsters with the same SpriteID must share atlas/animation resources.
	var monster_a: SpriteFrames = MonsterLoader.get_monster_frames("EN004A", true)
	var monster_b: SpriteFrames = MonsterLoader.get_monster_frames("EN004A", true)
	_check(monster_a != null, "monster atlas asset loads")
	_check(monster_a != null and monster_a == monster_b, "monster SpriteFrames cache is reused")

	# Prioritize ground items and NPCs over monsters without dropping packets.
	# This tests only queue ordering and does not instantiate world entities.
	var queued_before: Array = NetworkEntityManager._spawn_queue.duplicate(true)
	NetworkEntityManager._spawn_queue.clear()
	NetworkEntityManager._enqueue_spawn_by_priority({"EntityID": 101, "EntityType": 1})
	NetworkEntityManager._enqueue_spawn_by_priority({"EntityID": 102, "EntityType": 2})
	NetworkEntityManager._enqueue_spawn_by_priority({"EntityID": 103, "EntityType": 3})
	_check(NetworkEntityManager._spawn_queue.size() == 3, "spawn queue retains all entity types")
	if NetworkEntityManager._spawn_queue.size() == 3:
		_check(int(NetworkEntityManager._spawn_queue[0].get("EntityID", 0)) == 103,
			"ground items retain highest spawn priority")
		_check(int(NetworkEntityManager._spawn_queue[1].get("EntityID", 0)) == 102,
			"NPCs retain priority over monsters")
		_check(int(NetworkEntityManager._spawn_queue[2].get("EntityID", 0)) == 101,
			"monsters remain queued")
	NetworkEntityManager._spawn_queue.clear()
	for previous in queued_before:
		NetworkEntityManager._spawn_queue.append(previous)

	# A movement packet may change only the top-level coordinates. Nested
	# spawn metadata must remain intact for deferred AOI spawning.
	var test_id: int = 987654321
	var meta: Dictionary = {"Level": 9, "MaxHP": 200, "CurrentHP": 175}
	var template: Dictionary = {
		"EntityID": test_id, "EntityType": 1,
		"LocX": 10.0, "LocY": 20.0, "ExtraData": meta
	}
	NetworkEntityManager._monster_spawn_templates[test_id] = template.duplicate(true)
	NetworkEntityManager._deferred_monster_spawns[test_id] = template.duplicate(true)
	NetworkEntityManager._on_entity_moved({
		"EntityID": test_id, "X": 30.0, "Y": 40.0, "SpeedTime": 0.25
	})
	var updated: Dictionary = NetworkEntityManager._monster_spawn_templates.get(test_id, {})
	_check(is_equal_approx(float(updated.get("LocX", -1.0)), 30.0)
		and is_equal_approx(float(updated.get("LocY", -1.0)), 40.0),
		"AOI cached monster position follows movement packets")
	var extra: Dictionary = updated.get("ExtraData", {})
	_check(int(extra.get("Level", -1)) == 9 and int(extra.get("CurrentHP", -1)) == 175,
		"AOI movement preserves nested monster data")
	# A queued monster must use the newest authoritative position if
	# MoveEntity arrives before the scene is instantiated.
	var stale_spawn: Dictionary = template.duplicate(true)
	var resolved_spawn: Dictionary = NetworkEntityManager._get_current_queued_spawn_data(stale_spawn)
	_check(is_equal_approx(float(resolved_spawn.get("LocX", -1.0)), 30.0)
		and is_equal_approx(float(resolved_spawn.get("LocY", -1.0)), 40.0),
		"queued monster spawn uses latest MoveEntity position")
	_check(int((resolved_spawn.get("ExtraData", {}) as Dictionary).get("MaxHP", -1)) == 200,
		"latest queued spawn preserves health metadata")
	_check(int(stale_spawn.get("LocX", -1)) == 10,
		"queued source snapshot remains unchanged")
	_check(NetworkEntityManager._get_current_queued_spawn_data({"EntityID": 102, "EntityType": 2}).get("EntityType", -1) == 2,
		"NPC queued spawn data is unchanged")
	_check(MonsterEntity.NETWORK_MOVE_MIN_SPEED <= 10.0,
		"short monster waypoints are not forced to excessive speed")
	_check(MonsterEntity.MOVE_IDLE_GRACE_SECONDS >= 0.25,
		"monster walk animation covers the 250ms server tick")
	NetworkEntityManager._monster_spawn_templates.erase(test_id)
	NetworkEntityManager._deferred_monster_spawns.erase(test_id)
	NetworkEntityManager._remove_queued_spawn(test_id)

	if _failures == 0:
		print("[PERF_SMOKE] ALL PASS")
	else:
		print("[PERF_SMOKE] FAILURES=%d" % _failures)
	quit(0 if _failures == 0 else 1)
