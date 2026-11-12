extends SceneTree

# Run: godot --headless --path client --script res://tests/remote_player_motion_regression.gd
# No Server or test account required: visual state never affects gameplay.
const MOTION = preload("res://scripts/remote_player_motion.gd")
var failures: int = 0


func check(ok: bool, label: String) -> void:
	if ok:
		print("[REMOTE_MOTION_PASS] " + label)
	else:
		failures += 1
		push_error("[REMOTE_MOTION_FAIL] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var motion = MOTION.new()
	motion.reset(Vector2.ZERO)
	motion.receive(Vector2(16, 0), 0, 0.35)
	check(motion.visual_position == Vector2.ZERO,
		"first small snapshot does not teleport the remote character")
	var previous: float = motion.visual_position.x
	var max_step: float = 0.0
	for frame in range(21):
		var current: Vector2 = motion.advance(1.0 / 60.0)
		var delta_x: float = current.x - previous
		max_step = maxf(max_step, absf(delta_x))
		check(delta_x >= -0.01,
			"snapshot interpolation remains monotonic on straight movement frame %d" % frame)
		previous = current.x
	check(previous > 15.0 and previous < 42.0,
		"350ms movement interval creates continuous bounded visual progress")
	check(max_step < 8.0, "no 16px one-frame jump")
	check(motion.visual_position.x <= 16.01,
		"first post-spawn move never extrapolates beyond its first endpoint")

	# A new position packet must NOT restart / snap an on-screen tween.
	var at_packet: float = motion.visual_position.x
	motion.receive(Vector2(32, 0), 350, 0.35)
	check(absf(motion.visual_position.x - at_packet) < 0.01,
		"new authoritative update preserves the current rendered position")
	var first_after: float = motion.advance(1.0 / 60.0).x
	check(first_after >= at_packet and first_after - at_packet < 8.0,
		"next physics tick continues smoothly without stepping a tile")

	for frame in range(21):
		motion.advance(1.0 / 60.0)
	motion.receive(Vector2(32, 0), 700, 0.35)
	motion.stop_prediction()
	for frame in range(90):
		motion.advance(1.0 / 60.0)
	check(motion.visual_position.distance_to(Vector2(32, 0)) < 0.5,
		"authoritative idle settles at exact server position")

	# Large teleports remain immediate; a predictor may never interpolate
	# across a portal or across thousands of blocked map pixels.
	motion.receive(Vector2(3200, 4500), 2200, 0.1)
	check(motion.visual_position == Vector2(3200, 4500),
		"real portal teleports are instant")

	# Consecutive packets delivered after a congested receive drain should
	# not create unbounded velocity or jump through the next position.
	motion.receive(Vector2(3216, 4500), 2400, 0.35)
	motion.advance(1.0 / 60.0)
	var burst_before: Vector2 = motion.visual_position
	motion.receive(Vector2(3232, 4500), 2401, 0.35)
	check(motion.visual_position == burst_before,
		"back-to-back packet burst does not snap the remote character")
	check(motion.advance(1.0 / 60.0).distance_to(burst_before) < 8.0,
		"back-to-back packets use bounded interpolation speed")

	print("[REMOTE_MOTION_REGRESSION] %s" % ("ALL PASS" if failures == 0 else "FAILURES=%d" % failures))
	quit(0 if failures == 0 else 1)
