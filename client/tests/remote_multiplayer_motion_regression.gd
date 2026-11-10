extends SceneTree

# Deterministic, Server-free movement regression:
# godot --headless --path client --script res://tests/remote_multiplayer_motion_regression.gd
# Covers original-client-style continuous movement between remote snapshots.
var failures: int = 0


func _check(ok: bool, label: String) -> void:
	if ok:
		print("[REMOTE_MOTION_PASS] ", label)
	else:
		failures += 1
		push_error("[REMOTE_MOTION_FAIL] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	await process_frame
	var motion := RemotePlayerMotion.new()
	motion.reset(Vector2.ZERO)
	motion.receive(Vector2(20.0, 0.0), 1000, 0.10)
	var first_step: Vector2 = motion.advance(1.0 / 60.0)
	_check(first_step.x > 0.0 and first_step.x < 20.0,
		"remote player advances between 100ms snapshots, never jumps immediately")

	# Server time advances by 100ms; WebSocket delivery takes 300ms.
	# Client must NOT infer a 3x slower player from the delayed receive.
	motion.receive(Vector2(40.0, 0.0), 1300, 0.10)
	_check(motion._velocity.x >= 150.0,
		"server SpeedTime wins when receive-time jitter is large")
	var late_step: Vector2 = motion.advance(1.0 / 60.0)
	_check(late_step.x > first_step.x and late_step.x < 80.0,
		"late packet continues smooth movement without teleporting")

	# Older messages without a valid SpeedTime still use packet arrival time.
	var legacy := RemotePlayerMotion.new()
	legacy.reset(Vector2.ZERO)
	legacy.receive(Vector2(20.0, 0.0), 1000, 1.0)
	legacy.receive(Vector2(40.0, 0.0), 1300, 1.0)
	_check(legacy._velocity.x > 50.0 and legacy._velocity.x < 100.0,
		"legacy missing SpeedTime uses sane observed packet gap")

	motion.stop_prediction()
	_check(motion._velocity == Vector2.ZERO,
		"server idle stops prediction immediately")
	var stopped_step: Vector2 = motion.advance(1.0 / 60.0)
	_check(stopped_step.is_finite(),
		"server idle correction remains finite and nonblocking")

	# The currently visible character remains presentation-only: game
	# collision, server coordinates, attack and loot handling are untouched.
	_check(motion.authoritative_position.is_equal_approx(Vector2(40.0, 0.0)),
		"render prediction never changes the confirmed server position")

	print("[REMOTE_MOTION_TEST] ", "ALL PASS" if failures == 0 else "%d FAILURES" % failures)
	quit(0 if failures == 0 else 1)
