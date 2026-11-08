extends SceneTree

# Run after Godot has imported the project:
# godot --headless --path client --script res://tests/monster_chase_regression.gd
# This is a deterministic client interpolation test; no Server is required.

var failed: int = 0

func _check(ok: bool, label: String) -> void:
	if ok:
		print("[MONSTER_MOVE_TEST_PASS] ", label)
	else:
		failed += 1
		push_error("[MONSTER_MOVE_TEST_FAIL] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	await process_frame
	var monster: MonsterEntity = MonsterEntity.new()
	monster.global_position = Vector2.ZERO

	# Server sends a 35px waypoint (250ms), followed by an idle state before
	# the visual interpolation has reached that approved coordinate.
	monster.network_move_to(Vector2(35.0, 0.0), 0.25)
	_check(monster.current_state == "walk" and monster.network_move_active,
		"a MoveEntity starts walking")
	monster.apply_network_state("idle", "1")
	_check(monster.is_finishing_last_move_before_idle(),
		"early server idle does not cancel an authorized movement segment")
	_check(monster.current_state == "walk",
		"walk animation is not interrupted at an early idle")
	_check(monster.advance_network_runtime(0.10),
		"in-flight waypoint remains in the runtime update loop")
	_check(monster.global_position.x > 0.0 and monster.global_position.x < 35.0,
		"waypoint continues without teleporting")
	_check(not monster.advance_network_runtime(0.20),
		"waypoint arrival allows manager to stop updating")
	_check(is_equal_approx(monster.global_position.x, 35.0),
		"waypoint reaches exact server-approved position")
	_check(monster.current_state == "idle" and not monster.network_move_active,
		"server idle applies immediately after arrival")

	# At the normal 250ms server packet boundary, the preceding visual
	# segment should still be moving for a few milliseconds. This prevents
	# WALK -> IDLE -> WALK flicker when a new packet is slightly delayed.
	var boundary_monster: MonsterEntity = MonsterEntity.new()
	boundary_monster.network_move_to(Vector2(35.0, 0.0), 0.25)
	boundary_monster.advance_network_runtime(0.25)
	_check(boundary_monster.network_move_active
		and boundary_monster.global_position.x < 35.0
		and boundary_monster.current_state == "walk",
		"250ms server tick boundary keeps visual walk in progress")
	boundary_monster.network_move_to(Vector2(70.0, 0.0), 0.25)
	_check(boundary_monster.current_state == "walk" and boundary_monster.network_move_active,
		"next waypoint continues walk without re-entering idle")
	boundary_monster.free()

	# A newer MoveEntity must cancel the old deferred idle.
	monster.network_move_to(Vector2(70.0, 0.0), 0.25)
	monster.apply_network_state("idle", "1")
	_check(monster.is_finishing_last_move_before_idle(),
		"second waypoint can defer idle")
	monster.network_move_to(Vector2(105.0, 0.0), 0.25)
	_check(not monster.is_finishing_last_move_before_idle(),
		"a new movement packet cancels the obsolete deferred idle")

	# Combat remains authoritative over in-flight movement.
	monster.apply_network_state("idle", "1")
	monster.apply_network_state("attack", "1")
	_check(monster.current_state == "attack" and not monster.network_move_active,
		"attack stops movement immediately")
	_check(not monster.is_finishing_last_move_before_idle(),
		"attack cancels deferred idle")
	monster.apply_network_state("death", "1")
	_check(monster.current_state == "death" and not monster.network_move_active,
		"death continues to override movement")
	monster.free()

	# Explicit server idle without an unfinished waypoint must be immediate.
	var idle_monster: MonsterEntity = MonsterEntity.new()
	idle_monster.current_state = "walk"
	idle_monster.network_move_active = false
	idle_monster.apply_network_state("idle", "1")
	_check(idle_monster.current_state == "idle" and not idle_monster.is_finishing_last_move_before_idle(),
		"idle without in-flight movement is unchanged")
	idle_monster.free()

	# An offscreen monster that is already instantiated in AOI must still
	# interpolate normal movement. The previous dispatch would directly set
	# global_position, making monsters jump when they crossed the viewport.
	var offscreen_monster: MonsterEntity = MonsterEntity.new()
	offscreen_monster._is_on_screen = false
	offscreen_monster.global_position = Vector2(10.0, 0.0)
	offscreen_monster.network_move_to(Vector2(45.0, 0.0), 0.25)
	_check(is_equal_approx(offscreen_monster.global_position.x, 10.0),
		"offscreen regular movement does not immediately snap to destination")
	_check(offscreen_monster.network_move_active,
		"offscreen monster keeps its normal movement interpolation")
	offscreen_monster.advance_network_runtime(0.10)
	_check(offscreen_monster.global_position.x > 10.0
		and offscreen_monster.global_position.x < 45.0,
		"offscreen monster advances smoothly toward destination")
	offscreen_monster.network_move_to(Vector2(100.0, 0.0), 0.0)
	_check(is_equal_approx(offscreen_monster.global_position.x, 100.0),
		"explicit server zero-duration correction stays authoritative")
	offscreen_monster.apply_offscreen_network_move(Vector2(135.0, 0.0))
	_check(is_equal_approx(offscreen_monster.global_position.x, 100.0)
		and offscreen_monster.network_move_active,
		"legacy offscreen move helper also avoids direct position snapping")
	offscreen_monster.free()

	# Guard the actual network dispatcher as well, rather than only the
	# MonsterEntity movement helper above.
	var entity_manager_script: String = FileAccess.get_file_as_string(
		"res://scripts/network_entity_manager.gd"
	)
	_check(not entity_manager_script.contains("target_node.apply_offscreen_network_move("),
		"network dispatcher cannot reintroduce viewport-edge direct snapping")

	print("[MONSTER_MOVE_TEST] ", "ALL PASS" if failed == 0 else "%d FAILURES" % failed)
	quit(0 if failed == 0 else 1)
