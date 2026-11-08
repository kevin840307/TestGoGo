extends Node

# Godot CLI project-mode test (autoload globals work):
# godot --headless --path client --scene res://tests/render_resource_regression.tscn
# Checks that resource reuse does not change ground clicks or targeting.
const GROUND_ITEM_SCRIPT = preload("res://scripts/ground_item_entity.gd")
const DAY_NIGHT_SCRIPT = preload("res://scripts/day_night_overlay.gd")
const MONSTER_SCENE = preload("res://monster_entity.tscn")
const PET_SCENE = preload("res://pet_entity.tscn")
const GROUND_ITEM_SCENE = preload("res://ground_item_entity.tscn")
const NPC_SCENE = preload("res://npc_entity.tscn")
const AFK_MEMORY_AUDIT = preload("res://scripts/afk_memory_audit.gd")
# Compile the real SkillWindow and SkillCard dependencies during the
# client regression suite, not only when a player opens the skill book.
const SKILL_WINDOW_SCRIPT = preload("res://scripts/skill_window.gd")
const SKILL_CARD_SCENE = preload("res://skill_card.tscn")
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
	# Production COLOR_INDEX_TO_DEGREES defines 1=90 and 2=180,
	# while index 0 is an unmodified texture. The earlier test expected
	# 1=0/2=90 and incorrectly rejected the correct palette mapping.
	_check(is_equal_approx(float(mat1.get_shader_parameter("hue_shift")), 90.0),
		"palette index 1 preserves the original 90-degree hue shift")
	_check(is_equal_approx(float(mat2.get_shader_parameter("hue_shift")), 180.0),
		"palette index 2 preserves the original 180-degree hue shift")
	# Check every original palette color and cache identity so that a later
	# optimization cannot silently swap the colors of dropped items.
	var expected_hue_by_index: Dictionary = {
		0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0,
		4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0
	}
	for raw_color_index in expected_hue_by_index.keys():
		var color_index: int = int(raw_color_index)
		var palette: ShaderMaterial = GROUND_ITEM_SCRIPT.get_shared_palette_material(color_index)
		var palette_again: ShaderMaterial = GROUND_ITEM_SCRIPT.get_shared_palette_material(color_index)
		_check(palette == palette_again and
			is_equal_approx(float(palette.get_shader_parameter("hue_shift")),
				float(expected_hue_by_index[color_index])),
			"palette index %d reuses the correct original hue-shift material" % color_index)
	_check(is_equal_approx(float(mat1.get_shader_parameter("hue_shift")), 90.0) and
		is_equal_approx(float(mat2.get_shader_parameter("hue_shift")), 180.0),
		"looking up other palette colors does not change already-shared materials")

	_check(not DAY_NIGHT_SCRIPT.should_render_night_overlay(0.0),
		"fully transparent daytime shader does not draw fullscreen pass")
	_check(DAY_NIGHT_SCRIPT.should_render_night_overlay(0.001),
		"first nonzero dusk light restores fullscreen overlay")
	_check(DAY_NIGHT_SCRIPT.should_render_night_overlay(1.0),
		"nighttime lighting remains enabled")

	var skill_window_node: Control = SKILL_WINDOW_SCRIPT.new() as Control
	_check(is_instance_valid(skill_window_node) and skill_window_node.has_method("update_ui"),
		"skill window GDScript parses and exposes update_ui")
	if is_instance_valid(skill_window_node):
		skill_window_node.free()
	var skill_card_node: SkillCard = SKILL_CARD_SCENE.instantiate() as SkillCard
	_check(is_instance_valid(skill_card_node) and skill_card_node.has_method("setup"),
		"skill card scene parses and exposes setup")
	if is_instance_valid(skill_card_node):
		skill_card_node.free()

	# Synthetic adjacent atlas cells: transparent HIT corners must not
	# sample the opaque neighboring frame, even on the very first hit.
	var atlas_image := Image.create(4, 2, false, Image.FORMAT_RGBA8)
	atlas_image.fill(Color(0, 0, 0, 0))
	atlas_image.set_pixel(1, 1, Color(1, 0, 0, 1))
	for y in range(2):
		for x in range(2, 4):
			atlas_image.set_pixel(x, y, Color(0, 1, 0, 1))
	var source_atlas: ImageTexture = ImageTexture.create_from_image(atlas_image)
	var clipped_hit: AtlasTexture = MonsterLoader.make_clipped_frame_texture(
		source_atlas, Rect2(0, 0, 2, 2), Vector2(3, 7)
	)
	_check(clipped_hit.filter_clip and clipped_hit.region == Rect2(0, 0, 2, 2),
		"monster HIT atlas region clips neighboring frame colors")
	_check(clipped_hit.get_meta("old_offset", Vector2.ZERO) == Vector2(3, 7),
		"clipped HIT frame retains original recovered SPR origin")
	var hit_image: Image = clipped_hit.get_image()
	_check(hit_image != null and hit_image.get_width() == 2 and
		hit_image.get_height() == 2 and
		hit_image.get_pixel(0, 0).a < 0.01 and
		hit_image.get_pixel(1, 1).r > 0.9,
		"monster HIT atlas crop retains transparency and foreground color")

	# AFK must keep logical entities alive while severing their large visual
	# references. These synthetic resources make the contract testable without
	# requiring the ignored local recovered-media bundle.
	var audit_root := Node2D.new()
	audit_root.name = "AfkMemoryAuditRoot"
	get_tree().root.add_child(audit_root)
	var audit_image := Image.create(8, 8, false, Image.FORMAT_RGBA8)
	audit_image.fill(Color.WHITE)
	var audit_texture := ImageTexture.create_from_image(audit_image)
	var audit_sprite := Sprite2D.new()
	audit_sprite.texture = audit_texture
	audit_root.add_child(audit_sprite)
	var audit_frames := SpriteFrames.new()
	audit_frames.add_frame("default", audit_texture)
	var audit_animated := AnimatedSprite2D.new()
	audit_animated.sprite_frames = audit_frames
	audit_root.add_child(audit_animated)
	var memory_report: Dictionary = AFK_MEMORY_AUDIT.capture(
		{"test": audit_root},
		[{"name": "synthetic_cache", "value": {
			"texture": audit_texture, "frames": audit_frames
		}}],
		12
	)
	_check(int(memory_report.get("texture_resources", 0)) >= 1 and
		int(memory_report.get("sprite_frame_resources", 0)) >= 1,
		"AFK memory audit finds live Texture2D and SpriteFrames references")
	_check(int(memory_report.get("estimated_texture_bytes", 0)) >= 8 * 8 * 4,
		"AFK memory audit ranks texture ownership with a byte estimate")
	var found_multi_owner_texture: bool = false
	for raw_entry in memory_report.get("top", []):
		var entry: Dictionary = raw_entry
		if int(entry.get("instance_id", 0)) == audit_texture.get_instance_id():
			found_multi_owner_texture = int(entry.get("reference_sources", 0)) >= 2
			break
	_check(found_multi_owner_texture,
		"AFK memory audit reports both scene and cache ownership sources")

	var pet = PET_SCENE.instantiate()
	get_tree().root.add_child(pet)
	pet.sprite.sprite_frames = audit_frames
	var pet_collision := pet.get_node_or_null("CollisionShape2D")
	pet.set_afk_visual_suspended(true)
	_check(pet.sprite.sprite_frames == null and not pet.ui_container.visible,
		"AFK strips pet SpriteFrames and UI")
	_check(is_instance_valid(pet_collision) and pet_collision.get_parent() == pet,
		"AFK preserves pet collision/gameplay node identity")
	pet.set_afk_visual_suspended(false)
	pet.free()

	var ground = GROUND_ITEM_SCENE.instantiate()
	get_tree().root.add_child(ground)
	ground.sprite.texture = audit_texture
	var ground_collision := ground.get_node_or_null("CollisionShape2D")
	ground.set_afk_visual_suspended(true)
	_check(ground.sprite.texture == null and not ground.label_container.visible,
		"AFK strips ground-item texture and labels")
	_check(is_instance_valid(ground_collision) and ground_collision.get_parent() == ground,
		"AFK preserves ground-item pickup/collision node")
	ground.set_afk_visual_suspended(false)
	ground.free()

	var npc = NPC_SCENE.instantiate()
	get_tree().root.add_child(npc)
	npc.sprite.texture = audit_texture
	var npc_collision := npc.get_node_or_null("CollisionShape2D")
	npc.set_afk_visual_suspended(true)
	_check(npc.sprite.texture == null and not npc.visual_center.visible,
		"AFK strips NPC texture and visual UI")
	_check(is_instance_valid(npc_collision) and npc_collision.get_parent() == npc,
		"AFK preserves NPC interaction/collision node")
	npc.set_afk_visual_suspended(false)
	npc.free()
	audit_root.free()
	# Local client PNGs are intentionally excluded by Git. Exercise
	# production HIT frames as well when the full media set is installed.
	if ResourceLoader.exists("res://Assets/Monsters/EN004A.png"):
		var recovered: SpriteFrames = MonsterLoader.get_monster_frames("EN004A", true)
		_check(recovered != null, "local recovered monster atlas loads")
		if recovered:
			var checked_hits: int = 0
			for anim in recovered.get_animation_names():
				if not str(anim).begins_with("HIT_"):
					continue
				for frame_id in range(recovered.get_frame_count(anim)):
					var hit_frame := recovered.get_frame_texture(anim, frame_id) as AtlasTexture
					_check(hit_frame != null and hit_frame.filter_clip,
						"recovered %s HIT frame %d clips adjacent atlas pixels" % [anim, frame_id])
					checked_hits += 1
			_check(checked_hits > 0, "local monster atlas contains HIT frames")
	else:
		print("[RENDER_RESOURCE_NOTE] Monster PNG not in Git; local HIT alpha pixels require asset-enabled test")

	var monster: MonsterEntity = MONSTER_SCENE.instantiate() as MonsterEntity
	get_tree().root.add_child(monster)
	_check(monster.sprite.texture_filter == CanvasItem.TEXTURE_FILTER_NEAREST,
		"monster pixel sprite uses nearest-neighbor filtering to avoid atlas edge bleed")
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
	# Target markers may also have no frames when an effect resource is
	# missing. They must not call AnimatedSprite2D.play("default") either.
	if is_instance_valid(monster.target_indicator):
		monster.target_indicator.stop()
		monster.target_indicator.sprite_frames = null
		monster.target_indicator.visible = true
		monster.call("_on_screen_entered")
		_check(not monster.target_indicator.is_playing(),
			"missing target icon frames do not trigger default animation error")
		monster.target_indicator.visible = false

	# Simplification releases SpriteFrames entirely. A screen-entered event
	# must never try to play an absent "default" animation.
	monster.set_performance_simplified(true)
	monster.call("_on_screen_entered")
	_check(monster.sprite.sprite_frames == null and not monster.sprite.is_playing(),
		"simplified monster re-enters screen without playing nonexistent default frames")
	monster.set_performance_simplified(false)
	monster.sprite.stop()
	monster.call("_on_screen_entered")
	_check(not monster.sprite.is_playing(),
		"full monster without loaded SPR frames does not play missing default")
	var idle_frames := SpriteFrames.new()
	if idle_frames.has_animation("default"):
		idle_frames.remove_animation("default")
	idle_frames.add_animation("IDLE_1")
	var pixel_image := Image.create(2, 2, false, Image.FORMAT_RGBA8)
	pixel_image.fill(Color.WHITE)
	idle_frames.add_frame("IDLE_1", ImageTexture.create_from_image(pixel_image))
	monster.sprite.sprite_frames = idle_frames
	monster.sprite.animation = "IDLE_1"
	monster.sprite.stop()
	monster.call("_on_screen_entered")
	_check(monster.sprite.is_playing() and monster.sprite.animation == "IDLE_1",
		"real IDLE_1 animation resumes instead of missing default")
	monster.call("_on_screen_exited")
	_check(not monster.sprite.is_playing(),
		"offscreen full monster pauses animation to save resources")
	monster.call("_on_screen_entered")
	_check(monster.sprite.is_playing() and monster.sprite.animation == "IDLE_1",
		"offscreen paused animation resumes when the monster reappears")
	monster.free()

	print("[RENDER_RESOURCE_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	get_tree().quit(0 if _failures == 0 else 1)
