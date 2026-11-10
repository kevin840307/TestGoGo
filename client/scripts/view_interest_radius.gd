extends RefCounted
class_name ViewInterestRadius

# Delivery/presentation interest only. Server gameplay remains authoritative.
const MIN_RADIUS: float = 1800.0
const MAX_RADIUS: float = 4200.0
const VIEW_MARGIN: float = 480.0
const MIN_ZOOM: float = 0.05


# Conservative fallback for a temporarily unavailable/singular Canvas2D transform.
static func calculate(
	view_size: Vector2,
	camera_zoom: float = 1.0,
	camera_player_offset: float = 0.0
) -> float:
	if not view_size.is_finite() or view_size.x <= 0.0 or view_size.y <= 0.0:
		return MIN_RADIUS
	var zoom: float = maxf(MIN_ZOOM, absf(camera_zoom))
	var half_diagonal_world: float = view_size.length() * 0.5 / zoom
	var required: float = (
		half_diagonal_world
		+ VIEW_MARGIN
		+ maxf(0.0, camera_player_offset)
	)
	return clampf(maxf(MIN_RADIUS, required), MIN_RADIUS, MAX_RADIUS)


# Use the same source of truth as the client visual AOI: inverse-transform
# the actual viewport corners through Canvas2D. This handles resized windows,
# canvas_items stretch, non-uniform scale, camera offset and rotation without
# falling back to an 800x600/zoom-only guess.
# Returns 0 for invalid input so callers can use the bounded fallback above.
static func calculate_from_canvas(
	viewport_rect: Rect2,
	canvas_transform: Transform2D,
	observer_world: Vector2
) -> float:
	if not viewport_rect.has_area() or not observer_world.is_finite():
		return 0.0
	if absf(canvas_transform.determinant()) <= 0.00001:
		return 0.0

	var inverse: Transform2D = canvas_transform.affine_inverse()
	var origin: Vector2 = viewport_rect.position
	var end: Vector2 = viewport_rect.end
	var max_radius: float = 0.0
	for screen_corner in [
		origin,
		Vector2(end.x, origin.y),
		end,
		Vector2(origin.x, end.y),
	]:
		var world_corner: Vector2 = inverse * screen_corner
		if not world_corner.is_finite():
			return 0.0
		max_radius = maxf(
			max_radius,
			observer_world.distance_to(world_corner)
		)

	return clampf(
		maxf(MIN_RADIUS, max_radius + VIEW_MARGIN),
		MIN_RADIUS,
		MAX_RADIUS
	)
