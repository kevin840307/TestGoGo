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
	var pos := 0.0
	for _i in 15:
		pos = move_toward(pos, 90.0, speed / 60.0)
	_check(pos > 80.0 and pos < 90.0,
		"fixed 60Hz tick advances ~90px segment continuously in 250ms")
	print("[MONSTER_WAYPOINT_TEST] ", "ALL PASS" if failed == 0 else "%d FAILURES" % failed)
	quit(0 if failed == 0 else 1)
