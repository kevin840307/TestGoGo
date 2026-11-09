extends Node
# Run as the main_scene of a minimal Godot project, so normal autoload
# registration finishes before resolving AudioManager/AnimationTimingManager.

func _ready() -> void:
	call_deferred("_run")

func _run() -> void:
	var monsters_script: GDScript = load("res://scripts/monster_loader.gd") as GDScript
	var dolls_script: GDScript = load("res://scripts/paper_doll_loader.gd") as GDScript
	if monsters_script == null or dolls_script == null:
		push_error("[ATLAS_SCRIPT_PARSE_FAIL] loader failed to parse")
		get_tree().quit(1)
		return
	if not monsters_script.can_instantiate() or not dolls_script.can_instantiate():
		push_error("[ATLAS_SCRIPT_PARSE_FAIL] loader failed compilation")
		get_tree().quit(1)
		return
	var monster: Node = monsters_script.new() as Node
	var doll: Node = dolls_script.new() as Node
	if not is_instance_valid(monster) or not is_instance_valid(doll):
		push_error("[ATLAS_SCRIPT_PARSE_FAIL] failed to instantiate")
		get_tree().quit(1)
		return
	if not monster.has_method("get_monster_frames") or not doll.has_method("get_sprite_frames"):
		push_error("[ATLAS_SCRIPT_PARSE_FAIL] missing cache API")
		get_tree().quit(1)
		return
	print("[ATLAS_SCRIPT_PARSE_PASS] both loaders compile under normal autoload registration")
	get_tree().quit(0)
