extends SceneTree

# godot --headless --path client --script res://tests/simplified_monster_async_layout_regression.gd
# Reproduces an async _lock_ui_position() callback resuming after a mode switch
# has intentionally cleared SpriteFrames (previously a null has_animation crash).
var failures: int = 0


func check(ok: bool, message: String) -> void:
	if ok:
		print("[MONSTER_LAYOUT_PASS] " + message)
	else:
		failures += 1
		push_error("[MONSTER_LAYOUT_FAIL] " + message)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var monster: MonsterEntity = MonsterEntity.new()
	var sprite: AnimatedSprite2D = AnimatedSprite2D.new()
	sprite.name = "AnimatedSprite2D"
	var frames: SpriteFrames = SpriteFrames.new()
	frames.add_animation("IDLE_1")
	var image: Image = Image.create(8, 8, false, Image.FORMAT_RGBA8)
	image.fill(Color.WHITE)
	frames.add_frame("IDLE_1", ImageTexture.create_from_image(image))
	sprite.sprite_frames = frames
	monster.add_child(sprite)
	var ui: Control = Control.new()
	ui.name = "UIContainer"
	ui.size = Vector2(100, 40)
	monster.add_child(ui)
	var collision: CollisionShape2D = CollisionShape2D.new()
	collision.name = "CollisionShape2D"
	monster.add_child(collision)
	root.add_child(monster)
	# An in-flight request observes valid SpriteFrames before await.
	monster._lock_ui_position()
	# Mode switch clears SpriteFrames in the SAME frame.
	monster.set_performance_simplified(true)
	check(sprite.sprite_frames == null,
		"Simplification releases original monster SpriteFrames")
	check(not sprite.visible,
		"Simplified monster does not draw the original sprite")
	await process_frame
	check(monster._primitive_simplified_visual,
		"Async UI callback does not undo simplified render mode")
	check(ui.position == Vector2(-ui.size.x / 2.0, -105.0),
		"Old async UI callback does not overwrite simplified hitbox/name layout")
	check(monster._ui_visual_base == ui.position,
		"UI visual base and simplified position remain synchronized")
	# Repeating a call when frames are already null must be harmless.
	monster._lock_ui_position()
	await process_frame
	check(sprite.sprite_frames == null and monster._primitive_simplified_visual,
		"Repeated null-resource UI calculation is a safe no-op")
	monster.queue_free()
	await process_frame
	print("[MONSTER_LAYOUT_REGRESSION] %s" % ("ALL PASS" if failures == 0 else "FAILURES=%d" % failures))
	quit(0 if failures == 0 else 1)
