
class_name GameConstants


const CLIENT_SETTINGS_PATH = "user://client_settings.cfg"

const SETTING_SECTION_AUDIO = "Audio"
const SETTING_KEY_BGM_VOLUME = "bgm_volume"
const SETTING_DEFAULT_BGM_VOLUME = 50.0
const SETTING_KEY_SE_VOLUME = "se_volume"
const SETTING_DEFAULT_SE_VOLUME = 50.0

const SETTING_SECTION_GAME = "Game"
const SETTING_KEY_PET_COMBAT_MODE = "pet_combat_mode"
const SETTING_DEFAULT_PET_COMBAT_MODE = 1
const SETTING_KEY_QUALITY_ENHANCEMENT_ENABLED = "quality_enhancement_enabled"
const SETTING_DEFAULT_QUALITY_ENHANCEMENT_ENABLED = false
const SETTING_KEY_PERFORMANCE_LOAD_MODE = "performance_load_mode"
const SETTING_DEFAULT_PERFORMANCE_LOAD_MODE = ClientPerformanceProfile.LOAD_BALANCED
const SETTING_KEY_COMBAT_RENDER_MODE = "combat_render_mode"
const SETTING_DEFAULT_COMBAT_RENDER_MODE = ClientPerformanceProfile.COMBAT_NORMAL
const SETTING_KEY_SIMPLIFY_PLAYERS_MODE = "simplify_players_mode"
const SETTING_DEFAULT_SIMPLIFY_PLAYERS_MODE = ClientPerformanceProfile.SIMPLIFY_AUTO
const SETTING_KEY_SIMPLIFY_MONSTERS_MODE = "simplify_monsters_mode"
const SETTING_DEFAULT_SIMPLIFY_MONSTERS_MODE = ClientPerformanceProfile.SIMPLIFY_AUTO


const DEFAULT_MAP_ID = "map15"
const DEFAULT_SPAWN_POSITION = Vector2(10000, 10000)


const CHARACTER_MAX_NAME_WIDTH = 12
const CHARACTER_PASSWORD_LENGTH = 4
const CHARACTER_INIT_TOTAL_STAT_POINTS = 4


const RACE_ID_MAP = {0: "Boy", 1: "Girl", 2: "Migu", 3: "Robot", 4: "Yas"}
const RACE_PREFIX_MAP = {"Boy": "B", "Girl": "G", "Migu": "A", "Robot": "R", "Yas": "Y"}
const DIR_STRINGS = ["6", "7", "8", "1", "2", "3", "4", "5"]


const COLOR_NAME_WHITE = Color(1.0, 1.0, 1.0, 1.0)
const COLOR_NAME_NORMAL = Color(0.02, 0.075, 0.592, 1.0)
const COLOR_NAME_PK = Color(0.196, 0.196, 0.196, 1.0)
const COLOR_NAME_RED_MAN = Color(1.0, 0.296, 0.27, 1.0)
const COLOR_NAME_PARTY = Color(0.239, 0.973, 0.886, 1.0)
const COLOR_NAME_PURPLE = Color(0.692, 0.191, 0.694, 1.0)



const COLOR_DMG_MISS = Color(1.0, 1.0, 1.0, 1.0)
const COLOR_DMG_MISS_OUTLINE = Color(1.0, 0.541, 0.114, 1.0)
const COLOR_DMG_CRIT = Color(1.0, 0.541, 0.113, 1.0)
const COLOR_DMG_CRIT_OUTLINE = Color(1.0, 1.0, 1.0, 1.0)
const COLOR_DMG_NORMAL = Color(1.0, 1.0, 1.0, 1.0)
const COLOR_DMG_NORMAL_OUTLINE = Color(1.0, 0.541, 0.114, 1.0)


const COLOR_DMG_PET_MISS = Color(1.0, 1.0, 1.0, 1.0)
const COLOR_DMG_PET_MISS_OUTLINE = Color(0.925, 0.275, 0.498, 1.0)
const COLOR_DMG_PET_CRIT = Color(0.918, 0.275, 0.498, 1.0)
const COLOR_DMG_PET_CRIT_OUTLINE = Color(1.0, 1.0, 1.0, 1.0)
const COLOR_DMG_PET_NORMAL = Color(1.0, 1.0, 1.0, 1.0)
const COLOR_DMG_PET_NORMAL_OUTLINE = Color(0.927, 0.276, 0.499, 1.0)


const COLOR_DMG_MONSTER_MISS = Color(1.0, 1.0, 1.0, 1.0)
const COLOR_DMG_MONSTER_MISS_OUTLINE = Color(0.082, 0.52, 1.0, 1.0)
const COLOR_DMG_MONSTER_CRIT = Color(1.0, 0.289, 0.209, 1.0)
const COLOR_DMG_MONSTER_CRIT_OUTLINE = Color(1.0, 1.0, 1.0, 1.0)
const COLOR_DMG_MONSTER_NORMAL = Color(1.0, 1.0, 1.0, 1.0)
const COLOR_DMG_MONSTER_NORMAL_OUTLINE = Color(0.082, 0.52, 1.0, 1.0)

const CAREER_NAMES = {
	1: "靈鬥士", 2: "守護劍士", 3: "聖劍士", 4: "銃戰士", 
	5: "魔動使", 6: "守護使", 7: "聖靈使", 8: "魔銃使", 
	9: "道具使", 10: "噬魂使"
}
