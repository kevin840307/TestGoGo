extends RefCounted
class_name RemotePlayerMotion

# Presentation-only dead reckoning. Server positions are authoritative;
# combat/pickups/physics/network cadence never use this predicted position.
#
# Original-client parity: a received destination/snapshot changes the motion
# vector, while the visible character keeps stepping locally every frame.
# Do NOT replay each packet's already-elapsed historical segment from its
# beginning: receive jitter then becomes visible acceleration/deceleration.
const MIN_SAMPLE_SECONDS: float = 0.05
const MAX_SAMPLE_SECONDS: float = 0.75
const MAX_EXTENDED_SAMPLE_SECONDS: float = 8.0
const MAX_PREDICTION_SECONDS: float = 1.10
const MAX_PREDICTION_PIXELS: float = 140.0
const MAX_ESTIMATED_SPEED: float = 650.0
const ERROR_CORRECTION_RATE: float = 0.75
const MAX_VISUAL_CATCHUP_SPEED: float = 900.0
const MIN_VISUAL_CATCHUP_SPEED: float = 90.0
const NORMAL_CATCHUP_MULTIPLIER: float = 1.35
const EXTENDED_CATCHUP_MULTIPLIER: float = 1.60
const STOP_SNAP_DISTANCE: float = 8.0

var visual_position: Vector2 = Vector2.ZERO
var authoritative_position: Vector2 = Vector2.ZERO
var _velocity: Vector2 = Vector2.ZERO
var _sample_seconds: float = 0.12
var _prediction_horizon_seconds: float = 0.35
# Kept as public diagnostic fields for the existing regression/audit contract.
var _travel_horizon_seconds: float = 0.35
var _source_span_seconds: float = 0.12
var _packet_age_seconds: float = 0.0
var _last_received_msec: int = -1
var _has_sample: bool = false
var _visual_catchup_speed: float = MAX_VISUAL_CATCHUP_SPEED
var _settle_speed: float = 250.0


func reset(position: Vector2) -> void:
	visual_position = position
	authoritative_position = position
	_velocity = Vector2.ZERO
	_sample_seconds = 0.12
	_prediction_horizon_seconds = 0.35
	_travel_horizon_seconds = 0.35
	_source_span_seconds = 0.12
	_packet_age_seconds = 0.0
	_last_received_msec = -1
	_has_sample = false
	_visual_catchup_speed = MAX_VISUAL_CATCHUP_SPEED
	_settle_speed = 250.0


func receive(position: Vector2, received_msec: int,
	nominal_seconds: float = 0.1, teleport_threshold: float = 1200.0,
	extended_server_seconds: float = 0.0,
	expected_speed: float = 0.0) -> void:
	if not position.is_finite():
		return

	var expected_speed_valid: bool = is_finite(expected_speed) and expected_speed > 1.0
	if expected_speed_valid:
		_settle_speed = clampf(
			expected_speed * NORMAL_CATCHUP_MULTIPLIER,
			MIN_VISUAL_CATCHUP_SPEED,
			MAX_VISUAL_CATCHUP_SPEED
		)

	# Legacy SpeedTime stays <= 750ms. Only explicitly labeled coalesced
	# snapshots may cover a longer interval.
	var extended_valid: bool = (
		is_finite(extended_server_seconds)
		and extended_server_seconds > MAX_SAMPLE_SECONDS
		and extended_server_seconds <= MAX_EXTENDED_SAMPLE_SECONDS
	)

	# A large coordinate delta is not automatically a teleport when Server
	# explicitly says it represents several seconds of coalesced walking.
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

	# Legacy A10D-like authoritative endpoint is a correction, not a velocity
	# sample. Keep the current presentation and settle to it at bounded speed.
	if nominal_seconds <= 0.01 and not extended_valid:
		authoritative_position = position
		_velocity = Vector2.ZERO
		_prediction_horizon_seconds = 0.0
		_travel_horizon_seconds = 0.0
		_sample_seconds = MIN_SAMPLE_SECONDS
		_source_span_seconds = MIN_SAMPLE_SECONDS
		_last_received_msec = received_msec
		_packet_age_seconds = 0.0
		_has_sample = true
		return

	var fallback: float = clampf(
		extended_server_seconds if extended_valid else nominal_seconds,
		MIN_SAMPLE_SECONDS,
		MAX_EXTENDED_SAMPLE_SECONDS if extended_valid else MAX_SAMPLE_SECONDS
	)
	var observed: float = fallback
	var has_server_interval: bool = (
		nominal_seconds >= MIN_SAMPLE_SECONDS
		and nominal_seconds <= MAX_SAMPLE_SECONDS
	) or extended_valid

	# Arrival time may refine a trustworthy server interval, but may never
	# replace it after a WebSocket stall/backlog drain.
	if _has_sample and _last_received_msec >= 0:
		var wall_seconds: float = float(received_msec - _last_received_msec) / 1000.0
		if wall_seconds >= 0.04 and wall_seconds <= 1.0:
			if not has_server_interval:
				observed = clampf(
					wall_seconds, MIN_SAMPLE_SECONDS, MAX_SAMPLE_SECONDS
				)
			elif absf(wall_seconds - fallback) <= maxf(0.08, fallback * 0.5):
				observed = lerpf(fallback, wall_seconds, 0.10)

	var displacement: Vector2 = position - authoritative_position
	if displacement.length_squared() <= 0.25:
		_velocity = Vector2.ZERO
	elif _has_sample:
		_velocity = (displacement / maxf(
			observed, MIN_SAMPLE_SECONDS
		)).limit_length(MAX_ESTIMATED_SPEED)
	else:
		_velocity = (displacement / maxf(
			fallback, MIN_SAMPLE_SECONDS
		)).limit_length(MAX_ESTIMATED_SPEED)

	_sample_seconds = (
		lerpf(_sample_seconds, observed, 0.35) if _has_sample else fallback
	)
	_source_span_seconds = observed

	# Keep moving long enough to bridge the 350-750ms sparse bot snapshots
	# and the measured ~800ms tail without replaying the historical segment.
	# Explicit multi-second coalesced packets are old history and receive only
	# a short extrapolation window.
	_prediction_horizon_seconds = 0.12 if extended_valid else clampf(
		maxf(0.35, observed * 1.40),
		0.35,
		MAX_PREDICTION_SECONDS
	)
	_travel_horizon_seconds = _prediction_horizon_seconds

	var measured_speed: float = _velocity.length()
	var catchup_basis: float = measured_speed
	if catchup_basis <= 1.0 and expected_speed_valid:
		catchup_basis = expected_speed
	if catchup_basis > 1.0:
		_visual_catchup_speed = clampf(
			catchup_basis * (
				EXTENDED_CATCHUP_MULTIPLIER
				if extended_valid
				else NORMAL_CATCHUP_MULTIPLIER
			),
			MIN_VISUAL_CATCHUP_SPEED,
			MAX_VISUAL_CATCHUP_SPEED
		)
		_settle_speed = _visual_catchup_speed

	authoritative_position = position
	_last_received_msec = received_msec
	_packet_age_seconds = 0.0
	_has_sample = true


func stop_prediction() -> void:
	# Idle/death stops extrapolation, but the last authoritative endpoint can
	# still require a short visual correction.
	_velocity = Vector2.ZERO
	_prediction_horizon_seconds = 0.0
	_travel_horizon_seconds = 0.0


func advance(delta: float) -> Vector2:
	var step_seconds: float = clampf(delta, 0.0, 0.10)
	_packet_age_seconds += step_seconds

	if _velocity.length_squared() > 1.0:
		# Dead-reckon from the newest authoritative point forward. This target
		# remains nearly continuous across early/late packet arrival; unlike
		# replaying segment_start -> endpoint, jitter does not reset the visual
		# avatar to the beginning of an already-finished server interval.
		var prediction_seconds: float = minf(
			_packet_age_seconds, _prediction_horizon_seconds
		)
		var predicted: Vector2 = (
			authoritative_position + _velocity * prediction_seconds
		)
		var lead: Vector2 = predicted - authoritative_position
		if lead.length() > MAX_PREDICTION_PIXELS:
			predicted = (
				authoritative_position
				+ lead.limit_length(MAX_PREDICTION_PIXELS)
			)

		# Correct accumulated error gradually as a velocity term. A small
		# bounded servo avoids the old sprint -> slow -> sprint pattern.
		var error: Vector2 = predicted - visual_position
		var desired_velocity: Vector2 = (
			_velocity + error * ERROR_CORRECTION_RATE
		)

		# A late correction may slow forward progress, but must not make a
		# walking avatar visibly run backwards before the next packet.
		var forward: Vector2 = _velocity.normalized()
		var forward_component: float = desired_velocity.dot(forward)
		if forward_component < 0.0:
			desired_velocity -= forward * forward_component

		if desired_velocity.length() > _visual_catchup_speed:
			desired_velocity = desired_velocity.limit_length(
				_visual_catchup_speed
			)
		visual_position += desired_velocity * step_seconds
	else:
		var distance: float = visual_position.distance_to(
			authoritative_position
		)
		if distance <= STOP_SNAP_DISTANCE:
			visual_position = authoritative_position
		else:
			visual_position = visual_position.move_toward(
				authoritative_position,
				_settle_speed * step_seconds
			)

	return visual_position


func is_moving() -> bool:
	if _velocity.length_squared() > 1.0:
		return _packet_age_seconds < _prediction_horizon_seconds + 0.10
	return visual_position.distance_squared_to(authoritative_position) > 4.0
