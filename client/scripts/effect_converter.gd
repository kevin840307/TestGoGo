
@tool
extends EditorScript

const SOURCE_DIR = "res://Assets/Effects/RawData/"
const OUTPUT_DIR = "res://Assets/Effects/"
const DEFAULT_FPS = 20.0
const LOOP_ANIMATION = false

var regex_pattern_A: RegEx
var regex_pattern_B: RegEx

func _run() -> void :
	print("🚀 >>> 開始批次轉換 SpriteFrames (將 TXT 偏移量寫入 Meta) <<< 🚀")
	if not DirAccess.dir_exists_absolute(OUTPUT_DIR):
		DirAccess.make_dir_recursive_absolute(OUTPUT_DIR)

	regex_pattern_A = RegEx.new()
	regex_pattern_A.compile("^(.+)_([a-zA-Z]{2})(\\d)$")
	regex_pattern_B = RegEx.new()
	regex_pattern_B.compile("^(.+)_(\\d)$")

	var dir = DirAccess.open(SOURCE_DIR)
	if not dir:
		print("❌ 找不到來源目錄: ", SOURCE_DIR)
		return

	dir.list_dir_begin()
	var folder_name = dir.get_next()
	while folder_name != "":
		if dir.current_is_dir() and not folder_name.begins_with("."):
			_process_folder(SOURCE_DIR + folder_name + "/", folder_name)
		folder_name = dir.get_next()
	dir.list_dir_end()
	print("✅ >>> 所有特效轉換完成！請到 ", OUTPUT_DIR, " 查看 <<< ✅")

func _process_folder(folder_path: String, folder_name: String) -> void :
	var dir = DirAccess.open(folder_path)
	if not dir: return

	var png_files = []
	dir.list_dir_begin()
	var file_name = dir.get_next()
	while file_name != "":
		if not dir.current_is_dir() and file_name.ends_with(".png") and not file_name.ends_with(".import"):
			png_files.append(file_name)
		file_name = dir.get_next()
	dir.list_dir_end()

	if png_files.is_empty(): return
	png_files.sort()
	var grouped_effects = {}

	for png in png_files:

		var prefix = png.get_basename().get_basename()
		if ".spr." in png:
			prefix = png.split(".spr.")[0]
		elif "." in png.get_basename():
			prefix = png.split(".")[0]

		var tres_name = ""
		var anim_name = ""
		var match_A = regex_pattern_A.search(prefix)
		var match_B = regex_pattern_B.search(prefix)

		if match_A:
			tres_name = match_A.get_string(1) + "_" + match_A.get_string(2)
			anim_name = match_A.get_string(3)
		elif match_B:
			tres_name = match_B.get_string(1)
			anim_name = match_B.get_string(2)
		else:
			tres_name = prefix
			anim_name = "default"

		if not grouped_effects.has(tres_name):
			grouped_effects[tres_name] = {}
		if not grouped_effects[tres_name].has(anim_name):
			grouped_effects[tres_name][anim_name] = []

		grouped_effects[tres_name][anim_name].append(folder_path + png)

	var target_out_dir = OUTPUT_DIR + folder_name + "/"
	if not DirAccess.dir_exists_absolute(target_out_dir):
		DirAccess.make_dir_recursive_absolute(target_out_dir)

	for t_name in grouped_effects.keys():
		var sprite_frames = SpriteFrames.new()
		if sprite_frames.has_animation("default"):
			sprite_frames.remove_animation("default")

		for a_name in grouped_effects[t_name].keys():
			sprite_frames.add_animation(a_name)
			sprite_frames.set_animation_speed(a_name, DEFAULT_FPS)
			sprite_frames.set_animation_loop(a_name, LOOP_ANIMATION)

			for img_path in grouped_effects[t_name][a_name]:
				var tex = load(img_path) as Texture2D
				if tex:

					var atlas_tex = AtlasTexture.new()
					atlas_tex.atlas = tex
					atlas_tex.region = Rect2(0, 0, tex.get_width(), tex.get_height())

					var txt_path = img_path.get_basename() + ".txt"
					var offset_val = Vector2.ZERO
					if FileAccess.file_exists(txt_path):
						var file = FileAccess.open(txt_path, FileAccess.READ)
						var line = file.get_line().strip_edges()
						file.close()
						var parts = line.replace(",", " ").split(" ", false)
						if parts.size() >= 2:
							offset_val = Vector2(parts[0].to_float(), parts[1].to_float())

					atlas_tex.set_meta("old_offset", offset_val)
					sprite_frames.add_frame(a_name, atlas_tex)
				else:
					print("  ❌ 圖片載入失敗: ", img_path)

		var output_path = target_out_dir + t_name + ".tres"
		if ResourceSaver.save(sprite_frames, output_path) == OK:
			print("  ✅ 成功建立: ", output_path)
