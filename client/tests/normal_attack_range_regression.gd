extends SceneTree

# Godot --headless --path client --script res://tests/normal_attack_range_regression.gd
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

func _run() -> void:
	# All cases use numeric item attack_range data, never weapon style.
	check(REACH.from_stat(80, 0) == 40.0,
		"unarmed base 80 uses 40 world units")
	check(REACH.from_stat(80 + 120, 120) == 40.0,
		"ordinary 120-range sword uses exactly same distance as unarmed")
	check(REACH.from_stat(80 + 80, 80) == 40.0,
		"weapon with lower numeric base range also matches unarmed")
	check(REACH.from_stat(80 + 200, 200) == 80.0,
		"special 200-range sword gains 40 world units")
	check(REACH.from_stat(80 + 400, 400) == 180.0,
		"400-range weapon keeps long reach by numeric value")
	check(REACH.from_stat(80 + 120 + 50, 120) == 65.0,
		"accessory range bonus 50 extends standard sword reach")
	check(REACH.from_stat(80 + 50, 0) == 65.0,
		"unarmed accessory range bonus also extends reach")
	check(REACH.from_stat(80 + 200 + 50, 200) == 105.0,
		"special weapon and accessory numeric bonuses both count")
	check(REACH.from_stat(-10, 0) == 40.0,
		"negative numeric input stays safe")
	check(REACH.from_stat(80 + 121, 121) > REACH.from_stat(80 + 120, 120),
		"extra numeric weapon range beyond 120 increases reach")
	print("[NUMERIC_ATTACK_RANGE_REGRESSION] %s" % (
		"ALL PASS" if failures == 0 else "FAILURES=%d" % failures
	))
	quit(0 if failures == 0 else 1)
