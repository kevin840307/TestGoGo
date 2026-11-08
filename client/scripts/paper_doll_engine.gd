
extends Node2D
class_name PaperDollEngine



signal body_animation_finished()


const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}

const RACE_PREFIX_MAP = {"Boy": "B", "Girl": "G", "Migu": "A", "Robot": "R", "Yas": "Y"}
const PLAYER_CONTROLLER_PREFIX_MAP = {"Boy": "B", "Girl": "G", "Migu": "A", "Robot": "R", "Yas": "B"}

const DEFAULT_ORDER = ["Shadow", "Leg", "Body", "Head", "Hair", "L-Arm", "R-Arm"]

const ANIM_TO_RAW_PREFIX = {
	"idle": "NS", "walk": "NR", "crouch": "NB", "death": "ND", "guard": "AG", "buff": "NU", 
	"idle_weapon_melee": "AS", "idle_weapon_gun": "GS", "hit_weapon_melee": "AH", "hit_weapon_gun": "GH", 
	"attack_aa": "AA", "attack_ab": "AB", "attack_ga": "GA", "attack_gb": "GB", 
	"attack_na": "NA", "attack_nb": "NB", 
	"emote_a": "ZA", "emote_b": "ZB", "emote_c": "ZC", "emote_d": "ZD", "emote_e": "ZE", "emote_f": "ZF", 
	"emote_g": "ZG", "emote_h": "ZH", "emote_i": "ZI", "emote_j": "ZJ", "emote_k": "ZK", "emote_l": "ZL"
}


const RAW_ORDER_DATA = {
	"NS1": ["Shadow", "Back", "Leg", "R-Arm", "Body", "L-Arm", "Head", "Hair", "Bonnet"], "NS2": ["Shadow", "Leg", "R-Arm", "Body", "Back", "L-Arm", "Head", "Hair", "Bonnet"], 
	"NS3": ["Shadow", "Leg", "R-Arm", "Body", "L-Arm", "Back", "Head", "Hair", "Bonnet"], "NS4": ["Shadow", "Leg", "R-Arm", "Body", "L-Arm", "Back", "Head", "Hair", "Bonnet"], 
	"NS8": ["Shadow", "Back", "Leg", "L-Arm", "Body", "R-Arm", "Head", "Hair", "Bonnet"], "NR1": ["Shadow", "Back", "Leg", "R-Arm", "Body", "L-Arm", "Head", "Hair", "Bonnet"], 
	"NR2": ["Shadow", "Leg", "R-Arm", "Body", "Back", "L-Arm", "Head", "Hair", "Bonnet"], "NR3": ["Shadow", "Leg", "R-Arm", "Body", "L-Arm", "Back", "Head", "Hair", "Bonnet"], 
	"NR4": ["Shadow", "Leg", "Body", "R-Arm", "L-Arm", "Back", "Head", "Hair", "Bonnet"], "NR8": ["Shadow", "Back", "Leg", "Body", "R-Arm", "L-Arm", "Head", "Hair", "Bonnet"], 
	"NU1": ["Shadow", "Back", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], "NU2": ["Shadow", "Leg", "R-Arm", "Body", "Back", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], 
	"NU3": ["Shadow", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Back", "Head", "Hair", "Bonnet"], "NU4": ["Shadow", "Leg", "R-Arm", "L-Arm", "Body", "R-Shoulder", "L-Shoulder", "Back", "Head", "Hair", "Bonnet"], 
	"NU8": ["Shadow", "Back", "Leg", "Body", "R-Arm", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], "NB1": ["Shadow", "Back", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], 
	"NB2": ["Shadow", "Leg", "R-Arm", "Body", "Back", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], "NB3": ["Shadow", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Back", "Head", "Hair", "Bonnet"], 
	"NB4": ["Shadow", "Leg", "L-Arm", "R-Arm", "Body", "R-Shoulder", "L-Shoulder", "Back", "Head", "Hair", "Bonnet"], "NB8": ["Shadow", "Back", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], 
	"AA1": ["Shadow", "Back", "Leg", "R-Arm", "Body", "Head", "Hair", "Bonnet", "Shield", "L-Arm", "Sword"], "AA2": ["Shadow", "Shield", "Leg", "R-Arm", "Body", "Back", "Head", "Hair", "Bonnet", "L-Arm", "Sword"], 
	"AA3": ["Shadow", "Shield", "Leg", "R-Arm", "Body", "Back", "Head", "Hair", "Bonnet", "L-Arm", "Sword"], "AS1": ["Shadow", "Back", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet", "Shield", "Sword"], 
	"AS2": ["Shadow", "Leg", "R-Arm", "Back", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet", "Shield", "Sword"], "AS3": ["Shadow", "Shield", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Back", "Head", "Hair", "Bonnet", "Sword"], 
	"AS4": ["Shadow", "Shield", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Back", "Head", "Hair", "Bonnet", "Sword"], "AS8": ["Shadow", "Back", "Sword", "Leg", "L-Arm", "Body", "R-Arm", "L-Shoulder", "R-Shoulder", "Head", "Hair", "Bonnet", "Shield"], 
	"AH1": ["Shadow", "Back", "Shield", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet", "Sword"], "AH2": ["Shadow", "Shield", "Leg", "R-Arm", "Body", "Back", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet", "Sword"], 
	"AH3": ["Shadow", "Shield", "Leg", "R-Arm", "Head", "Hair", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Back", "Bonnet", "Sword"], "AB1": ["Shadow", "Back", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet", "Shield", "Sword"], 
	"AB2": ["Shadow", "Shield", "Leg", "R-Arm", "Back", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet", "Sword"], "AB3": ["Shadow", "Shield", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Back", "Head", "Hair", "Bonnet", "Sword"], 
	"AG1": ["Shadow", "Back", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet", "Shield", "Sword"], "AG2": ["Shadow", "Leg", "R-Arm", "Back", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet", "Shield", "Sword"], 
	"AG3": ["Shadow", "Shield", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "Back", "Head", "Hair", "Bonnet", "Sword"], "GS1": ["Shadow", "Back", "Leg", "L-Arm", "Body", "R-Arm", "L-Shoulder", "R-Shoulder", "L-Gun", "R-Gun", "Head", "Hair", "Bonnet"], 
	"GS2": ["Shadow", "Leg", "Back", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "R-Gun", "Head", "Hair", "Bonnet", "L-Gun"], "GS3": ["Shadow", "Leg", "R-Arm", "Body", "R-Shoulder", "R-Gun", "Back", "Head", "Hair", "Bonnet", "L-Gun", "L-Arm", "L-Shoulder"], 
	"GS4": ["Shadow", "Leg", "R-Arm", "R-Gun", "Body", "R-Shoulder", "Back", "Head", "Hair", "Bonnet", "L-Gun", "L-Arm", "L-Shoulder"], "GS8": ["Shadow", "Back", "Leg", "L-Arm", "L-Gun", "Body", "R-Arm", "L-Shoulder", "R-Shoulder", "R-Gun", "Head", "Hair", "Bonnet"], 
	"GH1": ["Shadow", "Back", "Leg", "R-Arm", "R-Gun", "Body", "L-Arm", "L-Gun", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], "GH2": ["Shadow", "Leg", "R-Arm", "R-Gun", "Body", "Back", "L-Arm", "L-Gun", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], 
	"GH3": ["Shadow", "Leg", "R-Gun", "R-Arm", "Body", "L-Gun", "L-Arm", "R-Shoulder", "L-Shoulder", "Back", "Head", "Hair", "Bonnet"], "GA1": ["Shadow", "Back", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "L-Gun", "Head", "Hair", "Bonnet"], 
	"GA2": ["Shadow", "Leg", "R-Arm", "Body", "Back", "L-Arm", "R-Shoulder", "L-Shoulder", "L-Gun", "Head", "Hair", "Bonnet"], "GA3": ["Shadow", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "L-Gun", "Back", "Head", "Hair", "Bonnet"], 
	"GB1": ["Shadow", "Back", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "R-Gun", "L-Gun", "Head", "Hair", "Bonnet"], "GB2": ["Shadow", "Leg", "R-Arm", "Body", "Back", "L-Arm", "R-Shoulder", "L-Shoulder", "R-Gun", "L-Gun", "Head", "Hair", "Bonnet"], 
	"GB3": ["Shadow", "Leg", "R-Arm", "Body", "L-Arm", "R-Shoulder", "L-Shoulder", "R-Gun", "L-Gun", "Back", "Head", "Hair", "Bonnet"], "ZA8": ["Shadow", "Back", "Leg", "Body", "R-Arm", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], 
	"ZB8": ["Shadow", "Back", "Leg", "Body", "R-Arm", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], "ZC8": ["Shadow", "Back", "Leg", "Body", "Head", "Hair", "R-Arm", "L-Arm", "R-Shoulder", "L-Shoulder", "Bonnet"], 
	"ZD8": ["Shadow", "Back", "Leg", "Body", "R-Arm", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], "ZE8": ["Shadow", "Back", "R-Arm", "L-Arm", "Leg", "Body", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], 
	"ZF8": ["Shadow", "Back", "Leg", "L-Arm", "Body", "R-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], "ZG8": ["Shadow", "Back", "Leg", "Body", "R-Arm", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], 
	"ZH8": ["Shadow", "Back", "Leg", "Body", "R-Arm", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], "ZI8": ["Shadow", "Back", "Leg", "Body", "L-Arm", "R-Arm", "L-Shoulder", "R-Shoulder", "Head", "Hair", "Bonnet"], 
	"ZJ8": ["Shadow", "Back", "Leg", "Body", "R-Arm", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], "ZK8": ["Shadow", "Back", "Leg", "Body", "R-Arm", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"], 
	"ZL8": ["Shadow", "Back", "Leg", "Body", "R-Arm", "L-Arm", "R-Shoulder", "L-Shoulder", "Head", "Hair", "Bonnet"]
}


const ROBOT_ORDER_DATA = {
	"NB4": ["Shadow", "Leg", "L-Arm", "Body", "R-Arm", "Back", "Head", "Hair", "Bonnet"], 
	"GS1": ["Shadow", "Back", "Leg", "R-Arm", "Body", "R-Gun", "Head", "Hair", "Bonnet", "L-Arm", "L-Gun"], 
	"GS2": ["Shadow", "Leg", "Back", "R-Arm", "Body", "R-Gun", "Head", "Hair", "Bonnet", "L-Arm", "L-Gun"], 
	"GS3": ["Shadow", "Leg", "R-Gun", "R-Arm", "Body", "Back", "Head", "Hair", "Bonnet", "L-Arm", "L-Gun"], 
	"ZC8": ["Shadow", "Back", "Leg", "Body", "Head", "Hair", "Bonnet", "R-Arm", "L-Arm"]
}


const MECHA_ORDER_DATA = {
	"NS1": ["MSLeg", "R-MSArm", "MSBody", "L-MSArm", "Head", "Hair", "Bonnet"], 
	"NS2": ["MSLeg", "R-MSArm", "MSBody", "L-MSArm", "Head", "Hair", "Bonnet"], 
	"NS3": ["MSLeg", "R-MSArm", "MSBody", "L-MSArm", "Head", "Hair", "Bonnet"], 
	"NS4": ["MSLeg", "R-MSArm", "L-MSArm", "MSBody", "Head", "Hair", "Bonnet"], 
	"NS8": ["MSLeg", "MSBody", "L-MSArm", "R-MSArm", "Head", "Hair", "Bonnet"], 
	"NR1": ["MSLeg", "R-MSArm", "MSBody", "L-MSArm", "Head", "Hair", "Bonnet"], 
	"NR2": ["MSLeg", "R-MSArm", "MSBody", "L-MSArm", "Head", "Hair", "Bonnet"], 
	"NR3": ["MSLeg", "R-MSArm", "MSBody", "L-MSArm", "Head", "Hair", "Bonnet"], 
	"NR4": ["MSLeg", "R-MSArm", "L-MSArm", "MSBody", "Head", "Hair", "Bonnet"], 
	"NR8": ["MSLeg", "MSBody", "L-MSArm", "R-MSArm", "Head", "Hair", "Bonnet"], 
	"NA1": ["MSLeg", "R-MSArm", "MSBody", "L-MSArm", "Head", "Hair", "Bonnet"], 
	"NA2": ["MSLeg", "R-MSArm", "MSBody", "L-MSArm", "Head", "Hair", "Bonnet"], 
	"NA3": ["MSLeg", "R-MSArm", "MSBody", "L-MSArm", "Head", "Hair", "Bonnet"], 
	"NA4": ["MSLeg", "R-MSArm", "L-MSArm", "MSBody", "Head", "Hair", "Bonnet"], 
	"NA8": ["MSLeg", "MSBody", "L-MSArm", "R-MSArm", "Head", "Hair", "Bonnet"], 
	"NB1": ["MSLeg", "R-MSArm", "MSBody", "L-MSArm", "Head", "Hair", "Bonnet"], 
	"NB2": ["MSLeg", "R-MSArm", "MSBody", "L-MSArm", "Head", "Hair", "Bonnet"], 
	"NB3": ["MSLeg", "R-MSArm", "MSBody", "L-MSArm", "Head", "Hair", "Bonnet"], 
	"NB4": ["MSLeg", "R-MSArm", "L-MSArm", "MSBody", "Head", "Hair", "Bonnet"], 
	"NB8": ["MSLeg", "MSBody", "L-MSArm", "R-MSArm", "Head", "Hair", "Bonnet"]
}


enum Mode{HUMAN, MECHA}
var current_mode: Mode = Mode.HUMAN
var current_mecha_type: String = "MS"

var current_race: String = "Boy"
var current_hair: String = "Hair000"
var current_hair_color_index: int = 0
var current_body_color_index: int = 0

var current_equips: Dictionary = {}
var doll_sprites: Dictionary = {}
var last_anim_state: String = ""
var current_mecha_body_spr_id: int = 0
var _audio_sequence_key: String = ""
var _audio_played_cues: Dictionary = {}
var _audio_last_frame: int = -1
static var _shared_controller_timed_frames_cache: Dictionary = {}
static var _mecha_head_anchor_cache: Dictionary = {}
# Bounded by 8 palette indices x effect types. Sharing identical material
# parameters avoids allocating a new ShaderMaterial for each doll part.
# The shader uses global TIME: identical materials preserve effect animation.
static var _shared_palette_materials: Dictionary = {}


static func get_shared_palette_material(color_index: int, effect_type: int) -> ShaderMaterial:
	var hue: float = float(COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
	var key: String = "%d:%.4f" % [effect_type, hue]
	var material: ShaderMaterial = _shared_palette_materials.get(key, null) as ShaderMaterial
	if is_instance_valid(material):
		return material
	material = ShaderMaterial.new()
	material.shader = PALETTE_SHADER
	material.set_shader_parameter("effect_type", effect_type)
	material.set_shader_parameter("hue_shift", hue)
	_shared_palette_materials[key] = material
	return material


static func get_shared_palette_material_count() -> int:
	return _shared_palette_materials.size()

const ACT_CONTROLLER_TIMELINES_META = "act_controller_timelines"
const USE_RESOURCE_MECHA_HEAD_ANCHORS: = true
const MS_HEAD_ANCHOR_ROOT: = "res://Assets/MS/HeadOffset/"
const MA_HEAD_ANCHOR_ROOT: = "res://Assets/MA/HeadOffset/"
const MECHA_HEAD_ANCHOR_ORIGIN: = Vector2(150.0, 176.0)
const MA_HEAD_ANCHOR_ORIGINS: = {
	"Migu": Vector2(75.0, 100.0), 
	"Boy": Vector2(125.0, 170.0), 
	"Girl": Vector2(125.0, 170.0), 
	"Robot": Vector2(150.0, 205.0), 
	"Yas": Vector2(125.0, 170.0)
}
const HUMAN_TIMING_ACTION_PREFIXES = {
	"NS": ["idle_"], "NR": ["walk_"], "NU": ["buff_"], 
	"NB": ["crouch_", "attack_nb_"], 
	"AS": ["idle_weapon_melee_"], "AH": ["hit_weapon_melee_"], 
	"AA": ["attack_aa_"], "AB": ["attack_ab_"], "AG": ["guard_"], 
	"GS": ["idle_weapon_gun_"], "GH": ["hit_weapon_gun_"], 
	"GA": ["attack_ga_"], "GB": ["attack_gb_"], 
	"ZA": ["emote_a_"], "ZB": ["emote_b_"], "ZC": ["emote_c_"], 
	"ZD": ["emote_d_"], "ZE": ["emote_e_"], "ZF": ["emote_f_"], 
	"ZG": ["emote_g_"], "ZH": ["emote_h_"], "ZI": ["emote_i_"], 
	"ZJ": ["emote_j_"], "ZK": ["emote_k_"], "ZL": ["emote_l_"], 
}
const MECHA_TIMING_ACTION_PREFIXES = {
	"NS": ["idle_"], "NR": ["walk_"], 
	"NA": ["attack_na_"], "NB": ["attack_nb_"], 
}



func clear_dolls() -> void :
	for child in get_children():
		child.free()
	doll_sprites.clear()

func get_player_controller(race: String) -> String:
	return "player:" + PLAYER_CONTROLLER_PREFIX_MAP.get(race, "B")

func _get_ma_head_anchor_origin(race: String) -> Vector2:
	return MA_HEAD_ANCHOR_ORIGINS.get(race, MA_HEAD_ANCHOR_ORIGINS["Boy"])

func _get_mecha_head_anchor(sprite_node: AnimatedSprite2D) -> Dictionary:
	if not is_instance_valid(sprite_node) or not sprite_node.sprite_frames:
		return {}
	var animation_name: = str(sprite_node.animation)
	var raw_action: = _get_raw_mecha_action(animation_name)
	var direction: = _get_animation_direction(animation_name)
	if raw_action.is_empty() or direction.is_empty():
		return {}
	var anchor_origin: = MECHA_HEAD_ANCHOR_ORIGIN
	if current_mecha_type == "MA":
		anchor_origin = _get_ma_head_anchor_origin(current_race)
	for path in _get_mecha_head_anchor_paths(raw_action, direction):
		var anchors: = _load_mecha_head_anchors(path)
		if anchors.is_empty():
			continue
		var anchor_index: = _get_mecha_head_anchor_frame(sprite_node, raw_action, anchors.size())
		if anchor_index >= 0 and anchor_index < anchors.size():
			return {
				"offset": anchors[anchor_index] - anchor_origin, 
				"path": path, 
				"frame": anchor_index, 
			}
	return {}

func _get_raw_mecha_action(animation_name: String) -> String:
	if animation_name.begins_with("idle_"):
		return "NS"
	if animation_name.begins_with("walk_"):
		return "NR"
	if animation_name.begins_with("attack_na_"):
		return "NA"
	if animation_name.begins_with("attack_nb_"):
		return "NB"
	return ""

func _get_animation_direction(animation_name: String) -> String:
	var separator: = animation_name.rfind("_")
	if separator < 0 or separator == animation_name.length() - 1:
		return ""
	var direction: = animation_name.substr(separator + 1)
	return direction if direction in ["1", "2", "3", "4", "8"] else ""

func _get_mecha_head_anchor_paths(raw_action: String, direction: String) -> Array[String]:
	var paths: Array[String] = []
	if current_mecha_type == "MA":
		var race_prefix: String = RACE_PREFIX_MAP.get(current_race, "B")
		if current_race == "Yas":
			race_prefix = "B"
		paths.append(MA_HEAD_ANCHOR_ROOT + "%s_MA%02d_%s%s.txt" % [race_prefix, current_mecha_body_spr_id, raw_action, direction])
		if race_prefix == "G":
			paths.append(MA_HEAD_ANCHOR_ROOT + "B_MA%02d_%s%s.txt" % [current_mecha_body_spr_id, raw_action, direction])
	else:
		paths.append(MS_HEAD_ANCHOR_ROOT + "B-MSBody%02d_%s%s.txt" % [current_mecha_body_spr_id, raw_action, direction])
	return paths

func _load_mecha_head_anchors(path: String) -> Array[Vector2]:
	if _mecha_head_anchor_cache.has(path):
		return _mecha_head_anchor_cache[path]
	var anchors: Array[Vector2] = []
	if not FileAccess.file_exists(path):
		_mecha_head_anchor_cache[path] = anchors
		return anchors
	var file: = FileAccess.open(path, FileAccess.READ)
	if file == null:
		_mecha_head_anchor_cache[path] = anchors
		return anchors
	var line_number: = 0
	while not file.eof_reached():
		line_number += 1
		var line: = file.get_line().strip_edges()
		if line.is_empty():
			continue
		var values: = line.split(",", false)
		if values.size() != 2 or not values[0].strip_edges().is_valid_int() or not values[1].strip_edges().is_valid_int():
			push_warning("[PaperDollEngine] Invalid mecha head anchor at %s:%d" % [path, line_number])
			anchors.clear()
			break
		anchors.append(Vector2(float(values[0].strip_edges().to_int()), float(values[1].strip_edges().to_int())))
	_mecha_head_anchor_cache[path] = anchors
	return anchors

func _get_mecha_head_anchor_frame(sprite_node: AnimatedSprite2D, raw_action: String, anchor_count: int) -> int:
	var timelines = sprite_node.sprite_frames.get_meta(ACT_CONTROLLER_TIMELINES_META, {})
	if timelines is Dictionary and timelines.has(raw_action):
		var timeline = timelines[raw_action]
		if timeline is Array and anchor_count != timeline.size() and sprite_node.frame >= 0 and sprite_node.frame < timeline.size():
			var entry = timeline[sprite_node.frame]
			if entry is Dictionary:
				return int(entry.get("frame", -1))
	return sprite_node.frame if sprite_node.frame >= 0 and sprite_node.frame < anchor_count else -1


func _yield_initial_build_frame() -> void :
	var tree := get_tree()
	if tree != null:
		await tree.process_frame

func build_human_model_initial_async(race: String, hair: String, hair_color: int, body_color: int, equips: Dictionary) -> void :
	current_mode = Mode.HUMAN
	current_race = race
	current_hair = hair
	current_hair_color_index = hair_color
	current_body_color_index = body_color
	current_equips = equips

	clear_dolls()

	_add_doll_part("Shadow", 0, "Shadow", "Shadow", false)
	await _yield_initial_build_frame()

	if current_race != "Robot":
		_add_doll_part("Head", 0, "Head", "Standard", false)
		await _yield_initial_build_frame()
		_add_doll_part("Hair", -1, current_hair, "Standard", false)
		await _yield_initial_build_frame()
		_add_doll_part("Leg", 0, "Leg", "Standard", false)
		await _yield_initial_build_frame()
		_add_doll_part("L-Arm", 0, "L-Arm", "Standard", false)
		await _yield_initial_build_frame()
		_add_doll_part("R-Arm", 0, "R-Arm", "Standard", false)
		await _yield_initial_build_frame()

	_add_doll_part("Body", 0, "Body", "Standard", false)
	await _yield_initial_build_frame()
	await _update_equipment_layers_initial_async()

func _update_equipment_layers_initial_async() -> void :
	var bonnet_id: int = int(current_equips.get(0, 0))
	var left_id: int = int(current_equips.get(1, 0))
	var right_id: int = int(current_equips.get(2, 0))
	var upper_id: int = int(current_equips.get(3, 0))
	var lower_id: int = int(current_equips.get(4, 0))
	var weapon_id: int = int(current_equips.get(5, 0))
	var shield_id: int = int(current_equips.get(6, 0))
	var back_id: int = int(current_equips.get(7, 0))

	if upper_id >= 0 and not (current_race == "Yas" and not _is_transmog_upper_armor(upper_id)):
		_add_doll_part("EquipBody", upper_id, "Body", "Equip", false)
		await _yield_initial_build_frame()
	if lower_id >= 0:
		_add_doll_part("EquipLeg", lower_id, "Leg", "Equip", false)
		await _yield_initial_build_frame()
	if left_id >= 0:
		_add_doll_part("EquipL-Arm", left_id, "L-Arm", "Equip", false)
		await _yield_initial_build_frame()
	if right_id >= 0:
		_add_doll_part("EquipR-Arm", right_id, "R-Arm", "Equip", false)
		await _yield_initial_build_frame()

	if bonnet_id > 0:
		_add_doll_part("Bonnet", bonnet_id, "Bonnet", "Equip", false)
		await _yield_initial_build_frame()
	elif current_race == "Robot":
		_add_doll_part("Bonnet", -1, "Bonnet000", "Equip", false)
		await _yield_initial_build_frame()

	if back_id > 0:
		_add_doll_part("Back", back_id, "Back", "Equip", false)
		await _yield_initial_build_frame()

	if weapon_id > 0:
		var w_prefix: String = "Sword"
		if ItemLoader:
			var n4: String = str(ItemLoader.get_item_property(weapon_id, "note_4", "")).strip_edges()
			if n4 != "":
				w_prefix = n4
		if w_prefix == "TwinGun":
			_add_doll_part("L-Gun", weapon_id, "L_TwinGun", "Equip", true)
			await _yield_initial_build_frame()
			_add_doll_part("R-Gun", weapon_id, "R_TwinGun", "Equip", true)
			await _yield_initial_build_frame()
		else:
			_add_doll_part("Sword", weapon_id, w_prefix, "Equip", true)
			await _yield_initial_build_frame()

	var disable_shield: bool = false
	if weapon_id > 0 and ItemLoader:
		disable_shield = int(ItemLoader.get_item_property(weapon_id, "no_shield", 0)) == 1
	if shield_id > 0 and not disable_shield:
		_add_doll_part("Shield", shield_id, "Shield", "Equip", true)
		await _yield_initial_build_frame()

func build_mecha_model_initial_async(race: String, hair: String, hair_color: int, bonnet_id: int, body_id: int, leg_id: int, weapon_id: int, mecha_type: String = "MS") -> void :
	current_mode = Mode.MECHA
	current_race = race
	current_hair = hair
	current_hair_color_index = hair_color
	current_mecha_type = mecha_type

	current_mecha_body_spr_id = 0
	if body_id > 0 and ItemLoader:
		current_mecha_body_spr_id = int(ItemLoader.get_item_property(body_id, "spr_id", 0))

	clear_dolls()

	if current_mecha_type == "MA":
		_add_doll_part("MSBody", body_id, "MA", "MA_Standard", false)
		await _yield_initial_build_frame()
		if USE_RESOURCE_MECHA_HEAD_ANCHORS:
			if current_race == "Robot":
				if bonnet_id > 0:
					_add_doll_part("Bonnet", bonnet_id, "Bonnet", "MechaHead", false)
				else:
					_add_doll_part("Bonnet", -1, "Bonnet00", "MechaHead", false)
				await _yield_initial_build_frame()
			else:
				_add_doll_part("Head", 0, "Head", "MechaHead", false)
				await _yield_initial_build_frame()
				_add_doll_part("Hair", -1, current_hair, "MechaHead", false)
				await _yield_initial_build_frame()
				if bonnet_id > 0:
					_add_doll_part("Bonnet", bonnet_id, "Bonnet", "MechaHead", false)
					await _yield_initial_build_frame()
	else:
		_add_doll_part("MSBody", body_id, "MSBody", "MechaStandard", false)
		await _yield_initial_build_frame()
		_add_doll_part("MSLeg", leg_id, "MSLeg", "MechaStandard", false)
		await _yield_initial_build_frame()
		_add_doll_part("L-MSArm", weapon_id, "L-MSArm", "MechaStandard", false)
		await _yield_initial_build_frame()
		_add_doll_part("R-MSArm", weapon_id, "R-MSArm", "MechaStandard", false)
		await _yield_initial_build_frame()

		if current_race == "Robot":
			if bonnet_id > 0:
				_add_doll_part("Bonnet", bonnet_id, "Bonnet", "MechaHead", false)
			else:
				_add_doll_part("Bonnet", -1, "Bonnet00", "MechaHead", false)
			await _yield_initial_build_frame()
		else:
			_add_doll_part("Head", 0, "Head", "MechaHead", false)
			await _yield_initial_build_frame()
			_add_doll_part("Hair", -1, current_hair, "MechaHead", false)
			await _yield_initial_build_frame()
			if bonnet_id > 0:
				_add_doll_part("Bonnet", bonnet_id, "Bonnet", "MechaHead", false)
				await _yield_initial_build_frame()

func build_human_model(race: String, hair: String, hair_color: int, body_color: int, equips: Dictionary) -> void :
	current_mode = Mode.HUMAN
	current_race = race
	current_hair = hair
	current_hair_color_index = hair_color
	current_body_color_index = body_color
	current_equips = equips

	clear_dolls()

	_add_doll_part("Shadow", 0, "Shadow", "Shadow", false)

	if current_race != "Robot":
		_add_doll_part("Head", 0, "Head", "Standard", false)
		_add_doll_part("Hair", -1, current_hair, "Standard", false)
		_add_doll_part("Leg", 0, "Leg", "Standard", false)
		_add_doll_part("L-Arm", 0, "L-Arm", "Standard", false)
		_add_doll_part("R-Arm", 0, "R-Arm", "Standard", false)

	_add_doll_part("Body", 0, "Body", "Standard", false)
	_update_equipment_layers()


func build_mecha_model(race: String, hair: String, hair_color: int, bonnet_id: int, body_id: int, leg_id: int, weapon_id: int, mecha_type: String = "MS") -> void :
	current_mode = Mode.MECHA
	current_race = race
	current_hair = hair
	current_hair_color_index = hair_color
	current_mecha_type = mecha_type


	current_mecha_body_spr_id = 0
	if body_id > 0 and ItemLoader:
		current_mecha_body_spr_id = ItemLoader.get_item_property(body_id, "spr_id", 0)

	clear_dolls()

	if current_mecha_type == "MA":

		_add_doll_part("MSBody", body_id, "MA", "MA_Standard", false)
		if USE_RESOURCE_MECHA_HEAD_ANCHORS:
			if current_race == "Robot":
				if bonnet_id > 0:
					_add_doll_part("Bonnet", bonnet_id, "Bonnet", "MechaHead", false)
				else:
					_add_doll_part("Bonnet", -1, "Bonnet00", "MechaHead", false)
			else:
				_add_doll_part("Head", 0, "Head", "MechaHead", false)
				_add_doll_part("Hair", -1, current_hair, "MechaHead", false)
				if bonnet_id > 0:
					_add_doll_part("Bonnet", bonnet_id, "Bonnet", "MechaHead", false)
	else:

		_add_doll_part("MSBody", body_id, "MSBody", "MechaStandard", false)
		_add_doll_part("MSLeg", leg_id, "MSLeg", "MechaStandard", false)
		_add_doll_part("L-MSArm", weapon_id, "L-MSArm", "MechaStandard", false)
		_add_doll_part("R-MSArm", weapon_id, "R-MSArm", "MechaStandard", false)


		if current_race == "Robot":
			if bonnet_id > 0:
				_add_doll_part("Bonnet", bonnet_id, "Bonnet", "MechaHead", false)
			else:
				_add_doll_part("Bonnet", -1, "Bonnet00", "MechaHead", false)
		else:
			_add_doll_part("Head", 0, "Head", "MechaHead", false)
			_add_doll_part("Hair", -1, current_hair, "MechaHead", false)
			if bonnet_id > 0:
				_add_doll_part("Bonnet", bonnet_id, "Bonnet", "MechaHead", false)


func update_paper_doll_part(part_index: int, new_item_id: int) -> void :
	current_equips[part_index] = new_item_id


	if current_mode == Mode.MECHA and current_mecha_type == "MA" and not USE_RESOURCE_MECHA_HEAD_ANCHORS: return
	if current_mode == Mode.MECHA and part_index != 0: return

	var node_name = ""; var prefix = ""; var category = "Equip"; var is_weapon = false

	if current_mode == Mode.MECHA and part_index == 0:
		category = "MechaHead"

	match part_index:
		0: node_name = "Bonnet";prefix = "Bonnet"
		1: node_name = "EquipL-Arm";prefix = "L-Arm"
		2: node_name = "EquipR-Arm";prefix = "R-Arm"
		3: node_name = "EquipBody";prefix = "Body"
		4: node_name = "EquipLeg";prefix = "Leg"
		5:
			node_name = "Sword";is_weapon = true;prefix = "Sword"
			if new_item_id > 0 and ItemLoader:
				var n4 = str(ItemLoader.get_item_property(new_item_id, "note_4", "")).strip_edges()
				if n4 != "": prefix = n4
		6: node_name = "Shield";prefix = "Shield";is_weapon = true
		7: node_name = "Back";prefix = "Back"

	if node_name == "": return

	var disable_shield = false
	var weapon_id = current_equips.get(5, 0)
	if weapon_id > 0 and ItemLoader:

		var no_shield_val = int(ItemLoader.get_item_property(weapon_id, "no_shield", 0))
		if no_shield_val == 1:
			disable_shield = true

	if node_name == "Shield" and disable_shield:
		new_item_id = 0

	if current_race == "Yas" and node_name == "EquipBody":
		if not _is_transmog_upper_armor(new_item_id):
			new_item_id = -1

	var is_armor_part = part_index in [1, 2, 3, 4]
	if new_item_id < 0 or (new_item_id == 0 and not is_armor_part):
		if current_race == "Robot" and part_index == 0:
			if current_mode == Mode.HUMAN:
				_add_doll_part("Bonnet", -1, "Bonnet000", "Equip", false)
			else:
				_add_doll_part("Bonnet", -1, "Bonnet00", "MechaHead", false)
		else:

			if part_index == 5:
				if doll_sprites.has("Sword"):
					doll_sprites["Sword"].free()
					doll_sprites.erase("Sword")
				if doll_sprites.has("L-Gun"):
					doll_sprites["L-Gun"].free()
					doll_sprites.erase("L-Gun")
				if doll_sprites.has("R-Gun"):
					doll_sprites["R-Gun"].free()
					doll_sprites.erase("R-Gun")
			elif doll_sprites.has(node_name):
				doll_sprites[node_name].free()
				doll_sprites.erase(node_name)
	else:

		if part_index == 5:

			if doll_sprites.has("Sword"):
				doll_sprites["Sword"].free()
				doll_sprites.erase("Sword")
			if doll_sprites.has("L-Gun"):
				doll_sprites["L-Gun"].free()
				doll_sprites.erase("L-Gun")
			if doll_sprites.has("R-Gun"):
				doll_sprites["R-Gun"].free()
				doll_sprites.erase("R-Gun")

			if prefix == "TwinGun":
				_add_doll_part("L-Gun", new_item_id, "L_TwinGun", category, is_weapon)
				_add_doll_part("R-Gun", new_item_id, "R_TwinGun", category, is_weapon)
			else:
				_add_doll_part("Sword", new_item_id, prefix, category, is_weapon)
		else:
			_add_doll_part(node_name, new_item_id, prefix, category, is_weapon)


	if part_index == 5:
		if disable_shield:
			if doll_sprites.has("Shield"):
				doll_sprites["Shield"].free()
				doll_sprites.erase("Shield")
		else:
			var shield_id = current_equips.get(6, 0)
			if shield_id > 0 and not doll_sprites.has("Shield"):
				_add_doll_part("Shield", shield_id, "Shield", "Equip", true)



func _update_equipment_layers() -> void :
	var bonnet_id = current_equips.get(0, 0)
	var left_id = current_equips.get(1, 0)
	var right_id = current_equips.get(2, 0)
	var upper_id = current_equips.get(3, 0)
	var lower_id = current_equips.get(4, 0)
	var weapon_id = current_equips.get(5, 0)
	var shield_id = current_equips.get(6, 0)
	var back_id = current_equips.get(7, 0)

	if upper_id >= 0:
		if current_race == "Yas" and not _is_transmog_upper_armor(upper_id):
			pass
		else:
			_add_doll_part("EquipBody", upper_id, "Body", "Equip", false)

	if lower_id >= 0: _add_doll_part("EquipLeg", lower_id, "Leg", "Equip", false)
	if left_id >= 0: _add_doll_part("EquipL-Arm", left_id, "L-Arm", "Equip", false)
	if right_id >= 0: _add_doll_part("EquipR-Arm", right_id, "R-Arm", "Equip", false)

	if bonnet_id > 0:
		_add_doll_part("Bonnet", bonnet_id, "Bonnet", "Equip", false)
	elif current_race == "Robot":
		_add_doll_part("Bonnet", -1, "Bonnet000", "Equip", false)

	if back_id > 0: _add_doll_part("Back", back_id, "Back", "Equip", false)

	if weapon_id > 0:
		var w_prefix = "Sword"
		if ItemLoader:
			var n4 = str(ItemLoader.get_item_property(weapon_id, "note_4", "")).strip_edges()
			if n4 != "": w_prefix = n4


		if w_prefix == "TwinGun":
			_add_doll_part("L-Gun", weapon_id, "L_TwinGun", "Equip", true)
			_add_doll_part("R-Gun", weapon_id, "R_TwinGun", "Equip", true)
		else:
			_add_doll_part("Sword", weapon_id, w_prefix, "Equip", true)

	var disable_shield = false
	if weapon_id > 0 and ItemLoader:
		var no_shield_val = int(ItemLoader.get_item_property(weapon_id, "no_shield", 0))
		if no_shield_val == 1:
			disable_shield = true

	if shield_id > 0 and not disable_shield:
		_add_doll_part("Shield", shield_id, "Shield", "Equip", true)

func _add_doll_part(node_key: String, item_id: int, prefix: String, category: String, is_weapon: bool) -> void :
	var race_prefix = RACE_PREFIX_MAP.get(current_race, "B")
	var part_file_name = ""

	var cat_path = category
	var is_mecha_part = (category in ["MechaStandard", "MechaHead", "MA_Standard"])
	if not is_mecha_part:
		cat_path = race_prefix + "/" + category

	var is_armor_zero = (item_id == 0 and node_key in ["EquipBody", "EquipLeg", "EquipL-Arm", "EquipR-Arm", "MSBody", "MSLeg", "L-MSArm", "R-MSArm"])

	var is_spr_zero_armor = false
	if is_armor_zero:
		is_spr_zero_armor = true
	elif item_id > 0 and ItemLoader and ItemLoader.has_method("get_item_property"):
		var spr_id = ItemLoader.get_item_property(item_id, "spr_id", -1)
		if spr_id == 0:
			is_spr_zero_armor = true


	if item_id > 0:
		var spr_id = ItemLoader.get_item_property(item_id, "spr_id", -1) if ItemLoader else -1
		if spr_id >= 0:
			if category == "MA_Standard":

				part_file_name = "%s_%s%02d" % [race_prefix, prefix, spr_id]
			elif is_mecha_part:
				part_file_name = "%s-%s%02d" % [race_prefix, prefix, spr_id]
			else:
				part_file_name = "%s-%s%03d" % [race_prefix, prefix, spr_id]
		else: return
	elif is_armor_zero:
		if category == "MA_Standard":
			part_file_name = "%s_%s00" % [race_prefix, prefix]
		elif is_mecha_part:
			part_file_name = "%s-%s00" % [race_prefix, prefix]
		else:
			part_file_name = "%s-%s000" % [race_prefix, prefix]
	elif item_id == 0:
		part_file_name = "%s-%s" % [race_prefix, prefix]
		if not is_mecha_part:
			cat_path = race_prefix + ("/Shadow" if node_key == "Shadow" else "/Standard")
	elif item_id == -1:

		if is_mecha_part and prefix.begins_with("Hair") and prefix.length() >= 7:
			part_file_name = "%s-Hair%s" % [race_prefix, prefix.substr(5, 2)]
		else:
			part_file_name = "%s-%s" % [race_prefix, prefix]

	if part_file_name == "": return

	var sf = DollLoader.get_sprite_frames(cat_path, part_file_name) if DollLoader else null
	var original_sf = sf
	var fallback_sf = null

	if current_race in ["Girl", "Yas"] or category == "MechaStandard":
		var boy_prefix = "B"
		var fallback_cat_path = boy_prefix + "/" + category if not is_mecha_part else category



		var fallback_file_name = boy_prefix + part_file_name.substr(1)

		if DollLoader:
			fallback_sf = DollLoader.get_sprite_frames(fallback_cat_path, fallback_file_name)

		if not sf and fallback_sf:
			sf = fallback_sf
			original_sf = fallback_sf

	if current_mode == Mode.HUMAN:
		var timing_controller = get_player_controller(current_race)
		if sf:
			sf = _get_controller_timed_frames(sf, timing_controller, HUMAN_TIMING_ACTION_PREFIXES, true, true)
		if original_sf:
			original_sf = _get_controller_timed_frames(original_sf, timing_controller, HUMAN_TIMING_ACTION_PREFIXES, true, true)
		if fallback_sf:
			fallback_sf = _get_controller_timed_frames(fallback_sf, timing_controller, HUMAN_TIMING_ACTION_PREFIXES, true, true)
	else:
		var timing_controller = "ma:MA%02d" % current_mecha_body_spr_id if current_mecha_type == "MA" else "mecha:MS"
		if sf:
			sf = _get_controller_timed_frames(sf, timing_controller, MECHA_TIMING_ACTION_PREFIXES, true, true)
		if original_sf:
			original_sf = _get_controller_timed_frames(original_sf, timing_controller, MECHA_TIMING_ACTION_PREFIXES, true, true)
		if fallback_sf:
			fallback_sf = _get_controller_timed_frames(fallback_sf, timing_controller, MECHA_TIMING_ACTION_PREFIXES, true, true)

	if sf:
		var anim_sprite: AnimatedSprite2D = null
		if doll_sprites.has(node_key):
			anim_sprite = doll_sprites[node_key]
			anim_sprite.sprite_frames = sf
		else:
			anim_sprite = AnimatedSprite2D.new()
			anim_sprite.sprite_frames = sf
			anim_sprite.centered = false
			anim_sprite.name = node_key
			add_child(anim_sprite)
			doll_sprites[node_key] = anim_sprite
			if node_key == "Body" or (current_race == "Robot" and node_key == "Bonnet") or node_key == "MSBody":
				anim_sprite.animation_finished.connect(_on_body_animation_finished)
			anim_sprite.frame_changed.connect(_on_sprite_frame_changed.bind(node_key))

		anim_sprite.set_meta("original_sf", original_sf)
		anim_sprite.set_meta("is_spr_zero_armor", is_spr_zero_armor)
		if fallback_sf and fallback_sf != original_sf:
			anim_sprite.set_meta("fallback_sf", fallback_sf)
		else:
			if anim_sprite.has_meta("fallback_sf"): anim_sprite.remove_meta("fallback_sf")

		var chosen_index: int = 0
		var chosen_effect_type: int = 0
		var apply_shader: bool = false

		if node_key == "Hair":
			chosen_index = current_hair_color_index
			apply_shader = true
		elif item_id <= 0 and node_key in ["Body", "Leg", "L-Arm", "R-Arm"]:
			chosen_index = current_body_color_index
			apply_shader = true
		elif (item_id > 0 or is_armor_zero) and node_key != "Shadow":
			chosen_index = 0
			if item_id > 0 and ItemLoader and ItemLoader.has_method("get_item_property"):
				chosen_index = ItemLoader.get_item_property(item_id, "color_index", 0)
				chosen_effect_type = ItemLoader.get_item_property(item_id, "effect_type", 0)
			apply_shader = true

		if apply_shader:
			# Materials are immutable after setup; reuse for identical hue/effect
			# across multiple dolls and parts without changing shader visuals.
			anim_sprite.material = get_shared_palette_material(chosen_index, chosen_effect_type)
		else: anim_sprite.material = null

func _on_sprite_frame_changed(part_key: String) -> void :
	if not doll_sprites.has(part_key): return
	var sprite_node: AnimatedSprite2D = doll_sprites[part_key]
	if not sprite_node.sprite_frames: return
	_play_controller_frame_audio(part_key, sprite_node)

	var current_texture = sprite_node.sprite_frames.get_frame_texture(sprite_node.animation, sprite_node.frame)
	if current_texture:
		sprite_node.centered = false
		var offset_vector = Vector2.ZERO
		var tex_w = current_texture.get_width()


		if current_texture.has_meta("old_offset"):
			offset_vector = current_texture.get_meta("old_offset")
			if current_texture is AtlasTexture:
				tex_w = current_texture.region.size.x


		var target_x = offset_vector.x - tex_w if sprite_node.flip_h else - offset_vector.x
		var target_y = - offset_vector.y


		if current_mode == Mode.MECHA and part_key in ["Head", "Hair", "Bonnet"]:
			var applied_resource_anchor: = false
			if USE_RESOURCE_MECHA_HEAD_ANCHORS and doll_sprites.has("MSBody"):
				var anchor_data: = _get_mecha_head_anchor(doll_sprites["MSBody"])
				if anchor_data.has("offset"):
					var anchor_offset: Vector2 = anchor_data["offset"]
					target_x += - anchor_offset.x if sprite_node.flip_h else anchor_offset.x
					target_y += anchor_offset.y
					applied_resource_anchor = true
			if not applied_resource_anchor and current_mecha_type == "MS" and doll_sprites.has("MSLeg"):
				var msleg_sprite: AnimatedSprite2D = doll_sprites["MSLeg"]


				var race_prefix = RACE_PREFIX_MAP.get(current_race, "B")
				var base_leg_file = "B-MSLeg00"


				var base_sf = DollLoader.get_sprite_frames("MechaStandard", base_leg_file) if DollLoader else null

				if base_sf and base_sf.has_animation(msleg_sprite.animation):

					var safe_frame = clampi(msleg_sprite.frame, 0, base_sf.get_frame_count(msleg_sprite.animation) - 1)
					var base_tex = base_sf.get_frame_texture(msleg_sprite.animation, safe_frame)

					if base_tex and base_tex.has_meta("old_offset"):

						target_y -= base_tex.get_meta("old_offset").y

						var anim_name = msleg_sprite.animation
						var dir_str = anim_name.substr(anim_name.length() - 1, 1)
						var head_y_fix = 27.0

						match dir_str:
							"1", "7": head_y_fix = 29.0
							"2", "6": head_y_fix = 30.0
							"3", "5": head_y_fix = 35.0
							"4": head_y_fix = 35.0
							"8": head_y_fix = 33.0
							_: head_y_fix = 27.0

						target_y -= head_y_fix
			elif not applied_resource_anchor and current_mecha_type == "MA":
				sprite_node.visible = false

		sprite_node.offset = Vector2(target_x, target_y)


	if part_key == "MSBody" and current_mode == Mode.MECHA:
		for head_part in ["Head", "Hair", "Bonnet"]:
			if doll_sprites.has(head_part):
				_on_sprite_frame_changed(head_part)


func update_animation(anim_state: String, anim_dir: String, flip: bool, _move_speed: float, atk_speed: float) -> void :
	if doll_sprites.is_empty(): return

	var force_stop_frame_0 = false


	if current_mode == Mode.MECHA:
		if anim_state == "buff":
			anim_state = "walk"
		elif anim_state == "crouch":
			anim_state = "idle"
			force_stop_frame_0 = true
		elif anim_state == "guard" or anim_state.begins_with("emote_"):
			anim_state = "idle"
		else:
			var valid_mecha_states = ["idle", "walk", "attack_na", "attack_nb"]
			if not (anim_state in valid_mecha_states):
				anim_state = "idle"

	last_anim_state = anim_state
	var target_anim = anim_state + "_" + anim_dir
	var part_finals = {}
	var ref_frame = -1
	var ref_progress = 0.0

	for part_key in doll_sprites.keys():
		var sprite_node: AnimatedSprite2D = doll_sprites[part_key]
		var original_sf = sprite_node.get_meta("original_sf") if sprite_node.has_meta("original_sf") else sprite_node.sprite_frames
		if not original_sf:
			part_finals[part_key] = ""
			continue

		var final_anim = target_anim
		var fallback_sf = sprite_node.get_meta("fallback_sf") if sprite_node.has_meta("fallback_sf") else null
		var target_sf = original_sf

		var allow_boy_fallback = true
		if current_race in ["Girl", "Yas"]:
			if part_key in ["Head", "Hair", "Body", "Leg", "L-Arm", "R-Arm"]:
				if current_race == "Yas" and anim_state == "death":
					allow_boy_fallback = true
				else:
					allow_boy_fallback = false
			elif sprite_node.has_meta("is_spr_zero_armor") and sprite_node.get_meta("is_spr_zero_armor") == true:
				allow_boy_fallback = false

		if not target_sf.has_animation(final_anim):
			if allow_boy_fallback and fallback_sf and fallback_sf.has_animation(final_anim):
				target_sf = fallback_sf
			else:
				var found_dir_sub = false
				if final_anim.length() > 2:
					var base_state = final_anim.substr(0, final_anim.length() - 1)
					var current_dir_char = final_anim.substr(final_anim.length() - 1, 1)
					var dir_fallback_map = {"4": ["3", "1", "2"], "8": ["1", "2", "3"], "1": ["2", "8", "3"], "2": ["1", "3", "8"], "3": ["2", "4", "1"]}

					if dir_fallback_map.has(current_dir_char):
						for try_dir in dir_fallback_map[current_dir_char]:
							var try_anim = base_state + try_dir
							if target_sf.has_animation(try_anim):
								final_anim = try_anim;found_dir_sub = true;break
							elif allow_boy_fallback and fallback_sf and fallback_sf.has_animation(try_anim):
								target_sf = fallback_sf;final_anim = try_anim;found_dir_sub = true;break

				if not found_dir_sub:
					if anim_state == "death":
						part_finals[part_key] = ""
						continue

					var found_substitute = false
					if part_key in ["Shadow", "Body", "Head", "Leg", "L-Arm", "R-Arm", "Hair", "Back", "Bonnet", "EquipBody", "EquipLeg", "EquipL-Arm", "EquipR-Arm", "MSBody", "MSLeg", "L-MSArm", "R-MSArm"]:
						for sub_a in ["idle_" + anim_dir, "idle_1"]:
							if target_sf.has_animation(sub_a):
								final_anim = sub_a;found_substitute = true;break
							elif allow_boy_fallback and fallback_sf and fallback_sf.has_animation(sub_a):
								target_sf = fallback_sf;final_anim = sub_a;found_substitute = true;break

					if not found_substitute:
						if part_key == "Shadow":
							var available = target_sf.get_animation_names()
							if available.size() > 0: final_anim = available[0]
							else: part_finals[part_key] = "";continue
						else:
							part_finals[part_key] = ""
							continue

		part_finals[part_key] = final_anim

		if sprite_node.is_playing() and sprite_node.animation == final_anim:
			if ref_frame == -1 or part_key in ["Body", "MSBody"] or (current_race == "Robot" and part_key == "Bonnet"):
				ref_frame = sprite_node.frame
				ref_progress = sprite_node.frame_progress

	var raw_prefix = ANIM_TO_RAW_PREFIX.get(anim_state, "NS")
	if anim_state.begins_with("emote_"): raw_prefix = "Z" + anim_state.substr(6, 1).to_upper()
	var raw_matrix_key = raw_prefix + anim_dir

	var current_order = DEFAULT_ORDER
	if current_mode == Mode.MECHA and MECHA_ORDER_DATA.has(raw_matrix_key):
		current_order = MECHA_ORDER_DATA[raw_matrix_key]
	elif current_race == "Robot" and current_mode == Mode.HUMAN and ROBOT_ORDER_DATA.has(raw_matrix_key):
		current_order = ROBOT_ORDER_DATA[raw_matrix_key]
	else:
		current_order = RAW_ORDER_DATA.get(raw_matrix_key, DEFAULT_ORDER)

	for part_key in doll_sprites.keys():
		var sprite_node: AnimatedSprite2D = doll_sprites[part_key]
		var final_anim = part_finals.get(part_key, "")

		if final_anim == "":
			sprite_node.visible = false
			continue

		var target_sf = sprite_node.get_meta("original_sf") if sprite_node.has_meta("original_sf") else sprite_node.sprite_frames
		var fallback_sf = sprite_node.get_meta("fallback_sf") if sprite_node.has_meta("fallback_sf") else null
		var allow_boy_fallback = not (sprite_node.has_meta("is_spr_zero_armor") and sprite_node.get_meta("is_spr_zero_armor") == true)
		if current_mode == Mode.MECHA and current_race == "Yas" and part_key in ["Head", "Hair"]:
			allow_boy_fallback = false

		if part_key in ["MSBody", "MSLeg", "L-MSArm", "R-MSArm"]:
			allow_boy_fallback = true

		if allow_boy_fallback and not target_sf.has_animation(final_anim) and fallback_sf and fallback_sf.has_animation(final_anim):
			target_sf = fallback_sf

		if sprite_node.sprite_frames != target_sf: sprite_node.sprite_frames = target_sf

		var non_loop_states = [
			"buff", "guard", "hit_weapon_melee", "hit_weapon_gun"
		]
		if anim_state in non_loop_states and target_sf.has_animation(final_anim):
			target_sf.set_animation_loop(final_anim, false)

		sprite_node.visible = true
		sprite_node.flip_h = flip

		if force_stop_frame_0:
			if sprite_node.animation != final_anim:
				sprite_node.animation = final_anim
			sprite_node.set_frame_and_progress(0, 0.0)
			sprite_node.stop()
		else:
			if (sprite_node.animation != final_anim) or not sprite_node.is_playing():
				sprite_node.play(final_anim)


				if ref_frame >= 0 and ref_frame < sprite_node.sprite_frames.get_frame_count(final_anim):
					sprite_node.set_frame_and_progress(ref_frame, ref_progress)
			if anim_state.begins_with("attack_"):
				sprite_node.speed_scale = max(atk_speed, 10.0) / 100.0
			elif anim_state == "walk":
				sprite_node.speed_scale = 1.0
			else:
				sprite_node.speed_scale = 1.0

		_on_sprite_frame_changed(part_key)

	var current_index = 0
	for raw_part in current_order:
		var key = "Hair" if raw_part.begins_with("Hair") else raw_part

		if doll_sprites.has(key):
			move_child(doll_sprites[key], current_index)
			current_index += 1

		if doll_sprites.has("Equip" + key):
			move_child(doll_sprites["Equip" + key], current_index)
			current_index += 1

func _on_body_animation_finished() -> void :
	body_animation_finished.emit()

func _play_controller_frame_audio(part_key: String, sprite_node: AnimatedSprite2D) -> void :
	var controller = ""
	var human_controller_part = "Bonnet" if current_race == "Robot" else "Body"
	if current_mode == Mode.HUMAN and part_key == human_controller_part:
		controller = get_player_controller(current_race)
	elif current_mode == Mode.MECHA and part_key == "MSBody":
		controller = "ma:MA%02d" % current_mecha_body_spr_id if current_mecha_type == "MA" else "mecha:MS"
	else:
		return
	if sprite_node.sprite_frames.has_meta(ACT_CONTROLLER_TIMELINES_META):
		if _play_controller_timeline_audio(controller, sprite_node):
			return
	var action = last_anim_state
	if action not in ["walk", "attack_na", "attack_nb"]:
		_audio_sequence_key = ""
		_audio_played_cues.clear()
		_audio_last_frame = -1
		return
	var sequence_key = "%s:%s:%s" % [controller, action, sprite_node.animation]
	if sequence_key != _audio_sequence_key:
		_audio_sequence_key = sequence_key
		_audio_played_cues.clear()
		_audio_last_frame = -1
	if action == "walk" and _audio_last_frame >= 0 and sprite_node.frame < _audio_last_frame:
		_audio_played_cues.clear()
	_audio_last_frame = sprite_node.frame
	var cue_key = "%s:%d" % [action, sprite_node.frame]
	if _audio_played_cues.has(cue_key):
		return
	var candidates = AudioManager.get_se_controller_event_candidates(controller, action, sprite_node.frame)
	if candidates.is_empty():
		return
	if AudioManager.play_se_candidates(candidates):
		_audio_played_cues[cue_key] = true

func _play_controller_timeline_audio(controller: String, sprite_node: AnimatedSprite2D) -> bool:
	var raw_action = str(ANIM_TO_RAW_PREFIX.get(last_anim_state, ""))
	if last_anim_state.begins_with("emote_"):
		raw_action = "Z" + last_anim_state.substr(6, 1).to_upper()
	var timelines = sprite_node.sprite_frames.get_meta(ACT_CONTROLLER_TIMELINES_META, {})
	if raw_action.is_empty() or not timelines is Dictionary or not timelines.has(raw_action):
		return false
	var timeline = timelines[raw_action]
	if not timeline is Array:
		return false
	var sequence_key = "%s:%s:%s" % [controller, raw_action, sprite_node.animation]
	if sequence_key != _audio_sequence_key:
		_audio_sequence_key = sequence_key
		_audio_played_cues.clear()
		_audio_last_frame = -1
	if raw_action in ["NS", "NR"] and _audio_last_frame >= 0 and sprite_node.frame < _audio_last_frame:
		_audio_played_cues.clear()
	_audio_last_frame = sprite_node.frame
	if sprite_node.frame < 0 or sprite_node.frame >= timeline.size():
		return true
	var cue_key = "%s:%d" % [raw_action, sprite_node.frame]
	if _audio_played_cues.has(cue_key):
		return true
	var entry = timeline[sprite_node.frame]
	if not entry is Dictionary:
		return true
	var played = false
	var audio_cues = entry.get("audio", [])
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
		_audio_played_cues[cue_key] = true
	return true

func _get_controller_timed_frames(source_frames: SpriteFrames, controller: String, action_prefixes: Dictionary, use_effective_tick_rate: bool = false, skip_movement_timing: bool = false) -> SpriteFrames:
	if not AnimationTimingManager or not AnimationTimingManager.is_loaded():
		return source_frames
	var tick_rate = AnimationTimingManager.get_effective_tick_rate_hz() if use_effective_tick_rate else float(AnimationTimingManager.get_tick_rate_hz())
	if tick_rate <= 0.0:
		return source_frames
	var cache_key = "%s:%d:%s" % [controller, source_frames.get_instance_id(), skip_movement_timing]
	if _shared_controller_timed_frames_cache.has(cache_key):
		return _shared_controller_timed_frames_cache[cache_key]
	# Duplicating SpriteFrames can be expensive when a PaperDoll has many
	# layers. Most items have no applicable ACT timeline; only clone after the
	# first valid timeline is found, preserving exactly the previous result.
	var timed_frames: SpriteFrames = null
	var applied_timelines: Dictionary = {}
	for raw_action in action_prefixes:
		if skip_movement_timing and raw_action == "NR":
			continue
		var timeline = AnimationTimingManager.get_controller_timeline(controller, raw_action)
		if timeline.is_empty():
			continue
		var animation_names: Array[StringName] = []
		for animation_name in source_frames.get_animation_names():
			for prefix in action_prefixes[raw_action]:
				if str(animation_name).begins_with(str(prefix)):
					animation_names.append(animation_name)
					break
		if animation_names.is_empty() or not _controller_timeline_fits(source_frames, animation_names, timeline, controller, raw_action):
			continue
		if timed_frames == null:
			timed_frames = source_frames.duplicate() as SpriteFrames
			if timed_frames == null:
				return source_frames
		for animation_name in animation_names:
			var textures: Array[Texture2D] = []
			for entry in timeline:
				textures.append(source_frames.get_frame_texture(animation_name, int(entry["frame"])))
			var looped = source_frames.get_animation_loop(animation_name)
			timed_frames.clear(animation_name)
			timed_frames.set_animation_speed(animation_name, tick_rate)
			timed_frames.set_animation_loop(animation_name, looped)
			for index in timeline.size():
				timed_frames.add_frame(animation_name, textures[index], float(timeline[index]["ticks"]))
		applied_timelines[raw_action] = timeline
	if applied_timelines.is_empty():
		_shared_controller_timed_frames_cache[cache_key] = source_frames
		return source_frames
	timed_frames.set_meta(ACT_CONTROLLER_TIMELINES_META, applied_timelines)
	_shared_controller_timed_frames_cache[cache_key] = timed_frames
	return timed_frames

func _controller_timeline_fits(source_frames: SpriteFrames, animation_names: Array[StringName], timeline: Array, controller: String, raw_action: String) -> bool:
	for entry in timeline:
		if not entry is Dictionary:
			return false
		var sprite_frame = int(entry.get("frame", -1))
		var ticks = int(entry.get("ticks", 0))
		if sprite_frame < 0 or ticks <= 0:
			return false
		for animation_name in animation_names:
			if sprite_frame >= source_frames.get_frame_count(animation_name):
				push_warning("[PaperDollEngine] ACT timing frame %d exceeds %s action %s animation %s" % [sprite_frame, controller, raw_action, animation_name])
				return false
	return true


func _is_transmog_upper_armor(item_id: int) -> bool:
	if item_id <= 0:
		return false

	if item_id >= 19272 and item_id <= 19347:
		return true

	return false
