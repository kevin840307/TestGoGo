extends Control

const DEFAULT_MINIMAP_SIZE: = Vector2(147.0, 101.0)
const UPDATE_INTERVAL: = 0.05
const NPC_REFRESH_INTERVAL: = 0.5
const NPC_PULSE_PERIOD: = 1.8
const NPC_MIN_ALPHA: = 0.45
const NPC_MARKER_SIZE: = Vector2(16.0, 16.0)
const NPC_DOT_SIZE: = Vector2(9.0, 9.0)
const NPC_COLOR: = Color(1.0, 0.72, 0.2, 1.0)
const PORTAL_COLOR: = Color(0.373, 0.397, 0.932, 1.0)
const PLAYER_MARKER_SIZE: = Vector2(16.0, 16.0)
const SOLO_PLAYER_COLOR: = Color8(51, 230, 255, 200)
const PARTY_PLAYER_COLORS: Array[Color] = [
	Color8(57, 157, 230, 200), 
	Color8(213, 20, 24, 200), 
	Color8(8, 178, 16, 200), 
	Color8(230, 97, 213, 200), 
	Color8(238, 234, 49, 200), 
	Color8(255, 157, 8, 200)
]
const PLAYER_MARKER_BORDER_COLOR: = Color(0.137, 0.137, 0.137, 1.0)

var _minimap_camera: Camera2D = null
var _viewport_container: Control = null
var _npc_markers: Dictionary = {}
var _player_markers: Dictionary = {}
var _npc_refresh_elapsed: = 0.0
var _npc_pulse_elapsed: = 0.0
var _update_elapsed: = 0.0
var _hovered_npc_id: = 0
var _is_active: = true

@onready var marker_layer: Control = $MarkerLayer
@onready var tooltip_layer: Control = $TooltipLayer
@onready var npc_tooltip: Panel = $TooltipLayer / NpcTooltip
@onready var npc_tooltip_portrait: TextureRect = $TooltipLayer / NpcTooltip / Portrait
@onready var npc_tooltip_name: Label = $TooltipLayer / NpcTooltip / NameLabel
@onready var npc_tooltip_coords: Label = $TooltipLayer / NpcTooltip / CoordsLabel


func _ready() -> void :
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_sync_overlay_size()
	set_process(false)


func _process(delta: float) -> void :
	if not is_instance_valid(_minimap_camera):
		return

	_update_elapsed += delta
	if _update_elapsed < UPDATE_INTERVAL:
		return
	var update_delta: = _update_elapsed
	_update_elapsed = fmod(_update_elapsed, UPDATE_INTERVAL)
	_npc_refresh_elapsed += update_delta
	_npc_pulse_elapsed = fmod(_npc_pulse_elapsed + update_delta, NPC_PULSE_PERIOD)
	if _npc_refresh_elapsed >= NPC_REFRESH_INTERVAL:
		_npc_refresh_elapsed = 0.0
		_refresh_npc_markers()
		_refresh_player_markers()
	_update_npc_markers()
	_update_player_markers()


func setup(camera_node: Camera2D, container_node: Control) -> void :
	_minimap_camera = camera_node
	_viewport_container = container_node
	_sync_overlay_size()
	_refresh_npc_markers()
	_refresh_player_markers()
	_update_player_markers()
	set_process(_is_active and is_instance_valid(_minimap_camera))


func set_active(is_active: bool) -> void :
	_is_active = is_active
	set_process(_is_active and is_instance_valid(_minimap_camera))
	if not _is_active:
		_hide_npc_tooltip()
	elif is_instance_valid(_minimap_camera):
		_refresh_player_markers()
		_update_player_markers()


func clear_context() -> void :
	_hide_npc_tooltip()
	_clear_npc_markers()
	_clear_player_markers()
	_minimap_camera = null
	_viewport_container = null
	_hovered_npc_id = 0
	set_process(false)
	_sync_overlay_size()


func world_to_minimap_screen(world_position: Vector2) -> Vector2:
	if not is_instance_valid(_minimap_camera):
		return Vector2.ZERO

	var viewport_size: = _get_minimap_size()
	var viewport_center: = viewport_size * 0.5
	return viewport_center + (world_position - _minimap_camera.global_position) * _minimap_camera.zoom


func is_world_position_visible(world_position: Vector2) -> bool:
	if not is_instance_valid(_minimap_camera):
		return false
	return Rect2(Vector2.ZERO, _get_minimap_size()).has_point(world_to_minimap_screen(world_position))


func _create_player_marker(dbid: int, color: Color) -> Dictionary:
	var marker: = Panel.new()
	marker.name = "PlayerMarker_%d" % dbid
	marker.custom_minimum_size = PLAYER_MARKER_SIZE
	marker.size = PLAYER_MARKER_SIZE
	marker.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_apply_player_marker_color(marker, color)

	marker_layer.add_child(marker)
	var marker_data: = {"marker": marker, "color": color}
	_player_markers[dbid] = marker_data
	return marker_data


func _apply_player_marker_color(marker: Panel, color: Color) -> void :
	var marker_style: = StyleBoxFlat.new()
	marker_style.bg_color = color
	marker_style.border_width_left = 2
	marker_style.border_width_top = 2
	marker_style.border_width_right = 2
	marker_style.border_width_bottom = 2
	marker_style.border_color = PLAYER_MARKER_BORDER_COLOR
	marker_style.corner_radius_top_left = 8
	marker_style.corner_radius_top_right = 8
	marker_style.corner_radius_bottom_right = 8
	marker_style.corner_radius_bottom_left = 8
	marker.add_theme_stylebox_override("panel", marker_style)


func _refresh_player_markers() -> void :
	if not is_instance_valid(marker_layer):
		return

	var local_player: Node2D = GameManager.current_player as Node2D if GameManager else null
	if not is_instance_valid(local_player) or not "data_model" in local_player:
		_clear_player_markers()
		return

	var local_data: PlayerData = local_player.data_model as PlayerData
	if local_data == null:
		_clear_player_markers()
		return

	var desired_markers: Dictionary = {}
	if local_data.party_id.is_empty():
		desired_markers[local_data.db_id] = {
			"entity": local_player, 
			"color": SOLO_PLAYER_COLOR, 
		}
	else:
		var member_count: = mini(local_data.party_members.size(), PARTY_PLAYER_COLORS.size())
		for member_index: int in range(member_count):
			var raw_member: Variant = local_data.party_members[member_index]
			if typeof(raw_member) != TYPE_DICTIONARY:
				continue

			var member: Dictionary = raw_member
			var member_dbid: = int(member.get("DBID", 0))
			if member_dbid <= 0:
				continue

			var member_entity: Node2D = local_player if member_dbid == local_data.db_id else _find_party_player_entity(member_dbid)
			if not is_instance_valid(member_entity):
				continue

			desired_markers[member_dbid] = {
				"entity": member_entity, 
				"color": PARTY_PLAYER_COLORS[member_index], 
			}

	for raw_dbid: Variant in desired_markers.keys():
		var dbid: = int(raw_dbid)
		var desired_data: Dictionary = desired_markers[raw_dbid]
		var desired_color: Color = desired_data["color"] as Color
		if not _player_markers.has(dbid):
			_create_player_marker(dbid, desired_color)

		var marker_data: Dictionary = _player_markers[dbid]
		var marker: Panel = marker_data.get("marker") as Panel
		if not is_instance_valid(marker):
			_player_markers.erase(dbid)
			marker_data = _create_player_marker(dbid, desired_color)
			marker = marker_data.get("marker") as Panel

		marker_data["entity"] = desired_data["entity"]
		if marker_data.get("color", Color.TRANSPARENT) != desired_color:
			_apply_player_marker_color(marker, desired_color)
			marker_data["color"] = desired_color

	var stale_dbids: Array = []
	for raw_dbid: Variant in _player_markers.keys():
		if not desired_markers.has(raw_dbid):
			stale_dbids.append(raw_dbid)

	for raw_dbid: Variant in stale_dbids:
		_remove_player_marker(int(raw_dbid))


func _find_party_player_entity(dbid: int) -> Node2D:
	var entity_manager: Node = get_node_or_null("/root/NetworkEntityManager")
	if not is_instance_valid(entity_manager):
		return null

	var active_entities: Variant = entity_manager.get("active_entities")
	if typeof(active_entities) != TYPE_DICTIONARY or not active_entities.has(dbid):
		return null

	var candidate: Variant = active_entities[dbid]
	if not candidate is Node2D or not "data_model" in candidate:
		return null

	var player_entity: = candidate as Node2D
	var player_data: Variant = player_entity.get("data_model")
	if player_data == null or int(player_data.get("db_id")) != dbid:
		return null
	return player_entity


func _remove_player_marker(dbid: int) -> void :
	if not _player_markers.has(dbid):
		return

	var marker_data: Dictionary = _player_markers[dbid]
	var marker: Control = marker_data.get("marker") as Control
	if is_instance_valid(marker):
		marker.queue_free()
	_player_markers.erase(dbid)


func _clear_player_markers() -> void :
	for raw_dbid: Variant in _player_markers.keys():
		_remove_player_marker(int(raw_dbid))


func _update_player_markers() -> void :
	for raw_dbid: Variant in _player_markers.keys():
		var marker_data: Dictionary = _player_markers[raw_dbid]
		var player_entity: Node2D = marker_data.get("entity") as Node2D
		var marker: Control = marker_data.get("marker") as Control
		if not is_instance_valid(player_entity) or not is_instance_valid(marker):
			if is_instance_valid(marker):
				marker.visible = false
			continue

		var player_position: = player_entity.global_position
		var marker_position: = world_to_minimap_screen(player_position)
		var desired_position: = marker_position - marker.size * 0.5
		if marker.position != desired_position:
			marker.position = desired_position
		var should_be_visible: = is_world_position_visible(player_position)
		if marker.visible != should_be_visible:
			marker.visible = should_be_visible


func _refresh_npc_markers() -> void :
	if not is_instance_valid(marker_layer):
		return

	var entity_manager: Node = get_node_or_null("/root/NetworkEntityManager")
	if not is_instance_valid(entity_manager):
		return
	var active_entities: Variant = entity_manager.get("active_entities")
	if typeof(active_entities) != TYPE_DICTIONARY:
		return

	var active_npc_ids: Dictionary = {}
	for raw_entity: Variant in active_entities.values():
		if not raw_entity is Node2D:
			continue

		var npc_entity: = raw_entity as Node2D
		if not "npc_name" in npc_entity or not "npc_script_name" in npc_entity:
			continue

		var entity_id: = int(npc_entity.get("entity_id"))
		if entity_id <= 0:
			continue

		active_npc_ids[entity_id] = true
		if not _npc_markers.has(entity_id):
			_create_npc_marker(entity_id, npc_entity)
		else:
			_npc_markers[entity_id]["entity"] = npc_entity

	var stale_ids: Array = []
	for raw_entity_id: Variant in _npc_markers.keys():
		if not active_npc_ids.has(raw_entity_id):
			stale_ids.append(raw_entity_id)

	for raw_entity_id: Variant in stale_ids:
		_remove_npc_marker(int(raw_entity_id))


func _create_npc_marker(entity_id: int, npc_entity: Node2D) -> void :
	var marker: = Control.new()
	marker.name = "NpcMarker_%d" % entity_id
	marker.custom_minimum_size = NPC_MARKER_SIZE
	marker.size = NPC_MARKER_SIZE
	marker.mouse_filter = Control.MOUSE_FILTER_STOP
	marker.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	marker.mouse_entered.connect(_on_npc_marker_mouse_entered.bind(entity_id))
	marker.mouse_exited.connect(_on_npc_marker_mouse_exited.bind(entity_id))

	var dot: = Panel.new()
	dot.name = "Dot"
	dot.position = (NPC_MARKER_SIZE - NPC_DOT_SIZE) * 0.5
	dot.size = NPC_DOT_SIZE
	dot.mouse_filter = Control.MOUSE_FILTER_IGNORE

	var dot_style: = StyleBoxFlat.new()
	var is_portal: = bool(npc_entity.get("is_portal"))
	dot_style.bg_color = PORTAL_COLOR if is_portal else NPC_COLOR
	dot_style.border_width_left = 1
	dot_style.border_width_top = 1
	dot_style.border_width_right = 1
	dot_style.border_width_bottom = 1
	dot_style.border_color = Color(0.137, 0.137, 0.137, 1.0)
	dot_style.corner_radius_top_left = 5
	dot_style.corner_radius_top_right = 5
	dot_style.corner_radius_bottom_right = 5
	dot_style.corner_radius_bottom_left = 5
	dot.add_theme_stylebox_override("panel", dot_style)

	marker.add_child(dot)
	marker_layer.add_child(marker)
	_npc_markers[entity_id] = {"entity": npc_entity, "marker": marker, "dot": dot}


func _remove_npc_marker(entity_id: int) -> void :
	if not _npc_markers.has(entity_id):
		return

	var marker_data: Dictionary = _npc_markers[entity_id]
	var marker: Control = marker_data.get("marker") as Control
	if is_instance_valid(marker):
		marker.queue_free()
	_npc_markers.erase(entity_id)
	if _hovered_npc_id == entity_id:
		_hide_npc_tooltip()


func _clear_npc_markers() -> void :
	for marker_data_value: Variant in _npc_markers.values():
		if typeof(marker_data_value) != TYPE_DICTIONARY:
			continue
		var marker_data: Dictionary = marker_data_value
		var marker: Control = marker_data.get("marker") as Control
		if is_instance_valid(marker):
			marker.queue_free()
	_npc_markers.clear()


func _update_npc_markers() -> void :
	var pulse_ratio: = (sin((_npc_pulse_elapsed / NPC_PULSE_PERIOD) * TAU) + 1.0) * 0.5
	var pulse_alpha: = lerpf(NPC_MIN_ALPHA, 1.0, pulse_ratio)

	for raw_entity_id: Variant in _npc_markers.keys():
		var entity_id: = int(raw_entity_id)
		var marker_data: Dictionary = _npc_markers[entity_id]
		var npc_entity: Node2D = marker_data.get("entity") as Node2D
		var marker: Control = marker_data.get("marker") as Control
		var dot: Control = marker_data.get("dot") as Control
		if not is_instance_valid(npc_entity) or not is_instance_valid(marker) or not is_instance_valid(dot):
			continue

		var marker_position: = world_to_minimap_screen(npc_entity.global_position)
		var desired_position: = marker_position - marker.size * 0.5
		if marker.position != desired_position:
			marker.position = desired_position
		var should_be_visible: = is_world_position_visible(npc_entity.global_position)
		if marker.visible != should_be_visible:
			marker.visible = should_be_visible
		var desired_alpha: = 1.0 if entity_id == _hovered_npc_id else pulse_alpha
		if not is_equal_approx(dot.modulate.a, desired_alpha):
			dot.modulate.a = desired_alpha

	if _hovered_npc_id > 0:
		_update_npc_tooltip(_hovered_npc_id)


func _on_npc_marker_mouse_entered(entity_id: int) -> void :
	_hovered_npc_id = entity_id
	_update_npc_tooltip(entity_id)


func _on_npc_marker_mouse_exited(entity_id: int) -> void :
	if _hovered_npc_id == entity_id:
		_hide_npc_tooltip()


func _update_npc_tooltip(entity_id: int) -> void :
	if not _npc_markers.has(entity_id):
		_hide_npc_tooltip()
		return

	var marker_data: Dictionary = _npc_markers[entity_id]
	var npc_entity: Node2D = marker_data.get("entity") as Node2D
	var marker: Control = marker_data.get("marker") as Control
	if not is_instance_valid(npc_entity) or not is_instance_valid(marker) or not marker.visible:
		_hide_npc_tooltip()
		return

	var npc_name: = str(npc_entity.get("npc_name"))
	var npc_position: = npc_entity.global_position
	npc_tooltip_name.text = npc_name if not npc_name.is_empty() else "未知NPC"
	npc_tooltip_coords.text = "X：%d\nY：%d" % [int(npc_position.x), int(npc_position.y)]

	var npc_sprite: = npc_entity.get_node_or_null("Sprite2D") as Sprite2D
	npc_tooltip_portrait.texture = npc_sprite.texture if is_instance_valid(npc_sprite) else null
	npc_tooltip.visible = true
	_position_npc_tooltip(marker)


func _position_npc_tooltip(marker: Control) -> void :
	var marker_rect: = marker.get_global_rect()
	var screen_rect: = get_viewport_rect()
	var tooltip_size: = npc_tooltip.size
	var tooltip_position: = Vector2(marker_rect.end.x + 8.0, marker_rect.position.y)

	if tooltip_position.x + tooltip_size.x > screen_rect.end.x:
		tooltip_position.x = marker_rect.position.x - tooltip_size.x - 8.0
	if tooltip_position.y + tooltip_size.y > screen_rect.end.y:
		tooltip_position.y = screen_rect.end.y - tooltip_size.y

	tooltip_position.x = clampf(tooltip_position.x, screen_rect.position.x, maxf(screen_rect.end.x - tooltip_size.x, screen_rect.position.x))
	tooltip_position.y = clampf(tooltip_position.y, screen_rect.position.y, maxf(screen_rect.end.y - tooltip_size.y, screen_rect.position.y))
	npc_tooltip.global_position = tooltip_position


func _hide_npc_tooltip() -> void :
	_hovered_npc_id = 0
	if is_instance_valid(npc_tooltip):
		npc_tooltip.visible = false


func _sync_overlay_size() -> void :
	var minimap_size: = _get_minimap_size()
	custom_minimum_size = minimap_size
	size = minimap_size


func _get_minimap_size() -> Vector2:
	if is_instance_valid(_viewport_container) and _viewport_container.size.x > 0.0 and _viewport_container.size.y > 0.0:
		return _viewport_container.size
	if size.x > 0.0 and size.y > 0.0:
		return size
	return DEFAULT_MINIMAP_SIZE
