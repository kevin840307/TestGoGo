extends Control

const NODE_SIZE: = Vector2(190.0, 148.0)
const NODE_GAP: = Vector2(28.0, 24.0)
const TOWN_COLOR: = Color("d69a42")
const FIELD_COLOR: = Color("39758a")
const CURRENT_COLOR: = Color("5fd48d")
const ROUTE_COLOR: = Color(0.34, 0.2, 0.09, 0.42)
const ROUTE_DIM_COLOR: = Color(0.3, 0.2, 0.12, 0.1)
const ROUTE_HIGHLIGHT_COLOR: = Color(1.0, 0.7, 0.2, 0.98)
const PARCHMENT_TEXTURE: = preload("res://ui/world_map/parchment_background.png")
const TOWN_TEXTURE: = preload("res://ui/world_map/town_emblem.png")
const WILDERNESS_TEXTURE: = preload("res://ui/world_map/wilderness_emblem.png")

var map_entries: Array[Dictionary] = []
var routes: Array[Vector2i] = []
var current_map_id: = 0
var _positions: Dictionary = {}
var _hovered_map_id: = 0


func configure(entries: Array[Dictionary], route_list: Array[Vector2i], active_map_id: int) -> void :
	map_entries = entries
	routes = route_list
	current_map_id = active_map_id
	_build_graph()


func _build_graph() -> void :
	_hovered_map_id = 0
	for child: Node in get_children():
		child.queue_free()

	_calculate_layout()
	custom_minimum_size = _calculate_content_size()
	size = custom_minimum_size

	for entry: Dictionary in map_entries:
		var map_id: = int(entry.get("id", 0))
		var map_node: = _create_map_node(
			map_id, 
			str(entry.get("name", "未知地圖")), 
			_build_connection_tooltip(map_id), 
			bool(entry.get("is_town", false)), 
			map_id == current_map_id
		)
		map_node.position = _positions.get(map_id, Vector2.ZERO)
		add_child(map_node)

	queue_redraw()


func _draw() -> void :
	draw_texture_rect(PARCHMENT_TEXTURE, Rect2(Vector2.ZERO, size), false, Color(0.92, 0.86, 0.72, 1.0))


	for route: Vector2i in routes:
		if not _positions.has(route.x) or not _positions.has(route.y):
			continue
		if _hovered_map_id != 0 and _route_contains_map(route, _hovered_map_id):
			continue
		var from_position: Vector2 = _positions[route.x] + NODE_SIZE * 0.5
		var to_position: Vector2 = _positions[route.y] + NODE_SIZE * 0.5
		var color: = ROUTE_DIM_COLOR if _hovered_map_id != 0 else ROUTE_COLOR
		draw_line(from_position, to_position, color, 2.0, true)

	if _hovered_map_id == 0:
		return
	for route: Vector2i in routes:
		if not _route_contains_map(route, _hovered_map_id):
			continue
		if not _positions.has(route.x) or not _positions.has(route.y):
			continue
		var from_position: Vector2 = _positions[route.x] + NODE_SIZE * 0.5
		var to_position: Vector2 = _positions[route.y] + NODE_SIZE * 0.5
		draw_line(from_position, to_position, Color(0.18, 0.08, 0.02, 0.8), 8.0, true)
		draw_line(from_position, to_position, ROUTE_HIGHLIGHT_COLOR, 4.0, true)


func _calculate_layout() -> void :
	_positions.clear()
	var connected_ids: Array[int] = []
	var connected_lookup: = {}
	for route: Vector2i in routes:
		connected_lookup[route.x] = true
		connected_lookup[route.y] = true

	for entry: Dictionary in map_entries:
		var map_id: = int(entry.get("id", 0))
		if connected_lookup.has(map_id):
			connected_ids.append(map_id)

	connected_ids.sort()
	var layout_size: = Vector2(1800.0, 1120.0)
	var center: = layout_size * 0.5
	var random: = RandomNumberGenerator.new()
	random.seed = 5782993836792435525
	for map_id: int in connected_ids:
		_positions[map_id] = Vector2(
			random.randf_range(160.0, layout_size.x - 160.0), 
			random.randf_range(120.0, layout_size.y - 120.0)
		)



	for iteration: int in 260:
		var forces: = {}
		for map_id: int in connected_ids:
			forces[map_id] = (center - (_positions[map_id] as Vector2)) * 0.006

		for first_index: int in connected_ids.size():
			for second_index: int in range(first_index + 1, connected_ids.size()):
				var first_id: = connected_ids[first_index]
				var second_id: = connected_ids[second_index]
				var delta: Vector2 = _positions[first_id] - _positions[second_id]
				var distance_squared: = maxf(delta.length_squared(), 900.0)
				var repulsion: = delta.normalized() * (2600000.0 / distance_squared)
				forces[first_id] += repulsion
				forces[second_id] -= repulsion

		for route: Vector2i in routes:
			if not forces.has(route.x) or not forces.has(route.y):
				continue
			var delta: Vector2 = _positions[route.y] - _positions[route.x]
			var distance: = maxf(delta.length(), 1.0)
			var attraction: = delta.normalized() * (distance - 245.0) * 0.018
			forces[route.x] += attraction
			forces[route.y] -= attraction

		var temperature: = lerpf(16.0, 2.0, float(iteration) / 259.0)
		for map_id: int in connected_ids:
			_positions[map_id] += (forces[map_id] as Vector2).limit_length(temperature)

	_normalize_layout(connected_ids, layout_size, Vector2(130.0, 110.0))
	_resolve_overlaps(connected_ids)
	_shift_layout_to_positive_area(connected_ids, Vector2(80.0, 70.0))


	for map_id: Variant in _positions.keys():
		_positions[map_id] = (_positions[map_id] as Vector2) - NODE_SIZE * 0.5


func _normalize_layout(map_ids: Array[int], layout_size: Vector2, margin: Vector2) -> void :
	if map_ids.is_empty():
		return
	var bounds: = Rect2(_positions[map_ids[0]], Vector2.ZERO)
	for map_id: int in map_ids:
		bounds = bounds.expand(_positions[map_id])
	var usable_size: = layout_size - margin * 2.0
	var source_size: = Vector2(maxf(bounds.size.x, 1.0), maxf(bounds.size.y, 1.0))
	var scale_factor: = minf(usable_size.x / source_size.x, usable_size.y / source_size.y)
	var scaled_size: = source_size * scale_factor
	var offset: = margin + (usable_size - scaled_size) * 0.5
	for map_id: int in map_ids:
		_positions[map_id] = offset + ((_positions[map_id] as Vector2) - bounds.position) * scale_factor


func _resolve_overlaps(map_ids: Array[int]) -> void :
	var separation_size: = NODE_SIZE + NODE_GAP
	for _iteration: int in 220:
		var moved: = false
		for first_index: int in map_ids.size():
			for second_index: int in range(first_index + 1, map_ids.size()):
				var first_id: = map_ids[first_index]
				var second_id: = map_ids[second_index]
				var delta: Vector2 = _positions[second_id] - _positions[first_id]
				var overlap_x: = separation_size.x - absf(delta.x)
				var overlap_y: = separation_size.y - absf(delta.y)
				if overlap_x <= 0.0 or overlap_y <= 0.0:
					continue
				moved = true
				if overlap_x < overlap_y:
					var direction_x: = 1.0 if delta.x >= 0.0 else -1.0
					var push_x: = (overlap_x + 0.5) * 0.5 * direction_x
					_positions[first_id].x -= push_x
					_positions[second_id].x += push_x
				else:
					var direction_y: = 1.0 if delta.y >= 0.0 else -1.0
					var push_y: = (overlap_y + 0.5) * 0.5 * direction_y
					_positions[first_id].y -= push_y
					_positions[second_id].y += push_y
		if not moved:
			break


func _shift_layout_to_positive_area(map_ids: Array[int], margin: Vector2) -> void :
	if map_ids.is_empty():
		return
	var minimum: = _positions[map_ids[0]] as Vector2
	for map_id: int in map_ids:
		minimum.x = minf(minimum.x, (_positions[map_id] as Vector2).x)
		minimum.y = minf(minimum.y, (_positions[map_id] as Vector2).y)
	var shift: = margin - minimum
	for map_id: int in map_ids:
		_positions[map_id] += shift


func _calculate_content_size() -> Vector2:
	var required_size: = Vector2(2000.0, 1500.0)
	for position: Vector2 in _positions.values():
		required_size.x = maxf(required_size.x, position.x + NODE_SIZE.x + 80.0)
		required_size.y = maxf(required_size.y, position.y + NODE_SIZE.y + 80.0)
	return required_size


func _build_connection_tooltip(map_id: int) -> String:
	var connected_ids: Array[int] = []
	for route: Vector2i in routes:
		var connected_id: = 0
		if route.x == map_id:
			connected_id = route.y
		elif route.y == map_id:
			connected_id = route.x
		if connected_id != 0 and connected_id not in connected_ids:
			connected_ids.append(connected_id)
	connected_ids.sort()

	var connected_names: Array[String] = []
	for connected_id: int in connected_ids:
		for entry: Dictionary in map_entries:
			if int(entry.get("id", 0)) == connected_id:
				connected_names.append(str(entry.get("name", "未知地圖")))
				break
	return "相連地圖：\n%s" % "\n".join(connected_names)


func _create_map_node(map_id: int, map_name: String, connection_tooltip: String, is_town: bool, is_current: bool) -> Control:
	var map_node: = Control.new()
	map_node.name = "Map%d" % map_id
	map_node.set_meta("map_id", map_id)
	map_node.size = NODE_SIZE
	map_node.tooltip_text = connection_tooltip
	map_node.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND

	if is_current:
		var halo: = Panel.new()
		halo.name = "CurrentHalo"
		halo.position = Vector2(43.0, -7.0)
		halo.size = Vector2(104.0, 104.0)
		halo.mouse_filter = Control.MOUSE_FILTER_IGNORE
		var halo_style: = StyleBoxFlat.new()
		halo_style.bg_color = Color(1.0, 0.82, 0.28, 0.12)
		halo_style.border_color = Color(1.0, 0.82, 0.28, 0.95)
		halo_style.set_border_width_all(4)
		halo_style.set_corner_radius_all(52)
		halo_style.shadow_color = Color(1.0, 0.68, 0.18, 0.55)
		halo_style.shadow_size = 10
		halo.add_theme_stylebox_override("panel", halo_style)
		map_node.add_child(halo)

	var emblem: = TextureRect.new()
	emblem.name = "Emblem"
	emblem.position = Vector2(51.0, 0.0)
	emblem.size = Vector2(88.0, 88.0)
	emblem.texture = TOWN_TEXTURE if is_town else WILDERNESS_TEXTURE
	emblem.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	emblem.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	emblem.mouse_filter = Control.MOUSE_FILTER_IGNORE
	map_node.add_child(emblem)

	var name_panel: = Panel.new()
	name_panel.name = "NamePanel"
	name_panel.position = Vector2(2.0, 96.0)
	name_panel.size = Vector2(186.0, 46.0)
	name_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var panel_style: = StyleBoxFlat.new()
	panel_style.bg_color = Color(0.2, 0.105, 0.045, 0.94)
	panel_style.border_color = CURRENT_COLOR if is_current else (TOWN_COLOR if is_town else Color("9b7448"))
	panel_style.set_border_width_all(3 if is_current else 2)
	panel_style.set_corner_radius_all(8)
	panel_style.shadow_color = Color(0.08, 0.035, 0.01, 0.65)
	panel_style.shadow_size = 5
	name_panel.add_theme_stylebox_override("panel", panel_style)
	map_node.add_child(name_panel)

	var label: = Label.new()
	label.name = "MapName"
	label.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	label.offset_left = 7.0
	label.offset_right = -7.0
	label.text = map_name if not map_name.strip_edges().is_empty() else "未知地圖"
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	label.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	label.add_theme_color_override("font_color", Color("f7e5bd"))
	label.add_theme_color_override("font_shadow_color", Color(0.05, 0.02, 0.0, 0.9))
	label.add_theme_constant_override("shadow_offset_x", 1)
	label.add_theme_constant_override("shadow_offset_y", 2)
	label.add_theme_font_size_override("font_size", 14)
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	name_panel.add_child(label)

	map_node.mouse_entered.connect( func():
		_on_map_node_hovered(map_id)
		var tween: = map_node.create_tween()
		tween.tween_property(map_node, "scale", Vector2(1.06, 1.06), 0.1)
	)
	map_node.mouse_exited.connect( func():
		_on_map_node_unhovered(map_id)
		var tween: = map_node.create_tween()
		tween.tween_property(map_node, "scale", Vector2.ONE, 0.1)
	)
	map_node.pivot_offset = NODE_SIZE * 0.5
	return map_node


func _on_map_node_hovered(map_id: int) -> void :
	_hovered_map_id = map_id
	_update_connection_focus()
	queue_redraw()


func _on_map_node_unhovered(map_id: int) -> void :
	if _hovered_map_id != map_id:
		return
	_hovered_map_id = 0
	_update_connection_focus()
	queue_redraw()


func _update_connection_focus() -> void :
	var related_ids: = {}
	if _hovered_map_id != 0:
		related_ids[_hovered_map_id] = true
		for route: Vector2i in routes:
			if route.x == _hovered_map_id:
				related_ids[route.y] = true
			elif route.y == _hovered_map_id:
				related_ids[route.x] = true

	for child: Node in get_children():
		if not (child is Control) or not child.has_meta("map_id"):
			continue
		var map_node: = child as Control
		var map_id: = int(map_node.get_meta("map_id"))
		var is_related: = _hovered_map_id == 0 or related_ids.has(map_id)
		map_node.modulate = Color.WHITE if is_related else Color(0.42, 0.36, 0.28, 0.48)
		map_node.z_index = 2 if map_id == _hovered_map_id else (1 if is_related else 0)


func _route_contains_map(route: Vector2i, map_id: int) -> bool:
	return route.x == map_id or route.y == map_id
