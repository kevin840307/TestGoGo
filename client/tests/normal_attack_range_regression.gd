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
	check(REACH.from_stat(80) == 40.0, "base unarmed 80 -> 40 world units")
	check(REACH.from_stat(80 + 120) == 100.0, "sword numeric 120 -> 100 world units")
	check(REACH.from_stat(80 + 200) == 140.0, "long-range sword numeric 200 -> 140 world units")
	check(REACH.from_stat(80 + 400) == 240.0, "gun numeric 400 -> 240 world units")
	check(REACH.from_stat(80 + 120 + 50) == 125.0,
		"additional 50 numerical range -> additional 25 world units")
	check(REACH.from_stat(-10) == 40.0, "negative numeric input stays safe")
	check(REACH.from_stat(80 + 121) > REACH.from_stat(80 + 120),
		"every positive attack range increase is respected")
	print("[NUMERIC_ATTACK_RANGE_REGRESSION] %s" % ("ALL PASS" if failures == 0 else "FAILURES=%d" % failures))
	quit(0 if failures == 0 else 1)
