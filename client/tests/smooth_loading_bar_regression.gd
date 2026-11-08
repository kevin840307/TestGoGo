extends SceneTree

# godot --headless --path client --script res://tests/smooth_loading_bar_regression.gd
var _failures: int = 0


func _check(ok: bool, description: String) -> void:
	if ok:
		print("[LOADING_BAR_PASS] " + description)
	else:
		_failures += 1
		push_error("[LOADING_BAR_FAIL] " + description)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var bar: SmoothLoadingBar = SmoothLoadingBar.new()
	root.add_child(bar)
	bar.reset_progress()
	bar.set_target_progress(80.0)
	bar._process(1.0 / 60.0)
	_check(bar.value > 0.0 and bar.value < 80.0,
		"stage jump eases instead of instantly changing")
	bar.set_target_progress(12.0)
	_check(is_equal_approx(bar.get_target_progress(), 80.0),
		"late stage update cannot move progress backwards")
	var old_value: float = bar.value
	bar._process(2.0)
	_check(bar.value > old_value and bar.value < 40.0,
		"long frame stalls cannot snap to target")
	bar.reset_progress()
	_check(is_zero_approx(bar.value) and is_zero_approx(bar.get_target_progress()),
		"new loading session clears old progress")
	bar.set_target_progress(35.0)
	bar.finish_progress()
	await create_timer(0.36).timeout
	_check(bar.value >= 99.9, "completed load reaches 100 during fade")
	bar.reset_progress()
	bar.set_target_progress(16.0)
	_check(is_zero_approx(bar.value) and is_equal_approx(bar.get_target_progress(), 16.0),
		"reset cancels old completion tween")
	bar.free()
	print("[LOADING_BAR_REGRESSION] %s" % ("ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures))
	quit(0 if _failures == 0 else 1)
