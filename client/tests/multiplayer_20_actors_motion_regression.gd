extends SceneTree

# Deterministic, resource-free 20-player + 60-monster movement simulation.
# No textures, WebSocket, SQLite or scenes: isolates presentation pacing
# under 100ms / 350ms player snapshots and 250ms monster waypoints.
# Run with pinned Godot 4.7.2 in the client-multiplayer-motion workflow.
const PLAYER_COUNT: int = 20
const MONSTER_COUNT: int = 60
const PHYSICS_HZ: float = 60.0
const FRAMES: int = 720
var failures: int = 0


func _check(condition: bool, label: String) -> void:
	if condition:
		print("[MULTI20_MOTION_PASS] ", label)
	else:
		failures += 1
		push_error("[MULTI20_MOTION_FAIL] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	await process_frame
	var players: Array[RemotePlayerMotion] = []
	var player_cadence: Array[int] = []
	var monster_position: Array[float] = []
	var monster_target: Array[float] = []
	var monster_speed: Array[float] = []
	for i in PLAYER_COUNT:
		var motion := RemotePlayerMotion.new()
		motion.reset(Vector2(float(i * 200), 0.0))
		players.append(motion)
		player_cadence.append(6 if i % 2 == 0 else 21)

	for i in MONSTER_COUNT:
		monster_position.append(float(i * 25))
		monster_target.append(float(i * 25))
		monster_speed.append(0.0)

	var player_backward_steps: int = 0
	var player_jump_steps: int = 0
	var moving_player_ticks: int = 0
	var monster_paused_ticks: int = 0
	var moving_monster_ticks: int = 0
	var biggest_player_step: float = 0.0
	var sim_start_usec: int = Time.get_ticks_usec()

	for tick in FRAMES:
		var arrival_msec: int = 1000 + roundi(float(tick) * 1000.0 / PHYSICS_HZ)
		for i in PLAYER_COUNT:
			var motion: RemotePlayerMotion = players[i]
			var cadence: int = player_cadence[i]
			if tick % cadence == 0:
				var confirmed := Vector2(float(i * 200) + float(tick) * 3.0, 0.0)
				motion.receive(
					confirmed,
					arrival_msec,
					float(cadence) / PHYSICS_HZ,
					1200.0,
					0.0,
					200.0
				)
			var before_x: float = motion.visual_position.x
			var after_x: float = motion.advance(1.0 / PHYSICS_HZ).x
			var dx: float = after_x - before_x
			biggest_player_step = maxf(biggest_player_step, dx)
			if dx < -0.01:
				player_backward_steps += 1
			if dx > RemotePlayerMotion.MAX_VISUAL_CATCHUP_SPEED / PHYSICS_HZ + 0.01:
				player_jump_steps += 1
			if dx > 0.01:
				moving_player_ticks += 1

		if tick % 15 == 0:
			for i in MONSTER_COUNT:
				monster_target[i] += 40.0
				var remaining: float = monster_target[i] - monster_position[i]
				monster_speed[i] = MonsterWaypointSpeed.calculate(remaining, 0.25)

		for i in MONSTER_COUNT:
			var old_x: float = monster_position[i]
			monster_position[i] = move_toward(
				old_x, monster_target[i],
				monster_speed[i] / PHYSICS_HZ
			)
			var moved: float = monster_position[i] - old_x
			if moved > 0.01:
				moving_monster_ticks += 1
			elif tick > 15:
				monster_paused_ticks += 1

	var simulated_seconds: float = float(FRAMES) / PHYSICS_HZ
	var wall_ms: float = float(Time.get_ticks_usec() - sim_start_usec) / 1000.0
	_check(player_backward_steps == 0,
		"20 remote avatars never visually reverse between consecutive snapshots")
	_check(player_jump_steps == 0,
		"20 remote avatars obey a per-60Hz-step visual catch-up bound")
	_check(moving_player_ticks > PLAYER_COUNT * (FRAMES - 35) * 0.90,
		"100ms/350ms player cohorts keep walking between packets")
	_check(monster_paused_ticks == 0,
		"60 monsters keep moving every 60Hz tick between 250ms AI waypoints")
	_check(moving_monster_ticks == MONSTER_COUNT * FRAMES,
		"no monster resumes only when a new 250ms network packet arrives")
	_check(biggest_player_step <=
		RemotePlayerMotion.MAX_VISUAL_CATCHUP_SPEED / PHYSICS_HZ + 0.01,
		"maximum remote-player draw step remains bounded under 20-player load")
	_check(biggest_player_step <= 400.0 / PHYSICS_HZ + 0.05,
		"expected 200px/s players never use the old 900px/s visual catch-up burst")
	print("[MULTI20_MOTION_STATS] players=", PLAYER_COUNT,
		" monsters=", MONSTER_COUNT,
		" simulated_seconds=", simulated_seconds,
		" max_visual_step_px=", snappedf(biggest_player_step, 0.01),
		" cpu_wall_ms=", snappedf(wall_ms, 0.01))
	print("[MULTI20_MOTION_TEST] ", "ALL PASS" if failures == 0 else "%d FAILURES" % failures)
	quit(0 if failures == 0 else 1)
