extends RefCounted
class_name RemotePlayerMotion

# Presentation-only dead reckoning. Server positions are authoritative,
# combat/pickups/physics/network cadence never use this predicted position.
const MIN_SAMPLE_SECONDS: float = 0.05
const MAX_SAMPLE_SECONDS: float = 0.75
# Only coalesced packets explicitly carrying their full span may use longer samples.
const MAX_EXTENDED_SAMPLE_SECONDS: float = 8.0
const MAX_PREDICTION_SECONDS: float = 0.42
const MAX_PREDICTION_PIXELS: float = 140.0
const MAX_ESTIMATED_SPEED: float = 650.0
const SMOOTHING_RATE: float = 14.0
# Safety cap for a delayed 2s+ packet: spring smoothing must not turn one
# badly delayed snapshot into a visible 100px jump in one physics frame.
const MAX_VISUAL_CATCHUP_SPEED: float = 900.0

var visual_position: Vector2 = Vector2.ZERO
var authoritative_position: Vector2 = Vector2.ZERO
var _velocity: Vector2 = Vector2.ZERO
var _sample_seconds: float = 0.12
var _prediction_horizon_seconds: float = 0.14
var _packet_age_seconds: float = 0.0
var _last_received_msec: int = -1
var _has_sample: bool = false


func reset(position: Vector2) -> void:
	visual_position = position
	authoritative_position = position
	_velocity = Vector2.ZERO
	_sample_seconds = 0.12
	_prediction_horizon_seconds = 0.14
	_packet_age_seconds = 0.0
	_last_received_msec = -1
	_has_sample = false


func receive(position: Vector2, received_msec: int,
	nominal_seconds: float = 0.1, teleport_threshold: float = 1200.0,
	extended_server_seconds: float = 0.0) -> void:
	if not position.is_finite():
		return
	if visual_position.distance_to(position) >= teleport_threshold:
		reset(position)
		_last_received_msec = received_msec
		_has_sample = true
		return

	# Legacy SpeedTime stays <= 750ms. Only explicitly labeled
	# coalesced snapshots may cover a longer interval; never mistake the
	# old default SpeedTime=1.0 (missing on the wire) for such a span.
	var extended_valid: bool = (
		extended_server_seconds > MAX_SAMPLE_SECONDS
		and extended_server_seconds <= MAX_EXTENDED_SAMPLE_SECONDS
	)
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
		maxf(0.14, observed * 1.08), 0.14, MAX_PREDICTION_SECONDS
	)
	authoritative_position = position
	_last_received_msec = received_msec
	_packet_age_seconds = 0.0
	_has_sample = true


func stop_prediction() -> void:
	# An authoritative SyncState idle is a stop signal, not a reason to
	# teleport the visible character back to the last reported position.
	_velocity = Vector2.ZERO


func advance(delta: float) -> Vector2:
	_packet_age_seconds += maxf(0.0, delta)

	var desired: Vector2 = authoritative_position
	if _velocity.length_squared() > 1.0:
		var prediction_seconds: float = minf(
			_packet_age_seconds, _prediction_horizon_seconds
		)
		var lead: Vector2 = (_velocity * prediction_seconds).limit_length(
			MAX_PREDICTION_PIXELS
		)
		desired += lead

		# A missing packet must never extrapolate forever; the lead has
		# already stopped increasing after MAX_PREDICTION_SECONDS and is
		# capped at MAX_PREDICTION_PIXELS. Do not slowly rubber-band back to
		# the old authoritative point solely because a packet was delayed:
		# that causes remote actors to visibly walk backward, then forward.
		# New authoritative positions/idle/death/teleport still supersede
		# this presentation-only held offset immediately.

	var step_seconds: float = clampf(delta, 0.0, 0.10)
	var alpha: float = 1.0 - exp(-SMOOTHING_RATE * step_seconds)
	var eased_position: Vector2 = visual_position.lerp(desired, alpha)
	visual_position = visual_position.move_toward(
		eased_position, MAX_VISUAL_CATCHUP_SPEED * step_seconds
	)
	if visual_position.distance_squared_to(desired) < 0.001:
		visual_position = desired
	return visual_position


func is_moving() -> bool:
	return (
		visual_position.distance_squared_to(authoritative_position) > 4.0 or
		(_velocity.length_squared() > 1.0 and
			_packet_age_seconds < maxf(0.50, _sample_seconds + 0.20))
	)
