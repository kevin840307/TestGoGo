extends RefCounted
class_name RemotePlayerStatePolicy

# A hurt pose is a one-shot hit reaction, not a walk/idle update.
# BaseEntity._on_body_animation_finished() releases the lock.
static func preserve_hit_animation(
	current_state: String, action_locked: bool, incoming_state: String
) -> bool:
	return action_locked and current_state.begins_with("hit_") and incoming_state in ["walk", "idle"]

static func movement_may_replace_animation(current_state: String, action_locked: bool) -> bool:
	return not action_locked and not current_state.begins_with("attack") \
		and not current_state.begins_with("hit") \
		and current_state not in ["death", "die"]
