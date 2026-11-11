extends RefCounted
class_name RemotePlayerStatePolicy

# Attack/hurt poses are one-shot actions, not walk/idle updates.
# BaseEntity._on_body_animation_finished() releases the lock. Late movement
# state packets may change motion, but must not restart/erase the action pose.
static func preserve_hit_animation(
	current_state: String, action_locked: bool, incoming_state: String
) -> bool:
	var one_shot_action: bool = (
		current_state.begins_with("hit_")
		or current_state.begins_with("attack_")
	)
	return (
		action_locked
		and one_shot_action
		and incoming_state in ["walk", "idle"]
	)

static func movement_may_replace_animation(current_state: String, action_locked: bool) -> bool:
	return not action_locked and not current_state.begins_with("attack") \
		and not current_state.begins_with("hit") \
		and current_state not in ["death", "die"]
