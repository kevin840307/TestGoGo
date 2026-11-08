class_name ClientPerformanceProfile
extends RefCounted

const LOAD_QUALITY := 0
const LOAD_BALANCED := 1
const LOAD_LOW := 2
const LOAD_MULTIBOX := 3

const COMBAT_NORMAL := 0
const COMBAT_PVP := 1
const COMBAT_MASS := 2

static func sanitize_load_mode(value: int) -> int:
	return clampi(value, LOAD_QUALITY, LOAD_MULTIBOX)

static func sanitize_combat_mode(value: int) -> int:
	return clampi(value, COMBAT_NORMAL, COMBAT_MASS)

static func load_mode_name(value: int) -> String:
	match sanitize_load_mode(value):
		LOAD_QUALITY: return "quality"
		LOAD_BALANCED: return "balanced"
		LOAD_LOW: return "low"
		LOAD_MULTIBOX: return "multibox"
	return "balanced"

static func combat_mode_name(value: int) -> String:
	match sanitize_combat_mode(value):
		COMBAT_NORMAL: return "normal"
		COMBAT_PVP: return "pvp"
		COMBAT_MASS: return "mass"
	return "normal"

static func resolve(load_mode: int, combat_mode: int) -> Dictionary:
	load_mode = sanitize_load_mode(load_mode)
	combat_mode = sanitize_combat_mode(combat_mode)

	var profile := {
		"load_mode": load_mode,
		"combat_mode": combat_mode,
		"foreground_fps": 0,
		"background_fps": 30,
		"animation_interval": 0.0,
		"full_player_budget": 24,
		"full_monster_budget": 40,
		"effect_budget_percent": 100,
		"simplify_other_players": false,
		"simplify_noncritical_monsters": false,
		"hide_far_noncombat_monsters": false,
		"aggressive_cache_trim": false,
	}

	match load_mode:
		LOAD_QUALITY:
			pass
		LOAD_BALANCED:
			profile["foreground_fps"] = 120
			profile["background_fps"] = 20
			profile["animation_interval"] = 1.0 / 30.0
			profile["full_player_budget"] = 18
			profile["full_monster_budget"] = 30
			profile["effect_budget_percent"] = 80
		LOAD_LOW:
			profile["foreground_fps"] = 60
			profile["background_fps"] = 10
			profile["animation_interval"] = 1.0 / 20.0
			profile["full_player_budget"] = 12
			profile["full_monster_budget"] = 18
			profile["effect_budget_percent"] = 55
			profile["simplify_other_players"] = true
			profile["simplify_noncritical_monsters"] = true
		LOAD_MULTIBOX:
			profile["foreground_fps"] = 45
			profile["background_fps"] = 5
			profile["animation_interval"] = 1.0 / 15.0
			profile["full_player_budget"] = 8
			profile["full_monster_budget"] = 10
			profile["effect_budget_percent"] = 35
			profile["simplify_other_players"] = true
			profile["simplify_noncritical_monsters"] = true
			profile["hide_far_noncombat_monsters"] = true
			profile["aggressive_cache_trim"] = true

	match combat_mode:
		COMBAT_PVP:
			# PvP protects player readability and frame-time stability.
			profile["full_player_budget"] = maxi(int(profile["full_player_budget"]), 16)
			profile["full_monster_budget"] = mini(int(profile["full_monster_budget"]), 10)
			profile["effect_budget_percent"] = mini(int(profile["effect_budget_percent"]), 65)
			profile["simplify_other_players"] = load_mode >= LOAD_LOW
			profile["simplify_noncritical_monsters"] = true
			profile["hide_far_noncombat_monsters"] = true
		COMBAT_MASS:
			profile["full_player_budget"] = maxi(int(profile["full_player_budget"]), 20)
			profile["full_monster_budget"] = mini(int(profile["full_monster_budget"]), 8)
			profile["effect_budget_percent"] = mini(int(profile["effect_budget_percent"]), 45)
			profile["simplify_other_players"] = true
			profile["simplify_noncritical_monsters"] = true
			profile["hide_far_noncombat_monsters"] = true

	return profile
