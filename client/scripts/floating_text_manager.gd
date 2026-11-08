
extends Node

const POOL_SIZE = 100
const INITIAL_POOL_SIZE = 16
const DYNAMIC_ENTITY_VISIBILITY_LAYER: = 1 << 1




var _text_pool: Array[Label] = []
var _effect_pool: Array[AnimatedSprite2D] = []

var _damage_queue: Array = []
var _damage_queue_head: = 0
const MAX_NUMBERS_PER_FRAME = 5
const DAMAGE_QUEUE_COMPACT_THRESHOLD: = 64

func _ready() -> void :
	for _i in range(INITIAL_POOL_SIZE):
		_create_pooled_label()
		_create_pooled_effect()
	set_process(false)

func _create_pooled_label() -> Label:
	var lbl: = Label.new()
	lbl.visibility_layer = DYNAMIC_ENTITY_VISIBILITY_LAYER
	lbl.visible = false
	lbl.z_index = 100
	add_child(lbl)
	_text_pool.append(lbl)
	return lbl

func _create_pooled_effect() -> AnimatedSprite2D:
	var eff: = AnimatedSprite2D.new()
	eff.visibility_layer = DYNAMIC_ENTITY_VISIBILITY_LAYER
	eff.visible = false
	add_child(eff)
	eff.frame_changed.connect(_on_pooled_effect_frame_changed.bind(eff))
	eff.animation_finished.connect(_on_pooled_effect_finished.bind(eff))
	_effect_pool.append(eff)
	return eff


func _get_free_label() -> Label:
	for lbl in _text_pool:
		if not lbl.visible: return lbl
	if _text_pool.size() < POOL_SIZE:
		return _create_pooled_label()
	return null


func stop_combat_visual_effects() -> void:
	# Retain damage labels; those are essential combat feedback, not FX sprites.
	for effect in _effect_pool:
		if is_instance_valid(effect):
			effect.stop()
			effect.visible = false


func release_world_runtime_for_login() -> void:
	_damage_queue.clear()
	_damage_queue_head = 0
	set_process(false)
	for effect in _effect_pool:
		if not is_instance_valid(effect):
			continue
		effect.stop()
		effect.visible = false
		effect.sprite_frames = null
		effect.material = null
		effect.remove_meta("effect_mode")
		if effect.has_meta("effect_target"):
			effect.remove_meta("effect_target")
	for label in _text_pool:
		if is_instance_valid(label):
			_reset_label_tweens(label)
			label.visible = false


func _get_free_effect() -> AnimatedSprite2D:
	for eff in _effect_pool:
		if not eff.visible: return eff
	if _effect_pool.size() < POOL_SIZE:
		return _create_pooled_effect()
	return null




func show_damage(target: Node2D, damage: int, is_crit: bool = false, hit_delay: float = 0.3, split_count: int = 1, skill_id: int = 0, is_primary: bool = true) -> void :
	if hit_delay > 0:
		var tree := get_tree()
		if tree == null:
			return
		var timer := tree.create_timer(hit_delay)
		await timer.timeout

	if not is_instance_valid(target) or not target.is_inside_tree():
		return


	var is_player = target is BaseEntity
	var is_pet = target is PetEntity
	var fx_duration = 0.0

	if damage > 0 or skill_id > 0:
		fx_duration = _play_hit_effect(target, skill_id, is_primary)

	if damage <= 0:

		var effect_name = "Normal_Effect/New_Miss_Blue"
		if is_player:
			effect_name = "Normal_Effect/New_Miss_Orange"
		elif is_pet:
			effect_name = "Normal_Effect/New_Miss_Purple"

		var frames: SpriteFrames = EffectLoader.get_effect(effect_name) if (not GameManager or GameManager.should_render_combat_effects()) else null
		if frames:
			var miss_effect = _get_free_effect()
			if miss_effect:
				miss_effect.set_meta("effect_mode", "miss")
				miss_effect.sprite_frames = frames
				miss_effect.z_index = 100

				var anim_name = "default" if frames.has_animation("default") else frames.get_animation_names()[0]
				var tex = frames.get_frame_texture(anim_name, 0)

				if tex:
					var offset_val = EffectLoader.get_offset(tex)
					miss_effect.centered = false
					miss_effect.offset = Vector2( - offset_val.x, - offset_val.y)


				miss_effect.global_position = target.global_position + Vector2(0, -150)
				miss_effect.visible = true
				miss_effect.modulate.a = 1.0

				miss_effect.play(anim_name)
		else:

			var dmg_lbl = _get_free_label()
			if dmg_lbl:
				_reset_label_tweens(dmg_lbl)
				dmg_lbl.text = "MISS"
				dmg_lbl.add_theme_constant_override("outline_size", 6)


				if is_player:
					dmg_lbl.add_theme_color_override("font_color", GameConstants.COLOR_DMG_MISS)
					dmg_lbl.add_theme_color_override("font_outline_color", GameConstants.COLOR_DMG_MISS_OUTLINE)
				elif is_pet:
					dmg_lbl.add_theme_color_override("font_color", GameConstants.COLOR_DMG_PET_MISS)
					dmg_lbl.add_theme_color_override("font_outline_color", GameConstants.COLOR_DMG_PET_MISS_OUTLINE)
				else:
					dmg_lbl.add_theme_color_override("font_color", GameConstants.COLOR_DMG_MONSTER_MISS)
					dmg_lbl.add_theme_color_override("font_outline_color", GameConstants.COLOR_DMG_MONSTER_MISS_OUTLINE)

				dmg_lbl.add_theme_font_size_override("font_size", 22)
				dmg_lbl.global_position = target.global_position + Vector2(-25, -40)
				dmg_lbl.visible = true
				dmg_lbl.modulate.a = 1.0

				var floor_y = dmg_lbl.global_position.y
				var tween = create_tween().set_parallel(true)
				dmg_lbl.set_meta("tween", tween)

				tween.tween_property(dmg_lbl, "global_position:y", floor_y - 50.0, 0.8).set_trans(Tween.TRANS_LINEAR)
				tween.tween_property(dmg_lbl, "modulate:a", 0.0, 0.6).set_delay(0.2)
				tween.chain().tween_callback( func(): dmg_lbl.visible = false)
		return


	if fx_duration > 0.0:
		await get_tree().create_timer(fx_duration * 0.3).timeout

	if not is_instance_valid(target) or not target.is_inside_tree(): return

	# Skill 99001 can kill dozens of monsters in the same packet batch.
	# Spawn its number immediately while the target node still exists; queued
	# numbers would otherwise be discarded after EntityDeath removes the node.
	if skill_id == 99001:
		_spawn_bouncing_number(target, damage, is_crit, 0.0, false)
		return

	if split_count > 1 and damage > 1:
		var remain_dmg = damage
		var parts = []
		for i in range(split_count):
			if i == split_count - 1:
				parts.append(remain_dmg)
			else:
				var avg = float(remain_dmg) / (split_count - i)
				var variance = avg * 0.15
				var p_dmg = int(round(randf_range(avg - variance, avg + variance)))
				if p_dmg < 1: p_dmg = 1
				if p_dmg >= remain_dmg: p_dmg = remain_dmg - 1
				parts.append(p_dmg)
				remain_dmg -= p_dmg

		for i in range(split_count):
			var dir_ratio = float(i) / float(split_count - 1) * 2.0 - 1.0
			_damage_queue.append({"target": target, "val": parts[i], "crit": is_crit, "dir": dir_ratio, "split": true})
	else:
		_damage_queue.append({"target": target, "val": damage, "crit": is_crit, "dir": 0.0, "split": false})
	set_process(true)

func _process(delta: float) -> void :
	var pending_count: = _damage_queue.size() - _damage_queue_head
	if pending_count > 0:

		var process_count = min(pending_count, MAX_NUMBERS_PER_FRAME)
		for i in range(process_count):
			var data = _damage_queue[_damage_queue_head]
			_damage_queue_head += 1
			if is_instance_valid(data.target) and data.target.is_inside_tree():
				_spawn_bouncing_number(data.target, data.val, data.crit, data.dir, data.split)

		if _damage_queue_head == _damage_queue.size():
			_damage_queue.clear()
			_damage_queue_head = 0
			set_process(false)
		elif _damage_queue_head >= DAMAGE_QUEUE_COMPACT_THRESHOLD and _damage_queue_head * 2 >= _damage_queue.size():
			_damage_queue = _damage_queue.slice(_damage_queue_head)
			_damage_queue_head = 0





func _spawn_bouncing_number(target: Node2D, val: int, is_crit: bool, dir_ratio: float, is_split: bool) -> void :
	var dmg_lbl = _get_free_label()
	if not dmg_lbl: return

	var is_player = target is BaseEntity
	var is_pet = target is PetEntity

	_reset_label_tweens(dmg_lbl)

	dmg_lbl.text = str(val)
	dmg_lbl.add_theme_constant_override("outline_size", 6)
	dmg_lbl.visible = true
	dmg_lbl.modulate.a = 1.0

	var base_pos = target.global_position


	if is_crit:
		if is_player:
			dmg_lbl.add_theme_color_override("font_color", GameConstants.COLOR_DMG_CRIT)
			dmg_lbl.add_theme_color_override("font_outline_color", GameConstants.COLOR_DMG_CRIT_OUTLINE)
		elif is_pet:
			dmg_lbl.add_theme_color_override("font_color", GameConstants.COLOR_DMG_PET_CRIT)
			dmg_lbl.add_theme_color_override("font_outline_color", GameConstants.COLOR_DMG_PET_CRIT_OUTLINE)
		else:
			dmg_lbl.add_theme_color_override("font_color", GameConstants.COLOR_DMG_MONSTER_CRIT)
			dmg_lbl.add_theme_color_override("font_outline_color", GameConstants.COLOR_DMG_MONSTER_CRIT_OUTLINE)
		dmg_lbl.add_theme_font_size_override("font_size", 26)
		dmg_lbl.global_position = base_pos + Vector2(-40, -45)
	else:
		if is_player:
			dmg_lbl.add_theme_color_override("font_color", GameConstants.COLOR_DMG_NORMAL)
			dmg_lbl.add_theme_color_override("font_outline_color", GameConstants.COLOR_DMG_NORMAL_OUTLINE)
		elif is_pet:
			dmg_lbl.add_theme_color_override("font_color", GameConstants.COLOR_DMG_PET_NORMAL)
			dmg_lbl.add_theme_color_override("font_outline_color", GameConstants.COLOR_DMG_PET_NORMAL_OUTLINE)
		else:
			dmg_lbl.add_theme_color_override("font_color", GameConstants.COLOR_DMG_MONSTER_NORMAL)
			dmg_lbl.add_theme_color_override("font_outline_color", GameConstants.COLOR_DMG_MONSTER_NORMAL_OUTLINE)
		dmg_lbl.add_theme_font_size_override("font_size", 24)
		dmg_lbl.global_position = base_pos + Vector2(-20, -40)

	var floor_y = dmg_lbl.global_position.y
	var offset_x = 0.0

	if is_split:
		offset_x = dir_ratio * randf_range(60.0, 80.0)
		dmg_lbl.global_position.x += dir_ratio * 15.0
		dmg_lbl.global_position.y += randf_range(-10.0, 10.0)
	else:
		var rand_sign = 1.0 if randf() > 0.5 else -1.0
		offset_x = randf_range(80.0, 100.0) * rand_sign

	var target_x = dmg_lbl.global_position.x + offset_x
	var jump_height = 130.0 if is_crit else 150.0
	var jump_duration = 0.22
	var bounce_count = 3
	var height_decay = 0.4
	var time_decay = 0.8
	var stay_time = 0.2
	var fade_time = 0.3

	var total_time = (jump_duration * 2)
	var temp_duration = jump_duration
	for i in range(bounce_count):
		temp_duration *= time_decay
		total_time += (temp_duration * 2)

	var tween_x = create_tween()
	var tween_y = create_tween()

	dmg_lbl.set_meta("tween_x", tween_x)
	dmg_lbl.set_meta("tween_y", tween_y)

	tween_x.tween_property(dmg_lbl, "global_position:x", target_x, total_time).set_trans(Tween.TRANS_LINEAR)

	tween_y.tween_property(dmg_lbl, "global_position:y", floor_y - jump_height, jump_duration).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	tween_y.chain().tween_property(dmg_lbl, "global_position:y", floor_y, jump_duration).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)

	for i in range(bounce_count):
		jump_height *= height_decay
		jump_duration *= time_decay
		tween_y.chain().tween_property(dmg_lbl, "global_position:y", floor_y - jump_height, jump_duration).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
		tween_y.chain().tween_property(dmg_lbl, "global_position:y", floor_y, jump_duration).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)

	tween_y.chain().tween_interval(stay_time)
	tween_y.chain().tween_property(dmg_lbl, "modulate:a", 0.0, fade_time).set_trans(Tween.TRANS_LINEAR)
	tween_y.chain().tween_callback( func(): dmg_lbl.visible = false)




func _spawn_effect_on_target(target: Node2D, full_effect_name: String, z_idx: int) -> float:
	if GameManager and not GameManager.should_render_combat_effects():
		return 0.0
	var duration = 0.0
	var frames = EffectLoader.get_effect(full_effect_name)

	if frames:
		var hit_effect = _get_free_effect()
		if not hit_effect: return 0.0

		hit_effect.sprite_frames = frames
		hit_effect.z_index = z_idx
		hit_effect.centered = false
		hit_effect.visible = true
		hit_effect.modulate.a = 1.0

		var anim_name = "default"
		if frames.has_animation("default"):
			anim_name = "default"
		elif "current_dir" in target and frames.has_animation(target.current_dir):
			anim_name = target.current_dir
		else:
			var anims = frames.get_animation_names()
			if anims.size() > 0: anim_name = anims[0]

		hit_effect.animation = anim_name
		hit_effect.frame = 0

		hit_effect.set_meta("effect_mode", "hit")
		hit_effect.set_meta("effect_target", weakref(target))
		_on_pooled_effect_frame_changed(hit_effect)
		hit_effect.play(anim_name)

		var fps = frames.get_animation_speed(anim_name)
		if fps > 0:
			duration = float(frames.get_frame_count(anim_name)) / fps

	return duration

func _play_hit_effect(target: Node2D, skill_id: int = 0, is_primary: bool = true) -> float:
	if GameManager and not GameManager.should_render_combat_effects():
		return 0.0
	var effect_name = ""
	var skill_effect_name = ""
	var fx_target = target

	if skill_id > 0 and SkillLoader:
		var s_res = SkillLoader.get_skill_data(skill_id)
		if s_res:
			if is_primary:
				if s_res.skill_type in [3, 4]:
					if GameManager and is_instance_valid(GameManager.current_player):
						fx_target = GameManager.current_player

				if s_res.skill_type >= 2 and s_res.skill_type <= 5:
					pass
				elif s_res.fx_hit != "":
					if s_res.fx_hit.begins_with("Normal"):
						skill_effect_name = "Normal_Effect/" + s_res.fx_hit
					elif s_res.fx_hit.begins_with("Skill"):
						skill_effect_name = "Skill_Effect/" + s_res.fx_hit
					else:
						skill_effect_name = "Skill_Effect/" + s_res.fx_hit
			else:
				if s_res.fx_extra != "":
					effect_name = "Hit_Effect/" + s_res.fx_extra
	else:
		effect_name = "Hit_Effect/Hit_Effect00"
		if not (target is BaseEntity):
			if GameManager and is_instance_valid(GameManager.current_player) and "data_model" in GameManager.current_player:
				var weapon_id = GameManager.current_player.data_model.weapon_id
				if weapon_id > 0 and ItemLoader:
					var note_1 = str(ItemLoader.get_item_property(weapon_id, "note_1", "")).strip_edges()
					if note_1 != "":
						effect_name = "Hit_Effect/" + note_1

	var max_duration = 0.0

	if is_instance_valid(fx_target):
		if effect_name != "":
			var d1 = _spawn_effect_on_target(fx_target, effect_name, 95)
			max_duration = max(max_duration, d1)

		if skill_effect_name != "":
			var check_res = EffectLoader.get_effect(skill_effect_name)
			if not check_res:
				var raw_name = skill_effect_name.split("/")[1]
				skill_effect_name = "Hit_Effect/" + raw_name

			var d2 = _spawn_effect_on_target(fx_target, skill_effect_name, 96)
			max_duration = max(max_duration, d2)

	return max_duration




func _reset_label_tweens(lbl: Label) -> void :

	for key in ["tween", "tween_x", "tween_y"]:
		if lbl.has_meta(key):
			var t = lbl.get_meta(key)
			if is_instance_valid(t) and t.is_running():
				t.kill()

func _on_pooled_effect_frame_changed(effect: AnimatedSprite2D) -> void :
	if effect.get_meta("effect_mode", "") != "hit" or not effect.sprite_frames:
		return
	if not effect.sprite_frames.has_animation(effect.animation):
		return
	EffectLoader.play_confirmed_frame_audio(effect)
	var cur_tex = effect.sprite_frames.get_frame_texture(effect.animation, effect.frame)
	if not cur_tex:
		return
	var offset = EffectLoader.get_offset(cur_tex)
	effect.offset = Vector2( - offset.x, - offset.y)

	var target: Node2D = null
	var target_ref = effect.get_meta("effect_target", null) as WeakRef
	if target_ref:
		var target_object = target_ref.get_ref()
		if is_instance_valid(target_object) and target_object is Node2D:
			target = target_object as Node2D
	if target:
		if cur_tex.has_meta("old_offset"):
			effect.global_position = target.global_position
		else:
			var col_shape = target.get_node_or_null("CollisionShape2D")
			effect.global_position = target.global_position + (col_shape.position if is_instance_valid(col_shape) else Vector2(0, -40))

func _on_pooled_effect_finished(effect: AnimatedSprite2D) -> void :
	effect.visible = false
	effect.stop()
	effect.remove_meta("effect_mode")
	if effect.has_meta("effect_target"):
		effect.remove_meta("effect_target")
