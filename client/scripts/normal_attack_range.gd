extends RefCounted
class_name NormalAttackRange

# Ordinary attacks use the numeric additive AttackRange from base character
# and equipment. Never classify distance by weapon name or weapon style.
# Keep in sync with CombatService.NormalAttackWorldRange on the server.
const MIN_WORLD_REACH: float = 40.0
const STAT_TO_WORLD_SCALE: float = 0.5

static func from_stat(raw_attack_range: int) -> float:
	return maxf(MIN_WORLD_REACH, float(raw_attack_range) * STAT_TO_WORLD_SCALE)
