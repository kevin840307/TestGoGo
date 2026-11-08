extends RefCounted

# Keeps Lua Say()/Ask() packets from the same script execution in order.
# Does not call the server or change callback targets; the HUD advances it
# only on an explicit user click or option selection.
var _active := false
var _entity_id := 0
var _script_name := ""
var _batch_id := ""
var _pending: Array[Dictionary] = []
var _awaiting_choice := false


func push_or_begin(packet: Dictionary) -> bool:
	var entity_id := int(packet.get("EntityID", 0))
	var script_name := str(packet.get("ScriptName", ""))
	var batch_id := str(packet.get("DialogBatchId", ""))
	if not _active or entity_id != _entity_id or script_name != _script_name or batch_id != _batch_id:
		reset()
		_active = true
		_entity_id = entity_id
		_script_name = script_name
		_batch_id = batch_id
		return true

	_pending.append(packet.duplicate(true))
	return false


# Lua QuestEnd() is often at the end of a function that just showed Ask().
# The open choice must remain actionable until the user presses one option.
func note_presented(packet: Dictionary) -> void:
	var options: Array = packet.get("Options", [])
	_awaiting_choice = (
		str(packet.get("DialogType", "")) == "Dialog" and
		not options.is_empty()
	)


func is_awaiting_choice() -> bool:
	return _active and _awaiting_choice


func defer_close() -> bool:
	if not _active:
		return false
	_pending.append({"DialogType": "Close"})
	return true


func has_pending() -> bool:
	return not _pending.is_empty()


func only_terminal_close_pending() -> bool:
	return _pending.size() == 1 and (
		str(_pending[0].get("DialogType", "")) == "Close"
	)


func take_next() -> Dictionary:
	if _pending.is_empty():
		return {}
	return _pending.pop_front()


# A Close packet from an older Lua execution must not dismiss the
# current dialogue, even if it came from the same NPC and script name.
func is_stale_close(packet: Dictionary) -> bool:
	if not _active:
		return false
	var incoming := str(packet.get("DialogBatchId", ""))
	return incoming != "" and _batch_id != "" and incoming != _batch_id


func is_active() -> bool:
	return _active


func reset() -> void:
	_active = false
	_entity_id = 0
	_script_name = ""
	_batch_id = ""
	_awaiting_choice = false
	_pending.clear()
