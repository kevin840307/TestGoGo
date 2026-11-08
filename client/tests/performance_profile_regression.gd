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

	assert(int(multibox["background_fps"]) < int(balanced["background_fps"]))
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

	print("PASS: client performance profiles preserve player-first combat priorities and visual overrides")
