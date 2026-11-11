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

	# A >1s websocket delay must not visually run backwards to stale
	# server coordinates. Prediction is capped, then holds until a genuine
	# server idle/correction/move packet arrives.
	var delayed := RemotePlayerMotion.new()
	delayed.reset(Vector2.ZERO)
	delayed.receive(Vector2(20.0, 0.0), 1000, 0.10)
	for i in 60:
		delayed.advance(1.0 / 60.0)
	var before_long_gap: float = delayed.visual_position.x
	for i in 120:
		delayed.advance(1.0 / 60.0)
	_check(delayed.visual_position.x >= before_long_gap - 0.5
		and delayed.visual_position.x <= 60.1,
		"missing 2s packets hold capped visual lead instead of rubber-banding")
	_check(delayed.authoritative_position.is_equal_approx(Vector2(20.0, 0.0)),
		"held visual offset never advances the authoritative gameplay position")
	delayed.stop_prediction()
	var idle_visual: Vector2 = delayed.advance(0.1)
	_check(idle_visual.x < before_long_gap + 0.1,
		"authoritative idle signal releases bounded prediction")
	# A large but non-teleport server correction after lost packets must not
	# appear as a one-frame jump. Only display position is speed-limited.
	var burst := RemotePlayerMotion.new()
	burst.reset(Vector2.ZERO)
	burst.receive(Vector2(20.0, 0.0), 1000, 0.10)
	for i in 120:
		burst.advance(1.0 / 60.0)
	burst.receive(Vector2(420.0, 0.0), 3000, 0.10, 1200.0)
	var before_burst: float = burst.visual_position.x
	var after_burst: float = burst.advance(1.0 / 60.0).x
	_check(after_burst >= before_burst
		and after_burst - before_burst <=
			RemotePlayerMotion.MAX_VISUAL_CATCHUP_SPEED / 60.0 + 0.01,
		"2s delayed movement cannot create a 100px visual jump in one physics tick")
	_check(burst.authoritative_position.x == 420.0,
		"catch-up cap affects only visuals; new authoritative coordinate applies immediately")


	# Stop/position correction must not invent a new movement velocity while
	# the following SyncState(idle) is still queued on the WebSocket.
	var standalone_correction := RemotePlayerMotion.new()
	standalone_correction.reset(Vector2.ZERO)
	standalone_correction.receive(Vector2(20.0, 0.0), 1000, 0.10)
	standalone_correction.advance(1.0 / 60.0)
	var before_correction: Vector2 = standalone_correction.visual_position
	standalone_correction.receive(Vector2(23.0, 0.0), 1100, 0.0)
	_check(standalone_correction._velocity == Vector2.ZERO and
		standalone_correction.authoritative_position == Vector2(23.0, 0.0),
		"zero-time position correction updates authority without inventing a sprint")
	_check(standalone_correction.visual_position == before_correction,
		"zero-time correction never teleports its presentation position")
	var corrected_frame: Vector2 = standalone_correction.advance(1.0 / 60.0)
	_check(corrected_frame.distance_to(Vector2(23.0, 0.0)) <=
		before_correction.distance_to(Vector2(23.0, 0.0)),
		"interpolation converges to stop endpoint even before idle arrives")

	# Stop after a position that fell inside Server's 100ms broadcast
	# throttle. The final MoveEntity (SpeedTime=0) and SyncState(idle)
	# must converge to the actual approved endpoint without a hard jump.
	var final_stop := RemotePlayerMotion.new()
	final_stop.reset(Vector2.ZERO)
	final_stop.receive(Vector2(20.0, 0.0), 1000, 0.10)
	for i in 25:
		final_stop.advance(1.0 / 60.0)
	final_stop.receive(Vector2(32.0, 0.0), 1100, 0.0)
	final_stop.stop_prediction()
	var stop_before: Vector2 = final_stop.visual_position
	var stop_first: Vector2 = final_stop.advance(1.0 / 60.0)
	_check(stop_first.distance_to(stop_before) <=
		RemotePlayerMotion.MAX_VISUAL_CATCHUP_SPEED / 60.0 + 0.01,
		"final stop correction cannot jump a remote avatar in one frame")
	for i in 90:
		final_stop.advance(1.0 / 60.0)
	_check(final_stop.authoritative_position.x == 32.0
		and final_stop.visual_position.distance_to(Vector2(32.0, 0.0)) < 0.5,
		"final stop position converges to last server-approved endpoint")

	delayed.receive(Vector2(4000.0, 4000.0), 4000, 0.1, 500.0)
	_check(delayed.visual_position.is_equal_approx(Vector2(4000.0, 4000.0)),
		"map/teleport discontinuity snaps immediately to the new server position")

	# 350ms bot snapshots must not run only 120ms then stand still.
	var sparse := RemotePlayerMotion.new()
	sparse.reset(Vector2.ZERO)
	sparse.receive(Vector2(70.0, 0.0), 1000, 0.35)
	var sparse_continuous: bool = true
	for i in 19:
		var previous_x: float = sparse.visual_position.x
		var next_x: float = sparse.advance(1.0 / 60.0).x
		if next_x <= previous_x + 0.001:
			sparse_continuous = false
	_check(sparse_continuous,
		"350ms snapshots remain visually moving throughout the packet interval")

	# Coalesced 2-second movement reports elapsed server time separately.
	var merged := RemotePlayerMotion.new()
	merged.reset(Vector2.ZERO)
	merged.receive(Vector2(400.0, 0.0), 3000, 0.75, 1200.0, 2.0)
	_check(absf(merged._velocity.x - 200.0) < 0.01,
		"coalesced movement uses full 2s server interval instead of 750ms")
	_check(merged._prediction_horizon_seconds <= 0.12 + 0.001,
		"historical coalesced movement cannot predict 2s into the future")


	# A large same-map movement can still be legitimate when Server says it
	# represents several seconds of coalesced walking. Untagged jumps keep
	# the historical teleport behavior.
	var outer_visible := RemotePlayerMotion.new()
	outer_visible.reset(Vector2.ZERO)
	outer_visible.receive(Vector2(20.0, 0.0), 1000, 0.10)
	outer_visible.advance(1.0 / 60.0)
	var outer_before: Vector2 = outer_visible.visual_position
	outer_visible.receive(Vector2(1800.0, 0.0), 4000, 0.75, 1200.0, 3.0)
	_check(outer_visible.visual_position.distance_to(outer_before) < 0.1,
		"tagged 1800px coalesced visible walk does not hard-reset the remote avatar")
	var outer_step := outer_visible.advance(1.0 / 60.0)
	_check(outer_step.x > outer_before.x and outer_step.x < 1800.0,
		"large coalesced visible movement begins bounded interpolation")
	var untagged_warp := RemotePlayerMotion.new()
	untagged_warp.reset(Vector2.ZERO)
	untagged_warp.receive(Vector2(1800.0, 0.0), 1000, 0.10)
	_check(untagged_warp.visual_position.x == 1800.0,
		"untagged 1800px discontinuity remains an immediate authoritative warp")

	# Runtime NetworkPlayer supplies the entity's expected movement speed.
	# Backlog recovery is allowed, but capped to 2x normal movement speed.
	var expected_speed_motion := RemotePlayerMotion.new()
	expected_speed_motion.reset(Vector2.ZERO)
	expected_speed_motion.receive(
		Vector2(400.0, 0.0), 3000, 0.75, 1200.0, 2.0, 200.0
	)
	var expected_step := expected_speed_motion.advance(1.0 / 60.0).x
	_check(expected_step <= 400.0 / 60.0 + 0.05,
		"200px/s remote player backlog catch-up is capped near 400px/s")

	print("[REMOTE_MOTION_TEST] ", "ALL PASS" if failures == 0 else "%d FAILURES" % failures)
	quit(0 if failures == 0 else 1)
