extends Node

const BG_CACHE_TRIM = preload("res://scripts/background_cache_trim.gd")

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
	var normal_profile: Dictionary = {"foreground_fps": 120, "background_fps": 20}
	_check(GameManager.fps_cap_for_focus(normal_profile, true, false) == 120,
		"focused login screen renders at the configured 120 FPS")
	_check(GameManager.fps_cap_for_focus(normal_profile, false, false) == 120,
		"unfocused login and character selection are never forced down to 10/20 FPS")
	_check(not GameManager.should_throttle_background_visuals(false, false),
		"login startup never disables the render loop on focus loss")
	_check(GameManager.fps_cap_for_focus(normal_profile, true, true) == 120,
		"active foreground game retains its configured FPS")
	_check(GameManager.fps_cap_for_focus(normal_profile, false, true) == 20,
		"unfocused active game alone receives the background FPS cap")
	_check(GameManager.should_throttle_background_visuals(false, true),
		"background rendering optimization remains enabled after entering the world")
	_check(GameManager.fps_cap_for_focus(normal_profile, false, false) == 120,
		"returning to login restores full FPS even while unfocused")
	_check(GameManager.configured_foreground_fps(normal_profile) == 120,
		"foreground FPS is independent from a previously throttled Engine.max_fps")
	_check(GameManager.classify_frame_gap(100000, false, 10) == "background_throttle",
		"a custom 10 FPS in-world background profile is classified correctly")
	_check(GameManager.classify_frame_gap(250000, false, 10) == "background_stall",
		"an unexpectedly long background frame remains visible in diagnostics")
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

	# A brief Alt+Tab never evicts resources; active SpriteFrames stay
	# retained by consumers. Idle cache entries are the only eviction
	# candidates, and the newest cached visuals stay warm on refocus.
	_check(not GameManager.should_run_background_cache_sweep(false, true, 11999, 0, 40000),
		"11.999-second focus loss does not unload resources")
	_check(GameManager.should_run_background_cache_sweep(false, true, 12000, 0, 40000),
		"12-second focus loss permits one idle-cache sweep")
	_check(not GameManager.should_run_background_cache_sweep(true, true, 60000, 0, 80000),
		"focused window never trims caches")
	_check(not GameManager.should_run_background_cache_sweep(false, false, 60000, 0, 80000),
		"login, loading and map transitions do not trim")
	_check(not GameManager.should_run_background_cache_sweep(false, true, 60000, 40000, 80000),
		"repeated background sweeps obey the 60-second cooldown")
	_check(GameManager.should_run_background_cache_sweep(false, true, 60000, 40000, 100000),
		"long-running background client can reclaim later stale assets")

	var held_resource := Resource.new()
	var idle_cache: Dictionary = {"cold": Resource.new(), "active": held_resource}
	var removed: int = BG_CACHE_TRIM.trim_unused(idle_cache, 0)
	_check(removed == 1 and not idle_cache.has("cold") and idle_cache.has("active"),
		"cold resource is evicted while an independently held resource survives")
	_check(idle_cache["active"] == held_resource,
		"active cached resource keeps its identity on focus return")
	var recent_cache: Dictionary = {"older": Resource.new(), "newest": Resource.new()}
	removed = BG_CACHE_TRIM.trim_unused(recent_cache, 1)
	_check(removed == 1 and not recent_cache.has("older") and recent_cache.has("newest"),
		"most recent assets remain warmed even if otherwise unreferenced")
	var large_idle_cache: Dictionary = {
		"a": Resource.new(), "b": Resource.new(),
		"c": Resource.new(), "d": Resource.new()
	}
	removed = BG_CACHE_TRIM.trim_unused(large_idle_cache, 0, 2)
	_check(removed == 2 and large_idle_cache.size() == 2,
		"background sweep evicts in bounded batches to avoid long frame stalls")
	_check(BG_CACHE_TRIM.trim_unused(large_idle_cache, 0, 0) == 0,
		"zero eviction budget never touches live resources")

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
