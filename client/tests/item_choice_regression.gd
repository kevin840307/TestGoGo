extends SceneTree

# Server-free UI regression for the authoritative choice-reward flow.
# Run:
# godot --headless --path client --script res://tests/item_choice_regression.gd

var _failures: int = 0


func _check(condition: bool, description: String) -> void:
	if condition:
		print("[ITEM_CHOICE_TEST_PASS] ", description)
	else:
		_failures += 1
		push_error("[ITEM_CHOICE_TEST_FAIL] " + description)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	await process_frame

	var scene := load("res://item_choice_window.tscn") as PackedScene
	_check(scene != null, "item choice scene loads")
	if scene == null:
		quit(1)
		return

	var window = scene.instantiate()
	get_root().add_child(window)
	await process_frame

	var options := [
		{"ItemIndex": 15114, "Name": "聖殿騎士鎧甲"},
		{"ItemIndex": 15113, "Name": "聖殿騎士鎧甲．復仇"},
		{"ItemIndex": 15112, "Name": "聖殿騎士鎧甲．懲戒"},
	]
	window.setup(123456, "聖殿騎士鎧甲裝備箱", options)

	var option_container: VBoxContainer = window.get_node("ScrollContainer/Options")
	_check(option_container.get_child_count() == 3,
		"server options render as three buttons")
	if option_container.get_child_count() == 3:
		_check(option_container.get_child(0) is Button,
			"first reward option is actionable")
		_check(option_container.get_child(1).text == "聖殿騎士鎧甲．復仇",
			"reward label is preserved")

	# Selecting while no socket is connected must still be safe and close the
	# dialog; NetworkManager itself owns the connection-state check.
	window._on_option_pressed(15113)
	await process_frame
	_check(not is_instance_valid(window),
		"selection closes the window without requiring a live server")

	print("[ITEM_CHOICE_TEST] ", "ALL PASS" if _failures == 0 else "%d FAILURES" % _failures)
	quit(0 if _failures == 0 else 1)
