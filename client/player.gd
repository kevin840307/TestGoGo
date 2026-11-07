
extends BaseEntity

@export var move_speed: float = 200.0

var sync_timer: float = 0.0
const SYNC_RATE: float = 0.05
var _movement_state_reported: bool = false
var _initial_visual_ready: bool = false
const DEFAULT_COLLISION_MASK: int = 21
const SPECIAL_MECHA_COLLISION_MASK: int = 5


var target_position: Vector2 = Vector2.ZERO
var move_destination: Vector2 = Vector2.ZERO
var move_path: PackedVector2Array = PackedVector2Array()
var move_path_index: int = 0
var is_moving: bool = false
var is_interacting_with_npc: bool = false
var is_mouse_holding: bool = false

var stuck_frames: int = 0
const MAX_STUCK_FRAMES: int = 30
var action_lock_timer: float = 0.0
var death_timer: float = 0.0
var _last_death_display_second: int = -1

var combat_target: Node2D = null
var attack_cooldown: float = 0.0
var attack_speed: float = 1.0
var ready_to_cast_skill_id: int = -1
var pending_skill_id: int = -1
var confirmed_summoned_horse_uid: int = -1
var can_cross_special_terrain: bool = false



func _ready() -> void :
	super._ready()

	# Building the layered paper doll can synchronously decode/create many
	# textures on first login. Defer it until the world has rendered at least
	# one frame so it no longer blocks the map-entry critical path.
	call_deferred("_initialize_player_paper_doll")

	if is_dead or data_model.hp <= 0:
		is_dead = false
		_die()
	else:
		sync_animation()


	if NetworkManager:
		NetworkManager.enter_world_success.connect(_on_enter_world_success)
		NetworkManager.inventory_synced.connect(_on_inventory_synced)
		NetworkManager.equip_synced.connect(_on_equip_synced)
		NetworkManager.pet_synced.connect(_on_pet_synced)
		NetworkManager.horse_synced.connect(_on_horse_synced)
		NetworkManager.drive_mecha_response_received.connect(_on_drive_mecha_response_received)
		NetworkManager.equip_response.connect(_on_equip_response)
		NetworkManager.change_map_received.connect(_on_change_map_received)
		NetworkManager.entity_appearance_updated.connect(_on_entity_appearance_updated)
		if NetworkManager.has_signal("allocate_stat_response_received"): NetworkManager.allocate_stat_response_received.connect(_on_allocate_stat_response)





	var click_area = Area2D.new()
	click_area.name = "ClickArea"
	click_area.collision_layer = 8
	click_area.collision_mask = 0
	var click_shape = CollisionShape2D.new()
	var rect = RectangleShape2D.new()
	rect.size = Vector2(60, 100)
	click_shape.shape = rect
	click_shape.position = Vector2(0, -40)
	click_area.add_child(click_shape)
	add_child(click_area)

	click_area.input_event.connect(_on_click_area_input_event)
	click_area.mouse_entered.connect(_on_mouse_entered)
	click_area.mouse_exited.connect(_on_mouse_exited)

func _initialize_player_paper_doll() -> void :
	if not is_inside_tree() or not is_instance_valid(doll_engine):
		_initial_visual_ready = true
		return
	if not doll_engine.doll_sprites.is_empty():
		_initial_visual_ready = true
		return
	await get_tree().process_frame
	if not is_inside_tree() or not is_instance_valid(doll_engine):
		_initial_visual_ready = true
		return
	if not doll_engine.doll_sprites.is_empty():
		_initial_visual_ready = true
		return
	if GameManager:
		GameManager.set_frame_gap_stage("player_paper_doll")
	var started_usec := Time.get_ticks_usec()
	setup_layered_paper_dolls()
	var elapsed_ms := float(Time.get_ticks_usec() - started_usec) / 1000.0
	_initial_visual_ready = true
	if elapsed_ms >= 50.0:
		print("[PLAYER_INIT_TIMING] paper_doll_ms=%.2f" % elapsed_ms)

func is_initial_visual_ready() -> bool:
	return _initial_visual_ready

func _exit_tree() -> void :
	if CursorManager:
		CursorManager.unregister_hover(self)



func is_entity_moving() -> bool:
	return is_moving

func update_hp(new_hp: int, hit_delay: float = 0.3, is_miss: bool = false) -> void :
	await super.update_hp(new_hp, hit_delay, is_miss)

	if not is_instance_valid(self) or not is_inside_tree(): return

	var hud = GameManager.current_hud
	if hud and hud.has_method("update_status_panel"):
		hud.update_status_panel()

func _die() -> void :
	if is_dead: return
	super._die()

	is_moving = false
	_movement_state_reported = false
	is_mouse_holding = false
	combat_target = null
	if CursorManager:
		CursorManager.unregister_hover(self)

	death_timer = 120.0
	if not is_instance_valid(death_label):
		death_label = Label.new()
		death_label.add_theme_color_override("font_color", GameConstants.COLOR_NAME_WHITE)
		death_label.add_theme_color_override("font_outline_color", Color(1.0, 0.081, 0.122, 1.0))
		death_label.add_theme_constant_override("outline_size", 3)
		death_label.add_theme_font_size_override("font_size", 21)
		death_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		death_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		ui_container.add_child(death_label)

	var label_width = 80.0
	death_label.size = Vector2(label_width, 20)
	var offset_y = -28.0
	if is_instance_valid(name_label):
		death_label.position.x = name_label.position.x + (name_label.size.x - label_width) / 2.0
		death_label.position.y = name_label.position.y + offset_y
	else:
		death_label.position = Vector2( - label_width / 2.0, offset_y)

	death_label.text = "120"
	_last_death_display_second = 120
	death_label.visible = true

func revive() -> void :
	super.revive()
	is_moving = false
	_movement_state_reported = false
	combat_target = null

func trigger_combat_action(new_action_state: String) -> void :
	super.trigger_combat_action(new_action_state)
	action_lock_timer = 0.0



func _on_inventory_synced(items: Array, max_size: int) -> void : data_model.update_inventory(items, max_size)
func _on_equip_synced(equips: Array, max_size: int) -> void : data_model.update_equips(equips, max_size)
func _on_pet_synced(pets: Array, max_size: int) -> void : data_model.update_pets(pets, max_size)
func _on_horse_synced(horses: Array, equips: Array, max_size: int) -> void :
	data_model.update_horses(horses, equips, max_size)
	_update_special_terrain_collision(confirmed_summoned_horse_uid)
	if data_model.summoned_horse_uid != -1:
		setup_layered_paper_dolls()
		sync_animation()

func _on_drive_mecha_response_received(success: bool, horse_uid: int, _message: String) -> void :
	if not success:
		var needs_appearance_rollback: = data_model.summoned_horse_uid != confirmed_summoned_horse_uid
		data_model.summoned_horse_uid = confirmed_summoned_horse_uid
		data_model.rider_state["SummonedHorseUID"] = confirmed_summoned_horse_uid
		_update_special_terrain_collision(confirmed_summoned_horse_uid)

		if needs_appearance_rollback:
			execute_mecha_transition( func():
				data_model.summoned_horse_uid = confirmed_summoned_horse_uid
				setup_layered_paper_dolls()
				sync_animation()
			)

		if GameManager.current_hud and GameManager.current_hud.has_method("_refresh_inventory_if_open"):
			GameManager.current_hud._refresh_inventory_if_open()
		return
	confirmed_summoned_horse_uid = horse_uid
	_update_special_terrain_collision(confirmed_summoned_horse_uid)

func _update_special_terrain_collision(horse_uid: int) -> void :
	can_cross_special_terrain = false
	if horse_uid != -1:
		for horse_equip in data_model.horse_equip_list:
			if int(horse_equip.get("horse_uid", -1)) != horse_uid:
				continue
			if int(horse_equip.get("part", -1)) != 0 or int(horse_equip.get("in_bank", 0)) != 0:
				continue

			var equip_idx: = int(horse_equip.get("equip_idx", 0))
			can_cross_special_terrain = (equip_idx >= 50311 and equip_idx <= 50318) or equip_idx == 50344 or (equip_idx >= 50359 and equip_idx <= 50361)
			break

	collision_mask = SPECIAL_MECHA_COLLISION_MASK if can_cross_special_terrain else DEFAULT_COLLISION_MASK

func _on_change_map_received(_map_id: int, _target_x: float, _target_y: float) -> void :
	is_moving = false
	_movement_state_reported = false
	is_mouse_holding = false
	move_path = PackedVector2Array()
	move_path_index = 0
	move_destination = global_position
	target_position = global_position
	velocity = Vector2.ZERO
	combat_target = null

	if is_dead and data_model.hp > 0:
		revive()

func _on_enter_world_success(data: Dictionary) -> void :
	if data.get("DBID", 0) == data_model.db_id:
		if data.has("DeathTimer") and data_model.hp <= 0:
			death_timer = float(data.get("DeathTimer", 120.0))
			if is_instance_valid(death_label):
				var displayed_second: int = int(ceil(death_timer))
				death_label.text = str(displayed_second)
				_last_death_display_second = displayed_second
				death_label.visible = true

func _on_entity_appearance_updated(data: Dictionary) -> void :
	if int(data.get("EntityID", 0)) != data_model.db_id: return

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
	data_model.transform_type = int(data.get("TransformType", data_model.transform_type))
	data_model.transform_id = str(data.get("TransformID", data_model.transform_id))

	if data.has("SummonedHorseUID"):
		data_model.summoned_horse_uid = int(data.get("SummonedHorseUID", -1))
		confirmed_summoned_horse_uid = data_model.summoned_horse_uid
		_update_special_terrain_collision(confirmed_summoned_horse_uid)
	if data.has("MechaType"):
		data_model.mecha_type = str(data.get("MechaType", "MS"))

	var mecha_parts_changed = false
	if data.has("MechaBody") or data.has("MechaWeapon") or data.has("MechaEngine") or old_mecha_type != data_model.mecha_type:
		mecha_parts_changed = true

	if name_label:
		var visible_color_state = 2 if _name_color_state == 3 else _name_color_state
		_apply_name_color_style(visible_color_state)

	current_race = GameConstants.RACE_ID_MAP.get(data_model.race_id, "Boy")

	var current_hair_id = int(current_hair.replace("Hair", "")) if current_hair.begins_with("Hair") else 0
	var hair_id = int(data.get("Hair", current_hair_id))
	current_hair = "Hair%03d" % hair_id if hair_id >= 0 else "Hair000"

	if old_race != data_model.race_id or old_hair != current_hair or old_horse_uid != data_model.summoned_horse_uid or mecha_parts_changed or old_transform_type != data_model.transform_type or old_transform_id != data_model.transform_id or old_transform_color_index != data_model.transform_color_index:
		if old_horse_uid != data_model.summoned_horse_uid:
			play_mecha_transform()
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

	if GameManager.current_hud and GameManager.current_hud.has_method("_refresh_inventory_if_open"):
		GameManager.current_hud._refresh_inventory_if_open()

func parse_login_server_data(json_packet: Dictionary) -> void :
	var clean_packet: = {}
	for key in json_packet.keys():
		var val = json_packet[key]
		clean_packet[key] = int(val) if typeof(val) == TYPE_FLOAT else val

	data_model.load_from_json(clean_packet)

	if clean_packet.has("Shortcuts"):

		if "shortcuts" in data_model:
			data_model.shortcuts = clean_packet.get("Shortcuts", [])
		else:
			data_model.set_meta("shortcuts", clean_packet.get("Shortcuts", []))

	current_race = GameConstants.RACE_ID_MAP.get(int(data_model.race_id), "Boy")

	var hair_id = int(clean_packet.get("Hair", clean_packet.get("hair", -1)))
	current_hair = "Hair%03d" % hair_id if hair_id >= 0 else "Hair000"

	current_hair_color_index = int(clean_packet.get("HairColor", 0))
	current_body_color_index = int(clean_packet.get("EquipColor0", 0))

	if ui_container: ui_container.visible = true
	name_label = get_node_or_null("UIContainer/NameLabel")
	if name_label and data_model.character_name.length() > 0:
		var race_map = {"Boy": "[騎]", "Girl": "[騎]", "Migu": "[魔]", "Robot": "[機]", "Yas": "[遺]"}
		name_label.text = race_map.get(current_race, "[?]") + data_model.character_name
		_apply_name_color(1 if data_model.red_man > 0 else 0)
		name_label.visible = not (data_model.is_shop_open or data_model.transform_type == 2)

	current_state = str(clean_packet.get("State", "idle"))
	if current_state == "":
		current_state = "idle"

	if data_model.hp <= 0 or current_state == "death":
		is_dead = true
		current_state = "death"
		is_action_locked = true

	if clean_packet.has("MechaBody") or clean_packet.has("MechaWeapon") or clean_packet.has("MechaEngine"):
		data_model.horse_equip_list.clear()
		if int(clean_packet.get("MechaBody", 0)) > 0:
			data_model.horse_equip_list.append({"horse_uid": data_model.summoned_horse_uid, "in_bank": 0, "part": 0, "equip_idx": int(clean_packet.get("MechaBody", 0))})
		if int(clean_packet.get("MechaWeapon", 0)) > 0:
			data_model.horse_equip_list.append({"horse_uid": data_model.summoned_horse_uid, "in_bank": 0, "part": 1, "equip_idx": int(clean_packet.get("MechaWeapon", 0))})
		if int(clean_packet.get("MechaEngine", 0)) > 0:
			data_model.horse_equip_list.append({"horse_uid": data_model.summoned_horse_uid, "in_bank": 0, "part": 2, "equip_idx": int(clean_packet.get("MechaEngine", 0))})

	confirmed_summoned_horse_uid = data_model.summoned_horse_uid
	_update_special_terrain_collision(confirmed_summoned_horse_uid)

	if is_inside_tree():
		setup_layered_paper_dolls()
		if data_model.hp <= 0 or current_state == "death":
			is_dead = false
			_die()
		else:
			sync_animation()


func _unhandled_input(event: InputEvent) -> void :
	if is_dead:
		if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
			if death_timer > 0.0 and death_timer <= 120.0:
				trigger_revive_sequence()
		return

	if data_model.is_shop_open: return
	if is_interacting_with_npc: return

	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT:
		if ready_to_cast_skill_id > 0:
			ready_to_cast_skill_id = -1
			if CursorManager:
				if CursorManager.has_method("cancel_skill_state"):
					CursorManager.cancel_skill_state()
				else:
					CursorManager.set_cursor_state("normal")
			return

	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed:
			if CursorManager:

				if CursorManager.cursor_sprite.animation == "skill":
					return

				if CursorManager.cursor_sprite.animation != "normal":
					return




			if data_model and data_model.is_auto_saint_on:
				if GameManager.current_hud and GameManager.current_hud.has_method("show_rejection_message_flash"):
					GameManager.current_hud.show_rejection_message_flash("[color=#FFF02D]自動聖靈[/color] 啟動中，禁止移動！")
				get_viewport().set_input_as_handled()
				return

			if current_state != "death":
				combat_target = null
				pending_skill_id = -1
				is_emoting = false
				_set_navigation_target(get_global_mouse_position())
				is_mouse_holding = true
				if not is_action_locked:
					current_state = "walk"
		else:
			is_mouse_holding = false

	if event is InputEventKey and event.pressed and not event.is_echo() and event.keycode == KEY_ALT:
		if is_action_locked: return
		is_emoting = false
		if current_state == "crouch":
			current_state = "idle"
			if NetworkManager.is_connected_to_server: NetworkManager.send_secure_packet({"Type": "SyncState", "State": "idle", "Dir": current_dir})
		elif current_state in ["idle", "idle_weapon_melee", "idle_weapon_gun"]:
			is_moving = false
			velocity = Vector2.ZERO
			current_state = "crouch"
			if NetworkManager.is_connected_to_server: NetworkManager.send_secure_packet({"Type": "SyncState", "State": "crouch", "Dir": current_dir})

func _physics_process(_delta: float) -> void :
	if is_dead:
		_handle_death_timer(_delta)
		return

	if is_mouse_holding:

		if not Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT):
			is_mouse_holding = false
		elif current_state != "death" and not data_model.is_shop_open and not is_interacting_with_npc and not data_model.is_auto_saint_on:
			var mouse_pos = get_global_mouse_position()

			if global_position.distance_to(mouse_pos) > 10.0:
				if move_destination.distance_to(mouse_pos) >= 8.0 or not is_moving:
					_set_navigation_target(mouse_pos)
				if not is_action_locked and current_state != "walk":
					current_state = "walk"

	if attack_cooldown > 0:
		attack_cooldown -= _delta

	if is_instance_valid(combat_target):
		_handle_combat_tracking()

	if is_action_locked and current_state != "guard":
		_handle_action_lock(_delta)
	else:
		action_lock_timer = 0.0

	if is_moving:
		_handle_movement(_delta)
		if not is_action_locked and current_state != "walk":
			current_state = "walk"
			sync_animation()
	elif current_state == "walk" and not is_action_locked:
		current_state = "idle"
		stuck_frames = 0

	sync_animation()
	_report_movement_started_if_needed()
	_handle_network_sync(_delta)

func _handle_death_timer(delta: float) -> void :
	death_timer -= delta
	if death_label and death_timer < 9999.0:
		var displayed_second: int = int(ceil(death_timer))
		if displayed_second != _last_death_display_second:
			_last_death_display_second = displayed_second
			death_label.text = str(displayed_second)
	if death_timer <= 0:
		trigger_revive_sequence()

func _handle_combat_tracking() -> void :
	var target_hp: int = 0
	var target_id: int = 0
	var target_type: int = 1

	if "current_hp" in combat_target:
		target_hp = combat_target.current_hp
		target_id = combat_target.entity_id

		if combat_target is PetEntity:
			target_type = 4
		else:
			target_type = 1
	elif "data_model" in combat_target:
		target_hp = combat_target.data_model.hp
		target_id = combat_target.data_model.db_id
		target_type = 0
	else:
		combat_target = null
		pending_skill_id = -1
		return




	var is_revive_skill = false
	if pending_skill_id > 0 and SkillLoader:
		var s_data = SkillLoader.get_skill_data(pending_skill_id)
		if s_data and s_data.skill_type == 6:
			is_revive_skill = true

	if is_revive_skill:

		if target_hp > 0 or target_type != 0:
			combat_target = null
			pending_skill_id = -1
			return
	else:

		if target_hp <= 0:
			combat_target = null
			pending_skill_id = -1
			return


	var dist = global_position.distance_to(combat_target.global_position)
	var required_range = float(data_model.attack_range)

	if pending_skill_id > 0 and SkillLoader:
		var s_res = SkillLoader.get_skill_data(pending_skill_id)
		if s_res:
			var my_lvl = 1
			if "data_model" in self:
				for s in data_model.skill_list:
					if s.skill_id == pending_skill_id:
						my_lvl = s.level
						break
			var s_lv = s_res.get_level_data(my_lvl)
			if not s_lv: s_lv = s_res.get_level_data(1)
			if s_lv and s_lv.cast_range > 0:
				required_range = float(s_lv.cast_range)

	if dist > required_range:
		var combat_destination: Vector2 = combat_target.global_position
		if move_destination.distance_to(combat_destination) >= 16.0 or not is_moving:
			_set_navigation_target(combat_destination)
		if current_state != "walk":
			current_state = "walk"
			sync_animation()
	else:
		if is_moving: _stop_movement()
		if not is_action_locked:
			update_direction_8way(combat_target.global_position - global_position)

			if pending_skill_id > 0:
				_send_skill_request(pending_skill_id, target_id, target_type)
				pending_skill_id = -1
				combat_target = null
			elif attack_cooldown <= 0:



				if not is_riding_mecha():
					var w_id = data_model.weapon_id
					if w_id > 0 and ItemLoader:
						var n4 = str(ItemLoader.get_item_property(w_id, "note_4", "")).strip_edges()

						var req_bullet = -1
						var bullet_name = ""
						if n4 == "TwinGun":
							req_bullet = 4103
							bullet_name = "合金彈"
						elif n4 == "Gun":
							req_bullet = 4112
							bullet_name = "魔咒彈"

						if req_bullet != -1:
							var has_bullet = false
							for itm in data_model.inventory:
								if itm.get("item_index", 0) == req_bullet and itm.get("in_bank", 0) == 0 and itm.get("in_shop", 0) == 0 and itm.get("count", 0) > 0:
									has_bullet = true
									break


							if not has_bullet:
								combat_target = null
								_stop_movement()
								if GameManager.current_hud and GameManager.current_hud.has_method("show_rejection_message_flash"):
									GameManager.current_hud.show_rejection_message_flash("[color=#FFF02D]" + bullet_name + "[/color][color=#FFFFFF] 不足！[/color]")
								return


				trigger_combat_action(get_combat_attack_state())
				if NetworkManager.is_connected_to_server:
					NetworkManager.send_attack_request(target_id, target_type)
				attack_cooldown = get_real_attack_cooldown()

func _handle_action_lock(delta: float) -> void :
	if current_state == "buff":
		return

	action_lock_timer += delta

	var lock_limit = 0.5

	if action_lock_timer > lock_limit:
		is_action_locked = false
		action_lock_timer = 0.0
		if is_moving:
			current_state = "walk"
		else:
			current_state = get_combat_idle_state()
		sync_animation()

func _handle_movement(delta: float) -> void :
	var current_real_speed: float = get_real_move_speed()
	var step_distance: float = current_real_speed * delta
	var stop_threshold: float = max(4.0, step_distance)
	var dir_vector: Vector2 = target_position - global_position
	var distance: float = dir_vector.length()

	if distance <= stop_threshold:
		if move_path_index + 1 < move_path.size():
			move_path_index += 1
			target_position = move_path[move_path_index]
			dir_vector = target_position - global_position
			distance = dir_vector.length()
		else:
			_stop_movement()
			return

	if distance <= 0.001:
		return

	# Blocking.dat decides the route. Godot physics remains only a final
	# collision guard for scene objects that are not represented in the
	# legacy blocking grid.
	velocity = dir_vector.normalized() * current_real_speed
	update_direction_8way(velocity)

	var old_position: Vector2 = global_position
	move_and_slide()
	var moved_distance: float = old_position.distance_to(global_position)

	if moved_distance >= 1.0:
		stuck_frames = 0
		return

	stuck_frames += 1
	if stuck_frames >= MAX_STUCK_FRAMES:
		_stop_movement()

func _handle_network_sync(delta: float) -> void :
	if current_state == "walk":
		sync_timer += delta
		if sync_timer >= SYNC_RATE:
			sync_timer = 0.0
			if NetworkManager.is_connected_to_server:
				NetworkManager.send_sync_position(int(global_position.x), int(global_position.y))

func _set_navigation_target(destination: Vector2) -> void :
	move_destination = destination

	var map_id: int = int(data_model.map_id) if data_model else 0
	var route: PackedVector2Array = LegacyPathfinder.get_path(
		map_id,
		global_position,
		destination,
		can_cross_special_terrain
	)

	if route.is_empty():
		_stop_movement()
		return

	move_path = route
	move_path_index = 0
	target_position = move_path[0]
	is_moving = true


func apply_position_correction(corrected_position: Vector2) -> void :
	global_position = corrected_position
	velocity = Vector2.ZERO
	is_moving = false
	is_mouse_holding = false
	move_path = PackedVector2Array()
	move_path_index = 0
	move_destination = corrected_position
	target_position = corrected_position
	stuck_frames = 0
	_movement_state_reported = false
	if not is_action_locked and not is_dead:
		current_state = "idle"
		sync_animation()


func _stop_movement() -> void :
	velocity = Vector2.ZERO
	is_moving = false
	move_path = PackedVector2Array()
	move_path_index = 0
	move_destination = global_position
	target_position = global_position
	current_state = "idle"
	stuck_frames = 0
	if NetworkManager.is_connected_to_server:
		NetworkManager.send_sync_position(int(global_position.x), int(global_position.y))
		if _movement_state_reported:
			NetworkManager.send_secure_packet({"Type": "SyncState", "State": "idle", "Dir": current_dir})
	_movement_state_reported = false

func _report_movement_started_if_needed() -> void :
	if _movement_state_reported or not is_moving or current_state != "walk":
		return
	if NetworkManager.is_connected_to_server:
		NetworkManager.send_secure_packet({"Type": "SyncState", "State": "walk", "Dir": current_dir})
		_movement_state_reported = true

func update_direction_8way(move_velocity: Vector2) -> void :
	if move_velocity == Vector2.ZERO: return
	var angle_deg: float = posmod(rad_to_deg(move_velocity.angle()), 360.0)
	var index_raw: int = int(round(angle_deg / 45.0))
	current_dir = GameConstants.DIR_STRINGS[index_raw % 8]



func get_real_move_speed() -> float:
	return move_speed * (max(data_model.move_speed, 10.0) / 100.0)

func get_real_attack_cooldown() -> float:
	var atk_spd_multiplier = max(data_model.attack_speed, 10.0) / 100.0
	return attack_speed / atk_spd_multiplier

func trigger_revive_sequence() -> void :
	death_timer = 9999.0
	_last_death_display_second = -1
	if death_label:
		death_label.text = ""
		death_label.visible = false
	if revive_fade_tween and revive_fade_tween.is_running():
		revive_fade_tween.kill()

	revive_fade_tween = create_tween()
	revive_fade_tween.tween_property(self, "modulate:a", 0.0, 1.0)
	revive_fade_tween.tween_callback( func():
		if NetworkManager.has_method("send_revive_request"):
			NetworkManager.send_revive_request()
	)

func _on_equip_response(success: bool, _message: String, data: Dictionary) -> void :
	var hud = GameManager.current_hud if GameManager else null
	if hud and hud.has_method("get_node_or_null"):
		var inv_win = hud.get_node_or_null("WindowsContainer/InventoryWindow")
		if inv_win and inv_win.has_method("unlock_equip"): inv_win.unlock_equip()

	if success:
		data_model.apply_equip_response(data)
	else:
		data_model.rollback_optimistic()
		setup_layered_paper_dolls()
		sync_animation()

	if hud:
		var inv_win = hud.get_node_or_null("WindowsContainer/InventoryWindow")
		if inv_win and inv_win.visible: inv_win.update_ui()


		var char_win = hud.get_node_or_null("WindowsContainer/CharacterWindow")
		if char_win and char_win.visible and char_win.has_method("update_ui"):
			char_win.update_ui()

		if hud.has_method("update_status_panel"):
			hud.update_status_panel()


func prepare_skill(skill_id: int) -> void :
	if not can_attack():
		if GameManager.current_hud and GameManager.current_hud.has_method("show_rejection_message_flash"):
			GameManager.current_hud.show_rejection_message_flash("駕駛此機甲無法進行戰鬥！")
		return

	if SkillLoader:
		var skill = SkillLoader.get_skill_data(skill_id)
		if not skill: return


		var require_target = false
		if skill.skill_type in [0, 1, 2, 5, 6]:
			require_target = true
		elif skill.skill_type == 7:
			if "special_skill" in skill and skill.special_skill.contains("冰之觸"):
				require_target = false
			else:
				var s_lv = skill.get_level_data(1)
				if s_lv and s_lv.cast_range > 0:
					require_target = true

		if require_target:
			ready_to_cast_skill_id = skill_id
			if CursorManager:
				CursorManager.set_cursor_state("skill")

				if is_instance_valid(CursorManager.get("current_hovered_entity")):
					var hovered = CursorManager.current_hovered_entity
					if hovered.has_method("_on_mouse_entered"):
						hovered._on_mouse_entered()
		else:

			_send_skill_request(skill_id, -1, 1)

func _send_skill_request(skill_id: int, target_id: int, target_type: int) -> void :
	if NetworkManager and NetworkManager.is_connected_to_server:
		NetworkManager.send_secure_packet({
			"Type": "UseSkill", 
			"SkillID": skill_id, 
			"TargetID": target_id, 
			"TargetType": target_type, 
			"TargetX": int(global_position.x), 
			"TargetY": int(global_position.y)
		})




func _on_click_area_input_event(_viewport: Node, event: InputEvent, _shape_idx: int) -> void :
	if is_dead: return
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if ready_to_cast_skill_id > 0:
			if SkillLoader:
				var skill = SkillLoader.get_skill_data(ready_to_cast_skill_id)


				var can_self_cast = false
				if skill and (skill.skill_type == 2 or skill.skill_type == 5):
					can_self_cast = true
				elif skill and skill.skill_type == 7:
					var s_lv = skill.get_level_data(1)
					if s_lv and s_lv.cast_range > 0:
						can_self_cast = true

				if can_self_cast:
					combat_target = self
					pending_skill_id = ready_to_cast_skill_id
					ready_to_cast_skill_id = -1
					if CursorManager:
						if CursorManager.has_method("cancel_skill_state"):
							CursorManager.cancel_skill_state()
						else:
							CursorManager.set_cursor_state("normal")
					get_viewport().set_input_as_handled()




func _on_mouse_entered() -> void :
	if is_dead: return
	if CursorManager:

		CursorManager.register_hover(self, "player")


		if CursorManager.cursor_sprite.animation == "skill":
			if ready_to_cast_skill_id > 0 and SkillLoader:
				var skill_data = SkillLoader.get_skill_data(ready_to_cast_skill_id)


				var can_self_cast = false
				if skill_data and (skill_data.skill_type == 2 or skill_data.skill_type == 5):
					can_self_cast = true
				elif skill_data and skill_data.skill_type == 7:
					var s_lv = skill_data.get_level_data(1)
					if s_lv and s_lv.cast_range > 0:
						can_self_cast = true

				if can_self_cast:
					var char_name = data_model.character_name if data_model else "自己"
					if CursorManager.has_method("show_skill_target_tooltip"):
						CursorManager.show_skill_target_tooltip(char_name, skill_data.skill_name)




func _on_mouse_exited() -> void :
	if CursorManager:

		CursorManager.unregister_hover(self)

		if CursorManager.cursor_sprite.animation == "skill":
			if CursorManager.has_method("hide_skill_target_tooltip"):
				CursorManager.hide_skill_target_tooltip()


func _on_allocate_stat_response(data: Dictionary) -> void :
	if not is_instance_valid(self) or not "data_model" in self: return


	data_model.apply_allocate_stat_response(data)


	var hud = GameManager.current_hud if GameManager else null
	if hud:
		var char_win = hud.get_node_or_null("WindowsContainer/CharacterWindow")
		if char_win and char_win.visible and char_win.has_method("update_ui"):
			char_win.update_ui()

		if hud.has_method("update_status_panel"):
			hud.update_status_panel()
