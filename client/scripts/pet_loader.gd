
extends Node

const BG_CACHE_TRIM = preload("res://scripts/background_cache_trim.gd")
const ASSET_ROOT = "res://Assets/Pets/"
const CONFIG_PATH = "res://Assets/Pets/Config.json"

var _config_cache: Dictionary = {}
var _frames_cache: Dictionary = {}
var is_data_loaded: bool = false

func trim_unreferenced_runtime_cache() -> int:
	return BG_CACHE_TRIM.trim_unused(_frames_cache, 8)


func clear_runtime_cache() -> void :
	_frames_cache.clear()

func preload_all_pet_data() -> void :
	if is_data_loaded: return

	if FileAccess.file_exists(CONFIG_PATH):
		var json_text = FileAccess.open(CONFIG_PATH, FileAccess.READ).get_as_text()
		var parsed = JSON.parse_string(json_text)
		if parsed and parsed is Dictionary:

			for key in parsed.keys():
				_config_cache[int(key)] = parsed[key]
	else:
		push_error("⚠️ [PetLoader] 找不到寵物設定檔: " + CONFIG_PATH)

	is_data_loaded = true


func get_pet_config(pet_id: int) -> Dictionary:
	if not is_data_loaded: preload_all_pet_data()
	return _config_cache.get(pet_id, {})


func get_all_pet_configs() -> Dictionary:
	if not is_data_loaded: preload_all_pet_data()
	return _config_cache.duplicate(true)


func get_pet_frames(outlook_name: String) -> SpriteFrames:
	outlook_name = outlook_name.to_upper()
	if _frames_cache.has(outlook_name):
		return _frames_cache[outlook_name]

	var png_path = ASSET_ROOT + outlook_name + ".png"
	var json_path = ASSET_ROOT + outlook_name + ".json"

	if not ResourceLoader.exists(png_path) or not FileAccess.file_exists(json_path):
		push_error("⚠️ 找不到寵物圖集資源: " + outlook_name)
		return null

	var json_text = FileAccess.open(json_path, FileAccess.READ).get_as_text()
	var atlas_data: Dictionary = JSON.parse_string(json_text)
	var big_texture = ResourceLoader.load(
		png_path, "", ResourceLoader.CACHE_MODE_IGNORE
	) as Texture2D
	var sf = SpriteFrames.new()
	sf.remove_animation("default")

	var animations = atlas_data.get("animations", {})

	for anim_name in animations.keys():
		sf.add_animation(anim_name)
		sf.set_animation_loop(anim_name, not ("attack" in anim_name or "hit" in anim_name or "death" in anim_name))
		sf.set_animation_speed(anim_name, 8.0)

		for frame_data in animations[anim_name]:
			var tex = AtlasTexture.new()
			tex.atlas = big_texture
			tex.region = Rect2(frame_data[0], frame_data[1], frame_data[2], frame_data[3])


			if frame_data.size() >= 6:
				tex.set_meta("old_offset", Vector2(frame_data[4], frame_data[5]))
			else:
				tex.set_meta("old_offset", Vector2.ZERO)

			sf.add_frame(anim_name, tex)

	_frames_cache[outlook_name] = sf
	return sf
