class_name AnimatedResourceBar
extends ProgressBar

enum BarType{
	HP, 
	MP, 
	SP, 
	EXP, 
	ENERGY, 
	FEED, 
}

@export var bar_type: BarType = BarType.HP:
	set(new_type):
		bar_type = new_type
		_apply_palette()
		queue_redraw()

@export_range(0.0, 1.0, 0.01) var value_transition_duration: float = 0.18
@export var smooth_value_changes: bool = true
@export_range(0.1, 10.0, 0.1) var sheen_min_interval: float = 2.0
@export_range(0.1, 10.0, 0.1) var sheen_max_interval: float = 3.0
@export_range(0.1, 2.0, 0.05) var sheen_duration: float = 0.7
@export var enable_tooltip_input: bool = false

@onready var _sheen_timer: Timer = $SheenTimer

var _background_style: = StyleBoxFlat.new()
var _fill_style: = StyleBoxFlat.new()
var _display_value: float = 0.0
var _has_received_value_update: = false
var _value_tween: Tween
var _sheen_tween: Tween
var _sheen_progress: float = -1.0:
	set(new_progress):
		_sheen_progress = new_progress
		queue_redraw()
var _rng: = RandomNumberGenerator.new()
var _fill_color: = Color.WHITE
var _fill_highlight_color: = Color.WHITE


func _ready() -> void :
	show_percentage = false
	mouse_filter = Control.MOUSE_FILTER_PASS if enable_tooltip_input else Control.MOUSE_FILTER_IGNORE
	_rng.seed = int(Time.get_ticks_usec()) ^ get_instance_id()
	_display_value = clampf(value, min_value, max_value)
	value_changed.connect(_on_value_changed)
	changed.connect(_on_range_changed)
	_sheen_timer.timeout.connect(_on_sheen_timer_timeout)
	_apply_palette()
	_schedule_next_sheen()
	queue_redraw()


func _notification(what: int) -> void :
	if what == NOTIFICATION_RESIZED:
		_update_style_geometry()
		queue_redraw()
	elif what == NOTIFICATION_VISIBILITY_CHANGED and is_node_ready():
		if is_visible_in_tree():
			_schedule_next_sheen()
		else:
			_stop_sheen()


func _draw() -> void :
	var bar_rect: = Rect2(Vector2.ZERO, size)
	if bar_rect.size.x <= 0.0 or bar_rect.size.y <= 0.0:
		return

	draw_style_box(_background_style, bar_rect)

	var inner_margin: = maxf(1.0, minf(2.0, size.y * 0.12))
	var inner_rect: = bar_rect.grow( - inner_margin)
	var ratio: = _get_display_ratio()
	if ratio <= 0.0 or inner_rect.size.x <= 0.0 or inner_rect.size.y <= 0.0:
		return

	var fill_rect: = inner_rect
	fill_rect.size.x *= ratio
	draw_style_box(_fill_style, fill_rect)


	var highlight_height: = maxf(1.0, floorf(fill_rect.size.y * 0.28))
	var top_highlight: = Rect2(fill_rect.position + Vector2(1.0, 1.0), Vector2(maxf(0.0, fill_rect.size.x - 2.0), highlight_height))
	if top_highlight.size.x > 0.0:
		draw_rect(top_highlight, _fill_highlight_color, true)

	if _sheen_progress >= 0.0:
		_draw_sheen(fill_rect)


func set_value_immediate(new_value: float) -> void :
	_has_received_value_update = true
	_kill_value_tween()
	value = new_value
	_set_display_value(clampf(new_value, min_value, max_value))


func _on_value_changed(new_value: float) -> void :
	var target: = clampf(new_value, min_value, max_value)
	if not _has_received_value_update:
		_has_received_value_update = true
		_set_display_value(target)
		return

	if not smooth_value_changes or value_transition_duration <= 0.0 or not is_inside_tree():
		_set_display_value(target)
		return

	_kill_value_tween()
	_value_tween = create_tween()
	_value_tween.set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	_value_tween.tween_method(_set_display_value, _display_value, target, value_transition_duration)


func _on_range_changed() -> void :
	_set_display_value(clampf(_display_value, min_value, max_value))


func _set_display_value(new_value: float) -> void :
	_display_value = clampf(new_value, min_value, max_value)
	queue_redraw()


func _get_display_ratio() -> float:
	var value_range: = max_value - min_value
	if value_range <= 0.0:
		return 0.0
	return clampf((_display_value - min_value) / value_range, 0.0, 1.0)


func _draw_sheen(fill_rect: Rect2) -> void :
	if fill_rect.size.x <= 1.0:
		return
	var sheen_width: = clampf(size.x * 0.18, 8.0, 30.0)
	var travel_start: = fill_rect.position.x - sheen_width
	var travel_end: = fill_rect.end.x + sheen_width
	var center_x: = lerpf(travel_start, travel_end, _sheen_progress)
	var sheen_rect: = Rect2(
		Vector2(center_x - sheen_width * 0.5, fill_rect.position.y), 
		Vector2(sheen_width, fill_rect.size.y)
	).intersection(fill_rect)
	if sheen_rect.size.x <= 0.0:
		return

	var edge_alpha: = 0.06
	var center_alpha: = 0.24
	var third: = sheen_rect.size.x / 3.0
	draw_rect(Rect2(sheen_rect.position, Vector2(third, sheen_rect.size.y)), Color(1.0, 1.0, 1.0, edge_alpha), true)
	draw_rect(Rect2(sheen_rect.position + Vector2(third, 0.0), Vector2(third, sheen_rect.size.y)), Color(1.0, 1.0, 1.0, center_alpha), true)
	draw_rect(Rect2(sheen_rect.position + Vector2(third * 2.0, 0.0), Vector2(sheen_rect.size.x - third * 2.0, sheen_rect.size.y)), Color(1.0, 1.0, 1.0, edge_alpha), true)


func _schedule_next_sheen() -> void :
	if not is_node_ready() or not is_visible_in_tree() or _sheen_timer == null:
		return
	if _sheen_tween and _sheen_tween.is_running():
		return
	var lower: = minf(sheen_min_interval, sheen_max_interval)
	var upper: = maxf(sheen_min_interval, sheen_max_interval)
	_sheen_timer.start(_rng.randf_range(lower, upper))


func _on_sheen_timer_timeout() -> void :
	if not is_visible_in_tree() or _get_display_ratio() <= 0.0:
		_schedule_next_sheen()
		return
	_sheen_progress = 0.0
	_sheen_tween = create_tween()
	_sheen_tween.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	_sheen_tween.tween_property(self, "_sheen_progress", 1.0, sheen_duration)
	_sheen_tween.tween_callback(_finish_sheen)


func _finish_sheen() -> void :
	_sheen_progress = -1.0
	_sheen_tween = null
	_schedule_next_sheen()


func _stop_sheen() -> void :
	if _sheen_timer:
		_sheen_timer.stop()
	if _sheen_tween and _sheen_tween.is_running():
		_sheen_tween.kill()
	_sheen_tween = null
	_sheen_progress = -1.0


func _kill_value_tween() -> void :
	if _value_tween and _value_tween.is_running():
		_value_tween.kill()
	_value_tween = null


func _apply_palette() -> void :
	match bar_type:
		BarType.HP:
			_fill_color = Color("b91f38")
			_fill_highlight_color = Color("ff7887")
		BarType.MP:
			_fill_color = Color("236cc4")
			_fill_highlight_color = Color("83c9ff")
		BarType.SP:
			_fill_color = Color("1d9b68")
			_fill_highlight_color = Color("7cf1b6")
		BarType.EXP:
			_fill_color = Color("d89518")
			_fill_highlight_color = Color("ffe080")
		BarType.ENERGY:
			_fill_color = Color("d89518")
			_fill_highlight_color = Color("ffe080")
		BarType.FEED:
			_fill_color = Color("d89518")
			_fill_highlight_color = Color("ffe080")
	_fill_highlight_color = _fill_color.lerp(_fill_highlight_color, 0.55)
	_update_styles()


func _update_styles() -> void :
	_background_style.bg_color = Color(0.025, 0.032, 0.045, 0.92)
	_background_style.border_color = Color(0.48, 0.55, 0.65, 0.72)
	_fill_style.bg_color = _fill_color
	_fill_style.border_color = _fill_color.lightened(0.24)
	_update_style_geometry()


func _update_style_geometry() -> void :
	var radius: = maxi(1, int(roundf(size.y * 0.25)))
	var fill_radius: = maxi(1, radius - 1)
	_set_style_corners(_background_style, radius)
	_set_style_corners(_fill_style, fill_radius)
	_set_style_border_width(_background_style, 1)
	_set_style_border_width(_fill_style, 1)
	_background_style.anti_aliasing = true
	_fill_style.anti_aliasing = true


func _set_style_corners(style: StyleBoxFlat, radius: int) -> void :
	style.corner_radius_top_left = radius
	style.corner_radius_top_right = radius
	style.corner_radius_bottom_left = radius
	style.corner_radius_bottom_right = radius


func _set_style_border_width(style: StyleBoxFlat, width: int) -> void :
	style.border_width_left = width
	style.border_width_top = width
	style.border_width_right = width
	style.border_width_bottom = width
