extends SceneTree
# godot --headless --path client --script res://tests/world_visual_audit_regression.gd
const AUDIT = preload("res://scripts/world_visual_audit.gd")
var failures: int = 0

func expect(ok: bool, label: String) -> void:
	if ok:
		print("[WORLD_VISUAL_AUDIT_PASS] " + label)
	else:
		failures += 1
		push_error("[WORLD_VISUAL_AUDIT_FAIL] " + label)

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var empty: Dictionary = AUDIT.snapshot(null)
	expect(int(empty["nodes"]) == 0, "empty scene reports zero resources")
	var map := Node2D.new()
	root.add_child(map)
	var tex := ImageTexture.create_from_image(Image.create(4, 4, false, Image.FORMAT_RGBA8))
	var one := Sprite2D.new()
	one.texture = tex
	map.add_child(one)
	var two := Sprite2D.new()
	two.texture = tex
	map.add_child(two)
	var frames := SpriteFrames.new()
	frames.add_frame("default", tex)
	var animated := AnimatedSprite2D.new()
	animated.sprite_frames = frames
	map.add_child(animated)
	var hud := Control.new()
	map.add_child(hud)
	var before: Dictionary = AUDIT.snapshot(map)
	expect(before["nodes"] == 5 and before["sprite2d"] == 2,
		"counts map and duplicated sprites")
	expect(before["unique_textures"] == 1 and before["unique_sprite_frames"] == 1,
		"counts distinct referenced visual resources once")
	expect(before["controls"] == 1 and before["animated_sprites"] == 1,
		"distinguishes HUD-like nodes and animation resources")
	expect(one.texture == tex and two.texture == tex,
		"audit never mutates or unloads any graphics")
	map.queue_free()
	await process_frame
	quit(0 if failures == 0 else 1)
