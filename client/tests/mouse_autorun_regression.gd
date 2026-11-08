extends SceneTree

# Offline runtime regression. Execute:
# godot --headless --path client --script res://tests/mouse_autorun_regression.gd
# Tests the 1s latch, pointer visual and right-click cancellation without
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
	_test_one_second_latch_and_terrain_cancel()
	_test_existing_cursor_precedence()
	_test_right_click_preserves_player_interaction()
	print("[MOUSE_AUTORUN] ", "ALL PASS" if failures == 0 else "%d FAILED" % failures)
	quit(0 if failures == 0 else 1)


func _test_one_second_latch_and_terrain_cancel() -> void:
	var state = AUTORUN.new()
	_check(AUTORUN.HOLD_TO_LATCH_MSEC == 1000,
		"terrain requires one full second, not 0.5s, to latch")
	_check(not state.terrain_press(1000), "initial terrain press enters normal hold")
	_check(not state.poll_long_hold(1999) and not state.latched,
		"ordinary dragging for under one second does not latch")
	_check(state.poll_long_hold(2000) and state.latched,
		"crossing exactly 1 second latches continuous movement")
	_check(not state.poll_long_hold(2010),
		"held frames do not re-trigger the latch transition")
	_check(state.terrain_release(2100) and state.latched and not state.holding,
		"releasing mouse leaves continuous movement enabled")
	_check(state.terrain_press(3000) and not state.latched and not state.holding,
		"next terrain click cancels latch, does not start a second hold")
	_check(not state.terrain_release(3100),
		"release after cancel stays in ordinary click-to-move mode")

	state.terrain_press(4000)
	_check(not state.terrain_release(4200) and not state.latched,
		"short click preserves normal one-shot walking behavior")
	state.terrain_press(5000)
	_check(state.terrain_release(6000) and state.latched,
		"one-second hold also latches if no physics poll ran at threshold")
	state.cancel()
	_check(not state.latched and not state.holding,
		"right-click, map transition and combat cancel both latch and hold")


func _test_existing_cursor_precedence() -> void:
	# CursorManager is a project autoload backed by the existing cursor scene.
	CursorManager.set_game_window_hovered(false)
	CursorManager.set_cursor_state("normal")
	CursorManager.set_autorun_indicator(true)
	var animated_frames: SpriteFrames = CursorManager.cursor_sprite.sprite_frames
	_check(animated_frames.has_animation("autorun") and
		animated_frames.get_frame_count("autorun") == 10,
		"new cursor uses ten PNG animation frames")
	_check(CursorManager.cursor_sprite.animation == "autorun",
		"locked walk replaces ordinary mouse sprite instead of adding an overlay")
	_check(CursorManager.is_autorun_indicator_visible(),
		"latched movement has a distinct animated cursor")
	var status_badge: Label = CursorManager.get("_autorun_status_label") as Label
	_check(is_instance_valid(status_badge) and status_badge.visible and
		status_badge.text.contains("自動走路 ON") and
		status_badge.text.contains("右鍵停止") and
		status_badge.mouse_filter == Control.MOUSE_FILTER_IGNORE,
		"persistent readable lock-ON badge explains state and does not intercept clicks")
	_check(CursorManager.cursor_sprite.scale.x > 1.0,
		"only locked walking increases sprite visibility")
	_check(CursorManager.cursor_sprite.sprite_frames.get_frame_texture(
		"autorun", 0).get_width() == 32,
		"cursor atlas keeps its actual 320x32 PNG layout without scaling mismatch")
	CursorManager.set_cursor_state("attack")
	_check(not CursorManager.is_autorun_indicator_visible() and
		CursorManager.cursor_sprite.animation == "attack" and
		not status_badge.visible and
		is_equal_approx(CursorManager.cursor_sprite.scale.x, 1.0),
		"hovered monster keeps original dimensions and hides locked-walk badge")
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
	_check(not CursorManager.is_autorun_indicator_visible() and
		not status_badge.visible and
		is_equal_approx(CursorManager.cursor_sprite.scale.x, 1.0),
		"right-click or terrain tap immediately hides lock badge and restores regular sprite")


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
	player._mouse_autorun.poll_long_hold(1000)
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

	# A left click on a monster, another player or an NPC should stop
	# movement before the existing entity handler gets that same click.
	var left = InputEventMouseButton.new()
	left.button_index = MOUSE_BUTTON_LEFT
	left.pressed = true
	var fake_monster: Node = Node.new()
	CursorManager.current_hovered_entity = fake_monster
	player._mouse_autorun.terrain_press(1000)
	player._mouse_autorun.poll_long_hold(2000)
	player.is_moving = true
	player.move_path = PackedVector2Array([Vector2(60.0, 60.0)])
	player._input(left)
	_check(not player._mouse_autorun.latched and not player.is_moving
		and player.move_path.is_empty(),
		"left-clicking another entity stops continuous movement first")
	_check(CursorManager.current_hovered_entity == fake_monster,
		"auto-walk cancellation does not clear original clicked entity")
	CursorManager.current_hovered_entity = null
	fake_monster.free()

	# Bare terrain left clicks must not be intercepted by the global
	# mouse handler; Player._unhandled_input owns one-shot click navigation.
	player._mouse_autorun.terrain_press(4000)
	player._mouse_autorun.poll_long_hold(5000)
	player._input(left)
	_check(player._mouse_autorun.latched,
		"terrain left click remains available to regular click-to-move")
	player.cancel_autorun_tracking()

	# UI clicks must have the same priority without turning an ordinary
	# MOUSE_FILTER_PASS HUD background into a false interactable click.
	var ui = Control.new()
	ui.mouse_filter = Control.MOUSE_FILTER_STOP
	_check(player._autorun_click_has_priority(null, ui),
		"UI controls stop continuous movement before processing their click")
	ui.mouse_filter = Control.MOUSE_FILTER_PASS
	_check(not player._autorun_click_has_priority(null, ui),
		"full-screen PASS overlays do not hijack bare terrain clicks")
	ui.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_check(not player._autorun_click_has_priority(null, ui),
		"non-interactive UI cannot interrupt terrain walking")
	ui.free()
	var button = Button.new()
	button.mouse_filter = Control.MOUSE_FILTER_PASS
	_check(player._autorun_click_has_priority(null, button),
		"PASS buttons still preempt autorun while retaining button events")
	button.free()
	_check(not player._autorun_click_has_priority(null, null),
		"bare ground is still routed to one-shot click-to-move")

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
