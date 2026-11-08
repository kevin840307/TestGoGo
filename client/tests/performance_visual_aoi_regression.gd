extends SceneTree

# Run: godot --headless --path client --script res://tests/performance_visual_aoi_regression.gd
# Server-free visual AOI regression. Authoritative packets never change.
const ENTITY_MANAGER = preload("res://scripts/network_entity_manager.gd")
var failures: int = 0


func check(ok: bool, label: String) -> void:
	if ok:
		print("[VISUAL_AOI_PASS] " + label)
	else:
		failures += 1
		push_error("[VISUAL_AOI_FAIL] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var previous_monster: float = INF
	var previous_ground: float = INF
	for mode: int in [
		ClientPerformanceProfile.LOAD_QUALITY,
		ClientPerformanceProfile.LOAD_BALANCED,
		ClientPerformanceProfile.LOAD_LOW,
		ClientPerformanceProfile.LOAD_MULTIBOX
	]:
		var profile: Dictionary = ClientPerformanceProfile.resolve(mode, ClientPerformanceProfile.COMBAT_NORMAL)
		var monster: Vector2 = ENTITY_MANAGER.resolve_visual_aoi_radii(profile, true, 0.0)
		var ground: Vector2 = ENTITY_MANAGER.resolve_visual_aoi_radii(profile, false, 0.0)
		check(monster.x < previous_monster, "lower quality reduces monster visual instance radius")
		check(ground.x < previous_ground, "lower quality reduces ground visual instance radius")
		check(monster.y >= monster.x + 400.0, "monster enter/exit hysteresis prevents churn")
		check(ground.y >= ground.x + 400.0, "ground enter/exit hysteresis prevents churn")
		previous_monster = monster.x
		previous_ground = ground.x

	var multibox: Dictionary = ClientPerformanceProfile.resolve(
		ClientPerformanceProfile.LOAD_MULTIBOX, ClientPerformanceProfile.COMBAT_NORMAL
	)
	var camera_monster: Vector2 = ENTITY_MANAGER.resolve_visual_aoi_radii(multibox, true, 3000.0)
	check(camera_monster.x >= 3000.0, "large camera still shows onscreen monsters")
	var camera_ground: Vector2 = ENTITY_MANAGER.resolve_visual_aoi_radii(multibox, false, 2800.0)
	check(camera_ground.x >= 2800.0, "large camera still shows clickable drops")

	var manager: Node = ENTITY_MANAGER.new()
	manager.call("_on_entity_spawned", {
		"EntityID": 90111, "EntityType": 1, "LocX": 8000.0, "LocY": 0.0,
		"ExtraData": {"CurrentHP": 55, "MaxHP": 100}
	})
	var templates: Dictionary = manager.get("_monster_spawn_templates") as Dictionary
	check(templates.has(90111), "distant server monster remains cached after visual deferral")
	manager.call("_on_entity_moved", {"EntityID": 90111, "X": 8200.0, "Y": 25.0, "SpeedTime": 0.25})
	check(float((templates.get(90111, {}) as Dictionary).get("LocX", -1.0)) == 8200.0,
		"deferred monster still applies latest authoritative movement")
	manager.call("_on_entity_despawned", {"EntityID": 90111})
	check(not templates.has(90111), "server DespawnEntity still removes client cache")
	manager.free()
	print("[VISUAL_AOI_REGRESSION] %s" % ("ALL PASS" if failures == 0 else "FAILURES=%d" % failures))
	quit(0 if failures == 0 else 1)
