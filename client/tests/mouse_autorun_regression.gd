extends SceneTree

# Offline runtime regression. Execute:
# godot --headless --path client --script res://tests/mouse_autorun_regression.gd
# Tests the 2s latch, pointer visual and right-click cancellation without
# needing a game server or changing mouse interactions with other players.
const AUTORUN = preload("res://scripts/mouse_autorun_state.gd")
var failures: int = 0


func _check(ok: bool, description: String) -> void:
	if ok:
		print("[MOUSE_AUTORUN_PASS] ", description)
	else:
		failures += 1
		push_error("[MOUSE_AUTORUN_FAIL] " + description)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	await process_frame
	_test_two_second_latch_and_terrain_cancel()
	_test_existing_cursor_precedence()
	_test_right_click_preserves_player_interaction()
	print("[MOUSE_AUTORUN] ", "ALL PASS" if failures == 0 else "%d FAILED" % failures)
	quit(0 if failures == 0 else 1)


func _test_two_second_latch_and_terrain_cancel() -> void:
	var state = AUTORUN.new()
	_check(AUTORUN.HOLD_TO_LATCH_MSEC == 2000,
		"terrain requires two full seconds, not 0.5s, to latch")
	_check(not state.terrain_press(1000), "initial terrain press enters normal hold")
	_check(not state.poll_long_hold(2999) and not state.latched,
		"ordinary dragging for under two seconds does not latch")
	_check(state.poll_long_hold(3000) and state.latched,
		"crossing exactly 2 seconds latches continuous movement")
	_check(not state.poll_long_hold(3010),
		"held frames do not re-trigger the latch transition")
	_check(state.terrain_release(3100) and state.latched and not state.holding,
		"releasing mouse leaves continuous movement enabled")
	_check(state.terrain_press(4000) and not state.latched and not state.holding,
		"next terrain click cancels latch, does not start a second hold")
	_check(not state.terrain_release(4100),
		"release after cancel stays in ordinary click-to-move mode")

	state.terrain_press(5000)
	_check(not state.terrain_release(5200) and not state.latched,
		"short click preserves normal one-shot walking behavior")
	state.terrain_press(6000)
	_check(state.terrain_release(8000) and state.latched,
		"two-second hold also latches if no physics poll ran at threshold")
	state.cancel()
	_check(not state.latched and not state.holding,
		"right-click, map transition and combat cancel both latch and hold")


func _test_existing_cursor_precedence() -> void:
	# CursorManager is a project autoload backed by the existing cursor scene.
	CursorManager.set_game_window_hovered(false)
	CursorManager.set_cursor_state("normal")
	CursorManager.set_autorun_indicator(true)
	_check(CursorManager.is_autorun_indicator_visible(),
		"latched movement has a distinct visible double-chevron cursor")
	CursorManager.set_cursor_state("attack")
	_check(not CursorManager.is_autorun_indicator_visible() and
		CursorManager.cursor_sprite.animation == "attack",
		"hovered monster keeps existing attack cursor")
	CursorManager.set_cursor_state("check")
	_check(not CursorManager.is_autorun_indicator_visible(),
		"hovered NPC keeps existing check cursor")
	CursorManager.set_cursor_state("skill")
	_check(not CursorManager.is_autorun_indicator_visible() and
		CursorManager.cursor_sprite.animation == "skill",
		"targeting skills keep original cursor priority")
	CursorManager.set_cursor_state("normal")
	CursorManager.set_game_window_hovered(true)
	_check(not CursorManager.is_autorun_indicator_visible(),
		"UI window hides gameplay autorun pointer")
	CursorManager.set_game_window_hovered(false)
	CursorManager.set_cursor_state("normal")
	_check(CursorManager.is_autorun_indicator_visible(),
		"cursor returns on terrain while latch is still active")
	CursorManager.set_autorun_indicator(false)
	_check(not CursorManager.is_autorun_indicator_visible(),
		"right-click or terrain tap returns to ordinary pointer")


func _test_right_click_preserves_player_interaction() -> void:
	var script = load("res://player.gd") as GDScript
	_check(script != null, "actual player script loads")
	if script == null:
		return
	var player = script.new()
	var right = InputEventMouseButton.new()
	right.button_index = MOUSE_BUTTON_RIGHT
	right.pressed = true

	player._mouse_autorun.terrain_press(0)
	player._mouse_autorun.poll_long_hold(2000)
	player.is_moving = true
	player.move_path = PackedVector2Array([Vector2(40.0, 40.0)])
	player._input(right)
	_check(not player._mouse_autorun.latched and not player.is_moving
		and player.move_path.is_empty(),
		"right-click stops latched movement and clears destination/path")

	player.is_moving = true
	player._input(right)
	_check(not player.is_moving,
		"right-click also stops an ordinary non-combat click-to-move path")

	# A large-detour A* can still be in flight after a rejected direct path.
	# Cancel its request ID as well as the visible movement so it cannot
	# resurrect an old target after right-click.
	player._navigation_fallback_running = true
	var old_request: int = player._navigation_request_id
	player._input(right)
	_check(not player._navigation_fallback_running and
		player._navigation_request_id > old_request,
		"right-click invalidates an in-flight asynchronous detour")

	# These handlers own player context menus. Our _input must not consume the
	# event before NetworkPlayer._input_event sees the same right click.
	var player_source: String = FileAccess.get_file_as_string("res://player.gd")
	var network_source: String = FileAccess.get_file_as_string("res://network_player.gd")
	var input_start: int = player_source.find("func _input(event: InputEvent) -> void:")
	var input_end: int = player_source.find("func _unhandled_input(", input_start)
	var input_body: String = player_source.substr(input_start, input_end - input_start)
	_check(input_start >= 0 and input_end > input_start
		and not input_body.contains("set_input_as_handled"),
		"global right-click stop never consumes original player context-menu event")
	_check(network_source.contains("show_player_context_menu"),
		"player right-click menu behavior remains present")
	player.free()
