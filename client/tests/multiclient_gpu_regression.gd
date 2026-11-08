extends SceneTree

# godot --headless --path client --script res://tests/multiclient_gpu_regression.gd
# Server-free checks: duplicate GPU resource elimination, unchanged palettes,
# and PID-based diagnostic tagging for multi-client measurements.
const LEGACY_RESOLVER = preload("res://scripts/legacy_media_resolver.gd")
var _failures: int = 0


func _check(ok: bool, label: String) -> void:
	if ok:
		print("[MULTICLIENT_GPU_PASS] " + label)
	else:
		_failures += 1
		push_error("[MULTICLIENT_GPU_FAIL] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var first: ShaderMaterial = PaperDollEngine.get_shared_palette_material(1, 0)
	var same: ShaderMaterial = PaperDollEngine.get_shared_palette_material(1, 0)
	var different_hue: ShaderMaterial = PaperDollEngine.get_shared_palette_material(2, 0)
	var different_effect: ShaderMaterial = PaperDollEngine.get_shared_palette_material(1, 2)
	_check(first == same, "identical paper-doll hue and effect share one material")
	_check(first != different_hue, "different hue preserves its own material")
	_check(first != different_effect, "special equipment effect remains independent")
	_check(first.shader == different_effect.shader,
		"all paper-doll variants continue using the original palette shader")
	_check(is_equal_approx(float(first.get_shader_parameter("hue_shift")), 90.0),
		"the first paper-doll palette keeps its original hue")
	_check(is_equal_approx(float(different_hue.get_shader_parameter("hue_shift")), 180.0),
		"the second paper-doll palette keeps its original hue")
	_check(int(different_effect.get_shader_parameter("effect_type")) == 2,
		"dynamic glow effect parameter is preserved")
	_check(PaperDollEngine.get_shared_palette_material_count() >= 3,
		"paper-doll material cache contains the distinct combinations")

	var monster_first: ShaderMaterial = MonsterEntity.get_shared_monster_palette_material(1)
	var monster_same: ShaderMaterial = MonsterEntity.get_shared_monster_palette_material(1)
	var monster_other: ShaderMaterial = MonsterEntity.get_shared_monster_palette_material(3)
	_check(monster_first == monster_same,
		"same-color monsters use one palette material across instances")
	_check(monster_first != monster_other,
		"different-color monsters keep independent palettes")
	_check(is_equal_approx(float(monster_other.get_shader_parameter("hue_shift")), 270.0),
		"monster palette hue is unchanged")
	_check(int(monster_first.get_shader_parameter("effect_type")) == 0,
		"monster palette remains the original effect-free color shift")
	_check(MonsterEntity.get_shared_monster_palette_material_count() >= 2,
		"monster material cache reports its current resource count")

	# Importing LegacyResolver here also compiles the previously broken
	# candidate variable declarations under Godot 4.7.2.
	_check(LEGACY_RESOLVER.normalized_stem("SE0136.mp3") == "SE0136",
		"legacy cinematic media path script compiles and keeps file stem logic")

	var context: String = GameManager._multiclient_gpu_context()
	_check(context.contains("pid=") and context.contains("window="),
		"the GPU log can be correlated across 1, 5 and 10 clients")
	_check(context.contains("gpu_memory_metric=godot_resources"),
		"resource allocations are not mislabeled as Windows GPU utilization")
	_check(context.contains("paper_palette_materials=")
		and context.contains("monster_palette_materials="),
		"GPU diagnostics include both material resource pools")

	print("[MULTICLIENT_GPU_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	quit(0 if _failures == 0 else 1)
