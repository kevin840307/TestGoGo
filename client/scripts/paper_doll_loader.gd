
extends Node

const BG_CACHE_TRIM = preload("res://scripts/background_cache_trim.gd")
const ASSET_ROOT = "res://Assets/Player/%s/materials/"
const MECHA_ASSET_ROOT = "res://Assets/MS/%s/materials/"
const MA_ASSET_ROOT = "res://Assets/MA/%s/materials/"
const SR_ASSET_ROOT = "res://Assets/SR/Standard/materials/"
const SR_EFFECT_ASSET_ROOT = "res://Assets/SR/Effect/materials/"
const SR_IDS = ["SR01", "SR02", "SR03", "SR04", "SR05", "SR06", "SR07"]
const SR_EFFECT_FILES = {
	"TransformIn": "SR_TransformIn", 
	"TransformOut": "SR_TransformOut", 
}

var _cache: Dictionary = {}
var _sr_cache: Dictionary = {}
var _sr_effect_cache: Dictionary = {}


func trim_unreferenced_runtime_cache() -> int:
	var removed: int = 0
	for category in _cache.keys():
		if removed >= 24:
			break
		var category_cache: Dictionary = _cache.get(category, {})
		removed += BG_CACHE_TRIM.trim_unused(category_cache, 12, 24 - removed)
	if removed < 24:
		removed += BG_CACHE_TRIM.trim_unused(_sr_cache, 4, 24 - removed)
	if removed < 24:
		removed += BG_CACHE_TRIM.trim_unused(_sr_effect_cache, 2, 24 - removed)
	return removed


func clear_runtime_cache() -> void :
	_cache.clear()
	_sr_cache.clear()
	_sr_effect_cache.clear()


func clear_runtime_cache_batched(frame_budget_usec: int = 3500) -> int:
	# AFK entry must not destroy hundreds of AtlasTexture references in one
	# rendered frame. Active actor visuals have already been detached before
	# this runs, so each cached SpriteFrames can be released independently.
	var removed: int = 0
	var frame_started_usec: int = Time.get_ticks_usec()
	for category_value in _cache.keys().duplicate():
		var category = category_value
		var category_cache: Dictionary = _cache.get(category, {})
		for part_value in category_cache.keys().duplicate():
			category_cache.erase(part_value)
			removed += 1
			if Time.get_ticks_usec() - frame_started_usec >= frame_budget_usec:
				await get_tree().process_frame
				frame_started_usec = Time.get_ticks_usec()
		_cache.erase(category)
	for key in _sr_cache.keys().duplicate():
		_sr_cache.erase(key)
		removed += 1
		if Time.get_ticks_usec() - frame_started_usec >= frame_budget_usec:
			await get_tree().process_frame
			frame_started_usec = Time.get_ticks_usec()
	for key in _sr_effect_cache.keys().duplicate():
		_sr_effect_cache.erase(key)
		removed += 1
		if Time.get_ticks_usec() - frame_started_usec >= frame_budget_usec:
			await get_tree().process_frame
			frame_started_usec = Time.get_ticks_usec()
	return removed


func get_sr_frames(sr_id: String) -> SpriteFrames:
	if sr_id not in SR_IDS:
		return null
	if _sr_cache.has(sr_id):
		return _sr_cache[sr_id]
	var frames = _load_sr_atlas(SR_ASSET_ROOT + sr_id, true)
	if frames:
		_sr_cache[sr_id] = frames
	return frames


func get_sr_effect_frames(effect_name: String) -> SpriteFrames:
	if not SR_EFFECT_FILES.has(effect_name):
		return null
	if _sr_effect_cache.has(effect_name):
		return _sr_effect_cache[effect_name]
	var frames = _load_sr_atlas(SR_EFFECT_ASSET_ROOT + SR_EFFECT_FILES[effect_name], false)
	if frames:
		_sr_effect_cache[effect_name] = frames
	return frames


func _load_sr_atlas(base_path: String, allow_looping: bool) -> SpriteFrames:
	var png_path = base_path + ".png"
	var json_path = base_path + ".json"
	if not ResourceLoader.exists(png_path) or not FileAccess.file_exists(json_path):
		return null

	var big_texture = ResourceLoader.load(
		png_path, "", ResourceLoader.CACHE_MODE_IGNORE
	) as Texture2D
	if not big_texture:
		return null
	var json_file = FileAccess.open(json_path, FileAccess.READ)
	if not json_file:
		return null
	var parsed = JSON.parse_string(json_file.get_as_text())
	if not parsed is Dictionary:
		return null
	var atlas_data: Dictionary = parsed
	var animations = atlas_data.get("animations", {})
	var frames_meta = atlas_data.get("frames", {})
	if not animations is Dictionary or not frames_meta is Dictionary:
		return null

	var sprite_frames = SpriteFrames.new()
	sprite_frames.remove_animation("default")
	for anim_name_value in animations.keys():
		var anim_name = str(anim_name_value)
		var frame_keys = animations[anim_name_value]
		if not frame_keys is Array or frame_keys.is_empty():
			return null
		sprite_frames.add_animation(anim_name)
		var loops = allow_looping and (
			anim_name.begins_with("idle_")
			or anim_name.begins_with("walk_")
			or anim_name.begins_with("combat_idle_")
		)
		sprite_frames.set_animation_loop(anim_name, loops)
		sprite_frames.set_animation_speed(anim_name, 8.0)
		for frame_key in frame_keys:
			if not frames_meta.has(frame_key) or not frames_meta[frame_key] is Dictionary:
				return null
			var meta: Dictionary = frames_meta[frame_key]
			for required_field in ["x", "y", "w", "h", "offset_x", "offset_y"]:
				if not meta.has(required_field):
					return null
			var atlas_texture = AtlasTexture.new()
			atlas_texture.atlas = big_texture
			atlas_texture.region = Rect2(
				float(meta["x"]), 
				float(meta["y"]), 
				float(meta["w"]), 
				float(meta["h"])
			)
			atlas_texture.set_meta("old_offset", Vector2(float(meta["offset_x"]), float(meta["offset_y"])))
			if meta.has("source_action"):
				atlas_texture.set_meta("source_action", str(meta["source_action"]))
			if meta.has("source_frame"):
				atlas_texture.set_meta("source_frame", int(meta["source_frame"]))
			sprite_frames.add_frame(anim_name, atlas_texture)
	return sprite_frames

func get_sprite_frames(category: String, part_name: String) -> SpriteFrames:
	if not _cache.has(category): _cache[category] = {}
	if _cache[category].has(part_name): return _cache[category][part_name]

	var base_path = ""


	if category == "MechaStandard":
		base_path = MECHA_ASSET_ROOT % "Standard" + part_name
	elif category == "MA_Standard":
		base_path = MA_ASSET_ROOT % "Standard" + part_name
	elif category == "MechaHead":
		base_path = MECHA_ASSET_ROOT % "Head" + part_name
	else:
		base_path = ASSET_ROOT % category + part_name

	var png_path = base_path + ".png"
	var json_path = base_path + ".json"


	if not ResourceLoader.exists(png_path) or not FileAccess.file_exists(json_path):
		if category.begins_with("Y/") or category.begins_with("A/") or part_name.begins_with("Y-") or part_name.begins_with("A-") or part_name.begins_with("Y_") or part_name.begins_with("A_"):
			var boy_part_name = "B" + part_name.substr(1)
			var boy_category = category
			if category.begins_with("Y/") or category.begins_with("A/"):
				boy_category = "B" + category.substr(1)
			return get_sprite_frames(boy_category, boy_part_name)
		return null

	var big_texture = ResourceLoader.load(
		png_path, "", ResourceLoader.CACHE_MODE_IGNORE
	) as Texture2D
	var json_text = FileAccess.open(json_path, FileAccess.READ).get_as_text()
	var atlas_data: Dictionary = JSON.parse_string(json_text)

	var sf = SpriteFrames.new()
	sf.remove_animation("default")

	var animations = atlas_data.get("animations", {})
	var frames_meta = atlas_data.get("frames", {})

	for anim_name in animations.keys():
		sf.add_animation(anim_name)
		sf.set_animation_loop(anim_name, not ("attack" in anim_name or "hit" in anim_name))
		sf.set_animation_speed(anim_name, 8.0)

		for f_key in animations[anim_name]:
			var meta = frames_meta[f_key]
			var atlas_tex = AtlasTexture.new()
			atlas_tex.atlas = big_texture
			atlas_tex.region = Rect2(meta.x, meta.y, meta.w, meta.h)


			atlas_tex.set_meta("old_offset", Vector2(meta.offset_x, meta.offset_y))

			sf.add_frame(anim_name, atlas_tex)

	_cache[category][part_name] = sf
	return sf
