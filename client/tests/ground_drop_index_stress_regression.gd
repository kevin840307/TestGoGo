extends SceneTree

# Standalone: validates 4096 authoritative drops with no original art assets.
# Godot --headless --path <isolated-project> --script res://tests/ground_drop_index_stress_regression.gd
const DROP_INDEX = preload("res://scripts/ground_drop_spatial_index.gd")
var failures: int = 0

func _check(ok: bool, label: String) -> void:
	if ok:
		print("[MASS_DROPS_PASS] ", label)
	else:
		failures += 1
		push_error("[MASS_DROPS_FAIL] " + label)

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	await process_frame
	var index = DROP_INDEX.new()
	for i in range(4096):
		var x: float = float((i % 64) - 32) * 128.0
		var y: float = float((i / 64) - 32) * 128.0
		index.upsert(1000 + i, {
			"LocX": x, "LocY": y,
			"ExtraData": {"ItemIndex": (i % 12) + 1}
		})
	_check(index.records.size() == 4096,
		"all 4096 authoritative items survive indexing")
	var center := Vector2.ZERO
	var nearby: Dictionary = index.find_nearest(center, 500.0, 5)
	_check(nearby.size() > 0 and nearby.size() <= 60,
		"duplicate limit selects nearest five of each item type")
	_check(index.last_visited_items < 600,
		"stationary AOI visits only nearby grid cells, not every drop")
	var built: int = index.rebuild_count
	var repeated: Dictionary = index.find_nearest(center, 500.0, 5)
	_check(index.rebuild_count == built and repeated.size() == nearby.size(),
		"unchanged observer reuses selection without a grid rebuild")
	var victim: int = int(nearby.keys()[0])
	index.erase(victim)
	var after_despawn: Dictionary = index.find_nearest(center, 500.0, 5)
	_check(not after_despawn.has(victim) and not index.records.has(victim),
		"authoritative despawn immediately removes the item from selection")
	index.upsert(99999, {
		"LocX": 0.0, "LocY": 0.0,
		"ExtraData": {"ItemIndex": 7}
	})
	var after_insert: Dictionary = index.find_nearest(center, 500.0, 5)
	_check(after_insert.has(99999), "new nearby drop enters selection")
	index.find_nearest(Vector2(3000.0, 3000.0), 500.0, 5)
	_check(index.last_visited_items < 600,
		"camera movement also limits work to nearby grid cells")
	index.clear()
	_check(index.records.is_empty() and index.cells.is_empty()
		and index.find_nearest(Vector2.ZERO, 500.0, 5).is_empty(),
		"map transition clears all stale item indices")
	print("[MASS_DROPS_TEST] ", "ALL PASS" if failures == 0 else "%d FAILURES" % failures)
	quit(0 if failures == 0 else 1)
