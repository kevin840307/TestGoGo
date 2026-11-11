extends SceneTree

# godot --headless --path client --script res://tests/viewport_aoi_radius_regression.gd
var failures: int = 0

func check(ok: bool, description: String) -> void:
	if ok:
		print("[VIEWPORT_AOI_PASS] " + description)
	else:
		failures += 1
		push_error("[VIEWPORT_AOI_FAIL] " + description)

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	await process_frame
	var standard: int = ViewportAoiRadius.estimate(
		Rect2(0, 0, 1400, 900), Transform2D.IDENTITY, Vector2(700, 450))
	var wide: int = ViewportAoiRadius.estimate(
		Rect2(0, 0, 3840, 2160), Transform2D.IDENTITY, Vector2(1920, 1080))
	check(standard == ViewportAoiRadius.MIN_RADIUS,
		"small world view preserves legacy 1800-radius player AOI")
	check(wide > 2600,
		"4K camera view requests beyond legacy 2600-radius monster AOI")
	var large_zoom: Transform2D = Transform2D.IDENTITY.scaled(Vector2(0.4, 0.4))
	var large_radius: int = ViewportAoiRadius.estimate(
		Rect2(0, 0, 3840, 2160), large_zoom, Vector2(4800, 2700))
	check(large_radius > 5000 and large_radius <= ViewportAoiRadius.MAX_RADIUS,
		"4K 0.4x zoom includes Canvas corners beyond 5000 world units")

	var zoom_out: Transform2D = Transform2D.IDENTITY.scaled(Vector2(0.5, 0.5))
	var zoom_radius: int = ViewportAoiRadius.estimate(
		Rect2(0, 0, 1920, 1080), zoom_out, Vector2(1920, 1080))
	check(zoom_radius > standard, "zoom-out expands world interest")
	var shifted: int = ViewportAoiRadius.estimate(
		Rect2(0, 0, 1920, 1080), Transform2D.IDENTITY, Vector2.ZERO)
	check(shifted > standard, "off-center player does not clip outer visible actors")
	check(ViewportAoiRadius.estimate(
		Rect2(0, 0, 0, 0), Transform2D.IDENTITY, Vector2.ZERO
	) == ViewportAoiRadius.MIN_RADIUS,
		"empty/uninitialized viewport returns safe legacy radius")
	check(ViewportAoiRadius.estimate(
		Rect2(0, 0, 30000, 30000), Transform2D.IDENTITY, Vector2.ZERO
	) == ViewportAoiRadius.MAX_RADIUS,
		"extreme zoom/view size cannot demand unbounded server bandwidth")
	print("[VIEWPORT_AOI_TEST] ", "ALL PASS" if failures == 0 else "%d FAILURES" % failures)
	quit(0 if failures == 0 else 1)
