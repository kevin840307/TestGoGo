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


# A screen notifier may update one frame after a large/zoomed camera makes
# an actor visible. Resolve the camera-transformed position against the
# actual viewport instead of assuming an 800x600 visible world.
static func is_near_viewport(viewport_rect: Rect2, screen_position: Vector2,
	margin_pixels: float = 64.0) -> bool:
	return viewport_rect.grow(maxf(0.0, margin_pixels)).has_point(screen_position)
