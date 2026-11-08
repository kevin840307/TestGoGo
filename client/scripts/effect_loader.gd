
extends Node

const BG_CACHE_TRIM = preload("res://scripts/background_cache_trim.gd")
const EFFECT_ROOT = "res://Assets/Effects/"
const AUDIO_RESOURCE_PATH_META = "se_event_resource_path"
const AUDIO_LAST_FRAME_META = "se_event_last_frame"
const AUDIO_PLAYED_FRAME_META = "se_event_played_frame"
const ACT_TIMELINE_META = "act_timing_timeline"
var _effect_cache: Dictionary = {}
var _missing_effect_cache: Dictionary = {}

func trim_unreferenced_runtime_cache() -> int:
	return BG_CACHE_TRIM.trim_unused(_effect_cache, 8)


func clear_runtime_cache() -> void :
	_effect_cache.clear()
	_missing_effect_cache.clear()

func get_effect(sub_path: String) -> SpriteFrames:
	var full_path = EFFECT_ROOT + sub_path + ".tres"
	if _effect_cache.has(full_path):
		return _effect_cache[full_path]
	if _missing_effect_cache.has(full_path):
		# Assets may be added or replaced while developing. Re-check the
		# filesystem instead of permanently returning a stale miss.
		if ResourceLoader.exists(full_path):
			_missing_effect_cache.erase(full_path)
		else:
			return null

	if ResourceLoader.exists(full_path):
		var source_frames = load(full_path) as SpriteFrames
		if source_frames:
			var frames = _build_timed_frames(source_frames, full_path)
			frames.set_meta(AUDIO_RESOURCE_PATH_META, full_path)
			_effect_cache[full_path] = frames
			return frames

	_missing_effect_cache[full_path] = true
	push_warning("[EffectLoader] 找不到特效資源: " + full_path)
	return null

func get_offset(tex: Texture2D) -> Vector2:
	if not tex: return Vector2.ZERO
	if tex.has_meta("old_offset"):
		return tex.get_meta("old_offset")
	return Vector2.ZERO

func play_confirmed_frame_audio(effect_node: AnimatedSprite2D) -> bool:
	if not is_instance_valid(effect_node) or not effect_node.sprite_frames:
		return false
	var frames = effect_node.sprite_frames
	var resource_path = str(frames.get_meta(AUDIO_RESOURCE_PATH_META, ""))
	if resource_path.is_empty():
		return false

	var current_frame = effect_node.frame
	var last_frame = int(effect_node.get_meta(AUDIO_LAST_FRAME_META, -1))
	if current_frame == 0 and last_frame != 0:
		effect_node.set_meta(AUDIO_PLAYED_FRAME_META, -1)
	effect_node.set_meta(AUDIO_LAST_FRAME_META, current_frame)
	if frames.has_meta(ACT_TIMELINE_META):
		var timeline = frames.get_meta(ACT_TIMELINE_META, [])
		if not timeline is Array or current_frame < 0 or current_frame >= timeline.size():
			return false
		if int(effect_node.get_meta(AUDIO_PLAYED_FRAME_META, -1)) == current_frame:
			return false
		var timeline_entry = timeline[current_frame]
		if not timeline_entry is Dictionary:
			return false
		var played = false
		var audio_cues = timeline_entry.get("audio", [])
		if audio_cues is Array:
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
		if played:
			effect_node.set_meta(AUDIO_PLAYED_FRAME_META, current_frame)
		return played

	var candidates = AudioManager.get_se_event_candidates(resource_path, "Effect", current_frame)
	if candidates.is_empty():
		return false
	if int(effect_node.get_meta(AUDIO_PLAYED_FRAME_META, -1)) == current_frame:
		return false

	if not AudioManager.play_se_candidates(candidates):
		return false
	effect_node.set_meta(AUDIO_PLAYED_FRAME_META, current_frame)
	return true

func _build_timed_frames(source_frames: SpriteFrames, resource_path: String) -> SpriteFrames:
	if not AnimationTimingManager or not AnimationTimingManager.is_loaded():
		return source_frames
	var timeline = AnimationTimingManager.get_resource_timeline(resource_path, "Effect")
	if timeline.is_empty():
		return source_frames
	var effective_tick_rate = AnimationTimingManager.get_effective_tick_rate_hz()
	if effective_tick_rate <= 0.0:
		return source_frames
	var animation_names = source_frames.get_animation_names()
	if animation_names.is_empty():
		return source_frames
	for entry in timeline:
		if not entry is Dictionary:
			return source_frames
		var sprite_frame = int(entry.get("frame", -1))
		var ticks = int(entry.get("ticks", 0))
		if sprite_frame < 0 or ticks <= 0:
			return source_frames
		for animation_name in animation_names:
			if sprite_frame >= source_frames.get_frame_count(animation_name):
				push_warning("[EffectLoader] ACT timing frame %d exceeds %s animation %s" % [sprite_frame, resource_path, animation_name])
				return source_frames

	var timed_frames = source_frames.duplicate() as SpriteFrames
	if timed_frames == null:
		return source_frames
	for animation_name in animation_names:
		var looped = source_frames.get_animation_loop(animation_name)
		timed_frames.clear(animation_name)
		timed_frames.set_animation_speed(animation_name, effective_tick_rate)
		timed_frames.set_animation_loop(animation_name, looped)
		for entry in timeline:
			var sprite_frame = int(entry["frame"])
			var ticks = float(entry["ticks"])
			var texture = source_frames.get_frame_texture(animation_name, sprite_frame)
			timed_frames.add_frame(animation_name, texture, ticks)
	timed_frames.set_meta(ACT_TIMELINE_META, timeline)
	return timed_frames
