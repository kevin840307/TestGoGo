extends SceneTree

# Compare the two recovered 2004/2009 legacy constants with numerical
# equipment/character normalization required by the current Godot client.
const REACH = preload("res://scripts/normal_attack_range.gd")
var failures: int = 0

func check(ok: bool, label: String) -> void:
	if ok:
		print("[NUMERIC_ATTACK_RANGE_PASS] " + label)
	else:
		failures += 1
		push_error("[NUMERIC_ATTACK_RANGE_FAIL] " + label)

func _initialize() -> void:
	call_deferred("_run")

func _legacy_reach(raw_weapon_reach: float) -> float:
	return sqrt(maxf(0.0, raw_weapon_reach * raw_weapon_reach - 200.0))

func _run() -> void:
	# The legacy default is 45 (stored as squared 2025). The original
	# attack/approach threshold is sqrt(reach² - 200), not 0.5 * stat.
	var close_reach: float = _legacy_reach(45.0)
	check(is_equal_approx(close_reach, sqrt(1825.0)),
		"recovered legacy 45^2 - 200 threshold")
	check(is_equal_approx(REACH.threshold_squared(80, 0), 1825.0),
		"original squared attack bound is 1825 for unarmed")
	check(REACH.within_range_squared(1824.0, 80, 0),
		"distance squared below threshold allows normal attack")
	check(not REACH.within_range_squared(1825.0, 80, 0),
		"exact equal distance follows original jge -> approach")
	check(not REACH.within_range_squared(1826.0, 80, 0),
		"distance squared above threshold continues approach")
	check(REACH.threshold_squared(80 + 120, 120) == 1825.0,
		"normal sword uses identical strict squared threshold as unarmed")
	check(is_equal_approx(REACH.from_stat(80, 0), close_reach),
		"unarmed stays at original default close reach")
	check(is_equal_approx(REACH.from_stat(80 + 120, 120), close_reach),
		"ordinary numeric 120 sword has exactly same stop distance as unarmed")
	check(is_equal_approx(REACH.from_stat(80 + 80, 80), close_reach),
		"weapon numeric value below 120 does not add range")
	check(is_equal_approx(REACH.from_stat(80 + 200, 200), _legacy_reach(125.0)),
		"special 200 sword increases numeric reach beyond baseline")
	check(is_equal_approx(REACH.from_stat(80 + 400, 400), _legacy_reach(325.0)),
		"long-range numeric 400 weapon remains ranged")
	check(is_equal_approx(REACH.from_stat(80 + 120 + 50, 120), _legacy_reach(95.0)),
		"accessory +50 extends standard sword's range")
	check(is_equal_approx(REACH.from_stat(80 + 50, 0), _legacy_reach(95.0)),
		"accessory +50 extends unarmed range")
	check(is_equal_approx(REACH.from_stat(80 + 200 + 50, 200), _legacy_reach(175.0)),
		"unusual weapon and accessory add their numeric excess")
	check(is_equal_approx(REACH.from_stat(-10, 0), close_reach),
		"invalid negative raw stat clamps to default close reach")
	check(REACH.from_stat(80 + 121, 121) > REACH.from_stat(80 + 120, 120),
		"numeric increase above 120 changes the range continuously")
	print("[NUMERIC_ATTACK_RANGE_REGRESSION] %s" % (
		"ALL PASS" if failures == 0 else "FAILURES=%d" % failures
	))
	quit(0 if failures == 0 else 1)
