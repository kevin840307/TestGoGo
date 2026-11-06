
extends Node


const ASSET_ROOT = "res://Assets/Monsters/"
const AUDIO_RESOURCE_PATH_META = "se_event_resource_path"
const AUDIO_PLAYED_CUES_META = "se_event_played_cues"
const AUDIO_LAST_FRAME_META = "se_event_last_frame"
const ACT_TIMELINES_META = "act_timing_timelines"
var _cache: Dictionary = {}

func clear_runtime_cache() -> void :
	_cache.clear()

func get_monster_frames(monster_id: String, apply_act_timing: bool = false) -> SpriteFrames:
	monster_id = monster_id.to_upper()
	var cache_key = "%s:%s" % [monster_id, "timed" if apply_act_timing else "legacy"]


	if _cache.has(cache_key):
		return _cache[cache_key]

	var png_path = ASSET_ROOT + monster_id + ".png"
	var json_path = ASSET_ROOT + monster_id + ".json"


	if not ResourceLoader.exists(png_path) or not FileAccess.file_exists(json_path):
		push_error("⚠️ 找不到怪物資源: " + monster_id)
		return null


	var big_texture = load(png_path) as Texture2D
	var json_text = FileAccess.open(json_path, FileAccess.READ).get_as_text()
	var atlas_data: Dictionary = JSON.parse_string(json_text)

	var sf = SpriteFrames.new()
	sf.remove_animation("default")
	sf.set_meta(AUDIO_RESOURCE_PATH_META, json_path)

	var animations = atlas_data.get("animations", {})
	var frames_meta = atlas_data.get("frames", {})


	for anim_name in animations.keys():
		sf.add_animation(anim_name)

		sf.set_animation_loop(anim_name, not ("DEATH" in anim_name or "ATTACK" in anim_name or "HIT" in anim_name))
		sf.set_animation_speed(anim_name, 8.0)

		for f_key in animations[anim_name]:
			var meta = frames_meta[f_key]
			var atlas_tex = AtlasTexture.new()
			atlas_tex.atlas = big_texture
			atlas_tex.region = Rect2(meta.x, meta.y, meta.w, meta.h)


			atlas_tex.set_meta("old_offset", Vector2(meta.offset_x, meta.offset_y))

			sf.add_frame(anim_name, atlas_tex)


	if apply_act_timing:
		_apply_act_timing(sf, json_path)
	_cache[cache_key] = sf
	return sf

func begin_audio_sequence(sprite: AnimatedSprite2D) -> void :
	if is_instance_valid(sprite):
		sprite.set_meta(AUDIO_PLAYED_CUES_META, {})
		sprite.set_meta(AUDIO_LAST_FRAME_META, -1)

func play_confirmed_frame_audio(sprite: AnimatedSprite2D) -> bool:
	if not is_instance_valid(sprite) or not sprite.sprite_frames:
		return false
	var action = _get_act_action(str(sprite.animation))
	if action.is_empty():
		return false
	var resource_path = str(sprite.sprite_frames.get_meta(AUDIO_RESOURCE_PATH_META, ""))
	if resource_path.is_empty():
		return false

	var cue_key = "%s:%d" % [action, sprite.frame]
	var played_cues = sprite.get_meta(AUDIO_PLAYED_CUES_META, {})
	if not played_cues is Dictionary:
		played_cues = {}
	var last_frame = int(sprite.get_meta(AUDIO_LAST_FRAME_META, -1))
	if action in ["S", "W"] and last_frame >= 0 and sprite.frame < last_frame:
		played_cues.clear()
	sprite.set_meta(AUDIO_LAST_FRAME_META, sprite.frame)
	if played_cues.has(cue_key):
		return false
	if sprite.sprite_frames.has_meta(ACT_TIMELINES_META):
		var timelines = sprite.sprite_frames.get_meta(ACT_TIMELINES_META, {})
		if timelines is Dictionary and timelines.has(action):
			var timeline = timelines[action]
			if not timeline is Array or sprite.frame < 0 or sprite.frame >= timeline.size():
				return false
			var entry = timeline[sprite.frame]
			if not entry is Dictionary:
				return false
			var played = _play_timeline_audio(entry)
			if played:
				played_cues[cue_key] = true
				sprite.set_meta(AUDIO_PLAYED_CUES_META, played_cues)
			return played

	var candidates = AudioManager.get_se_event_candidates(resource_path, action, sprite.frame)
	if candidates.is_empty():
		return false
	if not AudioManager.play_se_candidates(candidates):
		return false
	played_cues[cue_key] = true
	sprite.set_meta(AUDIO_PLAYED_CUES_META, played_cues)
	return true

func _play_timeline_audio(entry: Dictionary) -> bool:
	var played = false
	var audio_cues = entry.get("audio", [])
	if not audio_cues is Array:
		return false
	for cue in audio_cues:
		if not cue is Dictionary:
			continue
		var candidates: Array[String] = []
		var sounds = cue.get("sounds", [])
		if sounds is Array:
			for sound in sounds:
				var normalized = str(sound).strip_edges().to_upper()
				if not normalized.is_empty():
					candidates.append(normalized)
		if not candidates.is_empty() and AudioManager.play_se_candidates(candidates):
			played = true
	return played

func _apply_act_timing(sprite_frames: SpriteFrames, resource_path: String) -> void :
	if not AnimationTimingManager or not AnimationTimingManager.is_loaded():
		return
	var effective_tick_rate = AnimationTimingManager.get_effective_tick_rate_hz()
	if effective_tick_rate <= 0.0:
		return
	var action_prefixes = {
		"S": "IDLE", 
		"W": "WALK", 
		"A": "ATTACK", 
		"D": "DEATH", 
		"H": "HIT", 
	}
	var applied_timelines: Dictionary = {}
	for action in action_prefixes:
		if action == "W":
			continue
		var timeline = AnimationTimingManager.get_resource_timeline(resource_path, action)
		if timeline.is_empty():
			continue
		var animation_names: Array[StringName] = []
		var prefix = str(action_prefixes[action]) + "_"
		for animation_name in sprite_frames.get_animation_names():
			if str(animation_name).begins_with(prefix):
				animation_names.append(animation_name)
		if animation_names.is_empty() or not _timeline_fits_animations(sprite_frames, animation_names, timeline, resource_path):
			continue
		for animation_name in animation_names:
			var textures: Array[Texture2D] = []
			for entry in timeline:
				textures.append(sprite_frames.get_frame_texture(animation_name, int(entry["frame"])))
			var looped = sprite_frames.get_animation_loop(animation_name)
			sprite_frames.clear(animation_name)
			sprite_frames.set_animation_speed(animation_name, effective_tick_rate)
			sprite_frames.set_animation_loop(animation_name, looped)
			for index in timeline.size():
				sprite_frames.add_frame(animation_name, textures[index], float(timeline[index]["ticks"]))
		applied_timelines[action] = timeline
	if not applied_timelines.is_empty():
		sprite_frames.set_meta(ACT_TIMELINES_META, applied_timelines)

func _timeline_fits_animations(sprite_frames: SpriteFrames, animation_names: Array[StringName], timeline: Array, resource_path: String) -> bool:
	for entry in timeline:
		if not entry is Dictionary:
			return false
		var sprite_frame = int(entry.get("frame", -1))
		var ticks = int(entry.get("ticks", 0))
		if sprite_frame < 0 or ticks <= 0:
			return false
		for animation_name in animation_names:
			if sprite_frame >= sprite_frames.get_frame_count(animation_name):
				push_warning("[MonsterLoader] ACT timing frame %d exceeds %s animation %s" % [sprite_frame, resource_path, animation_name])
				return false
	return true

func _get_act_action(animation_name: String) -> String:
	if animation_name.begins_with("IDLE"):
		return "S"
	if animation_name.begins_with("WALK"):
		return "W"
	if animation_name.begins_with("ATTACK"):
		return "A"
	if animation_name.begins_with("DEATH"):
		return "D"
	if animation_name.begins_with("HIT"):
		return "H"
	return ""
