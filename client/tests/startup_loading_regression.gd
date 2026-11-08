extends SceneTree

# godot --headless --path client --script res://tests/startup_loading_regression.gd
# Tests startup UI without entering the game, logging in, or invoking prewarm.
const ENTRY_SCENE = preload("res://entry_manager.tscn")
const CRAFTING_SCRIPT = preload("res://scripts/crafting_window.gd")
var _failures: int = 0


func _check(ok: bool, description: String) -> void:
	if ok:
		print("[STARTUP_LOADING_PASS] " + description)
	else:
		_failures += 1
		push_error("[STARTUP_LOADING_FAIL] " + description)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	# Do not add the entry scene to the tree. Verify its first-render scene
	# defaults without kicking off login, network or 1453-recipe prewarm.
	var entry: Control = ENTRY_SCENE.instantiate() as Control
	_check(entry != null, "entry scene loads successfully")
	if entry == null:
		quit(1)
		return
	var overlay: CanvasLayer = entry.get_node_or_null("LoadingOverlay") as CanvasLayer
	_check(overlay != null and overlay.visible,
		"loading overlay is visible from initial scene, before EntryManager._ready")
	if overlay:
		var bar: SmoothLoadingBar = overlay.get_node_or_null("LoadingBar") as SmoothLoadingBar
		_check(bar != null and bar.visible,
			"progress bar is present and visible before any prewarm begins")
		var label: Label = overlay.get_node_or_null("StatusLabel") as Label
		_check(label != null and not label.text.is_empty(),
			"startup loading explains the current preparation stage")

	_check(is_equal_approx(CRAFTING_SCRIPT.recipe_preload_progress(0, 1453), 0.0),
		"recipe progress starts at zero")
	_check(CRAFTING_SCRIPT.recipe_preload_progress(64, 1453) > 0.0
		and CRAFTING_SCRIPT.recipe_preload_progress(64, 1453) < 1.0,
		"first 64-item batch produces an intermediate progress fraction")
	_check(CRAFTING_SCRIPT.recipe_preload_progress(1452, 1453) < 1.0,
		"recipe processing does not report completion before the last item")
	_check(is_equal_approx(CRAFTING_SCRIPT.recipe_preload_progress(1453, 1453), 1.0),
		"only finishing all recipe rows reaches 100 percent")
	_check(is_equal_approx(CRAFTING_SCRIPT.recipe_preload_progress(0, 0), 0.0),
		"empty recipe data does not divide by zero")

	entry.free()
	print("[STARTUP_LOADING_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	quit(0 if _failures == 0 else 1)
