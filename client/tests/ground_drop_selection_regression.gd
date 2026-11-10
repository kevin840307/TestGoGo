extends SceneTree

# Server-free deterministic oracle test of display-only ground drop selection.
const PROFILE = preload("res://scripts/client_performance_profile.gd")
const SPATIAL_INDEX = preload("res://scripts/ground_drop_spatial_index.gd")
var _failures: int = 0

func _check(ok: bool, label: String) -> void:
	if ok:
		print("[DROP_SELECTION_PASS] " + label)
	else:
		_failures += 1
		push_error("[DROP_SELECTION_FAIL] " + label)

# Frozen reference implementation from before bounded top-K optimization.
# Use it only in tests to confirm exact selected IDs and tie behavior.
func _reference_select(
	records: Dictionary, observer: Vector2, radius: float, limit: int
) -> Dictionary:
	var selected: Dictionary = {}
	if limit <= 0:
		return selected
	var candidates: Array[Dictionary] = []
	for raw_id in records:
		var record: Dictionary = records[raw_id]
		var at := Vector2(float(record.get("LocX", 0.0)), float(record.get("LocY", 0.0)))
		var dist: float = at.distance_squared_to(observer)
		if dist > radius * radius:
			continue
		var extra: Dictionary = record.get("ExtraData", {})
		candidates.append({
			"id": int(raw_id), "item": int(extra.get("ItemIndex", 0)), "distance_sq": dist
		})
	candidates.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		if a["distance_sq"] == b["distance_sq"]:
			return int(a["id"]) < int(b["id"])
		return float(a["distance_sq"]) < float(b["distance_sq"])
	)
	var counted: Dictionary = {}
	for candidate in candidates:
		var item_id: int = int(candidate["item"])
		if item_id <= 0:
			selected[int(candidate["id"])] = true
			continue
		if int(counted.get(item_id, 0)) >= limit:
			continue
		counted[item_id] = int(counted.get(item_id, 0)) + 1
		selected[int(candidate["id"])] = true
	return selected

func _same_ids(left: Dictionary, right: Dictionary) -> bool:
	if left.size() != right.size():
		return false
	for id in left:
		if not right.has(id):
			return false
	return true

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var records: Dictionary = {}
	# Thousands of same-item drops, multiple item types and equal distances.
	# No packets are sent; this only exercises presentation choices.
	for i: int in range(4500):
		var item_id: int = 1000 + (i % 7)
		var x: float = float((i * 97) % 2001) - 1000.0
		var y: float = float((i * 53) % 2001) - 1000.0
		records[100000 + i] = {
			"LocX": x, "LocY": y, "ExtraData": {"ItemIndex": item_id}
		}
	records[1] = {"LocX": 100.0, "LocY": 0.0, "ExtraData": {"ItemIndex": 1001}}
	records[2] = {"LocX": -100.0, "LocY": 0.0, "ExtraData": {"ItemIndex": 1001}}
	records[3] = {"LocX": 1.0, "LocY": 1.0, "ExtraData": {"ItemIndex": 0}}
	records[4] = {"LocX": 5000.0, "LocY": 5000.0, "ExtraData": {"ItemIndex": 0}}

	var count_before: int = records.size()
	for limit: int in [0, 1, 5, 10]:
		for radius: float in [5.0, 500.0, 1500.0]:
			var expected: Dictionary = _reference_select(records, Vector2.ZERO, radius, limit)
			var actual: Dictionary = PROFILE.select_visible_ground_drop_ids(
				records, Vector2.ZERO, radius, limit
			)
			_check(_same_ids(actual, expected),
				"matches sorted reference for limit=%d radius=%.0f" % [limit, radius])
	_check(records.size() == count_before, "authoritative drop dictionary unchanged")

	# Real presentation index stores only compact positions/item types.
	# Compare to the independent sorting oracle at several AOI sizes and caps.
	var index = SPATIAL_INDEX.new()
	for raw_id in records:
		index.upsert(int(raw_id), records[raw_id])
	_check(index.records.size() == count_before, "all 4504 drops indexed as data only")
	for limit: int in [1, 5, 10]:
		for radius: float in [5.0, 500.0, 1500.0]:
			var oracle: Dictionary = _reference_select(records, Vector2.ZERO, radius, limit)
			var indexed: Dictionary = index.find_nearest(Vector2.ZERO, radius, limit)
			_check(_same_ids(indexed, oracle),
				"spatial top-K agrees with reference for cap=%d radius=%.0f" % [limit, radius])
	var selected_once: Dictionary = index.find_nearest(Vector2.ZERO, 500.0, 5)
	var builds_before: int = index.rebuild_count
	_check(_same_ids(selected_once, index.find_nearest(Vector2.ZERO, 500.0, 5)),
		"same query returns the exact same ground winners")
	_check(index.rebuild_count == builds_before,
		"stationary observer does not rebuild ground candidates")
	_check(index.last_visited_items < count_before,
		"spatial radius examines fewer drops than the entire map")
	# A duplicate snapshot should not invalidate the presentation cache.
	index.upsert(1, records[1])
	index.find_nearest(Vector2.ZERO, 500.0, 5)
	_check(index.rebuild_count == builds_before, "duplicate server snapshot causes no redraw selection")
	var relocated: Dictionary = records[1].duplicate(true)
	relocated["LocX"] = 7000.0
	index.upsert(1, relocated)
	var revised: Dictionary = records.duplicate()
	revised[1] = relocated
	_check(_same_ids(index.find_nearest(Vector2.ZERO, 500.0, 5),
		_reference_select(revised, Vector2.ZERO, 500.0, 5)), "moving ground item updates spatial cell")
	index.erase(2)
	revised.erase(2)
	_check(_same_ids(index.find_nearest(Vector2.ZERO, 500.0, 5),
		_reference_select(revised, Vector2.ZERO, 500.0, 5)), "item despawn selects a replacement")
	index.clear()
	_check(index.records.is_empty() and index.cells.is_empty(),
		"map switch clears cached spatial records")

	var unknown: Dictionary = PROFILE.select_visible_ground_drop_ids(
		records, Vector2.ZERO, 500.0, 5
	)
	_check(unknown.has(3) and not unknown.has(4),
		"unknown ItemIndex remains visible inside AOI and hidden outside")
	# Repeated requests must be deterministic even though no full sort runs.
	_check(_same_ids(unknown, PROFILE.select_visible_ground_drop_ids(
		records, Vector2.ZERO, 500.0, 5
	)), "selection stable across repeated unchanged scans")
	print("[DROP_SELECTION_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	quit(0 if _failures == 0 else 1)
