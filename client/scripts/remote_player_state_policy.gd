extends RefCounted
class_name RemotePlayerStatePolicy

# Attack/hurt poses are one-shot actions, not walk/idle updates.
# BaseEntity._on_body_animation_finished() releases the lock. Late movement
# state packets may change motion, but must not restart/erase the action pose.
static func network_state_is_one_shot(state: String) -> bool:
	return state.begins_with("hit_") or state.begins_with("attack_")


static func preserve_hit_animation(
	current_state: String, action_locked: bool, incoming_state: String
) -> bool:
	return (
		action_locked
		and network_state_is_one_shot(current_state)
		and incoming_state in ["walk", "idle"]
	)

static func movement_may_replace_animation(current_state: String, action_locked: bool) -> bool:
	return not action_locked and not current_state.begins_with("attack") \
		and not current_state.begins_with("hit") \
		and current_state not in ["death", "die"]

# Position snapshots may continue to arrive while a hit/attack one-shot owns
# the paper-doll direction. Applying their heading immediately restarts or
# flips the action animation through NetworkPlayer's animation cache.
static func movement_direction_may_replace_animation(
	current_state: String, action_locked: bool
) -> bool:
	return not (
		action_locked
		and (
			current_state.begins_with("hit_")
			or current_state.begins_with("attack_")
		)
	)

# EntityDamage is the fallback visual trigger for ordinary remote-player
# normal attacks. SkillCast already owns skill animations, while an explicit
# network attack state (used by load bots/legacy peers) has already started
# the one-shot without taking BaseEntity's local action lock.
static func damage_should_start_attack_visual(
	skill_id: int,
	explicit_network_attack_pending: bool
) -> bool:
	if skill_id > 0:
		return false
	return not explicit_network_attack_pending
