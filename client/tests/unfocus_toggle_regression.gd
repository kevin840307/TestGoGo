extends SceneTree

# Godot --headless --path <isolated-project> --script res://tests/unfocus_toggle_regression.gd
const POLICY = preload("res://scripts/unfocus_presentation_policy.gd")
var failures: int = 0

func _check(value: bool, label: String) -> void:
	if value:
		print("[UNFOCUS_TOGGLE_PASS] ", label)
	else:
		failures += 1
		push_error("[UNFOCUS_TOGGLE_FAIL] " + label)

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	_check(not POLICY.should_background(true, false, true),
		"focused normal window never sleeps regardless of enabled Unfocus")
	_check(not POLICY.should_background(true, false, false),
		"focused normal window remains active when optional Unfocus disabled")
	_check(POLICY.should_background(false, false, true),
		"Alt+Tab saves visuals and audio when Unfocus enabled")
	_check(not POLICY.should_background(false, false, false),
		"Alt+Tab leaves renderer/FPS/audio active when Unfocus disabled")
	_check(POLICY.should_background(false, true, true),
		"minimize saves resources with Unfocus enabled")
	_check(POLICY.should_background(false, true, false),
		"minimize ALWAYS saves resources with Unfocus disabled")
	_check(POLICY.should_background(true, true, false),
		"minimize wins even when OS briefly reports window focused")
	_check(POLICY.should_background(true, true, true),
		"minimize also wins when optional Unfocus enabled")
	print("[UNFOCUS_TOGGLE_TEST] ", "ALL PASS" if failures == 0 else "%d FAILURES" % failures)
	quit(0 if failures == 0 else 1)
