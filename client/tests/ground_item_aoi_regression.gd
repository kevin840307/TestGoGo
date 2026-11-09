extends SceneTree

# Godot --headless --path client --script res://tests/ground_item_aoi_regression.gd
# Server-free: tests cached-drop lifetime, rendering radius and priority.
const ENTITY_SCRIPT = preload("res://scripts/network_entity_manager.gd")
var _failures: int = 0


func _check(ok: bool, description: String) -> void:
	if ok:
		print("[GROUND_AOI_PASS] " + description)
	else:
		_failures += 1
		push_error("[GROUND_AOI_FAIL] " + description)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var item: Dictionary = {"EntityID": 70001, "EntityType": 3, "LocX": 100.0, "LocY": 40.0}
	_check(ENTITY_SCRIPT.is_ground_inside_aoi(item, Vector2.ZERO, 2000.0),
		"nearby ground drops remain drawable and pickable")
	_check(not ENTITY_SCRIPT.is_ground_inside_aoi(item, Vector2(5000, 0), 2000.0),
		"distant ground drops defer expensive scene nodes")
	_check(ENTITY_SCRIPT.GROUND_AOI_EXIT_RADIUS > ENTITY_SCRIPT.GROUND_AOI_ENTER_RADIUS,
		"enter/exit hysteresis prevents edge toggling")
	_check(ENTITY_SCRIPT.is_ground_inside_aoi(
		{"LocX": -3000.0, "LocY": -3000.0}, Vector2(-3050, -3050), 2000.0
	), "negative-coordinate maps and teleport destinations remain supported")

	var manager: Node = ENTITY_SCRIPT.new()
	var templates: Dictionary = manager.get("_ground_spawn_templates") as Dictionary
	var created_msec: int = Time.get_ticks_msec() - 2500
	templates[70001] = {
		"EntityID": 70001,
		"EntityType": 3,
		"LocX": 100.0,
		"LocY": 40.0,
		"IsSnapshot": false,
		"_aoi_cached_at": created_msec,
		"ExtraData": {
			"ItemIndex": 1001,
			"Count": 3,
			"RemainingTime": 60.0,
			"ProtectTimeSeconds": 10.0,
			"OwnerDBID": 200
		}
	}
	var first: Dictionary = manager.call("_ground_spawn_data_at_current_time", 70001)
	var extra: Dictionary = first.get("ExtraData", {}) as Dictionary
	_check(float(extra.get("RemainingTime", 0)) < 59.0 and float(extra.get("RemainingTime", 0)) > 56.0,
		"rehydrating deferred item does not restart its server lifetime")
	_check(float(extra.get("ProtectTimeSeconds", 0)) < 9.0 and float(extra.get("ProtectTimeSeconds", 0)) > 6.0,
		"rehydrating deferred item does not restart ownership protection")
	_check(bool(first.get("IsSnapshot", false)),
		"old deferred drops do not replay newly killed monster bounce")
	_check(not bool(templates[70001].get("IsSnapshot", true)),
		"visual snapshot creation never mutates authoritative cached metadata")
	_check(is_equal_approx(
		float((templates[70001]["ExtraData"] as Dictionary)["RemainingTime"]), 60.0
	), "cached authoritative lifetime remains immutable")
	templates[70001]["_aoi_cached_at"] = Time.get_ticks_msec() - 300000
	var expired: Dictionary = manager.call("_ground_spawn_data_at_current_time", 70001)
	_check(is_zero_approx(float((expired["ExtraData"] as Dictionary)["RemainingTime"])),
		"long-deferred snapshot clamps negative remaining lifetime to zero")

	var aoi_stats: Dictionary = manager.call("get_ground_aoi_stats")
	_check(int(aoi_stats.get("known", 0)) == 1,
		"diagnostics count all known items, including non-instantiated drops")
	manager.call("_on_entity_despawned", {"EntityID": 70001})
	aoi_stats = manager.call("get_ground_aoi_stats")
	_check(int(aoi_stats.get("known", 1)) == 0,
		"authoritative DespawnEntity removes cached drops too")

	# Inserting many equal-priority drops should be stable and should not
	# scan all previously queued entries to find an insertion position.
	for i: int in range(256):
		manager.call("_enqueue_spawn_by_priority", {
			"EntityID": 80000 + i, "EntityType": 3, "LocX": 0, "LocY": 0
		})
	var queue: Array = manager.get("_spawn_queue") as Array
	_check(queue.size() == 256, "bulk spawn queue retains every drop")
	var ordered: bool = true
	for i: int in range(queue.size()):
		if int((queue[i] as Dictionary).get("EntityID", -1)) != 80000 + i:
			ordered = false
			break
	_check(ordered, "binary priority insertion preserves stable order for equal-priority drops")
	# A large drop snapshot must never delay the creation of players.
	manager.call("_enqueue_spawn_by_priority", {
		"EntityID": 300801, "EntityType": 0, "LocX": 20.0, "LocY": 40.0
	})
	queue = manager.get("_spawn_queue") as Array
	_check(int((queue[0] as Dictionary).get("EntityID", -1)) == 300801,
		"player scene spawn bypasses hundreds of queued ground drops")

	manager.call("_enqueue_spawn_by_priority", {
		"EntityID": 1200001, "EntityType": 1, "LocX": 30.0, "LocY": 50.0
	})
	queue = manager.get("_spawn_queue") as Array
	_check(int((queue[1] as Dictionary).get("EntityID", -1)) == 1200001,
		"nearby monster instantiates before ground-drop visuals")

	manager.call("_enqueue_spawn_by_priority", {
		"EntityID": 300802, "EntityType": 0, "LocX": 60.0, "LocY": 70.0
	})
	queue = manager.get("_spawn_queue") as Array
	_check(int((queue[0] as Dictionary).get("EntityID", -1)) == 300801 and
		int((queue[1] as Dictionary).get("EntityID", -1)) == 300802,
		"player arrival order stays stable independently of monster and drop backlog")
	_check(queue.size() == 259,
		"priority staging never discards ground items or other entities")
	manager.free()

	print("[GROUND_AOI_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	quit(0 if _failures == 0 else 1)
