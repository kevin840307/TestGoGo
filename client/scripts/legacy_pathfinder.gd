extends Node

const CELL_SIZE: int = 16
const BLOCK_MASK: int = 0x07
const SEARCH_WINDOW_CELLS: int = 50
const HALF_SEARCH_WINDOW_CELLS: int = 25
const MAX_GOAL_SEARCH_RADIUS: int = 8

var _map_cache: Dictionary = {}
var _query_can_cross_special_terrain: bool = false

func find_path(
	map_id: int,
	start_world: Vector2,
	target_world: Vector2,
	can_cross_special_terrain: bool = false
) -> PackedVector2Array:
	_query_can_cross_special_terrain = can_cross_special_terrain
	var grid: Dictionary = _get_grid(map_id)
	if grid.is_empty():
		return PackedVector2Array()

	if _is_segment_walkable(grid, start_world, target_world):
		return PackedVector2Array([target_world])

	var start_cell: Vector2i = _world_to_cell(start_world)
	var goal_cell: Vector2i = _world_to_cell(target_world)

	if not _is_inside(grid, start_cell):
		return PackedVector2Array()

	if not _is_inside(grid, goal_cell) or _is_blocked(grid, goal_cell):
		var nearest: Vector2i = _find_nearest_walkable(grid, goal_cell, MAX_GOAL_SEARCH_RADIUS)
		if nearest.x < 0:
			return PackedVector2Array()
		goal_cell = nearest

	var cell_path: Array[Vector2i] = _find_segmented_path(grid, start_cell, goal_cell)
	if cell_path.size() < 2:
		return PackedVector2Array()

	var result := PackedVector2Array()
	for i: int in range(1, cell_path.size()):
		result.append(_cell_center(cell_path[i]))

	if not result.is_empty() and _is_segment_walkable(grid, result[result.size() - 1], target_world):
		result.append(target_world)

	return _smooth_path(grid, start_world, result)


func is_segment_walkable(
	map_id: int,
	from_world: Vector2,
	to_world: Vector2,
	can_cross_special_terrain: bool = false
) -> bool:
	_query_can_cross_special_terrain = can_cross_special_terrain
	var grid: Dictionary = _get_grid(map_id)
	if grid.is_empty():
		return false
	return _is_segment_walkable(grid, from_world, to_world)


func is_walkable(
	map_id: int,
	world_position: Vector2,
	can_cross_special_terrain: bool = false
) -> bool:
	_query_can_cross_special_terrain = can_cross_special_terrain
	var grid: Dictionary = _get_grid(map_id)
	if grid.is_empty():
		return false
	return not _is_blocked(grid, _world_to_cell(world_position))


func _get_grid(map_id: int) -> Dictionary:
	if _map_cache.has(map_id):
		return _map_cache[map_id] as Dictionary

	var map_name: String = "Map%02d" % map_id
	var legacy_relative: String = "../Old/服務端/Dat/Map/%s/Blocking.dat" % map_name
	var legacy_absolute: String = ProjectSettings.globalize_path("res://").path_join(legacy_relative)
	var candidates: Array[String] = [
		"res://legacy_map/%s/Blocking.dat" % map_name,
		legacy_absolute,
	]

	var file: FileAccess = null
	var selected_path: String = ""
	for candidate: String in candidates:
		if FileAccess.file_exists(candidate):
			file = FileAccess.open(candidate, FileAccess.READ)
			selected_path = candidate
			break

	if file == null:
		_map_cache[map_id] = {}
		push_warning("[LegacyPathfinder] Blocking.dat not found for %s" % map_name)
		return {}

	if file.get_length() < 8:
		_map_cache[map_id] = {}
		push_warning("[LegacyPathfinder] Invalid Blocking.dat: %s" % selected_path)
		return {}

	var width: int = file.get_32()
	var height: int = file.get_32()
	var expected: int = width * height
	if width <= 0 or height <= 0 or expected <= 0 or file.get_length() < 8 + expected:
		_map_cache[map_id] = {}
		push_warning("[LegacyPathfinder] Invalid blocking dimensions: %s" % selected_path)
		return {}

	var cells: PackedByteArray = file.get_buffer(expected)
	var grid: Dictionary = {
		"width": width,
		"height": height,
		"cells": cells,
		"path": selected_path,
	}
	_map_cache[map_id] = grid
	return grid


func _world_to_cell(pos: Vector2) -> Vector2i:
	return Vector2i(floori(pos.x / float(CELL_SIZE)), floori(pos.y / float(CELL_SIZE)))


func _cell_center(cell: Vector2i) -> Vector2:
	return Vector2(
		float(cell.x * CELL_SIZE) + float(CELL_SIZE) * 0.5,
		float(cell.y * CELL_SIZE) + float(CELL_SIZE) * 0.5
	)


func _is_inside(grid: Dictionary, cell: Vector2i) -> bool:
	var width: int = int(grid.get("width", 0))
	var height: int = int(grid.get("height", 0))
	return cell.x >= 0 and cell.y >= 0 and cell.x < width and cell.y < height


func _is_blocked(grid: Dictionary, cell: Vector2i) -> bool:
	if not _is_inside(grid, cell):
		return true
	var width: int = int(grid.get("width", 0))
	var cells: PackedByteArray = grid.get("cells", PackedByteArray()) as PackedByteArray
	var index: int = cell.y * width + cell.x
	if index < 0 or index >= cells.size():
		return true

	var flags: int = int(cells[index]) & BLOCK_MASK
	if flags == 0:
		return false

	return not (_query_can_cross_special_terrain and flags == 3)


func _is_segment_walkable(grid: Dictionary, from_world: Vector2, to_world: Vector2) -> bool:
	var distance: float = from_world.distance_to(to_world)
	if distance <= 0.001:
		return not _is_blocked(grid, _world_to_cell(from_world))

	var samples: int = maxi(1, ceili(distance / (float(CELL_SIZE) * 0.5)))
	for i: int in range(samples + 1):
		var t: float = float(i) / float(samples)
		var pos: Vector2 = from_world.lerp(to_world, t)
		if _is_blocked(grid, _world_to_cell(pos)):
			return false
	return true


func _find_nearest_walkable(grid: Dictionary, origin: Vector2i, radius: int) -> Vector2i:
	if _is_inside(grid, origin) and not _is_blocked(grid, origin):
		return origin

	for r: int in range(1, radius + 1):
		for y: int in range(origin.y - r, origin.y + r + 1):
			for x: int in range(origin.x - r, origin.x + r + 1):
				if abs(x - origin.x) != r and abs(y - origin.y) != r:
					continue
				var candidate := Vector2i(x, y)
				if _is_inside(grid, candidate) and not _is_blocked(grid, candidate):
					return candidate
	return Vector2i(-1, -1)


func _find_segmented_path(grid: Dictionary, start: Vector2i, goal: Vector2i) -> Array[Vector2i]:
	var complete: Array[Vector2i] = [start]
	var current: Vector2i = start
	var safety: int = 0

	while current != goal and safety < 64:
		safety += 1
		var delta := Vector2(goal.x - current.x, goal.y - current.y)
		var segment_goal: Vector2i = goal

		if absi(goal.x - current.x) >= SEARCH_WINDOW_CELLS - 6 or absi(goal.y - current.y) >= SEARCH_WINDOW_CELLS - 6:
			if delta.length_squared() <= 0.001:
				break
			var direction: Vector2 = delta.normalized()
			var advance: float = float(SEARCH_WINDOW_CELLS - 8)
			segment_goal = Vector2i(
				current.x + roundi(direction.x * advance),
				current.y + roundi(direction.y * advance)
			)
			if _is_blocked(grid, segment_goal):
				var nearest: Vector2i = _find_nearest_walkable(grid, segment_goal, MAX_GOAL_SEARCH_RADIUS)
				if nearest.x < 0:
					return []
				segment_goal = nearest

		var part: Array[Vector2i] = _find_path(grid, current, segment_goal)
		if part.size() < 2:
			return []

		for i: int in range(1, part.size()):
			complete.append(part[i])

		var next_current: Vector2i = part[part.size() - 1]
		if next_current == current:
			return []
		current = next_current

	return complete if current == goal else []


func _find_path(grid: Dictionary, start: Vector2i, goal: Vector2i) -> Array[Vector2i]:
	if start == goal:
		return [start]

	var min_x: int = maxi(0, mini(start.x, goal.x) - 3)
	var min_y: int = maxi(0, mini(start.y, goal.y) - 3)
	var max_x: int = mini(int(grid.get("width", 0)) - 1, maxi(start.x, goal.x) + 3)
	var max_y: int = mini(int(grid.get("height", 0)) - 1, maxi(start.y, goal.y) + 3)

	if max_x - min_x + 1 > SEARCH_WINDOW_CELLS:
		var center_x: int = floori(float(start.x + goal.x) / 2.0)
		min_x = maxi(0, center_x - HALF_SEARCH_WINDOW_CELLS)
		max_x = mini(int(grid.get("width", 0)) - 1, min_x + SEARCH_WINDOW_CELLS - 1)
		min_x = maxi(0, max_x - SEARCH_WINDOW_CELLS + 1)

	if max_y - min_y + 1 > SEARCH_WINDOW_CELLS:
		var center_y: int = floori(float(start.y + goal.y) / 2.0)
		min_y = maxi(0, center_y - HALF_SEARCH_WINDOW_CELLS)
		max_y = mini(int(grid.get("height", 0)) - 1, min_y + SEARCH_WINDOW_CELLS - 1)
		min_y = maxi(0, max_y - SEARCH_WINDOW_CELLS + 1)

	if goal.x < min_x or goal.x > max_x or goal.y < min_y or goal.y > max_y:
		return []

	var open: Array[Vector2i] = [start]
	var closed: Dictionary = {}
	var came_from: Dictionary = {}
	var g_score: Dictionary = {start: 0}

	var neighbors: Array[Vector3i] = [
		Vector3i(1, 0, 10), Vector3i(-1, 0, 10),
		Vector3i(0, 1, 10), Vector3i(0, -1, 10),
		Vector3i(1, 1, 14), Vector3i(1, -1, 14),
		Vector3i(-1, 1, 14), Vector3i(-1, -1, 14),
	]

	while not open.is_empty():
		var best_index: int = 0
		var best_score: int = 2147483647
		for i: int in range(open.size()):
			var candidate: Vector2i = open[i]
			var candidate_g: int = int(g_score.get(candidate, 2147483647))
			var candidate_f: int = candidate_g + _heuristic(candidate, goal)
			if candidate_f < best_score:
				best_score = candidate_f
				best_index = i

		var current: Vector2i = open[best_index]
		open.remove_at(best_index)

		if current == goal:
			return _reconstruct_path(came_from, start, goal)

		closed[current] = true

		for spec: Vector3i in neighbors:
			var next := Vector2i(current.x + spec.x, current.y + spec.y)
			if next.x < min_x or next.x > max_x or next.y < min_y or next.y > max_y:
				continue
			if closed.has(next) or _is_blocked(grid, next):
				continue

			if spec.x != 0 and spec.y != 0:
				if _is_blocked(grid, Vector2i(current.x + spec.x, current.y)) or _is_blocked(grid, Vector2i(current.x, current.y + spec.y)):
					continue

			var new_g: int = int(g_score.get(current, 0)) + spec.z
			if new_g >= int(g_score.get(next, 2147483647)):
				continue

			came_from[next] = current
			g_score[next] = new_g
			if not open.has(next):
				open.append(next)

	return []


func _heuristic(a: Vector2i, b: Vector2i) -> int:
	var dx: int = absi(a.x - b.x)
	var dy: int = absi(a.y - b.y)
	var diagonal: int = mini(dx, dy)
	var straight: int = maxi(dx, dy) - diagonal
	return diagonal * 14 + straight * 10


func _reconstruct_path(came_from: Dictionary, start: Vector2i, goal: Vector2i) -> Array[Vector2i]:
	var path: Array[Vector2i] = [goal]
	var current: Vector2i = goal
	while current != start:
		if not came_from.has(current):
			return []
		current = Vector2i(came_from[current])
		path.append(current)
	path.reverse()
	return path


func _smooth_path(grid: Dictionary, start_world: Vector2, raw_path: PackedVector2Array) -> PackedVector2Array:
	if raw_path.size() <= 1:
		return raw_path

	var result := PackedVector2Array()
	var anchor: Vector2 = start_world
	var index: int = 0

	while index < raw_path.size():
		var furthest: int = index
		for candidate: int in range(index, raw_path.size()):
			if _is_segment_walkable(grid, anchor, raw_path[candidate]):
				furthest = candidate
			else:
				break

		result.append(raw_path[furthest])
		anchor = raw_path[furthest]
		index = furthest + 1

	return result
