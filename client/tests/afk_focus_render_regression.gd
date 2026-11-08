extends SceneTree

# Run: godot --headless --path client --script res://tests/afk_focus_render_regression.gd
# AFK background focus regression: no GPU frame submission, gameplay still ticks.
class PhysicsProbe:
	extends Node
	var ticks: int = 0

	func _physics_process(_delta: float) -> void:
		ticks += 1


var failures: int = 0


func check(ok: bool, label: String) -> void:
	if ok:
		print("[AFK_FOCUS_PASS] " + label)
	else:
		failures += 1
		push_error("[AFK_FOCUS_FAIL] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var old_focus: bool = GameManager._window_has_focus
	var old_afk: bool = GameManager._afk_active
	var old_fps: int = Engine.max_fps
	var old_render_loop: bool = RenderingServer.render_loop_enabled
	var old_physics_hz: int = Engine.physics_ticks_per_second
	var probe := PhysicsProbe.new()
	root.add_child(probe)

	check(GameManager.should_draw_afk_dashboard(true),
		"AFK dashboard may draw only when focused")
	check(not GameManager.should_draw_afk_dashboard(false),
		"AFK dashboard must never draw after losing focus")

	GameManager._afk_active = true
	GameManager._window_has_focus = false
	GameManager._refresh_focus_render_policy()
	check(not RenderingServer.render_loop_enabled,
		"AFK focus-out disables the entire GPU render loop")
	check(Engine.max_fps == old_fps,
		"AFK focus-out does not need to alter main-loop pacing")
	check(Engine.physics_ticks_per_second == old_physics_hz,
		"AFK focus-out leaves fixed simulation tick rate untouched")

	await create_timer(0.4).timeout
	check(probe.ticks >= 12,
		"physics processing continues while AFK GPU rendering is stopped")
	check(NetworkManager.is_physics_processing(),
		"network processing remains enabled while AFK is unfocused")

	GameManager._window_has_focus = true
	GameManager._refresh_focus_render_policy()
	check(RenderingServer.render_loop_enabled,
		"focused AFK enables only the low-cost dashboard render loop")
	check(Engine.max_fps == GameManager.AFK_MONITOR_FPS,
		"focused AFK dashboard is capped at 20 FPS")
	check(Engine.physics_ticks_per_second == old_physics_hz,
		"AFK refocus does not modify fixed physics")

	GameManager._window_has_focus = false
	GameManager._refresh_focus_render_policy()
	check(not RenderingServer.render_loop_enabled,
		"unfocused AFK immediately disables dashboard rendering")

	GameManager._afk_active = old_afk
	GameManager._window_has_focus = old_focus
	GameManager._refresh_focus_render_policy()
	Engine.max_fps = old_fps
	RenderingServer.render_loop_enabled = old_render_loop
	probe.queue_free()
	await process_frame
	print("[AFK_FOCUS_RENDER_REGRESSION] %s" % (
		"ALL PASS" if failures == 0 else "FAILURES=%d" % failures
	))
	quit(0 if failures == 0 else 1)
