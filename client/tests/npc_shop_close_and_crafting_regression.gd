extends SceneTree

# godot --headless --path client --script res://tests/npc_shop_close_and_crafting_regression.gd
# NPC store panel event wiring, palette allocation, and time-budget batching.
const SHOP_SCENE = preload("res://npc_shop_window.tscn")
const CRAFTING_SCRIPT = preload("res://scripts/crafting_window.gd")
var _failures: int = 0


func _check(ok: bool, label: String) -> void:
	if not ok:
		_failures += 1
		push_error("[SHOP_CRAFT_REGRESSION_FAIL] " + label)
	else:
		print("[SHOP_CRAFT_REGRESSION_PASS] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	# The left X has always worked. Both buttons must use the same close
	# handler so inventory-side close also cleans up tooltips.
	var left_shop: Control = SHOP_SCENE.instantiate() as Control
	root.add_child(left_shop)
	var left_close := left_shop.get_node("Background/BtnClose") as BaseButton
	left_close.pressed.emit()
	_check(left_shop.is_queued_for_deletion(), "NPC shop left X closes shop")
	await process_frame

	var right_shop: Control = SHOP_SCENE.instantiate() as Control
	root.add_child(right_shop)
	var right_close := right_shop.get_node("SharedInventoryPanel/BtnClose") as BaseButton
	right_close.pressed.emit()
	_check(right_shop.is_queued_for_deletion(), "NPC shop right inventory X closes entire shop")
	await process_frame

	var palette_shop: Control = SHOP_SCENE.instantiate() as Control
	root.add_child(palette_shop)
	var mat_a: Material = palette_shop._get_palette_mat(2, 1)
	var mat_a2: Material = palette_shop._get_palette_mat(2, 1)
	var mat_b: Material = palette_shop._get_palette_mat(3, 1)
	_check(mat_a == mat_a2, "same shop palette/effect reuses one material")
	_check(mat_a != mat_b, "different shop palette colors keep independent materials")
	palette_shop.queue_free()
	await process_frame

	_check(not CRAFTING_SCRIPT.should_yield_static_recipe_batch(24, 1000),
		"cheap 24 recipe rows no longer waste an entire frame")
	_check(CRAFTING_SCRIPT.should_yield_static_recipe_batch(64, 1000),
		"recipe build hard cap prevents oversized frame work")
	_check(CRAFTING_SCRIPT.should_yield_static_recipe_batch(5, 2500),
		"recipe time budget stops expensive batches early")
	_check(not CRAFTING_SCRIPT.should_yield_static_recipe_batch(5, 2499),
		"recipe build does not yield before its budget")

	print("[SHOP_CRAFT_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	quit(0 if _failures == 0 else 1)
