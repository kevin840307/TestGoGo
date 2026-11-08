extends SceneTree

# godot --headless --path client --script res://tests/multiclient_render_dedup_regression.gd
# No server, login, recipes or world map required.
const HUD_SCRIPT = preload("res://game_hud.gd")
const ENTITIES_SCRIPT = preload("res://scripts/network_entity_manager.gd")
var _failures: int = 0


func _check(ok: bool, label: String) -> void:
	if ok:
		print("[RENDER_DEDUP_PASS] " + label)
	else:
		_failures += 1
		push_error("[RENDER_DEDUP_FAIL] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_check(HUD_SCRIPT.should_redraw_minimap_world(true, false, 0.0),
		"moving player redraws the minimap world immediately")
	_check(HUD_SCRIPT.should_redraw_minimap_world(false, true, 0.0),
		"map changes and refocus force a minimap redraw")
	_check(not HUD_SCRIPT.should_redraw_minimap_world(false, false, 0.05),
		"stationary minimap does not redraw the static world every 50ms")
	_check(not HUD_SCRIPT.should_redraw_minimap_world(false, false, 0.9),
		"idle world map is still reused before the periodic refresh")
	_check(HUD_SCRIPT.should_redraw_minimap_world(false, false, 1.0),
		"stationary map periodically refreshes at one second")
	_check(
		(HUD_SCRIPT.MINIMAP_WORLD_CULL_MASK & ENTITIES_SCRIPT.DYNAMIC_ENTITY_VISIBILITY_LAYER) == 0,
		"dynamic monsters, players and ground items are excluded from static minimap texture"
	)

	# SpawnEntity made the canonical deep copy before an active monster is
	# created. Spawning from a slightly stale queued packet must not replace
	# newer MoveEntity coordinates or deep-clone nested ExtraData again.
	var manager: Node = ENTITIES_SCRIPT.new()
	var templates: Dictionary = manager.get("_monster_spawn_templates") as Dictionary
	var monster_id: int = 900090
	var newer: Dictionary = {
		"EntityID": monster_id, "EntityType": 1, "Name": "newer",
		"LocX": 200.0, "LocY": 300.0,
		"ExtraData": {"Items": [{"ID": 123}]},
	}
	var stale: Dictionary = {
		"EntityID": monster_id, "EntityType": 1, "Name": "stale",
		"LocX": 10.0, "LocY": 20.0,
		"ExtraData": {"Items": [{"ID": 999}]},
	}
	templates[monster_id] = newer
	manager.call("_spawn_entity_now", stale)
	var kept: Dictionary = templates.get(monster_id, {})
	_check(kept.get("Name") == "newer" and float(kept.get("LocX", 0)) == 200.0,
		"spawn queue preserves latest server monster coordinates and identity")
	_check(((kept.get("ExtraData", {}) as Dictionary).get("Items", []) as Array)[0]["ID"] == 123,
		"canonical monster metadata is not overwritten by queued data")

	# Defensive compatibility path: direct first-time creation still copies
	# packet metadata so callers cannot mutate a shared authoritative record.
	var fresh_id: int = monster_id + 1
	var fresh: Dictionary = {
		"EntityID": fresh_id, "EntityType": 1, "Name": "fresh",
		"LocX": 20.0, "LocY": 10.0, "ExtraData": {"Level": 3},
	}
	manager.call("_spawn_entity_now", fresh)
	fresh["ExtraData"]["Level"] = 999
	var fresh_cached: Dictionary = templates.get(fresh_id, {})
	_check(int((fresh_cached.get("ExtraData", {}) as Dictionary).get("Level", 0)) == 3,
		"a direct uncached monster packet still receives an isolated deep copy")
	manager.free()

	print("[RENDER_DEDUP_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	quit(0 if _failures == 0 else 1)
