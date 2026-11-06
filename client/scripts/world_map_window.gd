extends Control

signal close_requested

const MAP_CONFIG_PATH: = "res://Assets/GameInfo/MapSpawnConfig.json"
const TOWN_MAP_IDS: = [4, 9, 15, 17, 22, 39, 53, 75, 93]
const INITIAL_FOCUS_MAP_ID: = 15
const MAP_NAME_OVERRIDES: = {
	8: "廢棄供水裝置", 
	14: "廢水處理廠/古可山洞", 
	16: "皇宮大殿/大會議廳/大聖殿內部/戰團小屋", 
	21: "廢棄機甲工廠"
}



const ROUTES: = [
	[1, 2], [1, 42], [1, 49], [4, 12], [4, 15], [4, 34], [4, 40], [4, 42], 
	[7, 8], [7, 9], [7, 19], [9, 40], [9, 47], [12, 45], [12, 66], 
	[13, 14], [13, 15], [13, 31], [15, 16], [15, 19], [15, 27], 
	[15, 29], [15, 39], [15, 40], [15, 47], [15, 49], [15, 51], 
	[15, 93], [17, 38], [17, 40], [19, 20], [21, 38], [22, 25], 
	[22, 40], [25, 27], [27, 28], [29, 38], [31, 32], [31, 39], 
	[31, 73], [33, 34], [34, 35], [39, 40], [39, 43], [39, 44], 
	[39, 45], [40, 53], [40, 75], [51, 52], [51, 53], [53, 68], 
	[68, 76], [75, 78], [75, 79], [75, 99], [77, 79], [86, 94], 
	[93, 94], [99, 100]
]

@onready var graph = $WindowPanel / Content / ScrollContainer / WorldMapGraph
@onready var scroll_container: ScrollContainer = $WindowPanel / Content / ScrollContainer

var current_map_id: = 0
var _map_names: Dictionary = {}


func _ready() -> void :
	$WindowPanel / Header / BtnClose.pressed.connect( func(): close_requested.emit())
	_load_map_names()


func set_current_map(map_id: int) -> void :
	current_map_id = map_id
	_rebuild_graph()
	call_deferred("_center_map", INITIAL_FOCUS_MAP_ID)


func _rebuild_graph() -> void :
	if not is_instance_valid(graph):
		return
	var available_map_ids: = {}
	for map_id: int in range(1, 1000):
		if ResourceLoader.exists("res://maps/Map%02d.scn" % map_id):
			available_map_ids[map_id] = true

	var route_list: Array[Vector2i] = []
	var routed_map_ids: = {}
	for route: Array in ROUTES:
		var from_map_id: = int(route[0])
		var to_map_id: = int(route[1])
		if not available_map_ids.has(from_map_id) or not available_map_ids.has(to_map_id):
			continue
		route_list.append(Vector2i(from_map_id, to_map_id))
		routed_map_ids[from_map_id] = true
		routed_map_ids[to_map_id] = true

	var entries: Array[Dictionary] = []
	var routed_ids: Array = routed_map_ids.keys()
	routed_ids.sort()
	for raw_map_id: Variant in routed_ids:
		var map_id: = int(raw_map_id)
		entries.append({
			"id": map_id, 
			"name": str(MAP_NAME_OVERRIDES.get(map_id, _map_names.get(map_id, "未知地圖"))), 
			"is_town": map_id in TOWN_MAP_IDS
		})
	entries.sort_custom( func(a: Dictionary, b: Dictionary): return int(a["id"]) < int(b["id"]))
	graph.configure(entries, route_list, current_map_id)


func _load_map_names() -> void :
	_map_names.clear()
	if not FileAccess.file_exists(MAP_CONFIG_PATH):
		return
	var file: = FileAccess.open(MAP_CONFIG_PATH, FileAccess.READ)
	if file == null:
		return
	var parsed: Variant = JSON.parse_string(file.get_as_text())
	if typeof(parsed) != TYPE_DICTIONARY:
		return
	for raw_id: Variant in parsed.keys():
		var id_text: = str(raw_id)
		if not id_text.is_valid_int() or typeof(parsed[raw_id]) != TYPE_DICTIONARY:
			continue
		var entry: Dictionary = parsed[raw_id]
		var map_name: = str(entry.get("MapName", "")).strip_edges()
		if not map_name.is_empty():
			_map_names[id_text.to_int()] = map_name


func _center_map(map_id: int) -> void :
	var map_node: = graph.get_node_or_null("Map%d" % map_id) as Control
	if map_node == null:
		return
	var target: = map_node.position + map_node.size * 0.5 - scroll_container.size * 0.5
	scroll_container.scroll_horizontal = maxi(0, int(target.x))
	scroll_container.scroll_vertical = maxi(0, int(target.y))
