extends Node

# Godot CLI project-mode test (autoload globals work):
# godot --headless --path client --scene res://tests/focus_performance_regression.tscn
# These tests do not actually change window focus, FPS cap, rendering, or audio.
var _failures: int = 0


func _check(ok: bool, description: String) -> void:
	if ok:
		print("[FOCUS_PERF_PASS] " + description)
	else:
		_failures += 1
		push_error("[FOCUS_PERF_FAIL] " + description)


func _ready() -> void:
	call_deferred("_run")


func _run() -> void:
	_check(GameManager.background_loading_display_fps(false) == 30,
		"normal unfocused gameplay keeps the configured 30 FPS throttle")
	_check(GameManager.background_loading_display_fps(true) == 10,
		"only the initial bootstrap loading bar may draw at 10 FPS")
	_check(GameManager.classify_frame_gap(100000, false, 10) == "background_throttle",
		"startup 10 FPS progress redraw is intentional, not a lag spike")
	_check(GameManager.classify_frame_gap(250000, false, 10) == "background_stall",
		"an unexpectedly long startup frame remains visible in diagnostics")
	_check(GameManager.classify_frame_gap(34000, false, 30) == "background_throttle",
		"current 30 FPS background cap is an intentional 33ms frame interval")
	_check(GameManager.classify_frame_gap(150000, false, 30) == "background_stall",
		"long background stalls remain detectable at 30 FPS")
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
	_check(GameManager.focus_transition_within_interval(10000, 40000, 25000),
		"a monster packet gap crossing a background/foreground transition is tagged")
	_check(not GameManager.focus_transition_within_interval(25001, 40000, 25000),
		"a packet gap starting after refocus is not marked as background-affected")
	_check(not GameManager.focus_transition_within_interval(10000, 24000, 25000),
		"future focus transitions cannot explain an earlier packet gap")

	# The background branch must stop visual-only work. Never pause
	# networking, gameplay world updates or the entire SceneTree.
	var cursor_was_processing: bool = CursorManager.is_processing()
	GameManager._set_background_visual_processing(true)
	_check(not CursorManager.is_processing(),
		"unfocused visual pause disables cursor animation/scanning")
	GameManager._set_background_visual_processing(false)
	_check(CursorManager.is_processing(),
		"refocus resumes visual cursor processing")
	CursorManager.set_process(cursor_was_processing)

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
	get_tree().quit(0 if _failures == 0 else 1)
