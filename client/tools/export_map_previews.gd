extends SceneTree

const BOUNDS_PATH := "res://maps/map_bounds.json"
const MANIFEST_PATH := "res://maps/map_generated_manifest.json"
const DEFAULT_MAX_SIZE := 1600
const CELL_SIZE := 16

var _output_dir := ""
var _max_size := DEFAULT_MAX_SIZE
var _requested_ids: Array[int] = []


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_parse_args()
	if _output_dir.is_empty():
		_output_dir = ProjectSettings.globalize_path("res://../game-editor/.generated/map-previews")
	var mkdir_error := DirAccess.make_dir_recursive_absolute(_output_dir)
	if mkdir_error != OK:
		printerr("[MAP_PREVIEW] cannot create output dir: %s error=%d" % [_output_dir, mkdir_error])
		quit(2)
		return

	var bounds_by_id := _load_bounds()
	var map_ids := _requested_ids if not _requested_ids.is_empty() else _discover_map_ids()
	if map_ids.is_empty():
		printerr("[MAP_PREVIEW] no map scenes listed/found. Expected %s or generated MapXX.scn files." % MANIFEST_PATH)
		quit(3)
		return

	var exported := 0
	var failed := 0
	var missing_scene_count := 0
	for map_id in map_ids:
		var scene_path := "res://maps/Map%02d.scn" % map_id
		if not ResourceLoader.exists(scene_path):
			missing_scene_count += 1
			printerr("[MAP_PREVIEW] generated scene missing map=%d path=%s" % [map_id, scene_path])
			continue
		var ok := await _export_map(map_id, bounds_by_id)
		if ok:
			exported += 1
		else:
			failed += 1

	if exported == 0 and missing_scene_count > 0:
		printerr("[MAP_PREVIEW] no previews exported because generated MapXX.scn files are absent.")
		printerr("[MAP_PREVIEW] These scenes are listed by map_generated_manifest.json but ignored by Git; use a complete recovered/generated client workspace.")
		quit(5)
		return

	print("[MAP_PREVIEW] complete exported=%d failed=%d missing_scene=%d output=%s" % [exported, failed, missing_scene_count, _output_dir])
	quit(0 if failed == 0 else 4)


func _parse_args() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--output="):
			_output_dir = arg.trim_prefix("--output=")
		elif arg.begins_with("--max-size="):
			_max_size = clampi(int(arg.trim_prefix("--max-size=")), 256, 4096)
		elif arg.begins_with("--map="):
			for raw_id in arg.trim_prefix("--map=").split(",", false):
				var map_id := int(raw_id.strip_edges())
				if map_id > 0 and not _requested_ids.has(map_id):
					_requested_ids.append(map_id)
	_requested_ids.sort()


func _load_bounds() -> Dictionary:
	if not FileAccess.file_exists(BOUNDS_PATH):
		printerr("[MAP_PREVIEW] missing %s" % BOUNDS_PATH)
		return {}
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(BOUNDS_PATH))
	return parsed if typeof(parsed) == TYPE_DICTIONARY else {}


func _discover_map_ids() -> Array[int]:
	var ids: Array[int] = []

	# MapXX.scn files are generated/recovered assets and intentionally ignored by
	# Git. The checked-in manifest is therefore the authoritative list of scenes
	# that should exist in a complete local client workspace.
	if FileAccess.file_exists(MANIFEST_PATH):
		var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(MANIFEST_PATH))
		if typeof(parsed) == TYPE_DICTIONARY:
			var scenes: Variant = (parsed as Dictionary).get("scenes", [])
			if typeof(scenes) == TYPE_ARRAY:
				for raw_scene: Variant in scenes:
					var name := str(raw_scene).get_file()
					if not name.begins_with("Map") or not name.ends_with(".scn"):
						continue
					var id_text := name.trim_prefix("Map").trim_suffix(".scn")
					if id_text.is_valid_int():
						var map_id := int(id_text)
						if map_id > 0 and not ids.has(map_id):
							ids.append(map_id)

	if ids.is_empty():
		var dir := DirAccess.open("res://maps")
		if dir != null:
			dir.list_dir_begin()
			while true:
				var name := dir.get_next()
				if name.is_empty():
					break
				if dir.current_is_dir():
					continue
				if not name.begins_with("Map") or not name.ends_with(".scn"):
					continue
				var id_text := name.trim_prefix("Map").trim_suffix(".scn")
				if id_text.is_valid_int():
					var map_id := int(id_text)
					if map_id > 0:
						ids.append(map_id)
			dir.list_dir_end()

	ids.sort()
	return ids


func _aligned_bounds(map_id: int, bounds_by_id: Dictionary) -> Rect2:
	var raw: Variant = bounds_by_id.get(str(map_id), {})
	if typeof(raw) != TYPE_DICTIONARY:
		return Rect2()
	var data: Dictionary = raw
	for key in ["min_x", "min_y", "max_x", "max_y"]:
		if not data.has(key):
			return Rect2()
	var min_x := floorf(float(data["min_x"]) / CELL_SIZE) * CELL_SIZE
	var min_y := floorf(float(data["min_y"]) / CELL_SIZE) * CELL_SIZE
	var max_x := ceilf(float(data["max_x"]) / CELL_SIZE) * CELL_SIZE
	var max_y := ceilf(float(data["max_y"]) / CELL_SIZE) * CELL_SIZE
	if max_x <= min_x or max_y <= min_y:
		return Rect2()
	return Rect2(Vector2(min_x, min_y), Vector2(max_x - min_x, max_y - min_y))


func _export_map(map_id: int, bounds_by_id: Dictionary) -> bool:
	var path := "res://maps/Map%02d.scn" % map_id
	if not ResourceLoader.exists(path):
		printerr("[MAP_PREVIEW] missing scene map=%d path=%s" % [map_id, path])
		return false

	var bounds := _aligned_bounds(map_id, bounds_by_id)
	if not bounds.has_area():
		printerr("[MAP_PREVIEW] missing/invalid bounds map=%d" % map_id)
		return false

	var packed := ResourceLoader.load(path) as PackedScene
	if packed == null:
		printerr("[MAP_PREVIEW] load failed map=%d" % map_id)
		return false

	var map_node := packed.instantiate()
	if map_node == null:
		printerr("[MAP_PREVIEW] instantiate failed map=%d" % map_id)
		return false

	# Preview is visual-only. Entity/collision containers are not part of the
	# rendered map background and can trigger unnecessary runtime work.
	for child_name in ["Entities", "CollisionContainer", "CollisionLayer"]:
		var child := map_node.get_node_or_null(child_name)
		if is_instance_valid(child):
			map_node.remove_child(child)
			child.free()

	var longest := maxf(bounds.size.x, bounds.size.y)
	var scale := minf(1.0, float(_max_size) / maxf(1.0, longest))
	var output_size := Vector2i(
		maxi(64, int(round(bounds.size.x * scale))),
		maxi(64, int(round(bounds.size.y * scale)))
	)

	var viewport := SubViewport.new()
	viewport.name = "MapPreviewViewport"
	viewport.size = output_size
	viewport.disable_3d = true
	viewport.transparent_bg = false
	viewport.render_target_clear_mode = SubViewport.CLEAR_MODE_ALWAYS
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	get_root().add_child(viewport)

	viewport.add_child(map_node)
	var camera := Camera2D.new()
	camera.name = "MapPreviewCamera"
	viewport.add_child(camera)
	camera.enabled = true
	camera.position = bounds.get_center()
	camera.zoom = Vector2.ONE * scale

	# Give imported textures and CanvasItems time to become render-ready.
	await process_frame
	await process_frame
	RenderingServer.force_draw()
	await process_frame

	var image := viewport.get_texture().get_image()
	var output_path := _output_dir.path_join("Map%02d.png" % map_id)
	var save_error := image.save_png(output_path)
	viewport.queue_free()
	await process_frame

	if save_error != OK:
		printerr("[MAP_PREVIEW] save failed map=%d error=%d" % [map_id, save_error])
		return false
	print("[MAP_PREVIEW] map=%d size=%dx%d -> %s" % [map_id, output_size.x, output_size.y, output_path])
	return true
