extends Control

signal close_requested

const MAP_BOUNDS_PATH: = "res://maps/map_bounds.json"
const REQUIRED_BOUNDS_FIELDS: Array[String] = ["min_x", "min_y", "max_x", "max_y"]
const DEFAULT_ZOOM: = 0.05
const MIN_ZOOM: = 0.005
const ZOOM_STEP: = 1.15
const FIT_PADDING_RATIO: = 0.9
const MAP_WORLD_CULL_MASK: = 1 << 0
const NPC_REFRESH_INTERVAL: = 0.5
const NPC_PULSE_PERIOD: = 1.8
const NPC_MIN_ALPHA: = 0.45
const NPC_MARKER_SIZE: = Vector2(22.0, 22.0)
const NPC_DOT_SIZE: = Vector2(14.0, 14.0)
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

@export_range(0.01, 2.0, 0.01) var maximum_zoom: = 0.2

var _map_bounds_by_id: Dictionary = {}
var _current_map_id: = 0
var _current_map_node: Node2D = null
var _current_player: Node2D = null
var _current_bounds: = Rect2()
var _is_dragging_map: = false
var _npc_markers: Dictionary = {}
var _player_markers: Dictionary = {}
var _npc_refresh_elapsed: = 0.0
var _npc_pulse_elapsed: = 0.0
var _hovered_npc_id: = 0

@onready var map_name_label: Label = $WindowPanel / Header / MapNameLabel
@onready var coords_label: Label = $WindowPanel / Header / CoordsLabel
@onready var viewport_container: SubViewportContainer = $WindowPanel / MapFrame / ViewportContainer
@onready var map_viewport: SubViewport = $WindowPanel / MapFrame / ViewportContainer / SubViewport
@onready var map_camera: Camera2D = $WindowPanel / MapFrame / ViewportContainer / SubViewport / MapCamera
@onready var marker_overlay: Control = $WindowPanel / MapFrame / MarkerOverlay
@onready var player_marker: Control = $WindowPanel / MapFrame / MarkerOverlay / PlayerMarker
@onready var npc_tooltip: Panel = $WindowPanel / MapFrame / TooltipOverlay / NpcTooltip
@onready var npc_tooltip_portrait: TextureRect = $WindowPanel / MapFrame / TooltipOverlay / NpcTooltip / Portrait
@onready var npc_tooltip_name: Label = $WindowPanel / MapFrame / TooltipOverlay / NpcTooltip / NameLabel
@onready var npc_tooltip_coords: Label = $WindowPanel / MapFrame / TooltipOverlay / NpcTooltip / CoordsLabel
@onready var close_button: Button = $WindowPanel / Header / BtnClose
@onready var fit_map_button: Button = $WindowPanel / Footer / BtnFitMap
@onready var focus_player_button: Button = $WindowPanel / Footer / BtnFocusPlayer


func _ready() -> void :
	reload_map_bounds()
	close_button.pressed.connect(close_requested.emit)
	fit_map_button.pressed.connect(fit_full_map)
	focus_player_button.pressed.connect(focus_player)
	viewport_container.gui_input.connect(_on_viewport_gui_input)
	visibility_changed.connect(_on_visibility_changed)
	_update_viewport_rendering()
	_update_player_presentation()
	set_process(visible)


func _process(delta: float) -> void :
	if visible:
		_update_player_presentation()
		_npc_refresh_elapsed += delta
		_npc_pulse_elapsed = fmod(_npc_pulse_elapsed + delta, NPC_PULSE_PERIOD)
		if _npc_refresh_elapsed >= NPC_REFRESH_INTERVAL:
			_npc_refresh_elapsed = 0.0
			_refresh_npc_markers()
			_refresh_player_markers()
		_update_npc_markers()
		_update_player_markers()


func set_map_context(map_id: int, display_name: String, map_node: Node2D, shared_world: World2D) -> void :
	_current_map_id = map_id
	_current_map_node = map_node
	map_name_label.text = display_name if not display_name.is_empty() else "未知地圖"
	map_viewport.world_2d = shared_world
	map_viewport.canvas_cull_mask = MAP_WORLD_CULL_MASK
	_current_bounds = get_map_bounds(map_id)

	if not _current_bounds.has_area():
		_current_bounds = _infer_map_bounds(map_node)

	_refresh_npc_markers()
	_refresh_player_markers()
	call_deferred("fit_full_map")


func set_player_context(player_node: Node2D) -> void :
	_current_player = player_node
	_refresh_player_markers()
	_update_player_presentation()


func open_map() -> void :
	visible = true
	_update_viewport_rendering()
	_refresh_npc_markers()
	_refresh_player_markers()
	call_deferred("fit_full_map")


func close_map() -> void :
	_hide_npc_tooltip()
	visible = false


func prepare_for_map_change() -> void :
	_is_dragging_map = false
	_hide_npc_tooltip()
	_clear_npc_markers()
	_clear_player_markers()
	_current_map_id = 0
	_current_map_node = null
	_current_player = null
	_current_bounds = Rect2()
	player_marker.visible = false
	coords_label.text = "X：-- , Y：--"
	visible = false
	set_process(false)
	map_viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED


func clear_context() -> void :
	prepare_for_map_change()


func fit_full_map() -> void :
	if not is_node_ready() or not is_instance_valid(map_camera):
		return

	var viewport_size: = viewport_container.size
	if viewport_size.x <= 0.0 or viewport_size.y <= 0.0:
		return

	if not _current_bounds.has_area():
		map_camera.zoom = Vector2.ONE * DEFAULT_ZOOM
		if is_instance_valid(_current_map_node):
			map_camera.global_position = _current_map_node.global_position
		return

	var fit_zoom: = _calculate_fit_zoom(viewport_size, _current_bounds)
	map_camera.zoom = Vector2.ONE * fit_zoom
	map_camera.global_position = _current_bounds.get_center()
	_update_player_presentation()


func _calculate_fit_zoom(viewport_size: Vector2, map_bounds: Rect2) -> float:
	if viewport_size.x <= 0.0 or viewport_size.y <= 0.0 or not map_bounds.has_area():
		return clampf(DEFAULT_ZOOM, MIN_ZOOM, maximum_zoom)

	var zoom_x: = viewport_size.x / map_bounds.size.x
	var zoom_y: = viewport_size.y / map_bounds.size.y
	return clampf(minf(zoom_x, zoom_y) * FIT_PADDING_RATIO, MIN_ZOOM, maximum_zoom)


func focus_player() -> void :
	if not is_instance_valid(_current_player):
		return
	map_camera.global_position = _current_player.global_position
	_clamp_camera_to_bounds()
	_update_player_presentation()


func _on_visibility_changed() -> void :
	if not is_node_ready():
		return
	_update_viewport_rendering()
	set_process(visible)
	if visible:
		_update_player_presentation()


func _update_viewport_rendering() -> void :
	if not is_instance_valid(map_viewport):
		return
	map_viewport.render_target_update_mode = (
		SubViewport.UPDATE_ALWAYS if visible else SubViewport.UPDATE_DISABLED
	)


func _on_viewport_gui_input(event: InputEvent) -> void :
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_LEFT:
			_is_dragging_map = event.pressed
			viewport_container.accept_event()
		elif event.pressed and event.button_index == MOUSE_BUTTON_WHEEL_UP:
			_zoom_map(ZOOM_STEP)
			viewport_container.accept_event()
		elif event.pressed and event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			_zoom_map(1.0 / ZOOM_STEP)
			viewport_container.accept_event()
	elif event is InputEventMouseMotion and _is_dragging_map:
		var camera_zoom: = maxf(map_camera.zoom.x, MIN_ZOOM)
		map_camera.global_position -= event.relative / camera_zoom
		_clamp_camera_to_bounds()
		viewport_container.accept_event()


func _zoom_map(multiplier: float) -> void :
	var minimum_zoom: = MIN_ZOOM
	var viewport_size: = viewport_container.size
	if (
		_current_bounds.has_area()
		and viewport_size.x > 0.0
		and viewport_size.y > 0.0
	):
		minimum_zoom = _calculate_fit_zoom(viewport_size, _current_bounds)

	var next_zoom: = clampf(map_camera.zoom.x * multiplier, minimum_zoom, maximum_zoom)
	map_camera.zoom = Vector2.ONE * next_zoom
	_clamp_camera_to_bounds()
	_update_player_presentation()


func _clamp_camera_to_bounds() -> void :
	if not _current_bounds.has_area():
		return

	var visible_world_size: = viewport_container.size / map_camera.zoom
	var half_visible: = visible_world_size * 0.5
	var bounds_center: = _current_bounds.get_center()
	var next_position: = map_camera.global_position

	if visible_world_size.x >= _current_bounds.size.x:
		next_position.x = bounds_center.x
	else:
		next_position.x = clampf(
			next_position.x, 
			_current_bounds.position.x + half_visible.x, 
			_current_bounds.end.x - half_visible.x
		)

	if visible_world_size.y >= _current_bounds.size.y:
		next_position.y = bounds_center.y
	else:
		next_position.y = clampf(
			next_position.y, 
			_current_bounds.position.y + half_visible.y, 
			_current_bounds.end.y - half_visible.y
		)

	map_camera.global_position = next_position


func _update_player_presentation() -> void :
	if not is_instance_valid(player_marker) or not is_instance_valid(coords_label):
		return

	if not is_instance_valid(_current_player) or not is_instance_valid(map_camera):
		player_marker.visible = false
		coords_label.text = "X：-- , Y：--"
		focus_player_button.disabled = true
		return

	focus_player_button.disabled = false
	var player_world_position: = _current_player.global_position
	coords_label.text = "X：%d , Y：%d" % [int(player_world_position.x), int(player_world_position.y)]


func _world_to_map_screen(world_position: Vector2) -> Vector2:
	var viewport_center: = viewport_container.size * 0.5
	return viewport_center + (world_position - map_camera.global_position) * map_camera.zoom


func _create_player_marker(dbid: int, color: Color) -> Dictionary:
	var marker: Panel
	if is_instance_valid(_current_player) and "data_model" in _current_player:
		var local_data: PlayerData = _current_player.data_model as PlayerData
		if local_data != null and dbid == local_data.db_id:
			marker = player_marker as Panel

	if not is_instance_valid(marker):
		marker = Panel.new()
		marker.name = "PlayerMarker_%d" % dbid
		marker.custom_minimum_size = PLAYER_MARKER_SIZE
		marker.size = PLAYER_MARKER_SIZE
		marker.mouse_filter = Control.MOUSE_FILTER_IGNORE
		marker_overlay.add_child(marker)

	_apply_player_marker_color(marker, color)
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
	if not is_instance_valid(marker_overlay):
		return
	if not is_instance_valid(_current_player) or not "data_model" in _current_player:
		_clear_player_markers()
		return

	var local_data: PlayerData = _current_player.data_model as PlayerData
	if local_data == null:
		_clear_player_markers()
		return

	var desired_markers: Dictionary = {}
	if local_data.party_id.is_empty():
		desired_markers[local_data.db_id] = {
			"entity": _current_player, 
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

			var member_entity: Node2D = _current_player if member_dbid == local_data.db_id else _find_party_player_entity(member_dbid)
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
		if marker == player_marker:
			marker.visible = false
		else:
			marker.queue_free()
	_player_markers.erase(dbid)


func _clear_player_markers() -> void :
	for raw_dbid: Variant in _player_markers.keys():
		_remove_player_marker(int(raw_dbid))


func _update_player_markers() -> void :
	var viewport_rect: = Rect2(Vector2.ZERO, viewport_container.size)
	for raw_dbid: Variant in _player_markers.keys():
		var marker_data: Dictionary = _player_markers[raw_dbid]
		var player_entity: Node2D = marker_data.get("entity") as Node2D
		var marker: Control = marker_data.get("marker") as Control
		if not is_instance_valid(player_entity) or not is_instance_valid(marker):
			if is_instance_valid(marker):
				marker.visible = false
			continue

		var marker_position: = _world_to_map_screen(player_entity.global_position)
		marker.position = marker_position - marker.size * 0.5
		marker.visible = viewport_rect.has_point(marker_position)


func _refresh_npc_markers() -> void :
	if not is_instance_valid(marker_overlay):
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
	dot_style.corner_radius_top_left = 7
	dot_style.corner_radius_top_right = 7
	dot_style.corner_radius_bottom_right = 7
	dot_style.corner_radius_bottom_left = 7
	dot.add_theme_stylebox_override("panel", dot_style)

	marker.add_child(dot)
	marker_overlay.add_child(marker)
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
	var viewport_rect: = Rect2(Vector2.ZERO, viewport_container.size)

	for raw_entity_id: Variant in _npc_markers.keys():
		var entity_id: = int(raw_entity_id)
		var marker_data: Dictionary = _npc_markers[entity_id]
		var npc_entity: Node2D = marker_data.get("entity") as Node2D
		var marker: Control = marker_data.get("marker") as Control
		var dot: Control = marker_data.get("dot") as Control
		if not is_instance_valid(npc_entity) or not is_instance_valid(marker) or not is_instance_valid(dot):
			continue

		var marker_position: = _world_to_map_screen(npc_entity.global_position)
		marker.position = marker_position - marker.size * 0.5
		marker.visible = viewport_rect.has_point(marker_position)
		dot.modulate.a = 1.0 if entity_id == _hovered_npc_id else pulse_alpha

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
	_position_npc_tooltip(marker.position + marker.size * 0.5)


func _position_npc_tooltip(marker_center: Vector2) -> void :
	var tooltip_position: = marker_center + Vector2(16.0, 16.0)
	var available_size: = marker_overlay.size
	tooltip_position.x = clampf(tooltip_position.x, 0.0, maxf(available_size.x - npc_tooltip.size.x, 0.0))
	tooltip_position.y = clampf(tooltip_position.y, 0.0, maxf(available_size.y - npc_tooltip.size.y, 0.0))
	npc_tooltip.position = tooltip_position


func _hide_npc_tooltip() -> void :
	_hovered_npc_id = 0
	if is_instance_valid(npc_tooltip):
		npc_tooltip.visible = false


func _infer_map_bounds(map_node: Node2D) -> Rect2:
	if not is_instance_valid(map_node):
		return Rect2()

	var inferred_bounds: = Rect2()
	var has_inferred_bounds: = false
	var nodes_to_visit: Array[Node] = [map_node]

	while not nodes_to_visit.is_empty():
		var current_node: Node = nodes_to_visit.pop_back()
		for child: Node in current_node.get_children():
			nodes_to_visit.append(child)

		var node_bounds: = Rect2()
		if current_node is TileMapLayer:
			node_bounds = _get_tile_map_layer_bounds(current_node)
		elif current_node is Sprite2D:
			node_bounds = _get_sprite_bounds(current_node)

		if not node_bounds.has_area():
			continue

		if has_inferred_bounds:
			inferred_bounds = inferred_bounds.merge(node_bounds)
		else:
			inferred_bounds = node_bounds
			has_inferred_bounds = true

	return inferred_bounds


func _get_tile_map_layer_bounds(tile_layer: TileMapLayer) -> Rect2:
	var used_rect: = tile_layer.get_used_rect()
	if not used_rect.has_area():
		return Rect2()

	var tile_size: = Vector2(tile_layer.tile_set.tile_size) if tile_layer.tile_set else Vector2.ONE
	var half_tile: = tile_size * 0.5
	var first_center: = tile_layer.map_to_local(used_rect.position)
	var last_cell: = used_rect.position + used_rect.size - Vector2i.ONE
	var last_center: = tile_layer.map_to_local(last_cell)
	var local_rect: = Rect2(first_center - half_tile, last_center - first_center + tile_size)
	return _local_rect_to_global_rect(tile_layer, local_rect)


func _get_sprite_bounds(sprite_node: Sprite2D) -> Rect2:
	if sprite_node.texture == null or not sprite_node.visible:
		return Rect2()
	return _local_rect_to_global_rect(sprite_node, sprite_node.get_rect())


func _local_rect_to_global_rect(canvas_item: CanvasItem, local_rect: Rect2) -> Rect2:
	var corners: Array[Vector2] = [
		canvas_item.to_global(local_rect.position), 
		canvas_item.to_global(Vector2(local_rect.end.x, local_rect.position.y)), 
		canvas_item.to_global(local_rect.end), 
		canvas_item.to_global(Vector2(local_rect.position.x, local_rect.end.y))
	]
	var global_rect: = Rect2(corners[0], Vector2.ZERO)
	for corner: Vector2 in corners:
		global_rect = global_rect.expand(corner)
	return global_rect


func reload_map_bounds() -> void :
	_map_bounds_by_id.clear()

	if not FileAccess.file_exists(MAP_BOUNDS_PATH):
		push_warning("[BigMap] 找不到地圖邊界資料：%s，將使用自動推算。" % MAP_BOUNDS_PATH)
		return

	var file: = FileAccess.open(MAP_BOUNDS_PATH, FileAccess.READ)
	if file == null:
		push_warning("[BigMap] 無法讀取地圖邊界資料：%s，將使用自動推算。" % MAP_BOUNDS_PATH)
		return

	var json: = JSON.new()
	var parse_error: = json.parse(file.get_as_text())
	if parse_error != OK:
		push_warning(
			"[BigMap] 地圖邊界 JSON 格式錯誤（第 %d 行：%s），將使用自動推算。"
			%[json.get_error_line(), json.get_error_message()]
		)
		return

	var parsed_data: Variant = json.data
	if typeof(parsed_data) != TYPE_DICTIONARY:
		push_warning("[BigMap] 地圖邊界 JSON 根節點必須是物件，將使用自動推算。")
		return

	for raw_map_id: Variant in parsed_data.keys():
		var map_id_text: = str(raw_map_id)
		if not map_id_text.is_valid_int() or map_id_text.to_int() <= 0:
			push_warning("[BigMap] 忽略無效的 Map ID：%s" % map_id_text)
			continue

		var map_id: = map_id_text.to_int()
		var bounds: = _parse_bounds_entry(map_id, parsed_data[raw_map_id])
		if bounds.has_area():
			_map_bounds_by_id[map_id] = bounds


func has_map_bounds(map_id: int) -> bool:
	return _map_bounds_by_id.has(map_id)


func get_map_bounds(map_id: int) -> Rect2:
	return _map_bounds_by_id.get(map_id, Rect2()) as Rect2


func _parse_bounds_entry(map_id: int, raw_entry: Variant) -> Rect2:
	if typeof(raw_entry) != TYPE_DICTIONARY:
		push_warning("[BigMap] Map %d 的 bounds 必須是物件，已忽略。" % map_id)
		return Rect2()

	var entry: Dictionary = raw_entry
	for field_name: String in REQUIRED_BOUNDS_FIELDS:
		if not entry.has(field_name) or not _is_number(entry[field_name]):
			push_warning("[BigMap] Map %d 的 %s 缺少或不是數字，已忽略該地圖 bounds。" % [map_id, field_name])
			return Rect2()

	var min_x: = float(entry["min_x"])
	var min_y: = float(entry["min_y"])
	var max_x: = float(entry["max_x"])
	var max_y: = float(entry["max_y"])

	if max_x <= min_x or max_y <= min_y:
		push_warning("[BigMap] Map %d 的 max 必須大於 min，已忽略該地圖 bounds。" % map_id)
		return Rect2()

	return Rect2(Vector2(min_x, min_y), Vector2(max_x - min_x, max_y - min_y))


func _is_number(value: Variant) -> bool:
	return typeof(value) == TYPE_INT or typeof(value) == TYPE_FLOAT
