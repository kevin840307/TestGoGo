extends SceneTree

const META_TEXTURE_PATH: StringName = &"map_visual_texture_path"
const MANIFEST_PATH := "res://maps/map_generated_manifest.json"
const OUTPUT_DIR := "res://maps/streamed"
const FESTIVAL_SWAPPABLE_TOKENS := [
	"C_STICK_A_L", "C_STICK_A_R", "K_STICK_A_L", "K_STICK_A_R",
	"MC_STICK_01A", "MC_STICK_01B", "R007_R_CENOCAPH",
	"R_STICK_A_L", "R_STICK_A_R", "GRASS_02", "GRASS_03",
	"ON_BOX_05", "ON_BOX_06", "ON_BOX_07", "PLANT_01", "PLANT_05",
	"TREE_02",
]
var _requested_ids: Array[int] = []


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_parse_args()
	var output_abs: String = ProjectSettings.globalize_path(OUTPUT_DIR)
	var mkdir_error: int = DirAccess.make_dir_recursive_absolute(output_abs)
	if mkdir_error != OK:
		printerr("[MAP_AOI_BUILD] cannot create output dir error=%d path=%s" % [mkdir_error, output_abs])
		quit(2)
		return
	var ids: Array[int] = _requested_ids if not _requested_ids.is_empty() else _discover_map_ids()
	if ids.is_empty():
		printerr("[MAP_AOI_BUILD] no generated MapXX.scn scenes found/listed")
		quit(3)
		return
	var built: int = 0
	var missing: int = 0
	var stripped_total: int = 0
	var results: Array[Dictionary] = []
	for map_id in ids:
		var source_path: String = "res://maps/Map%02d.scn" % map_id
		if not ResourceLoader.exists(source_path):
			missing += 1
			continue
		var result: Dictionary = await _build_map(map_id, source_path)
		results.append(result)
		if bool(result.get("ok", false)):
			built += 1
			stripped_total += int(result.get("stripped", 0))
	var manifest := {"version": 1, "built": built, "missing": missing, "stripped": stripped_total, "maps": results}
	var manifest_file := FileAccess.open(OUTPUT_DIR + "/manifest.json", FileAccess.WRITE)
	if manifest_file:
		manifest_file.store_string(JSON.stringify(manifest, "\t"))
	print("[MAP_AOI_BUILD] complete built=%d missing=%d stripped=%d" % [built, missing, stripped_total])
	quit(0 if built > 0 else 4)


func _build_map(map_id: int, source_path: String) -> Dictionary:
	var packed := ResourceLoader.load(source_path, "", ResourceLoader.CACHE_MODE_IGNORE_DEEP) as PackedScene
	if packed == null:
		return {"map": map_id, "ok": false, "reason": "load_failed"}
	var root := packed.instantiate()
	if root == null:
		return {"map": map_id, "ok": false, "reason": "instantiate_failed"}
	for collision_name in ["CollisionContainer", "CollisionLayer"]:
		var collision := root.get_node_or_null(collision_name)
		if is_instance_valid(collision):
			collision.get_parent().remove_child(collision)
			collision.free()
	var stripped: int = 0
	var skipped_festival: int = 0
	var pending: Array[Node] = [root]
	while not pending.is_empty():
		var node: Node = pending.pop_back()
		for child in node.get_children():
			pending.append(child)
		if not (node is Sprite2D):
			continue
		var sprite := node as Sprite2D
		if sprite.get_script() != null or _inside_dynamic_or_building(sprite, root):
			continue
		if not is_instance_valid(sprite.texture):
			continue
		var path: String = sprite.texture.resource_path
		if not path.begins_with("res://"):
			continue
		if _is_festival_swappable(path):
			skipped_festival += 1
			continue
		sprite.set_meta(META_TEXTURE_PATH, path)
		sprite.texture = null
		stripped += 1
	var base := PackedScene.new()
	var pack_error: int = base.pack(root)
	var output_path: String = "%s/Map%02d_base.scn" % [OUTPUT_DIR, map_id]
	var save_error: int = ERR_CANT_CREATE
	if pack_error == OK:
		save_error = ResourceSaver.save(base, output_path)
	root.free()
	await process_frame
	var ok: bool = pack_error == OK and save_error == OK
	print("[MAP_AOI_BUILD] map=%d ok=%s stripped=%d festival_kept=%d output=%s" % [
		map_id, str(ok), stripped, skipped_festival, output_path,
	])
	return {"map": map_id, "ok": ok, "stripped": stripped, "festival_kept": skipped_festival, "output": output_path, "pack_error": pack_error, "save_error": save_error}


func _inside_dynamic_or_building(node: Node, root: Node) -> bool:
	var cursor: Node = node.get_parent()
	while is_instance_valid(cursor) and cursor != root:
		if str(cursor.name) in ["Entities", "EntityContainer", "MonsterContainer", "NpcContainer", "NPCContainer", "DropContainer"]:
			return true
		if cursor.is_in_group("Building"):
			return true
		cursor = cursor.get_parent()
	return false


func _is_festival_swappable(resource_path: String) -> bool:
	for token in FESTIVAL_SWAPPABLE_TOKENS:
		if resource_path.contains(token):
			return true
	return false


func _parse_args() -> void:
	for arg in OS.get_cmdline_user_args():
		if not arg.begins_with("--map="):
			continue
		for raw_id in arg.trim_prefix("--map=").split(",", false):
			var map_id: int = int(raw_id.strip_edges())
			if map_id > 0 and not _requested_ids.has(map_id):
				_requested_ids.append(map_id)
	_requested_ids.sort()


func _discover_map_ids() -> Array[int]:
	var ids: Array[int] = []
	if FileAccess.file_exists(MANIFEST_PATH):
		var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(MANIFEST_PATH))
		if typeof(parsed) == TYPE_DICTIONARY:
			for raw_scene in (parsed as Dictionary).get("scenes", []):
				var name: String = str(raw_scene).get_file()
				if name.begins_with("Map") and name.ends_with(".scn"):
					var id_text: String = name.trim_prefix("Map").trim_suffix(".scn")
					if id_text.is_valid_int():
						ids.append(int(id_text))
	ids.sort()
	return ids
