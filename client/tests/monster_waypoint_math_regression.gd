extends SceneTree

# Isolated Godot 4.7.2 regression; requires no character textures or autoloads.
# The MonsterEntity uses this same pure helper for every MoveEntity waypoint.
var failed: int = 0


func _check(ok: bool, description: String) -> void:
	if ok:
		print("[MONSTER_WAYPOINT_PASS] ", description)
	else:
		failed += 1
		push_error("[MONSTER_WAYPOINT_FAIL] " + description)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	await process_frame
	var speed := MonsterWaypointSpeed.calculate(90.0, 0.25)
	_check(speed > 300.0 and speed < 900.0,
		"90px/250ms waypoint can catch up; prior 205px/s limit is absent")
	_check(MonsterWaypointSpeed.calculate(90.0, 0.0) == 0.0,
		"server zero-time correction remains immediate, not interpolated")
	_check(MonsterWaypointSpeed.calculate(0.5, 0.25) == 0.0,
		"subpixel movement does not reanimate the monster")
	_check(MonsterWaypointSpeed.calculate(40.0, 0.35) > 100.0,
		"350ms jittered bot waypoint retains its own server duration")
	_check(MonsterWaypointSpeed.calculate(1000.0, 0.25) <= 900.0,
		"large temporary network backlog has a bounded catch-up speed")
	# Keep the full Server span for a coalesced multi-waypoint move,
	# without changing 250ms movement or authoritative combat snaps.
	var playback := MonsterWaypointSpeed.playback_duration(0.75, 2.0)
	_check(playback > 0.75 and playback <= 1.25,
		"coalesced world movement gets bounded full-span playback")
	_check(MonsterWaypointSpeed.calculate(200.0, playback) <
		MonsterWaypointSpeed.calculate(200.0, 0.75),
		"historical movement cannot cause a compressed 750ms sprint")
	_check(is_equal_approx(MonsterWaypointSpeed.playback_duration(0.25, 0.0), 0.25),
		"normal 250ms world movement is unchanged")
	_check(is_equal_approx(MonsterWaypointSpeed.playback_duration(0.0, 2.0), 0.0),
		"combat correction is still immediate")
	_check(MonsterWaypointSpeed.should_linger_outside_aoi(2240.0, 2200.0, 400)
		and not MonsterWaypointSpeed.should_linger_outside_aoi(2240.0, 2200.0, 1000)
		and not MonsterWaypointSpeed.should_linger_outside_aoi(3000.0, 2200.0, 0),
		"only viewport-fringe monsters may linger briefly")
	var full_hd_viewport := Rect2(Vector2.ZERO, Vector2(1920.0, 1080.0))
	_check(MonsterWaypointSpeed.is_near_viewport(
		full_hd_viewport, Vector2(1900.0, 1000.0)),
		"actual 1920x1080 viewport keeps edge monsters eligible for visual smoothing")
	_check(MonsterWaypointSpeed.is_near_viewport(
		full_hd_viewport, Vector2(1940.0, 1000.0)),
		"one-frame visibility notifier delay at camera edge does not force teleport")
	_check(not MonsterWaypointSpeed.is_near_viewport(
		full_hd_viewport, Vector2(2300.0, 1000.0)),
		"offscreen corrections beyond viewport margin are not visually delayed")
	var ultra_wide_viewport := Rect2(Vector2.ZERO, Vector2(3440.0, 1440.0))
	_check(MonsterWaypointSpeed.is_near_viewport(
		ultra_wide_viewport, Vector2(3300.0, 900.0)),
		"ultrawide camera coverage is independent of legacy 800x600 assumptions")
	# The view radius must follow the current Canvas2D transform, not
	# hardcoded 800x600 dimensions nor an approximation around camera origin.
	var full_hd_actual_radius: float = MonsterWaypointSpeed.required_view_radius(
		full_hd_viewport, Transform2D.IDENTITY, Vector2(960.0, 540.0), 0.0
	)
	_check(absf(full_hd_actual_radius - Vector2(960.0, 540.0).length()) < 0.1,
		"full HD visible corners match actual pixel dimensions")
	var ultra_wide_actual_radius: float = MonsterWaypointSpeed.required_view_radius(
		ultra_wide_viewport, Transform2D.IDENTITY, Vector2(1720.0, 720.0), 0.0
	)
	_check(ultra_wide_actual_radius > full_hd_actual_radius + 700.0,
		"ultrawide viewport includes distant on-screen actors")
	var zoomed_canvas := Transform2D(
		Vector2(0.5, 0.0), Vector2(0.0, 0.5), Vector2.ZERO
	)
	var zoomed_radius: float = MonsterWaypointSpeed.required_view_radius(
		full_hd_viewport, zoomed_canvas, Vector2(1920.0, 1080.0), 0.0
	)
	_check(absf(zoomed_radius - full_hd_actual_radius * 2.0) < 0.1,
		"zooming out requires twice the visible world-space radius")
	var offset_canvas := Transform2D(
		Vector2.RIGHT, Vector2.DOWN, Vector2(-360.0, 0.0)
	)
	var offset_radius: float = MonsterWaypointSpeed.required_view_radius(
		full_hd_viewport, offset_canvas, Vector2(960.0, 540.0), 0.0
	)
	_check(offset_radius > full_hd_actual_radius + 260.0,
		"camera offset from local observer cannot clip visible edge monsters")
	var invalid_radius: float = MonsterWaypointSpeed.required_view_radius(
		Rect2(Vector2.ZERO, Vector2.ZERO), Transform2D.IDENTITY,
		Vector2.ZERO, 360.0
	)
	_check(invalid_radius == 0.0, "invalid viewport safely uses fallback")

	var pos := 0.0
	for _i in 15:
		pos = move_toward(pos, 90.0, speed / 60.0)
	_check(pos > 80.0 and pos < 90.0,
		"fixed 60Hz tick advances ~90px segment continuously in 250ms")
	print("[MONSTER_WAYPOINT_TEST] ", "ALL PASS" if failed == 0 else "%d FAILURES" % failed)
	quit(0 if failed == 0 else 1)
