extends Node

func _ready() -> void:
	var balanced := ClientPerformanceProfile.resolve(
		ClientPerformanceProfile.LOAD_BALANCED,
		ClientPerformanceProfile.COMBAT_NORMAL
	)
	var multibox := ClientPerformanceProfile.resolve(
		ClientPerformanceProfile.LOAD_MULTIBOX,
		ClientPerformanceProfile.COMBAT_NORMAL
	)
	var pvp := ClientPerformanceProfile.resolve(
		ClientPerformanceProfile.LOAD_BALANCED,
		ClientPerformanceProfile.COMBAT_PVP
	)
	var mass := ClientPerformanceProfile.resolve(
		ClientPerformanceProfile.LOAD_BALANCED,
		ClientPerformanceProfile.COMBAT_MASS
	)

	assert(int(multibox["background_fps"]) < int(balanced["background_fps"]))
	assert(int(multibox["full_monster_budget"]) < int(balanced["full_monster_budget"]))
	assert(bool(multibox["simplify_other_players"]))
	assert(bool(multibox["hide_far_noncombat_monsters"]))

	assert(int(pvp["full_player_budget"]) >= int(balanced["full_player_budget"]))
	assert(int(pvp["full_monster_budget"]) <= int(balanced["full_monster_budget"]))
	assert(bool(pvp["hide_far_noncombat_monsters"]))

	assert(int(mass["full_player_budget"]) >= int(pvp["full_player_budget"]))
	assert(int(mass["effect_budget_percent"]) <= int(pvp["effect_budget_percent"]))

	var auto_profile := ClientPerformanceProfile.apply_visual_overrides(
		multibox,
		ClientPerformanceProfile.SIMPLIFY_AUTO,
		ClientPerformanceProfile.SIMPLIFY_AUTO
	)
	assert(bool(auto_profile["simplify_other_players"]))
	assert(bool(auto_profile["simplify_noncritical_monsters"]))

	var forced_off := ClientPerformanceProfile.apply_visual_overrides(
		multibox,
		ClientPerformanceProfile.SIMPLIFY_OFF,
		ClientPerformanceProfile.SIMPLIFY_OFF
	)
	assert(not bool(forced_off["simplify_other_players"]))
	assert(not bool(forced_off["simplify_noncritical_monsters"]))

	var forced_on := ClientPerformanceProfile.apply_visual_overrides(
		balanced,
		ClientPerformanceProfile.SIMPLIFY_ON,
		ClientPerformanceProfile.SIMPLIFY_ON
	)
	assert(bool(forced_on["simplify_other_players"]))
	assert(bool(forced_on["simplify_noncritical_monsters"]))

	print("PASS: client performance profiles preserve player-first combat priorities and visual overrides")
