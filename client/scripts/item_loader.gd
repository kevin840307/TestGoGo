
extends Node


const BG_CACHE_TRIM = preload("res://scripts/background_cache_trim.gd")
const ICON_PATH = "res://Assets/Items/Icons/Icon%04d.png"
const PHOTO_PATH = "res://Assets/Items/Photos/Photo%04d.png"
const GROUND_PATH = "res://Assets/Items/Grounds/Ground%04d.png"


const ITEM_FOLDERS: Array[String] = [
	"booster", 
	"equip", 
	"general", 
	"recovery", 
	"weapon", 
	"horse", 
	"horseequip", 
	"horsebattery"
]


var _item_database: Dictionary = {}
var _item_path_map: Dictionary = {}
var _is_data_loaded: bool = false

var _icon_cache: Dictionary = {}
var _photo_cache: Dictionary = {}
var _ground_cache: Dictionary = {}



func trim_unreferenced_runtime_cache() -> int:
	var removed: int = 0
	removed += BG_CACHE_TRIM.trim_unused(_icon_cache, 48)
	removed += BG_CACHE_TRIM.trim_unused(_photo_cache, 8)
	removed += BG_CACHE_TRIM.trim_unused(_ground_cache, 24)
	return removed


func clear_runtime_cache() -> void :
	_icon_cache.clear()
	_photo_cache.clear()
	_ground_cache.clear()

func _safe_get(obj: Object, property: String, default_value: Variant) -> Variant:
	var val = obj.get(property)
	return val if val != null else default_value


func preload_all_item_data() -> void :
	if _is_data_loaded: return

	for folder in ITEM_FOLDERS:
		var dir_path = "res://Items/%s/" % folder
		var dir = DirAccess.open(dir_path)
		if dir:
			for file_name in dir.get_files():
				if file_name.ends_with(".tres"):
					var item_id = file_name.get_basename().to_int()
					var file_path = dir_path + file_name

					_item_path_map[item_id] = file_path
		else:
			print("⚠️ [ItemLoader] 找不到目錄: ", dir_path)

	_is_data_loaded = true



func get_item_data(item_id: int) -> Resource:

	if _item_database.has(item_id):
		return _item_database[item_id]


	if _item_path_map.has(item_id):
		var path = _item_path_map[item_id]
		var item_data = ResourceLoader.load(path)
		if item_data:
			_item_database[item_id] = item_data
			return item_data

	return null


func get_item_property(item_id: int, property: String, default_value: Variant) -> Variant:


	var item = get_item_data(item_id)
	if item:
		var val = item.get(property)
		return val if val != null else default_value
	return default_value


func get_item_ids_in_folders(folder_names: Array) -> Array[int]:
	if not _is_data_loaded:
		preload_all_item_data()

	var requested_folders: Array[String] = []
	for folder_name in folder_names:
		var normalized_name: = str(folder_name).strip_edges().to_lower()
		if normalized_name in ITEM_FOLDERS and normalized_name not in requested_folders:
			requested_folders.append(normalized_name)

	var item_ids: Array[int] = []
	for raw_item_id in _item_path_map:
		var item_path: = str(_item_path_map[raw_item_id]).to_lower()
		for folder_name in requested_folders:
			if "/%s/" % folder_name in item_path:
				item_ids.append(int(raw_item_id))
				break

	item_ids.sort()
	return item_ids


func get_icon(icon_id: int) -> Texture2D:
	if icon_id <= 0: return null
	if _icon_cache.has(icon_id): return _icon_cache[icon_id]
	var path = ICON_PATH % icon_id
	if ResourceLoader.exists(path):
		var tex = load(path) as Texture2D
		_icon_cache[icon_id] = tex
		return tex
	return null

func get_photo(photo_id: int) -> Texture2D:
	if photo_id <= 0: return null
	if _photo_cache.has(photo_id): return _photo_cache[photo_id]
	var path = PHOTO_PATH % photo_id
	if ResourceLoader.exists(path):
		var tex = load(path) as Texture2D
		_photo_cache[photo_id] = tex
		return tex
	return null

func get_ground(ground_id: int) -> Texture2D:
	if ground_id <= 0: return null
	if _ground_cache.has(ground_id): return _ground_cache[ground_id]
	var path = GROUND_PATH % ground_id
	if ResourceLoader.exists(path):
		var tex = load(path) as Texture2D
		_ground_cache[ground_id] = tex
		return tex
	return null








func get_item_category(item_id: int) -> int:
	var item = get_item_data(item_id)
	if not item: return -1


	var path = item.resource_path.to_lower()

	if "/general/" in path or "/recovery/" in path or "/booster/" in path:
		return 0
	elif "/equip/" in path or "/weapon/" in path:
		return 1
	elif "/horse/" in path:
		return 3
	elif "/horseequip/" in path:
		return 4
	elif "/horsebattery/" in path:
		return 5

	return -1
