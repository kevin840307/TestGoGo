extends Node

const CELL_SIZE: int = 16
const BLOCK_MASK: int = 0x07
const SEARCH_WINDOW_CELLS: int = 50
const HALF_SEARCH_WINDOW_CELLS: int = 25
const MAX_GOAL_SEARCH_RADIUS: int = 16
# Pathfinding runs on the main thread. A definitely unreachable target must
# never spend an unbounded amount of time exhausting progressively larger
# search windows and freeze the rendered frame.
const PATH_QUERY_BUDGET_USEC: int = 6000
const PATH_SLOW_LOG_USEC: int = 4000
const PATH_BUDGET_CHECK_INTERVAL: int = 32
const ADAPTIVE_SEARCH_SPECS: Array[Vector2i] = [
	Vector2i(3, 50),
	Vector2i(8, 80),
	Vector2i(16, 112),
	Vector2i(24, 144),
]
const PATH_NEIGHBORS: Array[Vector3i] = [
	Vector3i(1, 0, 10), Vector3i(-1, 0, 10),
	Vector3i(0, 1, 10), Vector3i(0, -1, 10),
	Vector3i(1, 1, 14), Vector3i(1, -1, 14),
	Vector3i(-1, 1, 14), Vector3i(-1, -1, 14),
]

var _map_cache: Dictionary = {}
var _query_can_cross_special_terrain: bool = false
var _path_query_deadline_usec: int = 0
var _path_query_expanded_cells: int = 0

func find_path(
	map_id: int,
	start_world: Vector2,
	target_world: Vector2,
	can_cross_special_terrain: bool = false
) -> PackedVector2Array:
	var query_started_usec: int = Time.get_ticks_usec()
	_path_query_deadline_usec = query_started_usec + PATH_QUERY_BUDGET_USEC
	_path_query_expanded_cells = 0
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
		_log_path_query_if_slow(query_started_usec, map_id, start_world, target_world, false)
		return PackedVector2Array()

	var result := PackedVector2Array()
	for i: int in range(1, cell_path.size()):
		result.append(_cell_center(cell_path[i]))

	if not result.is_empty() and _is_segment_walkable(grid, result[result.size() - 1], target_world):
		result.append(target_world)

	var smoothed: PackedVector2Array = _smooth_path(grid, start_world, result)
	_log_path_query_if_slow(query_started_usec, map_id, start_world, target_world, true)
	return smoothed


func _log_path_query_if_slow(
	started_usec: int,
	map_id: int,
	start_world: Vector2,
	target_world: Vector2,
	found: bool
) -> void:
	var elapsed_usec: int = Time.get_ticks_usec() - started_usec
	if elapsed_usec < PATH_SLOW_LOG_USEC:
		return
	print("[PATHFIND_TIMING] map=%d from=(%.0f,%.0f) to=(%.0f,%.0f) found=%s elapsed_ms=%.2f expanded=%d budget_hit=%s" % [
		map_id,
		start_world.x,
		start_world.y,
		target_world.x,
		target_world.y,
		str(found),
		float(elapsed_usec) / 1000.0,
		_path_query_expanded_cells,
		str(Time.get_ticks_usec() >= _path_query_deadline_usec)
	])


func _is_blocked_for_query(
	grid: Dictionary,
	cell: Vector2i,
	can_cross_special_terrain: bool
) -> bool:
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
	return not (can_cross_special_terrain and flags == 3)


func _is_segment_walkable_for_query(
	grid: Dictionary,
	from_world: Vector2,
	to_world: Vector2,
	can_cross_special_terrain: bool
) -> bool:
	var distance: float = from_world.distance_to(to_world)
	if distance <= 0.001:
		return not _is_blocked_for_query(
			grid,
			_world_to_cell(from_world),
			can_cross_special_terrain
		)
	var samples: int = maxi(1, ceili(distance / (float(CELL_SIZE) * 0.5)))
	for i: int in range(samples + 1):
		var t: float = float(i) / float(samples)
		var pos: Vector2 = from_world.lerp(to_world, t)
		if _is_blocked_for_query(
			grid,
			_world_to_cell(pos),
			can_cross_special_terrain
		):
			return false
	return true


func validate_world_path(
	map_id: int,
	start_world: Vector2,
	path: PackedVector2Array,
	can_cross_special_terrain: bool = false
) -> bool:
	var grid: Dictionary = _get_grid(map_id)
	if grid.is_empty():
		return false
	var from_world: Vector2 = start_world
	for waypoint: Vector2 in path:
		if not _is_segment_walkable_for_query(
			grid,
			from_world,
			waypoint,
			can_cross_special_terrain
		):
			return false
		from_world = waypoint
	return true


func _fallback_heap_push(heap: Array[Vector3i], cell: Vector2i, priority: int) -> void:
	var node := Vector3i(cell.x, cell.y, priority)
	heap.append(node)
	var index: int = heap.size() - 1
	while index > 0:
		var parent: int = (index - 1) / 2
		if heap[parent].z <= heap[index].z:
			break
		var tmp: Vector3i = heap[parent]
		heap[parent] = heap[index]
		heap[index] = tmp
		index = parent


func _fallback_heap_pop(heap: Array[Vector3i]) -> Vector3i:
	var root: Vector3i = heap[0]
	var last: Vector3i = heap.pop_back()
	if heap.is_empty():
		return root

	heap[0] = last
	var index: int = 0
	while true:
		var left: int = index * 2 + 1
		if left >= heap.size():
			break
		var right: int = left + 1
		var smallest: int = left
		if right < heap.size() and heap[right].z < heap[left].z:
			smallest = right
		if heap[index].z <= heap[smallest].z:
			break
		var tmp: Vector3i = heap[index]
		heap[index] = heap[smallest]
		heap[smallest] = tmp
		index = smallest
	return root


func find_large_detour_path_async(
	map_id: int,
	start_world: Vector2,
	target_world: Vector2,
	can_cross_special_terrain: bool = false,
	frame_budget_usec: int = 1500,
	max_expanded_cells: int = 45000
) -> PackedVector2Array:
	var grid: Dictionary = _get_grid(map_id)
	if grid.is_empty():
		return PackedVector2Array()

	var start: Vector2i = _world_to_cell(start_world)
	var goal: Vector2i = _world_to_cell(target_world)
	if not _is_inside(grid, start):
		return PackedVector2Array()

	if not _is_inside(grid, goal) or _is_blocked_for_query(
		grid,
		goal,
		can_cross_special_terrain
	):
		var found_goal := Vector2i(-1, -1)
		for radius: int in range(1, MAX_GOAL_SEARCH_RADIUS + 1):
			for y: int in range(goal.y - radius, goal.y + radius + 1):
				for x: int in range(goal.x - radius, goal.x + radius + 1):
					if abs(x - goal.x) != radius and abs(y - goal.y) != radius:
						continue
					var candidate := Vector2i(x, y)
					if _is_inside(grid, candidate) and not _is_blocked_for_query(
						grid,
						candidate,
						can_cross_special_terrain
					):
						found_goal = candidate
						break
				if found_goal.x >= 0:
					break
			if found_goal.x >= 0:
				break
		if found_goal.x < 0:
			return PackedVector2Array()
		goal = found_goal

	# Large-detour fallback is deliberately isolated from the normal fast path.
	# It expands incrementally and yields once its per-frame CPU budget is used.
	var open_heap: Array[Vector3i] = []
	_fallback_heap_push(open_heap, start, _heuristic(start, goal))
	var closed: Dictionary = {}
	var came_from: Dictionary = {}
	var g_score: Dictionary = {start: 0}
	var expanded: int = 0
	var frame_started_usec: int = Time.get_ticks_usec()

	while not open_heap.is_empty() and expanded < max_expanded_cells:
		if Time.get_ticks_usec() - frame_started_usec >= frame_budget_usec:
			await get_tree().process_frame
			frame_started_usec = Time.get_ticks_usec()

		var entry: Vector3i = _fallback_heap_pop(open_heap)
		var current := Vector2i(entry.x, entry.y)
		if closed.has(current):
			continue
		if current == goal:
			var cells: Array[Vector2i] = _reconstruct_path(came_from, start, goal)
			var raw := PackedVector2Array()
			for i: int in range(1, cells.size()):
				raw.append(_cell_center(cells[i]))
			if not raw.is_empty() and _is_segment_walkable_for_query(
				grid,
				raw[raw.size() - 1],
				target_world,
				can_cross_special_terrain
			):
				raw.append(target_world)

			# Keep smoothing conservative and query-local so other movement checks
			# cannot change this fallback's terrain mode while it is yielding.
			var smoothed := PackedVector2Array()
			var anchor_world: Vector2 = start_world
			var raw_index: int = 0
			while raw_index < raw.size():
				var furthest: int = raw_index
				for test_index: int in range(raw_index, raw.size()):
					if _is_segment_walkable_for_query(
						grid,
						anchor_world,
						raw[test_index],
						can_cross_special_terrain
					):
						furthest = test_index
					else:
						break
				smoothed.append(raw[furthest])
				anchor_world = raw[furthest]
				raw_index = furthest + 1

			if validate_world_path(
				map_id,
				start_world,
				smoothed,
				can_cross_special_terrain
			):
				print("[PATHFIND_FALLBACK] map=%d from=(%.0f,%.0f) to=(%.0f,%.0f) found=true expanded=%d waypoints=%d" % [
					map_id,
					start_world.x,
					start_world.y,
					target_world.x,
					target_world.y,
					expanded,
					smoothed.size()
				])
				return smoothed
			return PackedVector2Array()

		closed[current] = true
		expanded += 1

		for spec: Vector3i in PATH_NEIGHBORS:
			var next := Vector2i(current.x + spec.x, current.y + spec.y)
			if closed.has(next) or _is_blocked_for_query(
				grid,
				next,
				can_cross_special_terrain
			):
				continue

			if spec.x != 0 and spec.y != 0:
				if _is_blocked_for_query(
					grid,
					Vector2i(current.x + spec.x, current.y),
					can_cross_special_terrain
				) or _is_blocked_for_query(
					grid,
					Vector2i(current.x, current.y + spec.y),
					can_cross_special_terrain
				):
					continue

			var new_g: int = int(g_score.get(current, 0)) + spec.z
			if new_g >= int(g_score.get(next, 2147483647)):
				continue
			came_from[next] = current
			g_score[next] = new_g
			# Duplicate heap entries are intentional. If a cheaper path is found,
			# push a new priority and let the closed-set discard stale entries.
			# This avoids a costly decrease-key search and keeps the fallback O(log n).
			_fallback_heap_push(
				open_heap,
				next,
				new_g + _heuristic(next, goal)
			)

	print("[PATHFIND_FALLBACK] map=%d from=(%.0f,%.0f) to=(%.0f,%.0f) found=false expanded=%d" % [
		map_id,
		start_world.x,
		start_world.y,
		target_world.x,
		target_world.y,
		expanded
	])
	return PackedVector2Array()


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

		var part: Array[Vector2i] = _find_path_adaptive(grid, current, segment_goal)
		if part.size() < 2 and segment_goal != goal:
			var alternate_goal: Vector2i = _find_reachable_segment_goal(
				grid,
				current,
				segment_goal,
				goal
			)
			if alternate_goal.x >= 0:
				segment_goal = alternate_goal
				part = _find_path_adaptive(grid, current, segment_goal)
		if part.size() < 2:
			return []

		for i: int in range(1, part.size()):
			complete.append(part[i])

		var next_current: Vector2i = part[part.size() - 1]
		if next_current == current:
			return []
		current = next_current

	return complete if current == goal else []


func _find_reachable_segment_goal(
	grid: Dictionary,
	start: Vector2i,
	preferred: Vector2i,
	final_goal: Vector2i
) -> Vector2i:
	var best := Vector2i(-1, -1)
	var best_goal_distance: int = 2147483647
	for radius: int in [4, 8, 12]:
		if Time.get_ticks_usec() >= _path_query_deadline_usec:
			return best
		for y: int in range(preferred.y - radius, preferred.y + radius + 1):
			for x: int in range(preferred.x - radius, preferred.x + radius + 1):
				if abs(x - preferred.x) != radius and abs(y - preferred.y) != radius:
					continue
				var candidate := Vector2i(x, y)
				if not _is_inside(grid, candidate) or _is_blocked(grid, candidate):
					continue
				var goal_distance: int = _heuristic(candidate, final_goal)
				if goal_distance >= best_goal_distance:
					continue
				var candidate_path: Array[Vector2i] = _find_path_adaptive(grid, start, candidate)
				if candidate_path.size() < 2:
					continue
				best = candidate
				best_goal_distance = goal_distance
		if best.x >= 0:
			return best
	return best


func _find_path_adaptive(grid: Dictionary, start: Vector2i, goal: Vector2i) -> Array[Vector2i]:
	if start == goal:
		return [start]

	for spec: Vector2i in ADAPTIVE_SEARCH_SPECS:
		if Time.get_ticks_usec() >= _path_query_deadline_usec:
			return []
		var path: Array[Vector2i] = _find_path_with_limits(
			grid,
			start,
			goal,
			spec.x,
			spec.y
		)
		if path.size() >= 2:
			return path

	return []


func _find_path_with_limits(
	grid: Dictionary,
	start: Vector2i,
	goal: Vector2i,
	margin: int,
	window_cells: int
) -> Array[Vector2i]:
	if start == goal:
		return [start]

	var grid_width: int = int(grid.get("width", 0))
	var grid_height: int = int(grid.get("height", 0))
	var min_x: int = maxi(0, mini(start.x, goal.x) - margin)
	var min_y: int = maxi(0, mini(start.y, goal.y) - margin)
	var max_x: int = mini(grid_width - 1, maxi(start.x, goal.x) + margin)
	var max_y: int = mini(grid_height - 1, maxi(start.y, goal.y) + margin)

	if max_x - min_x + 1 > window_cells:
		var center_x: int = floori(float(start.x + goal.x) / 2.0)
		var half_window: int = window_cells / 2
		min_x = maxi(0, center_x - half_window)
		max_x = mini(grid_width - 1, min_x + window_cells - 1)
		min_x = maxi(0, max_x - window_cells + 1)

	if max_y - min_y + 1 > window_cells:
		var center_y: int = floori(float(start.y + goal.y) / 2.0)
		var half_window: int = window_cells / 2
		min_y = maxi(0, center_y - half_window)
		max_y = mini(grid_height - 1, min_y + window_cells - 1)
		min_y = maxi(0, max_y - window_cells + 1)

	if goal.x < min_x or goal.x > max_x or goal.y < min_y or goal.y > max_y:
		return []

	var open: Array[Vector2i] = [start]
	# Membership checks were previously Array.has(next), which is O(n) for
	# every expanded neighbor. Keep the existing stable ordering, but track
	# membership separately in O(1) without changing route selection.
	var open_set: Dictionary = {start: true}
	var closed: Dictionary = {}
	var came_from: Dictionary = {}
	var g_score: Dictionary = {start: 0}

	var expanded_since_budget_check: int = 0
	while not open.is_empty():
		_path_query_expanded_cells += 1
		expanded_since_budget_check += 1
		if expanded_since_budget_check >= PATH_BUDGET_CHECK_INTERVAL:
			expanded_since_budget_check = 0
			if Time.get_ticks_usec() >= _path_query_deadline_usec:
				return []

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
		open_set.erase(current)

		if current == goal:
			return _reconstruct_path(came_from, start, goal)

		closed[current] = true

		for spec: Vector3i in PATH_NEIGHBORS:
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
			if not open_set.has(next):
				open.append(next)
				open_set[next] = true

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
