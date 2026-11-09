extends SceneTree
# Minimal Godot fixture. Defer dynamic Script.load until SceneTree has
# registered project autoload globals AudioManager/AnimationTimingManager.
func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var monsters_script := load("res://scripts/monster_loader.gd") as GDScript
	var dolls_script := load("res://scripts/paper_doll_loader.gd") as GDScript
	if not is_instance_valid(monsters_script) or not is_instance_valid(dolls_script):
		push_error("[ATLAS_SCRIPT_PARSE_FAIL] missing or non-compilable loader scripts")
		quit(1)
		return
	var monster: Node = monsters_script.new() as Node
	var doll: Node = dolls_script.new() as Node
	if not is_instance_valid(monster) or not is_instance_valid(doll):
		push_error("[ATLAS_SCRIPT_PARSE_FAIL] cannot instantiate loaders")
		quit(1)
		return
	if not monster.has_method("get_monster_frames") or not doll.has_method("get_sprite_frames"):
		push_error("[ATLAS_SCRIPT_PARSE_FAIL] missing loader API")
		quit(1)
		return
	var frames := SpriteFrames.new()
	var atlas := AtlasTexture.new()
	atlas.region = Rect2(0, 0, 8, 8)
	frames.add_frame("default", atlas)
	if monsters_script.call("_atlas_from_sprite_frames", frames) != null:
		push_error("[ATLAS_SCRIPT_PARSE_FAIL] returned atlas where none exists")
		quit(1)
		return
	print("[ATLAS_SCRIPT_PARSE_PASS] Godot compiled both recovered sprite loaders")
	quit(0)
