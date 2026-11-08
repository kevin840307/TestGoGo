class_name ClientPerformanceProfile
extends RefCounted

const LOAD_QUALITY := 0
const LOAD_BALANCED := 1
const LOAD_LOW := 2
const LOAD_MULTIBOX := 3

const COMBAT_NORMAL := 0
const COMBAT_RESPONSE := 1
# Backward-compatible alias for existing callers/config values.
const COMBAT_PVP := COMBAT_RESPONSE
const COMBAT_MASS := 2

# Only three user-facing choices. Old persisted style values (3/4) are
# migrated to 2 so existing accounts keep simplification enabled.
const SIMPLIFY_AUTO := 0
const SIMPLIFY_NORMAL := 1
const SIMPLIFY_SIMPLIFIED := 2
# Legacy names kept for callers and older config files.
const SIMPLIFY_OFF := SIMPLIFY_NORMAL
const SIMPLIFY_ON := SIMPLIFY_SIMPLIFIED
const SIMPLIFY_PIXEL_A := SIMPLIFY_SIMPLIFIED
const SIMPLIFY_PIXEL_B := 3
const SIMPLIFY_MINIMAL := 4

static func sanitize_fps_limit(value: int) -> int:
	if value == 0 or value == 144:
		return value
	return clampi(int(round(float(value) / 5.0)) * 5, 30, 120)


static func normalize_fps_spin_step(previous: int, requested: int) -> int:
	# SpinBox increments by five; jump over the unsupported gaps at
	# 0 <-> 30 and 120 <-> 144 rather than trapping the arrow buttons.
	if previous == 0 and requested > 0 and requested <= 5:
		return 30
	if previous == 30 and requested >= 25 and requested < 30:
		return 0
	if previous == 120 and requested > 120 and requested <= 125:
		return 144
	if previous == 144 and requested >= 139 and requested < 144:
		return 120
	return sanitize_fps_limit(requested)


static func fps_options() -> Array[int]:
	var options: Array[int] = [0]
	for fps: int in range(30, 121, 5):
		options.append(fps)
	options.append(144)
	return options


static func sanitize_load_mode(value: int) -> int:
	return clampi(value, LOAD_QUALITY, LOAD_MULTIBOX)

static func sanitize_combat_mode(value: int) -> int:
	return clampi(value, COMBAT_NORMAL, COMBAT_MASS)

static func sanitize_simplify_mode(value: int) -> int:
	if value <= SIMPLIFY_AUTO:
		return SIMPLIFY_AUTO
	if value == SIMPLIFY_NORMAL:
		return SIMPLIFY_NORMAL
	return SIMPLIFY_SIMPLIFIED

static func simplify_mode_name(value: int) -> String:
	match sanitize_simplify_mode(value):
		SIMPLIFY_AUTO: return "auto"
		SIMPLIFY_NORMAL: return "normal"
		SIMPLIFY_SIMPLIFIED: return "simplified"
	return "auto"

static func simplify_mode_enabled(value: int) -> bool:
	return sanitize_simplify_mode(value) == SIMPLIFY_SIMPLIFIED

static func default_auto_style(_load_mode: int) -> int:
	# One shared procedural orb per entity type, regardless of load preset.
	return SIMPLIFY_SIMPLIFIED

static func apply_visual_overrides(
	profile: Dictionary,
	player_mode: int,
	monster_mode: int
) -> Dictionary:
	var result := profile.duplicate(true)
	player_mode = sanitize_simplify_mode(player_mode)
	monster_mode = sanitize_simplify_mode(monster_mode)
	if player_mode != SIMPLIFY_AUTO:
		result["simplify_other_players"] = simplify_mode_enabled(player_mode)
	if monster_mode != SIMPLIFY_AUTO:
		result["simplify_noncritical_monsters"] = simplify_mode_enabled(monster_mode)

	result["simplify_players_mode"] = player_mode
	result["simplify_monsters_mode"] = monster_mode
	result["player_simplify_style"] = (
		default_auto_style(int(result.get("load_mode", LOAD_BALANCED)))
		if player_mode == SIMPLIFY_AUTO
		else player_mode
	)
	result["monster_simplify_style"] = (
		default_auto_style(int(result.get("load_mode", LOAD_BALANCED)))
		if monster_mode == SIMPLIFY_AUTO
		else monster_mode
	)
	# Both player and monster simplified visuals are texture-free built-in
	# primitives. This flag remains for older diagnostics/callers.
	result["use_primitive_monsters"] = bool(
		result.get("simplify_noncritical_monsters", false)
	)
	return result

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
		COMBAT_RESPONSE: return "response"
		COMBAT_MASS: return "mass"
	return "normal"

static func resolve(load_mode: int, combat_mode: int) -> Dictionary:
	load_mode = sanitize_load_mode(load_mode)
	combat_mode = sanitize_combat_mode(combat_mode)

	var profile := {
		"load_mode": load_mode,
		"combat_mode": combat_mode,
		"foreground_fps": 60,
		"background_fps": 20,
		"animation_interval": 0.0,
		"full_player_budget": 24,
		"full_monster_budget": 40,
		"effect_budget_percent": 100,
		# Client-side visual allocation only, NEVER changes network/server AOI.
		"monster_aoi_enter_radius": 2200.0,
		"monster_aoi_exit_radius": 2600.0,
		"ground_aoi_enter_radius": 2400.0,
		"ground_aoi_exit_radius": 2800.0,
		"simplify_other_players": false,
		"simplify_noncritical_monsters": false,
		"use_primitive_monsters": false,
		"player_simplify_style": SIMPLIFY_PIXEL_A,
		"monster_simplify_style": SIMPLIFY_PIXEL_A,
		"hide_far_noncombat_monsters": false,
		"aggressive_cache_trim": false,
	}

	match load_mode:
		LOAD_QUALITY:
			pass
		LOAD_BALANCED:
			profile["animation_interval"] = 1.0 / 30.0
			profile["full_player_budget"] = 18
			profile["full_monster_budget"] = 30
			profile["effect_budget_percent"] = 75
			profile["monster_aoi_enter_radius"] = 1800.0
			profile["monster_aoi_exit_radius"] = 2200.0
			profile["ground_aoi_enter_radius"] = 2000.0
			profile["ground_aoi_exit_radius"] = 2400.0
		LOAD_LOW:
			profile["animation_interval"] = 1.0 / 20.0
			profile["full_player_budget"] = 12
			profile["full_monster_budget"] = 18
			profile["effect_budget_percent"] = 35
			profile["monster_aoi_enter_radius"] = 1250.0
			profile["monster_aoi_exit_radius"] = 1650.0
			profile["ground_aoi_enter_radius"] = 1450.0
			profile["ground_aoi_exit_radius"] = 1850.0
			profile["simplify_other_players"] = true
			profile["simplify_noncritical_monsters"] = true
		LOAD_MULTIBOX:
			profile["animation_interval"] = 1.0 / 15.0
			profile["full_player_budget"] = 8
			profile["full_monster_budget"] = 10
			profile["effect_budget_percent"] = 15
			profile["monster_aoi_enter_radius"] = 950.0
			profile["monster_aoi_exit_radius"] = 1350.0
			profile["ground_aoi_enter_radius"] = 1100.0
			profile["ground_aoi_exit_radius"] = 1500.0
			profile["simplify_other_players"] = true
			profile["simplify_noncritical_monsters"] = true
			profile["use_primitive_monsters"] = true
			profile["hide_far_noncombat_monsters"] = true
			profile["aggressive_cache_trim"] = true

	match combat_mode:
		COMBAT_RESPONSE:
			# Reaction-first mode preserves combat readability and frame-time
			# stability for focused fights regardless of whether the target is
			# another player, a boss, or a small group of monsters.
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

	# Direct resolve() callers receive a complete style as well. MultiBox uses
	# the cheapest minimal style; other automatic simplification keeps enough
	# pixel detail to remain readable.
	var auto_style := default_auto_style(load_mode)
	profile["player_simplify_style"] = auto_style
	profile["monster_simplify_style"] = auto_style
	profile["use_primitive_monsters"] = bool(
		profile.get("simplify_noncritical_monsters", false)
	)
	return profile
