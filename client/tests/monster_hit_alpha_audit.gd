extends Node
# Offline asset audit. PNGs are local/generated and ignored by Git, so this
# must run on a machine with the actual recovered monster media installed.
# godot --headless --path client --scene res://tests/monster_hit_alpha_audit.tscn
# godot --headless --path client --scene res://tests/monster_hit_alpha_audit.tscn -- --monster=EN004A
const ASSET_ROOT: String = "res://Assets/Monsters/"


func _ready() -> void:
	call_deferred("_audit")


func _audit() -> void:
	var requested_id: String = ""
	for argument in OS.get_cmdline_user_args():
		if str(argument).begins_with("--monster="):
			requested_id = str(argument).trim_prefix("--monster=").to_upper()
	var dir: DirAccess = DirAccess.open(ASSET_ROOT)
	if dir == null:
		push_error("[MONSTER_HIT_ALPHA_AUDIT] Missing monster asset directory")
		get_tree().quit(2)
		return
	var filenames: PackedStringArray = dir.get_files()
	filenames.sort()
	var loaded: int = 0
	var missing_media: int = 0
	var checked_frames: int = 0
	var suspicious_frames: int = 0
	var invalid_frames: int = 0
	for filename in filenames:
		if not filename.ends_with(".json"):
			continue
		var monster_id: String = filename.get_basename().to_upper()
		if not requested_id.is_empty() and monster_id != requested_id:
			continue
		if not ResourceLoader.exists(ASSET_ROOT + monster_id + ".png"):
			missing_media += 1
			continue
		var frames: SpriteFrames = MonsterLoader.get_monster_frames(monster_id, false)
		if frames == null:
			invalid_frames += 1
			continue
		loaded += 1
		for animation_name in frames.get_animation_names():
			if not str(animation_name).begins_with("HIT_"):
				continue
			for index in range(frames.get_frame_count(animation_name)):
				checked_frames += 1
				var tile: AtlasTexture = frames.get_frame_texture(
					animation_name, index) as AtlasTexture
				if tile == null or not tile.filter_clip:
					invalid_frames += 1
					push_error("[MONSTER_HIT_ALPHA_AUDIT] Invalid clipped atlas monster=%s animation=%s index=%d" % [
						monster_id, animation_name, index
					])
					continue
				var pixels: Image = tile.get_image()
				if pixels == null or pixels.is_empty():
					invalid_frames += 1
					continue
				# Sampling is intentionally diagnostic only: tightly cropped
				# opaque artwork can be legitimate. Never auto-key or damage
				# an original monster texture based on this heuristic.
				var transparent: int = 0
				var total: int = 0
				var step_x: int = maxi(1, pixels.get_width() / 16)
				var step_y: int = maxi(1, pixels.get_height() / 16)
				for y in range(0, pixels.get_height(), step_y):
					for x in range(0, pixels.get_width(), step_x):
						total += 1
						if pixels.get_pixel(x, y).a < 0.99:
							transparent += 1
				if total > 0 and transparent == 0:
					suspicious_frames += 1
					push_warning("[MONSTER_HIT_ALPHA_AUDIT] SUSPECT_OPAQUE monster=%s animation=%s frame=%d region=%s; verify source PNG alpha" % [
						monster_id, animation_name, index, str(tile.region)
					])
	print("[MONSTER_HIT_ALPHA_AUDIT] loaded_monsters=%d checked_hit_frames=%d suspicious_opaque=%d invalid=%d missing_media=%d filter_clip_enabled=true" % [
		loaded, checked_frames, suspicious_frames, invalid_frames, missing_media
	])
	if loaded == 0:
		print("[MONSTER_HIT_ALPHA_AUDIT] No local PNG found. Git ignores generated PNGs; run this test where the actual game media is installed.")
	get_tree().quit(1 if invalid_frames > 0 else 0)
