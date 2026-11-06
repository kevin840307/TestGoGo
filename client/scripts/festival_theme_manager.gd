class_name FestivalThemeManager
extends RefCounted







const NORMAL: = "normal"
const MID_AUTUMN: = "mid_autumn"
const EASTER: = "easter"
const HALLOWEEN: = "halloween"
const CHRISTMAS: = "christmas"
const LUNAR_NEW_YEAR: = "lunar_new_year"

const FESTIVAL_ROOT: = "res://maps/Map/OBJECT/節日"
const NORMAL_MAP_OBJECT_ROOT: = "res://maps/Map/OBJECT"

const META_ASSET_NAME: = &"festival_asset_name"
const META_NORMAL_TEXTURE: = &"festival_normal_texture"
const META_NORMAL_OFFSET: = &"festival_normal_offset"

const THEME_FOLDERS: = {
	NORMAL: "一般", 
	MID_AUTUMN: "中秋節", 
	EASTER: "復活節", 
	HALLOWEEN: "萬聖節", 
	CHRISTMAS: "聖誕節", 
	LUNAR_NEW_YEAR: "農曆新年", 
}

const ASSET_DEFINITIONS: = {
	"C_STICK_A_L": {
		"relative_path": "outside_object/C_STICK_A_L.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(38, 299), MID_AUTUMN: Vector2i(42, 299), 
			EASTER: Vector2i(58, 299), HALLOWEEN: Vector2i(45, 299), 
			CHRISTMAS: Vector2i(57, 299), LUNAR_NEW_YEAR: Vector2i(77, 299), 
		}, 
	}, 
	"C_STICK_A_R": {
		"relative_path": "outside_object/C_STICK_A_R.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(43, 299), MID_AUTUMN: Vector2i(57, 299), 
			EASTER: Vector2i(67, 299), HALLOWEEN: Vector2i(55, 299), 
			CHRISTMAS: Vector2i(57, 299), LUNAR_NEW_YEAR: Vector2i(63, 299), 
		}, 
	}, 
	"K_STICK_A_L": {
		"relative_path": "outside_object/K_STICK_A_L.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(42, 363), MID_AUTUMN: Vector2i(47, 363), 
			EASTER: Vector2i(66, 363), HALLOWEEN: Vector2i(42, 363), 
			CHRISTMAS: Vector2i(52, 363), LUNAR_NEW_YEAR: Vector2i(71, 363), 
		}, 
	}, 
	"K_STICK_A_R": {
		"relative_path": "outside_object/K_STICK_A_R.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(44, 364), MID_AUTUMN: Vector2i(53, 364), 
			EASTER: Vector2i(65, 364), HALLOWEEN: Vector2i(49, 364), 
			CHRISTMAS: Vector2i(52, 364), LUNAR_NEW_YEAR: Vector2i(61, 364), 
		}, 
	}, 
	"MC_STICK_01A": {
		"relative_path": "outside_object/MC_STICK_01A.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(52, 306), MID_AUTUMN: Vector2i(52, 306), 
			EASTER: Vector2i(63, 306), HALLOWEEN: Vector2i(52, 306), 
			CHRISTMAS: Vector2i(57, 306), LUNAR_NEW_YEAR: Vector2i(70, 306), 
		}, 
	}, 
	"MC_STICK_01B": {
		"relative_path": "outside_object/MC_STICK_01B.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(52, 302), MID_AUTUMN: Vector2i(52, 302), 
			EASTER: Vector2i(64, 302), HALLOWEEN: Vector2i(52, 302), 
			CHRISTMAS: Vector2i(57, 302), LUNAR_NEW_YEAR: Vector2i(65, 302), 
		}, 
	}, 
	"R_STICK_A_L": {
		"relative_path": "outside_object/R_STICK_A_L.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(44, 324), MID_AUTUMN: Vector2i(55, 324), 
			EASTER: Vector2i(64, 324), HALLOWEEN: Vector2i(50, 324), 
			CHRISTMAS: Vector2i(57, 324), LUNAR_NEW_YEAR: Vector2i(76, 324), 
		}, 
	}, 
	"R_STICK_A_R": {
		"relative_path": "outside_object/R_STICK_A_R.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(34, 326), MID_AUTUMN: Vector2i(41, 326), 
			EASTER: Vector2i(63, 326), HALLOWEEN: Vector2i(38, 326), 
			CHRISTMAS: Vector2i(57, 326), LUNAR_NEW_YEAR: Vector2i(68, 326), 
		}, 
	}, 
	"R007_R_CENOCAPH": {
		"relative_path": "outside_object/R007_R_CENOCAPH.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(194, 554), MID_AUTUMN: Vector2i(194, 554), 
			EASTER: Vector2i(194, 554), HALLOWEEN: Vector2i(194, 554), 
			CHRISTMAS: Vector2i(201, 571), LUNAR_NEW_YEAR: Vector2i(212, 525), 
		}, 
	}, 
	"GRASS_02": {
		"relative_path": "Plant&Tree/GRASS_02.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(37, 64), MID_AUTUMN: Vector2i(49, 62), 
			EASTER: Vector2i(37, 64), HALLOWEEN: Vector2i(48, 123), 
			CHRISTMAS: Vector2i(37, 64), LUNAR_NEW_YEAR: Vector2i(39, 64), 
		}, 
	}, 
	"GRASS_03": {
		"relative_path": "Plant&Tree/GRASS_03.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(40, 51), MID_AUTUMN: Vector2i(40, 51), 
			EASTER: Vector2i(40, 51), HALLOWEEN: Vector2i(40, 51), 
			CHRISTMAS: Vector2i(65, 68), LUNAR_NEW_YEAR: Vector2i(40, 51), 
		}, 
	}, 
	"ON_BOX_05": {
		"relative_path": "Plant&Tree/ON_BOX_05.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(38, 105), MID_AUTUMN: Vector2i(50, 109), 
			EASTER: Vector2i(38, 105), HALLOWEEN: Vector2i(54, 119), 
			CHRISTMAS: Vector2i(45, 105), LUNAR_NEW_YEAR: Vector2i(44, 105), 
		}, 
	}, 
	"ON_BOX_06": {
		"relative_path": "Plant&Tree/ON_BOX_06.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(34, 96), MID_AUTUMN: Vector2i(28, 89), 
			EASTER: Vector2i(34, 96), HALLOWEEN: Vector2i(34, 96), 
			CHRISTMAS: Vector2i(37, 96), LUNAR_NEW_YEAR: Vector2i(44, 96), 
		}, 
	}, 
	"ON_BOX_07": {
		"relative_path": "Plant&Tree/ON_BOX_07.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(39, 106), MID_AUTUMN: Vector2i(71, 106), 
			EASTER: Vector2i(65, 129), HALLOWEEN: Vector2i(39, 109), 
			CHRISTMAS: Vector2i(39, 110), LUNAR_NEW_YEAR: Vector2i(44, 106), 
		}, 
	}, 
	"PLANT_01": {
		"relative_path": "Plant&Tree/PLANT_01.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(69, 124), MID_AUTUMN: Vector2i(83, 124), 
			EASTER: Vector2i(74, 130), HALLOWEEN: Vector2i(69, 124), 
			CHRISTMAS: Vector2i(69, 146), LUNAR_NEW_YEAR: Vector2i(89, 124), 
		}, 
	}, 
	"PLANT_05": {
		"relative_path": "Plant&Tree/PLANT_05.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(69, 144), MID_AUTUMN: Vector2i(69, 164), 
			EASTER: Vector2i(69, 144), HALLOWEEN: Vector2i(69, 144), 
			CHRISTMAS: Vector2i(69, 158), LUNAR_NEW_YEAR: Vector2i(91, 144), 
		}, 
	}, 
	"TREE_02": {
		"relative_path": "Plant&Tree/TREE_02.spr.0000.png", 
		"offsets": {
			NORMAL: Vector2i(108, 262), MID_AUTUMN: Vector2i(108, 262), 
			EASTER: Vector2i(108, 262), HALLOWEEN: Vector2i(85, 337), 
			CHRISTMAS: Vector2i(85, 337), LUNAR_NEW_YEAR: Vector2i(112, 314), 
		}, 
	}, 
}

static var _texture_cache: Dictionary = {}


static func normalize_theme(theme: String) -> String:
	var normalized: = theme.strip_edges().to_lower()
	return normalized if THEME_FOLDERS.has(normalized) else NORMAL


static func is_valid_theme(theme: String) -> bool:
	return THEME_FOLDERS.has(theme.strip_edges().to_lower())


static func get_theme_ids() -> Array[String]:
	var theme_ids: Array[String] = []
	for theme_id in THEME_FOLDERS:
		theme_ids.append(theme_id)
	return theme_ids


static func get_asset_names() -> Array[String]:
	var asset_names: Array[String] = []
	for asset_name in ASSET_DEFINITIONS:
		asset_names.append(asset_name)
	return asset_names


static func has_asset(asset_name: String) -> bool:
	return ASSET_DEFINITIONS.has(asset_name)


static func get_source_offset(asset_name: String, theme: String) -> Vector2i:
	if not ASSET_DEFINITIONS.has(asset_name):
		return Vector2i.ZERO
	var valid_theme: = normalize_theme(theme)
	return ASSET_DEFINITIONS[asset_name]["offsets"][valid_theme]


static func get_texture_path(asset_name: String, theme: String) -> String:
	if not ASSET_DEFINITIONS.has(asset_name):
		return ""
	var valid_theme: = normalize_theme(theme)
	var folder: String = THEME_FOLDERS[valid_theme]
	var relative_path: String = ASSET_DEFINITIONS[asset_name]["relative_path"]
	return "%s/%s/%s" % [FESTIVAL_ROOT, folder, relative_path]


static func get_texture(asset_name: String, theme: String) -> Texture2D:
	var texture_path: = get_texture_path(asset_name, theme)
	if texture_path.is_empty():
		return null
	if _texture_cache.has(texture_path):
		return _texture_cache[texture_path] as Texture2D

	var texture: = ResourceLoader.load(texture_path) as Texture2D
	if texture == null and normalize_theme(theme) != NORMAL:
		texture_path = get_texture_path(asset_name, NORMAL)
		if _texture_cache.has(texture_path):
			return _texture_cache[texture_path] as Texture2D
		texture = ResourceLoader.load(texture_path) as Texture2D

	if texture != null:
		_texture_cache[texture_path] = texture
	return texture


static func clear_texture_cache() -> void :
	_texture_cache.clear()


static func apply_theme_to_map(map_root: Node, theme: String) -> Dictionary:
	var result: = {
		"theme": normalize_theme(theme), 
		"matched": 0, 
		"changed": 0, 
		"failed": 0, 
	}
	if map_root == null:
		result["failed"] = 1
		return result

	var valid_theme: String = result["theme"]
	var pending: Array[Node] = [map_root]
	while not pending.is_empty():
		var node: Node = pending.pop_back()
		for child in node.get_children():
			pending.append(child)
		if node is Sprite2D:
			_apply_theme_to_sprite(node as Sprite2D, valid_theme, result)
	return result


static func _apply_theme_to_sprite(sprite: Sprite2D, theme: String, result: Dictionary) -> void :
	var asset_name: = _get_sprite_asset_name(sprite)
	if asset_name.is_empty():
		return
	result["matched"] += 1

	if not sprite.has_meta(META_NORMAL_TEXTURE):
		sprite.set_meta(META_NORMAL_TEXTURE, sprite.texture)
		sprite.set_meta(META_NORMAL_OFFSET, sprite.offset)

	var normal_texture: = sprite.get_meta(META_NORMAL_TEXTURE) as Texture2D
	var normal_offset: Vector2 = sprite.get_meta(META_NORMAL_OFFSET)
	var effective_theme: = theme
	var target_texture: = normal_texture if theme == NORMAL else get_texture(asset_name, theme)
	var requested_path: = get_texture_path(asset_name, theme)
	if target_texture == null or (theme != NORMAL and target_texture.resource_path != requested_path):
		result["failed"] += 1
		effective_theme = NORMAL
		target_texture = normal_texture
	if target_texture == null:
		return

	var normal_source_offset: = Vector2(get_source_offset(asset_name, NORMAL))
	var theme_source_offset: = Vector2(get_source_offset(asset_name, effective_theme))
	var target_offset: = normal_offset + normal_source_offset - theme_source_offset
	if sprite.texture != target_texture or sprite.offset != target_offset:
		sprite.texture = target_texture
		sprite.offset = target_offset
		result["changed"] += 1


static func _get_sprite_asset_name(sprite: Sprite2D) -> String:
	if sprite.has_meta(META_ASSET_NAME):
		var saved_asset_name: = str(sprite.get_meta(META_ASSET_NAME))
		return saved_asset_name if ASSET_DEFINITIONS.has(saved_asset_name) else ""
	if sprite.texture == null:
		return ""

	var texture_path: = sprite.texture.resource_path
	for asset_name in ASSET_DEFINITIONS:
		var relative_path: String = ASSET_DEFINITIONS[asset_name]["relative_path"]
		if texture_path == "%s/%s" % [NORMAL_MAP_OBJECT_ROOT, relative_path]:
			sprite.set_meta(META_ASSET_NAME, asset_name)
			return asset_name
	return ""


static func validate_catalog(load_textures: bool = true) -> PackedStringArray:
	var errors: = PackedStringArray()
	if THEME_FOLDERS.size() != 6:
		errors.append("主題數量應為 6，實際為 %d" % THEME_FOLDERS.size())
	if ASSET_DEFINITIONS.size() != 17:
		errors.append("素材數量應為 17，實際為 %d" % ASSET_DEFINITIONS.size())

	for asset_name in ASSET_DEFINITIONS:
		var definition: Dictionary = ASSET_DEFINITIONS[asset_name]
		var offsets: Dictionary = definition.get("offsets", {})
		for theme_id in THEME_FOLDERS:
			if not offsets.has(theme_id):
				errors.append("%s 缺少 %s offset" % [asset_name, theme_id])
				continue
			var texture_path: = get_texture_path(asset_name, theme_id)
			if not ResourceLoader.exists(texture_path):
				errors.append("找不到素材：%s" % texture_path)
			elif load_textures and get_texture(asset_name, theme_id) == null:
				errors.append("素材載入失敗：%s" % texture_path)
	return errors
