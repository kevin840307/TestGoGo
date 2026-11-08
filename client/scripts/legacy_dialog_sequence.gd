extends RefCounted

# Keeps Lua Say()/Ask() packets from the same script execution in order.
# Does not call the server or change callback targets; the HUD advances it
# only on an explicit user click or option selection.
var _active := false
var _entity_id := 0
var _script_name := ""
var _pending: Array[Dictionary] = []


func push_or_begin(packet: Dictionary) -> bool:
	var entity_id := int(packet.get("EntityID", 0))
	var script_name := str(packet.get("ScriptName", ""))
	if not _active or entity_id != _entity_id or script_name != _script_name:
		reset()
		_active = true
		_entity_id = entity_id
		_script_name = script_name
		return true

	_pending.append(packet.duplicate(true))
	return false


func defer_close() -> bool:
	if not _active:
		return false
	_pending.append({"DialogType": "Close"})
	return true


func has_pending() -> bool:
	return not _pending.is_empty()


func take_next() -> Dictionary:
	if _pending.is_empty():
		return {}
	return _pending.pop_front()


func is_active() -> bool:
	return _active


func reset() -> void:
	_active = false
	_entity_id = 0
	_script_name = ""
	_pending.clear()
