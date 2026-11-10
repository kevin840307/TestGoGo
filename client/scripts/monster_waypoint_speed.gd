extends RefCounted
class_name MonsterWaypointSpeed

# Pure movement arithmetic; independent from texture/scene loading.
# The rendering position is smoothed, but authoritative movement packets,
# combat events, click targeting, and AOI semantics are unchanged.
static func calculate(distance: float, duration: float,
	visual_slack: float = 0.01, min_speed: float = 1.0,
	max_speed: float = 900.0, snap_epsilon: float = 1.0) -> float:
	if not is_finite(distance) or not is_finite(duration):
		return 0.0
	if distance <= snap_epsilon or duration <= 0.01:
		return 0.0
	var visual_duration: float = maxf(duration + visual_slack, 0.01)
	return clampf(distance / visual_duration, min_speed, max_speed)
