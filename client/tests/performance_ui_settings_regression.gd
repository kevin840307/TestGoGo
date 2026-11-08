extends SceneTree

# Run: godot --headless --path client --script res://tests/performance_ui_settings_regression.gd
var failures: int = 0


func check(ok: bool, label: String) -> void:
	if ok:
		print("[PERF_SETTINGS_PASS] " + label)
	else:
		failures += 1
		push_error("[PERF_SETTINGS_FAIL] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var options: Array[int] = ClientPerformanceProfile.fps_options()
	check(options.has(0) and options.has(30) and options.has(60) and options.has(120) and options.has(144),
		"unlimited, 30, 60, 120, 144 FPS are selectable")
	check(options.has(35) and options.has(40) and options.has(45) and options.has(50),
		"every 5 FPS step is available")
	check(ClientPerformanceProfile.sanitize_fps_limit(47) == 45,
		"unsupported FPS values are normalized")
	check(ClientPerformanceProfile.sanitize_fps_limit(0) == 0,
		"unlimited FPS value persists")
	var quality := ClientPerformanceProfile.resolve(ClientPerformanceProfile.LOAD_QUALITY, ClientPerformanceProfile.COMBAT_NORMAL)
	var multibox := ClientPerformanceProfile.resolve(ClientPerformanceProfile.LOAD_MULTIBOX, ClientPerformanceProfile.COMBAT_NORMAL)
	check(int(quality["foreground_fps"]) == int(multibox["foreground_fps"]),
		"quality/multibox never alter foreground FPS")
	var hud_source: String = FileAccess.get_file_as_string("res://game_hud.gd")
	check(not hud_source.contains("option_quality_enhancement")
		and not hud_source.contains("current_quality_enhancement_enabled"),
		"former independent enhancement control is removed")
	check(hud_source.contains("GameManager.set_foreground_fps_limit")
		and hud_source.contains("SETTING_KEY_FOREGROUND_FPS_LIMIT"),
		"FPS saved per account and applied to GameManager")
	var game_manager_source: String = FileAccess.get_file_as_string("res://scripts/game_manager.gd")
	check(game_manager_source.contains(
		"set_world_enhancement_enabled(current_performance_load_mode == ClientPerformanceProfile.LOAD_QUALITY)"),
		"enhancement owned by quality mode")
	print("[PERF_SETTINGS_REGRESSION] %s" % ("ALL PASS" if failures == 0 else "FAILURES=%d" % failures))
	quit(0 if failures == 0 else 1)
