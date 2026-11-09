extends SceneTree
# Minimal Godot 4.7.2 project fixture; no recovered assets or Server needed.
# godot --headless --path <minimal-fixture-dir> --script res://tests/map_early_detach_regression.gd

const STREAMER = preload("res://scripts/map_visual_aoi_streamer.gd")
var failures: int = 0

func check(ok: bool, label: String) -> void:
	if ok:
		print("[MAP_EARLY_DETACH_PASS] " + label)
	else:
		failures += 1
		push_error("[MAP_EARLY_DETACH_FAIL] " + label)


func create_test_texture(path: String) -> Texture2D:
	var texture := GradientTexture2D.new()
	texture.width = 8
	texture.height = 8
	texture.gradient = Gradient.new()
	var error: int = ResourceSaver.save(texture, path)
	check(error == OK, "save synthetic 8px texture " + path)
	return ResourceLoader.load(path, "", ResourceLoader.CACHE_MODE_IGNORE) as Texture2D


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var regular_path: String = "res://tests/aoitest_regular.tres"
	var festival_path: String = "res://tests/GRASS_03_aoitest.tres"
	var texture: Texture2D = create_test_texture(regular_path)
	var festival_texture: Texture2D = create_test_texture(festival_path)
	check(is_instance_valid(texture) and is_instance_valid(festival_texture),
		"test textures are resource-backed and loadable")
	if not is_instance_valid(texture) or not is_instance_valid(festival_texture):
		quit(1)
		return

	var map_root := Node2D.new()
	map_root.name = "Map15Synthetic"
	var near := Sprite2D.new()
	near.name = "NearStatic"
	near.position = Vector2(100, 0)
	near.texture = texture
	map_root.add_child(near)

	var far := Sprite2D.new()
	far.name = "FarStatic"
	far.position = Vector2(9000, 0)
	far.texture = texture
	map_root.add_child(far)

	var building := Node2D.new()
	building.name = "BuildingNode"
	building.add_to_group("Building")
	map_root.add_child(building)
	var door := Sprite2D.new()
	door.position = Vector2(9000, 0)
	door.texture = texture
	building.add_child(door)

	var dynamic_container := Node2D.new()
	dynamic_container.name = "Entities"
	map_root.add_child(dynamic_container)
	var actor := Sprite2D.new()
	actor.position = Vector2(9000, 0)
	actor.texture = texture
	dynamic_container.add_child(actor)

	var festival := Sprite2D.new()
	festival.name = "FestivalSprite"
	festival.position = Vector2(9000, 0)
	festival.texture = festival_texture
	map_root.add_child(festival)

	var profile: Dictionary = {
		"map_visual_aoi_enter_radius": 1200.0,
		"map_visual_aoi_exit_radius": 1800.0,
	}
	var streamer = STREAMER.new()
	var result: Dictionary = streamer.detach_far_before_attach(
		map_root, Vector2.ZERO, profile
	)
	check(int(result.get("indexed", 0)) == 2, "only static, nonswappable sprites selected")
	check(int(result.get("detached", 0)) == 1, "one distant static sprite detached before scene attach")
	check(is_instance_valid(near.texture), "nearby static texture remains ready")
	check(far.texture == null, "distant static texture reference is released")
	check(str(far.get_meta(&"map_visual_texture_path", "")) == regular_path,
		"distant sprite records its loadable path for restoration")
	check(is_instance_valid(door.texture), "building door texture remains loaded")
	check(is_instance_valid(actor.texture), "dynamic entity visual is never affected")
	check(is_instance_valid(festival.texture), "festival swappable texture remains loaded")

	root.add_child(map_root)
	var bind_stats: Dictionary = streamer.bind_map(map_root)
	check(int(bind_stats.get("indexed", 0)) == 2, "runtime AOI indexes both tracked static sprites")
	check(int(bind_stats.get("unloaded", 0)) == 1, "runtime AOI recognizes pre-detached sprite")
	var prime_stats: Dictionary = await streamer.prime_near(
		self, Vector2.ZERO, profile, 2
	)
	check(int(prime_stats.get("unloaded", 0)) >= 1, "priming keeps far visuals unloaded")

	# Simulate walking/teleporting into the region. This must reload an
	# ignored-cache texture, without dropping any gameplay or special sprite.
	for _i in range(160):
		streamer.update(Vector2(9000, 0), profile, 0.2)
		await process_frame
		if is_instance_valid(far.texture):
			break
	check(is_instance_valid(far.texture), "far static sprite reloads when player approaches")
	check(is_instance_valid(door.texture) and is_instance_valid(actor.texture),
		"building and dynamic sprites remain intact after streaming")
	streamer.clear()
	map_root.queue_free()
	await process_frame
	DirAccess.remove_absolute(ProjectSettings.globalize_path(regular_path))
	DirAccess.remove_absolute(ProjectSettings.globalize_path(festival_path))
	print("[MAP_EARLY_DETACH_REGRESSION] %s" % ("ALL PASS" if failures == 0 else "FAILURES=%d" % failures))
	quit(0 if failures == 0 else 1)
