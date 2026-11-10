
extends Node

const NETWORK_PLAYER_SCENE = preload("res://network_player.tscn")
const MONSTER_SCENE = preload("res://monster_entity.tscn")
const NPC_SCENE = preload("res://npc_entity.tscn")
const GROUND_ITEM_SCENE = preload("res://ground_item_entity.tscn")
const PET_SCENE = preload("res://pet_entity.tscn")
const PENDING_AOI_MOVES = preload("res://scripts/network_entity_pending_moves.gd")
const GROUND_DROP_SPATIAL_INDEX = preload("res://scripts/ground_drop_spatial_index.gd")
const DYNAMIC_ENTITY_VISIBILITY_LAYER: = 1 << 1
const DEBUG_MONSTER_NETWORK_LOGS: bool = false
const MONSTER_AOI_ENTER_RADIUS: float = 1800.0
const MONSTER_AOI_EXIT_RADIUS: float = 2200.0
const MONSTER_AOI_UPDATE_INTERVAL: float = 0.20
# Server MoveEntity/SpawnEntity events update queues immediately. If the
# local observer has not moved, full-map scans can be reduced safely.
const MONSTER_AOI_STATIONARY_SCAN_INTERVAL: float = 1.0
# Defer distant ground-drop scenes but always retain server snapshots.
const GROUND_AOI_ENTER_RADIUS: float = 2000.0
const GROUND_AOI_EXIT_RADIUS: float = 2400.0
const GROUND_AOI_UPDATE_INTERVAL: float = 0.25
const GROUND_AOI_STATIONARY_SCAN_INTERVAL: float = 1.0
# AFK keeps ground-drop packets as dictionaries and releases their presentation
# nodes in small batches. 64 at foreground cadence avoids a long teardown frame.
const AFK_GROUND_COMPACT_BATCH_SIZE: int = 64
const AFK_MONSTER_COMPACT_BATCH_SIZE: int = 48
# Keep server-confirmed combat monsters present even across AOI boundaries.
const MONSTER_COMBAT_REVEAL_MSEC: int = 12000
# In dense AOI snapshots, rendering thousands of pixels beyond the camera
# instantiates costly monster scenes nobody can see. This is purely visual;
# network packets, combat data and targeting are never skipped.
const MONSTER_CROWD_ENTER_COUNT: int = 96
const MONSTER_CROWD_EXIT_COUNT: int = 64
const MONSTER_CROWD_DRAW_RADIUS: float = 1250.0

var active_entities: Dictionary = {}
var _last_skill_casts: Dictionary = {}
var _active_entity_types: Dictionary = {}

# Initial map sync can deliver dozens of entities back-to-back. Instantiating
# every scene immediately makes all _ready()/texture/animation work land in the
# same frame, causing a visible multi-second stall.
const SPAWN_BATCH_PER_FRAME: int = 1
const SPAWN_FRAME_BUDGET_USEC: int = 3500
const INITIAL_SPAWN_BATCH_PER_FRAME: int = 10
const INITIAL_SPAWN_FRAME_BUDGET_USEC: int = 9000
var _spawn_queue: Array[Dictionary] = []
var _queued_spawn_ids: Dictionary = {}
var _last_spawn_enqueue_usec: int = 0
var _runtime_monster_ids: Dictionary = {}
var _spawn_queue_paused: bool = false
var _initial_spawn_boost: bool = false
var _monster_spawn_templates: Dictionary = {}
var _deferred_monster_spawns: Dictionary = {}
var _monster_aoi_elapsed: float = 0.0
var _monster_aoi_last_observer: Variant = null
var _monster_crowd_visual_only: bool = false
var _monster_aoi_stationary_elapsed: float = 0.0
var _monster_aoi_full_scans: int = 0
var _monster_aoi_reused_scans: int = 0
var _ground_spawn_templates: Dictionary = {}
var _ground_visibility_index = GROUND_DROP_SPATIAL_INDEX.new()
var _deferred_ground_spawns: Dictionary = {}
var _ground_aoi_elapsed: float = 0.0
var _ground_protection_tick_elapsed: float = 0.0
# The drop bounce is decorative; keep only short-lived recent fatal positions.
# Avoid scanning every active monster for every dropped item in a kill burst.
const DROP_BOUNCE_DEATH_WINDOW_MS: int = 2500
var _recent_fatal_monsters: Dictionary = {}
var _ground_aoi_last_context: Variant = null
var _ground_aoi_stationary_elapsed: float = 0.0
var _ground_aoi_full_scans: int = 0
var _ground_aoi_reused_scans: int = 0
var _player_visibility_elapsed: float = 0.0
const PLAYER_VISIBILITY_REFRESH_INTERVAL: float = 0.5
# Display-only trimming is spread across frames; network processing is untouched.
const GROUND_VISUAL_TRIM_PER_FRAME: int = 12
var _ground_display_winners: Dictionary = {}
var _ground_display_cache_dirty: bool = true
var _ground_display_cache_center: Vector2 = Vector2.INF
var _ground_display_cache_radius: float = -1.0
var _ground_trim_queue: Array[int] = []
var _ground_trim_pending: Dictionary = {}


static func should_run_stationary_aoi_scan(
	context_changed: bool, elapsed_stationary: float, max_idle_interval: float
) -> bool:
	return context_changed or elapsed_stationary >= max_idle_interval
var _combat_reveal_until_msec: Dictionary = {}
var _pending_aoi_moves = PENDING_AOI_MOVES.new()

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
	_combat_reveal_until_msec.clear()
	_pending_aoi_moves.clear()
	_monster_aoi_elapsed = 0.0
	_monster_aoi_last_observer = null
	_monster_aoi_stationary_elapsed = 0.0
	_monster_aoi_full_scans = 0
	_monster_aoi_reused_scans = 0
	_ground_spawn_templates.clear()
	_deferred_ground_spawns.clear()
	_ground_visibility_index.clear()
	_ground_protection_tick_elapsed = 0.0
	_recent_fatal_monsters.clear()
	_ground_aoi_elapsed = 0.0
	_ground_aoi_last_context = null
	_ground_aoi_stationary_elapsed = 0.0
	_ground_aoi_full_scans = 0
	_ground_aoi_reused_scans = 0
	_ground_display_winners.clear()
	_ground_display_cache_dirty = true
	_ground_display_cache_center = Vector2.INF
	_ground_display_cache_radius = -1.0
	_ground_trim_queue.clear()
	_ground_trim_pending.clear()
	_player_visibility_elapsed = 0.0
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
	if GameManager and GameManager.has_signal("performance_profile_changed"):
		if not GameManager.performance_profile_changed.is_connected(_on_performance_profile_changed):
			GameManager.performance_profile_changed.connect(_on_performance_profile_changed)
	if NetworkManager.has_signal("skill_cast_received"):
		if not NetworkManager.skill_cast_received.is_connected(_on_skill_cast_received):
			NetworkManager.skill_cast_received.connect(_on_skill_cast_received)

	if NetworkManager.has_signal("entity_heal_received"):
		if not NetworkManager.entity_heal_received.is_connected(_on_entity_heal_received):
			NetworkManager.entity_heal_received.connect(_on_entity_heal_received)

func _is_local_combat_target(entity: Node2D) -> bool:
	var player = GameManager.current_player if GameManager else null
	return (
		is_instance_valid(player)
		and "combat_target" in player
		and is_instance_valid(player.combat_target)
		and player.combat_target == entity
	)

func _should_simplify_player(entity: Node2D) -> bool:
	if not GameManager or not is_instance_valid(entity):
		return false
	var profile: Dictionary = GameManager.get_performance_profile()
	# Simplified players stay simplified while targeted. Relationship/hostility
	# is shown by the built-in outline instead of loading the full paper doll.
	return bool(profile.get("simplify_other_players", false))

func _should_simplify_monster(_entity_id: int, entity: Node2D) -> bool:
	if not GameManager or not is_instance_valid(entity):
		return false
	var profile: Dictionary = GameManager.get_performance_profile()
	if not bool(profile.get("simplify_noncritical_monsters", false)):
		return false
	if entity.has_method("can_use_performance_simplification") and not entity.can_use_performance_simplification():
		return false
	# Combat targeting, attacks, hits and death are lifecycle/state changes,
	# not reasons to reload the original monster artwork.
	return true

func _should_show_player(entity: Node2D, profile: Dictionary) -> bool:
	var party: bool = entity.has_method("is_local_party_member") and bool(entity.is_local_party_member())
	var enemy: bool = entity.has_method("is_local_hostile_player") and bool(entity.is_local_hostile_player())
	return ClientPerformanceProfile.should_show_other_player(
		int(profile.get("other_players_visibility", ClientPerformanceProfile.PLAYER_SHOW_ALL)),
		party, enemy
	)

func _should_show_monster(entity: Node2D, profile: Dictionary) -> bool:
	var protected: bool = entity.has_method("can_use_performance_simplification") and not bool(entity.can_use_performance_simplification())
	return ClientPerformanceProfile.should_show_ordinary_monster(
		int(profile.get("ordinary_monsters_visibility", ClientPerformanceProfile.MONSTER_SHOW_ALL)),
		protected
	)

func _remember_fatal_monster(entity_id: int, entity: Node2D, delay: float) -> void:
	if entity_id <= 0 or not is_instance_valid(entity):
		return
	_recent_fatal_monsters[entity_id] = {
		"pos": entity.global_position,
		"delay": maxf(0.0, delay),
		"expires": Time.get_ticks_msec() + DROP_BOUNCE_DEATH_WINDOW_MS,
	}

func _recent_drop_bounce_delay(spawn_pos: Vector2) -> float:
	var now_ms: int = Time.get_ticks_msec()
	for raw_id in _recent_fatal_monsters.keys():
		var death: Dictionary = _recent_fatal_monsters[raw_id]
		if int(death["expires"]) <= now_ms:
			_recent_fatal_monsters.erase(raw_id)
			continue
		if (death["pos"] as Vector2).distance_squared_to(spawn_pos) < 10000.0:
			return float(death["delay"])
	return -1.0

func _ground_drop_mode() -> int:
	if not GameManager:
		return ClientPerformanceProfile.GROUND_DROPS_ALL
	return ClientPerformanceProfile.sanitize_ground_drop_display_mode(
		int(GameManager.current_performance_profile.get(
			"ground_drop_display_mode", ClientPerformanceProfile.GROUND_DROPS_ALL
		))
	)

func _should_display_ground_drop(entity_id: int, _data: Dictionary, center: Vector2, radius: float) -> bool:
	var mode: int = _ground_drop_mode()
	if mode == ClientPerformanceProfile.GROUND_DROPS_ALL:
		return true
	if mode == ClientPerformanceProfile.GROUND_DROPS_HIDDEN:
		return false
	# No nested scan per ground-item node. Recompute winners once for the
	# observer AOI or newly arrived/removed drop, then O(1) membership checks.
	if _ground_display_cache_dirty or _ground_display_cache_radius != radius or _ground_display_cache_center.distance_squared_to(center) > 6400.0:
		_ground_display_winners = _ground_visibility_index.find_nearest(center, radius, 5)
		_ground_display_cache_center = center
		_ground_display_cache_radius = radius
		_ground_display_cache_dirty = false
	return _ground_display_winners.has(entity_id)

func _schedule_ground_visual_trim(entity_id: int, drop: Node2D) -> void:
	# Hiding is immediate, freeing is budgeted: no large same-frame teardown.
	if _ground_trim_pending.has(entity_id) or not is_instance_valid(drop):
		return
	if bool(drop.get_meta("is_requesting", false)):
		return
	drop.visible = false
	if drop is CollisionObject2D:
		(drop as CollisionObject2D).input_pickable = false
	_unregister_entity_hover(drop)
	_ground_trim_pending[entity_id] = true
	_ground_trim_queue.append(entity_id)

func _trim_ground_visuals_budgeted() -> void:
	var aoi: Dictionary = _ground_aoi_context()
	var checked: int = mini(GROUND_VISUAL_TRIM_PER_FRAME, _ground_trim_queue.size())
	for i in range(checked):
		var entity_id: int = _ground_trim_queue.pop_front()
		_ground_trim_pending.erase(entity_id)
		var drop: Node2D = get_active_entity(entity_id)
		if not is_instance_valid(drop) or not _ground_spawn_templates.has(entity_id):
			continue
		if bool(drop.get_meta("is_requesting", false)):
			continue
		var allowed: bool = aoi["center"] != null and _should_display_ground_drop(
			entity_id, _ground_spawn_templates[entity_id], aoi["center"], float(aoi["enter"])
		)
		if allowed:
			drop.visible = true
			if drop is CollisionObject2D:
				(drop as CollisionObject2D).input_pickable = true
			continue
		_deferred_ground_spawns[entity_id] = _ground_spawn_templates[entity_id]
		drop.queue_free()
		active_entities.erase(entity_id)
		_active_entity_types.erase(entity_id)

func _refresh_ground_drop_visibility() -> void:
	_ground_display_cache_dirty = true
	_ground_aoi_last_context = null
	_ground_aoi_elapsed = GROUND_AOI_UPDATE_INTERVAL
	var aoi: Dictionary = _ground_aoi_context()
	if aoi["center"] == null:
		return
	var center: Vector2 = aoi["center"]
	var enter_radius: float = float(aoi["enter"])
	var mode: int = _ground_drop_mode()
	for raw_id in _active_entity_types.keys():
		var entity_id: int = int(raw_id)
		if int(_active_entity_types.get(entity_id, -1)) != 3:
			continue
		var drop: Node2D = get_active_entity(entity_id)
		if not is_instance_valid(drop):
			continue
		if mode == ClientPerformanceProfile.GROUND_DROPS_ALL or _should_display_ground_drop(
			entity_id, _ground_spawn_templates.get(entity_id, {}), center, enter_radius
		):
			if not _ground_trim_pending.has(entity_id):
				drop.visible = true
				if drop is CollisionObject2D:
					(drop as CollisionObject2D).input_pickable = true
		else:
			_schedule_ground_visual_trim(entity_id, drop)

func _apply_performance_visual_to_entity(entity_id: int, entity_type: int, entity: Node2D) -> void:
	if not is_instance_valid(entity) or not entity.has_method("set_performance_simplified"):
		return
	var profile: Dictionary = GameManager.get_performance_profile() if GameManager else {}
	match entity_type:
		0:
			var show_player: bool = _should_show_player(entity, profile)
			entity.set_performance_simplified(
				_should_simplify_player(entity) or not show_player,
				int(profile.get(
					"player_simplify_style",
					ClientPerformanceProfile.SIMPLIFY_PIXEL_A
				))
			)
			entity.visible = show_player
		1:
			var show_monster: bool = _should_show_monster(entity, profile)
			var simplify_monster := _should_simplify_monster(entity_id, entity) or not show_monster
			entity.set_performance_simplified(
				simplify_monster,
				int(profile.get(
					"monster_simplify_style",
					ClientPerformanceProfile.SIMPLIFY_PIXEL_A
				))
			)
			entity.visible = show_monster

func _refresh_performance_visuals() -> void:
	for raw_id in active_entities.keys():
		var entity_id := int(raw_id)
		var entity := get_active_entity(entity_id)
		if not is_instance_valid(entity):
			continue
		_apply_performance_visual_to_entity(
			entity_id,
			int(_active_entity_types.get(entity_id, -1)),
			entity
		)

func _on_performance_profile_changed(_profile: Dictionary) -> void:
	_refresh_performance_visuals()
	_player_visibility_elapsed = 0.0
	# Changes to display density should immediately reconsider deferred drops.
	_ground_aoi_last_context = null
	# Never alter packet subscriptions; re-evaluate scene nodes immediately.
	_monster_aoi_last_observer = null
	_ground_aoi_last_context = null
	_monster_aoi_elapsed = MONSTER_AOI_UPDATE_INTERVAL
	_ground_aoi_elapsed = GROUND_AOI_UPDATE_INTERVAL
	_refresh_ground_drop_visibility()

func get_aoi_active_monster_count() -> int:
	var count: int = 0
	for entity_type in _active_entity_types.values():
		if int(entity_type) == 1:
			count += 1
	return count

func get_aoi_deferred_monster_count() -> int:
	return _deferred_monster_spawns.size()

func get_spawn_queue_remaining_count() -> int:
	return _spawn_queue.size()


func _unhandled_input(event: InputEvent) -> void :
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if GameManager.current_hud and is_instance_valid(GameManager.current_hud):
			var hud = GameManager.current_hud
			if hud.get("_next_dialog_node") != "" or (
				hud.has_method("has_legacy_dialog_line_waiting")
				and hud.has_legacy_dialog_line_waiting()
			):
				hud.request_next_dialog_sentence()
				get_viewport().set_input_as_handled()

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

static func resolve_visual_aoi_radii(profile: Dictionary, is_monster: bool, camera_radius: float) -> Vector2:
	# Reduce offscreen render nodes, not authoritative client data or gameplay.
	var prefix: String = "monster" if is_monster else "ground"
	var fallback_enter: float = MONSTER_AOI_ENTER_RADIUS if is_monster else GROUND_AOI_ENTER_RADIUS
	var fallback_exit: float = MONSTER_AOI_EXIT_RADIUS if is_monster else GROUND_AOI_EXIT_RADIUS
	var enter_radius: float = maxf(
		float(profile.get(prefix + "_aoi_enter_radius", fallback_enter)), maxf(0.0, camera_radius)
	)
	var exit_radius: float = maxf(
		float(profile.get(prefix + "_aoi_exit_radius", fallback_exit)), enter_radius + 400.0
	)
	return Vector2(enter_radius, exit_radius)


func _monster_aoi_radii() -> Vector2:
	var profile: Dictionary = GameManager.current_performance_profile if GameManager else {}
	var required_radius: float = 0.0
	var viewport: Viewport = get_viewport()
	var camera: Camera2D = viewport.get_camera_2d() if is_instance_valid(viewport) else null
	if is_instance_valid(camera):
		var view_size: Vector2 = viewport.get_visible_rect().size
		var zoom: float = maxf(0.05, minf(absf(camera.zoom.x), absf(camera.zoom.y)))
		required_radius = view_size.length() * 0.5 / zoom + 360.0
		var player_pos: Variant = _get_local_player_position()
		if player_pos != null:
			required_radius += camera.global_position.distance_to(player_pos as Vector2)
	var radii: Vector2 = resolve_visual_aoi_radii(profile, true, required_radius)
	# Adaptive display distance is enabled only for the "auto" visual choice
	# in balanced/low profiles. Manual normal/quality modes retain the exact
	# user-selected AOI, regardless of monster density.
	var auto_visual: bool = (
		int(profile.get("simplify_monsters_mode", ClientPerformanceProfile.SIMPLIFY_AUTO)) == ClientPerformanceProfile.SIMPLIFY_AUTO
		and int(profile.get("load_mode", ClientPerformanceProfile.LOAD_BALANCED)) >= ClientPerformanceProfile.LOAD_BALANCED
	)
	if not auto_visual:
		_monster_crowd_visual_only = false
	else:
		var known_monsters: int = _monster_spawn_templates.size()
		if known_monsters >= MONSTER_CROWD_ENTER_COUNT:
			_monster_crowd_visual_only = true
		elif known_monsters <= MONSTER_CROWD_EXIT_COUNT:
			_monster_crowd_visual_only = false
	if not _monster_crowd_visual_only:
		return radii
	var enter_radius: float = minf(
		radii.x, maxf(required_radius, MONSTER_CROWD_DRAW_RADIUS)
	)
	return Vector2(enter_radius, minf(radii.y, enter_radius + 400.0))


static func is_ground_inside_aoi(data: Dictionary, center: Vector2, radius: float) -> bool:
	var location := Vector2(float(data.get("LocX", 0.0)), float(data.get("LocY", 0.0)))
	return location.distance_squared_to(center) <= radius * radius


func _ground_aoi_context() -> Dictionary:
	var center: Variant = _get_local_player_position()
	var required_radius: float = 0.0
	# Preserve original drop packets/timestamps while deferring visuals.
	var viewport: Viewport = get_viewport()
	var camera: Camera2D = viewport.get_camera_2d() if is_instance_valid(viewport) else null
	if is_instance_valid(camera):
		center = camera.global_position
		var view_size: Vector2 = viewport.get_visible_rect().size
		var zoom: float = maxf(0.05, minf(absf(camera.zoom.x), absf(camera.zoom.y)))
		required_radius = view_size.length() * 0.5 / zoom + 420.0
	var profile: Dictionary = GameManager.current_performance_profile if GameManager else {}
	var radii: Vector2 = resolve_visual_aoi_radii(profile, false, required_radius)
	return {"center": center, "enter": radii.x, "exit": radii.y}


func get_ground_aoi_stats() -> Dictionary:
	return {
		"known": _ground_spawn_templates.size(),
		"deferred": _deferred_ground_spawns.size(),
		"display_mode": _ground_drop_mode(),
		"selected": _ground_display_winners.size() if _ground_drop_mode() == ClientPerformanceProfile.GROUND_DROPS_LIMIT_DUPLICATES else -1,
		"pending_trim": _ground_trim_queue.size(),
		"ground_index_records": _ground_visibility_index.records.size(),
		"ground_index_cells": _ground_visibility_index.cells.size(),
		"ground_index_last_visited": _ground_visibility_index.last_visited_items,
		"ground_index_rebuilds": _ground_visibility_index.rebuild_count,
		"ground_full_scans": _ground_aoi_full_scans,
		"ground_skipped_scans": _ground_aoi_reused_scans,
		"monster_full_scans": _monster_aoi_full_scans,
		"monster_skipped_scans": _monster_aoi_reused_scans,
		"monster_enter_radius": _monster_aoi_radii().x,
		"ground_enter_radius": float(_ground_aoi_context().get("enter", GROUND_AOI_ENTER_RADIUS))
	}


func _is_afk_ground_data_only() -> bool:
	return GameManager and GameManager.is_afk_mode_active()


func _is_afk_monster_data_only() -> bool:
	return GameManager and GameManager.is_afk_mode_active()


func compact_monsters_for_afk_batched(
	batch_size: int = AFK_MONSTER_COMPACT_BATCH_SIZE
) -> Dictionary:
	# AFK keeps authoritative monster packets as Dictionaries and removes the
	# heavyweight CharacterBody2D presentation scenes. Keep at most the
	# player's current manual combat target so an in-flight manual attack is
	# not interrupted. Server-driven automation does not depend on these Nodes.
	var monster_ids: Array[int] = []
	for raw_id in _active_entity_types.keys():
		var entity_id: int = int(raw_id)
		if int(_active_entity_types.get(entity_id, -1)) == 1:
			monster_ids.append(entity_id)

	var local_player = GameManager.current_player if GameManager else null
	var released_nodes: int = 0
	var preserved_combat_target: int = 0
	var stale_nodes: int = 0
	var since_yield: int = 0
	var safe_batch: int = maxi(1, batch_size)

	for entity_id in monster_ids:
		var monster: Node2D = get_active_entity(entity_id)
		var cached: Dictionary = _monster_spawn_templates.get(entity_id, {})
		if not is_instance_valid(monster):
			if not cached.is_empty():
				_deferred_monster_spawns[entity_id] = cached
			active_entities.erase(entity_id)
			_active_entity_types.erase(entity_id)
			_runtime_monster_ids.erase(entity_id)
			stale_nodes += 1
			continue

		var is_current_manual_target: bool = (
			is_instance_valid(local_player)
			and "combat_target" in local_player
			and is_instance_valid(local_player.combat_target)
			and local_player.combat_target == monster
		)
		if is_current_manual_target:
			preserved_combat_target += 1
			continue

		if not cached.is_empty():
			# Prefer the latest authoritative template position. Only fill
			# missing state fields from the scene before destroying it.
			var compacted: Dictionary = cached.duplicate()
			if "current_state" in monster:
				compacted["State"] = str(monster.current_state)
			if "current_dir" in monster:
				compacted["Dir"] = str(monster.current_dir)
			if "current_hp" in monster:
				var extra: Dictionary = (compacted.get("ExtraData", {}) as Dictionary).duplicate()
				extra["CurrentHP"] = int(monster.current_hp)
				if "max_hp" in monster:
					extra["MaxHP"] = int(monster.max_hp)
				compacted["ExtraData"] = extra
			_monster_spawn_templates[entity_id] = compacted
			_deferred_monster_spawns[entity_id] = compacted

		_unregister_entity_hover(monster)
		monster.queue_free()
		active_entities.erase(entity_id)
		_active_entity_types.erase(entity_id)
		_runtime_monster_ids.erase(entity_id)
		released_nodes += 1
		since_yield += 1
		if since_yield >= safe_batch:
			since_yield = 0
			await get_tree().process_frame

	# A monster still waiting in the visual spawn queue is already pure data;
	# remove it from the queue so AFK cannot instantiate it later.
	var retained_queue: Array[Dictionary] = []
	var queued_to_data: int = 0
	for queued_data in _spawn_queue:
		if int(queued_data.get("EntityType", -1)) != 1:
			retained_queue.append(queued_data)
			continue
		var entity_id: int = int(queued_data.get("EntityID", 0))
		_queued_spawn_ids.erase(entity_id)
		var cached: Dictionary = _monster_spawn_templates.get(entity_id, queued_data)
		if not cached.is_empty():
			_deferred_monster_spawns[entity_id] = cached
		queued_to_data += 1
	_spawn_queue = retained_queue

	if released_nodes > 0 or queued_to_data > 0:
		await get_tree().process_frame

	return {
		"released_nodes": released_nodes,
		"preserved_combat_target": preserved_combat_target,
		"stale_nodes": stale_nodes,
		"queued_to_data": queued_to_data,
		"known": _monster_spawn_templates.size(),
		"deferred": _deferred_monster_spawns.size(),
		"active_nodes": get_aoi_active_monster_count(),
	}


func restore_monsters_after_afk() -> Dictionary:
	# Resume presentation only for monsters inside the current visual AOI.
	# The existing one-per-frame spawn queue prevents restore from causing a
	# large frame spike; packets received during AFK already updated templates.
	_monster_aoi_last_observer = null
	_monster_aoi_stationary_elapsed = 0.0
	var player_pos = _get_local_player_position()
	if player_pos == null:
		return {
			"queued": 0,
			"known": _monster_spawn_templates.size(),
			"deferred": _deferred_monster_spawns.size(),
		}
	var local_pos: Vector2 = player_pos as Vector2
	var enter_radius_sq: float = _monster_aoi_radii().x * _monster_aoi_radii().x
	var queued: int = 0
	for raw_id in _deferred_monster_spawns.keys():
		var entity_id: int = int(raw_id)
		var cached = _deferred_monster_spawns.get(entity_id, null)
		if not cached is Dictionary:
			continue
		if (
			local_pos.distance_squared_to(_monster_packet_position(cached)) <= enter_radius_sq
			or _is_combat_monster_revealed(entity_id)
		):
			_queue_cached_monster_spawn(entity_id)
			queued += 1
	return {
		"queued": queued,
		"known": _monster_spawn_templates.size(),
		"deferred": _deferred_monster_spawns.size(),
	}


func compact_ground_items_for_afk_batched(
	batch_size: int = AFK_GROUND_COMPACT_BATCH_SIZE
) -> Dictionary:
	# Ground drops are presentation-only on the AFK screen. Keep their
	# authoritative SpawnEntity dictionaries so Despawn/new snapshots continue
	# to work, but release Sprite/Label/Collision/Timer nodes. A pickup already
	# in progress is intentionally preserved until its request resolves.
	var ground_ids: Array[int] = []
	for raw_id in _active_entity_types.keys():
		var entity_id: int = int(raw_id)
		if int(_active_entity_types.get(entity_id, -1)) == 3:
			ground_ids.append(entity_id)

	var released_nodes: int = 0
	var preserved_requesting: int = 0
	var stale_nodes: int = 0
	var since_yield: int = 0
	var safe_batch: int = maxi(1, batch_size)
	for entity_id in ground_ids:
		var drop: Node2D = get_active_entity(entity_id)
		var cached: Dictionary = _ground_spawn_templates.get(entity_id, {})
		if not is_instance_valid(drop):
			if not cached.is_empty():
				_deferred_ground_spawns[entity_id] = cached
			active_entities.erase(entity_id)
			_active_entity_types.erase(entity_id)
			stale_nodes += 1
			continue
		if bool(drop.get_meta("is_requesting", false)):
			preserved_requesting += 1
			continue
		if not cached.is_empty():
			_deferred_ground_spawns[entity_id] = cached
		_unregister_entity_hover(drop)
		drop.queue_free()
		active_entities.erase(entity_id)
		_active_entity_types.erase(entity_id)
		released_nodes += 1
		since_yield += 1
		if since_yield >= safe_batch:
			since_yield = 0
			await get_tree().process_frame

	var retained_queue: Array[Dictionary] = []
	var queued_to_data: int = 0
	for queued_data in _spawn_queue:
		if int(queued_data.get("EntityType", -1)) != 3:
			retained_queue.append(queued_data)
			continue
		var entity_id: int = int(queued_data.get("EntityID", 0))
		_queued_spawn_ids.erase(entity_id)
		var cached: Dictionary = _ground_spawn_templates.get(entity_id, queued_data)
		if not cached.is_empty():
			_deferred_ground_spawns[entity_id] = cached
		queued_to_data += 1
	_spawn_queue = retained_queue

	# Let the last batch's queue_free calls settle while the window is still at
	# the normal foreground cadence. NetworkManager and fixed physics continue.
	if released_nodes > 0 or queued_to_data > 0:
		await get_tree().process_frame
	return {
		"released_nodes": released_nodes,
		"preserved_requesting": preserved_requesting,
		"stale_nodes": stale_nodes,
		"queued_to_data": queued_to_data,
		"known": _ground_spawn_templates.size(),
		"deferred": _deferred_ground_spawns.size(),
	}


func restore_ground_items_after_afk() -> Dictionary:
	# Do not block AFK restore rebuilding hundreds of drop scenes. Re-enable
	# only current-AOI packets in the normal one-entity-per-frame spawn queue.
	_ground_aoi_last_context = null
	_ground_aoi_stationary_elapsed = 0.0
	var aoi: Dictionary = _ground_aoi_context()
	if aoi["center"] == null:
		return {
			"queued": 0,
			"known": _ground_spawn_templates.size(),
			"deferred": _deferred_ground_spawns.size(),
		}
	var center: Vector2 = aoi["center"] as Vector2
	var enter_radius: float = float(aoi["enter"])
	var queued: int = 0
	for raw_id in _deferred_ground_spawns.keys():
		var entity_id: int = int(raw_id)
		var cached: Dictionary = _deferred_ground_spawns.get(entity_id, {})
		if cached.is_empty():
			continue
		if not is_ground_inside_aoi(cached, center, enter_radius):
			continue
		if not _should_display_ground_drop(entity_id, cached, center, enter_radius):
			continue
		_queue_deferred_ground_spawn(entity_id)
		queued += 1
	return {
		"queued": queued,
		"known": _ground_spawn_templates.size(),
		"deferred": _deferred_ground_spawns.size(),
	}


func _ground_spawn_data_at_current_time(entity_id: int) -> Dictionary:
	var cached: Dictionary = _ground_spawn_templates.get(entity_id, {})
	if cached.is_empty():
		return {}
	var data: Dictionary = cached.duplicate()
	var extra: Dictionary = (cached.get("ExtraData", {}) as Dictionary).duplicate()
	var cached_at: int = int(cached.get("_aoi_cached_at", Time.get_ticks_msec()))
	var age: float = maxf(0.0, float(Time.get_ticks_msec() - cached_at) / 1000.0)
	for key: String in ["RemainingTime", "ProtectTimeSeconds"]:
		if extra.has(key):
			extra[key] = maxf(0.0, float(extra[key]) - age)
	data["ExtraData"] = extra
	# Reappearing old items must not replay the fresh-monster-drop bounce.
	data["IsSnapshot"] = true
	return data


func _is_monster_inside_aoi(data: Dictionary, radius: float) -> bool:
	var player_pos = _get_local_player_position()
	if player_pos == null:
		return true
	return (player_pos as Vector2).distance_squared_to(_monster_packet_position(data)) <= radius * radius


func _is_combat_monster_revealed(entity_id: int) -> bool:
	var expiry: int = int(_combat_reveal_until_msec.get(entity_id, 0))
	if expiry <= 0:
		return false
	if Time.get_ticks_msec() < expiry:
		return true
	_combat_reveal_until_msec.erase(entity_id)
	return false


func _can_spawn_monster(data: Dictionary, radius: float) -> bool:
	# Remote bots fighting outside the visible area must not force hundreds
	# of costly monster scene nodes into the local player's viewport.
	if _monster_crowd_visual_only:
		return _is_monster_inside_aoi(data, radius)
	return _is_combat_monster_revealed(int(data.get("EntityID", 0))) or _is_monster_inside_aoi(data, radius)


func _reveal_combat_monster(entity_id: int, reason: String) -> void:
	# Never invent monsters: only restore a server-known SpawnEntity on
	# the current map, preserving its appearance and latest coordinates.
	if entity_id <= 0 or not _monster_spawn_templates.has(entity_id):
		return
	var already_revealed: bool = _is_combat_monster_revealed(entity_id)
	_combat_reveal_until_msec[entity_id] = Time.get_ticks_msec() + MONSTER_COMBAT_REVEAL_MSEC
	var active_monster := get_active_entity(entity_id)
	if is_instance_valid(active_monster):
		# Reveal is an AOI/lifecycle concern only. Re-apply the current
		# visual policy instead of promoting attackers or selected targets
		# back to their original artwork.
		_apply_performance_visual_to_entity(
			entity_id,
			1,
			active_monster
		)
		return
	if _is_afk_monster_data_only():
		_deferred_monster_spawns[entity_id] = _monster_spawn_templates[entity_id]
		_remove_queued_spawn(entity_id)
		return
	if _monster_crowd_visual_only and not _is_monster_inside_aoi(
		_monster_spawn_templates[entity_id], _monster_aoi_radii().x
	):
		_deferred_monster_spawns[entity_id] = _monster_spawn_templates[entity_id]
		_remove_queued_spawn(entity_id)
		return
	if not _queued_spawn_ids.has(entity_id):
		_deferred_monster_spawns[entity_id] = _monster_spawn_templates[entity_id]
		_queue_cached_monster_spawn(entity_id)
	elif not already_revealed:
		# Promote an already-queued monster only once per combat encounter.
		# Reordering for every damage packet would create unnecessary work.
		for index: int in range(_spawn_queue.size()):
			if int(_spawn_queue[index].get("EntityID", 0)) == entity_id:
				var queued_data: Dictionary = _spawn_queue[index]
				_spawn_queue.remove_at(index)
				_enqueue_spawn_by_priority(queued_data)
				break
	if OS.is_debug_build() and not already_revealed:
		print("[MONSTER_COMBAT_REVEAL] id=%d reason=%s queued=%s" % [
			entity_id, reason, str(_queued_spawn_ids.has(entity_id))
		])

func _defer_monster_spawn(data: Dictionary) -> void :
	var entity_id: int = int(data.get("EntityID", 0))
	if entity_id <= 0:
		return
	# Spawn packets were already copied on receipt. Preserve immutable nested
	# metadata (ExtraData) by reference instead of recursively cloning it again.
	var cached: Dictionary = data.duplicate()
	_monster_spawn_templates[entity_id] = cached
	_deferred_monster_spawns[entity_id] = cached
	# Deferral must cancel any stale queue entry as well as its ID marker.
	# Otherwise repeated WorldReady snapshots can create phantom / duplicate
	# monsters that later jump when authoritative MoveEntity arrives.
	if _queued_spawn_ids.has(entity_id):
		_remove_queued_spawn(entity_id)

func _remove_queued_spawn(entity_id: int) -> void :
	_queued_spawn_ids.erase(entity_id)
	for index: int in range(_spawn_queue.size() - 1, -1, -1):
		if int(_spawn_queue[index].get("EntityID", 0)) == entity_id:
			_spawn_queue.remove_at(index)

func _spawn_priority(data: Dictionary, player_pos: Variant) -> float:
	var entity_type: int = int(data.get("EntityType", -1))
	# A scene spawn may take milliseconds. Under many-player combat and
	# hundreds of dropped items, players must never sit behind drop visuals.
	# All packets still enter authoritative state immediately; this priority
	# only orders expensive presentation scene instantiation.
	if entity_type == 0:
		return -3000000000.0
	if entity_type == 1 and _is_combat_monster_revealed(int(data.get("EntityID", 0))):
		return -2000000000.0
	if entity_type == 4:
		return -1500000000.0
	if entity_type == 1:
		var distance: float = 0.0
		if player_pos != null:
			distance = (player_pos as Vector2).distance_squared_to(_monster_packet_position(data))
		return -1000000000.0 + distance
	if entity_type == 2:
		return -500000000.0
	# Ground drops are cached with their authoritative expiration / owner
	# data when the packet arrives. Drawing their sprites can wait behind
	# players and monsters without delaying pickup or changing game logic.
	if entity_type == 3:
		return 0.0
	return 100000000.0

func _enqueue_spawn_by_priority(data: Dictionary) -> void :
	# The queue remains sorted by priority. The former linear scan called
	# _spawn_priority for every queued entry whenever a packet arrived:
	# hundreds of map-snapshot drops made this quadratic CPU work.
	# Binary insertion preserves stable ordering for equal priorities.
	var player_pos: Variant = _get_local_player_position()
	var priority: float = _spawn_priority(data, player_pos)
	var low: int = 0
	var high: int = _spawn_queue.size()
	while low < high:
		var mid: int = (low + high) / 2
		if priority < _spawn_priority(_spawn_queue[mid], player_pos):
			high = mid
		else:
			low = mid + 1
	_spawn_queue.insert(low, data)

func _get_current_queued_spawn_data(data: Dictionary) -> Dictionary:
	# MoveEntity can arrive after SpawnEntity but before a player's/pet's
	# queued scene exists. Apply the newest buffered move at instantiate time.
	# For monsters, their canonical template already tracks every move.
	if int(data.get("EntityType", -1)) != 1:
		var entity_id: int = int(data.get("EntityID", 0))
		var buffered: Dictionary = _pending_aoi_moves.consume_spawn_move(
			entity_id, Time.get_ticks_msec()
		)
		if buffered.is_empty():
			return data
		var latest: Dictionary = data.duplicate()
		latest["LocX"] = buffered.get("X", latest.get("LocX", 0.0))
		latest["LocY"] = buffered.get("Y", latest.get("LocY", 0.0))
		if str(buffered.get("Dir", "")) != "":
			latest["Dir"] = buffered["Dir"]
		return latest
	var entity_id: int = int(data.get("EntityID", 0))
	var latest = _monster_spawn_templates.get(entity_id, null)
	if latest is Dictionary:
		return (latest as Dictionary).duplicate()
	return data


func set_initial_spawn_boost(enabled: bool) -> void :
	_initial_spawn_boost = enabled

func _queue_cached_monster_spawn(entity_id: int) -> void :
	if get_active_entity(entity_id) or _queued_spawn_ids.has(entity_id):
		return
	var cached = _deferred_monster_spawns.get(entity_id, null)
	if not cached is Dictionary:
		return
	_deferred_monster_spawns.erase(entity_id)
	_queued_spawn_ids[entity_id] = true
	_enqueue_spawn_by_priority((cached as Dictionary).duplicate())
	_last_spawn_enqueue_usec = Time.get_ticks_usec()

func _unload_monster_for_aoi(entity_id: int, monster: Node2D) -> void :
	var cached = _monster_spawn_templates.get(entity_id, null)
	if cached is Dictionary:
		# AOI removal must preserve the last SERVER-approved MoveEntity position.
		# The CharacterBody is visually interpolated and can be dozens of pixels
		# behind the server. Copying that body position into the spawn template
		# causes a visible teleport when the server sends its next correction.
		_deferred_monster_spawns[entity_id] = cached
	_unregister_entity_hover(monster)
	monster.queue_free()
	active_entities.erase(entity_id)
	_active_entity_types.erase(entity_id)
	_runtime_monster_ids.erase(entity_id)

func _update_monster_aoi(delta: float) -> void :
	if _is_afk_monster_data_only():
		_monster_aoi_elapsed = 0.0
		_monster_aoi_last_observer = null
		return
	_monster_aoi_elapsed += delta
	if _monster_aoi_elapsed < MONSTER_AOI_UPDATE_INTERVAL:
		return
	var check_elapsed: float = _monster_aoi_elapsed
	_monster_aoi_elapsed = 0.0
	var player_pos = _get_local_player_position()
	if player_pos == null:
		_monster_aoi_last_observer = null
		return
	var local_pos: Vector2 = player_pos as Vector2
	var radii: Vector2 = _monster_aoi_radii()
	var observer: Dictionary = {"position": local_pos, "enter": radii.x, "exit": radii.y}
	var observer_changed: bool = _monster_aoi_last_observer == null or _monster_aoi_last_observer != observer
	if not observer_changed:
		_monster_aoi_stationary_elapsed += check_elapsed
	else:
		_monster_aoi_stationary_elapsed = 0.0
	if not should_run_stationary_aoi_scan(
		observer_changed, _monster_aoi_stationary_elapsed, MONSTER_AOI_STATIONARY_SCAN_INTERVAL
	):
		_monster_aoi_reused_scans += 1
		return
	_monster_aoi_last_observer = observer
	_monster_aoi_stationary_elapsed = 0.0
	_monster_aoi_full_scans += 1
	var exit_radius_sq: float = radii.y * radii.y
	var enter_radius_sq: float = radii.x * radii.x
	for raw_id in _active_entity_types.keys():
		var entity_id: int = int(raw_id)
		if int(_active_entity_types.get(entity_id, -1)) != 1:
			continue
		var monster := get_active_entity(entity_id)
		if is_instance_valid(monster) and local_pos.distance_squared_to(monster.global_position) > exit_radius_sq:
			# Active local targets always stay visible. Under crowd pressure,
			# remote combat elsewhere must not pin hundreds of monster nodes.
			if (_monster_crowd_visual_only or not _is_combat_monster_revealed(entity_id)) and not _is_local_combat_target(monster):
				_unload_monster_for_aoi(entity_id, monster)
	for raw_id in _deferred_monster_spawns.keys():
		var entity_id: int = int(raw_id)
		var cached = _deferred_monster_spawns.get(entity_id, null)
		if cached is Dictionary and (
			local_pos.distance_squared_to(_monster_packet_position(cached)) <= enter_radius_sq
			or _is_combat_monster_revealed(entity_id)
		):
			_queue_cached_monster_spawn(entity_id)

func _queue_deferred_ground_spawn(entity_id: int) -> void:
	if _queued_spawn_ids.has(entity_id) or get_active_entity(entity_id):
		return
	var snapshot: Dictionary = _ground_spawn_data_at_current_time(entity_id)
	if snapshot.is_empty():
		_deferred_ground_spawns.erase(entity_id)
		return
	_queued_spawn_ids[entity_id] = true
	_enqueue_spawn_by_priority(snapshot)
	_deferred_ground_spawns.erase(entity_id)
	_last_spawn_enqueue_usec = Time.get_ticks_usec()


func _update_ground_aoi(delta: float) -> void:
	if _is_afk_ground_data_only():
		_ground_display_cache_dirty = true
		_ground_aoi_elapsed = 0.0
		_ground_aoi_last_context = null
		return
	# Hidden drops still have authoritative cached data for pickup, owner and
	# expiry. With thousands of drops, scanning each cached entry every 250ms
	# wastes CPU even though zero Sprite/Label nodes should be displayed.
	# _on_performance_profile_changed() handles visual teardown immediately
	# and resets the context when the user re-enables ground display.
	if _ground_drop_mode() == ClientPerformanceProfile.GROUND_DROPS_HIDDEN:
		_ground_aoi_elapsed = 0.0
		_ground_aoi_last_context = null
		return
	_ground_aoi_elapsed += delta
	if _ground_aoi_elapsed < GROUND_AOI_UPDATE_INTERVAL:
		return
	var check_elapsed: float = _ground_aoi_elapsed
	_ground_aoi_elapsed = 0.0
	var aoi: Dictionary = _ground_aoi_context()
	if aoi["center"] == null:
		_ground_aoi_last_context = null
		return
	var center: Vector2 = aoi["center"] as Vector2
	var context_changed: bool = (
		_ground_aoi_last_context == null
		or _ground_aoi_last_context["center"] != center
		or _ground_aoi_last_context["enter"] != aoi["enter"]
		or _ground_aoi_last_context["exit"] != aoi["exit"]
	)
	if not context_changed:
		_ground_aoi_stationary_elapsed += check_elapsed
	else:
		_ground_aoi_stationary_elapsed = 0.0
	if not should_run_stationary_aoi_scan(
		context_changed, _ground_aoi_stationary_elapsed, GROUND_AOI_STATIONARY_SCAN_INTERVAL
	):
		_ground_aoi_reused_scans += 1
		return
	_ground_aoi_last_context = {"center": center, "enter": aoi["enter"], "exit": aoi["exit"]}
	_ground_aoi_stationary_elapsed = 0.0
	_ground_aoi_full_scans += 1
	var enter_radius: float = float(aoi["enter"])
	var exit_radius_sq: float = float(aoi["exit"]) * float(aoi["exit"])
	for raw_id in _active_entity_types.keys():
		var entity_id: int = int(raw_id)
		if int(_active_entity_types.get(entity_id, -1)) != 3:
			continue
		var drop: Node2D = get_active_entity(entity_id)
		if not is_instance_valid(drop) or not _ground_spawn_templates.has(entity_id):
			continue
		var still_in_aoi: bool = center.distance_squared_to(drop.global_position) <= exit_radius_sq
		var cap_allows: bool = _should_display_ground_drop(
			entity_id, _ground_spawn_templates[entity_id], center, enter_radius
		)
		if still_in_aoi and cap_allows:
			continue
		if not cap_allows:
			_schedule_ground_visual_trim(entity_id, drop)
			continue
		# The player may be walking to pick up an item outside the screen.
		# Keep the target alive until its pending pickup request resolves.
		if bool(drop.get_meta("is_requesting", false)):
			continue
		_unregister_entity_hover(drop)
		drop.queue_free()
		active_entities.erase(entity_id)
		_active_entity_types.erase(entity_id)
		_deferred_ground_spawns[entity_id] = _ground_spawn_templates[entity_id]
	for raw_id in _deferred_ground_spawns.keys():
		var entity_id: int = int(raw_id)
		var cached: Dictionary = _deferred_ground_spawns.get(entity_id, {})
		if is_ground_inside_aoi(cached, center, enter_radius) and _should_display_ground_drop(entity_id, cached, center, enter_radius):
			_queue_deferred_ground_spawn(entity_id)


func _on_entity_spawned(data: Dictionary) -> void :
	var entity_id := int(data.get("EntityID", 0))
	if entity_id <= 0:
		return
	# The first MoveEntity can precede SpawnEntity when two server tasks
	# publish concurrently. Use its latest coordinates without allocating
	# a visual object before the server confirmed the entity exists.
	var buffered_move: Dictionary = _pending_aoi_moves.consume_spawn_move(
		entity_id, Time.get_ticks_msec()
	)
	if not buffered_move.is_empty():
		data = data.duplicate(true)
		data["LocX"] = buffered_move["X"]
		data["LocY"] = buffered_move["Y"]
		if str(buffered_move.get("Dir", "")) != "":
			data["Dir"] = buffered_move["Dir"]
	var entity_type: int = int(data.get("EntityType", -1))
	if entity_type == 1:
		var cached: Dictionary = data.duplicate(true)
		# The server can send another WorldReady snapshot while a monster is
		# still queued/deferred. A newer MoveEntity already updated the
		# authoritative local coordinates: never overwrite that position with
		# an older repeated SpawnEntity, or the monster will visually teleport
		# backward when the queue finally instantiates it.
		var previous: Dictionary = _monster_spawn_templates.get(entity_id, {}) as Dictionary
		if bool(previous.get("_aoi_has_move", false)):
			cached["LocX"] = previous.get("LocX", cached.get("LocX", 0.0))
			cached["LocY"] = previous.get("LocY", cached.get("LocY", 0.0))
			cached["_aoi_has_move"] = true
		_monster_spawn_templates[entity_id] = cached
		# Repeated authoritative snapshots must not move an existing render
		# entity into the deferred queue while it is still on the map.
		if get_active_entity(entity_id):
			_deferred_monster_spawns.erase(entity_id)
			return
		if _is_afk_monster_data_only():
			_deferred_monster_spawns[entity_id] = cached
			_remove_queued_spawn(entity_id)
			return
		if not _can_spawn_monster(cached, _monster_aoi_radii().x):
			_defer_monster_spawn(cached)
			return
		_deferred_monster_spawns.erase(entity_id)
	elif entity_type == 3:
		# Keep a canonical packet until the server sends DespawnEntity, even
		# if its heavy Sprite/Label/Collision nodes have been unloaded.
		var cached_ground: Dictionary = data.duplicate(true)
		cached_ground["_aoi_cached_at"] = Time.get_ticks_msec()
		_ground_spawn_templates[entity_id] = cached_ground
		_ground_visibility_index.upsert(entity_id, cached_ground)
		_ground_display_cache_dirty = true
		_ground_aoi_last_context = null
		if get_active_entity(entity_id):
			_deferred_ground_spawns.erase(entity_id)
			return
		if _is_afk_ground_data_only():
			_deferred_ground_spawns[entity_id] = cached_ground
			_remove_queued_spawn(entity_id)
			return
		# During a burst (hundreds of drops) do not sort/rebuild the winner
		# set after every SpawnEntity. Keep all data, then select once at the
		# next 250ms display AOI pass, without holding up network processing.
		if _ground_drop_mode() == ClientPerformanceProfile.GROUND_DROPS_LIMIT_DUPLICATES:
			_deferred_ground_spawns[entity_id] = cached_ground
			_remove_queued_spawn(entity_id)
			return
		var aoi: Dictionary = _ground_aoi_context()
		if aoi["center"] != null and not is_ground_inside_aoi(
			cached_ground, aoi["center"], float(aoi["enter"])
		):
			_deferred_ground_spawns[entity_id] = cached_ground
			_remove_queued_spawn(entity_id)
			return
		if not _should_display_ground_drop(
			entity_id, cached_ground,
			aoi["center"] if aoi["center"] != null else Vector2.ZERO,
			float(aoi["enter"])
		):
			_deferred_ground_spawns[entity_id] = cached_ground
			_remove_queued_spawn(entity_id)
			return
		_deferred_ground_spawns.erase(entity_id)
	if get_active_entity(entity_id) or _queued_spawn_ids.has(entity_id):
		return
	_queued_spawn_ids[entity_id] = true
	var queued_data: Dictionary
	if entity_type == 1:
		# The authoritative packet was deep-copied once into the monster template.
		# Only the top-level LocX/LocY fields can change before spawning; reuse
		# its read-only ExtraData instead of deep-cloning it a second time.
		queued_data = (_monster_spawn_templates[entity_id] as Dictionary).duplicate()
	elif entity_type == 3:
		queued_data = _ground_spawn_data_at_current_time(entity_id)
		queued_data["IsSnapshot"] = bool(data.get("IsSnapshot", false))
	else:
		queued_data = data.duplicate(true)
	# Prioritize interactables and nearest monsters instead of FIFO. During
	# teleport/login snapshots the closest threats should be the first Nodes
	# instantiated, while far monsters stay deferred by AOI.
	_enqueue_spawn_by_priority(queued_data)
	_last_spawn_enqueue_usec = Time.get_ticks_usec()

func set_spawn_queue_paused(paused: bool) -> void :
	_spawn_queue_paused = paused

func is_spawn_queue_paused() -> bool:
	return _spawn_queue_paused

func get_initial_spawn_settle_progress(quiet_usec: int = 300000) -> float:
	if not _spawn_queue.is_empty() or not _queued_spawn_ids.is_empty():
		return 0.0
	if _last_spawn_enqueue_usec <= 0:
		return 1.0
	return clampf(
		float(Time.get_ticks_usec() - _last_spawn_enqueue_usec) / float(quiet_usec),
		0.0,
		1.0
	)

func is_initial_spawn_queue_idle(quiet_usec: int = 300000) -> bool:
	return get_initial_spawn_settle_progress(quiet_usec) >= 1.0

func _process(delta: float) -> void:
	if GameManager and GameManager.is_afk_mode_active():
		return
	_run_entity_runtime(delta)


func _physics_process(delta: float) -> void:
	# AFK monitors render at 20 FPS, but spawn queues, monster interpolation
	# and targeting stay on the normal fixed 60Hz physics cadence.
	if not GameManager or not GameManager.is_afk_mode_active():
		return
	_run_entity_runtime(delta)


func _run_entity_runtime(delta: float) -> void :
	if not is_instance_valid(GameManager.current_map_node) or GameManager.current_map_node.is_queued_for_deletion():
		_spawn_queue.clear()
		_queued_spawn_ids.clear()
		_runtime_monster_ids.clear()
		_monster_spawn_templates.clear()
		_deferred_monster_spawns.clear()
		_combat_reveal_until_msec.clear()
		_pending_aoi_moves.clear()
		_ground_spawn_templates.clear()
		_deferred_ground_spawns.clear()
		_ground_visibility_index.clear()
		_recent_fatal_monsters.clear()
		_ground_display_winners.clear()
		_ground_display_cache_dirty = true
		_ground_trim_queue.clear()
		_ground_trim_pending.clear()
		_ground_aoi_elapsed = 0.0
		_monster_aoi_last_observer = null
		_ground_aoi_last_context = null
		return

	# Spawn already queued player visuals before scanning hundreds of drops
	# and deferred monsters. The exact same per-frame budget is retained.
	# Packet handling and all fixed-rate combat/physics remain unchanged.
	if not _spawn_queue_paused and not _spawn_queue.is_empty():
		# Never starve the spawn queue after the world becomes visible.
		# Normal runtime spawning is limited to one entity per frame so existing
		# movement/animation stays smooth without delaying NPC/monster creation.
		var frame_started_usec := Time.get_ticks_usec()
		var spawned_count := 0
		var batch_limit: int = INITIAL_SPAWN_BATCH_PER_FRAME if _initial_spawn_boost else SPAWN_BATCH_PER_FRAME
		var frame_budget_usec: int = INITIAL_SPAWN_FRAME_BUDGET_USEC if _initial_spawn_boost else SPAWN_FRAME_BUDGET_USEC
		# Other players are interactive and their visual latency matters more
		# than ground items. Allow up to three player scenes per frame only
		# when the existing microsecond budget permits. This never reduces
		# the fixed network/physics packet update cadence or FPS target.
		if not _initial_spawn_boost and int(_spawn_queue[0].get("EntityType", -1)) == 0:
			batch_limit = maxi(batch_limit, 3)
		while not _spawn_queue.is_empty() and spawned_count < batch_limit:
			if spawned_count > 0 and Time.get_ticks_usec() - frame_started_usec >= frame_budget_usec:
				break
			var data: Dictionary = _spawn_queue.pop_front()
			var entity_id := int(data.get("EntityID", 0))
			_queued_spawn_ids.erase(entity_id)
			# Always take the most recent cached monster coordinates instead
			# of using the stale queue-time snapshot.
			_spawn_entity_now(_get_current_queued_spawn_data(data))
			spawned_count += 1

		var elapsed_usec := Time.get_ticks_usec() - frame_started_usec
		if OS.is_debug_build() and elapsed_usec >= 10000:
			print("[ENTITY_SPAWN_TIMING] count=%d elapsed_ms=%.2f remaining=%d" % [
				spawned_count,
				float(elapsed_usec) / 1000.0,
				_spawn_queue.size()
			])

	_trim_ground_visuals_budgeted()
	_update_monster_aoi(delta)
	_update_ground_aoi(delta)
	# One clock for every protected ground label. No Timer node per item.
	# Visual only: 60Hz simulation, pickups and network packets are unchanged.
	_ground_protection_tick_elapsed += delta
	if _ground_protection_tick_elapsed >= 1.0:
		_ground_protection_tick_elapsed = fmod(_ground_protection_tick_elapsed, 1.0)
		var now_ms: int = Time.get_ticks_msec()
		for raw_id in _recent_fatal_monsters.keys():
			if int(_recent_fatal_monsters[raw_id]["expires"]) <= now_ms:
				_recent_fatal_monsters.erase(raw_id)
		for item in get_tree().get_nodes_in_group("sgo_protected_ground_item"):
			if is_instance_valid(item) and item.has_method("refresh_protection_label"):
				item.refresh_protection_label()
	_player_visibility_elapsed += delta
	if _player_visibility_elapsed >= PLAYER_VISIBILITY_REFRESH_INTERVAL:
		_player_visibility_elapsed = 0.0
		var visibility_profile: Dictionary = GameManager.current_performance_profile
		if int(visibility_profile.get("other_players_visibility", 0)) != ClientPerformanceProfile.PLAYER_SHOW_ALL:
			for player_id in _active_entity_types.keys():
				if int(_active_entity_types[player_id]) != 0:
					continue
				var remote_player: Node2D = get_active_entity(int(player_id))
				if is_instance_valid(remote_player):
					_apply_performance_visual_to_entity(int(player_id), 0, remote_player)


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

# Decorative animation must operate on the Sprite2D child, never the
# authoritative Area2D root (position, click shape and AOI anchor).
static func get_ground_drop_bounce_sprite(entity: Node2D) -> Node2D:
	if not is_instance_valid(entity):
		return null
	return entity.get_node_or_null("Sprite2D") as Node2D


static func _should_check_ground_drop_bounce(data: Dictionary) -> bool:
	# WorldReady/periodic snapshots describe already existing items.
	# Only genuinely new drops may play the monster-death bounce animation.
	return not bool(data.get("IsSnapshot", false))


func _spawn_entity_now(data: Dictionary) -> void :
	var entity_id = int(data.get("EntityID", 0))
	var entity_type = int(data.get("EntityType", 0))
	if entity_type == 3:
		# The queued packet may be several seconds old under a busy map.
		# Use the stored absolute creation timestamp to preserve server item
		# lifetime and ownership countdown at the *actual* render time.
		if _ground_spawn_templates.has(entity_id):
			var is_first_drop: bool = not bool(data.get("IsSnapshot", false))
			data = _ground_spawn_data_at_current_time(entity_id)
			data["IsSnapshot"] = not is_first_drop
		# The player can move away while a ground SpawnEntity waits in
		# the queue. Never allocate a distant item just to free it again.
		var aoi: Dictionary = _ground_aoi_context()
		if aoi["center"] != null and not is_ground_inside_aoi(
			data, aoi["center"], float(aoi["exit"])
		):
			_deferred_ground_spawns[entity_id] = _ground_spawn_templates.get(entity_id, data)
			return
		if not _should_display_ground_drop(
			entity_id, data,
			aoi["center"] if aoi["center"] != null else Vector2.ZERO,
			float(aoi["enter"])
		):
			_deferred_ground_spawns[entity_id] = _ground_spawn_templates.get(entity_id, data)
			return
	if entity_type == 1:
		# The authoritative SpawnEntity handler already made one deep copy.
		# A queued spawn must reuse that canonical record instead of doing a
		# second deep copy of ExtraData for every one of 200+ nearby monsters.
		# Also preserve newer server MoveEntity coordinates in the template.
		if not _monster_spawn_templates.has(entity_id):
			_monster_spawn_templates[entity_id] = data.duplicate(true)
		if not _can_spawn_monster(data, _monster_aoi_radii().y):
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
			var player_spawn_profile: Dictionary = GameManager.get_performance_profile() if GameManager else {}
			var pre_simplify_player := bool(player_spawn_profile.get("simplify_other_players", false)) or int(player_spawn_profile.get("other_players_visibility", 0)) != ClientPerformanceProfile.PLAYER_SHOW_ALL
			var pre_player_style := int(player_spawn_profile.get(
				"player_simplify_style",
				ClientPerformanceProfile.SIMPLIFY_PIXEL_A
			))
			if new_entity.has_method("prepare_performance_visual"):
				new_entity.prepare_performance_visual(
					pre_simplify_player,
					pre_player_style
				)
			target_parent.add_child(new_entity)
			new_entity.global_position = spawn_pos
			if new_entity.has_method("initialize_network_player"):
				new_entity.initialize_network_player(data)
		1:
			var monster_spawn_started_usec: int = Time.get_ticks_usec()
			var monster_instantiate_started_usec: int = monster_spawn_started_usec
			new_entity = MONSTER_SCENE.instantiate()
			var spawn_profile: Dictionary = GameManager.get_performance_profile() if GameManager else {}
			var pre_simplified := bool(spawn_profile.get("simplify_noncritical_monsters", false)) or (
			int(spawn_profile.get("ordinary_monsters_visibility", 0)) == ClientPerformanceProfile.MONSTER_HIDE_ORDINARY
			and not bool((data.get("ExtraData", {}) as Dictionary).get("IsWorldBoss", false))
		)
			var pre_style := int(spawn_profile.get(
				"monster_simplify_style",
				ClientPerformanceProfile.SIMPLIFY_PIXEL_A
			))
			if new_entity.has_method("prepare_performance_visual"):
				new_entity.prepare_performance_visual(
					pre_simplified,
					pre_style
				)
			var monster_instantiate_ms: float = float(Time.get_ticks_usec() - monster_instantiate_started_usec) / 1000.0

			var monster_add_started_usec: int = Time.get_ticks_usec()
			target_parent.add_child(new_entity)
			var monster_add_ms: float = float(Time.get_ticks_usec() - monster_add_started_usec) / 1000.0

			new_entity.global_position = spawn_pos
			var monster_setup_started_usec: int = Time.get_ticks_usec()
			if new_entity.has_method("setup"):
				new_entity.setup(entity_id, data.get("Name", "未知怪物"), data.get("ExtraData", {}))
			var monster_setup_ms: float = float(Time.get_ticks_usec() - monster_setup_started_usec) / 1000.0

			if OS.is_debug_build():
				var monster_total_ms: float = float(Time.get_ticks_usec() - monster_spawn_started_usec) / 1000.0
				if monster_total_ms >= 8.0:
					print("[MONSTER_SPAWN_TIMING] id=%d instantiate_ms=%.2f add_child_ms=%.2f setup_ms=%.2f total_ms=%.2f" % [
						entity_id,
						monster_instantiate_ms,
						monster_add_ms,
						monster_setup_ms,
						monster_total_ms
					])
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

			if _should_check_ground_drop_bounce(data):
				# Original per-drop scan walked all rendered monsters; that is
				# O(drops * monsters) in a large battle. Recent fatal locations
				# are captured from the same authoritative damage/death events.
				var fatal_delay: float = _recent_drop_bounce_delay(spawn_pos)
				if fatal_delay >= 0.0:
					drop_delay = fatal_delay
					is_monster_drop = true

			if is_monster_drop:
				new_entity.visible = false

			if new_entity.has_method("initialize_ground_item"):
				new_entity.initialize_ground_item(data)

			if is_monster_drop:
				# Server already validated this item's global position against
				# Blocking.dat. Never move the Area2D itself for a decorative
				# bounce: that shifts its click target and can land the item in
				# a wall after the Server chose a safe tile.
				var bounce_tween = new_entity.create_tween()
				bounce_tween.tween_interval(drop_delay)
				bounce_tween.tween_callback(func():
					if not is_instance_valid(new_entity) or new_entity.is_queued_for_deletion():
						return
					new_entity.visible = true
					var drop_sprite := get_ground_drop_bounce_sprite(new_entity)
					if not is_instance_valid(drop_sprite):
						return
					var landing_offset: Vector2 = drop_sprite.position
					drop_sprite.position = landing_offset + Vector2(0.0, -40.0)
					var visual_tween := new_entity.create_tween()
					visual_tween.tween_property(drop_sprite, "position", landing_offset + Vector2(0.0, 7.0), 0.24).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)
					visual_tween.tween_property(drop_sprite, "position", landing_offset, 0.12).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
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
		_apply_performance_visual_to_entity(entity_id, entity_type, new_entity)
		if GameManager and GameManager.is_afk_mode_active() and new_entity.has_method("set_afk_visual_suspended"):
			new_entity.set_afk_visual_suspended(true)

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
	# A delayed MoveEntity after an AOI exit must not revive an actor.
	_pending_aoi_moves.mark_despawned(entity_id, Time.get_ticks_msec())
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
	_combat_reveal_until_msec.erase(entity_id)
	_ground_spawn_templates.erase(entity_id)
	_deferred_ground_spawns.erase(entity_id)
	_ground_visibility_index.erase(entity_id)
	_ground_display_cache_dirty = true
	_ground_trim_pending.erase(entity_id)
	_remove_queued_spawn(entity_id)
	# Despawning a duplicate can make the sixth item eligible for display.
	_ground_aoi_last_context = null
	_ground_aoi_elapsed = GROUND_AOI_UPDATE_INTERVAL

func _on_entity_moved(data: Dictionary) -> void :
	var entity_id = int(data.get("EntityID", 0))
	if entity_id <= 0:
		return
	if _monster_spawn_templates.has(entity_id):
		# Keep the original immutable-template behavior. In-place mutation
		# of a shared deferred/queued snapshot can change spawn lifecycles.
		var cached: Dictionary = (_monster_spawn_templates[entity_id] as Dictionary).duplicate()
		cached["LocX"] = float(data.get("X", cached.get("LocX", 0.0)))
		cached["LocY"] = float(data.get("Y", cached.get("LocY", 0.0)))
		cached["_aoi_has_move"] = true
		_monster_spawn_templates[entity_id] = cached
		if _deferred_monster_spawns.has(entity_id):
			_deferred_monster_spawns[entity_id] = cached
			if _is_afk_monster_data_only():
				return
			if _is_monster_inside_aoi(cached, _monster_aoi_radii().x):
				_queue_cached_monster_spawn(entity_id)
			return
	var target_node = get_active_entity(entity_id)
	if not target_node and not _monster_spawn_templates.has(entity_id) and not _ground_spawn_templates.has(entity_id):
		_pending_aoi_moves.record_move(entity_id, data, Time.get_ticks_msec())
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
			# Keep AOI-instantiated monsters moving through the same visual
			# interpolation even while their sprite is outside the viewport.
			# The old offscreen fast path assigned global_position directly:
			# a monster crossing the viewport boundary could visibly teleport.
			# VisibleOnScreenNotifier2D still pauses offscreen sprite animation.
			# Server correction packets (SpeedTime == 0) remain authoritative.
			target_node.network_move_to(target_pos, speed_time)
			if is_monster and speed_time > 0.01:
				_runtime_monster_ids[entity_id] = true

func _on_entity_state_changed(entity_id: int, new_state: String, new_dir: String) -> void :
	if _monster_spawn_templates.has(entity_id):
		var cached: Dictionary = (_monster_spawn_templates[entity_id] as Dictionary).duplicate()
		cached["State"] = new_state
		if not new_dir.is_empty():
			cached["Dir"] = new_dir
		_monster_spawn_templates[entity_id] = cached
		if _deferred_monster_spawns.has(entity_id):
			_deferred_monster_spawns[entity_id] = cached
	if new_state == "attack" or new_state.begins_with("attack_"):
		_reveal_combat_monster(entity_id, "attack_state")
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
			elif new_state == "idle":
				# An early idle may be deferred until the last server-approved
				# waypoint has been rendered. Keep that monster in the runtime
				# loop; advance_network_runtime will remove it on arrival.
				if target_node.has_method("is_finishing_last_move_before_idle") and target_node.is_finishing_last_move_before_idle():
					_runtime_monster_ids[entity_id] = true
				else:
					_runtime_monster_ids.erase(entity_id)
			elif new_state in ["death", "die"]:
				_runtime_monster_ids.erase(entity_id)

func _on_pet_idle_speech_received(entity_id: int, message: String) -> void :
	var target_node = get_active_entity(entity_id)
	if target_node and target_node.has_method("show_idle_speech_bubble"):
		target_node.show_idle_speech_bubble(message)

func _on_entity_appearance_updated(data: Dictionary) -> void :
	var entity_id = int(data.get("EntityID", 0))
	if _monster_spawn_templates.has(entity_id):
		var cached: Dictionary = (_monster_spawn_templates[entity_id] as Dictionary).duplicate(true)
		for key in data.keys():
			if key == "EntityID":
				continue
			cached[key] = data[key]
		_monster_spawn_templates[entity_id] = cached
		if _deferred_monster_spawns.has(entity_id):
			_deferred_monster_spawns[entity_id] = cached
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
	if target_type == 1 and current_hp > 0:
		_reveal_combat_monster(target_id, "monster_damaged")
	var local_player_for_reveal = GameManager.current_player if GameManager else null
	if target_type == 0 and is_instance_valid(local_player_for_reveal) and "data_model" in local_player_for_reveal:
		if int(local_player_for_reveal.data_model.db_id) == target_id:
			_reveal_combat_monster(attacker_id, "player_damaged")
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
		if target_type == 1 and current_hp <= 0:
			var fatal_delay: float = float(target_node.get_meta("fatal_hit_delay", hit_delay))
			_remember_fatal_monster(target_id, target_node, fatal_delay)




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
		if entity_type == 1:
			_remember_fatal_monster(entity_id, entity, death_delay)
		if death_delay > 0:
			get_tree().create_timer(death_delay).timeout.connect( func():
				_finalize_entity_death(entity, clear_hover_on_death)
			)
		else:
			_finalize_entity_death(entity, clear_hover_on_death)
	if entity_type != 0:
		# EntityDeath is also a terminal AOI incarnation boundary. A late
		# MoveEntity from the dying monster must not override the fresh
		# authoritative Respawn/SpawnEntity position.
		_pending_aoi_moves.mark_despawned(entity_id, Time.get_ticks_msec())
		active_entities.erase(entity_id)
	if entity_type == 1:
		_active_entity_types.erase(entity_id)
		_runtime_monster_ids.erase(entity_id)
		_monster_spawn_templates.erase(entity_id)
		_deferred_monster_spawns.erase(entity_id)
		_combat_reveal_until_msec.erase(entity_id)
		_remove_queued_spawn(entity_id)


func _on_skill_cast_received(caster_id: int, caster_type: int, skill_id: int, skill_level: int, target_id: int, target_x: int, target_y: int, aoe_targets: Array = []) -> void :
	if caster_type == 1:
		_reveal_combat_monster(caster_id, "skill_cast")

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
	if has_target:
		_reveal_combat_monster(entity_id, "aggro")
	else:
		_combat_reveal_until_msec.erase(entity_id)
	var target_node = get_active_entity(entity_id)
	if target_node and target_node.has_method("set_target_indicator"):
		target_node.set_target_indicator(has_target)

func _on_entity_heal_received(data: Dictionary) -> void :



	pass
