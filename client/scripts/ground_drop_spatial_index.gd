extends RefCounted

# Scene-less index for authoritative ground-drop snapshots.
const CELL_SIZE: float = 512.0
var records: Dictionary = {}
var cells: Dictionary = {}
var winners: Dictionary = {}
var dirty: bool = true
var last_center: Vector2 = Vector2.INF
var last_radius: float = -1.0
var last_cap: int = -1
var rebuild_count: int = 0
var last_visited_items: int = 0

func _cell(pos: Vector2) -> Vector2i:
	return Vector2i(floori(pos.x / CELL_SIZE), floori(pos.y / CELL_SIZE))

func upsert(id: int, packet: Dictionary) -> void:
	if id <= 0:
		return
	var pos := Vector2(float(packet.get("LocX", 0)), float(packet.get("LocY", 0)))
	var extra: Dictionary = packet.get("ExtraData", {})
	var item: int = int(extra.get("ItemIndex", 0))
	if records.has(id) and records[id]["pos"] == pos and records[id]["item"] == item:
		return
	erase(id)
	var key: Vector2i = _cell(pos)
	records[id] = {"pos": pos, "item": item, "cell": key}
	if not cells.has(key):
		cells[key] = {}
	(cells[key] as Dictionary)[id] = true
	dirty = true

func erase(id: int) -> void:
	if not records.has(id):
		return
	var key: Vector2i = records[id]["cell"]
	records.erase(id)
	if cells.has(key):
		(cells[key] as Dictionary).erase(id)
		if (cells[key] as Dictionary).is_empty():
			cells.erase(key)
	winners.erase(id)
	dirty = true

func clear() -> void:
	records.clear()
	cells.clear()
	winners.clear()
	dirty = true
	last_center = Vector2.INF
	last_radius = -1.0
	last_cap = -1
	last_visited_items = 0

func find_nearest(center: Vector2, radius: float, cap: int = 5) -> Dictionary:
	if not dirty and center == last_center and is_equal_approx(radius, last_radius) and cap == last_cap:
		return winners
	dirty = false
	last_center = center
	last_radius = radius
	last_cap = cap
	rebuild_count += 1
	last_visited_items = 0
	winners = {}
	if radius <= 0.0 or cap <= 0:
		return winners
	var lower := _cell(center - Vector2(radius, radius))
	var upper := _cell(center + Vector2(radius, radius))
	var grouped: Dictionary = {}
	for cy in range(lower.y, upper.y + 1):
		for cx in range(lower.x, upper.x + 1):
			var key := Vector2i(cx, cy)
			if not cells.has(key):
				continue
			for raw in (cells[key] as Dictionary):
				var id: int = int(raw)
				var record: Dictionary = records[id]
				last_visited_items += 1
				var distance: float = (record["pos"] as Vector2).distance_squared_to(center)
				if distance > radius * radius:
					continue
				var item: int = record["item"]
				if item <= 0:
					winners[id] = true
					continue
				var top: Array = grouped.get(item, [])
				var index: int = top.size()
				for i in range(top.size()):
					if distance < top[i][0] or (distance == top[i][0] and id < top[i][1]):
						index = i
						break
				if index >= cap:
					continue
				top.insert(index, [distance, id])
				if top.size() > cap:
					top.pop_back()
				grouped[item] = top
	for item in grouped:
		for pair in grouped[item]:
			winners[pair[1]] = true
	return winners
