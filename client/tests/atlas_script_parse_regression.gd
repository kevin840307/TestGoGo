extends SceneTree
# Minimal project: parse actual monster and paper doll loader GDScript without
# loading any recovered PNG files (the assets are intentionally not in Git).
const MONSTERS = preload("res://scripts/monster_loader.gd")
const DOLLS = preload("res://scripts/paper_doll_loader.gd")

func _initialize() -> void:
	var monster := MONSTERS.new()
	var doll := DOLLS.new()
	if not monster.has_method("get_monster_frames") or not doll.has_method("get_sprite_frames"):
		push_error("[ATLAS_SCRIPT_PARSE_FAIL] missing loader API")
		quit(1)
		return
	var frames := SpriteFrames.new()
	var atlas := AtlasTexture.new()
	atlas.region = Rect2(0, 0, 8, 8)
	frames.add_frame("default", atlas)
	if MONSTERS._atlas_from_sprite_frames(frames) != null:
		# This AtlasTexture has no backing atlas; null is expected.
		push_error("[ATLAS_SCRIPT_PARSE_FAIL] returned non-existent atlas")
		quit(1)
		return
	print("[ATLAS_SCRIPT_PARSE_PASS] Godot parsed both loaders and atlas helper")
	quit(0)
