extends RefCounted
class_name NormalAttackRange

# Recovered from 2004 Paradise.exe CSGOPlayer::SyncAttackRange / RunPlayerLogic
# and verified in the 2009 Client.exe disassembly:
#    (world distance)^2 <= (weapon reach)^2 - 200
#    bare-handed default weapon reach = 45
#    valid equipped weapon reach = item range field (NOT added to actor base)
#
# Compatibility adjustment to satisfy SGO's design requirement that bare
# hands and a regular numerical 120-range weapon stop at the same distance:
# normalize raw PlayerStatusSync AttackRange (base 80 + equipment additive)
# into the original 45-unit baseline; only weapon excess beyond 120 and
# other numeric AttackRange bonuses extend it.
#
# That normalization is NOT an original client formula. It is required by
# current SGO item stat semantics and has no weapon name/category branches.
# Keep exactly aligned with CombatService.NormalAttackWorldRange().
const ORIGINAL_BASE_REACH: float = 45.0
const ORIGINAL_SQUARED_DISTANCE_MARGIN: float = 200.0
const CHARACTER_STAT_BASELINE: int = 80
const STANDARD_EQUIPPED_WEAPON_RANGE: int = 120

static func threshold_squared(raw_attack_range: int, equipped_weapon_range: int = 0) -> float:
	var ordinary_equipped_range: int = mini(
		STANDARD_EQUIPPED_WEAPON_RANGE, maxi(0, equipped_weapon_range)
	)
	var extra_range: int = maxi(
		0, raw_attack_range - CHARACTER_STAT_BASELINE - ordinary_equipped_range
	)
	var reach: float = ORIGINAL_BASE_REACH + float(extra_range)
	return maxf(0.0, reach * reach - ORIGINAL_SQUARED_DISTANCE_MARGIN)


static func within_range_squared(
	distance_squared: float, raw_attack_range: int, equipped_weapon_range: int = 0
) -> bool:
	# Original x86: cmp distanceSquared, thresholdSquared; jge approach.
	return distance_squared < threshold_squared(raw_attack_range, equipped_weapon_range)


static func from_stat(raw_attack_range: int, equipped_weapon_range: int = 0) -> float:
	return sqrt(threshold_squared(raw_attack_range, equipped_weapon_range))
