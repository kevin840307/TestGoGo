
extends Node

const NETWORK_PLAYER_SCENE = preload("res://network_player.tscn")
const MONSTER_SCENE = preload("res://monster_entity.tscn")
const NPC_SCENE = preload("res://npc_entity.tscn")
const GROUND_ITEM_SCENE = preload("res://ground_item_entity.tscn")
const PET_SCENE = preload("res://pet_entity.tscn")
const DYNAMIC_ENTITY_VISIBILITY_LAYER: = 1 << 1
const DEBUG_MONSTER_NETWORK_LOGS: bool = false
const MONSTER_AOI_ENTER_RADIUS: float = 1800.0
const MONSTER_AOI_EXIT_RADIUS: float = 2200.0
const MONSTER_AOI_UPDATE_INTERVAL: float = 0.20

var active_entities: Dictionary = {}
var _last_skill_casts: Dictionary = {}
var _active_entity_types: Dictionary = {}

# Initial map sync can deliver dozens of entities back-to-back. Instantiating
# every scene immediately makes all _ready()/texture/animation work land in the
# same frame, causing a visible multi-second stall.
const SPAWN_BATCH_PER_FRAME: int = 3
const SPAWN_FRAME_BUDGET_USEC: int = 3500
var _spawn_queue: Array[Dictionary] = []
var _queued_spawn_ids: Dictionary = {}
var _last_spawn_enqueue_usec: int = 0
var _runtime_monster_ids: Dictionary = {}
var _spawn_queue_paused: bool = false
var _monster_spawn_templates: Dictionary = {}
var _deferred_monster_spawns: Dictionary = {}
var _monster_aoi_elapsed: float = 0.0

func get_active_entity(entity_id: int) -> Node2D:
	var entity = active_entities.get(entity_id)
	if not is_instance_valid(entity) or not entity.is_inside_tree() or entity.is_queued_for_deletion():
		return null
	var current_map = GameManager.current_map_node
	if not is_instance_valid(current_map) or not current_map.is_ancestor_of(entity):
		return null
	return entity

func clear_map_entities() -> Dictionary:
	var cleared_by_type: = {}
	for entity in active_entities.values():
		if is_instance_valid(entity):
			_unregister_entity_hover(entity)
			entity.queue_free()
	for entity_type in _active_entity_types.values():
		var type_key: = str(int(entity_type))
		cleared_by_type[type_key] = int(cleared_by_type.get(type_key, 0)) + 1
	var cleared_total: = active_entities.size()
	active_entities.clear()
	_active_entity_types.clear()
	_last_skill_casts.clear()
	_spawn_queue.clear()
	_queued_spawn_ids.clear()
	_runtime_monster_ids.clear()
	_monster_spawn_templates.clear()
	_deferred_monster_spawns.clear()
	_monster_aoi_elapsed = 0.0
	return {"total": cleared_total, "by_type": cleared_by_type}

func _ready() -> void :
	NetworkManager.entity_spawned.connect(_on_entity_spawned)
	NetworkManager.entity_despawned.connect(_on_entity_despawned)
	NetworkManager.entity_moved.connect(_on_entity_moved)
	NetworkManager.entity_state_changed.connect(_on_entity_state_changed)
	NetworkManager.entity_appearance_updated.connect(_on_entity_appearance_updated)
	NetworkManager.entity_damage_received.connect(_on_entity_damage_received)
	NetworkManager.entity_death_received.connect(_on_entity_death_received)
	NetworkManager.monster_target_changed.connect(_on_monster_target_changed)
	NetworkManager.pet_idle_speech_received.connect(_on_pet_idle_speech_received)
	if NetworkManager.has_signal("skill_cast_received"):
		if not NetworkManager.skill_cast_received.is_connected(_on_skill_cast_received):
			NetworkManager.skill_cast_received.connect(_on_skill_cast_received)

	if NetworkManager.has_signal("entity_heal_received"):
		if not NetworkManager.entity_heal_received.is_connected(_on_entity_heal_received):
			NetworkManager.entity_heal_received.connect(_on_entity_heal_received)

func get_aoi_active_monster_count() -> int:
	var count: int = 0
	for entity_type in _active_entity_types.values():
		if int(entity_type) == 1:
			count += 1
	return count

func get_aoi_deferred_monster_count() -> int:
	return _deferred_monster_spawns.size()

func _unhandled_input(event: InputEvent) -> void :
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if GameManager.current_hud and is_instance_valid(GameManager.current_hud):
			if GameManager.current_hud.get("_next_dialog_node") != "":
				GameManager.current_hud.request_next_dialog_sentence()

func _get_local_player_position() -> Variant:
	var player = GameManager.current_player if GameManager else null
	if not is_instance_valid(player) or not player.is_inside_tree():
		return null
	return player.global_position

func _monster_packet_position(data: Dictionary) -> Vector2:
	return Vector2(
		float(data.get("LocX", data.get("X", 0.0))),
		float(data.get("LocY", data.get("Y", 0.0)))
	)

func _is_monster_inside_aoi(data: Dictionary, radius: float) -> bool:
	var player_pos = _get_local_player_position()
	if player_pos == null:
		return true
	return (player_pos as Vector2).distance_squared_to(_monster_packet_position(data)) <= radius * radius

func _defer_monster_spawn(data: Dictionary) -> void :
	var entity_id: int = int(data.get("EntityID", 0))
	if entity_id <= 0:
		return
	var cached: Dictionary = data.duplicate(true)
	_monster_spawn_templates[entity_id] = cached
	_deferred_monster_spawns[entity_id] = cached
	_queued_spawn_ids.erase(entity_id)

func _remove_queued_spawn(entity_id: int) -> void :
	_queued_spawn_ids.erase(entity_id)
	for index: int in range(_spawn_queue.size() - 1, -1, -1):
		if int(_spawn_queue[index].get("EntityID", 0)) == entity_id:
			_spawn_queue.remove_at(index)

func _queue_cached_monster_spawn(entity_id: int) -> void :
	if get_active_entity(entity_id) or _queued_spawn_ids.has(entity_id):
		return
	var cached = _deferred_monster_spawns.get(entity_id, null)
	if not cached is Dictionary:
		return
	_deferred_monster_spawns.erase(entity_id)
	_queued_spawn_ids[entity_id] = true
	_spawn_queue.append((cached as Dictionary).duplicate(true))
	_last_spawn_enqueue_usec = Time.get_ticks_usec()

func _unload_monster_for_aoi(entity_id: int, monster: Node2D) -> void :
	var cached = _monster_spawn_templates.get(entity_id, null)
	if cached is Dictionary:
		var updated: Dictionary = (cached as Dictionary).duplicate(true)
		updated["LocX"] = monster.global_position.x
		updated["LocY"] = monster.global_position.y
		_monster_spawn_templates[entity_id] = updated
		_deferred_monster_spawns[entity_id] = updated
	_unregister_entity_hover(monster)
	monster.queue_free()
	active_entities.erase(entity_id)
	_active_entity_types.erase(entity_id)
	_runtime_monster_ids.erase(entity_id)

func _update_monster_aoi(delta: float) -> void :
	_monster_aoi_elapsed += delta
	if _monster_aoi_elapsed < MONSTER_AOI_UPDATE_INTERVAL:
		return
	_monster_aoi_elapsed = 0.0
	var player_pos = _get_local_player_position()
	if player_pos == null:
		return
	var local_pos: Vector2 = player_pos as Vector2
	var exit_radius_sq: float = MONSTER_AOI_EXIT_RADIUS * MONSTER_AOI_EXIT_RADIUS
	var enter_radius_sq: float = MONSTER_AOI_ENTER_RADIUS * MONSTER_AOI_ENTER_RADIUS
	for raw_id in _active_entity_types.keys():
		var entity_id: int = int(raw_id)
		if int(_active_entity_types.get(entity_id, -1)) != 1:
			continue
		var monster := get_active_entity(entity_id)
		if is_instance_valid(monster) and local_pos.distance_squared_to(monster.global_position) > exit_radius_sq:
			_unload_monster_for_aoi(entity_id, monster)
	for raw_id in _deferred_monster_spawns.keys():
		var entity_id: int = int(raw_id)
		var cached = _deferred_monster_spawns.get(entity_id, null)
		if cached is Dictionary and local_pos.distance_squared_to(_monster_packet_position(cached)) <= enter_radius_sq:
			_queue_cached_monster_spawn(entity_id)

func _on_entity_spawned(data: Dictionary) -> void :
	var entity_id := int(data.get("EntityID", 0))
	if entity_id <= 0:
		return
	var entity_type: int = int(data.get("EntityType", -1))
	if entity_type == 1:
		var cached: Dictionary = data.duplicate(true)
		_monster_spawn_templates[entity_id] = cached
		if not _is_monster_inside_aoi(cached, MONSTER_AOI_ENTER_RADIUS):
			_defer_monster_spawn(cached)
			return
		_deferred_monster_spawns.erase(entity_id)
	if get_active_entity(entity_id) or _queued_spawn_ids.has(entity_id):
		return
	_queued_spawn_ids[entity_id] = true
	var queued_data: Dictionary = data.duplicate(true)
	# Existing drops should be visible as soon as a revisited map is revealed.
	# Put ground items ahead of bulk monster/player snapshot work so they do not
	# wait behind hundreds of entity instantiations.
	if entity_type == 3:
		_spawn_queue.push_front(queued_data)
	else:
		_spawn_queue.append(queued_data)
	_last_spawn_enqueue_usec = Time.get_ticks_usec()

func set_spawn_queue_paused(paused: bool) -> void :
	_spawn_queue_paused = paused

func is_spawn_queue_paused() -> bool:
	return _spawn_queue_paused

func is_initial_spawn_queue_idle(quiet_usec: int = 300000) -> bool:
	if not _spawn_queue.is_empty() or not _queued_spawn_ids.is_empty():
		return false
	if _last_spawn_enqueue_usec <= 0:
		return true
	return Time.get_ticks_usec() - _last_spawn_enqueue_usec >= quiet_usec

func _process(delta: float) -> void :
	if not is_instance_valid(GameManager.current_map_node) or GameManager.current_map_node.is_queued_for_deletion():
		_spawn_queue.clear()
		_queued_spawn_ids.clear()
		_runtime_monster_ids.clear()
		_monster_spawn_templates.clear()
		_deferred_monster_spawns.clear()
		return

	_update_monster_aoi(delta)

	if not _spawn_queue_paused and not _spawn_queue.is_empty():
		var frame_started_usec := Time.get_ticks_usec()
		var spawned_count := 0
		while not _spawn_queue.is_empty() and spawned_count < SPAWN_BATCH_PER_FRAME:
			if spawned_count > 0 and Time.get_ticks_usec() - frame_started_usec >= SPAWN_FRAME_BUDGET_USEC:
				break
			var data: Dictionary = _spawn_queue.pop_front()
			var entity_id := int(data.get("EntityID", 0))
			_queued_spawn_ids.erase(entity_id)
			_spawn_entity_now(data)
			spawned_count += 1

		var elapsed_usec := Time.get_ticks_usec() - frame_started_usec
		if elapsed_usec >= 10000:
			print("[ENTITY_SPAWN_TIMING] count=%d elapsed_ms=%.2f remaining=%d" % [
				spawned_count,
				float(elapsed_usec) / 1000.0,
				_spawn_queue.size()
			])

	if _runtime_monster_ids.is_empty():
		return

	var finished_ids: Array[int] = []
	for runtime_id in _runtime_monster_ids.keys():
		var entity_id := int(runtime_id)
		var monster := get_active_entity(entity_id)
		if not is_instance_valid(monster) or not monster.has_method("advance_network_runtime"):
			finished_ids.append(entity_id)
			continue
		var keep_active: bool = bool(monster.advance_network_runtime(delta))
		if not keep_active:
			finished_ids.append(entity_id)

	for entity_id in finished_ids:
		_runtime_monster_ids.erase(entity_id)

func _spawn_entity_now(data: Dictionary) -> void :
	var entity_id = int(data.get("EntityID", 0))
	var entity_type = int(data.get("EntityType", 0))
	if entity_type == 1:
		_monster_spawn_templates[entity_id] = data.duplicate(true)
		if not _is_monster_inside_aoi(data, MONSTER_AOI_EXIT_RADIUS):
			_defer_monster_spawn(data)
			return

	if get_active_entity(entity_id):
		return
	active_entities.erase(entity_id)
	if not is_instance_valid(GameManager.current_map_node) or GameManager.current_map_node.is_queued_for_deletion():
		return

	var spawn_pos = Vector2(float(data.get("LocX", 0)), float(data.get("LocY", 0)))
	var new_entity: Node2D = null

	var target_parent = GameManager.current_map_node
	if target_parent.has_node("Entities"):
		target_parent = target_parent.get_node("Entities")
	elif target_parent.has_node("ObjectContainer"):
		target_parent = target_parent.get_node("ObjectContainer")

	match entity_type:
		0:
			new_entity = NETWORK_PLAYER_SCENE.instantiate()
			target_parent.add_child(new_entity)
			new_entity.global_position = spawn_pos
			if new_entity.has_method("initialize_network_player"):
				new_entity.initialize_network_player(data)
		1:
			new_entity = MONSTER_SCENE.instantiate()
			target_parent.add_child(new_entity)
			new_entity.global_position = spawn_pos
			if new_entity.has_method("setup"):
				new_entity.setup(entity_id, data.get("Name", "未知怪物"), data.get("ExtraData", {}))
		2:
			new_entity = NPC_SCENE.instantiate()
			target_parent.add_child(new_entity)
			new_entity.global_position = spawn_pos
			if new_entity.has_method("initialize_npc"):
				new_entity.initialize_npc(data)
		3:
			new_entity = GROUND_ITEM_SCENE.instantiate()
			target_parent.add_child(new_entity)
			new_entity.global_position = spawn_pos

			var drop_delay = 0.0
			var is_monster_drop = false

			for active_entity_id in _active_entity_types:
				if int(_active_entity_types[active_entity_id]) != 1:
					continue
				var monster = get_active_entity(int(active_entity_id))
				if not monster:
					continue
				if monster.current_hp <= 0 or monster.has_meta("fatal_hit_delay"):
					if monster.global_position.distance_to(spawn_pos) < 100.0:
						var fatal_delay = float(monster.get_meta("fatal_hit_delay")) if monster.has_meta("fatal_hit_delay") else 0.0
						drop_delay = fatal_delay
						is_monster_drop = true
						break

			if is_monster_drop:
				new_entity.visible = false

			if new_entity.has_method("initialize_ground_item"):
				new_entity.initialize_ground_item(data)

			if is_monster_drop:
				var bounce_tween = new_entity.create_tween()
				bounce_tween.tween_interval(drop_delay)
				bounce_tween.tween_callback( func():
					if is_instance_valid(new_entity):
						new_entity.visible = true

						var start_pos = new_entity.position
						var floor_pos = start_pos + Vector2(randf_range(-45.0, 45.0), randf_range(-20.0, 25.0))
						var peak_pos = start_pos + Vector2((floor_pos.x - start_pos.x) / 2.0, -50.0)

						var t = new_entity.create_tween()
						t.tween_property(new_entity, "position", peak_pos, 0.2).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
						t.tween_property(new_entity, "position", floor_pos, 0.2).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)
						t.tween_property(new_entity, "position", floor_pos + Vector2(0, -10), 0.1).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
						t.tween_property(new_entity, "position", floor_pos, 0.1).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)
				)
		4:
			new_entity = PET_SCENE.instantiate()
			target_parent.add_child(new_entity)
			new_entity.global_position = spawn_pos
			if new_entity.has_method("initialize_pet"):
				new_entity.initialize_pet(data, data.get("ExtraData", {}))

	if new_entity:
		new_entity.visibility_layer = DYNAMIC_ENTITY_VISIBILITY_LAYER
		active_entities[entity_id] = new_entity
		_active_entity_types[entity_id] = entity_type

func _unregister_entity_hover(entity: Node) -> void :
	if CursorManager and is_instance_valid(entity):
		CursorManager.unregister_hover(entity)

func _finalize_entity_death(entity: Node, clear_hover: bool) -> void :
	if not is_instance_valid(entity) or not entity.is_inside_tree() or entity.is_queued_for_deletion():
		return


	if clear_hover:
		_unregister_entity_hover(entity)
	if entity.has_method("_die"):
		entity._die()
	else:
		entity.queue_free()

func _on_entity_despawned(data: Dictionary) -> void :
	var entity_id = int(data.get("EntityID", 0))
	var target_node = get_active_entity(entity_id)
	if target_node:
		var local_player = GameManager.current_player
		if is_instance_valid(local_player) and local_player.combat_target == target_node:
			local_player.combat_target = null
		_unregister_entity_hover(target_node)
		target_node.queue_free()
	active_entities.erase(entity_id)
	_active_entity_types.erase(entity_id)
	if _last_skill_casts.has(entity_id):
		_last_skill_casts.erase(entity_id)
	_runtime_monster_ids.erase(entity_id)
	_monster_spawn_templates.erase(entity_id)
	_deferred_monster_spawns.erase(entity_id)
	_remove_queued_spawn(entity_id)

func _on_entity_moved(data: Dictionary) -> void :
	var entity_id = int(data.get("EntityID", 0))
	if _monster_spawn_templates.has(entity_id):
		var cached: Dictionary = (_monster_spawn_templates[entity_id] as Dictionary).duplicate(true)
		cached["LocX"] = float(data.get("X", cached.get("LocX", 0.0)))
		cached["LocY"] = float(data.get("Y", cached.get("LocY", 0.0)))
		_monster_spawn_templates[entity_id] = cached
		if _deferred_monster_spawns.has(entity_id):
			_deferred_monster_spawns[entity_id] = cached
			if _is_monster_inside_aoi(cached, MONSTER_AOI_ENTER_RADIUS):
				_queue_cached_monster_spawn(entity_id)
			return
	var target_node = get_active_entity(entity_id)
	if target_node:
		if DEBUG_MONSTER_NETWORK_LOGS and int(_active_entity_types.get(entity_id, -1)) == 1:
			print("[MONSTER_NET_MOVE] id=", entity_id,
				" x=", data.get("X", 0),
				" y=", data.get("Y", 0),
				" speed=", data.get("SpeedTime", 1.0))
		var target_x = float(data.get("X", 0))
		var target_y = float(data.get("Y", 0))
		var new_dir = str(data.get("Dir", ""))
		var speed_time = float(data.get("SpeedTime", 1.0))

		var target_pos := Vector2(target_x, target_y)
		var derives_direction_from_target: bool = target_node.has_method("_apply_network_target_direction")

		if new_dir != "" and not derives_direction_from_target:
			if target_node.has_method("apply_network_move_direction"):
				target_node.apply_network_move_direction(new_dir)
			elif "current_dir" in target_node:
				target_node.current_dir = new_dir

		if target_node.has_method("network_move_to"):
			var is_monster := int(_active_entity_types.get(entity_id, -1)) == 1
			var is_visible_runtime := true
			if is_monster and target_node.has_method("is_runtime_visible"):
				is_visible_runtime = bool(target_node.is_runtime_visible())

			if is_monster and not is_visible_runtime and target_node.has_method("apply_offscreen_network_move"):
				target_node.apply_offscreen_network_move(target_pos, new_dir)
				_runtime_monster_ids.erase(entity_id)
			else:
				target_node.network_move_to(target_pos, speed_time)
				if is_monster and speed_time > 0.01:
					_runtime_monster_ids[entity_id] = true

func _on_entity_state_changed(entity_id: int, new_state: String, new_dir: String) -> void :
	var target_node = get_active_entity(entity_id)
	if target_node:
		if DEBUG_MONSTER_NETWORK_LOGS and int(_active_entity_types.get(entity_id, -1)) == 1:
			print("[MONSTER_NET_STATE] id=", entity_id,
				" state=", new_state,
				" dir=", new_dir)
		if target_node.has_method("apply_network_state"):
			target_node.apply_network_state(new_state, new_dir)
		else:
			if "current_state" in target_node:
				target_node.current_state = new_state
			if "current_dir" in target_node:
				target_node.current_dir = new_dir
		if "is_action_locked" in target_node:
			target_node.is_action_locked = false
		if target_node.has_method("adjust_interact_area_for_death"):
			target_node.adjust_interact_area_for_death(new_state == "death")
		if target_node.has_method("sync_animation"):
			target_node.sync_animation()
		if target_node.has_method("refresh_network_physics"):
			target_node.refresh_network_physics()
		if int(_active_entity_types.get(entity_id, -1)) == 1:
			if new_state in ["walk", "attack", "hit"]:
				_runtime_monster_ids[entity_id] = true
			elif new_state in ["idle", "death", "die"]:
				_runtime_monster_ids.erase(entity_id)

func _on_pet_idle_speech_received(entity_id: int, message: String) -> void :
	var target_node = get_active_entity(entity_id)
	if target_node and target_node.has_method("show_idle_speech_bubble"):
		target_node.show_idle_speech_bubble(message)

func _on_entity_appearance_updated(data: Dictionary) -> void :
	var entity_id = int(data.get("EntityID", 0))
	var target_node = get_active_entity(entity_id)
	if target_node and target_node.has_method("update_appearance_from_server"):
		target_node.update_appearance_from_server(data)

func _on_entity_damage_received(data: Dictionary) -> void :
	var target_id = int(data.get("TargetID", 0))
	var target_type = int(data.get("TargetType", 0))
	var damage = int(data.get("Damage", 0))
	var current_hp = int(data.get("CurrentHP", 0))
	if target_type == 1 and _monster_spawn_templates.has(target_id):
		var cached: Dictionary = (_monster_spawn_templates[target_id] as Dictionary).duplicate(true)
		var extra: Dictionary = (cached.get("ExtraData", {}) as Dictionary).duplicate(true)
		extra["CurrentHP"] = current_hp
		if data.has("MaxHP"):
			extra["MaxHP"] = int(data.get("MaxHP", extra.get("MaxHP", 1)))
		cached["ExtraData"] = extra
		_monster_spawn_templates[target_id] = cached
		if _deferred_monster_spawns.has(target_id):
			_deferred_monster_spawns[target_id] = cached
	var is_crit = bool(data.get("IsCrit", false))
	var skill_id = int(data.get("SkillID", 0))

	var attacker_id = int(data.get("AttackerID", 0))
	if DEBUG_MONSTER_NETWORK_LOGS and int(_active_entity_types.get(attacker_id, -1)) == 1:
		print("[MONSTER_NET_DAMAGE] attacker=", attacker_id,
			" target=", target_id,
			" target_type=", target_type,
			" damage=", damage,
			" hp=", current_hp)

	var is_primary = true
	if skill_id > 0 and _last_skill_casts.has(attacker_id):
		if _last_skill_casts[attacker_id].skill_id == skill_id:
			if _last_skill_casts[attacker_id].target_id != target_id:
				is_primary = false

	var local_player = GameManager.current_player if GameManager else null
	var target_node = null
	var attacker_node = null
	var hit_delay: float = 0.3




	if target_type == 0 and is_instance_valid(local_player) and "data_model" in local_player and local_player.data_model.db_id == target_id:
		target_node = local_player
	else:
		target_node = get_active_entity(target_id)




	var is_mecha_double = false
	var is_twingun = false

	if attacker_id != 0:
		if is_instance_valid(local_player) and "data_model" in local_player and local_player.data_model.db_id == attacker_id:
			attacker_node = local_player
		else:
			attacker_node = get_active_entity(attacker_id)

		if is_instance_valid(attacker_node):
			if attacker_node.has_method("is_riding_mecha") and attacker_node.is_riding_mecha():
				is_mecha_double = true
			else:

				var weapon_id = 0
				if "data_model" in attacker_node:
					weapon_id = attacker_node.data_model.weapon_id
				elif "weapon_id" in attacker_node:
					weapon_id = attacker_node.weapon_id

				if weapon_id > 0 and ItemLoader:
					var n4 = str(ItemLoader.get_item_property(weapon_id, "note_4", "")).strip_edges()
					if n4 == "TwinGun":
						is_twingun = true


			var a_spd: float = 100.0
			if "data_model" in attacker_node and "attack_speed" in attacker_node.data_model:
				a_spd = float(attacker_node.data_model.attack_speed)
			elif "attack_speed" in attacker_node:
				a_spd = float(attacker_node.attack_speed)
				if a_spd < 10.0: a_spd *= 100.0

			if a_spd > 0:
				hit_delay = 0.28 / (a_spd / 100.0)


			var a_range: float = 80.0
			if "data_model" in attacker_node and "attack_range" in attacker_node.data_model:
				a_range = float(attacker_node.data_model.attack_range)
			elif "attack_range" in attacker_node:
				a_range = float(attacker_node.attack_range)

			var is_ranged = false
			if a_range > 250.0 or is_mecha_double:
				is_ranged = true


			if is_ranged and is_instance_valid(target_node):
				var dist = attacker_node.global_position.distance_to(target_node.global_position)
				var bullet_speed = 600.0
				var ranged_offset = -0.05

				if attacker_node == local_player:
					ranged_offset = -0.05

				hit_delay = max((dist / bullet_speed) + ranged_offset, 0.05)




		if skill_id > 0:
			hit_delay += 0.2
		elif attacker_node == local_player and is_instance_valid(attacker_node) and attacker_node.has_method("get_remaining_normal_hit_delay"):


			hit_delay = attacker_node.get_remaining_normal_hit_delay(hit_delay)

	if is_instance_valid(attacker_node) and is_instance_valid(target_node):


		if "combat_target" in attacker_node and attacker_node != local_player:
			attacker_node.combat_target = target_node




	if is_instance_valid(target_node):
		var is_miss = (damage <= 0)


		if is_twingun and damage > 1 and not is_miss and skill_id == 0:
			var split_percent = randf_range(0.4, 0.6)
			var dmg1 = int(round(damage * split_percent))
			var dmg2 = damage - dmg1
			if dmg1 <= 0: dmg1 = 1;dmg2 = damage - 1
			if dmg2 <= 0: dmg2 = 1;dmg1 = damage - 1

			var hit_delay_1 = hit_delay * 0.8
			var hit_delay_2 = hit_delay * 1.6

			if current_hp <= 0:
				target_node.set_meta("fatal_hit_delay", hit_delay_2)

			if target_node.has_method("update_hp"):
				target_node.update_hp(current_hp + dmg2, hit_delay_1, false)
				target_node.update_hp(current_hp, hit_delay_2, false)

			if target_node.has_method("show_damage_number"):

				target_node.show_damage_number(dmg1, is_crit, hit_delay_1, 1, skill_id, is_primary)
				target_node.show_damage_number(dmg2, is_crit, hit_delay_2, 1, skill_id, is_primary)

		else:

			if current_hp <= 0: target_node.set_meta("fatal_hit_delay", hit_delay)
			if target_node.has_method("update_hp"): target_node.update_hp(current_hp, hit_delay, is_miss)

			var simultaneous_split = 1


			if skill_id > 0:
				var skill_level = int(data.get("SkillLevel", 1))
				if SkillLoader:
					var s_res = SkillLoader.get_skill_data(skill_id)
					if s_res:
						var s_lv = s_res.get_level_data(skill_level)
						if s_lv and s_lv.tick_count > 0:
							simultaneous_split = s_lv.tick_count
						elif s_res.levels.size() > 0 and s_res.levels[0].tick_count > 0:
							simultaneous_split = s_res.levels[0].tick_count

			elif is_mecha_double:
				simultaneous_split = 2

			if target_node.has_method("show_damage_number"):
				target_node.show_damage_number(damage, is_crit, hit_delay, simultaneous_split, skill_id, is_primary)




	if is_instance_valid(attacker_node):
		if attacker_node == local_player:
			pass
		else:
			if is_instance_valid(target_node):
				if attacker_node is MonsterEntity and attacker_node.has_method("apply_attack_visual_direction"):
					# The first attack keeps the known-good chase facing. For
					# repeated stationary attacks, update toward the target's
					# current rendered position with hysteresis in MonsterEntity.
					attacker_node.apply_attack_visual_direction(target_node.global_position)
				elif attacker_node.has_method("update_direction_8way"):
					attacker_node.update_direction_8way(target_node.global_position - attacker_node.global_position)
				elif "current_dir" in attacker_node:
					var dir_vec = target_node.global_position - attacker_node.global_position
					if dir_vec != Vector2.ZERO:
						var angle_deg = posmod(rad_to_deg(dir_vec.angle()), 360.0)
						var index_raw = int(round(angle_deg / 45.0))
						var DIR_STRINGS = ["6", "7", "8", "1", "2", "3", "4", "5"]
						attacker_node.current_dir = DIR_STRINGS[index_raw % 8]

			if attacker_node.has_method("trigger_combat_action") and attacker_node.has_method("get_combat_attack_state"):
				attacker_node.trigger_combat_action(attacker_node.get_combat_attack_state())

func _on_entity_death_received(data: Dictionary) -> void :
	var entity_id = int(data.get("EntityID", 0))
	var entity_type = int(data.get("EntityType", 0))

	var local_player = GameManager.current_player if GameManager else null
	if entity_type == 0 and is_instance_valid(local_player) and "data_model" in local_player:
		if local_player.data_model.db_id == entity_id:
			return

	var entity = get_active_entity(entity_id)
	if entity:
		var clear_hover_on_death = entity_type != 0
		var death_delay = 0.0
		if entity.has_meta("fatal_hit_delay"):
			death_delay = float(entity.get_meta("fatal_hit_delay"))
		if death_delay > 0:
			get_tree().create_timer(death_delay).timeout.connect( func():
				_finalize_entity_death(entity, clear_hover_on_death)
			)
		else:
			_finalize_entity_death(entity, clear_hover_on_death)
	if entity_type != 0:
		active_entities.erase(entity_id)
	if entity_type == 1:
		_active_entity_types.erase(entity_id)
		_runtime_monster_ids.erase(entity_id)
		_monster_spawn_templates.erase(entity_id)
		_deferred_monster_spawns.erase(entity_id)
		_remove_queued_spawn(entity_id)


func _on_skill_cast_received(caster_id: int, caster_type: int, skill_id: int, skill_level: int, target_id: int, target_x: int, target_y: int, aoe_targets: Array = []) -> void :

	_last_skill_casts[caster_id] = {
		"skill_id": skill_id, 
		"target_id": target_id
	}

	var caster_node = null
	var local_player = GameManager.current_player if GameManager else null

	if caster_type == 0 and is_instance_valid(local_player) and "data_model" in local_player and local_player.data_model.db_id == caster_id:
		caster_node = local_player
	else:
		caster_node = get_active_entity(caster_id)

	if is_instance_valid(caster_node) and caster_node.has_method("execute_skill_visuals"):
		var target_node = null
		if target_id > 0:
			target_node = get_active_entity(target_id)
			if not target_node and is_instance_valid(local_player) and "data_model" in local_player and local_player.data_model.db_id == target_id:
				target_node = local_player

		caster_node.execute_skill_visuals(skill_id, skill_level, target_node, aoe_targets)

func _on_monster_target_changed(entity_id: int, has_target: bool) -> void :
	var target_node = get_active_entity(entity_id)
	if target_node and target_node.has_method("set_target_indicator"):
		target_node.set_target_indicator(has_target)

func _on_entity_heal_received(data: Dictionary) -> void :



	pass
