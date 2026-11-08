extends CanvasLayer

const TIME_REFRESH_INTERVAL_SECONDS: = 1.0
const DAWN_START_HOUR: = 5.0
const DAY_START_HOUR: = 6.0
const DUSK_START_HOUR: = 20.0
const NIGHT_START_HOUR: = 22.0

@onready var screen_overlay: ColorRect = $ScreenOverlay

var _tracked_player: Node2D = null
var _time_refresh_elapsed: = 0.0
var _last_viewport_size: = Vector2.ZERO


func _ready() -> void :
	_update_night_strength()
	_update_viewport_size(true)
	_update_player_screen_position()


func _process(delta: float) -> void :
	_time_refresh_elapsed += delta
	if _time_refresh_elapsed >= TIME_REFRESH_INTERVAL_SECONDS:
		_time_refresh_elapsed = 0.0
		_update_night_strength()

	_update_viewport_size()
	# The shader is fully transparent during the daytime. Skip all player-UV
	# updates while the ColorRect is not rendered; resume automatically at dusk.
	if is_instance_valid(screen_overlay) and screen_overlay.visible:
		_update_player_screen_position()


func set_tracked_player(player: Node2D) -> void :
	_tracked_player = player
	_update_player_screen_position()


static func should_render_night_overlay(strength: float) -> bool:
	# At strength 0 the shader produces alpha=0 for every pixel, so a
	# full-screen pass has no visible effect whatsoever.
	return strength > 0.0


func calculate_night_strength(hour: float) -> float:
	var normalized_hour: = fposmod(hour, 24.0)
	if normalized_hour < DAWN_START_HOUR or normalized_hour >= NIGHT_START_HOUR:
		return 1.0
	if normalized_hour < DAY_START_HOUR:
		return 1.0 - smoothstep(DAWN_START_HOUR, DAY_START_HOUR, normalized_hour)
	if normalized_hour < DUSK_START_HOUR:
		return 0.0
	return smoothstep(DUSK_START_HOUR, NIGHT_START_HOUR, normalized_hour)


func _update_night_strength() -> void :
	var current_time: = Time.get_datetime_dict_from_system()
	var hour: = float(current_time.get("hour", 0))
	hour += float(current_time.get("minute", 0)) / 60.0
	hour += float(current_time.get("second", 0)) / 3600.0
	var strength: float = calculate_night_strength(hour)
	if is_instance_valid(screen_overlay):
		screen_overlay.visible = should_render_night_overlay(strength)
	_set_shader_parameter(&"night_strength", strength)


func _update_viewport_size(force_update: bool = false) -> void :
	var current_size: = Vector2(get_viewport().get_visible_rect().size)
	if not force_update and current_size.is_equal_approx(_last_viewport_size):
		return
	_last_viewport_size = current_size
	_set_shader_parameter(&"viewport_size", current_size)


func _update_player_screen_position() -> void :
	var viewport_size: = Vector2(get_viewport().get_visible_rect().size)
	var player_screen_uv: = Vector2(0.5, 0.5)
	if is_instance_valid(_tracked_player) and viewport_size.x > 0.0 and viewport_size.y > 0.0:
		var player_screen_position: = _tracked_player.get_global_transform_with_canvas().origin
		player_screen_uv = player_screen_position / viewport_size
	_set_shader_parameter(&"player_screen_uv", player_screen_uv)


func _set_shader_parameter(parameter: StringName, value: Variant) -> void :
	if not is_instance_valid(screen_overlay):
		return
	var shader_material: = screen_overlay.material as ShaderMaterial
	if shader_material:
		shader_material.set_shader_parameter(parameter, value)
