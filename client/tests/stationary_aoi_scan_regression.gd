extends SceneTree

# Run:
# godot --headless --path client --script res://tests/stationary_aoi_scan_regression.gd
#
# Does not need a server or character. Tests the bounded idle-AOI scheduler
# plus source guard for the immediate authoritative Spawn/Move/Despawn paths.
const MANAGER = preload("res://scripts/network_entity_manager.gd")
var _failures: int = 0


func _check(ok: bool, label: String) -> void:
	if ok:
		print("[STATIONARY_AOI_PASS] " + label)
	else:
		_failures += 1
		push_error("[STATIONARY_AOI_FAIL] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var manager: Node = MANAGER.new()
	var stats: Dictionary = manager.call("get_ground_aoi_stats")
	_check(int(stats.get("ground_full_scans", -1)) == 0
		and int(stats.get("ground_skipped_scans", -1)) == 0,
		"new map starts with zero idle ground scan counters")
	_check(int(stats.get("monster_full_scans", -1)) == 0
		and int(stats.get("monster_skipped_scans", -1)) == 0,
		"new map starts with zero monster scan counters")
	manager.free()

	_check(MANAGER.should_run_stationary_aoi_scan(true, 0.0, 1.0),
		"first AOI scan always runs when observer enters the map")
	_check(MANAGER.should_run_stationary_aoi_scan(true, 0.25, 1.0),
		"player movement and camera zoom force prompt AOI update")
	_check(not MANAGER.should_run_stationary_aoi_scan(false, 0.20, 1.0),
		"stationary monsters do not need five complete scans per second")
	_check(not MANAGER.should_run_stationary_aoi_scan(false, 0.75, 1.0),
		"stationary ground drops can reuse their cached AOI state")
	_check(MANAGER.should_run_stationary_aoi_scan(false, 1.0, 1.0),
		"both AOI scans still run every second as a safety refresh")
	_check(MANAGER.MONSTER_AOI_UPDATE_INTERVAL == 0.20
		and MANAGER.GROUND_AOI_UPDATE_INTERVAL == 0.25,
		"moving observers retain original fast 200/250ms AOI cadence")
	_check(MANAGER.MONSTER_AOI_STATIONARY_SCAN_INTERVAL == 1.0
		and MANAGER.GROUND_AOI_STATIONARY_SCAN_INTERVAL == 1.0,
		"stationary fallback remains one second, not permanently disabled")

	var source: String = FileAccess.get_file_as_string(
		"res://scripts/network_entity_manager.gd"
	)
	_check(source.contains("if _is_monster_inside_aoi(cached, _monster_aoi_radii().x):")
		and source.contains("_queue_cached_monster_spawn(entity_id)"),
		"server MoveEntity can still awaken a deferred monster immediately")
	_check(source.contains("_ground_spawn_templates[entity_id] = cached_ground")
		and source.contains("_deferred_ground_spawns[entity_id] = cached_ground"),
		"ground SpawnEntity retains authoritative drop data outside visibility range")
	_check(source.contains('if bool(drop.get_meta("is_requesting", false)):'),
		"AOI unloading still protects drops being approached for pickup")
	_check(source.contains("_ground_aoi_last_context = null")
		and source.contains("_monster_aoi_last_observer = null"),
		"clearing or replacing maps forces the next AOI scan")
	_check(source.contains('or _ground_aoi_last_context["enter"] != aoi["enter"]')
		and source.contains('or _ground_aoi_last_context["exit"] != aoi["exit"]'),
		"minimap camera zoom or viewport radius change does not leave stale ground AOI")

	print("[STATIONARY_AOI_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	quit(0 if _failures == 0 else 1)
