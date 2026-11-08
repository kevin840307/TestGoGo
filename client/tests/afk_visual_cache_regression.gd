extends SceneTree

# godot --headless --path client --script res://tests/afk_visual_cache_regression.gd
# Real Godot integration checks, not a source-string-only assertion.
class TickProbe:
	extends Node
	var physics_ticks: int = 0
	func _physics_process(_delta: float) -> void:
		physics_ticks += 1


var failures: int = 0


func check(ok: bool, message: String) -> void:
	if ok:
		print("[AFK_CACHE_PASS] " + message)
	else:
		failures += 1
		push_error("[AFK_CACHE_FAIL] " + message)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var texture: Texture2D = load("res://ui/cursor/C_AUTOWALK.spr.png") as Texture2D
	check(is_instance_valid(texture), "real existing map-compatible texture imports")
	if not is_instance_valid(texture):
		quit(1)
		return

	var map_root: Node2D = Node2D.new()
	map_root.name = "TestMap"
	root.add_child(map_root)

	var static_sprite: Sprite2D = Sprite2D.new()
	static_sprite.name = "StaticMapDecoration"
	static_sprite.texture = texture
	map_root.add_child(static_sprite)

	var dynamic_actor: CharacterBody2D = CharacterBody2D.new()
	dynamic_actor.name = "LiveActor"
	map_root.add_child(dynamic_actor)
	var actor_sprite: Sprite2D = Sprite2D.new()
	actor_sprite.name = "ActorVisual"
	actor_sprite.texture = texture
	dynamic_actor.add_child(actor_sprite)

	var dynamic_drop: Area2D = Area2D.new()
	dynamic_drop.name = "LiveGroundDrop"
	map_root.add_child(dynamic_drop)
	var drop_sprite: Sprite2D = Sprite2D.new()
	drop_sprite.texture = texture
	dynamic_drop.add_child(drop_sprite)

	var animated: AnimatedSprite2D = AnimatedSprite2D.new()
	var animated_frames: SpriteFrames = load("res://Assets/Effects/Hit_Effect/Hit_Effect00.tres") as SpriteFrames
	check(is_instance_valid(animated_frames), "external animated SpriteFrames resource exists")
	if is_instance_valid(animated_frames):
		animated.sprite_frames = animated_frames
		map_root.add_child(animated)
		var animations: PackedStringArray = animated_frames.get_animation_names()
		if not animations.is_empty():
			animated.play(animations[0])

	var probe: TickProbe = TickProbe.new()
	map_root.add_child(probe)
	var physics_before: int = Engine.physics_ticks_per_second
	var cap_before: int = Engine.max_fps

	var cache: AfkVisualCache = AfkVisualCache.new()
	var stats: Dictionary = cache.suspend_static_map(map_root)
	check(int(stats.get("released", 0)) == (2 if is_instance_valid(animated_frames) else 1),
		"only static map textures/animations are released, never actor/drop graphics")
	check(static_sprite.texture == null, "static visual resource released")
	if is_instance_valid(animated_frames):
		check(animated.sprite_frames == null, "static animation resource released")
	check(actor_sprite.texture == texture and drop_sprite.texture == texture,
		"live character and ground-item visuals are untouched")
	check(dynamic_actor.get_parent() == map_root and dynamic_drop.get_parent() == map_root,
		"gameplay entities were not deleted or reparented")

	# Hidden map nodes must still run fixed physics; a low rendering cap must
	# not reduce the physics update rate or freeze gameplay.
	map_root.visible = false
	Engine.max_fps = 20
	await create_timer(0.4).timeout
	check(probe.physics_ticks >= 12,
		"hidden map continues fixed gameplay physics at a 20 FPS render cap")
	check(Engine.physics_ticks_per_second == physics_before,
		"render FPS selection has not changed the physics rate")

	var restore: Dictionary = await cache.restore_static_map(1)
	check(int(restore.get("restored", 0)) == (2 if is_instance_valid(animated_frames) else 1) and cache.suspended_count() == 0,
		"original resource is restored and snapshot discarded")
	check(static_sprite.texture != null,
		"static texture can be loaded back without recreating gameplay nodes")
	if is_instance_valid(animated_frames):
		check(animated.sprite_frames != null and animated.sprite_frames.has_animation(animated.animation),
			"static animated SpriteFrames restore the previous animation")
	check(dynamic_actor.get_parent() == map_root and dynamic_drop.get_parent() == map_root,
		"combat actor and pickup survive unload/reload")
	# If the server swaps maps during AFK, a view snapshot from Map A
	# cannot be replayed into Map B. New snapshots supersede the old set.
	var second_map: Node2D = Node2D.new()
	second_map.name = "SecondMap"
	root.add_child(second_map)
	var second_sprite: Sprite2D = Sprite2D.new()
	second_sprite.texture = texture
	second_map.add_child(second_sprite)
	var first_map_again: Dictionary = cache.suspend_static_map(map_root)
	check(int(first_map_again.get("released", 0)) == (2 if is_instance_valid(animated_frames) else 1),
		"old map can be stripped again before transition")
	var second_stats: Dictionary = cache.suspend_static_map(second_map)
	check(cache.tracked_map() == second_map and int(second_stats.get("released", 0)) == 1,
		"second map supersedes the old suspended resource list")
	var second_restore: Dictionary = await cache.restore_static_map(1)
	check(int(second_restore.get("restored", 0)) == 1 and second_sprite.texture != null,
		"only the new map is restored after map change")
	Engine.max_fps = cap_before
	second_map.queue_free()
	map_root.queue_free()
	await process_frame
	print("[AFK_CACHE_REGRESSION] %s" % ("ALL PASS" if failures == 0 else "FAILURES=%d" % failures))
	quit(0 if failures == 0 else 1)
