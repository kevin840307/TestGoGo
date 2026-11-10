extends RefCounted
class_name ViewInterestRadius

# Delivery/presentation interest only. Server gameplay remains authoritative.
const MIN_RADIUS: float = 1800.0
const MAX_RADIUS: float = 4200.0
const VIEW_MARGIN: float = 480.0
const MIN_ZOOM: float = 0.05


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
