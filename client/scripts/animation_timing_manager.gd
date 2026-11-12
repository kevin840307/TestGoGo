extends Node

const MANIFEST_PATH: = "res://Assets/AnimationTimingManifest.json"
const ACT_PLAYBACK_SCALE: = 1.5

# Recovered player AH/GH are one-frame, one-tick hit reactions at the
# original 50Hz controller cadence. Applying the global 1.5x scale reduces
# them to ~13.3ms, shorter than one 60Hz frame, so the hit can finish before
# it is visibly presented. Keep only these source actions at recovered rate.
const SOURCE_RATE_ACTIONS := {
	"AH": true,
	"GH": true,
}

var _tick_rate_hz: int = 0
var _resource_index: Dictionary = {}
var _controller_index: Dictionary = {}


func _ready() -> void :
	_load_manifest()


func is_loaded() -> bool:
	return _tick_rate_hz > 0


func get_tick_rate_hz() -> int:
	return _tick_rate_hz


func get_playback_scale() -> float:
	return ACT_PLAYBACK_SCALE


func get_effective_tick_rate_hz() -> float:
	return float(_tick_rate_hz) * ACT_PLAYBACK_SCALE


func get_action_tick_rate_hz(action: String, scaled: bool = true) -> float:
	if _tick_rate_hz <= 0:
		return 0.0
	if not scaled or SOURCE_RATE_ACTIONS.has(action):
		return float(_tick_rate_hz)
	return get_effective_tick_rate_hz()


func get_resource_timeline(resource_path: String, action: String) -> Array:
	return _get_timeline(_resource_index, resource_path, action)


func has_resource_timing(resource_path: String, action: String) -> bool:
	return not get_resource_timeline(resource_path, action).is_empty()


func get_controller_timeline(controller: String, action: String) -> Array:
	return _get_timeline(_controller_index, controller, action)


func has_controller_timing(controller: String, action: String) -> bool:
	return not get_controller_timeline(controller, action).is_empty()


func ticks_to_seconds(ticks: int) -> float:
	var effective_tick_rate = get_effective_tick_rate_hz()
	if effective_tick_rate <= 0.0 or ticks <= 0:
		return 0.0
	return float(ticks) / effective_tick_rate


func _get_timeline(index: Dictionary, key: String, action: String) -> Array:
	var actions = index.get(key, {})
	if not actions is Dictionary:
		return []
	var timeline = actions.get(action, [])
	if not timeline is Array:
		return []
	return timeline.duplicate(true)


func _load_manifest() -> void :
	_tick_rate_hz = 0
	_resource_index.clear()
	_controller_index.clear()
	if not FileAccess.file_exists(MANIFEST_PATH):
		push_warning("[AnimationTimingManager] Manifest not found: %s" % MANIFEST_PATH)
		return
	var file = FileAccess.open(MANIFEST_PATH, FileAccess.READ)
	if file == null:
		push_warning("[AnimationTimingManager] Unable to open manifest: %s" % MANIFEST_PATH)
		return
	var parsed = JSON.parse_string(file.get_as_text())
	if not parsed is Dictionary or int(parsed.get("format_version", 0)) != 1:
		push_warning("[AnimationTimingManager] Invalid manifest: %s" % MANIFEST_PATH)
		return
	var tick_rate = int(parsed.get("tick_rate_hz", 0))
	var entries = parsed.get("entries", [])
	var controller_entries = parsed.get("controller_entries", [])
	if tick_rate <= 0 or not entries is Array or not controller_entries is Array:
		push_warning("[AnimationTimingManager] Invalid manifest fields: %s" % MANIFEST_PATH)
		return
	_tick_rate_hz = tick_rate
	_index_entries(entries, "resource", _resource_index)
	_index_entries(controller_entries, "controller", _controller_index)


func _index_entries(entries: Array, key_name: String, target: Dictionary) -> void :
	for entry in entries:
		if not entry is Dictionary:
			continue
		var key = str(entry.get(key_name, ""))
		var actions = entry.get("actions", {})
		if key.is_empty() or not actions is Dictionary:
			continue
		target[key] = actions
