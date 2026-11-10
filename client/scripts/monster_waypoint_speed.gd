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


# Actual Canvas2D transform is the source of truth for a resized, rotated,
# stretched, or zoomed viewport. A fixed 800x600 (or zoom-only) estimate
# incorrectly hides actors near the edge and later respawns them at a newer
# server position, which looks like a teleport.
static func required_view_radius(viewport_rect: Rect2,
		canvas_transform: Transform2D, observer_world: Vector2,
		world_margin: float = 360.0) -> float:
	if not viewport_rect.has_area() or not observer_world.is_finite():
		return 0.0
	if absf(canvas_transform.determinant()) <= 0.00001:
		return 0.0
	var inverse: Transform2D = canvas_transform.affine_inverse()
	var origin: Vector2 = viewport_rect.position
	var end: Vector2 = viewport_rect.end
	var max_radius: float = 0.0
	for screen_corner in [
		origin, Vector2(end.x, origin.y),
		end, Vector2(origin.x, end.y)
	]:
		var world_corner: Vector2 = inverse * screen_corner
		if not world_corner.is_finite():
			return 0.0
		max_radius = maxf(max_radius, observer_world.distance_to(world_corner))
	return max_radius + maxf(0.0, world_margin)
