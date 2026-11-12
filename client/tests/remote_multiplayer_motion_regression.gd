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

	# Real Map13 clients observe 48-49px snapshots every 630-750ms.
	# Those must advance at near-uniform 60Hz speed rather than sprint
	# toward each snapshot then stand still halfway through the interval.
	var sparse750 := RemotePlayerMotion.new()
	sparse750.reset(Vector2.ZERO)
	sparse750.receive(Vector2(49.0, 0.0), 1000, 0.75, 1200.0, 0.0, 70.0)
	var first250: float = 0.0
	var last250: float = 0.0
	var min_step: float = INF
	var max_step: float = 0.0
	for frame in 45:
		var old_x: float = sparse750.visual_position.x
		var next_x: float = sparse750.advance(1.0 / 60.0).x
		var step: float = next_x - old_x
		min_step = minf(min_step, step)
		max_step = maxf(max_step, step)
		if frame < 15:
			first250 += step
		if frame >= 30:
			last250 += step
	_check(last250 > 11.0 and first250 > 11.0,
		"750ms remote movement remains active at the end of the packet interval")
	_check(max_step < min_step * 1.7,
		"750ms remote movement stays near steady speed instead of sprint-stop")
	var at750: float = sparse750.visual_position.x
	sparse750.receive(Vector2(98.0, 0.0), 1750, 0.75, 1200.0, 0.0, 70.0)
	var next_step750: float = sparse750.advance(1.0 / 60.0).x - at750
	_check(next_step750 > 0.5 and next_step750 < 2.5,
		"next delayed player packet continues smoothly without a speed spike")

	# Real receive cadence can vary even when the Server span is stable.
	# Replaying each historical segment from its beginning made these
	# 500-817ms gaps visibly alternate fast/slow.
	var jittered := RemotePlayerMotion.new()
	jittered.reset(Vector2.ZERO)
	var jitter_packets := [
		{"arrival": 1000, "x": 49.0},
		{"arrival": 1500, "x": 98.0},
		{"arrival": 2317, "x": 147.0},
		{"arrival": 2917, "x": 196.0},
	]
	var jitter_min_step: float = INF
	var jitter_max_step: float = 0.0
	var previous_arrival: int = 1000
	jittered.receive(Vector2(49.0, 0.0), 1000, 0.75, 1200.0, 0.0, 70.0)
	for packet_index in range(1, jitter_packets.size()):
		var packet: Dictionary = jitter_packets[packet_index]
		var arrival: int = int(packet["arrival"])
		var gap_seconds: float = float(arrival - previous_arrival) / 1000.0
		var frame_count: int = maxi(1, roundi(gap_seconds * 60.0))
		for frame in frame_count:
			var before_x: float = jittered.visual_position.x
			var after_x: float = jittered.advance(1.0 / 60.0).x
			var step_x: float = after_x - before_x
			if step_x > 0.001:
				jitter_min_step = minf(jitter_min_step, step_x)
				jitter_max_step = maxf(jitter_max_step, step_x)
		jittered.receive(
			Vector2(float(packet["x"]), 0.0),
			arrival, 0.75, 1200.0, 0.0, 70.0
		)
		previous_arrival = arrival
	_check(jitter_min_step > 0.5,
		"500-817ms receive jitter never creates a visible standstill")
	_check(jitter_max_step < jitter_min_step * 1.30,
		"receive jitter changes correction gradually instead of fast-slow pulses")

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

	# A hit is a one-shot animation even while new walk/idle packets arrive.
	_check(RemotePlayerStatePolicy.preserve_hit_animation(
		"hit_weapon_melee", true, "walk") and
		RemotePlayerStatePolicy.preserve_hit_animation(
			"hit_weapon_gun", true, "idle"),
		"server walk/idle cannot interrupt an active remote hit")
	_check(RemotePlayerStatePolicy.preserve_hit_animation(
		"attack_aa", true, "walk") and
		RemotePlayerStatePolicy.preserve_hit_animation(
			"attack_ga", true, "idle"),
		"late walk/idle cannot cut a remote attack animation in half")
	_check(not RemotePlayerStatePolicy.movement_may_replace_animation(
		"hit_weapon_melee", true) and
		not RemotePlayerStatePolicy.movement_may_replace_animation(
			"hit_weapon_gun", true),
		"60Hz remote movement does not replace an active hit animation")
	_check(not RemotePlayerStatePolicy.movement_direction_may_replace_animation(
		"hit_weapon_melee", true) and
		not RemotePlayerStatePolicy.movement_direction_may_replace_animation(
			"attack_aa", true) and
		RemotePlayerStatePolicy.movement_direction_may_replace_animation(
			"walk", false),
		"move snapshot heading cannot flip a locked hit/attack one-shot")
	_check(not RemotePlayerStatePolicy.preserve_hit_animation(
		"hit_weapon_melee", true, "death") and
		RemotePlayerStatePolicy.movement_may_replace_animation("idle", false),
		"death wins over hit and unlocked avatars can resume walk")
	_check(RemotePlayerStatePolicy.network_state_is_one_shot(
		"attack_aa") and
		RemotePlayerStatePolicy.network_state_is_one_shot(
			"hit_weapon_melee") and
		not RemotePlayerStatePolicy.network_state_is_one_shot("walk"),
		"explicit network attack/hit states use the one-shot lock contract")
	_check(RemotePlayerStatePolicy.damage_should_start_attack_visual(
		0, false),
		"ordinary remote normal damage starts the fallback attack visual")
	_check(not RemotePlayerStatePolicy.damage_should_start_attack_visual(
		0, true),
		"explicit network attack owns exactly the following normal damage visual")
	_check(not RemotePlayerStatePolicy.damage_should_start_attack_visual(
		12101, false),
		"SkillCast-owned animation is not replayed as a normal attack on damage")
	_check(RemotePlayerStatePolicy.damage_should_start_attack_visual(
		0, false),
		"a later real normal hit may restart a prior locally-owned attack swing")

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

	# The recovered Core.dll defaults to 10Hz Server Update and the old
	# Client to 50Hz. Display interpolation must remain comparable at 50
	# and 60 fixed updates; neither may turn a 100ms waypoint into a snap.
	var cadence_endpoints: Array[float] = []
	for hz in [50, 60]:
		var cadence := RemotePlayerMotion.new()
		cadence.reset(Vector2.ZERO)
		var frames_per_server_tick: int = hz / 10
		var most_pixels_one_step: float = 0.0
		var backward_cadence_frames: int = 0
		for frame in hz * 3:
			if frame % frames_per_server_tick == 0:
				var seconds: float = float(frame) / float(hz)
				cadence.receive(
					Vector2(seconds * 200.0, 0.0),
					1000 + roundi(seconds * 1000.0),
					0.10, 1200.0, 0.0, 200.0
				)
			var x_before: float = cadence.visual_position.x
			var x_after: float = cadence.advance(1.0 / float(hz)).x
			most_pixels_one_step = maxf(most_pixels_one_step, absf(x_after - x_before))
			if x_after + 0.01 < x_before:
				backward_cadence_frames += 1
		_check(backward_cadence_frames == 0,
			"10Hz server movement never runs backwards at %dHz visual pacing" % hz)
		_check(most_pixels_one_step <= 400.0 / float(hz) + 0.05,
			"10Hz snapshots stay bounded at %dHz local physics pacing" % hz)
		cadence_endpoints.append(cadence.visual_position.x)
	_check(absf(cadence_endpoints[0] - cadence_endpoints[1]) < 8.0,
		"legacy 50Hz and new 60Hz continuous motion agree for 10Hz server waypoints")

	# A newly spawned peer starts exactly at SpawnEntity's authoritative
	# location; the first subsequent move must take several physics frames.
	var entering_peer := RemotePlayerMotion.new()
	entering_peer.reset(Vector2(6000.0, 4800.0))
	entering_peer.receive(Vector2(6020.0, 4800.0), 1000, 0.10)
	var initial_visible := entering_peer.advance(1.0 / 60.0)
	_check(initial_visible.x > 6000.0 and initial_visible.x < 6020.0,
		"fresh peer first movement never visually teleports across its first waypoint")

	# Remote attack/hurt poses must survive delayed walk/idle and direction
	# packets; only a complete one-shot or bounded fallback may release them.
	_check(RemotePlayerStatePolicy.network_state_is_one_shot("attack_aa") and
		RemotePlayerStatePolicy.network_state_is_one_shot("hit_weapon_melee"),
		"remote normal attacks and hit reactions are one-shot actions")
	_check(RemotePlayerStatePolicy.preserve_hit_animation("hit_weapon_melee", true, "walk") and
		RemotePlayerStatePolicy.preserve_hit_animation("attack_aa", true, "idle"),
		"late motion states cannot interrupt an in-flight remote attack or hurt pose")
	_check(not RemotePlayerStatePolicy.movement_direction_may_replace_animation(
		"hit_weapon_melee", true) and not RemotePlayerStatePolicy.movement_may_replace_animation(
		"attack_aa", true),
		"late movement direction cannot flip an active combat action")
	_check(RemotePlayerStatePolicy.damage_should_start_attack_visual(0, false) and
		not RemotePlayerStatePolicy.damage_should_start_attack_visual(0, true) and
		not RemotePlayerStatePolicy.damage_should_start_attack_visual(20101, false),
		"damage starts only an unowned normal swing, never duplicates explicit or skill attack")

	print("[REMOTE_MOTION_TEST] ", "ALL PASS" if failures == 0 else "%d FAILURES" % failures)
	quit(0 if failures == 0 else 1)
