extends Node

# Godot CLI project-mode test (autoload globals work):
# godot --headless --path client --scene res://tests/render_resource_regression.tscn
# Checks that resource reuse does not change ground clicks or targeting.
const GROUND_ITEM_SCRIPT = preload("res://scripts/ground_item_entity.gd")
const DAY_NIGHT_SCRIPT = preload("res://scripts/day_night_overlay.gd")
const MONSTER_SCENE = preload("res://monster_entity.tscn")
var _failures: int = 0


func _check(ok: bool, description: String) -> void:
	if ok:
		print("[RENDER_RESOURCE_PASS] " + description)
	else:
		_failures += 1
		push_error("[RENDER_RESOURCE_FAIL] " + description)


func _ready() -> void:
	call_deferred("_run")


func _run() -> void:
	var first: RectangleShape2D = GROUND_ITEM_SCRIPT.get_shared_click_shape(Vector2(32, 48))
	var same: RectangleShape2D = GROUND_ITEM_SCRIPT.get_shared_click_shape(Vector2(32, 48))
	var different: RectangleShape2D = GROUND_ITEM_SCRIPT.get_shared_click_shape(Vector2(64, 48))
	_check(first == same, "identical ground item texture sizes share one click shape")
	_check(first != different, "different ground item texture sizes use different shapes")
	_check(first.size == Vector2(62, 78),
		"the original 30px click padding is unchanged")

	var mat1: ShaderMaterial = GROUND_ITEM_SCRIPT.get_shared_palette_material(1)
	var mat1_again: ShaderMaterial = GROUND_ITEM_SCRIPT.get_shared_palette_material(1)
	var mat2: ShaderMaterial = GROUND_ITEM_SCRIPT.get_shared_palette_material(2)
	_check(mat1 == mat1_again, "identical ground item palettes reuse material")
	_check(mat1 != mat2, "different ground item palettes remain independent")
	_check(is_equal_approx(float(mat1.get_shader_parameter("hue_shift")), 0.0),
		"first original palette color stays the same")
	_check(is_equal_approx(float(mat2.get_shader_parameter("hue_shift")), 90.0),
		"second original palette color stays the same")

	_check(not DAY_NIGHT_SCRIPT.should_render_night_overlay(0.0),
		"fully transparent daytime shader does not draw fullscreen pass")
	_check(DAY_NIGHT_SCRIPT.should_render_night_overlay(0.001),
		"first nonzero dusk light restores fullscreen overlay")
	_check(DAY_NIGHT_SCRIPT.should_render_night_overlay(1.0),
		"nighttime lighting remains enabled")

	var monster: MonsterEntity = MONSTER_SCENE.instantiate() as MonsterEntity
	get_tree().root.add_child(monster)
	_check(not is_instance_valid(monster.target_indicator),
		"idle monster allocates no hidden target AnimatedSprite2D")
	monster.set_target_indicator(false)
	_check(not is_instance_valid(monster.target_indicator),
		"no target marker is created when clearing target state")
	monster.set_target_indicator(true)
	_check(is_instance_valid(monster.target_indicator) and monster.target_indicator.visible,
		"first actual target event creates and displays marker")
	monster.set_target_indicator(false)
	_check(is_instance_valid(monster.target_indicator) and not monster.target_indicator.visible,
		"clearing target hides but does not recreate marker")
	monster.free()

	print("[RENDER_RESOURCE_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	get_tree().quit(0 if _failures == 0 else 1)
