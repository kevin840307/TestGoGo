extends SceneTree

# godot --headless --path client --script res://tests/render_optimization_regression.gd
# No server or real items required.
const MONSTER_SCRIPT = preload("res://scripts/monster_entity.gd")
const NETWORK_ENTITY_SCRIPT = preload("res://scripts/network_entity_manager.gd")
const GROUND_SCENE = preload("res://ground_item_entity.tscn")
var _failures: int = 0


func _check(ok: bool, description: String) -> void:
	if ok:
		print("[RENDER_OPT_PASS] " + description)
	else:
		_failures += 1
		push_error("[RENDER_OPT_FAIL] " + description)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var hitbox_a: RectangleShape2D = MONSTER_SCRIPT.get_shared_monster_click_shape(Vector2(48, 72))
	var hitbox_b: RectangleShape2D = MONSTER_SCRIPT.get_shared_monster_click_shape(Vector2(48, 72))
	var hitbox_c: RectangleShape2D = MONSTER_SCRIPT.get_shared_monster_click_shape(Vector2(64, 72))
	_check(hitbox_a == hitbox_b, "same monster texture dimensions reuse one immutable hitbox")
	_check(hitbox_a != hitbox_c, "different monster texture dimensions keep distinct hitboxes")
	_check(hitbox_a.size == Vector2(48, 72), "shared hitbox preserves original click dimensions")

	var ground: Area2D = GROUND_SCENE.instantiate() as Area2D
	_check(ground != null, "ground item scene still instantiates")
	if ground == null:
		quit(1)
		return
	ground.position = Vector2(480, 560)
	root.add_child(ground)
	await process_frame
	var visual: Node2D = NETWORK_ENTITY_SCRIPT.get_ground_drop_bounce_sprite(ground)
	_check(visual != null, "loot bounce targets its visual Sprite2D child")
	if visual != null:
		var original_root_position: Vector2 = ground.global_position
		var final_sprite_offset: Vector2 = visual.position
		visual.position += Vector2(0.0, -40.0)
		_check(ground.global_position == original_root_position,
			"drop animation moves visual only, retaining authoritative global position")
		var t: Tween = ground.create_tween()
		t.tween_property(visual, "position", final_sprite_offset, 0.01)
		await t.finished
		_check(ground.global_position == original_root_position and visual.position == final_sprite_offset,
			"visual drop bounce returns to the sprite's original offset")
	_check(NETWORK_ENTITY_SCRIPT.get_ground_drop_bounce_sprite(null) == null,
		"visual lookup safely handles released drop entities")
	ground.queue_free()
	await process_frame

	# Source-level guard protects against accidental reintroduction of root
	# tweening, which the Server cannot validate against Blocking.dat.
	var entity_source: String = FileAccess.get_file_as_string("res://scripts/network_entity_manager.gd")
	_check(not entity_source.contains('tween_property(new_entity, "position"'),
		"no tween animates ground-item Area2D position")
	_check(not entity_source.contains('floor_pos = start_pos + Vector2(randf_range'),
		"old random land-outside-Blocking.dat bounce was removed")
	print("[RENDER_OPT_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	quit(0 if _failures == 0 else 1)
