
extends Node

var bgm_player: AudioStreamPlayer
var current_map_id: int = -1
var current_bgm_volume_pct: float = GameConstants.SETTING_DEFAULT_BGM_VOLUME
var current_se_volume_pct: float = GameConstants.SETTING_DEFAULT_SE_VOLUME

const SE_BASE_PATH = "res://Assets/Audio/SE/"
const SE_EVENT_MANIFEST_PATH = "res://Assets/Audio/SEEventManifest.json"
const SE_PLAYER_POOL_SIZE = 16
const SE_BASE_VOLUME_SCALE = 0.25
const MIN_PITCH_SCALE = 0.01
const UI_HOVER_SE_ID = 43
const UI_BUTTON_PRESS_SE_ID = 44
const UI_INTERFACE_ACTIVATION_SE_ID = 46
const UI_FEEDBACK_BOUND_META: StringName = &"audio_ui_feedback_bound"
const UI_HOVER_DISABLED_META: StringName = &"audio_ui_hover_disabled"
const UI_PRESS_DISABLED_META: StringName = &"audio_ui_press_disabled"
const UI_INTERFACE_ACTIVATION_DISABLED_META: StringName = &"audio_ui_interface_activation_disabled"
const MAP_BGM_CACHE_LIMIT: = 3

var se_players: Array[AudioStreamPlayer] = []
var se_cache: Dictionary = {}
var _missing_se_names: Dictionary = {}
var _se_player_play_order: Dictionary = {}
var _se_player_volume_scales: Dictionary = {}
var _next_se_play_order: int = 0
var _last_se_played_at_msec: Dictionary = {}
const SE_REPEAT_COOLDOWN_MSEC = 90
const SE_REPEAT_COOLDOWN_UI_MSEC = 20
var _se_event_index: Dictionary = {}
var _se_controller_event_index: Dictionary = {}
var _map_bgm_cache: Dictionary = {}
var _map_bgm_cache_order: Array[String] = []
var _bgm_request_serial: int = 0
var _pending_bgm_path: String = ""

const LOGIN_BGM_PATH = "res://Assets/Audio/BGM/Silent_Memory.mp3"




const MAP_BGM_DICT: Dictionary = {
	1: "res://Assets/Audio/BGM/What_Do_You_Want_b_KPM_522_18.mp3", 
	2: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	4: "res://Assets/Audio/BGM/Jesu_Joy_of_Mans_Desiring_KPM_41_48.mp3", 
	5: "res://Assets/Audio/BGM/Kids_World_1_BRE_08_1.mp3", 
	6: "res://Assets/Audio/BGM/Jesu_Joy_of_Mans_Desiring_KPM_41_48.mp3", 
	7: "res://Assets/Audio/BGM/Top_Of_The_Morning_BRF_15_18.mp3", 
	8: "res://Assets/Audio/BGM/Sweet_Beat_BRE_10_4.mp3", 
	9: "res://Assets/Audio/BGM/Sunshine_KPM_207_20.mp3", 
	10: "res://Assets/Audio/BGM/Sunshine_KPM_207_20.mp3", 
	11: "res://Assets/Audio/BGM/Kids_World_1_BRE_08_1.mp3", 
	12: "res://Assets/Audio/BGM/You_Got_It_a_KPM_474_6.mp3", 
	13: "res://Assets/Audio/BGM/Treasure_Trove_1_BRE_09_10.mp3", 
	14: "res://Assets/Audio/BGM/Keyboard_Wizzard_BRF_10_15.mp3", 
	15: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	16: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	17: "res://Assets/Audio/BGM/The_Game_Zone_a_KPM_279_27.mp3", 
	18: "res://Assets/Audio/BGM/The_Game_Zone_a_KPM_279_27.mp3", 
	19: "res://Assets/Audio/BGM/Summertime_a_KPM_297_18.mp3", 
	20: "res://Assets/Audio/BGM/ToonTime_BRE_09_9.mp3", 
	21: "res://Assets/Audio/BGM/Cartoon_Time_1_BRE_08_8.mp3", 
	22: "res://Assets/Audio/BGM/Life_is_for_Living_KPM_247_24.mp3", 
	23: "res://Assets/Audio/BGM/Life_is_for_Living_KPM_247_24.mp3", 
	25: "res://Assets/Audio/BGM/Fun_Theme_Park_1_BRE_09_3.mp3", 
	26: "res://Assets/Audio/BGM/Talking_Toucan_BRE_09_7.mp3", 
	27: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	28: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	29: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	30: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	31: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	32: "res://Assets/Audio/BGM/Man_With_A_Mission_BRJ_49_2.mp3", 
	33: "res://Assets/Audio/BGM/Midnight_Siren_BRJ_50_3.mp3", 
	34: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	35: "res://Assets/Audio/BGM/Fear_Of_The_Unknown_BRJ_56_8.mp3", 
	38: "res://Assets/Audio/BGM/Coconut_Calypso_BRE_11_19.mp3", 
	39: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	40: "res://Assets/Audio/BGM/Sunshine_KPM_207_20.mp3", 
	42: "res://Assets/Audio/BGM/Gaw_Blimey_2_BRF_12_10.mp3", 
	43: "res://Assets/Audio/BGM/One_Step_Till_Tomorrow_b_KPM_522_10.mp3", 
	44: "res://Assets/Audio/BGM/Skate_City_a_KPM_80_20.mp3", 
	45: "res://Assets/Audio/BGM/Swing_It_a_KPM_250_1.mp3", 
	46: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	47: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	48: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	49: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	50: "res://Assets/Audio/BGM/Sunshine_KPM_207_20.mp3", 
	51: "res://Assets/Audio/BGM/Burning_Fires_BIGS_002_32.mp3", 
	52: "res://Assets/Audio/BGM/In_The_Line_Of_Fire_BIGS_002_31.mp3", 
	53: "res://Assets/Audio/BGM/025.mp3", 
	54: "res://Assets/Audio/BGM/Sunshine_KPM_207_20.mp3", 
	55: "res://Assets/Audio/BGM/Sunshine_KPM_207_20.mp3", 
	56: "res://Assets/Audio/BGM/Sunshine_KPM_207_20.mp3", 
	57: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	58: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	59: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	60: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	61: "res://Assets/Audio/BGM/Kids_World_1_BRE_08_1.mp3", 
	62: "res://Assets/Audio/BGM/Man_With_A_Mission_BRJ_49_2.mp3", 
	63: "res://Assets/Audio/BGM/Fear_Of_The_Unknown_BRJ_56_8.mp3", 
	64: "res://Assets/Audio/BGM/Man_With_A_Mission_BRJ_49_2.mp3", 
	65: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	66: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	67: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	68: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	69: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	70: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	71: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	73: "res://Assets/Audio/BGM/WouldRatherSea.mp3", 
	74: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	75: "res://Assets/Audio/BGM/024.mp3", 
	76: "res://Assets/Audio/BGM/013.mp3", 
	77: "res://Assets/Audio/BGM/Midnight_Siren_BRJ_50_3.mp3", 
	78: "res://Assets/Audio/BGM/001.mp3", 
	79: "res://Assets/Audio/BGM/Fear_Of_The_Unknown_BRJ_56_8.mp3", 
	81: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	82: "res://Assets/Audio/BGM/ToonTime_BRE_09_9.mp3", 
	83: "res://Assets/Audio/BGM/026.mp3", 
	84: "res://Assets/Audio/BGM/Critical_Moment_BIGS_002_11.mp3", 
	85: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	86: "res://Assets/Audio/BGM/021.mp3", 
	87: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	91: "res://Assets/Audio/BGM/021.mp3", 
	92: "res://Assets/Audio/BGM/016.mp3", 
	93: "res://Assets/Audio/BGM/021.mp3", 
	94: "res://Assets/Audio/BGM/016.mp3", 
	95: "res://Assets/Audio/BGM/021.mp3", 
	96: "res://Assets/Audio/BGM/016.mp3", 
	97: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	99: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	100: "res://Assets/Audio/BGM/In_The_Line_Of_Fire_BIGS_002_31.mp3", 
	101: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	102: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	103: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	104: "res://Assets/Audio/BGM/Coconut_Calypso_BRE_11_19.mp3", 
	105: "res://Assets/Audio/BGM/Coconut_Calypso_BRE_11_19.mp3", 
	106: "res://Assets/Audio/BGM/Coconut_Calypso_BRE_11_19.mp3", 
	107: "res://Assets/Audio/BGM/Fun_Theme_Park_1_BRE_09_3.mp3", 
	108: "res://Assets/Audio/BGM/Fun_Theme_Park_1_BRE_09_3.mp3", 
	109: "res://Assets/Audio/BGM/Fun_Theme_Park_1_BRE_09_3.mp3", 
	110: "res://Assets/Audio/BGM/Top_Of_The_Morning_BRF_15_18.mp3", 
	111: "res://Assets/Audio/BGM/Top_Of_The_Morning_BRF_15_18.mp3", 
	112: "res://Assets/Audio/BGM/Top_Of_The_Morning_BRF_15_18.mp3", 
	113: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	114: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	115: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	116: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	117: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	118: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	119: "res://Assets/Audio/BGM/Summertime_a_KPM_297_18.mp3", 
	120: "res://Assets/Audio/BGM/Summertime_a_KPM_297_18.mp3", 
	121: "res://Assets/Audio/BGM/Summertime_a_KPM_297_18.mp3", 
	122: "res://Assets/Audio/BGM/Treasure_Trove_1_BRE_09_10.mp3", 
	123: "res://Assets/Audio/BGM/Treasure_Trove_1_BRE_09_10.mp3", 
	124: "res://Assets/Audio/BGM/Treasure_Trove_1_BRE_09_10.mp3", 
	125: "res://Assets/Audio/BGM/Sunshine_KPM_207_20.mp3", 
	126: "res://Assets/Audio/BGM/Sunshine_KPM_207_20.mp3", 
	127: "res://Assets/Audio/BGM/Sunshine_KPM_207_20.mp3", 
	128: "res://Assets/Audio/BGM/Sunshine_KPM_207_20.mp3", 
	129: "res://Assets/Audio/BGM/Sunshine_KPM_207_20.mp3", 
	130: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	131: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	132: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	133: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	134: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	135: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	136: "res://Assets/Audio/BGM/WouldRatherSea.mp3", 
	137: "res://Assets/Audio/BGM/WouldRatherSea.mp3", 
	138: "res://Assets/Audio/BGM/WouldRatherSea.mp3", 
	139: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	140: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	141: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	142: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	143: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	144: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	145: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	146: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	147: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	148: "res://Assets/Audio/BGM/Fear_Of_The_Unknown_BRJ_56_8.mp3", 
	149: "res://Assets/Audio/BGM/Fear_Of_The_Unknown_BRJ_56_8.mp3", 
	150: "res://Assets/Audio/BGM/Fear_Of_The_Unknown_BRJ_56_8.mp3", 
	151: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	152: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	153: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	154: "res://Assets/Audio/BGM/Kids_World_1_BRE_08_1.mp3", 
	155: "res://Assets/Audio/BGM/Kids_World_1_BRE_08_1.mp3", 
	156: "res://Assets/Audio/BGM/Kids_World_1_BRE_08_1.mp3", 
	157: "res://Assets/Audio/BGM/Man_With_A_Mission_BRJ_49_2.mp3", 
	158: "res://Assets/Audio/BGM/Midnight_Siren_BRJ_50_3.mp3", 
	159: "res://Assets/Audio/BGM/Midnight_Siren_BRJ_50_3.mp3", 
	160: "res://Assets/Audio/BGM/Midnight_Siren_BRJ_50_3.mp3", 
	161: "res://Assets/Audio/BGM/Midnight_Siren_BRJ_50_3.mp3", 
	162: "res://Assets/Audio/BGM/Midnight_Siren_BRJ_50_3.mp3", 
	163: "res://Assets/Audio/BGM/Midnight_Siren_BRJ_50_3.mp3", 
	164: "res://Assets/Audio/BGM/Midnight_Siren_BRJ_50_3.mp3", 
	165: "res://Assets/Audio/BGM/In_The_Line_Of_Fire_BIGS_002_31.mp3", 
	166: "res://Assets/Audio/BGM/In_The_Line_Of_Fire_BIGS_002_31.mp3", 
	167: "res://Assets/Audio/BGM/In_The_Line_Of_Fire_BIGS_002_31.mp3", 
	168: "res://Assets/Audio/BGM/In_The_Line_Of_Fire_BIGS_002_31.mp3", 
	169: "res://Assets/Audio/BGM/In_The_Line_Of_Fire_BIGS_002_31.mp3", 
	170: "res://Assets/Audio/BGM/In_The_Line_Of_Fire_BIGS_002_31.mp3", 
	171: "res://Assets/Audio/BGM/In_The_Line_Of_Fire_BIGS_002_31.mp3", 
	172: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	173: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3", 
	174: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	175: "res://Assets/Audio/BGM/ToonTime_BRE_09_9.mp3", 
	176: "res://Assets/Audio/BGM/The_Only_One_b_KPM_522_20.mp3", 
	177: "res://Assets/Audio/BGM/ToonTime_BRE_09_9.mp3", 
	178: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	179: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	180: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	181: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	182: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	183: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	184: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	185: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	186: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	187: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	188: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	189: "res://Assets/Audio/BGM/School_Bus_2_BRE_09_2.mp3", 
	190: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	191: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	192: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	193: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	194: "res://Assets/Audio/BGM/Midnight_Siren_BRJ_50_3.mp3", 
	195: "res://Assets/Audio/BGM/Midnight_Siren_BRJ_50_3.mp3", 
	196: "res://Assets/Audio/BGM/Midnight_Siren_BRJ_50_3.mp3", 
	197: "res://Assets/Audio/BGM/Midnight_Siren_BRJ_50_3.mp3", 
	198: "res://Assets/Audio/BGM/Burning_Fires_BIGS_002_32.mp3", 
	199: "res://Assets/Audio/BGM/Midnight_Siren_BRJ_50_3.mp3", 
	200: "res://Assets/Audio/BGM/024.mp3", 
	201: "res://Assets/Audio/BGM/024.mp3", 
	202: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	203: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	204: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	205: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	206: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	207: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	208: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	209: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	210: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	211: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	212: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	213: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	214: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	215: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	216: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	217: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	218: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	219: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	220: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	221: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	222: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	223: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	224: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	225: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	226: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	227: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	228: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	229: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	230: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	231: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	232: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	233: "res://Assets/Audio/BGM/Speed_1_BRE_08_11.mp3", 
	234: "res://Assets/Audio/BGM/Jesu_Joy_of_Mans_Desiring_KPM_41_48.mp3", 
	235: "res://Assets/Audio/BGM/Jesu_Joy_of_Mans_Desiring_KPM_41_48.mp3", 
	237: "res://Assets/Audio/BGM/Zenith_BRH_92_15.mp3", 
	238: "res://Assets/Audio/BGM/World_Domination_BRJ_57_5.mp3"
}

func _ready() -> void :
	bgm_player = AudioStreamPlayer.new()
	bgm_player.bus = "Master"
	add_child(bgm_player)
	_create_se_player_pool()
	_load_se_event_manifest()
	get_tree().node_added.connect(_on_scene_tree_node_added)
	call_deferred("_bind_existing_ui_buttons")

	_load_settings()




func bind_button_ui_feedback(button: BaseButton) -> void :
	if not is_instance_valid(button):
		return
	if bool(button.get_meta(UI_FEEDBACK_BOUND_META, false)):
		return

	button.mouse_entered.connect(_on_ui_button_mouse_entered.bind(button))
	button.button_down.connect(_on_ui_button_down.bind(button))
	button.set_meta(UI_FEEDBACK_BOUND_META, true)

func configure_button_ui_feedback(button: BaseButton, hover_enabled: bool = true, press_enabled: bool = true) -> void :
	if not is_instance_valid(button):
		return
	button.set_meta(UI_HOVER_DISABLED_META, not hover_enabled)
	button.set_meta(UI_PRESS_DISABLED_META, not press_enabled)
	bind_button_ui_feedback(button)

func configure_interface_activation_feedback(interface: CanvasItem, enabled: bool = true) -> void :
	if not is_instance_valid(interface):
		return
	interface.set_meta(UI_INTERFACE_ACTIVATION_DISABLED_META, not enabled)

func play_interface_activation_feedback(interface: CanvasItem) -> bool:
	if not is_instance_valid(interface) or not interface.is_inside_tree() or not interface.is_visible_in_tree():
		return false
	if bool(interface.get_meta(UI_INTERFACE_ACTIVATION_DISABLED_META, false)):
		return false
	return play_se(UI_INTERFACE_ACTIVATION_SE_ID)

func _on_scene_tree_node_added(node: Node) -> void :
	if node is BaseButton:
		bind_button_ui_feedback(node)

func _bind_existing_ui_buttons() -> void :
	_bind_ui_buttons_recursive(get_tree().root)

func _bind_ui_buttons_recursive(node: Node) -> void :
	if node is BaseButton:
		bind_button_ui_feedback(node)
	for child in node.get_children():
		_bind_ui_buttons_recursive(child)

func _on_ui_button_mouse_entered(button: BaseButton) -> void :
	if not _can_play_button_ui_feedback(button):
		return
	if bool(button.get_meta(UI_HOVER_DISABLED_META, false)):
		return
	play_se(UI_HOVER_SE_ID)

func _on_ui_button_down(button: BaseButton) -> void :
	if not _can_play_button_ui_feedback(button):
		return
	if bool(button.get_meta(UI_PRESS_DISABLED_META, false)):
		return
	play_se(UI_BUTTON_PRESS_SE_ID)

func _can_play_button_ui_feedback(button: BaseButton) -> bool:
	return is_instance_valid(button) and button.is_inside_tree() and button.is_visible_in_tree() and not button.disabled

func _create_se_player_pool() -> void :
	for i in range(SE_PLAYER_POOL_SIZE):
		var player = AudioStreamPlayer.new()
		player.name = "SEPlayer%d" % i
		player.bus = "Master"
		add_child(player)
		se_players.append(player)
		_se_player_play_order[player] = -1
		_se_player_volume_scales[player] = 1.0




func play_se(se_id: int, volume_scale: float = 1.0, pitch_scale: float = 1.0) -> bool:
	if se_id < 0 or se_id > 9999:
		return false
	return play_se_by_name("SE%04d" % se_id, volume_scale, pitch_scale)

func play_se_by_name(se_name: String, volume_scale: float = 1.0, pitch_scale: float = 1.0) -> bool:
	var normalized_name = _normalize_se_name(se_name)
	if normalized_name.is_empty():
		return false

	var now_msec := Time.get_ticks_msec()
	var repeat_cooldown := SE_REPEAT_COOLDOWN_MSEC
	if normalized_name in ["SE%04d" % UI_HOVER_SE_ID, "SE%04d" % UI_BUTTON_PRESS_SE_ID, "SE%04d" % UI_INTERFACE_ACTIVATION_SE_ID]:
		repeat_cooldown = SE_REPEAT_COOLDOWN_UI_MSEC

	var last_played := int(_last_se_played_at_msec.get(normalized_name, -1000000))
	if now_msec - last_played < repeat_cooldown:
		return false

	var stream = _load_se_by_name(normalized_name)
	if stream == null:
		return false

	var player = _get_available_se_player()
	if player == null:
		return false

	var safe_volume_scale = maxf(volume_scale, 0.0)
	var safe_pitch_scale = maxf(pitch_scale, MIN_PITCH_SCALE)
	player.stop()
	player.stream = stream
	player.pitch_scale = safe_pitch_scale
	_se_player_volume_scales[player] = safe_volume_scale
	_apply_se_player_volume(player)
	_next_se_play_order += 1
	_se_player_play_order[player] = _next_se_play_order
	player.play()
	_last_se_played_at_msec[normalized_name] = now_msec
	return true

func play_se_candidates(candidates: Array[String], volume_scale: float = 1.0, pitch_scale: float = 1.0) -> bool:
	var available_candidates: Array[String] = []
	for candidate in candidates:
		var normalized_name = _normalize_se_name(candidate)
		if not normalized_name.is_empty() and is_se_name_available(normalized_name):
			available_candidates.append(normalized_name)
	if available_candidates.is_empty():
		return false
	return play_se_by_name(available_candidates.pick_random(), volume_scale, pitch_scale)

func preload_se(se_id: int) -> bool:
	if se_id < 0 or se_id > 9999:
		return false
	return preload_se_by_name("SE%04d" % se_id)

func preload_se_by_name(se_name: String) -> bool:
	var normalized_name = _normalize_se_name(se_name)
	if normalized_name.is_empty():
		return false
	return _load_se_by_name(normalized_name) != null

func is_se_available(se_id: int) -> bool:
	if se_id < 0 or se_id > 9999:
		return false
	return is_se_name_available("SE%04d" % se_id)

func is_se_name_available(se_name: String) -> bool:
	var normalized_name = _normalize_se_name(se_name)
	if normalized_name.is_empty():
		return false
	return ResourceLoader.exists(_get_se_path(normalized_name))

func stop_all_se() -> void :
	for player in se_players:
		player.stop()
		_se_player_play_order[player] = -1

func clear_se_cache() -> void :
	se_cache.clear()
	_missing_se_names.clear()
	_last_se_played_at_msec.clear()

func clear_runtime_cache() -> void :
	stop_all_se()
	clear_se_cache()
	_map_bgm_cache.clear()
	_map_bgm_cache_order.clear()

func set_se_volume(percentage: float) -> void :
	current_se_volume_pct = clampf(percentage, 0.0, 100.0)
	_apply_se_volume()
	save_settings()

func get_se_volume() -> float:
	return current_se_volume_pct

func get_se_event_candidates(visual_resource_path: String, action: String, frame_index: int) -> Array[String]:
	var result: Array[String] = []
	if visual_resource_path.is_empty() or action.is_empty() or frame_index < 0:
		return result
	var resource_actions = _se_event_index.get(visual_resource_path, {})
	if not resource_actions is Dictionary:
		return result
	var action_frames = resource_actions.get(action, {})
	if not action_frames is Dictionary:
		return result
	var candidates = action_frames.get(str(frame_index), [])
	if not candidates is Array:
		return result
	for candidate in candidates:
		var normalized_name = _normalize_se_name(str(candidate))
		if not normalized_name.is_empty():
			result.append(normalized_name)
	return result

func has_se_event(visual_resource_path: String, action: String, frame_index: int) -> bool:
	return not get_se_event_candidates(visual_resource_path, action, frame_index).is_empty()

func get_se_controller_event_candidates(controller: String, action: String, frame_index: int) -> Array[String]:
	var result: Array[String] = []
	var actions = _se_controller_event_index.get(controller, {})
	if not actions is Dictionary:
		return result
	var frames = actions.get(action, {})
	if not frames is Dictionary:
		return result
	var candidates = frames.get(str(frame_index), [])
	if candidates is Array:
		for candidate in candidates:
			var normalized_name = _normalize_se_name(str(candidate))
			if not normalized_name.is_empty():
				result.append(normalized_name)
	return result

func _load_se_event_manifest() -> void :
	_se_event_index.clear()
	_se_controller_event_index.clear()
	if not FileAccess.file_exists(SE_EVENT_MANIFEST_PATH):
		push_warning("[AudioManager] SE event manifest not found: %s" % SE_EVENT_MANIFEST_PATH)
		return
	var file = FileAccess.open(SE_EVENT_MANIFEST_PATH, FileAccess.READ)
	if file == null:
		push_warning("[AudioManager] Unable to open SE event manifest: %s" % SE_EVENT_MANIFEST_PATH)
		return
	var parsed = JSON.parse_string(file.get_as_text())
	if not parsed is Dictionary or int(parsed.get("format_version", 0)) != 1:
		push_warning("[AudioManager] Invalid SE event manifest: %s" % SE_EVENT_MANIFEST_PATH)
		return
	var entries = parsed.get("entries", [])
	if not entries is Array:
		push_warning("[AudioManager] Invalid SE event manifest entries: %s" % SE_EVENT_MANIFEST_PATH)
		return
	for entry in entries:
		if not entry is Dictionary:
			continue
		var resource_path = str(entry.get("resource", ""))
		var actions = entry.get("actions", {})
		if resource_path.is_empty() or not actions is Dictionary:
			continue
		_se_event_index[resource_path] = actions
	var controller_entries = parsed.get("controller_entries", [])
	if controller_entries is Array:
		for entry in controller_entries:
			if entry is Dictionary:
				var controller = str(entry.get("controller", ""))
				var actions = entry.get("actions", {})
				if not controller.is_empty() and actions is Dictionary:
					_se_controller_event_index[controller] = actions

func _normalize_se_name(se_name: String) -> String:
	var normalized_name = se_name.strip_edges().to_upper()
	if normalized_name.length() != 6:
		return ""
	if normalized_name.substr(0, 2) != "SE" and normalized_name.substr(0, 2) != "EN":
		return ""

	var number_part = normalized_name.substr(2, 4)
	for character in number_part:
		if character < "0" or character > "9":
			return ""
	return normalized_name

func _get_se_path(se_name: String) -> String:
	return SE_BASE_PATH + se_name + ".wav"

func _load_se_by_name(se_name: String) -> AudioStream:
	if se_cache.has(se_name):
		return se_cache[se_name] as AudioStream
	if _missing_se_names.has(se_name):
		return null

	var se_path = _get_se_path(se_name)
	if not ResourceLoader.exists(se_path):
		push_warning("[AudioManager] 找不到音效檔案: %s" % se_path)
		_missing_se_names[se_name] = true
		return null

	var stream = load(se_path) as AudioStream
	if stream == null:
		push_warning("[AudioManager] 無法載入音效檔案: %s" % se_path)
		_missing_se_names[se_name] = true
		return null

	se_cache[se_name] = stream
	return stream

func _get_available_se_player() -> AudioStreamPlayer:
	for player in se_players:
		if not player.playing:
			return player

	if se_players.is_empty():
		return null

	var oldest_player = se_players[0]
	var oldest_order = int(_se_player_play_order.get(oldest_player, -1))
	for player in se_players:
		var play_order = int(_se_player_play_order.get(player, -1))
		if play_order < oldest_order:
			oldest_player = player
			oldest_order = play_order
	return oldest_player

func _apply_se_volume() -> void :
	for player in se_players:
		_apply_se_player_volume(player)

func _apply_se_player_volume(player: AudioStreamPlayer) -> void :
	if not is_instance_valid(player):
		return
	var volume_scale = float(_se_player_volume_scales.get(player, 1.0))
	var linear_volume = (current_se_volume_pct / 100.0) * volume_scale * SE_BASE_VOLUME_SCALE
	player.volume_db = -80.0 if linear_volume <= 0.0 else linear_to_db(linear_volume)




func play_login_bgm() -> void :
	_bgm_request_serial += 1
	_pending_bgm_path = ""
	var stream = load(LOGIN_BGM_PATH) as AudioStream
	if stream:

		if bgm_player.stream == stream and bgm_player.playing:
			return

		bgm_player.stream = stream
		bgm_player.play()
		current_map_id = -1
	else:
		print("❌ [AudioManager] 找不到登入音樂檔案: ", LOGIN_BGM_PATH)




func play_map_bgm(map_id: int) -> void :
	_bgm_request_serial += 1
	var request_id: = _bgm_request_serial
	var load_started_usec: = Time.get_ticks_usec()


	if not MAP_BGM_DICT.has(map_id):
		_pending_bgm_path = ""
		print("🔈 [AudioManager] 地圖 ", map_id, " 沒有設定 BGM，停止播放音樂。")
		bgm_player.stop()
		current_map_id = -1
		return


	if current_map_id == map_id and bgm_player.playing:
		_pending_bgm_path = ""
		return


	var bgm_path: String = MAP_BGM_DICT[map_id]
	if _map_bgm_cache.has(bgm_path):
		_pending_bgm_path = ""
		_touch_map_bgm_cache(bgm_path)
		_play_loaded_map_bgm(map_id, bgm_path, _map_bgm_cache[bgm_path] as AudioStream, request_id, load_started_usec, true)
		return

	if not ResourceLoader.exists(bgm_path):
		_pending_bgm_path = ""
		print("❌ [AudioManager] 找不到音訊檔案: ", bgm_path)
		return

	_pending_bgm_path = bgm_path
	var request_error: = ResourceLoader.load_threaded_request(bgm_path, "AudioStream")
	if request_error != OK:
		var existing_status: = ResourceLoader.load_threaded_get_status(bgm_path)
		if existing_status != ResourceLoader.THREAD_LOAD_IN_PROGRESS and existing_status != ResourceLoader.THREAD_LOAD_LOADED:
			_pending_bgm_path = ""
			print("❌ [AudioManager] 無法啟動背景音樂載入，錯誤碼 ", request_error, ": ", bgm_path)
			return

	while request_id == _bgm_request_serial:
		var status: = ResourceLoader.load_threaded_get_status(bgm_path)
		if status == ResourceLoader.THREAD_LOAD_IN_PROGRESS:
			await get_tree().process_frame
			continue
		if status == ResourceLoader.THREAD_LOAD_LOADED:
			var stream: = ResourceLoader.load_threaded_get(bgm_path) as AudioStream
			if request_id != _bgm_request_serial:
				return
			_pending_bgm_path = ""
			if stream:
				_cache_map_bgm(bgm_path, stream)
				_play_loaded_map_bgm(map_id, bgm_path, stream, request_id, load_started_usec, false)
			else:
				print("❌ [AudioManager] 背景音樂資源型別錯誤: ", bgm_path)
			return
		_pending_bgm_path = ""
		print("❌ [AudioManager] 背景音樂載入失敗，狀態碼 ", status, ": ", bgm_path)
		return

func _play_loaded_map_bgm(map_id: int, bgm_path: String, stream: AudioStream, request_id: int, load_started_usec: int, cache_hit: bool) -> void :
	if request_id != _bgm_request_serial or not is_instance_valid(bgm_player) or stream == null:
		return
	bgm_player.stream = stream
	bgm_player.play()
	current_map_id = map_id
	if GameManager and GameManager.get_active_map_transition_id() > 0:
		GameManager.trace_map_diagnostic("bgm_load_completed", {
			"target_map": map_id, 
			"path": bgm_path, 
			"cache_hit": cache_hit, 
			"duration_ms": float(Time.get_ticks_usec() - load_started_usec) / 1000.0, 
		})

func _cache_map_bgm(path: String, stream: AudioStream) -> void :
	_map_bgm_cache[path] = stream
	_touch_map_bgm_cache(path)
	while _map_bgm_cache_order.size() > MAP_BGM_CACHE_LIMIT:
		var evicted_path: = str(_map_bgm_cache_order.pop_front())
		_map_bgm_cache.erase(evicted_path)

func _touch_map_bgm_cache(path: String) -> void :
	_map_bgm_cache_order.erase(path)
	_map_bgm_cache_order.append(path)




func stop_bgm() -> void :
	_bgm_request_serial += 1
	_pending_bgm_path = ""
	bgm_player.stop()
	current_map_id = -1




func set_bgm_volume(percentage: float) -> void :
	current_bgm_volume_pct = percentage
	_apply_volume(percentage)
	save_settings()

func _apply_volume(percentage: float) -> void :
	if not is_instance_valid(bgm_player): return
	if percentage <= 0.0:
		bgm_player.volume_db = -80.0
	else:
		bgm_player.volume_db = linear_to_db(percentage / 100.0)




func _load_settings() -> void :
	var config = ConfigFile.new()
	var err = config.load(GameConstants.CLIENT_SETTINGS_PATH)
	if err == OK:
		current_bgm_volume_pct = config.get_value(
			GameConstants.SETTING_SECTION_AUDIO, 
			GameConstants.SETTING_KEY_BGM_VOLUME, 
			GameConstants.SETTING_DEFAULT_BGM_VOLUME
		)
		current_se_volume_pct = clampf(float(config.get_value(
			GameConstants.SETTING_SECTION_AUDIO, 
			GameConstants.SETTING_KEY_SE_VOLUME, 
			GameConstants.SETTING_DEFAULT_SE_VOLUME
		)), 0.0, 100.0)
	else:
		current_bgm_volume_pct = GameConstants.SETTING_DEFAULT_BGM_VOLUME
		current_se_volume_pct = GameConstants.SETTING_DEFAULT_SE_VOLUME

	_apply_volume(current_bgm_volume_pct)
	_apply_se_volume()

func save_settings() -> void :
	var config = ConfigFile.new()
	config.load(GameConstants.CLIENT_SETTINGS_PATH)
	config.set_value(
		GameConstants.SETTING_SECTION_AUDIO, 
		GameConstants.SETTING_KEY_BGM_VOLUME, 
		current_bgm_volume_pct
	)
	config.set_value(
		GameConstants.SETTING_SECTION_AUDIO, 
		GameConstants.SETTING_KEY_SE_VOLUME, 
		current_se_volume_pct
	)
	config.save(GameConstants.CLIENT_SETTINGS_PATH)
