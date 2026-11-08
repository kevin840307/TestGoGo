extends RefCounted

# Pure input-state policy. No Scene, pathfinding, or network dependencies.
# Terrain long-hold latches continuous walking after button release; the
# next terrain click returns to ordinary one-click destination navigation.
const HOLD_TO_LATCH_MSEC: int = 500

var latched: bool = false
var holding: bool = false
var _press_started_msec: int = -1


func terrain_press(now_msec: int) -> bool:
	var was_latched: bool = latched
	latched = false
	holding = not was_latched
	_press_started_msec = now_msec if holding else -1
	return was_latched


func terrain_release(now_msec: int) -> bool:
	if holding and _press_started_msec >= 0:
		if now_msec - _press_started_msec >= HOLD_TO_LATCH_MSEC:
			latched = true
	holding = false
	_press_started_msec = -1
	return latched


func cancel() -> void:
	latched = false
	holding = false
	_press_started_msec = -1
