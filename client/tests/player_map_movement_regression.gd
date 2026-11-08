extends SceneTree

# Run after importing the Godot project:
# godot --headless --path client --script res://tests/player_map_movement_regression.gd
# Offline regression for the Map00 / old-map SyncPosition rollback bug.

var failures: int = 0

func _check(ok: bool, description: String) -> void:
	if ok:
		print("[PLAYER_MOVE_TEST_PASS] ", description)
	else:
		failures += 1
		push_error("[PLAYER_MOVE_TEST_FAIL] " + description)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	await process_frame
	var player_script: GDScript = load("res://player.gd") as GDScript
	_check(player_script != null, "player script is available")
	if player_script == null:
		quit(1)
		return

	var player = player_script.new()
	var old_pos: Vector2 = Vector2(1268.0, 317.0)
	var new_pos: Vector2 = Vector2(3104.0, 2918.0)
	player.reset_map_navigation_state(15, old_pos)
	_check(int(player.data_model.map_id) == 15, "original Map15 is registered")
	_check(player.has_last_walkable_sync_position
		and int(player._last_walkable_sync_map_id) == 15,
		"original sync anchor belongs to Map15")
	_check(player.last_walkable_sync_position.is_equal_approx(old_pos),
		"original Map15 anchor remembers the old coordinate")

	# The server sends ChangeMap to Map13. Invalidate the previous map's
	# anchor BEFORE the new map scene has finished loading.
	player._on_change_map_received(13, new_pos.x, new_pos.y)
	_check(not player.has_last_walkable_sync_position
		and player._last_walkable_sync_map_id == 0,
		"ChangeMap invalidates the previous map's sync anchor")
	_check(not player.is_moving and player.move_path.is_empty(),
		"ChangeMap clears previous navigation and input state")

	player.global_position = new_pos
	player.reset_map_navigation_state(13, new_pos)
	_check(int(player.data_model.map_id) == 13,
		"new Map13 is set for local player pathfinding")
	_check(player._get_navigation_map_id() == 13,
		"local pathfinding does not use Map00 after map transition")
	_check(player.has_last_walkable_sync_position
		and int(player._last_walkable_sync_map_id) == 13
		and player.last_walkable_sync_position.is_equal_approx(new_pos),
		"Map13 anchor uses the NEW authoritative position, not Map15")
	_check(player.move_destination.is_equal_approx(new_pos)
		and player.target_position.is_equal_approx(new_pos),
		"Map13 navigation targets start from the new spawn position")

	# A successful dismount ACK rebases to the server's coordinates,
	# regardless of any movement snapshot made while riding. This happens
	# without any map transition and is the reported regression scenario.
	player.global_position = old_pos
	player.last_walkable_sync_position = old_pos
	player.has_last_walkable_sync_position = true
	player._last_walkable_sync_map_id = 13
	player.is_moving = true
	player.is_mouse_holding = true
	player.move_path = PackedVector2Array([old_pos])
	player.apply_position_correction(new_pos)
	_check(player.global_position.is_equal_approx(new_pos),
		"dismount ACK correction uses authoritative Map13 position")
	_check(player.last_walkable_sync_position.is_equal_approx(new_pos)
		and player._last_walkable_sync_map_id == 13,
		"dismount resets stale movement anchor on the current map")
	_check(not player.is_moving and not player.is_mouse_holding
		and player.move_path.is_empty(),
		"dismount clears queued movement and held-mouse repathing")
	var network_source: String = FileAccess.get_file_as_string(
		"res://scripts/network_manager.gd"
	)
	_check(network_source.contains("[MECHA_DISMOUNT_REBASE]"),
		"dismount response handler includes authoritative rebase")

	# An invalid click must stop movement without synthesizing a network
	# position update. Keep this test independent of a live socket.
	var source: String = FileAccess.get_file_as_string("res://player.gd")
	_check(source.contains("func _stop_movement(send_final_position: bool = true)"),
		"stopping movement supports no-sync fallback")
	_check(source.contains("if send_position and send_final_position:"),
		"failed path cannot send a stale SyncPosition")
	_check(source.contains("_last_walkable_sync_map_id == map_id"),
		"fallback coordinate is restricted to the current map")
	_check(source.contains("_stop_movement(false)"),
		"invalid navigation calls no-sync stop")
	_check(source.contains("reset_map_navigation_state(map_id: int"),
		"map resets are an explicit player lifecycle step")

	# A moving player still sends position samples during skill animation.
	player.current_state = "buff"
	player.is_action_locked = true
	player.is_moving = true
	player.sync_timer = 0.0
	player._handle_network_sync(0.02)
	_check(is_equal_approx(player.sync_timer, 0.02),
		"buff animation does not suppress moving network position sync")
	player.is_moving = false
	player._handle_network_sync(0.02)
	_check(is_equal_approx(player.sync_timer, 0.02),
		"stationary buff does not produce additional position samples")

	# Reaching a waypoint during a locked skill must retain its animation.
	player.is_moving = true
	player._stop_movement(false)
	_check(player.current_state == "buff", "movement stop preserves locked skill")
	player.is_action_locked = false

	# Corner-steering probes shorter than the frame step must never
	# overshoot and reverse by several pixels on the next frame.
	var small_step: Vector2 = player._calculate_step_velocity(
		Vector2(2.0, 0.0), 200.0, 1.0 / 60.0
	)
	_check(small_step.length() / 60.0 <= 2.00001
		and small_step.x > 0.0, "corner probe step cannot overshoot")
	_check(player._calculate_step_velocity(Vector2(2.0, 0.0), 200.0, 0.0) == Vector2.ZERO,
		"zero delta does not cause invalid corner velocity")
	_check(is_equal_approx(
		player._calculate_step_velocity(Vector2(100.0, 0.0), 200.0, 1.0 / 60.0).length(),
		200.0), "long open-field movement keeps original movement speed")

	# Targeted skill must supersede the previous held-mouse route.
	var dummy_target: Node2D = Node2D.new()
	player.is_mouse_holding = true
	player.is_moving = true
	player.move_path = PackedVector2Array([new_pos + Vector2(20.0, 0.0)])
	var prior_navigation_request: int = player._navigation_request_id
	player.begin_targeted_skill(dummy_target, 26104)
	_check(not player.is_mouse_holding and not player.is_moving
		and player.move_path.is_empty(), "targeted skill clears old manual route")
	_check(player.combat_target == dummy_target and player.pending_skill_id == 26104,
		"targeted skill retains requested target and skill")
	_check(player._navigation_request_id > prior_navigation_request,
		"targeted skill invalidates stale async path")
	player.combat_target = null
	dummy_target.free()

	player.free()
	print("[PLAYER_MOVE_TEST] ", "ALL PASS" if failures == 0 else "%d FAILURES" % failures)
	quit(0 if failures == 0 else 1)
