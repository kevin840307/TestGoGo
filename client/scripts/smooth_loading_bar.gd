extends ProgressBar
class_name SmoothLoadingBar

# Stage completion is authoritative; only the visible fill is interpolated.
const APPROACH_RATE: float = 9.0
const MAX_FRAME_STEP_SECONDS: float = 1.0 / 30.0
const FINISH_ANIMATION_SECONDS: float = 0.28
const SETTLE_EPSILON: float = 0.15

var _target_progress: float = 0.0
var _finish_tween: Tween = null


func _ready() -> void:
	_target_progress = clampf(value, 0.0, 100.0)
	set_process(false)


func reset_progress() -> void:
	if _finish_tween and _finish_tween.is_valid():
		_finish_tween.kill()
	_finish_tween = null
	_target_progress = 0.0
	value = 0.0
	set_process(false)


func set_target_progress(progress: float) -> void:
	# Ignore late asynchronous stage updates that would move backwards.
	_target_progress = maxf(_target_progress, clampf(progress, 0.0, 100.0))
	if _target_progress - value > SETTLE_EPSILON:
		set_process(true)


func finish_progress() -> void:
	# Complete only after actual work is done. Catch up during overlay fade,
	# without introducing a new loading wait or blocking network packets.
	_target_progress = 100.0
	set_process(false)
	if _finish_tween and _finish_tween.is_valid():
		_finish_tween.kill()
	_finish_tween = create_tween()
	_finish_tween.tween_property(self, "value", 100.0, FINISH_ANIMATION_SECONDS).set_trans(
		Tween.TRANS_CUBIC
	).set_ease(Tween.EASE_OUT)


func get_target_progress() -> float:
	return _target_progress


func _process(delta: float) -> void:
	var remaining: float = _target_progress - value
	if remaining <= SETTLE_EPSILON:
		value = _target_progress
		set_process(false)
		return
	# A long blocked frame must not immediately snap to the last stage.
	var safe_delta: float = clampf(delta, 0.0, MAX_FRAME_STEP_SECONDS)
	var alpha: float = 1.0 - exp(-APPROACH_RATE * safe_delta)
	value = minf(_target_progress, value + remaining * alpha)
