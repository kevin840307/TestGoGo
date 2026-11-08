extends Node

func _ready() -> void:
	var balanced := ClientPerformanceProfile.resolve(
		ClientPerformanceProfile.LOAD_BALANCED,
		ClientPerformanceProfile.COMBAT_NORMAL
	)
	var low := ClientPerformanceProfile.resolve(
		ClientPerformanceProfile.LOAD_LOW,
		ClientPerformanceProfile.COMBAT_NORMAL
	)

	var multibox := ClientPerformanceProfile.resolve(
		ClientPerformanceProfile.LOAD_MULTIBOX,
		ClientPerformanceProfile.COMBAT_NORMAL
	)
	var response := ClientPerformanceProfile.resolve(
		ClientPerformanceProfile.LOAD_BALANCED,
		ClientPerformanceProfile.COMBAT_RESPONSE
	)
	var mass := ClientPerformanceProfile.resolve(
		ClientPerformanceProfile.LOAD_BALANCED,
		ClientPerformanceProfile.COMBAT_MASS
	)

	var quality := ClientPerformanceProfile.resolve(ClientPerformanceProfile.LOAD_QUALITY, ClientPerformanceProfile.COMBAT_NORMAL)
	assert(int(quality["foreground_fps"]) == 60 and int(low["foreground_fps"]) == 60 and int(multibox["foreground_fps"]) == 60)
	assert(int(multibox["background_fps"]) == int(balanced["background_fps"]))
	assert(float(quality["monster_aoi_enter_radius"]) > float(balanced["monster_aoi_enter_radius"]))
	assert(float(balanced["monster_aoi_enter_radius"]) > float(low["monster_aoi_enter_radius"]))
	assert(float(low["monster_aoi_enter_radius"]) > float(multibox["monster_aoi_enter_radius"]))
	assert(int(multibox["effect_budget_percent"]) < int(low["effect_budget_percent"]))
	assert(int(low["effect_budget_percent"]) < int(balanced["effect_budget_percent"]))
	assert(ClientPerformanceProfile.sanitize_fps_limit(35) == 35)
	assert(ClientPerformanceProfile.sanitize_fps_limit(144) == 144)
	assert(ClientPerformanceProfile.sanitize_fps_limit(0) == 0)
	assert(ClientPerformanceProfile.fps_options().has(45))
	assert(int(multibox["full_monster_budget"]) < int(balanced["full_monster_budget"]))
	assert(bool(multibox["simplify_other_players"]))
	assert(bool(multibox["use_primitive_monsters"]))
	assert(bool(multibox["hide_far_noncombat_monsters"]))
	assert(not bool(balanced["use_primitive_monsters"]))
	assert(bool(low["simplify_noncritical_monsters"]))
	assert(bool(low["use_primitive_monsters"]))
	assert(bool(response["use_primitive_monsters"]))
	assert(bool(mass["use_primitive_monsters"]))

	assert(int(response["full_player_budget"]) >= int(balanced["full_player_budget"]))
	assert(int(response["full_monster_budget"]) <= int(balanced["full_monster_budget"]))
	assert(bool(response["hide_far_noncombat_monsters"]))

	assert(int(mass["full_player_budget"]) >= int(response["full_player_budget"]))
	assert(int(mass["effect_budget_percent"]) <= int(response["effect_budget_percent"]))
	assert(ClientPerformanceProfile.combat_mode_name(
		ClientPerformanceProfile.COMBAT_RESPONSE
	) == "response")
	assert(ClientPerformanceProfile.COMBAT_PVP ==
		ClientPerformanceProfile.COMBAT_RESPONSE)

	var auto_profile := ClientPerformanceProfile.apply_visual_overrides(
		multibox,
		ClientPerformanceProfile.SIMPLIFY_AUTO,
		ClientPerformanceProfile.SIMPLIFY_AUTO
	)
	assert(bool(auto_profile["simplify_other_players"]))
	assert(bool(auto_profile["simplify_noncritical_monsters"]))
	assert(int(auto_profile["player_simplify_style"]) ==
		ClientPerformanceProfile.SIMPLIFY_SIMPLIFIED)
	assert(int(auto_profile["monster_simplify_style"]) ==
		ClientPerformanceProfile.SIMPLIFY_SIMPLIFIED)

	var forced_off := ClientPerformanceProfile.apply_visual_overrides(
		multibox,
		ClientPerformanceProfile.SIMPLIFY_OFF,
		ClientPerformanceProfile.SIMPLIFY_OFF
	)
	assert(not bool(forced_off["simplify_other_players"]))
	assert(not bool(forced_off["simplify_noncritical_monsters"]))
	assert(not bool(forced_off["use_primitive_monsters"]))

	var forced_on := ClientPerformanceProfile.apply_visual_overrides(
		balanced,
		ClientPerformanceProfile.SIMPLIFY_ON,
		ClientPerformanceProfile.SIMPLIFY_ON
	)
	assert(bool(forced_on["simplify_other_players"]))
	assert(bool(forced_on["simplify_noncritical_monsters"]))
	assert(bool(forced_on["use_primitive_monsters"]))
	assert(ClientPerformanceProfile.SIMPLIFY_ON ==
		ClientPerformanceProfile.SIMPLIFY_PIXEL_A)
	assert(int(forced_on["player_simplify_style"]) ==
		ClientPerformanceProfile.SIMPLIFY_PIXEL_A)


	# The UI now exposes exactly Auto / Normal / Simplified. Old per-account
	# settings 3 (beast/armored) and 4 (minimal) migrate to Simplified=2.
	assert(ClientPerformanceProfile.sanitize_simplify_mode(-1) ==
		ClientPerformanceProfile.SIMPLIFY_AUTO)
	assert(ClientPerformanceProfile.sanitize_simplify_mode(1) ==
		ClientPerformanceProfile.SIMPLIFY_NORMAL)
	assert(ClientPerformanceProfile.sanitize_simplify_mode(3) ==
		ClientPerformanceProfile.SIMPLIFY_SIMPLIFIED)
	assert(ClientPerformanceProfile.sanitize_simplify_mode(4) ==
		ClientPerformanceProfile.SIMPLIFY_SIMPLIFIED)
	assert(ClientPerformanceProfile.simplify_mode_name(1) == "normal")
	assert(ClientPerformanceProfile.simplify_mode_name(4) == "simplified")

	var migrated := ClientPerformanceProfile.apply_visual_overrides(
		balanced,
		ClientPerformanceProfile.SIMPLIFY_PIXEL_B,
		ClientPerformanceProfile.SIMPLIFY_MINIMAL
	)
	assert(bool(migrated["simplify_other_players"]))
	assert(bool(migrated["simplify_noncritical_monsters"]))
	assert(int(migrated["player_simplify_style"]) ==
		ClientPerformanceProfile.SIMPLIFY_SIMPLIFIED)
	assert(int(migrated["monster_simplify_style"]) ==
		ClientPerformanceProfile.SIMPLIFY_SIMPLIFIED)

	# Independent player and monster selectors must not influence each other.
	var player_only := ClientPerformanceProfile.apply_visual_overrides(
		balanced,
		ClientPerformanceProfile.SIMPLIFY_SIMPLIFIED,
		ClientPerformanceProfile.SIMPLIFY_NORMAL
	)
	assert(bool(player_only["simplify_other_players"]))
	assert(not bool(player_only["simplify_noncritical_monsters"]))
	assert(not bool(player_only["use_primitive_monsters"]))

	var monster_only := ClientPerformanceProfile.apply_visual_overrides(
		balanced,
		ClientPerformanceProfile.SIMPLIFY_NORMAL,
		ClientPerformanceProfile.SIMPLIFY_SIMPLIFIED
	)
	assert(not bool(monster_only["simplify_other_players"]))
	assert(bool(monster_only["simplify_noncritical_monsters"]))
	assert(bool(monster_only["use_primitive_monsters"]))

	print("PASS: performance modes, three-option orb simplification and legacy migration")
