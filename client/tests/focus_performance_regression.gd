extends SceneTree

# godot --headless --path client --script res://tests/focus_performance_regression.gd
# These tests do not actually change window focus, FPS cap, rendering, or audio.
var _failures: int = 0


func _check(ok: bool, description: String) -> void:
	if ok:
		print("[FOCUS_PERF_PASS] " + description)
	else:
		_failures += 1
		push_error("[FOCUS_PERF_FAIL] " + description)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_check(GameManager.classify_frame_gap(200000, false, 5) == "background_throttle",
		"5 FPS intentional 200ms interval must not be called a lag spike")
	_check(GameManager.classify_frame_gap(300000, false, 5) == "background_throttle",
		"minor background scheduling jitter remains intentional throttling")
	_check(GameManager.classify_frame_gap(450000, false, 5) == "background_stall",
		"an unexpectedly long background frame is still reported")
	_check(GameManager.classify_frame_gap(200000, true, 5) == "foreground_stall",
		"the same 200ms gap while focused is a real foreground warning")
	_check(GameManager.classify_frame_gap(200000, false, 0) == "background_stall",
		"unfocused and uncapped frames are never misclassified as intentional 5 FPS")

	var context: String = GameManager._focus_perf_context()
	_check(context.contains("focus=") and context.contains("focus_actual="),
		"the log contains requested and actual focus state")
	_check(context.contains("fps_cap=") and context.contains("power_save="),
		"the log records the active FPS cap and power-save mode")
	_check(context.contains("render_loop=") and context.contains("audio_muted="),
		"the log distinguishes rendering shutdown and temporary sound mute")
	_check(context.contains("focus_age_ms="),
		"the log makes foreground/background duration visible")

	print("[FOCUS_PERF_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	quit(0 if _failures == 0 else 1)
