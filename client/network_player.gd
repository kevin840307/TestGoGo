
extends BaseEntity

var interact_area: Area2D = null


var target_position: Vector2 = Vector2.ZERO
var combat_target: Node2D = null
const DEFAULT_SNAPSHOT_INTERVAL: float = 0.1
const MIN_SNAPSHOT_INTERVAL: float = 0.05
const MAX_SNAPSHOT_INTERVAL: float = 0.25
const MAX_CATCHUP_MULTIPLIER: float = 1.3
const MIN_TELEPORT_DISTANCE: float = 1200.0
const TELEPORT_SPEED_WINDOW: float = 1.0
var _snapshot_start_position: Vector2 = Vector2.ZERO
var _snapshot_interval: float = DEFAULT_SNAPSHOT_INTERVAL
var _snapshot_duration: float = DEFAULT_SNAPSHOT_INTERVAL
var _snapshot_elapsed: float = 0.0
var _last_snapshot_received_msec: int = -1
var _last_snapshot_target_position: Vector2 = Vector2.ZERO
var _has_snapshot_target: bool = false
var _observed_snapshot_speed: float = 0.0
var _pending_idle: bool = false
var _performance_simplified_visual: bool = false
var _performance_simplified_style: int = ClientPerformanceProfile.SIMPLIFY_PIXEL_A
const SIMPLIFIED_PLAYER_CLICK_SIZE := Vector2(104.0, 136.0)
const SIMPLIFIED_ORB_CACHE = preload("res://scripts/simplified_orb_cache.gd")
const NORMAL_PLAYER_CLICK_SIZE := Vector2(80.0, 120.0)

const SAFE_MAP_IDS: Array = [4, 9, 15, 17, 22, 39, 40, 53, 75, 93]

var _physics_animation_cache_initialized: = false
var _last_physics_animation_state: = ""
var _last_physics_animation_dir: = ""
var _last_physics_animation_position_parity: = -1
var _last_physics_animation_dead: = false
var _last_physics_animation_move_speed: = 0
var _last_physics_animation_attack_speed: = 0
var _last_physics_animation_horse_uid: = -1
var _last_physics_animation_mecha_type: = ""
var _last_physics_animation_transform_type: = 0
var _last_physics_animation_transform_id: = ""
var _last_physics_animation_transform_color: = 0



func _ready() -> void :
	super._ready()

	target_position = global_position
	_snapshot_start_position = global_position
	velocity = Vector2.ZERO
	set_physics_process(false)

	_setup_interact_area()
	input_pickable = true

	mouse_entered.connect(_on_mouse_entered)
	mouse_exited.connect(_on_mouse_exited)
	input_event.connect(_on_input_event)

	if is_instance_valid(doll_engine) and doll_engine.get("doll_sprites") != null and doll_engine.doll_sprites.is_empty():
		setup_layered_paper_dolls()
		sync_animation()

func _normalize_simplified_style(style: int) -> int:
	var normalized := ClientPerformanceProfile.sanitize_simplify_mode(style)
	if normalized < ClientPerformanceProfile.SIMPLIFY_PIXEL_A:
		return ClientPerformanceProfile.SIMPLIFY_PIXEL_A
	return normalized

func prepare_performance_visual(
	simplified: bool,
	style: int = ClientPerformanceProfile.SIMPLIFY_PIXEL_A
) -> void:
	# Called before add_child() so simplified network players never need to
	# build the full layered paper doll during spawn.
	_performance_simplified_visual = simplified
	_performance_simplified_style = _normalize_simplified_style(style)

func set_performance_simplified(
	enabled: bool,
	style: int = ClientPerformanceProfile.SIMPLIFY_PIXEL_A
) -> void:
	var normalized_style := _normalize_simplified_style(style)
	if (
		_performance_simplified_visual == enabled
		and _performance_simplified_style == normalized_style
	):
		return
	_performance_simplified_visual = enabled
	_performance_simplified_style = normalized_style
	setup_layered_paper_dolls()
	_apply_interact_area_size()
	sync_animation()
	queue_redraw()

func is_performance_simplified() -> bool:
	return _performance_simplified_visual

func _simplified_relation_color() -> Color:
	var local_player = GameManager.current_player if GameManager else null
	if is_instance_valid(local_player) and "data_model" in local_player:
		var local_data = local_player.data_model
		var my_party := str(local_data.party_id)
		if my_party not in ["", "0", "-1"] and my_party == str(data_model.party_id):
			return Color(0.28, 1.0, 0.56, 1.0)

		var my_club := int(local_data.club_id)
		if my_club > 0 and my_club == int(data_model.club_id):
			return Color(0.32, 0.70, 1.0, 1.0)

		var in_safe_zone := int(local_data.map_id) in SAFE_MAP_IDS
		var attackable := (
			int(local_data.pk_status) == 1
			and int(data_model.pk_status) == 1
			and int(data_model.hp) > 0
			and not in_safe_zone
		)
		if attackable and local_player.has_method("can_attack"):
			attackable = bool(local_player.can_attack())
		if attackable:
			return Color(1.0, 0.28, 0.30, 1.0)

	return Color(0.82, 0.88, 0.98, 1.0)

func _simplified_direction_vector() -> Vector2:
	match current_dir:
		"1": return Vector2(0.0, 1.0)
		"2": return Vector2(0.707, 0.707)
		"3": return Vector2(1.0, 0.0)
		"4": return Vector2(0.707, -0.707)
		"5": return Vector2(0.0, -1.0)
		"6": return Vector2(-0.707, -0.707)
		"7": return Vector2(-1.0, 0.0)
		"8": return Vector2(-0.707, 0.707)
	return Vector2.DOWN


func _draw_simplified_player_orb(
	center: Vector2,
	relation: Color,
	attacking: bool,
	hit: bool
) -> void:
	# One shared, slightly taller star-orb texture for ALL remote players.
	# Party/club/PvP changes only modulate its tint; no separate GPU textures.
	var tint: Color = relation
	if hit:
		tint = Color(1.0, 0.53, 0.53, 1.0)
	elif attacking:
		tint = relation.lightened(0.15)
	draw_texture(
		SIMPLIFIED_ORB_CACHE.player_texture(),
		center - SIMPLIFIED_ORB_CACHE.HALF_SIZE,
		tint
	)

func _draw() -> void:
	if not _performance_simplified_visual:
		return

	var state := current_state.to_lower()
	var relation := _simplified_relation_color()
	var center := Vector2(0.0, -50.0)
	var dead := is_dead or state == "death"
	var attacking := state.begins_with("attack") and not dead
	if attacking:
		center += _simplified_direction_vector() * 5.0

	if dead:
		# Death is a squashed version of the SAME procedural orb. Never
		# fall back to the layered character sprites.
		draw_set_transform(center + Vector2(0.0, 24.0), 0.0, Vector2(1.30, 0.34))
		_draw_simplified_player_orb(Vector2.ZERO, relation.darkened(0.25), false, false)
		draw_set_transform(Vector2.ZERO, 0.0, Vector2.ONE)
		return

	_draw_simplified_player_orb(
		center,
		relation,
		attacking,
		state.begins_with("hit")
	)


func setup_layered_paper_dolls() -> void:
	if not _performance_simplified_visual:
		super.setup_layered_paper_dolls()
		return

	if is_instance_valid(transform_sprite):
		transform_sprite.visible = false
	if is_instance_valid(doll_engine):
		# A player that was already rendered in full detail may still own many
		# AnimatedSprite2D layers. Release those nodes when switching to the
		# procedural mode; switching simplification off rebuilds them normally.
		if doll_engine.has_method("clear_dolls"):
			doll_engine.clear_dolls()
		doll_engine.visible = false

	if name_label:
		name_label.position.y = -112.0
		name_label.visible = not data_model.is_shop_open
	if title_label:
		title_label.position.y = -132.0
	if club_label:
		club_label.position.y = -152.0
	queue_redraw()

func sync_animation() -> void:
	if _performance_simplified_visual:
		# State/direction still change authoritatively; only the expensive
		# layered SpriteFrames animation is replaced by one redraw.
		if is_instance_valid(doll_engine):
			doll_engine.visible = false
		if is_instance_valid(transform_sprite):
			transform_sprite.visible = false
		if is_instance_valid(death_effect):
			death_effect.visible = false
			death_effect.stop()
		queue_redraw()
		return
	super.sync_animation()


func _play_death_effect() -> void:
	if _performance_simplified_visual:
		if is_instance_valid(doll_engine):
			doll_engine.visible = false
		if is_instance_valid(transform_sprite):
			transform_sprite.visible = false
		if is_instance_valid(death_effect):
			death_effect.visible = false
			death_effect.stop()
		queue_redraw()
		return
	super._play_death_effect()


func play_mecha_transform() -> void:
	if _performance_simplified_visual:
		# A simplified remote player remains the same procedural silhouette
		# through mount/dismount instead of loading a full mecha transition.
		queue_redraw()
		return
	super.play_mecha_transform()


func _on_party_sync_received(p_id: String, leader_id: int, members: Array) -> void:
	super._on_party_sync_received(p_id, leader_id, members)
	if _performance_simplified_visual:
		queue_redraw()


func _on_entity_name_color_updated(
	p_entity_id: int,
	color_state: int,
	pk_status: int = -1,
	red_man_kill_left: int = -1,
	party_id: String = ""
) -> void:
	super._on_entity_name_color_updated(
		p_entity_id,
		color_state,
		pk_status,
		red_man_kill_left,
		party_id
	)
	# Even when this packet belongs to the local player, its PK/party state
	# can change how every simplified remote player should be outlined.
	if _performance_simplified_visual:
		queue_redraw()


func _exit_tree() -> void :
	if CursorManager:
		CursorManager.unregister_hover(self)

func is_entity_moving() -> bool:
	return current_state == "walk"

func initialize_network_player(data: Dictionary) -> void :
	var spawn_pos = Vector2(float(data.get("LocX", 0)), float(data.get("LocY", 0)))
	global_position = spawn_pos
	target_position = spawn_pos
	velocity = Vector2.ZERO

	var extra = data.get("ExtraData", {})
	var c_name = data.get("Name", "未知玩家")

	current_state = extra.get("State", "idle")
	current_dir = extra.get("Dir", "1")
	data_model.party_id = str(extra.get("PartyID", ""))
	data_model.mecha_type = str(extra.get("MechaType", "MS"))
	data_model.transform_type = int(extra.get("TransformType", 0))
	data_model.transform_id = str(extra.get("TransformID", ""))
	data_model.transform_color_index = int(extra.get("TransformColorIndex", 0))
	data_model.club_id = int(extra.get("ClubID", 0))
	data_model.club_position = int(extra.get("ClubPosition", 0))
	data_model.club_name = str(extra.get("ClubName", ""))
	data_model.is_shop_open = bool(extra.get("IsShopOpen", extra.get("is_shop_open", false)))
	data_model.shop_name = str(extra.get("ShopName", extra.get("shop_name", "")))

	if current_state.begins_with("emote_"):
		is_emoting = true

	data_model.character_name = c_name
	data_model.db_id = int(data.get("EntityID", 0))
	data_model.race_id = int(extra.get("Race", 0))
	data_model.title = int(extra.get("Title", 0))
	data_model.red_man = int(extra.get("RedMan", 0))
	data_model.red_man_kill_left = int(extra.get("RedManKillLeft", 0))
	data_model.pk_status = int(extra.get("PKStatus", 0))
	data_model.hp = int(extra.get("HP", 100))
	data_model.max_hp = int(extra.get("MaxHP", 100))
	data_model.summoned_horse_uid = int(extra.get("SummonedHorseUID", -1))
	data_model.move_speed = int(extra.get("MoveSpeed", 100))
	data_model.attack_speed = int(extra.get("AttackSpeed", 100))

	if extra.has("MechaBody") or extra.has("MechaWeapon") or extra.has("MechaEngine"):
		data_model.horse_equip_list.clear()
		if int(extra.get("MechaBody", 0)) > 0:
			data_model.horse_equip_list.append({"horse_uid": data_model.summoned_horse_uid, "in_bank": 0, "part": 0, "equip_idx": int(extra.get("MechaBody", 0))})
		if int(extra.get("MechaWeapon", 0)) > 0:
			data_model.horse_equip_list.append({"horse_uid": data_model.summoned_horse_uid, "in_bank": 0, "part": 1, "equip_idx": int(extra.get("MechaWeapon", 0))})
		if int(extra.get("MechaEngine", 0)) > 0:
			data_model.horse_equip_list.append({"horse_uid": data_model.summoned_horse_uid, "in_bank": 0, "part": 2, "equip_idx": int(extra.get("MechaEngine", 0))})

	current_race = GameConstants.RACE_ID_MAP.get(data_model.race_id, "Boy")

	if name_label:
		var race_map = {"Boy": "[騎]", "Girl": "[騎]", "Migu": "[魔]", "Robot": "[機]", "Yas": "[遺]"}
		if not name_label.text or name_label.text == "":
			name_label.text = race_map.get(current_race, "[?]") + data_model.character_name
		_apply_name_color(1 if data_model.red_man > 0 else 0)
		name_label.visible = not (data_model.is_shop_open or data_model.transform_type == 2)

	data_model.upper_armor_id = int(extra.get("Upper_armor", 0))
	data_model.lower_armor_id = int(extra.get("Lower_armor", 0))
	data_model.weapon_id = int(extra.get("Weapon", 0))
	data_model.shield_id = int(extra.get("Shield", 0))
	data_model.bonnet_id = int(extra.get("Bonnet", 0))
	data_model.left_hand_id = int(extra.get("Left_hand", 0))
	data_model.right_hand_id = int(extra.get("Right_hand", 0))
	data_model.back_id = int(extra.get("Back", 0))

	var hair_id = int(extra.get("Hair", 0))
	current_hair = "Hair%03d" % hair_id if hair_id >= 0 else "Hair000"

	current_hair_color_index = int(extra.get("HairColor", 0))
	current_body_color_index = int(extra.get("EquipColor0", 0))

	if data_model.hp <= 0 or current_state == "death":
		current_state = "death"
		is_dead = true
		is_action_locked = true
		adjust_interact_area_for_death(true)

	if is_inside_tree():
		setup_layered_paper_dolls()
		sync_animation()

		set_physics_process(true)

func update_appearance_from_server(data: Dictionary) -> void :
	var old_race = data_model.race_id
	var old_hair = current_hair
	var old_horse_uid = data_model.summoned_horse_uid
	var old_mecha_type = data_model.mecha_type
	var old_transform_type = data_model.transform_type
	var old_transform_id = data_model.transform_id
	var old_transform_color_index = data_model.transform_color_index

	data_model.race_id = int(data.get("Race", data_model.race_id))
	data_model.title = int(data.get("Title", data_model.title))
	data_model.red_man = int(data.get("RedMan", data_model.red_man))
	data_model.pk_status = int(data.get("PKStatus", data_model.pk_status))

	if data.has("SummonedHorseUID"):
		data_model.summoned_horse_uid = int(data.get("SummonedHorseUID", -1))
	if data.has("MechaType"):
		data_model.mecha_type = str(data.get("MechaType", "MS"))

	data_model.party_id = str(data.get("PartyID", data_model.party_id))
	data_model.club_id = int(data.get("ClubID", data_model.club_id))
	data_model.club_position = int(data.get("ClubPosition", data_model.club_position))
	data_model.club_name = str(data.get("ClubName", data_model.club_name))
	data_model.transform_type = int(data.get("TransformType", data_model.transform_type))
	data_model.transform_id = str(data.get("TransformID", data_model.transform_id))
	data_model.transform_color_index = int(data.get("TransformColorIndex", data_model.transform_color_index))
	if _performance_simplified_visual:
		queue_redraw()

	var mecha_parts_changed = false
	if data.has("MechaBody") or data.has("MechaWeapon") or data.has("MechaEngine") or old_mecha_type != data_model.mecha_type:
		mecha_parts_changed = true
		data_model.horse_equip_list.clear()
		if int(data.get("MechaBody", 0)) > 0:
			data_model.horse_equip_list.append({"horse_uid": data_model.summoned_horse_uid, "in_bank": 0, "part": 0, "equip_idx": int(data.get("MechaBody", 0))})
		if int(data.get("MechaWeapon", 0)) > 0:
			data_model.horse_equip_list.append({"horse_uid": data_model.summoned_horse_uid, "in_bank": 0, "part": 1, "equip_idx": int(data.get("MechaWeapon", 0))})
		if int(data.get("MechaEngine", 0)) > 0:
			data_model.horse_equip_list.append({"horse_uid": data_model.summoned_horse_uid, "in_bank": 0, "part": 2, "equip_idx": int(data.get("MechaEngine", 0))})

	if name_label:
		var visible_color_state = 2 if _name_color_state == 3 else _name_color_state
		_apply_name_color_style(visible_color_state)

	current_race = GameConstants.RACE_ID_MAP.get(data_model.race_id, "Boy")

	var current_hair_id = int(current_hair.replace("Hair", "")) if current_hair.begins_with("Hair") else 0
	var hair_id = int(data.get("Hair", current_hair_id))
	current_hair = "Hair%03d" % hair_id if hair_id >= 0 else "Hair000"

	if old_race != data_model.race_id or old_hair != current_hair or old_horse_uid != data_model.summoned_horse_uid or mecha_parts_changed or old_transform_type != data_model.transform_type or old_transform_id != data_model.transform_id or old_transform_color_index != data_model.transform_color_index:
		if old_horse_uid != data_model.summoned_horse_uid:
			execute_mecha_transition( func():
				data_model.bonnet_id = int(data.get("Bonnet", data_model.bonnet_id))
				data_model.upper_armor_id = int(data.get("Upper_armor", data_model.upper_armor_id))
				data_model.lower_armor_id = int(data.get("Lower_armor", data_model.lower_armor_id))
				data_model.weapon_id = int(data.get("Weapon", data_model.weapon_id))
				data_model.shield_id = int(data.get("Shield", data_model.shield_id))
				data_model.left_hand_id = int(data.get("Left_hand", data_model.left_hand_id))
				data_model.right_hand_id = int(data.get("Right_hand", data_model.right_hand_id))
				data_model.back_id = int(data.get("Back", data_model.back_id))
				setup_layered_paper_dolls()
				sync_animation()
			)
		else:

			data_model.bonnet_id = int(data.get("Bonnet", data_model.bonnet_id))
			data_model.upper_armor_id = int(data.get("Upper_armor", data_model.upper_armor_id))
			data_model.lower_armor_id = int(data.get("Lower_armor", data_model.lower_armor_id))
			data_model.weapon_id = int(data.get("Weapon", data_model.weapon_id))
			data_model.shield_id = int(data.get("Shield", data_model.shield_id))
			data_model.left_hand_id = int(data.get("Left_hand", data_model.left_hand_id))
			data_model.right_hand_id = int(data.get("Right_hand", data_model.right_hand_id))
			data_model.back_id = int(data.get("Back", data_model.back_id))
			setup_layered_paper_dolls()
			sync_animation()
	else:
		_check_and_update_part(0, data_model.bonnet_id, int(data.get("Bonnet", data_model.bonnet_id)))
		_check_and_update_part(1, data_model.left_hand_id, int(data.get("Left_hand", data_model.left_hand_id)))
		_check_and_update_part(2, data_model.right_hand_id, int(data.get("Right_hand", data_model.right_hand_id)))
		_check_and_update_part(3, data_model.upper_armor_id, int(data.get("Upper_armor", data_model.upper_armor_id)))
		_check_and_update_part(4, data_model.lower_armor_id, int(data.get("Lower_armor", data_model.lower_armor_id)))
		_check_and_update_part(5, data_model.weapon_id, int(data.get("Weapon", data_model.weapon_id)))
		_check_and_update_part(6, data_model.shield_id, int(data.get("Shield", data_model.shield_id)))
		_check_and_update_part(7, data_model.back_id, int(data.get("Back", data_model.back_id)))
		sync_animation()



func network_move_to(new_pos: Vector2, _speed_time: float = 1.0) -> void :
	if is_dead: return
	_pending_idle = false
	var received_msec: = Time.get_ticks_msec()
	var observed_interval: = _snapshot_interval
	if _last_snapshot_received_msec >= 0:
		observed_interval = clampf(
			float(received_msec - _last_snapshot_received_msec) / 1000.0, 
			MIN_SNAPSHOT_INTERVAL, 
			MAX_SNAPSHOT_INTERVAL
		)
		_snapshot_interval = lerpf(_snapshot_interval, observed_interval, 0.25)
	_last_snapshot_received_msec = received_msec
	if _has_snapshot_target and observed_interval > 0.0:
		var target_delta: = _last_snapshot_target_position.distance_to(new_pos)
		if target_delta <= MIN_TELEPORT_DISTANCE:
			var sampled_speed: = target_delta / observed_interval
			if _observed_snapshot_speed <= 0.0:
				_observed_snapshot_speed = sampled_speed
			else:
				_observed_snapshot_speed = lerpf(_observed_snapshot_speed, sampled_speed, 0.5)
	elif _snapshot_interval > 0.0:
		var initial_target_delta: = global_position.distance_to(new_pos)
		if initial_target_delta <= MIN_TELEPORT_DISTANCE:
			_observed_snapshot_speed = initial_target_delta / _snapshot_interval
	_last_snapshot_target_position = new_pos
	_has_snapshot_target = true

	_snapshot_start_position = global_position
	target_position = new_pos
	_snapshot_elapsed = 0.0
	var snapshot_distance: = _snapshot_start_position.distance_to(target_position)
	var movement_speed_basis: = maxf(get_real_move_speed(), _observed_snapshot_speed)
	var max_catchup_speed: = movement_speed_basis * MAX_CATCHUP_MULTIPLIER
	_snapshot_duration = _snapshot_interval
	if max_catchup_speed > 0.0:
		_snapshot_duration = maxf(_snapshot_duration, snapshot_distance / max_catchup_speed)
	set_physics_process(true)
	var teleport_distance: = maxf(MIN_TELEPORT_DISTANCE, movement_speed_basis * TELEPORT_SPEED_WINDOW)
	if global_position.distance_to(target_position) > teleport_distance:
		global_position = target_position
		_snapshot_start_position = target_position
		_snapshot_elapsed = _snapshot_duration

	if is_emoting or current_state.begins_with("emote"):
		is_emoting = false
		current_state = "walk"

func refresh_network_physics() -> void :

	set_physics_process(true)

func apply_network_state(new_state: String, new_dir: String) -> void :
	if new_dir != "":
		current_dir = new_dir
	if new_state == "idle" and global_position.distance_squared_to(target_position) > 4.0:
		_pending_idle = true
		return
	_pending_idle = false
	current_state = new_state
	if _performance_simplified_visual:
		queue_redraw()
	if new_state.begins_with("emote_"):
		is_emoting = true
		if is_instance_valid(doll_engine):
			for sprite_node in doll_engine.doll_sprites.values():
				if is_instance_valid(sprite_node):
					sprite_node.frame = 0

func _physics_process(delta: float) -> void :
	if is_dead:
		_sync_physics_animation_if_changed()
		set_physics_process(false)
		return
	_handle_movement(delta)
	_sync_physics_animation_if_changed()
	if global_position.distance_squared_to(target_position) <= 4.0 and current_state != "walk":
		set_physics_process(false)

func _sync_physics_animation_if_changed() -> void :
	var position_parity: = -1
	if current_state.begins_with("attack_") or current_state.begins_with("hit_") or current_state == "guard" or current_state.begins_with("idle_weapon_"):
		if current_dir == "4":
			position_parity = int(global_position.x) & 1
		elif current_dir == "8":
			position_parity = int(global_position.y) & 1

	if _physics_animation_cache_initialized\
	and current_state == _last_physics_animation_state\
	and current_dir == _last_physics_animation_dir\
	and position_parity == _last_physics_animation_position_parity\
	and is_dead == _last_physics_animation_dead\
	and data_model.move_speed == _last_physics_animation_move_speed\
	and data_model.attack_speed == _last_physics_animation_attack_speed\
	and data_model.summoned_horse_uid == _last_physics_animation_horse_uid\
	and data_model.mecha_type == _last_physics_animation_mecha_type\
	and data_model.transform_type == _last_physics_animation_transform_type\
	and data_model.transform_id == _last_physics_animation_transform_id\
	and data_model.transform_color_index == _last_physics_animation_transform_color:
		return

	_physics_animation_cache_initialized = true
	_last_physics_animation_state = current_state
	_last_physics_animation_dir = current_dir
	_last_physics_animation_position_parity = position_parity
	_last_physics_animation_dead = is_dead
	_last_physics_animation_move_speed = data_model.move_speed
	_last_physics_animation_attack_speed = data_model.attack_speed
	_last_physics_animation_horse_uid = data_model.summoned_horse_uid
	_last_physics_animation_mecha_type = data_model.mecha_type
	_last_physics_animation_transform_type = data_model.transform_type
	_last_physics_animation_transform_id = data_model.transform_id
	_last_physics_animation_transform_color = data_model.transform_color_index
	sync_animation()

func get_real_move_speed() -> float:
	var base_speed = 200.0
	if data_model:
		return base_speed * (max(float(data_model.move_speed), 10.0) / 100.0)
	return base_speed

func _handle_movement(delta: float) -> void :
	var distance = global_position.distance_to(target_position)

	if distance > 2.0:
		var dir_vector = (target_position - global_position).normalized()
		_snapshot_elapsed = minf(_snapshot_elapsed + delta, _snapshot_duration)
		var progress: = 1.0
		if _snapshot_duration > 0.0:
			progress = _snapshot_elapsed / _snapshot_duration
		global_position = _snapshot_start_position.lerp(target_position, progress)

		if not current_state.begins_with("attack"):
			is_emoting = false
			current_state = "walk"
		update_direction_8way(dir_vector)
	else:
		global_position = target_position
		if _pending_idle:
			_pending_idle = false
			current_state = "idle"

func update_direction_8way(move_velocity: Vector2) -> void :
	if move_velocity == Vector2.ZERO: return
	var angle_deg: float = posmod(rad_to_deg(move_velocity.angle()), 360.0)
	var index_raw: int = int(round(angle_deg / 45.0))
	current_dir = GameConstants.DIR_STRINGS[index_raw % 8]


func _on_mouse_entered() -> void :
	if CursorManager:
		var local_player = GameManager.current_player if GameManager else null


		var state = "player"
		var in_safe_zone = data_model.map_id in SAFE_MAP_IDS


		if local_player and is_instance_valid(local_player) and local_player.data_model.pk_status == 1 and data_model.pk_status == 1 and data_model.hp > 0 and not in_safe_zone:
			if local_player.has_method("can_attack") and not local_player.can_attack():
				state = "player"
			else:
				state = "attack"


		CursorManager.register_hover(self, state)


		if CursorManager.cursor_sprite.animation == "skill":
			if is_instance_valid(local_player) and "ready_to_cast_skill_id" in local_player and local_player.ready_to_cast_skill_id > 0:
				if SkillLoader:
					var s_res = SkillLoader.get_skill_data(local_player.ready_to_cast_skill_id)
					if s_res:
						var t_name = data_model.character_name if data_model else "玩家"
						CursorManager.show_skill_target_tooltip(t_name, s_res.skill_name)

func _on_mouse_exited() -> void :
	if CursorManager:
		CursorManager.unregister_hover(self)
		if CursorManager.cursor_sprite.animation == "skill":
			CursorManager.hide_skill_target_tooltip()

func _on_input_event(_viewport: Node, event: InputEvent, _shape_idx: int) -> void :
	if event is InputEventMouseButton and event.pressed:

		if CursorManager and CursorManager.current_hovered_entity != self:
			return

		if event.button_index == MOUSE_BUTTON_RIGHT:
			var local_player = GameManager.current_player if GameManager else null

			if local_player and "ready_to_cast_skill_id" in local_player and local_player.ready_to_cast_skill_id > 0:
				local_player.ready_to_cast_skill_id = -1
				if CursorManager:
					if CursorManager.has_method("cancel_skill_state"):
						CursorManager.cancel_skill_state()
					else:
						CursorManager.set_cursor_state("normal")
				get_viewport().set_input_as_handled()
				return

			if CursorManager:
				CursorManager.set_cursor_state("normal")
			if GameManager.current_hud and GameManager.current_hud.has_method("show_player_context_menu"):
				GameManager.current_hud.show_player_context_menu(data_model.db_id, data_model.character_name)
			get_viewport().set_input_as_handled()
		elif event.button_index == MOUSE_BUTTON_LEFT:
			# Targeting does not reload the full paper doll. Hostility/relationship
			# stays visible through the built-in outline.
			var local_player = GameManager.current_player if GameManager else null
			if local_player and is_instance_valid(local_player):
				var in_safe_zone = data_model.map_id in SAFE_MAP_IDS

				if "ready_to_cast_skill_id" in local_player and local_player.ready_to_cast_skill_id > 0:
					if local_player.has_method("cancel_autorun_tracking"):
						local_player.cancel_autorun_tracking(true)
					local_player.pending_skill_id = local_player.ready_to_cast_skill_id
					local_player.combat_target = self
					local_player.ready_to_cast_skill_id = -1

					if CursorManager:
						CursorManager.cancel_skill_state()
					return


				if local_player.data_model.pk_status == 1 and data_model.pk_status == 1 and not in_safe_zone:
					if data_model.hp > 0:
						if local_player.has_method("can_attack") and not local_player.can_attack():
							return

						if local_player.has_method("cancel_autorun_tracking"):
							local_player.cancel_autorun_tracking(true)
						local_player.pending_skill_id = -1
						local_player.combat_target = self
						get_viewport().set_input_as_handled()

func _setup_interact_area() -> void :
	interact_area = Area2D.new()
	interact_area.name = "InteractArea"
	var collision = CollisionShape2D.new()
	var rect = RectangleShape2D.new()
	rect.size = (
		SIMPLIFIED_PLAYER_CLICK_SIZE
		if _performance_simplified_visual
		else NORMAL_PLAYER_CLICK_SIZE
	)
	collision.shape = rect
	collision.position = Vector2(0, -40)
	interact_area.add_child(collision)
	add_child(interact_area)

	interact_area.input_pickable = true
	interact_area.mouse_entered.connect(_on_mouse_entered)
	interact_area.mouse_exited.connect(_on_mouse_exited)
	interact_area.input_event.connect(_on_input_event)

	self.input_pickable = false


func _apply_interact_area_size() -> void:
	if not is_instance_valid(interact_area) or interact_area.get_child_count() == 0:
		return
	var collision := interact_area.get_child(0) as CollisionShape2D
	if not is_instance_valid(collision):
		return
	var rect := collision.shape as RectangleShape2D
	if not is_instance_valid(rect):
		return
	rect.size = (
		SIMPLIFIED_PLAYER_CLICK_SIZE
		if _performance_simplified_visual
		else NORMAL_PLAYER_CLICK_SIZE
	)


func adjust_interact_area_for_death(is_dead_state: bool) -> void :
	if interact_area:
		var collision = interact_area.get_child(0) as CollisionShape2D
		if collision:
			if is_dead_state:
				collision.rotation_degrees = 90
				collision.position = Vector2(0, -10)
			else:
				collision.rotation_degrees = 0
				collision.position = Vector2(0, -40)
