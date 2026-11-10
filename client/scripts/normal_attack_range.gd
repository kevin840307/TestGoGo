extends RefCounted
class_name NormalAttackRange

# Ordinary weapon attack_range includes a standard 120-point weapon baseline.
# Do not count that baseline as extra reach compared with bare hands. Only
# weapon values above 120 and other numeric range bonuses extend reach.
# No Sword/Gun/Dagger-style category logic. Keep in sync with server
# CombatService.NormalAttackWorldRange().
const MIN_WORLD_REACH: float = 40.0
const STAT_TO_WORLD_SCALE: float = 0.5
const STANDARD_EQUIPPED_WEAPON_RANGE: int = 120

static func from_stat(raw_attack_range: int, equipped_weapon_range: int = 0) -> float:
	var normal_weapon_baseline: int = mini(
		STANDARD_EQUIPPED_WEAPON_RANGE, maxi(0, equipped_weapon_range)
	)
	return maxf(
		MIN_WORLD_REACH,
		float(raw_attack_range - normal_weapon_baseline) * STAT_TO_WORLD_SCALE
	)
