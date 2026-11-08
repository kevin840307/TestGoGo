
extends Node



const BG_CACHE_TRIM = preload("res://scripts/background_cache_trim.gd")
const SKILL_DATA_PATH = "res://Skills/"


const ICON_PATH = "res://Skills/Icon/ICON_%03d.png"


var _skill_database: Dictionary = {}
var _skill_path_map: Dictionary = {}
var _icon_cache: Dictionary = {}
var _is_data_loaded: bool = false


func trim_unreferenced_runtime_cache() -> int:
	return BG_CACHE_TRIM.trim_unused(_icon_cache, 24)


func clear_runtime_cache() -> void :
	_icon_cache.clear()

func preload_all_skill_data() -> void :
	if _is_data_loaded: return
	var dir = DirAccess.open(SKILL_DATA_PATH)

	if dir:
		for file_name in dir.get_files():
			if file_name.ends_with(".tres"):
				var skill_id = file_name.get_basename().to_int()
				var file_path = SKILL_DATA_PATH + file_name
				_skill_path_map[skill_id] = file_path
	else:
		print("⚠️ [SkillLoader] 找不到技能資料目錄: ", SKILL_DATA_PATH)

	_is_data_loaded = true


func get_skill_data(skill_id: int) -> Resource:

	if _skill_database.has(skill_id):
		return _skill_database[skill_id]


	if _skill_path_map.has(skill_id):
		var path = _skill_path_map[skill_id]
		var skill_data = ResourceLoader.load(path)
		if skill_data:
			_skill_database[skill_id] = skill_data
			return skill_data

	return null


func get_icon(icon_id: int) -> Texture2D:
	if icon_id < 0: return null
	if _icon_cache.has(icon_id): return _icon_cache[icon_id]

	var path = ICON_PATH % icon_id
	if ResourceLoader.exists(path):
		var tex = load(path) as Texture2D
		_icon_cache[icon_id] = tex
		return tex

	return null
