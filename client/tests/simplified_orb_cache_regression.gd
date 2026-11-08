extends SceneTree

# godot --headless --path client --script res://tests/simplified_orb_cache_regression.gd
# Verifies lazy per-type texture reuse and the spiky procedural silhouette.
const ORB_CACHE = preload("res://scripts/simplified_orb_cache.gd")
var failures: int = 0


func check(ok: bool, label: String) -> void:
	if ok:
		print("[ORB_CACHE_PASS] " + label)
	else:
		failures += 1
		push_error("[ORB_CACHE_FAIL] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var monster_a: Texture2D = ORB_CACHE.monster_texture()
	var monster_b: Texture2D = ORB_CACHE.monster_texture()
	var player_a: Texture2D = ORB_CACHE.player_texture()
	var player_b: Texture2D = ORB_CACHE.player_texture()

	check(monster_a == monster_b, "every monster shares one cached GPU texture")
	check(player_a == player_b, "every player shares one cached GPU texture")
	check(monster_a != player_a, "monster and player silhouettes remain different")
	check(monster_a.get_size() == Vector2(96.0, 96.0)
		and player_a.get_size() == Vector2(96.0, 96.0),
		"both cached orb textures stay compact at 96x96")

	var monster_image: Image = monster_a.get_image()
	var player_image: Image = player_a.get_image()
	check(monster_image.get_pixel(48, 48).a > 0.95
		and player_image.get_pixel(48, 48).a > 0.95,
		"orb centers are opaque and clickable-looking")
	check(monster_image.get_pixel(0, 0).a < 0.01
		and player_image.get_pixel(0, 0).a < 0.01,
		"transparent corners do not render an opaque square")
	check(monster_image.get_pixel(85, 48).a >
		monster_image.get_pixel(76, 70).a + 0.5,
		"long radial tip is visibly sharper than the gap between spikes")

	print("[ORB_CACHE_REGRESSION] %s" % (
		"ALL PASS" if failures == 0 else "FAILURES=%d" % failures
	))
	quit(0 if failures == 0 else 1)
