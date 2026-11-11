extends RefCounted
class_name ViewportAoiRadius

# World-space canvas corners drive the network interest envelope, not the
# historical 800x600 design resolution or current physical monitor DPI.
const MIN_RADIUS: int = 1800
const MAX_RADIUS: int = 10000
const RADIUS_STEP: int = 128
const HEADROOM: float = 420.0

static func estimate(
	visible_rect: Rect2,
	canvas_transform: Transform2D,
	observer_position: Vector2,
	headroom: float = HEADROOM
) -> int:
	if visible_rect.size.x <= 0.0 or visible_rect.size.y <= 0.0:
		return MIN_RADIUS
	if not observer_position.is_finite() or absf(canvas_transform.determinant()) < 0.00001:
		return MIN_RADIUS
	var world_from_screen: Transform2D = canvas_transform.affine_inverse()
	var a: Vector2 = visible_rect.position
	var b: Vector2 = visible_rect.end
	var corners: Array[Vector2] = [
		a,
		Vector2(b.x, a.y),
		b,
		Vector2(a.x, b.y),
	]
	var farthest: float = 0.0
	for screen_corner: Vector2 in corners:
		var world_corner: Vector2 = world_from_screen * screen_corner
		if not world_corner.is_finite():
			return MIN_RADIUS
		farthest = maxf(farthest, observer_position.distance_to(world_corner))
	var requested: float = farthest + maxf(0.0, headroom)
	return int(clampf(ceilf(requested / float(RADIUS_STEP)) * RADIUS_STEP,
		MIN_RADIUS, MAX_RADIUS))
