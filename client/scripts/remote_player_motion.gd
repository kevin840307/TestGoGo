extends RefCounted
class_name RemotePlayerMotion

# Presentation-only dead reckoning. Server positions are authoritative,
# combat/pickups/physics/network cadence never use this predicted position.
const MIN_SAMPLE_SECONDS: float = 0.05
const MAX_SAMPLE_SECONDS: float = 0.75
# Only coalesced packets explicitly carrying their full span may use longer samples.
const MAX_EXTENDED_SAMPLE_SECONDS: float = 8.0
const MAX_PREDICTION_SECONDS: float = 0.92
const MAX_PREDICTION_PIXELS: float = 140.0
const MAX_ESTIMATED_SPEED: float = 650.0
const SMOOTHING_RATE: float = 14.0
# Safety cap for a delayed 2s+ packet: spring smoothing must not turn one
# badly delayed snapshot into a visible 100px jump in one physics frame.
const MAX_VISUAL_CATCHUP_SPEED: float = 900.0
const MIN_EXPECTED_CATCHUP_SPEED: float = 280.0
const EXPECTED_SPEED_CATCHUP_MULTIPLIER: float = 2.0

var visual_position: Vector2 = Vector2.ZERO
var authoritative_position: Vector2 = Vector2.ZERO
var _velocity: Vector2 = Vector2.ZERO
var _sample_seconds: float = 0.12
var _prediction_horizon_seconds: float = 0.14
var _travel_horizon_seconds: float = 0.14
var _source_span_seconds: float = 0.12
var _packet_age_seconds: float = 0.0
var _last_received_msec: int = -1
var _has_sample: bool = false
var _visual_catchup_speed: float = MAX_VISUAL_CATCHUP_SPEED


func reset(position: Vector2) -> void:
	visual_position = position
	authoritative_position = position
	_velocity = Vector2.ZERO
	_sample_seconds = 0.12
	_prediction_horizon_seconds = 0.14
	_travel_horizon_seconds = 0.14
	_source_span_seconds = 0.12
	_packet_age_seconds = 0.0
	_last_received_msec = -1
	_has_sample = false
	_visual_catchup_speed = MAX_VISUAL_CATCHUP_SPEED


func receive(position: Vector2, received_msec: int,
	nominal_seconds: float = 0.1, teleport_threshold: float = 1200.0,
	extended_server_seconds: float = 0.0,
	expected_speed: float = 0.0) -> void:
	if not position.is_finite():
		return
	if is_finite(expected_speed) and expected_speed > 1.0:
		_visual_catchup_speed = clampf(
			expected_speed * EXPECTED_SPEED_CATCHUP_MULTIPLIER,
			MIN_EXPECTED_CATCHUP_SPEED,
			MAX_VISUAL_CATCHUP_SPEED
		)
	else:
		_visual_catchup_speed = MAX_VISUAL_CATCHUP_SPEED
	# Legacy SpeedTime stays <= 750ms. Only explicitly labeled
	# coalesced snapshots may cover a longer interval; never mistake the
	# old default SpeedTime=1.0 (missing on the wire) for such a span.
	var extended_valid: bool = (
		is_finite(extended_server_seconds)
		and extended_server_seconds > MAX_SAMPLE_SECONDS
		and extended_server_seconds <= MAX_EXTENDED_SAMPLE_SECONDS
	)
	# A large coordinate delta is not automatically a teleport when Server
	# explicitly says it represents several seconds of coalesced walking.
	# Untagged or physically implausible discontinuities still hard reset.
	var historical_distance: float = authoritative_position.distance_to(position)
	var plausible_coalesced_walk: bool = (
		_has_sample
		and extended_valid
		and historical_distance <= maxf(
			teleport_threshold,
			MAX_ESTIMATED_SPEED * extended_server_seconds * 1.10
		)
	)
	if visual_position.distance_to(position) >= teleport_threshold \
			and not plausible_coalesced_walk:
		reset(position)
		_last_received_msec = received_msec
		_has_sample = true
		return
	# Legacy A10D-like authoritative endpoint is NOT a velocity sample.
	# The server can send SpeedTime=0 followed by idle/death in a later
	# encrypted frame. Deriving speed from 0ms would create a false sprint
	# (up to MAX_ESTIMATED_SPEED) before the stop packet is processed.
	# Keep the current rendered position for a bounded visual correction.
	if nominal_seconds <= 0.01 and not extended_valid:
		authoritative_position = position
		_velocity = Vector2.ZERO
		_prediction_horizon_seconds = 0.0
		_sample_seconds = MIN_SAMPLE_SECONDS
		_last_received_msec = received_msec
		_packet_age_seconds = 0.0
		_has_sample = true
		return
	var fallback: float = clampf(
		extended_server_seconds if extended_valid else nominal_seconds,
		MIN_SAMPLE_SECONDS, MAX_EXTENDED_SAMPLE_SECONDS if extended_valid else MAX_SAMPLE_SECONDS
	)
	var observed: float = fallback
	# SpeedTime describes the SERVER interval between authoritative position
	# snapshots. Network receive gaps are not simulation time: when several
	# packets stall in transit, dividing movement by their arrival gap makes
	# remote players move too slowly, then jump when the queue catches up.
	var has_server_interval: bool = (
		nominal_seconds >= MIN_SAMPLE_SECONDS
		and nominal_seconds <= MAX_SAMPLE_SECONDS
	) or extended_valid
	if _has_sample and _last_received_msec >= 0:
		var wall_seconds: float = float(received_msec - _last_received_msec) / 1000.0
		if wall_seconds >= 0.04 and wall_seconds <= 1.0:
			if not has_server_interval:
				# Compatibility with packets without a meaningful SpeedTime.
				observed = clampf(wall_seconds, MIN_SAMPLE_SECONDS, MAX_SAMPLE_SECONDS)
			elif absf(wall_seconds - fallback) <= maxf(0.08, fallback * 0.5):
				# A little arrival-time adaptation is fine if it agrees with
				# server time; never treat a stalled WebSocket as slower walking.
				observed = lerpf(fallback, wall_seconds, 0.15)

	var displacement: Vector2 = position - authoritative_position
	if displacement.length_squared() <= 0.25:
		_velocity = Vector2.ZERO
	elif _has_sample:
		_velocity = (displacement / maxf(observed, MIN_SAMPLE_SECONDS)).limit_length(
			MAX_ESTIMATED_SPEED
		)
	else:
		_velocity = (displacement / maxf(fallback, MIN_SAMPLE_SECONDS)).limit_length(
			MAX_ESTIMATED_SPEED
		)

	_sample_seconds = lerpf(_sample_seconds, observed, 0.45) if _has_sample else fallback
	# A 350ms sender needs motion throughout that interval, not just the
	# first 120ms. Long merged/backpressured spans are historical samples:
	# they describe past velocity but MUST NOT predict 2s into the future.
	_prediction_horizon_seconds = 0.12 if extended_valid else clampf(
		maxf(0.14, observed * 1.16), 0.14, MAX_PREDICTION_SECONDS
	)
	# SpeedTime is the distance already covered on the server. Keep
	# rendering that original-client-style segment at a constant speed.
	# Historical coalesced packets must not extrapolate beyond their end.
	_source_span_seconds = observed
	_travel_horizon_seconds = observed if extended_valid else _prediction_horizon_seconds
	authoritative_position = position
	_last_received_msec = received_msec
	_packet_age_seconds = 0.0
	_has_sample = true


func stop_prediction() -> void:
	# An authoritative SyncState idle is a stop signal, not a reason to
	# teleport the visible character back to the last reported position.
	_velocity = Vector2.ZERO


func advance(delta: float) -> Vector2:
	var step_seconds: float = clampf(delta, 0.0, 0.10)
	var previous_age: float = _packet_age_seconds
	_packet_age_seconds += step_seconds

	if _velocity.length_squared() > 1.0:
		# Reconstruct the server's past movement segment. The previous
		# endpoint-chasing spring sped up for 100ms, then stopped before
		# the next 650-750ms player packet shown in real Map13 logs.
		var travel_before: float = minf(previous_age, _travel_horizon_seconds)
		var travel_after: float = minf(_packet_age_seconds, _travel_horizon_seconds)
		var travel_seconds: float = maxf(0.0, travel_after - travel_before)
		var segment_start: Vector2 = authoritative_position - _velocity * _source_span_seconds
		var expected: Vector2 = segment_start + _velocity * travel_after
		# Allow at most a modest visual lead while a new packet is missing.
		var lead: Vector2 = expected - authoritative_position
		if lead.length() > MAX_PREDICTION_PIXELS:
			expected = authoritative_position + lead.limit_length(MAX_PREDICTION_PIXELS)
		var correction: Vector2 = (expected - visual_position) * clampf(
			step_seconds * 4.0, 0.0, 0.10
		)
		var movement: Vector2 = _velocity * travel_seconds + correction
		# Never let a burst of delayed snapshots accelerate an avatar to
		# ten times its ordinary speed. Bounded catch-up is visual only.
		movement = movement.limit_length(_visual_catchup_speed * step_seconds)
		visual_position += movement
	else:
		# The authoritative idle/death and SpeedTime=0 stop prediction.
		# Cosmetic endpoint settling must never affect game logic.
		var alpha: float = 1.0 - exp(-SMOOTHING_RATE * step_seconds)
		var eased: Vector2 = visual_position.lerp(authoritative_position, alpha)
		visual_position = visual_position.move_toward(
			eased, _visual_catchup_speed * step_seconds
		)
		if visual_position.distance_squared_to(authoritative_position) < 0.001:
			visual_position = authoritative_position
	return visual_position

func is_moving() -> bool:
	return (
		visual_position.distance_squared_to(authoritative_position) > 4.0 or
		(_velocity.length_squared() > 1.0 and
			_packet_age_seconds < maxf(0.50, _travel_horizon_seconds + 0.10))
	)
