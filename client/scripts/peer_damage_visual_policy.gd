extends RefCounted

# Pure packet classification. Only Server-confirmed NORMAL player-to-monster
# damage (including authentic zero-damage MISS) is drawn immediately on remote observers;
# local attack/skill/monster
# damage keeps its existing animation and effect timeline.
static func is_remote_normal_hit(
		target_type: int, skill_id: int, damage: int,
		attacker_id: int, local_character_id: int
	) -> bool:
	return (
		target_type == 1
		and skill_id == 0
		and damage >= 0
		and attacker_id > 0 and attacker_id < 1000000
		and attacker_id != local_character_id
	)
