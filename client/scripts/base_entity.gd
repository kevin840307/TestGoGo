
extends CharacterBody2D
class_name BaseEntity


var data_model: PlayerData = PlayerData.new()

var current_race: String = "Boy"
var current_hair: String = "Hair000"
var current_hair_color_index: int = 0
var current_body_color_index: int = 0


@onready var doll_engine = $DollContainer
var transform_sprite: AnimatedSprite2D = null

var ui_container: Node2D = null
var name_label: Label = null
var title_label: Label = null
var club_label: Label = null
var _label_display_cache_initialized: bool = false
var _cached_title_id: int = -1
var _cached_title_text: String = ""
var _cached_club_id: int = -1
var _cached_club_position: int = -1
var _cached_club_name: String = ""
var _cached_club_text: String = ""
var chat_bubble_bg: NinePatchRect = null
var chat_bubble_label: Label = null
var chat_bubble_timer: Timer = null
var bubble_fade_tween: Tween = null
var death_label: Label = null
const SHOP_BOARD_SCENE = preload("res://shop_board.tscn")
var shop_board_node: Control = null
var shop_board_label: Label = null
var shop_owner_label: Label = null


var current_state: String = "idle"
var _render_visible: bool = true
var _render_notifier: VisibleOnScreenNotifier2D = null
const RENDER_VISIBILITY_RECT: Rect2 = Rect2(-220.0, -280.0, 440.0, 500.0)
var current_dir: String = "1"
var hp_update_seq: int = 0
var normal_attack_visual_started_msec: int = -1
var _name_color_state: int = 0
var _name_color_flash_generation: int = 0

var is_action_locked: bool = false
var is_emoting: bool = false
var is_dead: bool = false
var revive_fade_tween: Tween = null
var transition_tween: Tween = null

var mecha_recover_effect: AnimatedSprite2D = null
var level_up_effect: AnimatedSprite2D = null
var mecha_transform_effect: AnimatedSprite2D = null
var death_effect: AnimatedSprite2D = null
var _active_transient_skill_effects: Array[AnimatedSprite2D] = []
var _buff_animation_reached_last_frame: bool = false
var _buff_animation_generation: int = 0
var item_effect: AnimatedSprite2D = null

const BUFF_ANIMATION_SAFETY_TIMEOUT_SECONDS: float = 5.0
const PURPLE_NAME_FLASH_DURATION_SECONDS: float = 3.0
const PURPLE_NAME_FLASH_START_INTERVAL_SECONDS: float = 0.2
const PURPLE_NAME_FLASH_END_INTERVAL_SECONDS: float = 0.05


var mecha_bullet_effect_l: AnimatedSprite2D = null
var mecha_bullet_effect_r: AnimatedSprite2D = null
var bullet_tween: Tween = null


var weapon_attack_effect: AnimatedSprite2D = null


var mecha_bullet_offsets: Dictionary = {
	"1": {"l_start": Vector2(-70, -30), "l_end": Vector2(-5, -15), "r_start": Vector2(-10, 5), "r_end": Vector2(15, -15)}, 
	"2": {"l_start": Vector2(-45, -8), "l_end": Vector2(30, -20), "r_start": Vector2(-45, -53), "r_end": Vector2(30, 0)}, 
	"3": {"l_start": Vector2(-70, -30), "l_end": Vector2(-10, 0), "r_start": Vector2(-10, -60), "r_end": Vector2(10, 0)}, 
	"4": {"l_start": Vector2(-70, -30), "l_end": Vector2(-10, 0), "r_start": Vector2(-10, -60), "r_end": Vector2(10, 0)}, 
	"5": {"l_start": Vector2(10, -65), "l_end": Vector2(-10, 0), "r_start": Vector2(65, -32), "r_end": Vector2(10, 0)}, 
	"6": {"l_start": Vector2(40, -7), "l_end": Vector2(0, -10), "r_start": Vector2(40, -55), "r_end": Vector2(0, 10)}, 
	"7": {"l_start": Vector2(10, 15), "l_end": Vector2(-10, 0), "r_start": Vector2(20, 5), "r_end": Vector2(10, 0)}, 
	"8": {"l_start": Vector2(-70, -30), "l_end": Vector2(10, 0), "r_start": Vector2(-10, 5), "r_end": Vector2(-10, 0)}
}

const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}


func _ready() -> void :
	_ensure_name_tag()
	_ensure_effects()
	_ensure_transform_sprite()
	if data_model and not data_model.identity_labels_changed.is_connected(_refresh_identity_labels):
		data_model.identity_labels_changed.connect(_refresh_identity_labels)
	if CursorManager and not CursorManager.ctrl_state_changed.is_connected(_on_ctrl_state_changed):
		CursorManager.ctrl_state_changed.connect(_on_ctrl_state_changed)
	_refresh_identity_labels()

	if is_instance_valid(doll_engine) and doll_engine.has_signal("body_animation_finished"):
		if not doll_engine.body_animation_finished.is_connected(_on_body_animation_finished):
			doll_engine.body_animation_finished.connect(_on_body_animation_finished)

	if NetworkManager:
		NetworkManager.sync_shop_board_received.connect(_on_sync_shop_board)
		NetworkManager.entity_name_color_updated.connect(_on_entity_name_color_updated)
		NetworkManager.party_sync_received.connect(_on_party_sync_received)

	_render_notifier = VisibleOnScreenNotifier2D.new()
	_render_notifier.rect = RENDER_VISIBILITY_RECT
	add_child(_render_notifier)
	_render_notifier.screen_entered.connect(_on_render_screen_entered)
	_render_notifier.screen_exited.connect(_on_render_screen_exited)

func _set_render_animation_paused(paused: bool) -> void :
	if is_instance_valid(doll_engine):
		for sprite_node in doll_engine.doll_sprites.values():
			if not is_instance_valid(sprite_node):
				continue
			if paused:
				sprite_node.pause()
			elif sprite_node.visible and not sprite_node.is_playing():
				sprite_node.play()
	if is_instance_valid(transform_sprite):
		if paused:
			transform_sprite.pause()
		elif transform_sprite.visible and not transform_sprite.is_playing():
			transform_sprite.play()
	if is_instance_valid(ui_container):
		ui_container.visible = not paused

func _on_render_screen_entered() -> void :
	_render_visible = true
	_set_render_animation_paused(false)

func _on_render_screen_exited() -> void :
	_render_visible = false
	_set_render_animation_paused(true)

func is_render_visible() -> bool:
	return _render_visible

func is_entity_moving() -> bool:
	return false


func update_hp(new_hp: int, hit_delay: float = 0.3, is_miss: bool = false) -> void :
	hp_update_seq += 1
	var current_seq = hp_update_seq

	if hit_delay > 0:
		if not is_instance_valid(self) or not is_inside_tree():
			return
		var tree := get_tree()
		if tree == null:
			return
		var timer := tree.create_timer(hit_delay)
		await timer.timeout
	if not is_instance_valid(self) or not is_inside_tree(): return
	if current_seq != hp_update_seq: return

	data_model.hp = new_hp

	if new_hp <= 0:
		_die()
		return

	if is_dead: revive()

	if current_state != "death" and not is_action_locked and not is_miss:
		current_state = get_combat_hit_state()
		is_action_locked = true
		if is_instance_valid(doll_engine):
			for sprite_node in doll_engine.doll_sprites.values(): sprite_node.frame = 0
		sync_animation()

func _die() -> void :
	if is_dead: return

	is_dead = true
	current_state = "death"
	is_action_locked = true
	velocity = Vector2.ZERO

	_clear_transient_skill_effects()
	if is_instance_valid(item_effect):
		item_effect.visible = false
		item_effect.stop()

	if is_riding_mecha():
		if GameManager and GameManager.current_player == self:
			if NetworkManager and NetworkManager.is_connected_to_server:
				NetworkManager.send_drive_mecha_request(-1)

		execute_mecha_transition( func():
			data_model.summoned_horse_uid = -1
			setup_layered_paper_dolls()
			_play_death_effect()
		)
	else:
		_play_death_effect()

func _play_death_effect() -> void :

	if is_riding_mecha():
		if name_label: name_label.position.y = -160.0
		if title_label: title_label.position.y = -180.0
		if club_label: club_label.position.y = -200.0
	else:
		if name_label: name_label.position.y = -130.0
		if title_label: title_label.position.y = -150.0
		if club_label: club_label.position.y = -170.0

	var effect_name = "Normal_Effect/R-Die_Effect" if current_race == "Robot" else "Normal_Effect/B-Die_Effect"
	if is_instance_valid(death_effect):
		var frames = EffectLoader.get_effect(effect_name)
		if frames:
			death_effect.sprite_frames = frames

			if is_instance_valid(doll_engine):
				doll_engine.visible = false


			if is_instance_valid(transform_sprite):
				transform_sprite.visible = false

			death_effect.visible = true
			var anim_name = "default" if frames.has_animation("default") else frames.get_animation_names()[0]
			death_effect.animation = anim_name
			death_effect.frame = 0
			death_effect.play(anim_name)
			_on_effect_frame_changed(death_effect)
		else:
			if is_instance_valid(doll_engine):
				doll_engine.visible = true
			if is_instance_valid(transform_sprite):
				transform_sprite.visible = false
			sync_animation()
	else:
		sync_animation()

func revive() -> void :
	if not is_dead: return
	is_dead = false
	if revive_fade_tween and revive_fade_tween.is_running(): revive_fade_tween.kill()

	modulate.a = 1.0
	current_state = "idle"
	current_dir = "1"
	is_action_locked = false
	if is_instance_valid(death_label): death_label.visible = false

	if is_instance_valid(doll_engine):
		doll_engine.visible = (data_model.transform_type == 0)
	if is_instance_valid(transform_sprite):
		transform_sprite.visible = (data_model.transform_type > 0)
	if is_instance_valid(death_effect):
		death_effect.visible = false
		death_effect.stop()

	if data_model.transform_type > 0:
		_lock_ui_position_for_transform()
	else:
		if is_riding_mecha():
			if name_label: name_label.position.y = -160.0
			if title_label: title_label.position.y = -180.0
			if club_label: club_label.position.y = -200.0
		else:
			if name_label: name_label.position.y = -130.0
			if title_label: title_label.position.y = -150.0
			if club_label: club_label.position.y = -170.0

	sync_animation()

func show_damage_number(damage: int, is_crit: bool = false, hit_delay: float = 0.3, split_count: int = 1, skill_id: int = 0, is_primary: bool = true) -> void :
	if FloatingTextManager:
		FloatingTextManager.show_damage(self, damage, is_crit, hit_delay, split_count, skill_id, is_primary)

func show_level_up(is_mecha: bool = false) -> void :
	if not is_instance_valid(level_up_effect): return


	var effect_path = "Horse/MS_Levelup" if is_mecha else "Normal_Effect/Normal_Effect08"
	var frames = EffectLoader.get_effect(effect_path)


	if frames and level_up_effect.sprite_frames != frames:
		level_up_effect.sprite_frames = frames

	if level_up_effect.sprite_frames:
		var target_anim = current_dir if level_up_effect.sprite_frames.has_animation(current_dir) else "default"
		if not level_up_effect.sprite_frames.has_animation(target_anim):
			var anims = level_up_effect.sprite_frames.get_animation_names()
			if anims.size() > 0: target_anim = anims[0]
			else: return

		level_up_effect.visible = true
		level_up_effect.animation = target_anim
		level_up_effect.frame = 0
		level_up_effect.play(target_anim)
		_on_effect_frame_changed(level_up_effect)

func trigger_combat_action(new_action_state: String) -> void :
	if current_state == "death": return
	_buff_animation_generation += 1
	_buff_animation_reached_last_frame = false
	if new_action_state == "buff":
		_start_buff_animation_safety_timeout(_buff_animation_generation)
	if new_action_state.begins_with("attack_"):
		normal_attack_visual_started_msec = Time.get_ticks_msec()
	is_emoting = false
	current_state = new_action_state
	is_action_locked = true

	if is_instance_valid(doll_engine):
		for sprite_node in doll_engine.doll_sprites.values(): sprite_node.frame = 0


	if is_instance_valid(mecha_bullet_effect_l):
		mecha_bullet_effect_l.set_meta("attack_triggered", false)
	if is_instance_valid(mecha_bullet_effect_r):
		mecha_bullet_effect_r.set_meta("attack_triggered", false)
	if is_instance_valid(weapon_attack_effect):
		weapon_attack_effect.set_meta("attack_triggered", false)

	sync_animation()

func get_remaining_normal_hit_delay(full_delay: float) -> float:
	if full_delay <= 0.0 or normal_attack_visual_started_msec < 0:
		return max(full_delay, 0.0)
	var elapsed = float(Time.get_ticks_msec() - normal_attack_visual_started_msec) / 1000.0
	return max(full_delay - max(elapsed, 0.0), 0.0)

func _on_body_animation_finished() -> void :
	if current_state == "buff":
		if data_model.transform_type > 0:
			_unlock_combat_state()
			return
		_buff_animation_reached_last_frame = true
		_freeze_buff_animation_at_last_frame()
		_try_finish_buff_animation()
	elif current_state in ["attack_aa", "attack_ab", "attack_ga", "attack_gb", "attack_na", "attack_nb", "hit_weapon_melee", "hit_weapon_gun"]:
		_unlock_combat_state()
	elif current_state.begins_with("emote_"):
		is_emoting = false
		current_state = "idle"
		sync_animation()
		if NetworkManager and NetworkManager.is_connected_to_server:
			NetworkManager.send_secure_packet({"Type": "SyncState", "State": "idle", "Dir": current_dir})


func _unlock_combat_state() -> void :
	_buff_animation_generation += 1
	_buff_animation_reached_last_frame = false
	is_action_locked = false
	if is_entity_moving():
		current_state = "walk"
	else:
		current_state = get_combat_idle_state()
	sync_animation()

func get_combat_idle_state() -> String:
	if is_riding_mecha(): return "idle"

	if data_model.weapon_id == 0 or not is_instance_valid(doll_engine):
		return "idle_weapon_melee"

	var sword_sprite = doll_engine.doll_sprites.get("Sword")
	if not sword_sprite: sword_sprite = doll_engine.doll_sprites.get("R-Gun")
	if not sword_sprite: sword_sprite = doll_engine.doll_sprites.get("L-Gun")

	if not sword_sprite:
		return "idle_weapon_melee"

	var sf = sword_sprite.get_meta("original_sf") if sword_sprite.has_meta("original_sf") else sword_sprite.sprite_frames

	if sf:
		var has_ga = sf.has_animation("attack_ga_1")
		var has_gb = sf.has_animation("attack_gb_1")
		if has_ga or has_gb:
			return "idle_weapon_gun"

	return "idle_weapon_melee"

func get_combat_attack_state() -> String:
	if is_riding_mecha():
		if data_model.mecha_type == "MA":
			return "attack_na"

		if is_instance_valid(doll_engine) and doll_engine.doll_sprites.has("R-MSArm"):
			var arm_sprite = doll_engine.doll_sprites["R-MSArm"]
			var sf = arm_sprite.get_meta("original_sf") if arm_sprite.has_meta("original_sf") else arm_sprite.sprite_frames
			if sf:
				var has_na = sf.has_animation("attack_na_1")
				var has_nb = sf.has_animation("attack_nb_1")
				if has_na and has_nb: return "attack_na" if randf() < 0.5 else "attack_nb"
				elif has_na: return "attack_na"
				elif has_nb: return "attack_nb"
		return "attack_na"

	if data_model.weapon_id == 0 or not is_instance_valid(doll_engine):
		return "attack_aa"

	var sword_sprite = doll_engine.doll_sprites.get("Sword")
	if not sword_sprite: sword_sprite = doll_engine.doll_sprites.get("R-Gun")
	if not sword_sprite: sword_sprite = doll_engine.doll_sprites.get("L-Gun")

	if not sword_sprite: return "attack_aa"

	var sf = sword_sprite.get_meta("original_sf") if sword_sprite.has_meta("original_sf") else sword_sprite.sprite_frames
	if not sf: return "attack_aa"

	var has_aa = sf.has_animation("attack_aa_1")
	var has_ab = sf.has_animation("attack_ab_1")
	var has_ga = sf.has_animation("attack_ga_1")
	var has_gb = sf.has_animation("attack_gb_1")

	if has_aa and has_ab: return "attack_aa" if randf() < 0.5 else "attack_ab"
	elif has_aa: return "attack_aa"
	elif has_ab: return "attack_ab"
	elif has_ga: return "attack_ga"
	elif has_gb: return "attack_gb"

	return "attack_aa"

func get_combat_hit_state() -> String:
	if is_riding_mecha(): return "idle"

	if data_model.weapon_id == 0 or not is_instance_valid(doll_engine):
		return "hit_weapon_melee"

	var sword_sprite = doll_engine.doll_sprites.get("Sword")
	if not sword_sprite: sword_sprite = doll_engine.doll_sprites.get("R-Gun")
	if not sword_sprite: sword_sprite = doll_engine.doll_sprites.get("L-Gun")

	if not sword_sprite:
		return "hit_weapon_melee"

	var sf = sword_sprite.get_meta("original_sf") if sword_sprite.has_meta("original_sf") else sword_sprite.sprite_frames

	if sf:
		var has_ga = sf.has_animation("attack_ga_1")
		var has_gb = sf.has_animation("attack_gb_1")
		if has_ga or has_gb:
			return "hit_weapon_gun"

	return "hit_weapon_melee"


func is_riding_mecha() -> bool:
	return "summoned_horse_uid" in data_model and data_model.summoned_horse_uid != -1

func setup_layered_paper_dolls_initial_async() -> void :
	if data_model.transform_type > 0:
		setup_layered_paper_dolls()
		return

	if not is_instance_valid(doll_engine):
		return
	if is_instance_valid(transform_sprite):
		transform_sprite.visible = false
	doll_engine.visible = true
	if is_instance_valid(name_label):
		name_label.visible = not data_model.is_shop_open

	if is_riding_mecha():
		var body_id: int = 0
		var leg_id: int = 0
		var weapon_id: int = 0
		var dynamic_mecha_type: String = str(data_model.mecha_type)
		var mecha_horse_idx: int = 0

		if "horse_list" in data_model and data_model.horse_list.size() > 0:
			for h in data_model.horse_list:
				if int(h.get("horse_uid", -1)) == int(data_model.summoned_horse_uid):
					dynamic_mecha_type = str(h.get("horse_type", "MS"))
					data_model.mecha_type = dynamic_mecha_type
					mecha_horse_idx = int(h.get("horse_idx", 0))
					break

		for heq in data_model.horse_equip_list:
			if int(heq.get("horse_uid", -1)) == int(data_model.summoned_horse_uid) and int(heq.get("in_bank", 0)) == 0:
				var e_idx: int = int(heq.get("equip_idx", 0))
				var part: int = int(heq.get("part", 0))
				if part == 0:
					body_id = e_idx
					if ItemLoader:
						var item_mecha_type: String = str(ItemLoader.get_item_property(e_idx, "horse_type", ""))
						if item_mecha_type != "" and item_mecha_type != "0":
							dynamic_mecha_type = item_mecha_type
							data_model.mecha_type = dynamic_mecha_type
				elif part == 1:
					weapon_id = e_idx
				elif part == 2:
					leg_id = e_idx

		if dynamic_mecha_type == "MA" and body_id == 0:
			body_id = mecha_horse_idx
			if ItemLoader:
				var ma_body_id: int = int(ItemLoader.get_item_property(mecha_horse_idx, "body_id", 0))
				if ma_body_id > 0:
					body_id = ma_body_id

		await doll_engine.build_mecha_model_initial_async(
			current_race,
			current_hair,
			current_hair_color_index,
			int(data_model.bonnet_id),
			body_id,
			leg_id,
			weapon_id,
			dynamic_mecha_type
		)

		if data_model.transform_type == 0:
			if name_label: name_label.position.y = -160.0
			if title_label: title_label.position.y = -180.0
			if club_label: club_label.position.y = -200.0
	else:
		var equips: Dictionary = {
			0: data_model.bonnet_id,
			1: data_model.left_hand_id,
			2: data_model.right_hand_id,
			3: data_model.upper_armor_id,
			4: data_model.lower_armor_id,
			5: data_model.weapon_id,
			6: data_model.shield_id,
			7: data_model.back_id,
		}
		await doll_engine.build_human_model_initial_async(
			current_race,
			current_hair,
			current_hair_color_index,
			current_body_color_index,
			equips
		)

		if data_model.transform_type == 0:
			if name_label: name_label.position.y = -130.0
			if title_label: title_label.position.y = -150.0
			if club_label: club_label.position.y = -170.0

	_refresh_appearance_visibility()

func setup_layered_paper_dolls() -> void :

	if data_model.transform_type > 0:
		_ensure_transform_sprite()
		if is_instance_valid(doll_engine): doll_engine.visible = false
		transform_sprite.visible = true


		if data_model.transform_type == 1 and MonsterLoader:
			var frames = MonsterLoader.get_monster_frames(data_model.transform_id)
			if frames: transform_sprite.sprite_frames = frames

			var c_idx = data_model.get("transform_color_index") if "transform_color_index" in data_model else 0
			var mat = ShaderMaterial.new()
			mat.shader = PALETTE_SHADER
			mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(c_idx, 0.0))
			transform_sprite.material = mat
		elif data_model.transform_type == 2:

			var tex_path = "res://Assets/NPC/" + data_model.transform_id + ".png"
			var txt_path = "res://Assets/NPC/" + data_model.transform_id + ".txt"
			if ResourceLoader.exists(tex_path):
				var tex = load(tex_path) as Texture2D

				var offset_vector = Vector2.ZERO
				if FileAccess.file_exists(txt_path):
					var file = FileAccess.open(txt_path, FileAccess.READ)
					var content = file.get_as_text().strip_edges()
					var parts = content.split(",")
					if parts.size() >= 2:
						offset_vector = Vector2(float(parts[0]), float(parts[1]))

				tex.set_meta("old_offset", offset_vector)

				var sf = SpriteFrames.new()
				if sf.has_animation("default"):
					sf.remove_animation("default")
				sf.add_animation("default")
				sf.add_frame("default", tex)
				transform_sprite.sprite_frames = sf
				transform_sprite.animation = "default"
				transform_sprite.play("default")
				_on_transform_frame_changed()

		_lock_ui_position_for_transform()

		if is_instance_valid(name_label):
			name_label.visible = not (data_model.is_shop_open or data_model.transform_type == 2)


		if is_riding_mecha():
			var dynamic_mecha_type = data_model.mecha_type
			if "horse_list" in data_model and data_model.horse_list.size() > 0:
				for h in data_model.horse_list:
					if h.get("horse_uid", -1) == data_model.summoned_horse_uid:
						dynamic_mecha_type = h.get("horse_type", "MS")
						data_model.mecha_type = dynamic_mecha_type
						break
	else:
		if is_instance_valid(transform_sprite): transform_sprite.visible = false
		if is_instance_valid(doll_engine): doll_engine.visible = true
		if is_instance_valid(name_label): name_label.visible = not data_model.is_shop_open

	if not is_instance_valid(doll_engine) or not doll_engine.has_method("build_human_model"): return

	if is_riding_mecha():
		var body_id = 0
		var leg_id = 0
		var weapon_id = 0

		var dynamic_mecha_type = data_model.mecha_type
		var mecha_horse_idx = 0
		if "horse_list" in data_model and data_model.horse_list.size() > 0:
			for h in data_model.horse_list:
				if h.get("horse_uid", -1) == data_model.summoned_horse_uid:
					dynamic_mecha_type = h.get("horse_type", "MS")
					data_model.mecha_type = dynamic_mecha_type
					mecha_horse_idx = h.get("horse_idx", 0)
					break

		for heq in data_model.horse_equip_list:
			if heq.get("horse_uid", -1) == data_model.summoned_horse_uid and heq.get("in_bank", 0) == 0:
				var e_idx = heq.get("equip_idx", 0)
				var part = heq.get("part", 0)
				if part == 0:
					body_id = e_idx
					if ItemLoader:
						var item_mecha_type = str(ItemLoader.get_item_property(e_idx, "horse_type", ""))
						if item_mecha_type != "" and item_mecha_type != "0":
							dynamic_mecha_type = item_mecha_type
							data_model.mecha_type = dynamic_mecha_type
				elif part == 1: weapon_id = e_idx
				elif part == 2: leg_id = e_idx


		if dynamic_mecha_type == "MA" and body_id == 0:
			body_id = mecha_horse_idx
			if ItemLoader:

				var ma_body_id = ItemLoader.get_item_property(mecha_horse_idx, "body_id", 0)
				if ma_body_id > 0:
					body_id = ma_body_id

		doll_engine.build_mecha_model(current_race, current_hair, current_hair_color_index, data_model.bonnet_id, body_id, leg_id, weapon_id, dynamic_mecha_type)


		if data_model.transform_type == 0:
			if name_label: name_label.position.y = -160.0
			if title_label: title_label.position.y = -180.0
			if club_label: club_label.position.y = -200.0
	else:
		var equips = {
			0: data_model.bonnet_id, 1: data_model.left_hand_id, 2: data_model.right_hand_id, 
			3: data_model.upper_armor_id, 4: data_model.lower_armor_id, 5: data_model.weapon_id, 
			6: data_model.shield_id, 7: data_model.back_id
		}
		doll_engine.build_human_model(current_race, current_hair, current_hair_color_index, current_body_color_index, equips)


		if data_model.transform_type == 0:
			if name_label: name_label.position.y = -130.0
			if title_label: title_label.position.y = -150.0
			if club_label: club_label.position.y = -170.0

	_refresh_appearance_visibility()

func _check_and_update_part(part_index: int, old_id: int, new_id: int) -> void :
	if old_id != new_id:
		match part_index:
			0: data_model.bonnet_id = new_id
			1: data_model.left_hand_id = new_id
			2: data_model.right_hand_id = new_id
			3: data_model.upper_armor_id = new_id
			4: data_model.lower_armor_id = new_id
			5: data_model.weapon_id = new_id
			6: data_model.shield_id = new_id
			7: data_model.back_id = new_id
		update_paper_doll_part(part_index, new_id)

func update_paper_doll_part(part_index: int, new_item_id: int) -> void :
	if is_instance_valid(doll_engine) and doll_engine.has_method("update_paper_doll_part"):
		doll_engine.update_paper_doll_part(part_index, new_item_id)
	sync_animation()

func sync_animation() -> void :
	if not is_instance_valid(doll_engine) or not doll_engine.has_method("update_animation"): return


	var anim_dir = current_dir
	var flip = false
	if current_dir in ["5", "6", "7"]:
		flip = true
		match current_dir:
			"5": anim_dir = "3"
			"6": anim_dir = "2"
			"7": anim_dir = "1"


	if current_state.begins_with("attack_") or current_state.begins_with("hit_") or current_state == "guard" or current_state.begins_with("idle_weapon_"):
		if anim_dir == "4":
			anim_dir = "3"

			if int(global_position.x) % 2 == 0:
				flip = true
		elif anim_dir == "8":
			anim_dir = "1"

			if int(global_position.y) % 2 == 0:
				flip = true

	if current_state == "death":
		anim_dir = "1"
		flip = false


	if data_model.transform_type == 1 and not is_dead:
		_sync_monster_transform_animation(anim_dir, flip)
	elif data_model.transform_type == 2 and not is_dead:
		if is_instance_valid(transform_sprite):
			transform_sprite.flip_h = flip
			_on_transform_frame_changed()



	if is_instance_valid(doll_engine) and doll_engine.has_method("update_animation"):
		doll_engine.update_animation(current_state, anim_dir, flip, float(data_model.move_speed), float(data_model.attack_speed))

	if is_instance_valid(mecha_recover_effect) and mecha_recover_effect.sprite_frames:
		if is_riding_mecha() and current_state == "crouch":
			var target_anim = current_dir if mecha_recover_effect.sprite_frames.has_animation(current_dir) else "default"
			if not mecha_recover_effect.sprite_frames.has_animation(target_anim):
				var anims = mecha_recover_effect.sprite_frames.get_animation_names()
				if anims.size() > 0: target_anim = anims[0]
				else: target_anim = ""

			if target_anim != "":
				mecha_recover_effect.visible = true
				if mecha_recover_effect.animation != target_anim or not mecha_recover_effect.is_playing():
					mecha_recover_effect.animation = target_anim
					mecha_recover_effect.play(target_anim)
					_on_effect_frame_changed(mecha_recover_effect)
		else:
			mecha_recover_effect.visible = false
			mecha_recover_effect.stop()

	if is_instance_valid(mecha_bullet_effect_l) and is_instance_valid(mecha_bullet_effect_r):
		if is_riding_mecha() and current_state.begins_with("attack_") and data_model.transform_type == 0:
			if not mecha_bullet_effect_l.get_meta("attack_triggered", false):
				mecha_bullet_effect_l.set_meta("attack_triggered", true)
				mecha_bullet_effect_r.set_meta("attack_triggered", true)

				var weapon_id = 0
				for heq in data_model.horse_equip_list:
					if heq.get("horse_uid", -1) == data_model.summoned_horse_uid and heq.get("in_bank", 0) == 0:
						if heq.get("part", 0) == 1:
							weapon_id = heq.get("equip_idx", 0)
							break

				if weapon_id > 0 and ItemLoader:
					var bullet_id = int(ItemLoader.get_item_property(weapon_id, "bullet_id", -1))

					if bullet_id >= 0:
						var effect_name = "Horse/MS_Bullet%02d" % bullet_id
						var loaded_res = EffectLoader.get_effect(effect_name)

						if loaded_res:
							if not mecha_bullet_effect_l.sprite_frames or mecha_bullet_effect_l.get_meta("loaded_bullet_id", -1) != bullet_id:
								mecha_bullet_effect_l.sprite_frames = loaded_res
								mecha_bullet_effect_r.sprite_frames = loaded_res
								mecha_bullet_effect_l.set_meta("loaded_bullet_id", bullet_id)
								mecha_bullet_effect_r.set_meta("loaded_bullet_id", bullet_id)

							var bullet_dir = current_dir
							var bullet_flip = false
							if current_dir in ["5", "6", "7"]:
								bullet_flip = true
								match current_dir:
									"5": bullet_dir = "3"
									"6": bullet_dir = "2"
									"7": bullet_dir = "1"

							var bullet_anim = bullet_dir if mecha_bullet_effect_l.sprite_frames.has_animation(bullet_dir) else "default"

							if mecha_bullet_effect_l.sprite_frames.has_animation(bullet_anim):
								var atk_spd_mult = max(float(data_model.attack_speed), 10.0) / 100.0
								var mecha_duration = 1.0

								if is_instance_valid(doll_engine) and doll_engine.doll_sprites.has("MSBody"):
									var ms_body = doll_engine.doll_sprites["MSBody"]
									var ms_anim = ms_body.animation
									if ms_body.sprite_frames and ms_body.sprite_frames.has_animation(ms_anim):
										var m_frames = ms_body.sprite_frames.get_frame_count(ms_anim)
										var m_fps = ms_body.sprite_frames.get_animation_speed(ms_anim)
										if m_fps > 0:
											mecha_duration = float(m_frames) / (m_fps * atk_spd_mult)

								var b_frames = mecha_bullet_effect_l.sprite_frames.get_frame_count(bullet_anim)

								mecha_bullet_effect_l.set_as_top_level(true)
								mecha_bullet_effect_r.set_as_top_level(true)
								mecha_bullet_effect_l.flip_h = bullet_flip
								mecha_bullet_effect_r.flip_h = bullet_flip
								mecha_bullet_effect_l.visible = true
								mecha_bullet_effect_r.visible = true
								mecha_bullet_effect_l.stop()
								mecha_bullet_effect_r.stop()

								var offsets = mecha_bullet_offsets.get(current_dir, {"l_start": Vector2.ZERO, "l_end": Vector2.ZERO, "r_start": Vector2.ZERO, "r_end": Vector2.ZERO})

								var base_center = global_position + Vector2(0, -30)
								var target_base_center = base_center

								var target = get("combat_target")
								if is_instance_valid(target):
									target_base_center = target.global_position + Vector2(0, -30)
								else:
									var dir_vec = Vector2.ZERO
									match current_dir:
										"1": dir_vec = Vector2(-1, 1).normalized()
										"2": dir_vec = Vector2(-1, 0)
										"3": dir_vec = Vector2(-1, -1).normalized()
										"4": dir_vec = Vector2(0, -1)
										"5": dir_vec = Vector2(1, -1).normalized()
										"6": dir_vec = Vector2(1, 0)
										"7": dir_vec = Vector2(1, 1).normalized()
										"8": dir_vec = Vector2(0, 1)

									var a_range = 200.0
									if "attack_range" in data_model:
										a_range = float(data_model.attack_range)
									target_base_center = base_center + (dir_vec * a_range)

								var dist = base_center.distance_to(target_base_center)
								var bullet_speed = 600.0
								var flight_duration = max(dist / bullet_speed, 0.05)

								var start_pos_l = base_center + offsets["l_start"]
								var start_pos_r = base_center + offsets["r_start"]
								var end_pos_l = target_base_center + offsets["l_end"]
								var end_pos_r = target_base_center + offsets["r_end"]

								mecha_bullet_effect_l.global_position = start_pos_l
								mecha_bullet_effect_r.global_position = start_pos_r

								if bullet_tween and bullet_tween.is_running():
									bullet_tween.kill()

								bullet_tween = create_tween()
								bullet_tween.set_parallel(true)

								bullet_tween.tween_property(mecha_bullet_effect_l, "global_position", end_pos_l, flight_duration)
								bullet_tween.tween_property(mecha_bullet_effect_r, "global_position", end_pos_r, flight_duration)

								bullet_tween.tween_method(_update_bullet_frame.bind(bullet_anim), 0.0, float(b_frames - 0.01), flight_duration)

								bullet_tween.chain().tween_callback( func():
									mecha_bullet_effect_l.visible = false
									mecha_bullet_effect_r.visible = false
								)

		elif not current_state.begins_with("attack_"):
			if is_instance_valid(mecha_bullet_effect_l):
				mecha_bullet_effect_l.set_meta("attack_triggered", false)
			if is_instance_valid(mecha_bullet_effect_r):
				mecha_bullet_effect_r.set_meta("attack_triggered", false)


	if is_instance_valid(weapon_attack_effect):
		if not is_riding_mecha() and current_state.begins_with("attack_") and data_model.transform_type == 0:
			if not weapon_attack_effect.get_meta("attack_triggered", false):
				weapon_attack_effect.set_meta("attack_triggered", true)

				var weapon_id = data_model.weapon_id
				if weapon_id > 0 and ItemLoader:
					var note_2 = str(ItemLoader.get_item_property(weapon_id, "note_2", "")).strip_edges()

					if note_2 != "":

						var r_prefix = "B"
						match current_race:
							"Migu": r_prefix = "A"
							"Robot": r_prefix = "R"
							_: r_prefix = "B"


						var atk_suffix = current_state.replace("attack_", "").to_upper()


						var effect_name = "Weapon_Effect/%s-%s_%s" % [r_prefix, note_2, atk_suffix]
						var loaded_res = EffectLoader.get_effect(effect_name)

						if loaded_res:
							if weapon_attack_effect.sprite_frames != loaded_res:
								weapon_attack_effect.sprite_frames = loaded_res

							var eff_dir = anim_dir
							var eff_flip = flip
							var anim_to_play = eff_dir if loaded_res.has_animation(eff_dir) else "default"

							weapon_attack_effect.flip_h = eff_flip
							weapon_attack_effect.visible = true

							if weapon_attack_effect.animation != anim_to_play or not weapon_attack_effect.is_playing():
								weapon_attack_effect.animation = anim_to_play
								weapon_attack_effect.frame = 0
								weapon_attack_effect.speed_scale = max(float(data_model.attack_speed), 10.0) / 100.0
								weapon_attack_effect.play(anim_to_play)
								_on_effect_frame_changed(weapon_attack_effect)
		elif not current_state.begins_with("attack_"):
			weapon_attack_effect.set_meta("attack_triggered", false)
			weapon_attack_effect.visible = false
			weapon_attack_effect.stop()


func play_emotion(anim_name: String) -> void :
	if current_state == "death" or is_action_locked: return
	var base_state = anim_name.replace("_8", "")
	if not base_state.begins_with("emote_"): base_state = "emote_" + base_state.to_lower()

	is_emoting = true
	current_state = base_state
	if anim_name.ends_with("_8"): current_dir = "8"
	if is_instance_valid(doll_engine):
		for sprite_node in doll_engine.doll_sprites.values(): sprite_node.frame = 0
	sync_animation()

	if NetworkManager and NetworkManager.is_connected_to_server:
		NetworkManager.send_secure_packet({"Type": "SyncState", "State": current_state, "Dir": current_dir})

func play_emote(emote_letter: String) -> void :
	play_emotion("emote_" + emote_letter.to_lower())


func _ensure_name_tag() -> void :
	ui_container = get_node_or_null("UIContainer")
	if not ui_container:
		ui_container = Node2D.new()
		ui_container.name = "UIContainer"
		add_child(ui_container)
	ui_container.visible = true

	name_label = ui_container.get_node_or_null("NameLabel")
	if not name_label:
		name_label = Label.new()
		name_label.name = "NameLabel"
		name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
		ui_container.add_child(name_label)
	else:
		name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE

	title_label = ui_container.get_node_or_null("TitleLabel")
	if not title_label:
		title_label = Label.new()
		title_label.name = "TitleLabel"
		title_label.mouse_filter = Control.MOUSE_FILTER_IGNORE

		title_label.add_theme_color_override("font_color", GameConstants.COLOR_NAME_WHITE)
		title_label.add_theme_color_override("font_outline_color", Color.BLACK)
		title_label.add_theme_font_size_override("font_size", 16)
		title_label.add_theme_constant_override("outline_size", 4)
		title_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		title_label.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
		title_label.custom_minimum_size = Vector2(200, 20)
		title_label.position.x = -100
		title_label.visible = false
		ui_container.add_child(title_label)
	else:
		title_label.mouse_filter = Control.MOUSE_FILTER_IGNORE

	club_label = ui_container.get_node_or_null("ClubLabel")
	if not club_label:
		club_label = Label.new()
		club_label.name = "ClubLabel"
		club_label.mouse_filter = Control.MOUSE_FILTER_IGNORE

		club_label.add_theme_color_override("font_color", GameConstants.COLOR_NAME_WHITE)
		club_label.add_theme_color_override("font_outline_color", Color.BLACK)
		club_label.add_theme_font_size_override("font_size", 16)
		club_label.add_theme_constant_override("outline_size", 4)
		club_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		club_label.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
		club_label.custom_minimum_size = Vector2(200, 20)
		club_label.position.x = -100
		club_label.visible = false
		ui_container.add_child(club_label)
	else:
		club_label.mouse_filter = Control.MOUSE_FILTER_IGNORE

	chat_bubble_bg = ui_container.get_node_or_null("ChatBubbleBG")
	if not chat_bubble_bg:
		chat_bubble_bg = NinePatchRect.new()
		chat_bubble_bg.name = "ChatBubbleBG"
		chat_bubble_bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
		chat_bubble_bg.visible = false
		chat_bubble_bg.z_as_relative = false
		chat_bubble_bg.z_index = 60
		ui_container.add_child(chat_bubble_bg)

		chat_bubble_label = Label.new()
		chat_bubble_label.name = "ChatBubbleLabel"
		chat_bubble_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
		chat_bubble_label.add_theme_color_override("font_color", Color.BLACK)
		chat_bubble_label.add_theme_font_size_override("font_size", 13)
		chat_bubble_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		chat_bubble_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		chat_bubble_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		chat_bubble_label.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
		chat_bubble_bg.add_child(chat_bubble_label)

	chat_bubble_timer = get_node_or_null("ChatBubbleTimer")
	if not chat_bubble_timer:
		chat_bubble_timer = Timer.new()
		chat_bubble_timer.name = "ChatBubbleTimer"
		chat_bubble_timer.one_shot = true
		chat_bubble_timer.timeout.connect(_on_chat_bubble_timeout)
		add_child(chat_bubble_timer)

func _ensure_effects() -> void :
	if not is_instance_valid(mecha_recover_effect):
		mecha_recover_effect = AnimatedSprite2D.new()
		mecha_recover_effect.name = "MechaRecoverEffect"
		mecha_recover_effect.visible = false
		mecha_recover_effect.z_index = 50
		mecha_recover_effect.position = Vector2.ZERO
		mecha_recover_effect.centered = false
		add_child(mecha_recover_effect)

		mecha_recover_effect.sprite_frames = EffectLoader.get_effect("Horse/MS_Charging")
		if not mecha_recover_effect.frame_changed.is_connected(_on_effect_frame_changed.bind(mecha_recover_effect)):
			mecha_recover_effect.frame_changed.connect(_on_effect_frame_changed.bind(mecha_recover_effect))

	if not is_instance_valid(level_up_effect):
		level_up_effect = AnimatedSprite2D.new()
		level_up_effect.name = "LevelUpEffect"
		level_up_effect.visible = false
		level_up_effect.z_index = 60
		level_up_effect.position = Vector2.ZERO
		level_up_effect.centered = false
		add_child(level_up_effect)

		level_up_effect.sprite_frames = EffectLoader.get_effect("Normal_Effect/Normal_Effect08")
		level_up_effect.animation_finished.connect( func():
			level_up_effect.visible = false
			level_up_effect.stop()
		)
		if not level_up_effect.frame_changed.is_connected(_on_effect_frame_changed.bind(level_up_effect)):
			level_up_effect.frame_changed.connect(_on_effect_frame_changed.bind(level_up_effect))

	if not is_instance_valid(mecha_transform_effect):
		mecha_transform_effect = AnimatedSprite2D.new()
		mecha_transform_effect.name = "MechaTransformEffect"
		mecha_transform_effect.visible = false
		mecha_transform_effect.z_index = 60
		mecha_transform_effect.position = Vector2.ZERO
		mecha_transform_effect.centered = false
		add_child(mecha_transform_effect)

		mecha_transform_effect.sprite_frames = EffectLoader.get_effect("Horse/MS_Inout")
		mecha_transform_effect.animation_finished.connect( func():
			mecha_transform_effect.visible = false
			mecha_transform_effect.stop()
			if not (is_dead and is_instance_valid(death_effect) and death_effect.is_playing()):
				_refresh_appearance_visibility()
		)
		if not mecha_transform_effect.frame_changed.is_connected(_on_effect_frame_changed.bind(mecha_transform_effect)):
			mecha_transform_effect.frame_changed.connect(_on_effect_frame_changed.bind(mecha_transform_effect))

	if not is_instance_valid(mecha_bullet_effect_l):
		mecha_bullet_effect_l = AnimatedSprite2D.new()
		mecha_bullet_effect_l.name = "MechaBulletEffectL"
		mecha_bullet_effect_l.visible = false
		mecha_bullet_effect_l.z_index = 55
		mecha_bullet_effect_l.position = Vector2.ZERO
		mecha_bullet_effect_l.centered = false
		add_child(mecha_bullet_effect_l)

		mecha_bullet_effect_l.animation_finished.connect( func():
			mecha_bullet_effect_l.visible = false
			mecha_bullet_effect_l.stop()
		)
		if not mecha_bullet_effect_l.frame_changed.is_connected(_on_effect_frame_changed.bind(mecha_bullet_effect_l)):
			mecha_bullet_effect_l.frame_changed.connect(_on_effect_frame_changed.bind(mecha_bullet_effect_l))

	if not is_instance_valid(mecha_bullet_effect_r):
		mecha_bullet_effect_r = AnimatedSprite2D.new()
		mecha_bullet_effect_r.name = "MechaBulletEffectR"
		mecha_bullet_effect_r.visible = false
		mecha_bullet_effect_r.z_index = 55
		mecha_bullet_effect_r.position = Vector2.ZERO
		mecha_bullet_effect_r.centered = false
		add_child(mecha_bullet_effect_r)

		mecha_bullet_effect_r.animation_finished.connect( func():
			mecha_bullet_effect_r.visible = false
			mecha_bullet_effect_r.stop()
		)
		if not mecha_bullet_effect_r.frame_changed.is_connected(_on_effect_frame_changed.bind(mecha_bullet_effect_r)):
			mecha_bullet_effect_r.frame_changed.connect(_on_effect_frame_changed.bind(mecha_bullet_effect_r))

	if not is_instance_valid(death_effect):
		death_effect = AnimatedSprite2D.new()
		death_effect.name = "DeathEffect"
		death_effect.visible = false
		death_effect.z_index = 60
		death_effect.position = Vector2.ZERO
		death_effect.centered = false
		add_child(death_effect)

		death_effect.animation_finished.connect( func():
			death_effect.visible = false
			death_effect.stop()

			if is_instance_valid(doll_engine):
				doll_engine.visible = true

			if is_instance_valid(transform_sprite):
				transform_sprite.visible = false

			sync_animation()
		)
		if not death_effect.frame_changed.is_connected(_on_effect_frame_changed.bind(death_effect)):
			death_effect.frame_changed.connect(_on_effect_frame_changed.bind(death_effect))

	if not is_instance_valid(weapon_attack_effect):
		weapon_attack_effect = AnimatedSprite2D.new()
		weapon_attack_effect.name = "WeaponAttackEffect"
		weapon_attack_effect.visible = false
		weapon_attack_effect.z_index = 55
		weapon_attack_effect.position = Vector2.ZERO
		weapon_attack_effect.centered = false
		add_child(weapon_attack_effect)

		weapon_attack_effect.animation_finished.connect( func():
			weapon_attack_effect.visible = false
			weapon_attack_effect.stop()
		)

		if not weapon_attack_effect.frame_changed.is_connected(_on_effect_frame_changed.bind(weapon_attack_effect)):
			weapon_attack_effect.frame_changed.connect(_on_effect_frame_changed.bind(weapon_attack_effect))

	if not is_instance_valid(item_effect):
		item_effect = AnimatedSprite2D.new()
		item_effect.name = "ItemEffect"
		item_effect.visible = false
		item_effect.z_index = 55
		item_effect.position = Vector2.ZERO
		item_effect.centered = false
		add_child(item_effect)

		item_effect.animation_finished.connect(_on_item_effect_finished)
		if not item_effect.frame_changed.is_connected(_on_effect_frame_changed.bind(item_effect)):
			item_effect.frame_changed.connect(_on_effect_frame_changed.bind(item_effect))

func _clear_mecha_bullet_effects() -> void :
	if bullet_tween and bullet_tween.is_running():
		bullet_tween.kill()
	bullet_tween = null
	for effect in [mecha_bullet_effect_l, mecha_bullet_effect_r]:
		if not is_instance_valid(effect):
			continue
		effect.visible = false
		effect.stop()
		effect.set_meta("attack_triggered", false)

func _update_bullet_frame(frame_idx_float: float, bullet_anim: String) -> void :
	var frame_idx = int(floor(frame_idx_float))
	for effect in [mecha_bullet_effect_l, mecha_bullet_effect_r]:
		if not is_instance_valid(effect) or not effect.sprite_frames: continue
		var total = effect.sprite_frames.get_frame_count(bullet_anim)
		var safe_idx = frame_idx
		if safe_idx >= total: safe_idx = total - 1

		if effect.frame != safe_idx or effect.animation != bullet_anim:
			effect.animation = bullet_anim
			effect.frame = safe_idx
			_on_effect_frame_changed(effect)

func _on_effect_frame_changed(effect_node: AnimatedSprite2D) -> void :
	if not effect_node.sprite_frames: return
	EffectLoader.play_confirmed_frame_audio(effect_node)

	var anim = effect_node.animation
	if not effect_node.sprite_frames.has_animation(anim): return
	var total_frames = effect_node.sprite_frames.get_frame_count(anim)
	var safe_frame = clampi(effect_node.frame, 0, total_frames - 1)
	var tex = effect_node.sprite_frames.get_frame_texture(anim, safe_frame)

	if not tex: return

	var offset_val = EffectLoader.get_offset(tex)
	var tex_w = tex.get_width()
	if tex is AtlasTexture:
		tex_w = tex.region.size.x

	var actual_offset_x = tex_w - offset_val.x if effect_node.flip_h else offset_val.x
	effect_node.offset = Vector2( - actual_offset_x, - offset_val.y)

func _play_transient_skill_effect(
	frames: SpriteFrames, 
	animation_name: StringName, 
	flip_effect_h: bool, 
	effect_z_index: int, 
	playback_speed: float, 
	is_self_cast_effect: bool = false
) -> AnimatedSprite2D:
	if not frames or not frames.has_animation(animation_name):
		return null

	var effect_node = AnimatedSprite2D.new()
	effect_node.name = "TransientSkillEffect"
	effect_node.sprite_frames = frames
	effect_node.animation = animation_name
	effect_node.frame = 0
	effect_node.flip_h = flip_effect_h
	effect_node.z_index = effect_z_index
	effect_node.position = Vector2.ZERO
	effect_node.centered = false
	effect_node.speed_scale = playback_speed
	effect_node.set_meta("is_self_cast_effect", is_self_cast_effect)
	add_child(effect_node)

	_active_transient_skill_effects.append(effect_node)
	effect_node.frame_changed.connect(_on_effect_frame_changed.bind(effect_node))
	effect_node.animation_finished.connect(
		_on_transient_skill_effect_finished.bind(effect_node), 
		CONNECT_ONE_SHOT
	)
	effect_node.play(animation_name)
	_on_effect_frame_changed(effect_node)
	return effect_node

func _on_transient_skill_effect_finished(effect_node: AnimatedSprite2D) -> void :
	_active_transient_skill_effects.erase(effect_node)
	if is_instance_valid(effect_node):
		effect_node.stop()
		effect_node.queue_free()
	_try_finish_buff_animation()

func _freeze_buff_animation_at_last_frame() -> void :
	if not is_instance_valid(doll_engine):
		return
	for sprite_node in doll_engine.doll_sprites.values():
		if not is_instance_valid(sprite_node) or not sprite_node.sprite_frames:
			continue
		var animation_name = sprite_node.animation
		if not str(animation_name).begins_with("buff_") or not sprite_node.sprite_frames.has_animation(animation_name):
			continue
		var frame_count = sprite_node.sprite_frames.get_frame_count(animation_name)
		if frame_count <= 0:
			continue
		sprite_node.frame = frame_count - 1
		sprite_node.pause()

func _try_finish_buff_animation() -> void :
	if current_state != "buff" or not _buff_animation_reached_last_frame:
		return
	for effect_node in _active_transient_skill_effects:
		if is_instance_valid(effect_node) and bool(effect_node.get_meta("is_self_cast_effect", false)):
			return
	_unlock_combat_state()

func _start_buff_animation_safety_timeout(generation: int) -> void :
	var scene_tree = get_tree()
	if scene_tree == null:
		return
	await scene_tree.create_timer(BUFF_ANIMATION_SAFETY_TIMEOUT_SECONDS).timeout
	if generation != _buff_animation_generation or current_state != "buff":
		return
	_unlock_combat_state()

func _clear_transient_skill_effects() -> void :
	var effects_to_clear = _active_transient_skill_effects.duplicate()
	_active_transient_skill_effects.clear()
	_buff_animation_generation += 1
	_buff_animation_reached_last_frame = false
	for effect_node in effects_to_clear:
		if is_instance_valid(effect_node):
			effect_node.stop()
			effect_node.queue_free()

func _ensure_transform_sprite() -> void :
	if not is_instance_valid(transform_sprite):
		transform_sprite = AnimatedSprite2D.new()
		transform_sprite.name = "TransformSprite"
		transform_sprite.visible = false
		transform_sprite.centered = false
		add_child(transform_sprite)


		if is_instance_valid(doll_engine):
			move_child(transform_sprite, doll_engine.get_index() + 1)

		transform_sprite.animation_finished.connect(_on_transform_animation_finished)
		transform_sprite.frame_changed.connect(_on_transform_frame_changed)

func _refresh_appearance_visibility() -> void :
	var show_transform = data_model.transform_type > 0
	if is_instance_valid(transform_sprite):
		transform_sprite.visible = show_transform
	if is_instance_valid(doll_engine):
		doll_engine.visible = not show_transform
func _on_transform_animation_finished() -> void :
	if current_state in ["attack_aa", "attack_ab", "attack_ga", "attack_gb", "attack_na", "attack_nb", "hit_weapon_melee", "hit_weapon_gun", "buff"]:
		_unlock_combat_state()
	elif current_state.begins_with("emote_"):
		is_emoting = false
		current_state = "idle"
		sync_animation()
		if NetworkManager and NetworkManager.is_connected_to_server:
			NetworkManager.send_secure_packet({"Type": "SyncState", "State": "idle", "Dir": current_dir})

func _on_transform_frame_changed() -> void :
	if data_model.transform_type > 0 and is_instance_valid(transform_sprite) and transform_sprite.sprite_frames:
		var anim_name = transform_sprite.animation
		if transform_sprite.sprite_frames.has_animation(anim_name):
			var tex = transform_sprite.sprite_frames.get_frame_texture(anim_name, transform_sprite.frame)
			if tex:
				var offset_val = tex.get_meta("old_offset") if tex.has_meta("old_offset") else Vector2.ZERO

				if data_model.transform_type == 1:
					var actual_offset_x = tex.get_width() - offset_val.x if transform_sprite.flip_h else offset_val.x
					transform_sprite.offset = Vector2( - actual_offset_x, - offset_val.y)
				elif data_model.transform_type == 2:

					if offset_val != Vector2.ZERO:
						var actual_offset_x = tex.get_width() - offset_val.x if transform_sprite.flip_h else offset_val.x
						transform_sprite.offset = Vector2( - actual_offset_x, - offset_val.y)
					else:
						var actual_offset_x = tex.get_width() / 2.0
						transform_sprite.offset = Vector2( - actual_offset_x, - tex.get_height())

func play_mecha_transform() -> void :
	if is_instance_valid(mecha_transform_effect):
		if mecha_transform_effect.sprite_frames:
			var available_anims = mecha_transform_effect.sprite_frames.get_animation_names()

			if is_instance_valid(doll_engine):
				doll_engine.visible = false
				doll_engine.modulate.a = 1.0

			var target_anim = current_dir if mecha_transform_effect.sprite_frames.has_animation(current_dir) else "default"
			if not mecha_transform_effect.sprite_frames.has_animation(target_anim):
				if available_anims.size() > 0:
					target_anim = available_anims[0]
				else:
					return

			mecha_transform_effect.visible = true
			mecha_transform_effect.animation = target_anim
			mecha_transform_effect.frame = 0
			mecha_transform_effect.play(target_anim)
			_on_effect_frame_changed(mecha_transform_effect)
		else:
			print("[Mecha過場-網路玩家] ❌ 錯誤：sprite_frames 為空！(EffectLoader 可能沒有讀到 .tres 檔)")
	else:
		print("[Mecha過場-網路玩家] ❌ 錯誤：mecha_transform_effect 節點未建立或已損毀！")

func show_chat_bubble(text: String, bubble_type: int = 0, margins: Dictionary = {}) -> void :
	if not chat_bubble_bg or not chat_bubble_label: return
	var tex_path = "res://ui/BG_BUBBLE_%02d.png" % clamp(bubble_type + 1, 1, 7)
	chat_bubble_bg.texture = load(tex_path)
	chat_bubble_label.text = text

	var fixed_bubble_size = Vector2(64, 48)
	if chat_bubble_bg.texture: fixed_bubble_size = chat_bubble_bg.texture.get_size()

	chat_bubble_bg.size = fixed_bubble_size
	chat_bubble_bg.position.x = - (chat_bubble_bg.size.x / 2.0)
	chat_bubble_bg.position.y = -120 - chat_bubble_bg.size.y

	var m_left = margins.get("left", 8.0)
	var m_right = margins.get("right", 8.0)
	var m_top = margins.get("top", 8.0)
	var m_bottom = margins.get("bottom", 15.0)

	chat_bubble_label.size = Vector2(fixed_bubble_size.x - m_left - m_right, fixed_bubble_size.y - m_top - m_bottom)
	chat_bubble_label.position = Vector2(m_left, m_top)

	if bubble_fade_tween and bubble_fade_tween.is_running(): bubble_fade_tween.kill()
	chat_bubble_bg.modulate.a = 1.0
	chat_bubble_bg.visible = true
	chat_bubble_timer.start(5.0)

func hide_chat_bubble() -> void :
	if not chat_bubble_bg or not chat_bubble_bg.visible: return

	if bubble_fade_tween and bubble_fade_tween.is_running():
		bubble_fade_tween.kill()

	bubble_fade_tween = create_tween()
	bubble_fade_tween.tween_property(chat_bubble_bg, "modulate:a", 0.0, 0.3)
	bubble_fade_tween.tween_callback( func(): chat_bubble_bg.visible = false)

func _on_chat_bubble_timeout() -> void :
	if not chat_bubble_bg: return
	if bubble_fade_tween and bubble_fade_tween.is_running(): bubble_fade_tween.kill()
	bubble_fade_tween = create_tween()
	bubble_fade_tween.tween_property(chat_bubble_bg, "modulate:a", 0.0, 0.5)
	bubble_fade_tween.tween_callback( func(): chat_bubble_bg.visible = false)

func _ensure_shop_board() -> void :
	if not is_instance_valid(ui_container):
		_ensure_name_tag()
		if not is_instance_valid(ui_container): return


	if not is_instance_valid(shop_board_node):
		shop_board_node = SHOP_BOARD_SCENE.instantiate()
		shop_board_node.name = "ShopBoardNode"
		shop_board_node.visible = false
		ui_container.add_child(shop_board_node)


		shop_board_label = shop_board_node.get_node_or_null("ShopBoardLabel")
		shop_owner_label = shop_board_node.get_node_or_null("ShopOwnerLabel")

func _on_sync_shop_board(p_entity_id: int, is_open: bool, shop_name: String) -> void :
	if p_entity_id != data_model.db_id: return

	data_model.set("is_shop_open", is_open)
	data_model.set("shop_name", shop_name)
	_ensure_shop_board()

	if not is_instance_valid(shop_board_node): return

	if is_open:

		if shop_board_label: shop_board_label.text = shop_name
		if shop_owner_label: shop_owner_label.text = data_model.character_name


		var fixed_size = shop_board_node.size
		if fixed_size == Vector2.ZERO:
			fixed_size = Vector2(119, 32)

		if is_riding_mecha():
			shop_board_node.position = Vector2( - (fixed_size.x / 2.0), -180)
		else:
			shop_board_node.position = Vector2( - (fixed_size.x / 2.0), -150)

		shop_board_node.visible = true
		if name_label: name_label.visible = false
	else:
		shop_board_node.visible = false
		if name_label: name_label.visible = (data_model.transform_type != 2)

func _apply_name_color(color_state: int) -> void :
	_name_color_state = color_state
	_name_color_flash_generation += 1
	var flash_generation = _name_color_flash_generation

	if color_state == 3:
		_run_name_color_flash(flash_generation)
		return

	_apply_name_color_style(color_state)

func _run_name_color_flash(flash_generation: int) -> void :
	var started_msec = Time.get_ticks_msec()
	var show_purple = true

	while flash_generation == _name_color_flash_generation and _name_color_state == 3:
		if not is_inside_tree() or not is_instance_valid(name_label):
			return

		var elapsed = float(Time.get_ticks_msec() - started_msec) / 1000.0
		if elapsed >= PURPLE_NAME_FLASH_DURATION_SECONDS:
			break

		_apply_name_color_style(2 if show_purple else 0)
		show_purple = not show_purple

		var progress = clampf(elapsed / PURPLE_NAME_FLASH_DURATION_SECONDS, 0.0, 1.0)
		var interval = lerpf(
			PURPLE_NAME_FLASH_START_INTERVAL_SECONDS, 
			PURPLE_NAME_FLASH_END_INTERVAL_SECONDS, 
			progress
		)
		await get_tree().create_timer(interval).timeout

	if flash_generation == _name_color_flash_generation and _name_color_state == 3:
		_apply_name_color_style(0)

func _apply_name_color_style(color_state: int) -> void :
	if not name_label: return
	name_label.add_theme_font_size_override("font_size", 16)
	name_label.add_theme_constant_override("outline_size", 3)

	var is_party_member = false
	var local_player = GameManager.current_player if GameManager else null
	if local_player and is_instance_valid(local_player) and "data_model" in local_player:
		var my_party_id = local_player.data_model.party_id
		if my_party_id != "" and my_party_id == data_model.party_id:
			is_party_member = true

	if color_state == 1 or data_model.red_man > 0:
		name_label.add_theme_color_override("font_color", GameConstants.COLOR_NAME_RED_MAN)
		name_label.add_theme_color_override("font_outline_color", GameConstants.COLOR_NAME_WHITE)
	elif is_party_member:
		name_label.add_theme_color_override("font_color", GameConstants.COLOR_NAME_PARTY)
		name_label.add_theme_color_override("font_outline_color", GameConstants.COLOR_NAME_PK)
	elif color_state == 2:
		name_label.add_theme_color_override("font_color", GameConstants.COLOR_NAME_PURPLE)
		name_label.add_theme_color_override("font_outline_color", GameConstants.COLOR_NAME_WHITE)
	else:
		if data_model.pk_status == 1:
			name_label.add_theme_color_override("font_color", GameConstants.COLOR_NAME_WHITE)
			name_label.add_theme_color_override("font_outline_color", GameConstants.COLOR_NAME_PK)
		else:
			name_label.add_theme_color_override("font_color", GameConstants.COLOR_NAME_NORMAL)
			name_label.add_theme_color_override("font_outline_color", GameConstants.COLOR_NAME_WHITE)

func _on_entity_name_color_updated(p_entity_id: int, color_state: int, pk_status: int = -1, red_man_kill_left: int = -1, party_id: String = "") -> void :
	if p_entity_id != data_model.db_id: return

	if color_state == 1: data_model.red_man = 1
	elif color_state == 0: data_model.red_man = 0

	if pk_status != -1: data_model.pk_status = pk_status
	if red_man_kill_left != -1: data_model.red_man_kill_left = red_man_kill_left
	if party_id != "": data_model.party_id = party_id

	_apply_name_color(color_state)

	var hud = GameManager.current_hud if GameManager else null
	if hud and hud.has_method("get_node_or_null"):
		var char_win = hud.get_node_or_null("WindowsContainer/CharacterWindow")
		if char_win and char_win.visible and char_win.has_method("update_ui"):
			char_win.update_ui()

func _on_party_sync_received(p_id: String, _leader_id: int, members: Array) -> void :
	var is_in_party = false
	for m in members:
		if int(m.get("DBID", 0)) == data_model.db_id:
			is_in_party = true
			break

	if is_in_party: data_model.party_id = p_id
	elif data_model.party_id == p_id: data_model.party_id = ""

	var visible_color_state = 2 if _name_color_state == 3 else _name_color_state
	_apply_name_color_style(visible_color_state)

func execute_mecha_transition(setup_func: Callable) -> void :
	if transition_tween and transition_tween.is_running():
		transition_tween.kill()

	setup_func.call()

	if is_instance_valid(mecha_transform_effect):
		if mecha_transform_effect.sprite_frames:
			var available_anims = mecha_transform_effect.sprite_frames.get_animation_names()

			if is_instance_valid(doll_engine):
				doll_engine.visible = false
				doll_engine.modulate.a = 1.0

			var target_anim = current_dir if mecha_transform_effect.sprite_frames.has_animation(current_dir) else "default"
			if not mecha_transform_effect.sprite_frames.has_animation(target_anim):
				if available_anims.size() > 0:
					target_anim = available_anims[0]
				else:
					print("[Mecha過場-本機] ❌ 嚴重錯誤：資源內沒有任何動畫可播放！")
					return

			mecha_transform_effect.visible = true
			mecha_transform_effect.animation = target_anim
			mecha_transform_effect.frame = 0
			mecha_transform_effect.play(target_anim)
			_on_effect_frame_changed(mecha_transform_effect)
		else:
			print("[Mecha過場-本機] ❌ 錯誤：sprite_frames 為空！(EffectLoader 可能沒有讀到 .tres 檔)")
	else:
		print("[Mecha過場-本機] ❌ 錯誤：mecha_transform_effect 節點未建立或已損毀！")

func execute_skill_visuals(skill_id: int, skill_level: int, target_node: Node2D = null, aoe_targets: Array = []) -> void :
	if SkillLoader:
		var s_res = SkillLoader.get_skill_data(skill_id)
		if not s_res: return


		if is_instance_valid(target_node):
			var dir_vec = target_node.global_position - global_position
			if dir_vec != Vector2.ZERO:
				var angle_deg = posmod(rad_to_deg(dir_vec.angle()), 360.0)
				var index_raw = int(round(angle_deg / 45.0))
				var DIR_STRINGS = ["6", "7", "8", "1", "2", "3", "4", "5"]
				current_dir = DIR_STRINGS[index_raw % 8]


		if not is_riding_mecha():
			if s_res.skill_type == 0 or s_res.skill_type == 1:
				if has_method("trigger_combat_action") and has_method("get_combat_attack_state"):
					trigger_combat_action(get_combat_attack_state())
			elif s_res.skill_type >= 2 and s_res.skill_type <= 7:
				if has_method("trigger_combat_action"):
					trigger_combat_action("buff")


		var fx_use = s_res.fx_use
		if fx_use != "":
			var r_prefix = "B"
			match current_race:
				"Migu": r_prefix = "A"
				"Robot": r_prefix = "R"
				_: r_prefix = "B"

			var effect_name = "Skill_Effect/%s_%s" % [r_prefix, fx_use]
			var loaded_res = EffectLoader.get_effect(effect_name)
			if loaded_res:
				var eff_dir = current_dir
				var eff_flip = false
				if current_dir in ["5", "6", "7"]:
					eff_flip = true
					match current_dir:
						"5": eff_dir = "3"
						"6": eff_dir = "2"
						"7": eff_dir = "1"

				var anim_to_play = eff_dir if loaded_res.has_animation(eff_dir) else "default"
				if not loaded_res.has_animation(anim_to_play):
					var anims = loaded_res.get_animation_names()
					if anims.size() > 0: anim_to_play = anims[0]

				var p_spd = 1.0
				if "data_model" in self and self.data_model: p_spd = max(float(self.data_model.attack_speed), 10.0) / 100.0
				elif "attack_speed" in self: p_spd = max(float(self.attack_speed), 10.0) / 100.0

				_play_transient_skill_effect(loaded_res, anim_to_play, eff_flip, 55, p_spd, true)


		var fx_hit = s_res.fx_hit
		if fx_hit != "":

			var is_aoe_target_hit: bool = false
			if s_res.skill_type == 7 and not aoe_targets.is_empty():
				var special_text: String = str(s_res.special_skill)
				is_aoe_target_hit = (
					special_text.contains("挑撥")
					or special_text.contains("範圍攻擊")
				)

			if is_aoe_target_hit:
				for t_info in aoe_targets:
					var t_id = int(t_info.get("ID", t_info.get("id", 0)))
					if NetworkEntityManager and NetworkEntityManager.active_entities.has(t_id):
						var t_node = NetworkEntityManager.active_entities[t_id]
						if is_instance_valid(t_node) and t_node.has_method("_play_skill_hit"):
							t_node._play_skill_hit(fx_hit)

			elif s_res.skill_type >= 2 and s_res.skill_type <= 7:



				var is_attack_special = false
				if s_res.skill_type == 7 and "special_skill" in s_res and s_res.special_skill != null:
					var sp_str = s_res.special_skill
					if "惡魔之擊" in sp_str or "天使之淚" in sp_str or "月神審判" in sp_str or "生命吸收" in sp_str or "魂體吸收" in sp_str or "霜龍召喚" in sp_str:
						is_attack_special = true

				if is_attack_special:

					pass
				elif s_res.skill_type == 3 or s_res.skill_type == 7:

					_play_skill_hit(fx_hit, true)
				else:


					if is_instance_valid(target_node) and target_node != self:
						if target_node.has_method("_play_skill_hit"):
							target_node._play_skill_hit(fx_hit)
					else:
						_play_skill_hit(fx_hit, true)


		var fx_extra = s_res.fx_extra
		if fx_extra != "":
			_play_skill_extra(fx_extra)


func _play_skill_hit(fx_hit: String, is_self_cast_effect: bool = false) -> void :
	var effect_name = ""
	if fx_hit.begins_with("Normal"):
		effect_name = "Normal_Effect/%s" % fx_hit
	elif fx_hit.begins_with("Skill"):
		effect_name = "Skill_Effect/%s" % fx_hit
	else:
		effect_name = "Skill_Effect/%s" % fx_hit

	var loaded_res = EffectLoader.get_effect(effect_name)


	if not loaded_res:
		loaded_res = EffectLoader.get_effect("Hit_Effect/%s" % fx_hit)

	if loaded_res:
		var eff_dir = current_dir
		var eff_flip = false
		if current_dir in ["5", "6", "7"]:
			eff_flip = true
			match current_dir:
				"5": eff_dir = "3"
				"6": eff_dir = "2"
				"7": eff_dir = "1"

		var anim_to_play = eff_dir if loaded_res.has_animation(eff_dir) else "default"
		if not loaded_res.has_animation(anim_to_play):
			var anims = loaded_res.get_animation_names()
			if anims.size() > 0: anim_to_play = anims[0]

		var p_spd = 1.0
		if "data_model" in self and self.data_model: p_spd = max(float(self.data_model.attack_speed), 10.0) / 100.0
		elif "attack_speed" in self: p_spd = max(float(self.attack_speed), 10.0) / 100.0

		_play_transient_skill_effect(loaded_res, anim_to_play, eff_flip, 60, p_spd, is_self_cast_effect)


func _play_skill_extra(fx_extra: String) -> void :
	var effect_name = ""
	if fx_extra.begins_with("Normal"):
		effect_name = "Normal_Effect/%s" % fx_extra
	elif fx_extra.begins_with("Skill"):
		effect_name = "Skill_Effect/%s" % fx_extra
	else:
		effect_name = "Skill_Effect/%s" % fx_extra

	var loaded_res = EffectLoader.get_effect(effect_name)

	if not loaded_res:
		loaded_res = EffectLoader.get_effect("Hit_Effect/%s" % fx_extra)

	if loaded_res:
		var eff_dir = current_dir
		var eff_flip = false
		if current_dir in ["5", "6", "7"]:
			eff_flip = true
			match current_dir:
				"5": eff_dir = "3"
				"6": eff_dir = "2"
				"7": eff_dir = "1"

		var anim_to_play = eff_dir if loaded_res.has_animation(eff_dir) else "default"
		if not loaded_res.has_animation(anim_to_play):
			var anims = loaded_res.get_animation_names()
			if anims.size() > 0: anim_to_play = anims[0]

		var p_spd = 1.0
		if "data_model" in self and self.data_model: p_spd = max(float(self.data_model.attack_speed), 10.0) / 100.0
		elif "attack_speed" in self: p_spd = max(float(self.attack_speed), 10.0) / 100.0

		_play_transient_skill_effect(loaded_res, anim_to_play, eff_flip, 61, p_spd, true)

func _play_item_effect(item_index: int) -> void :
	if not ItemLoader: return

	var note_1 = str(ItemLoader.get_item_property(item_index, "note_1", "")).strip_edges()
	var note_2 = str(ItemLoader.get_item_property(item_index, "note_2", "")).strip_edges()


	if note_1 == "" and note_2 == "": return
	if not is_instance_valid(item_effect): return


	var first_note = note_1 if note_1 != "" else note_2
	var second_note = note_2 if note_1 != "" else ""


	item_effect.set_meta("next_effect", second_note)
	_start_single_item_effect(first_note)

func _start_single_item_effect(eff_name: String) -> void :
	if eff_name == "": return
	var loaded_res = EffectLoader.get_effect("Normal_Effect/" + eff_name)


	if not loaded_res:
		_on_item_effect_finished()
		return

	if item_effect.sprite_frames != loaded_res:
		item_effect.sprite_frames = loaded_res

	var eff_dir = current_dir
	var eff_flip = false
	if current_dir in ["5", "6", "7"]:
		eff_flip = true
		match current_dir:
			"5": eff_dir = "3"
			"6": eff_dir = "2"
			"7": eff_dir = "1"

	var anim_to_play = eff_dir if loaded_res.has_animation(eff_dir) else "default"
	if not loaded_res.has_animation(anim_to_play):
		var anims = loaded_res.get_animation_names()
		if anims.size() > 0: anim_to_play = anims[0]

	item_effect.flip_h = eff_flip
	item_effect.visible = true


	item_effect.animation = anim_to_play
	item_effect.frame = 0

	var p_spd = 1.0
	if "data_model" in self and self.data_model: p_spd = max(float(self.data_model.attack_speed), 10.0) / 100.0
	elif "attack_speed" in self: p_spd = max(float(self.attack_speed), 10.0) / 100.0

	item_effect.speed_scale = p_spd
	item_effect.play(anim_to_play)
	_on_effect_frame_changed(item_effect)

func _on_item_effect_finished() -> void :
	if is_instance_valid(item_effect):
		var next_eff = item_effect.get_meta("next_effect", "")
		if next_eff != "":
			item_effect.set_meta("next_effect", "")
			_start_single_item_effect(next_eff)
		else:
			item_effect.visible = false
			item_effect.stop()

func _on_ctrl_state_changed(_is_pressed: bool) -> void :
	_refresh_identity_labels()

func _refresh_identity_labels() -> void :
	if not data_model:
		return

	var ctrl_pressed: bool = Input.is_key_pressed(KEY_CTRL)
	var title_changed: bool = not _label_display_cache_initialized or _cached_title_id != data_model.title
	var club_changed: bool = (
		not _label_display_cache_initialized
		or _cached_club_id != data_model.club_id
		or _cached_club_position != data_model.club_position
		or _cached_club_name != data_model.club_name
	)

	if title_changed:
		_cached_title_id = data_model.title
		_cached_title_text = ""
		if data_model.title > 0:
			var title_name: String = PlayerData.TITLE_MAP.get(data_model.title, "未知稱號")
			_cached_title_text = "< " + title_name + " >"

	if club_changed:
		_cached_club_id = data_model.club_id
		_cached_club_position = data_model.club_position
		_cached_club_name = data_model.club_name
		_cached_club_text = ""
		if data_model.club_id > 0 and data_model.club_name != "":
			var position_name: String = "團員"
			match data_model.club_position:
				3: position_name = "團長"
				2: position_name = "驍將騎士"
				1: position_name = "騎士"
			_cached_club_text = "[%s] %s" % [position_name, data_model.club_name]

	var has_title: bool = data_model.title > 0
	var is_title_shown: bool = has_title and ctrl_pressed and is_instance_valid(title_label)
	if is_instance_valid(title_label):
		if has_title and title_label.text != _cached_title_text:
			title_label.text = _cached_title_text
		if title_label.visible != is_title_shown:
			title_label.visible = is_title_shown

	var has_club: bool = data_model.club_id > 0 and data_model.club_name != ""
	if is_instance_valid(club_label):
		if has_club and club_label.text != _cached_club_text:
			club_label.text = _cached_club_text
		var should_show_club: bool = has_club and ctrl_pressed
		if club_label.visible != should_show_club:
			club_label.visible = should_show_club
		if has_club and is_instance_valid(name_label):
			var desired_club_y: float = name_label.position.y - (40.0 if is_title_shown else 20.0)
			if club_label.position.y != desired_club_y:
				club_label.position.y = desired_club_y

	_label_display_cache_initialized = true

func _lock_ui_position_for_transform() -> void :
	if data_model.transform_type > 0 and transform_sprite and transform_sprite.sprite_frames:
		await get_tree().process_frame
		if not is_instance_valid(self): return

		var fixed_head_y = -80.0
		var base_anim = "IDLE_1"

		if data_model.transform_type == 2:
			base_anim = "default"

		if not transform_sprite.sprite_frames.has_animation(base_anim):
			var anims = transform_sprite.sprite_frames.get_animation_names()
			if anims.size() > 0: base_anim = anims[0]

		if transform_sprite.sprite_frames.has_animation(base_anim):
			var tex = transform_sprite.sprite_frames.get_frame_texture(base_anim, 0)
			if tex:
				var offset_val = tex.get_meta("old_offset") if tex.has_meta("old_offset") else Vector2.ZERO

				if data_model.transform_type == 1:
					fixed_head_y = - offset_val.y - 55
				elif data_model.transform_type == 2:

					if offset_val != Vector2.ZERO:
						fixed_head_y = - offset_val.y - 15.0
					else:
						fixed_head_y = - tex.get_height() - 15.0

		if name_label: name_label.position.y = fixed_head_y
		if title_label: title_label.position.y = fixed_head_y - 20.0
		if club_label: club_label.position.y = fixed_head_y - 40.0

func _sync_monster_transform_animation(anim_dir: String, _flip: bool) -> void :
	if not is_instance_valid(transform_sprite) or not transform_sprite.sprite_frames: return


	var state_map = {
		"idle": "IDLE", "idle_weapon_melee": "IDLE", "idle_weapon_gun": "IDLE", "guard": "IDLE", "crouch": "IDLE", 
		"walk": "WALK", 
		"attack_aa": "ATTACK", "attack_ab": "ATTACK", "attack_ga": "ATTACK", "attack_gb": "ATTACK", "attack_na": "ATTACK", "attack_nb": "ATTACK", "buff": "ATTACK", 
		"hit_weapon_melee": "HIT", "hit_weapon_gun": "HIT", "hit": "HIT", 
		"death": "DEATH"
	}

	var mapped_prefix = "IDLE"
	if current_state in state_map: mapped_prefix = state_map[current_state]
	elif current_state.begins_with("emote_"): mapped_prefix = "IDLE"

	var dir_int = int(current_dir)
	var anim_suffix = str(dir_int)
	var should_flip = false


	if mapped_prefix == "IDLE":
		match dir_int:
			8, 1: anim_suffix = "1";should_flip = false
			2, 3: anim_suffix = "3";should_flip = false
			4, 5: anim_suffix = "3";should_flip = true
			6, 7: anim_suffix = "1";should_flip = true
	else:
		match dir_int:
			1, 2, 3, 4, 8: anim_suffix = str(dir_int);should_flip = false
			5: anim_suffix = "3";should_flip = true
			6: anim_suffix = "2";should_flip = true
			7: anim_suffix = "1";should_flip = true

	transform_sprite.flip_h = should_flip
	var target_anim = mapped_prefix + "_" + anim_suffix
	var fallback_anim = mapped_prefix + "_1"
	var idle_fallback = "IDLE_1"
	var anim_to_play = ""

	if transform_sprite.sprite_frames.has_animation(target_anim): anim_to_play = target_anim
	elif transform_sprite.sprite_frames.has_animation(fallback_anim): anim_to_play = fallback_anim
	elif transform_sprite.sprite_frames.has_animation(idle_fallback): anim_to_play = idle_fallback
	else:
		var all_anims = transform_sprite.sprite_frames.get_animation_names()
		if all_anims.size() > 0: anim_to_play = all_anims[0]

	if anim_to_play != "":
		if transform_sprite.animation != anim_to_play or not transform_sprite.is_playing():
			transform_sprite.animation = anim_to_play
			transform_sprite.frame = 0
			if mapped_prefix == "ATTACK":
				transform_sprite.speed_scale = max(float(data_model.attack_speed), 10.0) / 100.0
			else:
				transform_sprite.speed_scale = 1.0
			transform_sprite.play(anim_to_play)
			_on_transform_frame_changed()




func can_attack() -> bool:

	if is_riding_mecha() and data_model.mecha_type == "MA":
		var body_id = _get_current_mecha_body_id()
		if body_id == 50265 or (body_id >= 50303 and body_id <= 50310):
			return false

	return true


func _get_current_mecha_body_id() -> int:
	var body_id = 0
	var mecha_horse_idx = 0


	if "horse_list" in data_model and data_model.horse_list.size() > 0:
		for h in data_model.horse_list:
			if h.get("horse_uid", -1) == data_model.summoned_horse_uid:
				mecha_horse_idx = h.get("horse_idx", 0)
				break


	for heq in data_model.horse_equip_list:
		if heq.get("horse_uid", -1) == data_model.summoned_horse_uid and heq.get("in_bank", 0) == 0:
			if heq.get("part", 0) == 0:
				body_id = heq.get("equip_idx", 0)
				break


	if data_model.mecha_type == "MA" and body_id == 0:
		body_id = mecha_horse_idx
		if ItemLoader:
			var ma_body_id = ItemLoader.get_item_property(mecha_horse_idx, "body_id", 0)
			if ma_body_id > 0:
				body_id = ma_body_id

	return body_id
