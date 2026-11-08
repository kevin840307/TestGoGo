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
	check(ClientPerformanceProfile.normalize_fps_spin_step(0, 5) == 30,
		"SpinBox arrow up from unlimited jumps to 30")
	check(ClientPerformanceProfile.normalize_fps_spin_step(30, 25) == 0,
		"SpinBox arrow down from 30 jumps to unlimited")
	check(ClientPerformanceProfile.normalize_fps_spin_step(120, 125) == 144,
		"SpinBox arrow up from 120 reaches 144")
	check(ClientPerformanceProfile.normalize_fps_spin_step(144, 139) == 120,
		"SpinBox arrow down from 144 returns to 120")
	check(ClientPerformanceProfile.normalize_fps_spin_step(60, 65) == 65,
		"SpinBox standard arrow increments by five")
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
	check(hud_source.contains("option_fps_limit is SpinBox")
		and hud_source.contains("option_fps_limit.value_changed.connect(_on_opt_fps_limit_value_changed)")
		and hud_source.contains("option_fps_limit.set_value_no_signal(float(selected))"),
		"SpinBox numeric entry and arrows normalize values and update settings")
	var tscn: String = FileAccess.get_file_as_string("res://game_hud.tscn")
	check(tscn.contains("[node name=\"OptionFpsLimit\" type=\"SpinBox\"")
		and tscn.contains("step = 5.0"),
		"FPS settings widget is an editable SpinBox with five FPS per click")
	var original_cap: int = GameManager.current_foreground_fps_limit
	var original_load: int = GameManager.current_performance_load_mode
	var original_combat: int = GameManager.current_combat_render_mode
	GameManager.set_foreground_fps_limit(45)
	check(GameManager.current_foreground_fps_limit == 45 and Engine.max_fps == 45,
		"changing FPS limit actually applies to Engine.max_fps")
	GameManager.set_performance_modes(ClientPerformanceProfile.LOAD_LOW, ClientPerformanceProfile.COMBAT_NORMAL)
	check(Engine.max_fps == 45,
		"switching to Low does not override selected foreground FPS")
	GameManager.set_foreground_fps_limit(60)
	check(Engine.max_fps == 60,
		"FPS limit updates immediately without restarting client")
	GameManager.set_performance_modes(original_load, original_combat)
	GameManager.set_foreground_fps_limit(original_cap)
	var game_manager_source: String = FileAccess.get_file_as_string("res://scripts/game_manager.gd")
	check(game_manager_source.contains(
		"set_world_enhancement_enabled(current_performance_load_mode == ClientPerformanceProfile.LOAD_QUALITY)"),
		"enhancement owned by quality mode")
	print("[PERF_SETTINGS_REGRESSION] %s" % ("ALL PASS" if failures == 0 else "FAILURES=%d" % failures))
	quit(0 if failures == 0 else 1)
