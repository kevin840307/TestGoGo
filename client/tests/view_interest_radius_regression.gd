extends SceneTree

var failed: int = 0


func _check(ok: bool, description: String) -> void:
	if ok:
		print("[VIEW_INTEREST_PASS] ", description)
	else:
		failed += 1
		push_error("[VIEW_INTEREST_FAIL] " + description)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	await process_frame
	_check(
		ViewInterestRadius.calculate(Vector2(800, 600)) == 1800.0,
		"legacy 800x600 keeps the existing minimum AOI"
	)
	_check(
		ViewInterestRadius.calculate(Vector2(1920, 1080)) == 1800.0,
		"1080p does not pay extra server AOI traffic"
	)
	var ultrawide := ViewInterestRadius.calculate(Vector2(3440, 1440))
	_check(
		ultrawide > 2300.0 and ultrawide < 2400.0,
		"3440x1440 expands interest beyond the old 1800 radius"
	)
	var four_k := ViewInterestRadius.calculate(Vector2(3840, 2160))
	_check(
		four_k > 2600.0 and four_k < 2750.0,
		"4K corners stay inside server delivery interest"
	)
	_check(
		ViewInterestRadius.calculate(Vector2(3440, 1440), 0.75) > ultrawide,
		"zoomed-out camera requests additional bounded headroom"
	)
	_check(
		ViewInterestRadius.calculate(Vector2(12000, 8000), 0.25) == 4200.0,
		"malicious or extreme viewport request remains bounded"
	)
	var ultrawide_rect := Rect2(Vector2.ZERO, Vector2(3440, 1440))
	var transformed_ultrawide := ViewInterestRadius.calculate_from_canvas(
		ultrawide_rect,
		Transform2D.IDENTITY,
		Vector2(1720, 720)
	)
	_check(
		absf(transformed_ultrawide - ultrawide) < 0.1,
		"Canvas2D corner calculation matches centered ultrawide geometry"
	)
	var half_scale := Transform2D(
		Vector2(0.5, 0.0),
		Vector2(0.0, 0.5),
		Vector2.ZERO
	)
	_check(
		ViewInterestRadius.calculate_from_canvas(
			Rect2(Vector2.ZERO, Vector2(1920, 1080)),
			half_scale,
			Vector2(1920, 1080)
		) > four_k,
		"actual Canvas2D zoom-out expands server interest beyond 4K default"
	)
	var offset_canvas := Transform2D(
		Vector2.RIGHT,
		Vector2.DOWN,
		Vector2(-500, 0)
	)
	_check(
		ViewInterestRadius.calculate_from_canvas(
			Rect2(Vector2.ZERO, Vector2(1920, 1080)),
			offset_canvas,
			Vector2(960, 540)
		) > 1800.0,
		"camera offset is included in server interest radius"
	)
	print("[VIEW_INTEREST_TEST] ", "ALL PASS" if failed == 0 else "%d FAILURES" % failed)
	quit(0 if failed == 0 else 1)
