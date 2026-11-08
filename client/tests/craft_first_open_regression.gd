extends SceneTree

# godot --headless --path client --script res://tests/craft_first_open_regression.gd
# Server-free regression: crafting must remain lazy and show loading progress.
const CRAFT_SCENE = preload("res://craft_dismantle_window.tscn")
var _failures: int = 0


func _check(ok: bool, label: String) -> void:
	if ok:
		print("[CRAFT_FIRST_OPEN_PASS] " + label)
	else:
		_failures += 1
		push_error("[CRAFT_FIRST_OPEN_FAIL] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	# Never start the full 1453-recipe build as part of a smoke test.
	var craft: Control = CRAFT_SCENE.instantiate() as Control
	_check(craft != null, "craft/dismantle scene can be instantiated")
	if craft == null:
		quit(1)
		return
	root.add_child(craft)
	await process_frame
	_check(not bool(craft.is_initial_prepared()),
		"crafting tree is NOT pre-built merely because its scene exists")
	_check(not bool(craft.is_initial_preparing()),
		"crafting window starts with no recipe work in progress")
	craft.call("update_ui")
	_check(not bool(craft.is_initial_prepared()),
		"inventory refresh cannot force heavy tree construction before first opening")

	craft.call("_ensure_initial_loading_overlay")
	var content: Control = craft.get_node("Background/ContentContainer") as Control
	var overlay: ColorRect = content.get_node_or_null("CraftFirstOpenLoading") as ColorRect
	_check(overlay != null, "crafting window owns an in-window loading overlay")
	_check(overlay != null and overlay.mouse_filter == Control.MOUSE_FILTER_STOP,
		"loading overlay shields incomplete crafting controls")
	var close_button: BaseButton = craft.get_node("Background/BtnClose") as BaseButton
	_check(close_button != null and overlay.get_parent() == content,
		"progress is inside content so close button is not blocked")
	var progress: ProgressBar = craft.get("_initial_loading_progress") as ProgressBar
	var stage: Label = craft.get("_initial_loading_label") as Label
	craft.call("_set_initial_loading_progress", 0.5)
	_check(progress != null and is_equal_approx(progress.value, 50.0),
		"first-open progress uses actual completed recipe fraction")
	_check(stage != null and stage.text.contains("50%"),
		"progress text shows percentage while building recipes")
	craft.call("_set_initial_loading_progress", 0.95)
	_check(stage.text.contains("更新可合成數量"),
		"final 10% marks player-specific crafting availability refresh")
	craft.call("_set_initial_loading_progress", 1.5)
	_check(progress != null and is_equal_approx(progress.value, 100.0),
		"progress remains clamped at 100%")

	# Protect both former eager paths against future regressions.
	# They should neither preload the craft scene during login nor build the
	# recipe Tree during GameHUD's before-reveal phase.
	var startup_source: String = FileAccess.get_file_as_string("res://entry_manager.gd")
	var hud_source: String = FileAccess.get_file_as_string("res://game_hud.gd")
	_check(not startup_source.contains('"craft": "res://craft_dismantle_window.tscn"'),
		"login startup no longer instantiates the crafting UI")
	var pre_reveal: String = hud_source.get_slice("func prepare_lazy_windows_before_reveal", 1)
	pre_reveal = pre_reveal.get_slice("func _prewarm_lazy_window_scenes", 0)
	_check(not pre_reveal.contains('"' + "craft" + '"'),
		"world-load pre-reveal stage no longer initializes crafting")
	_check(hud_source.contains("prepare_initial_with_progress_async"),
		"first-open HUD path invokes progress-aware initialization")

	craft.queue_free()
	await process_frame
	print("[CRAFT_FIRST_OPEN_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	quit(0 if _failures == 0 else 1)
