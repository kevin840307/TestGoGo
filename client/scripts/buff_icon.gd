
extends TextureRect

@onready var lbl_time: Label = $LblTime

var buff_name: String = ""
var total_time: float = 1.0
var remaining_time: float = 0.0
var display_minutes: bool = false
const RING_REDRAW_INTERVAL: = 1.0 / 20.0
const TIMER_UPDATE_INTERVAL: = 1.0 / 20.0
var _ring_redraw_elapsed: float = 0.0
var _timer_update_elapsed: float = 0.0
var _last_display_value: int = -1


var ring_drawer: Control

func _ready() -> void :

	mouse_filter = Control.MOUSE_FILTER_STOP

	if lbl_time:

		lbl_time.mouse_filter = Control.MOUSE_FILTER_IGNORE


	ring_drawer = Control.new()
	ring_drawer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	ring_drawer.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(ring_drawer)
	ring_drawer.draw.connect(_on_ring_draw)
	set_process(remaining_time > 0.0)

func setup(b_name: String, tex: Texture2D, mat: Material, rem_time: float, tot_time: float, use_minutes: bool = false) -> void :
	buff_name = b_name
	texture = tex
	material = mat
	display_minutes = use_minutes


	if has_node("CooldownSweep"):
		get_node("CooldownSweep").queue_free()

	remaining_time = rem_time
	total_time = max(tot_time, 0.1)
	modulate.a = 1.0
	set_process(remaining_time > 0.0)
	_ring_redraw_elapsed = 0.0
	_timer_update_elapsed = 0.0
	_last_display_value = -1
	_update_time_label()
	if is_instance_valid(ring_drawer):
		ring_drawer.queue_redraw()

func update_time(rem_time: float, tot_time: float = -1.0) -> void :
	if tot_time > 0:
		total_time = max(tot_time, 0.1)
	elif rem_time > total_time:
		total_time = rem_time
	remaining_time = rem_time
	if remaining_time <= 0.0:
		remaining_time = 0.0
		modulate.a = 1.0
	elif remaining_time >= 5.0:
		modulate.a = 1.0
	set_process(remaining_time > 0.0)
	_update_time_label()
	_ring_redraw_elapsed = 0.0
	_timer_update_elapsed = 0.0

	if is_instance_valid(ring_drawer):
		ring_drawer.queue_redraw()

func _process(delta: float) -> void :
	if remaining_time > 0:
		_timer_update_elapsed += delta
		if remaining_time >= 5.0 and _timer_update_elapsed < TIMER_UPDATE_INTERVAL:
			return
		var update_delta: = _timer_update_elapsed
		_timer_update_elapsed = 0.0
		remaining_time = maxf(0.0, remaining_time - update_delta)
		_update_time_label()
		_ring_redraw_elapsed += update_delta

		if is_instance_valid(ring_drawer) and (remaining_time <= 0 or _ring_redraw_elapsed >= RING_REDRAW_INTERVAL):
			_ring_redraw_elapsed = fmod(_ring_redraw_elapsed, RING_REDRAW_INTERVAL)
			ring_drawer.queue_redraw()

		if remaining_time < 5.0:
			modulate.a = 0.5 + (sin(Time.get_ticks_msec() / 100.0) * 0.5)
		elif modulate.a != 1.0:
			modulate.a = 1.0

		if remaining_time <= 0.0:
			modulate.a = 1.0
			set_process(false)

func _update_time_label() -> void :
	var display_value = max(0, int(ceil(remaining_time / 60.0))) if display_minutes else max(0, int(ceil(remaining_time)))
	if lbl_time and display_value != _last_display_value:
		_last_display_value = display_value
		lbl_time.text = str(display_value)


func _on_ring_draw() -> void :
	if remaining_time <= 0 or total_time <= 0: return
	var center = size / 2.0
	var radius = min(size.x, size.y) / 2.0 - 2.0
	var progress = remaining_time / total_time
	var start_angle = - PI / 2.0 + (1.0 - progress) * TAU
	var end_angle = - PI / 2.0 + TAU


	ring_drawer.draw_arc(center, radius, start_angle, end_angle, 64, Color(0, 0.75, 1.0), 2.0, true)
