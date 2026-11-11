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


# Original-client-style pacing: movement speed is an entity property, while
# packets update the destination. Use Server-provided movement speed as the
# primary rate, and only allow bounded catch-up when the client is visibly
# behind a coalesced/new waypoint.
static func calculate_server_driven(
	distance: float,
	duration: float,
	server_speed: float,
	snapshot_span: float = 0.0,
	visual_slack: float = 0.01,
	min_speed: float = 1.0,
	max_speed: float = 900.0,
	snap_epsilon: float = 1.0
) -> float:
	if not is_finite(distance) or not is_finite(duration):
		return 0.0
	if distance <= snap_epsilon or duration <= 0.01:
		return 0.0
	if not is_finite(server_speed) or server_speed <= 0.0:
		return calculate(
			distance, duration, visual_slack,
			min_speed, max_speed, snap_epsilon
		)

	var nominal_speed: float = clampf(server_speed, min_speed, max_speed)
	var required_speed: float = distance / maxf(duration + visual_slack, 0.01)
	# A merged packet represents historical waypoints. Catch up faster than
	# nominal, but never turn several seconds of backlog into a 900px/s dash.
	var merged: bool = (
		is_finite(snapshot_span)
		and snapshot_span > duration + 0.05
	)
	var catchup_multiplier: float = 2.0 if merged else 1.5
	var bounded_required: float = minf(
		required_speed,
		nominal_speed * catchup_multiplier
	)
	return clampf(maxf(nominal_speed, bounded_required), min_speed, max_speed)


static func correction_visual_seconds(
	distance: float,
	min_seconds: float = 0.18,
	max_seconds: float = 1.0,
	target_pixels_per_second: float = 650.0
) -> float:
	if not is_finite(distance) or distance <= 0.0:
		return min_seconds
	return clampf(
		distance / maxf(1.0, target_pixels_per_second),
		min_seconds,
		max_seconds
	)


# An AOI cull is safe only after BOTH the moving visual body and the latest
# server-approved coordinate have left the exit envelope. During a stalled
# 250ms world tick or a coalesced waypoint the visual body may lag hundreds
# of pixels behind the canonical cached position. Discarding it at that point
# recreates the scene at the new coordinate and looks like a teleport.
static func is_fully_outside_aoi(observer: Vector2, visual_position: Vector2,
	authoritative_position: Vector2, exit_radius: float) -> bool:
	if not observer.is_finite() or not visual_position.is_finite() or not authoritative_position.is_finite():
		return false
	if not is_finite(exit_radius) or exit_radius <= 0.0:
		return false
	var radius_sq: float = exit_radius * exit_radius
	return (
		observer.distance_squared_to(visual_position) > radius_sq
		and observer.distance_squared_to(authoritative_position) > radius_sq
	)


# Avoid re-instantiating a monster at a newer authoritative coordinate during
# brief camera/monster oscillation at the visual AOI fringe. This is purely a
# Client scene-lifetime guard; Server Despawn still removes the actor.
static func should_linger_outside_aoi(
	distance: float,
	exit_radius: float,
	elapsed_ms: int
) -> bool:
	return distance <= exit_radius + 500.0 and elapsed_ms < 1000


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
