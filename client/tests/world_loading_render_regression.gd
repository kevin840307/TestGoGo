extends Node

# Scene-mode Godot test: no Server, login or actual map assets needed.
# Protects against first-world black screens at 83-100% when Windows
# focus changes while an opaque loading overlay still covers the world.
var _failures: int = 0


func _check(ok: bool, label: String) -> void:
	if ok:
		print("[WORLD_RENDER_PASS] " + label)
	else:
		_failures += 1
		push_error("[WORLD_RENDER_FAIL] " + label)


func _ready() -> void:
	call_deferred("_run")


func _run() -> void:
	_check(not GameManager.may_suspend_world_rendering(false, true, true, false, 100000),
		"world build complete alone never stops rendering before EntryManager fade")
	_check(not GameManager.may_suspend_world_rendering(true, true, true, true, 100000),
		"map transition overlay always keeps rendering enabled")
	_check(not GameManager.may_suspend_world_rendering(true, true, true, false, 1199),
		"render stays enabled through initial first-world grace interval")
	_check(GameManager.may_suspend_world_rendering(true, true, true, false, 1200),
		"normal unfocused world regains low-cost rendering after presentation")
	_check(not GameManager.may_suspend_world_rendering(true, false, true, false, 1200),
		"no map cannot activate background world renderer")
	_check(not GameManager.may_suspend_world_rendering(true, true, false, false, 1200),
		"no player cannot activate background world renderer")
	_check(GameManager.fps_cap_for_focus(
		{"foreground_fps": 120, "background_fps": 20}, false, false) == 120,
		"unfocused loading overlay keeps the full configured foreground FPS")
	_check(GameManager.fps_cap_for_focus(
		{"foreground_fps": 120, "background_fps": 20}, false, true) == 20,
		"completed background world still saves GPU and CPU resources")

	_check(not GameManager.should_release_initial_spawn_gate(11999),
		"remaining queued monsters may initialize for 12 seconds")
	_check(GameManager.should_release_initial_spawn_gate(12000),
		"continuous spawn packets never block the loading overlay indefinitely")
	_check(GameManager.should_release_initial_spawn_gate(20000),
		"late spawn queue still releases presentation; remaining work is not cleared")

	# No world exists in this isolated test scene. A stray completion
	# callback must not mark an invalid world as ready.
	GameManager.notify_world_loading_overlay_hidden()
	_check(not bool(GameManager.get("_world_ready_for_background_saving")),
		"overlay completion ignored safely without a live map and player")
	_check(RenderingServer.render_loop_enabled,
		"renderer remains enabled when login/world UI is being prepared")

	print("[WORLD_RENDER_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	get_tree().quit(0 if _failures == 0 else 1)
