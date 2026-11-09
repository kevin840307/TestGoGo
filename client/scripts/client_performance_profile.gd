class_name ClientPerformanceProfile
extends RefCounted

const LOAD_QUALITY := 0
const LOAD_BALANCED := 1
const LOAD_LOW := 2
# Legacy saved setting only. UI offers 0/1/2; saved 3 migrates to low resource.
const LOAD_MULTIBOX := 3

const COMBAT_NORMAL := 0
const COMBAT_RESPONSE := 1
# Backward-compatible alias for existing callers/config values.
const COMBAT_PVP := COMBAT_RESPONSE
# Legacy saved combat setting only. Saved 2 migrates to response first.
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

# Visibility is a separate, persistent choice; it never changes with quality.
const PLAYER_SHOW_ALL := 0
const PLAYER_SHOW_ENEMIES := 1
const PLAYER_SHOW_PARTY := 2
const PLAYER_SHOW_NONE := 3
const MONSTER_SHOW_ALL := 0
const MONSTER_HIDE_ORDINARY := 1

static func sanitize_player_visibility(value: int) -> int:
	return clampi(value, PLAYER_SHOW_ALL, PLAYER_SHOW_NONE)

static func sanitize_monster_visibility(value: int) -> int:
	return clampi(value, MONSTER_SHOW_ALL, MONSTER_HIDE_ORDINARY)

static func should_show_other_player(mode: int, same_party: bool, hostile: bool) -> bool:
	match sanitize_player_visibility(mode):
		PLAYER_SHOW_ENEMIES: return hostile
		PLAYER_SHOW_PARTY: return same_party
		PLAYER_SHOW_NONE: return false
	return true

static func should_show_ordinary_monster(mode: int, protected_monster: bool) -> bool:
	return protected_monster or sanitize_monster_visibility(mode) == MONSTER_SHOW_ALL

static func should_show_duplicate_drop(
	candidate_id: int, candidate: Dictionary, records: Dictionary,
	observer: Vector2, observer_radius: float,
	max_nearby: int = 5, neighborhood_radius: float = 180.0
) -> bool:
	if max_nearby <= 0:
		return false
	var extra: Dictionary = candidate.get("ExtraData", {})
	var item_id: int = int(extra.get("ItemIndex", 0))
	if item_id <= 0:
		return true
	var at := Vector2(float(candidate.get("LocX", 0.0)), float(candidate.get("LocY", 0.0)))
	var other_count: int = 0
	var near_sq := neighborhood_radius * neighborhood_radius
	var observer_sq := observer_radius * observer_radius
	for raw_id in records:
		var other_id: int = int(raw_id)
		if other_id >= candidate_id:
			continue
		var record: Dictionary = records[raw_id]
		var other_extra: Dictionary = record.get("ExtraData", {})
		if int(other_extra.get("ItemIndex", 0)) != item_id:
			continue
		var position := Vector2(float(record.get("LocX", 0.0)), float(record.get("LocY", 0.0)))
		if position.distance_squared_to(observer) > observer_sq:
			continue
		if position.distance_squared_to(at) <= near_sq:
			other_count += 1
			if other_count >= max_nearby:
				return false
	return true

static func apply_visibility_overrides(profile: Dictionary, player_mode: int, monster_mode: int) -> Dictionary:
	var result: Dictionary = profile.duplicate(true)
	result["other_players_visibility"] = sanitize_player_visibility(player_mode)
	result["ordinary_monsters_visibility"] = sanitize_monster_visibility(monster_mode)
	return result


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
	return clampi(value, LOAD_QUALITY, LOAD_LOW)

static func sanitize_combat_mode(value: int) -> int:
	return clampi(value, COMBAT_NORMAL, COMBAT_RESPONSE)

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
	# Both player and monster simplified visuals use one shared cached
	# generated texture per entity type. Keep the flag for older callers.
	result["use_primitive_monsters"] = bool(
		result.get("simplify_noncritical_monsters", false)
	)
	return result

static func load_mode_name(value: int) -> String:
	match sanitize_load_mode(value):
		LOAD_QUALITY: return "quality"
		LOAD_BALANCED: return "balanced"
		LOAD_LOW: return "low"
	return "balanced"

static func combat_mode_name(value: int) -> String:
	match sanitize_combat_mode(value):
		COMBAT_NORMAL: return "normal"
		COMBAT_RESPONSE: return "response"
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
		"combat_effects_enabled": true,
		# Client-side visual allocation only, NEVER changes network/server AOI.
		"monster_aoi_enter_radius": 2200.0,
		"monster_aoi_exit_radius": 2600.0,
		"ground_aoi_enter_radius": 2400.0,
		"ground_aoi_exit_radius": 2800.0,
		# Static map presentation AOI. This changes only Sprite2D texture
		# residency/draw work; Blocking/A*, collision and server AOI are untouched.
		"map_visual_aoi_enter_radius": 5000.0,
		"map_visual_aoi_exit_radius": 5800.0,
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
			profile["map_visual_aoi_enter_radius"] = 3400.0
			profile["map_visual_aoi_exit_radius"] = 4200.0
		LOAD_LOW:
			# Low resource + responsive gameplay: release nonessential visual
			# effects, simplify NPC-independent entity art and keep network/
			# physics at their normal fixed tick rate. Never slow game time.
			profile["animation_interval"] = 1.0 / 30.0
			profile["full_player_budget"] = 20
			profile["full_monster_budget"] = 10
			profile["effect_budget_percent"] = 0
			profile["combat_effects_enabled"] = false
			# Visual AOI only; ground interaction and server packets are untouched.
			profile["monster_aoi_enter_radius"] = 1250.0
			profile["monster_aoi_exit_radius"] = 1650.0
			profile["ground_aoi_enter_radius"] = 1450.0
			profile["ground_aoi_exit_radius"] = 1850.0
			profile["map_visual_aoi_enter_radius"] = 2400.0
			profile["map_visual_aoi_exit_radius"] = 3200.0
			profile["simplify_other_players"] = true
			profile["simplify_noncritical_monsters"] = true
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

	# Direct resolve() callers get the same shared cached orb style.
	var auto_style := default_auto_style(load_mode)
	profile["player_simplify_style"] = auto_style
	profile["monster_simplify_style"] = auto_style
	profile["use_primitive_monsters"] = bool(
		profile.get("simplify_noncritical_monsters", false)
	)
	return profile
