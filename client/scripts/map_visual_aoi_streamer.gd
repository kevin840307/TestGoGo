extends RefCounted
class_name MapVisualAoiStreamer

# Presentation-only static map streaming. This class never touches collision,
# navigation, networking, physics, or authoritative entity state.
const UPDATE_INTERVAL_SECONDS: float = 0.10
const SCAN_BATCH_SIZE: int = 240
const MAX_PENDING_PATHS: int = 12

# FestivalThemeManager may swap these at runtime and stores the normal texture
# in metadata. Keep them resident so AOI streaming cannot break seasonal swaps.
const FESTIVAL_SWAPPABLE_TOKENS := PackedStringArray([
	"C_STICK_A_L", "C_STICK_A_R", "K_STICK_A_L", "K_STICK_A_R",
	"MC_STICK_01A", "MC_STICK_01B", "R007_R_CENOCAPH",
	"R_STICK_A_L", "R_STICK_A_R", "GRASS_02", "GRASS_03",
	"ON_BOX_05", "ON_BOX_06", "ON_BOX_07", "PLANT_01", "PLANT_05",
	"TREE_02",
])

var _root_id: int = 0
var _entries: Array[Dictionary] = []
var _path_indices: Dictionary = {}
var _pending_paths: Dictionary = {}
var _scan_cursor: int = 0
var _elapsed: float = 0.0
var _indexed: int = 0
var _unloaded: int = 0
var _last_enter_radius: float = 0.0
var _last_exit_radius: float = 0.0


func clear() -> void:
	# Do not restore textures here. clear() is called before the old map is
	# destroyed/AFK-shell stripped, so dropping node references is intentional.
	_root_id = 0
	_entries.clear()
	_path_indices.clear()
	_pending_paths.clear()
	_scan_cursor = 0
	_elapsed = 0.0
	_indexed = 0
	_unloaded = 0
	_last_enter_radius = 0.0
	_last_exit_radius = 0.0


func is_bound_to(root: Node) -> bool:
	return is_instance_valid(root) and _root_id == root.get_instance_id()


func bind_map(root: Node) -> Dictionary:
	clear()
	if not is_instance_valid(root):
		return stats()

	_root_id = root.get_instance_id()
	var pending: Array[Node] = [root]
	while not pending.is_empty():
		var node: Node = pending.pop_back()
		if not is_instance_valid(node):
			continue
		for child in node.get_children():
			pending.append(child)
		if not node is Sprite2D:
			continue

		var sprite := node as Sprite2D
		if sprite.get_script() != null or _inside_dynamic_container(sprite, root):
			continue
		var texture := sprite.texture
		if not is_instance_valid(texture):
			continue
		var resource_path: String = texture.resource_path
		if not resource_path.begins_with("res://"):
			continue
		if _is_festival_swappable(resource_path):
			continue

		var index: int = _entries.size()
		_entries.append({
			"node": sprite,
			"path": resource_path,
			"position": sprite.global_position,
			"loaded": true,
		})
		if not _path_indices.has(resource_path):
			_path_indices[resource_path] = []
		(_path_indices[resource_path] as Array).append(index)

	_indexed = _entries.size()
	return stats()


func update(player_position: Vector2, profile: Dictionary, delta: float) -> void:
	if _entries.is_empty():
		return
	_elapsed += delta
	var enter_radius: float = float(profile.get("map_visual_aoi_enter_radius", 3200.0))
	var exit_radius: float = float(profile.get("map_visual_aoi_exit_radius", enter_radius + 800.0))
	exit_radius = maxf(exit_radius, enter_radius + 400.0)
	_last_enter_radius = enter_radius
	_last_exit_radius = exit_radius

	_poll_pending_paths(player_position, enter_radius)
	if _elapsed < UPDATE_INTERVAL_SECONDS:
		return
	_elapsed = 0.0

	var enter_sq: float = enter_radius * enter_radius
	var exit_sq: float = exit_radius * exit_radius
	var count: int = mini(SCAN_BATCH_SIZE, _entries.size())
	for _i in range(count):
		if _entries.is_empty():
			break
		if _scan_cursor >= _entries.size():
			_scan_cursor = 0
		var index: int = _scan_cursor
		_scan_cursor += 1
		var entry: Dictionary = _entries[index]
		var sprite := entry.get("node", null) as Sprite2D
		if not is_instance_valid(sprite):
			continue

		var distance_sq: float = player_position.distance_squared_to(entry.get("position", Vector2.ZERO))
		var loaded: bool = is_instance_valid(sprite.texture)
		entry["loaded"] = loaded
		if loaded and distance_sq > exit_sq:
			sprite.texture = null
			entry["loaded"] = false
			_unloaded += 1
			_entries[index] = entry
			continue

		if not loaded and distance_sq <= enter_sq:
			var resource_path: String = str(entry.get("path", ""))
			var shared := _find_live_texture(resource_path)
			if is_instance_valid(shared):
				sprite.texture = shared
				entry["loaded"] = true
				_unloaded = maxi(0, _unloaded - 1)
				_entries[index] = entry
			else:
				_request_path(resource_path)


func _request_path(resource_path: String) -> void:
	if resource_path.is_empty() or _pending_paths.has(resource_path):
		return
	if _pending_paths.size() >= MAX_PENDING_PATHS:
		return
	if not ResourceLoader.exists(resource_path):
		return
	var request_error: int = ResourceLoader.load_threaded_request(
		resource_path, "Texture2D", false, ResourceLoader.CACHE_MODE_IGNORE
	)
	if request_error == OK:
		_pending_paths[resource_path] = true


func _poll_pending_paths(player_position: Vector2, enter_radius: float) -> void:
	if _pending_paths.is_empty():
		return
	var enter_sq: float = enter_radius * enter_radius
	for resource_path in _pending_paths.keys().duplicate():
		var status: int = ResourceLoader.load_threaded_get_status(str(resource_path))
		if status == ResourceLoader.THREAD_LOAD_IN_PROGRESS:
			continue
		if status != ResourceLoader.THREAD_LOAD_LOADED:
			_pending_paths.erase(resource_path)
			continue
		var texture := ResourceLoader.load_threaded_get(str(resource_path)) as Texture2D
		_pending_paths.erase(resource_path)
		if not is_instance_valid(texture):
			continue
		_assign_loaded_texture(str(resource_path), texture, player_position, enter_sq)


func _assign_loaded_texture(
	resource_path: String,
	texture: Texture2D,
	player_position: Vector2,
	enter_sq: float
) -> void:
	var indices: Array = _path_indices.get(resource_path, [])
	for raw_index in indices:
		var index: int = int(raw_index)
		if index < 0 or index >= _entries.size():
			continue
		var entry: Dictionary = _entries[index]
		var sprite := entry.get("node", null) as Sprite2D
		if not is_instance_valid(sprite) or is_instance_valid(sprite.texture):
			continue
		if player_position.distance_squared_to(entry.get("position", Vector2.ZERO)) > enter_sq:
			continue
		sprite.texture = texture
		entry["loaded"] = true
		_entries[index] = entry
		_unloaded = maxi(0, _unloaded - 1)


func _find_live_texture(resource_path: String) -> Texture2D:
	var indices: Array = _path_indices.get(resource_path, [])
	for raw_index in indices:
		var index: int = int(raw_index)
		if index < 0 or index >= _entries.size():
			continue
		var sprite := (_entries[index] as Dictionary).get("node", null) as Sprite2D
		if is_instance_valid(sprite) and is_instance_valid(sprite.texture):
			return sprite.texture
	return null


func _inside_dynamic_container(node: Node, root: Node) -> bool:
	var cursor: Node = node.get_parent()
	while is_instance_valid(cursor) and cursor != root:
		if str(cursor.name) in [
			"Entities", "EntityContainer", "MonsterContainer",
			"NpcContainer", "NPCContainer", "DropContainer"
		]:
			return true
		cursor = cursor.get_parent()
	return false


func _is_festival_swappable(resource_path: String) -> bool:
	for token in FESTIVAL_SWAPPABLE_TOKENS:
		if resource_path.contains(token):
			return true
	return false


func stats() -> Dictionary:
	return {
		"indexed": _indexed,
		"unloaded": _unloaded,
		"loaded": maxi(0, _indexed - _unloaded),
		"pending_paths": _pending_paths.size(),
		"enter_radius": _last_enter_radius,
		"exit_radius": _last_exit_radius,
	}
