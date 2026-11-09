extends SceneTree

const STREAMER = preload("res://scripts/map_visual_aoi_streamer.gd")
var failures: int = 0


func check(ok: bool, label: String) -> void:
	if ok:
		print("[MAP_VISUAL_AOI_PASS] " + label)
	else:
		failures += 1
		push_error("[MAP_VISUAL_AOI_FAIL] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var root := Node2D.new()
	root.name = "MapTest"
	get_root().add_child(root)

	var near := Sprite2D.new()
	near.position = Vector2(100, 0)
	near.texture = load("res://ui/BG_LOADING.png")
	root.add_child(near)

	var far := Sprite2D.new()
	far.position = Vector2(9000, 0)
	far.texture = near.texture
	root.add_child(far)

	var entity_container := Node2D.new()
	entity_container.name = "Entities"
	root.add_child(entity_container)
	var dynamic_sprite := Sprite2D.new()
	dynamic_sprite.position = Vector2(9000, 0)
	dynamic_sprite.texture = near.texture
	entity_container.add_child(dynamic_sprite)

	var streamer = STREAMER.new()
	var bind_stats: Dictionary = streamer.bind_map(root)
	check(int(bind_stats.get("indexed", 0)) == 2, "indexes only static map Sprite2D nodes")

	var profile := {
		"map_visual_aoi_enter_radius": 1200.0,
		"map_visual_aoi_exit_radius": 1800.0,
	}
	for _i in range(4):
		streamer.update(Vector2.ZERO, profile, 1.0)
		await process_frame

	check(is_instance_valid(near.texture), "near static sprite stays resident")
	check(far.texture == null, "far static sprite releases texture reference")
	check(is_instance_valid(dynamic_sprite.texture), "Entities presentation is excluded from map streaming")

	var stats: Dictionary = streamer.stats()
	check(int(stats.get("unloaded", 0)) >= 1, "streamer reports unloaded static visuals")

	streamer.clear()
	root.queue_free()
	await process_frame
	print("[MAP_VISUAL_AOI_REGRESSION] %s" % ("ALL PASS" if failures == 0 else "FAILURES=%d" % failures))
	quit(0 if failures == 0 else 1)
