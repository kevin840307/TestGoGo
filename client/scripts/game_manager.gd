
extends Node

const MAP_LOADING_FADE_IN_SECONDS: = 0.3
const MAP_LOADING_COVER_DELAY_SECONDS: = 0.35
const MAP_LOADING_MIN_VISIBLE_SECONDS: = 0.55
const MAP_LOADING_RESOURCE_PROGRESS_MAX: = 90.0
const MAP_LOADING_INSTANTIATE_PROGRESS: = 94.0
const MAP_LOADING_ADD_CHILD_PROGRESS: = 97.0
const MAP_LOADING_FINALIZE_PROGRESS: = 99.0
const TRAIN_TRAVEL_MAP_TIMEOUT_SECONDS: = 15.0
const TRAIN_VIDEO_TIMEOUT_SECONDS: = 120.0
const WORLD_ENHANCEMENT_SCENE: = preload("res://world_enhancement.tscn")
const DAY_NIGHT_OVERLAY_SCENE: = preload("res://day_night_overlay.tscn")
const TRAIN_VIDEO_PATHS: = {
	"train_depart_king.ogv": "res://train_depart_king.ogv", 
	"train_depart_knight.ogv": "res://train_depart_knight.ogv", 
	"train_depart_magic.ogv": "res://train_depart_magic.ogv", 
	"train_depart_robot.ogv": "res://train_depart_robot.ogv", 
}
const VALID_FESTIVAL_ACTIVITIES: = [
	"lunar_new_year", 
	"valentine", 
	"white_valentine", 
	"easter", 
	"dragon_boat", 
	"grand_hunt", 
	"mid_autumn", 
	"halloween", 
	"christmas", 
]

signal festival_activity_changed(festival: String)

var current_map_node: Node2D = null
var current_player: CharacterBody2D = null
var current_hud: CanvasLayer = null
var current_world_enhancement: CanvasLayer = null
var current_day_night_overlay: CanvasLayer = null
var world_enhancement_enabled: bool = GameConstants.SETTING_DEFAULT_QUALITY_ENHANCEMENT_ENABLED
var current_festival_theme: String = FestivalThemeManager.NORMAL
var current_festival_activity: String = ""

var is_changing_map: bool = false
var max_recorded_progress: float = 0.0
var _map_transition_serial: int = 0
var _active_map_transition_id: int = 0
var _map_transition_started_usec: int = 0
var _map_transition_last_stage_usec: int = 0
var _frame_gap_last_usec: int = 0
var _frame_gap_stage: String = "startup"
var _frame_gap_stage_since_usec: int = 0
const FRAME_GAP_WARN_USEC: int = 100000
const RENDER_STATS_INTERVAL_SECONDS: float = 2.0
const PERF_ANALYSIS_INTERVAL_SECONDS: float = 5.0
var _render_stats_elapsed: float = 0.0
var _perf_analysis_elapsed: float = 0.0
var _focus_last_change_usec: int = 0
var _focus_change_count: int = 0
var _prewarmed_hud_windows: Dictionary = {}

var _train_travel_active: bool = false
var _train_travel_id: String = ""
var _train_destination_video: String = ""
var _train_travel_phase: String = ""
var _train_video_layer: CanvasLayer = null
var _train_video_player: VideoStreamPlayer = null
var _train_video_finished: bool = false
var _train_player_process_state: Dictionary = {}
var _train_hud_process_state: Dictionary = {}

func cache_prewarmed_hud_window(kind: String, window: Control) -> void :
	if kind.is_empty() or not is_instance_valid(window):
		return
	var existing = _prewarmed_hud_windows.get(kind, null)
	if is_instance_valid(existing):
		window.queue_free()
		return
	window.visible = false
	window.modulate.a = 0.0
	add_child(window)
	_prewarmed_hud_windows[kind] = window

func take_prewarmed_hud_window(kind: String) -> Control:
	var window = _prewarmed_hud_windows.get(kind, null)
	if not is_instance_valid(window):
		_prewarmed_hud_windows.erase(kind)
		return null
	_prewarmed_hud_windows.erase(kind)
	if window.get_parent() != null:
		window.get_parent().remove_child(window)
	return window

func has_prewarmed_hud_window(kind: String) -> bool:
	return is_instance_valid(_prewarmed_hud_windows.get(kind, null))

func set_frame_gap_stage(stage: String) -> void :
	_frame_gap_stage = stage
	_frame_gap_stage_since_usec = Time.get_ticks_usec()

# Background FPS caps intentionally increase frame intervals (e.g., 30 FPS = ~33ms).
# Distinguish that normal throttling from genuinely stalled background frames.
func classify_frame_gap(gap_usec: int, has_focus: bool, fps_cap: int) -> String:
	if has_focus:
		return "foreground_stall"
	if fps_cap > 0:
		var expected_usec: float = 1000000.0 / float(fps_cap)
		if float(gap_usec) <= expected_usec * 1.8:
			return "background_throttle"
	return "background_stall"


static func focus_transition_within_interval(start_ms: int, end_ms: int, change_ms: int) -> bool:
	# Packet timestamps use Time.get_ticks_msec(); the last focus transition
	# uses the same monotonic clock. A resumed chase packet should identify
	# that it spans a background interval instead of implying network lag.
	return start_ms > 0 and change_ms >= start_ms and change_ms <= end_ms


func did_focus_change_during_interval(start_ms: int, end_ms: int) -> bool:
	return focus_transition_within_interval(
		start_ms, end_ms, int(_focus_last_change_usec / 1000)
	)


func _focus_perf_context() -> String:
	var focus_name: String = "foreground" if _window_has_focus else "background"
	var render_enabled: bool = RenderingServer.render_loop_enabled
	var master_bus_index: int = AudioServer.get_bus_index("Master")
	var audio_muted: bool = master_bus_index >= 0 and AudioServer.is_bus_mute(master_bus_index)
	var focus_age_ms: int = -1
	if _focus_last_change_usec > 0:
		focus_age_ms = int((Time.get_ticks_usec() - _focus_last_change_usec) / 1000)
	return "focus=%s focus_actual=%s power_save=%s fps_cap=%d render_loop=%s audio_muted=%s focus_age_ms=%d startup_overlay=%s" % [
		focus_name, str(DisplayServer.window_is_focused()), str(not _window_has_focus),
		Engine.max_fps, str(render_enabled), str(audio_muted), focus_age_ms,
		str(_startup_loading_overlay_active),
	]


func _log_focus_power_save(event_name: String, trimmed_caches: bool = false) -> void:
	if not OS.is_debug_build():
		return
	# OS window-focus queries may lag a notification by one event/frame.
	# Keep both readings, explicitly marking this transition-time race.
	var query_disagrees: bool = DisplayServer.window_is_focused() != _window_has_focus
	print("[FOCUS_POWER_SAVE] event=%s %s focus_changes=%d caches_trimmed=%s focus_query_disagrees=%s" % [
		event_name, _focus_perf_context(), _focus_change_count,
		str(trimmed_caches), str(query_disagrees),
	])


func _process(delta: float) -> void :
	# Diagnostics are intentionally excluded from release exports.
	if not OS.is_debug_build():
		return

	var now_usec := Time.get_ticks_usec()
	if _frame_gap_last_usec > 0:
		var gap_usec := now_usec - _frame_gap_last_usec
		if gap_usec >= FRAME_GAP_WARN_USEC:
			var gap_kind: String = classify_frame_gap(gap_usec, _window_has_focus, Engine.max_fps)
			# A normal background frame interval is intentional, not a stall.
			# Preserve unusually long background gaps as real diagnostics.
			if gap_kind != "background_throttle":
				var stage_age_ms := float(now_usec - _frame_gap_stage_since_usec) / 1000.0 if _frame_gap_stage_since_usec > 0 else -1.0
				var line := "[FRAME_GAP] gap_ms=%.2f stage=%s stage_age_ms=%.2f memory_mb=%.2f kind=%s %s" % [
					float(gap_usec) / 1000.0,
					_frame_gap_stage,
					stage_age_ms,
					_get_process_memory_mb(),
					gap_kind,
					_focus_perf_context(),
				]
				print(line)
				_append_transition_log(line)
	_frame_gap_last_usec = now_usec

	_render_stats_elapsed += delta
	if _render_stats_elapsed >= RENDER_STATS_INTERVAL_SECONDS:
		_render_stats_elapsed = 0.0
		_print_render_stats()

	_perf_analysis_elapsed += delta
	if _perf_analysis_elapsed >= PERF_ANALYSIS_INTERVAL_SECONDS:
		_perf_analysis_elapsed = 0.0
		_print_performance_analysis("periodic")

func _multiclient_gpu_context() -> String:
	# Performance.RENDER_VIDEO_MEM_USED is Godot's resource allocation metric,
	# not GPU utilization or a Windows/WDDM process-VRAM measurement.
	# Include PID and resolution so 1/5/10 open-client logs can be matched
	# against OS-level CPU, GPU Engine and dedicated GPU memory counters.
	var window_size: Vector2i = DisplayServer.window_get_size()
	return "pid=%d window=%dx%d paper_palette_materials=%d monster_palette_materials=%d gpu_memory_metric=godot_resources" % [
		OS.get_process_id(),
		window_size.x,
		window_size.y,
		PaperDollEngine.get_shared_palette_material_count(),
		MonsterEntity.get_shared_monster_palette_material_count(),
	]


func _print_render_stats() -> void :
	if not OS.is_debug_build():
		return
	if not is_instance_valid(current_map_node):
		return

	var visible_monsters := 0
	var visible_players := 0
	var visible_pets := 0
	var ground_items_total := 0
	var ground_items_visible_nearby := 0
	var paper_doll_layers := 0

	if NetworkEntityManager:
		for entity_id in NetworkEntityManager.active_entities.keys():
			var entity = NetworkEntityManager.active_entities[entity_id]
			if not is_instance_valid(entity) or not entity.is_inside_tree():
				continue
			var entity_type := int(NetworkEntityManager._active_entity_types.get(entity_id, -1))
			var visible_runtime := true
			if entity.has_method("is_render_visible"):
				visible_runtime = bool(entity.is_render_visible())
			elif entity.has_method("is_runtime_visible"):
				visible_runtime = bool(entity.is_runtime_visible())
			if not visible_runtime:
				continue
			match entity_type:
				0: visible_players += 1
				1: visible_monsters += 1
				3:
					ground_items_total += 1
					if is_instance_valid(current_player) and entity.is_visible_in_tree():
						if entity.global_position.distance_squared_to(current_player.global_position) <= 900.0 * 900.0:
							ground_items_visible_nearby += 1
				4: visible_pets += 1
			if "doll_engine" in entity and is_instance_valid(entity.doll_engine):
				paper_doll_layers += entity.doll_engine.doll_sprites.size()

	if is_instance_valid(current_player) and "doll_engine" in current_player and is_instance_valid(current_player.doll_engine):
		paper_doll_layers += current_player.doll_engine.doll_sprites.size()

	var aoi_active_monsters: int = 0
	var aoi_deferred_monsters: int = 0
	if NetworkEntityManager:
		if NetworkEntityManager.has_method("get_aoi_active_monster_count"):
			aoi_active_monsters = int(NetworkEntityManager.get_aoi_active_monster_count())
		if NetworkEntityManager.has_method("get_aoi_deferred_monster_count"):
			aoi_deferred_monsters = int(NetworkEntityManager.get_aoi_deferred_monster_count())

	print("[RENDER_STATS] fps=%d draw_calls=%d objects=%d primitives=%d canvas_nodes=%d video_mem_mb=%.1f texture_mem_mb=%.1f visible_monsters=%d aoi_active_monsters=%d aoi_deferred_monsters=%d visible_players=%d visible_pets=%d ground_items=%d ground_nearby_visible=%d paper_doll_layers=%d render_stats_valid=%s %s %s" % [
		int(Performance.get_monitor(Performance.TIME_FPS)),
		int(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)),
		int(Performance.get_monitor(Performance.RENDER_TOTAL_OBJECTS_IN_FRAME)),
		int(Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME)),
		int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT)),
		float(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)) / 1048576.0,
		float(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED)) / 1048576.0,
		visible_monsters,
		aoi_active_monsters,
		aoi_deferred_monsters,
		visible_players,
		visible_pets,
		ground_items_total,
		ground_items_visible_nearby,
		paper_doll_layers,
		str(_window_has_focus and RenderingServer.render_loop_enabled),
		_focus_perf_context(),
		_multiclient_gpu_context(),
	])

func _print_performance_analysis(checkpoint: String) -> void:
	if not OS.is_debug_build():
		return

	var fps: float = float(Performance.get_monitor(Performance.TIME_FPS))
	var process_ms: float = float(Performance.get_monitor(Performance.TIME_PROCESS)) * 1000.0
	var physics_ms: float = float(Performance.get_monitor(Performance.TIME_PHYSICS_PROCESS)) * 1000.0
	var target_frame_ms: float = 1000.0 / maxf(fps, 1.0)
	var cpu_frame_pct: float = clampf(
		((process_ms + physics_ms) / maxf(target_frame_ms, 0.001)) * 100.0,
		0.0,
		999.0
	)

	var static_mem_mb: float = float(OS.get_static_memory_usage()) / 1048576.0
	var static_peak_mb: float = float(OS.get_static_memory_peak_usage()) / 1048576.0
	var texture_mem_mb: float = float(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED)) / 1048576.0
	var video_mem_mb: float = float(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)) / 1048576.0
	var object_count: int = int(Performance.get_monitor(Performance.OBJECT_COUNT))
	var node_count: int = int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var orphan_count: int = int(Performance.get_monitor(Performance.OBJECT_ORPHAN_NODE_COUNT))

	var active_entities: int = 0
	var active_monsters: int = 0
	var deferred_monsters: int = 0
	if NetworkEntityManager:
		active_entities = NetworkEntityManager.active_entities.size()
		if NetworkEntityManager.has_method("get_aoi_active_monster_count"):
			active_monsters = int(NetworkEntityManager.get_aoi_active_monster_count())
		if NetworkEntityManager.has_method("get_aoi_deferred_monster_count"):
			deferred_monsters = int(NetworkEntityManager.get_aoi_deferred_monster_count())

	print("[PERF_ANALYSIS] checkpoint=%s fps=%.1f process_ms=%.3f physics_ms=%.3f cpu_frame_pct=%.1f static_mem_mb=%.1f static_peak_mb=%.1f video_mem_mb=%.1f texture_mem_mb=%.1f objects=%d nodes=%d orphan_nodes=%d active_entities=%d active_monsters=%d deferred_monsters=%d cpu_metric=frame_budget_ratio %s %s" % [
		checkpoint,
		fps,
		process_ms,
		physics_ms,
		cpu_frame_pct,
		static_mem_mb,
		static_peak_mb,
		video_mem_mb,
		texture_mem_mb,
		object_count,
		node_count,
		orphan_count,
		active_entities,
		active_monsters,
		deferred_monsters,
		_focus_perf_context(),
		_multiclient_gpu_context(),
	])


func log_performance_checkpoint(checkpoint: String) -> void:
	if not OS.is_debug_build():
		return
	_print_performance_analysis(checkpoint)


var _initial_loading_progress_value: float = 0.0

func _report_initial_loading_progress(progress_callback: Callable, value: float, stage: String) -> void :
	# A loading operation must never visually move backwards. Keep a monotonic
	# floor so later async readiness gates cannot undo progress already shown.
	_initial_loading_progress_value = maxf(
		_initial_loading_progress_value,
		clampf(value, 0.0, 100.0)
	)
	if progress_callback.is_valid():
		progress_callback.call(_initial_loading_progress_value, stage)

func _get_process_memory_mb() -> float:
	return float(Performance.get_monitor(Performance.MEMORY_STATIC)) / 1048576.0

func _append_transition_log(line: String) -> void :
	if not OS.is_debug_build():
		return
	var log_path: = "user://map_transition_%d.log" % OS.get_process_id()
	var file: = FileAccess.open(log_path, FileAccess.READ_WRITE)
	if file == null:
		file = FileAccess.open(log_path, FileAccess.WRITE)
	if file == null:
		printerr("[MapTransition] 無法寫入診斷紀錄：", log_path)
		return
	file.seek_end()
	file.store_line(line)
	file.flush()

func append_lifecycle_diagnostic(line: String) -> void :
	_append_transition_log(line)

func get_active_map_transition_id() -> int:
	return _active_map_transition_id

func _is_map_transition_current(transition_id: int) -> bool:
	return is_changing_map and _active_map_transition_id == transition_id and is_inside_tree()

func _cancel_active_map_transition(reason: String) -> void :
	if _active_map_transition_id == 0:
		return
	if is_changing_map:
		trace_map_diagnostic("cancelled", {"reason": reason})
	is_changing_map = false
	_active_map_transition_id = 0

func trace_map_diagnostic(stage: String, details: Dictionary = {}) -> void :
	var source_map_id: = 0
	var target_map_id: = 0
	if is_instance_valid(current_player) and current_player.data_model:
		source_map_id = current_player.data_model.map_id
	if details.has("target_map"):
		target_map_id = int(details["target_map"])
	else:
		target_map_id = source_map_id
	_trace_map_change(stage, source_map_id, target_map_id, details)

func _trace_map_change(stage: String, source_map_id: int, target_map_id: int, details: Dictionary = {}) -> void :
	var dbid: = 0
	if is_instance_valid(current_player):
		var player_data = current_player.get("data_model")
		if player_data:
			dbid = player_data.db_id
	var now_usec: = Time.get_ticks_usec()
	var elapsed_ms: = 0.0
	var stage_ms: = 0.0
	if _map_transition_started_usec > 0:
		elapsed_ms = float(now_usec - _map_transition_started_usec) / 1000.0
	if _map_transition_last_stage_usec > 0:
		stage_ms = float(now_usec - _map_transition_last_stage_usec) / 1000.0
	_map_transition_last_stage_usec = now_usec





var _window_has_focus: bool = true
var _focused_max_fps: int = 0
var _master_bus_was_muted_before_focus_loss: bool = false
var _startup_loading_overlay_active: bool = false
const UNFOCUSED_MAX_FPS := 30
const STARTUP_UNFOCUSED_PROGRESS_FPS := 10


func set_startup_loading_overlay_active(active: bool) -> void:
	# A completely disabled background render loop cannot display the first
	# startup loading bar at all (the exact black-screen regression).
	# Draw it at 10 FPS *only during bootstrap*, then restore the original
	# render-disabled background policy as soon as login is ready.
	if _startup_loading_overlay_active == active:
		return
	_startup_loading_overlay_active = active
	if not _window_has_focus:
		Engine.max_fps = STARTUP_UNFOCUSED_PROGRESS_FPS if active else UNFOCUSED_MAX_FPS
		RenderingServer.render_loop_enabled = active
	if OS.is_debug_build():
		_log_focus_power_save("startup_overlay_on" if active else "startup_overlay_off")


static func background_loading_display_fps(bootstrap_active: bool) -> int:
	return STARTUP_UNFOCUSED_PROGRESS_FPS if bootstrap_active else UNFOCUSED_MAX_FPS

func _notification(what: int) -> void :
	if what == NOTIFICATION_APPLICATION_FOCUS_OUT:
		_set_window_focus_power_save(false)
	elif what == NOTIFICATION_APPLICATION_FOCUS_IN:
		_set_window_focus_power_save(true)

func _set_window_focus_power_save(has_focus: bool) -> void :
	if _window_has_focus == has_focus:
		return
	_window_has_focus = has_focus
	_focus_last_change_usec = Time.get_ticks_usec()
	_focus_change_count += 1
	# The frame spanning focus-in/out includes the intentional background FPS cap.
	# It cannot be assigned to foreground lag: restart the gap window.
	_frame_gap_last_usec = _focus_last_change_usec

	var master_bus_index := AudioServer.get_bus_index("Master")

	if not has_focus:
		_focused_max_fps = Engine.max_fps
		Engine.max_fps = background_loading_display_fps(_startup_loading_overlay_active)
		RenderingServer.render_loop_enabled = _startup_loading_overlay_active
		# RenderingServer alone stops GPU drawing but not HUD._process,
		# minimap/cursor scanning and day-night screen-UV updates.
		# Pause visuals only; network, timers and server world state continue.
		_set_background_visual_processing(true)
		_trim_unfocused_runtime_caches()

		if master_bus_index >= 0:
			_master_bus_was_muted_before_focus_loss = AudioServer.is_bus_mute(master_bus_index)
			AudioServer.set_bus_mute(master_bus_index, true)
	else:
		_set_background_visual_processing(false)
		RenderingServer.render_loop_enabled = true
		Engine.max_fps = _focused_max_fps

		if master_bus_index >= 0:
			AudioServer.set_bus_mute(
				master_bus_index,
				_master_bus_was_muted_before_focus_loss
			)

	_log_focus_power_save("focus_in" if has_focus else "focus_out", not has_focus)

func _set_background_visual_processing(paused: bool) -> void :
	if CursorManager:
		CursorManager.set_process(not paused)
	if is_instance_valid(current_day_night_overlay):
		current_day_night_overlay.set_process(not paused)
	if is_instance_valid(current_hud) and current_hud.has_method("set_background_visuals_paused"):
		current_hud.set_background_visuals_paused(paused)

func _trim_unfocused_runtime_caches() -> void :
	if DollLoader and DollLoader.has_method("clear_runtime_cache"):
		DollLoader.clear_runtime_cache()
	if EffectLoader and EffectLoader.has_method("clear_runtime_cache"):
		EffectLoader.clear_runtime_cache()
	if MonsterLoader and MonsterLoader.has_method("clear_runtime_cache"):
		MonsterLoader.clear_runtime_cache()
	if PetLoader and PetLoader.has_method("clear_runtime_cache"):
		PetLoader.clear_runtime_cache()
	if ItemLoader and ItemLoader.has_method("clear_runtime_cache"):
		ItemLoader.clear_runtime_cache()
	if SkillLoader and SkillLoader.has_method("clear_runtime_cache"):
		SkillLoader.clear_runtime_cache()
	if AudioManager and AudioManager.has_method("clear_runtime_cache"):
		AudioManager.clear_runtime_cache()

func _ready() -> void :
	_window_has_focus = DisplayServer.window_is_focused()
	_focused_max_fps = Engine.max_fps
	_focus_last_change_usec = Time.get_ticks_usec()
	if not _window_has_focus:
		_window_has_focus = true
		_set_window_focus_power_save(false)
	else:
		_log_focus_power_save("initial")

	if NetworkManager:
		NetworkManager.change_map_received.connect(_on_change_map_received)
		NetworkManager.train_travel_started.connect(_on_train_travel_started)
		NetworkManager.festival_theme_received.connect(set_festival_theme)
		NetworkManager.festival_activity_received.connect(set_festival_activity)

func set_festival_theme(theme: String) -> Dictionary:
	current_festival_theme = FestivalThemeManager.normalize_theme(theme)
	return _apply_current_festival_theme()

func set_festival_activity(festival: String) -> void :
	var normalized_festival: = festival.strip_edges().to_lower()
	if normalized_festival not in VALID_FESTIVAL_ACTIVITIES:
		normalized_festival = ""
	if current_festival_activity == normalized_festival:
		return
	current_festival_activity = normalized_festival
	festival_activity_changed.emit(current_festival_activity)

func _apply_current_festival_theme() -> Dictionary:
	if not is_instance_valid(current_map_node):
		return {
			"theme": current_festival_theme, 
			"matched": 0, 
			"changed": 0, 
			"failed": 0, 
		}
	set_frame_gap_stage("festival_theme_apply")
	var started_usec := Time.get_ticks_usec()
	var result: Dictionary = FestivalThemeManager.apply_theme_to_map(current_map_node, current_festival_theme)
	var elapsed_ms := float(Time.get_ticks_usec() - started_usec) / 1000.0
	if elapsed_ms >= 50.0:
		print("[INIT_TIMING] festival_theme_ms=%.2f" % elapsed_ms)
	return result

func _init_buildings_with_diagnostics(source_map_id: int, target_map_id: int) -> void :
	if not BuildingManager:
		return
	await get_tree().process_frame
	set_frame_gap_stage("building_init")
	var started_usec: = Time.get_ticks_usec()
	BuildingManager.init_buildings()
	var duration_ms := float(Time.get_ticks_usec() - started_usec) / 1000.0
	if duration_ms >= 50.0:
		print("[INIT_TIMING] building_init_ms=%.2f" % duration_ms)
	_trace_map_change("buildings_initialized", source_map_id, target_map_id, {
		"duration_ms": duration_ms, 
	})
	set_frame_gap_stage("world_running")

func enter_game_world(char_data: Dictionary, map_id: String, spawn_position: Vector2, progress_callback: Callable = Callable()) -> bool:
	_initial_loading_progress_value = 0.0
	var raw_num_str = map_id.replace("map", "")
	var target_map_id: int = int(raw_num_str)
	var formatted_map_name = "Map%02d" % target_map_id
	var map_path = "res://maps/" + formatted_map_name + ".scn"
	var initial_started_usec: int = Time.get_ticks_usec()
	# Network entity packets may arrive immediately, but instantiating dozens of
	# monsters while the first map/player/PaperDoll/HUD are initializing creates
	# main-thread contention. Queue them now and drain only after the critical
	# first-world setup is complete.
	if NetworkEntityManager and NetworkEntityManager.has_method("set_spawn_queue_paused"):
		NetworkEntityManager.set_spawn_queue_paused(true)

	_trace_map_change("initial_enter_started", 0, target_map_id)
	_report_initial_loading_progress(progress_callback, 5.0, "準備載入地圖")
	await get_tree().process_frame

	if not ResourceLoader.exists(map_path):
		_trace_map_change("initial_map_missing", 0, target_map_id)
		printerr("❌ [GameManager] 嚴重錯誤！找不到地圖檔案：", map_path)
		if NetworkEntityManager and NetworkEntityManager.has_method("set_spawn_queue_paused"):
			NetworkEntityManager.set_spawn_queue_paused(false)
		return false

	var request_error: int = ResourceLoader.load_threaded_request(map_path)
	if request_error != OK:
		_trace_map_change("initial_load_request_error", 0, target_map_id, {"error": request_error})
		if NetworkEntityManager and NetworkEntityManager.has_method("set_spawn_queue_paused"):
			NetworkEntityManager.set_spawn_queue_paused(false)
		return false

	var map_resource: PackedScene = null
	while true:
		var progress: Array = []
		var status: int = ResourceLoader.load_threaded_get_status(map_path, progress)
		if status == ResourceLoader.THREAD_LOAD_IN_PROGRESS:
			var threaded_progress := float(progress[0]) if not progress.is_empty() else 0.0
			_report_initial_loading_progress(progress_callback, 5.0 + threaded_progress * 18.0, "載入地圖資源")
			await get_tree().process_frame
			continue
		if status == ResourceLoader.THREAD_LOAD_LOADED:
			var load_get_started_usec: int = Time.get_ticks_usec()
			map_resource = ResourceLoader.load_threaded_get(map_path) as PackedScene
			_trace_map_change("initial_load_completed", 0, target_map_id, {
				"load_get_ms": float(Time.get_ticks_usec() - load_get_started_usec) / 1000.0,
			})
			_report_initial_loading_progress(progress_callback, 23.0, "地圖資源載入完成")
			await get_tree().process_frame
			break
		_trace_map_change("initial_load_failed", 0, target_map_id, {"status": status})
		if NetworkEntityManager and NetworkEntityManager.has_method("set_spawn_queue_paused"):
			NetworkEntityManager.set_spawn_queue_paused(false)
		return false

	if map_resource == null:
		printerr("[GameManager] 地圖資源載入失敗（可能有缺少或無法載入的相依資源）: ", map_path)
		if NetworkEntityManager and NetworkEntityManager.has_method("set_spawn_queue_paused"):
			NetworkEntityManager.set_spawn_queue_paused(false)
		return false

	await get_tree().process_frame
	var instantiate_started_usec: int = Time.get_ticks_usec()
	_report_initial_loading_progress(progress_callback, 26.0, "建立地圖")
	await get_tree().process_frame
	set_frame_gap_stage("initial_map_instantiate")
	current_map_node = map_resource.instantiate()

	# Blocking.dat is the authoritative navigation/collision source. Older map
	# scenes may still contain a huge legacy CollisionContainer TileMapLayer.
	# Remove it before the map enters SceneTree so Godot does not register/build
	# thousands of unused collision cells during initial add_child().
	var removed_collision_nodes: Array[String] = []
	for collision_name: String in ["CollisionContainer", "CollisionLayer"]:
		var legacy_collision := current_map_node.get_node_or_null(collision_name)
		if is_instance_valid(legacy_collision):
			current_map_node.remove_child(legacy_collision)
			legacy_collision.free()
			removed_collision_nodes.append(collision_name)
	if OS.is_debug_build() and not removed_collision_nodes.is_empty():
		print("[MAP_INIT_OPT] removed_collision_nodes=%s" % ",".join(removed_collision_nodes))

	# Keep the first world completely hidden while its nodes initialize.
	# The entry loading overlay remains the only visible surface until the
	# readiness gate at the end of this function has completed.
	current_map_node.visible = false
	var instantiate_ms: float = float(Time.get_ticks_usec() - instantiate_started_usec) / 1000.0
	_trace_map_change("initial_map_instantiated", 0, target_map_id, {
		"instantiate_ms": instantiate_ms,
	})

	await get_tree().process_frame
	var add_child_started_usec: int = Time.get_ticks_usec()
	_report_initial_loading_progress(progress_callback, 33.0, "初始化地圖")
	await get_tree().process_frame

	set_frame_gap_stage("initial_map_add_child")
	var root_add_started_usec: int = Time.get_ticks_usec()
	get_tree().root.add_child(current_map_node)
	var add_child_ms: float = float(Time.get_ticks_usec() - root_add_started_usec) / 1000.0
	if OS.is_debug_build():
		print("[MAP_INIT_TIMING] add_child_ms=%.2f" % add_child_ms)
	_trace_map_change("initial_map_added", 0, target_map_id, {
		"instantiate_ms": instantiate_ms,
		"add_child_ms": add_child_ms,
	})

	# Blocking.dat is authoritative for navigation. Do not rebuild a huge
	# TileMapLayer collision grid here; the player runtime collision mask does
	# not use those map collision layers.
	_report_initial_loading_progress(progress_callback, 38.0, "準備角色")
	await get_tree().process_frame

	_report_initial_loading_progress(progress_callback, 42.0, "建立角色")
	await get_tree().process_frame
	var player_scene = ResourceLoader.load("res://player.tscn")
	set_frame_gap_stage("player_instantiate")
	current_player = player_scene.instantiate()
	if current_player.has_method("parse_login_server_data"):
		current_player.parse_login_server_data(char_data)

	# SkillSync can arrive immediately after EnterWorldSuccess, before the
	# Player/HUD has been instantiated. Replay the cached authoritative list
	# now that PlayerData exists instead of losing the one-shot signal.
	if NetworkManager and NetworkManager.has_latest_skill_sync and "data_model" in current_player:
		current_player.data_model.update_skills(
			NetworkManager.latest_skill_sync
		)

	current_player.global_position = spawn_position
	if current_player.has_method("reset_map_navigation_state"):
		# EnterWorld's MapID is authoritative. Keep player navigation, cached
		# walkable position and loaded map in sync from the first world frame.
		current_player.reset_map_navigation_state(target_map_id, spawn_position)
	elif "target_position" in current_player:
		current_player.target_position = spawn_position

	_report_initial_loading_progress(progress_callback, 47.0, "初始化角色")
	await get_tree().process_frame
	set_frame_gap_stage("player_add_child")
	if current_map_node.has_node("Entities"):
		current_map_node.get_node("Entities").add_child(current_player)
	else:
		current_map_node.add_child(current_player)

	var camera = current_player.get_node_or_null("Camera2D")
	if camera:
		camera.make_current()
		camera.reset_smoothing()
		camera.force_update_scroll()

	_ensure_day_night_overlay()
	set_world_enhancement_enabled(_load_world_enhancement_setting())

	_report_initial_loading_progress(progress_callback, 54.0, "載入介面")
	await get_tree().process_frame
	var hud_scene = ResourceLoader.load("res://game_hud.tscn")
	set_frame_gap_stage("hud_instantiate")
	current_hud = hud_scene.instantiate()
	current_hud.visible = false
	_report_initial_loading_progress(progress_callback, 59.0, "建立遊戲介面")
	await get_tree().process_frame
	set_frame_gap_stage("hud_add_child")
	get_tree().root.add_child(current_hud)
	_report_initial_loading_progress(progress_callback, 64.0, "初始化遊戲介面")
	await get_tree().process_frame
	set_frame_gap_stage("hud_initialize")
	current_hud.initialize_hud(current_player)

	if CursorManager:
		CursorManager.set_cursor_state("normal")

	# Do not reveal the first world until every deferred/queued initialization
	# task is really complete. The entry loading overlay stays visible while
	# this readiness gate is running.
	_report_initial_loading_progress(progress_callback, 68.0, "載入角色外觀")
	var visual_wait_started_usec: int = Time.get_ticks_usec()
	while is_instance_valid(current_player) and current_player.has_method("is_initial_visual_ready") and not current_player.is_initial_visual_ready():
		var visual_elapsed_ms: float = float(Time.get_ticks_usec() - visual_wait_started_usec) / 1000.0
		_report_initial_loading_progress(
			progress_callback,
			minf(72.0, 68.0 + visual_elapsed_ms / 180.0),
			"載入角色外觀"
		)
		await get_tree().process_frame

	_report_initial_loading_progress(progress_callback, 73.0, "初始化遊戲資料")
	var hud_wait_started_usec: int = Time.get_ticks_usec()
	while is_instance_valid(current_hud) and current_hud.has_method("is_initial_hud_ready") and not current_hud.is_initial_hud_ready():
		var hud_elapsed_ms: float = float(Time.get_ticks_usec() - hud_wait_started_usec) / 1000.0
		_report_initial_loading_progress(
			progress_callback,
			minf(76.0, 73.0 + hud_elapsed_ms / 220.0),
			"初始化遊戲資料"
		)
		await get_tree().process_frame

	# Heavy optional windows are instantiated while the loading overlay is still
	# covering the world, one per frame. Their scenes were already prewarmed on
	# login/character-select screens, so first user opening is only a visibility
	# toggle instead of a first-load hitch.
	if is_instance_valid(current_hud) and current_hud.has_method("prepare_lazy_windows_before_reveal"):
		_report_initial_loading_progress(progress_callback, 77.0, "預先準備遊戲視窗")
		await current_hud.prepare_lazy_windows_before_reveal()

	_report_initial_loading_progress(progress_callback, 79.0, "套用地圖環境")
	await get_tree().process_frame
	_apply_current_festival_theme()
	await get_tree().process_frame

	_report_initial_loading_progress(progress_callback, 81.0, "初始化地圖物件")
	if BuildingManager:
		await _init_buildings_with_diagnostics(0, target_map_id)
	await get_tree().process_frame

	_report_initial_loading_progress(progress_callback, 83.0, "同步地圖角色")
	var initial_spawn_total: int = 0
	if NetworkEntityManager:
		if NetworkEntityManager.has_method("get_spawn_queue_remaining_count"):
			initial_spawn_total = int(NetworkEntityManager.get_spawn_queue_remaining_count())
		if NetworkEntityManager.has_method("set_initial_spawn_boost"):
			NetworkEntityManager.set_initial_spawn_boost(true)
		if NetworkEntityManager.has_method("set_spawn_queue_paused"):
			NetworkEntityManager.set_spawn_queue_paused(false)
	await get_tree().process_frame

	while NetworkEntityManager and NetworkEntityManager.has_method("is_initial_spawn_queue_idle") and not NetworkEntityManager.is_initial_spawn_queue_idle():
		var remaining_spawns: int = 0
		if NetworkEntityManager.has_method("get_spawn_queue_remaining_count"):
			remaining_spawns = int(NetworkEntityManager.get_spawn_queue_remaining_count())
			initial_spawn_total = maxi(initial_spawn_total, remaining_spawns)

		if remaining_spawns > 0:
			var spawn_progress: float = 0.0
			if initial_spawn_total > 0:
				spawn_progress = 1.0 - float(remaining_spawns) / float(initial_spawn_total)
			_report_initial_loading_progress(
				progress_callback,
				83.0 + clampf(spawn_progress, 0.0, 1.0) * 8.0,
				"同步地圖角色"
			)
		else:
			var settle_progress: float = 0.0
			if NetworkEntityManager.has_method("get_initial_spawn_settle_progress"):
				settle_progress = float(NetworkEntityManager.get_initial_spawn_settle_progress())
			_report_initial_loading_progress(
				progress_callback,
				91.0 + clampf(settle_progress, 0.0, 1.0) * 3.0,
				"確認地圖同步"
			)
		await get_tree().process_frame

	_report_initial_loading_progress(progress_callback, 94.0, "地圖角色同步完成")
	if NetworkEntityManager and NetworkEntityManager.has_method("set_initial_spawn_boost"):
		NetworkEntityManager.set_initial_spawn_boost(false)

	# Warm the first rendered world frame while EntryManager's loading overlay
	# still covers the screen. Setting these visible only at the final reveal
	# concentrated CanvasItem/render registration into a 200-260 ms hitch.
	_report_initial_loading_progress(progress_callback, 96.0, "預熱畫面")
	set_frame_gap_stage("world_render_warmup")
	if is_instance_valid(current_map_node):
		current_map_node.visible = true
	if is_instance_valid(current_hud):
		current_hud.visible = true
	await get_tree().process_frame
	await get_tree().process_frame

	_report_initial_loading_progress(progress_callback, 98.0, "載入背景音樂")
	await AudioManager.play_map_bgm(map_id.to_int())

	# The world has already rendered behind the loading overlay. Keep this stage
	# as a diagnostic marker; the eventual overlay fade should now be cheap.
	set_frame_gap_stage("world_reveal")
	await get_tree().process_frame

	# Optional heavy reference UI is no longer part of time-to-world. Warm it in
	# small batches only after the player can already see and control the world.
	if is_instance_valid(current_hud) and current_hud.has_method("start_post_reveal_background_prewarm"):
		current_hud.start_post_reveal_background_prewarm()

	set_frame_gap_stage("world_ready")
	_trace_map_change("initial_enter_completed", 0, target_map_id, {
		"total_ms": float(Time.get_ticks_usec() - initial_started_usec) / 1000.0,
	})
	_report_initial_loading_progress(progress_callback, 100.0, "載入完成")
	await get_tree().process_frame
	return true

func cleanup_and_return_to_login() -> void :
	_cancel_active_map_transition("cleanup_and_return_to_login")
	_cancel_train_travel("cleanup_and_return_to_login")
	AudioManager.stop_bgm()


	if is_instance_valid(current_player):
		current_player.queue_free()
		current_player = null


	if is_instance_valid(current_map_node):
		current_map_node.queue_free()
		current_map_node = null


	if is_instance_valid(current_hud):
		current_hud.queue_free()
		current_hud = null
	_remove_day_night_overlay()
	_remove_world_enhancement()


	if NetworkEntityManager:
		NetworkEntityManager.clear_map_entities()
	_clear_visual_runtime_caches()


	if GlobalTempData:
		GlobalTempData.selected_player_data = {}
		GlobalTempData.character_list = []


	get_tree().change_scene_to_file("res://entry_manager.tscn")

func _clear_visual_runtime_caches() -> void :
	if DollLoader and DollLoader.has_method("clear_runtime_cache"):
		DollLoader.clear_runtime_cache()
	if MonsterLoader and MonsterLoader.has_method("clear_runtime_cache"):
		MonsterLoader.clear_runtime_cache()
	if EffectLoader and EffectLoader.has_method("clear_runtime_cache"):
		EffectLoader.clear_runtime_cache()
	if PetLoader and PetLoader.has_method("clear_runtime_cache"):
		PetLoader.clear_runtime_cache()

func _ensure_day_night_overlay() -> void :
	if not is_instance_valid(current_day_night_overlay):
		current_day_night_overlay = DAY_NIGHT_OVERLAY_SCENE.instantiate() as CanvasLayer
		if current_day_night_overlay:
			get_tree().root.add_child(current_day_night_overlay)
	if is_instance_valid(current_day_night_overlay) and current_day_night_overlay.has_method("set_tracked_player"):
		current_day_night_overlay.set_tracked_player(current_player)

func _remove_day_night_overlay() -> void :
	if is_instance_valid(current_day_night_overlay):
		current_day_night_overlay.queue_free()
	current_day_night_overlay = null

func _load_world_enhancement_setting() -> bool:
	var config: = ConfigFile.new()
	if config.load(GameConstants.CLIENT_SETTINGS_PATH) != OK:
		return GameConstants.SETTING_DEFAULT_QUALITY_ENHANCEMENT_ENABLED
	return bool(config.get_value(
		GameConstants.SETTING_SECTION_GAME, 
		GameConstants.SETTING_KEY_QUALITY_ENHANCEMENT_ENABLED, 
		GameConstants.SETTING_DEFAULT_QUALITY_ENHANCEMENT_ENABLED
	))

func set_world_enhancement_enabled(enabled: bool) -> void :
	world_enhancement_enabled = enabled
	if world_enhancement_enabled:
		_ensure_world_enhancement()
	else:
		_remove_world_enhancement()

func _ensure_world_enhancement() -> void :
	if is_instance_valid(current_world_enhancement):
		return
	current_world_enhancement = WORLD_ENHANCEMENT_SCENE.instantiate() as CanvasLayer
	if current_world_enhancement:
		get_tree().root.add_child(current_world_enhancement)

func _remove_world_enhancement() -> void :
	if is_instance_valid(current_world_enhancement):
		current_world_enhancement.queue_free()
	current_world_enhancement = null

func _on_train_travel_started(data: Dictionary) -> void :
	if _train_travel_active or is_changing_map:
		return

	var travel_id: = str(data.get("TravelID", ""))
	var source_video: = str(data.get("SourceVideo", ""))
	var destination_video: = str(data.get("DestinationVideo", ""))
	if travel_id.is_empty() or not TRAIN_VIDEO_PATHS.has(source_video) or not TRAIN_VIDEO_PATHS.has(destination_video):
		printerr("[TrainTravel] 收到無效的火車行程資料。")
		return

	_train_travel_active = true
	_train_travel_id = travel_id
	_train_destination_video = destination_video
	_train_travel_phase = "departure"
	_close_npc_dialog_for_train()
	_lock_train_travel_input()

	var departure_played: = await _play_train_video(source_video)
	if not _train_travel_active or _train_travel_id != travel_id:
		return
	if not departure_played:
		_cancel_train_travel("departure_video_failed")
		return

	_train_travel_phase = "awaiting_map"
	NetworkManager.send_train_travel_ready(travel_id)
	_wait_for_train_map_timeout(travel_id)

func _wait_for_train_map_timeout(travel_id: String) -> void :
	await get_tree().create_timer(TRAIN_TRAVEL_MAP_TIMEOUT_SECONDS).timeout
	if _train_travel_active and _train_travel_id == travel_id and _train_travel_phase == "awaiting_map":
		printerr("[TrainTravel] 等待伺服器傳送逾時。")
		_cancel_train_travel("map_timeout")

func _close_npc_dialog_for_train() -> void :
	if is_instance_valid(current_hud) and current_hud.has_method("_on_npc_dialog_closed"):
		current_hud._on_npc_dialog_closed()

func _lock_train_travel_input() -> void :
	_train_player_process_state.clear()
	_train_hud_process_state.clear()
	if is_instance_valid(current_player):
		_train_player_process_state = {
			"physics": current_player.is_physics_processing(), 
			"input": current_player.is_processing_input(), 
			"unhandled_input": current_player.is_processing_unhandled_input(), 
		}
		current_player.set_physics_process(false)
		current_player.set_process_input(false)
		current_player.set_process_unhandled_input(false)
		if "target_position" in current_player:
			current_player.target_position = current_player.global_position
		current_player.velocity = Vector2.ZERO
	if is_instance_valid(current_hud):
		_train_hud_process_state = {
			"input": current_hud.is_processing_input(), 
			"unhandled_input": current_hud.is_processing_unhandled_input(), 
		}
		current_hud.set_process_input(false)
		current_hud.set_process_unhandled_input(false)

func _restore_train_travel_input() -> void :
	if is_instance_valid(current_player):
		current_player.set_physics_process(bool(_train_player_process_state.get("physics", true)))
		current_player.set_process_input(bool(_train_player_process_state.get("input", true)))
		current_player.set_process_unhandled_input(bool(_train_player_process_state.get("unhandled_input", true)))
	if is_instance_valid(current_hud):
		current_hud.set_process_input(bool(_train_hud_process_state.get("input", true)))
		current_hud.set_process_unhandled_input(bool(_train_hud_process_state.get("unhandled_input", true)))
	_train_player_process_state.clear()
	_train_hud_process_state.clear()

func _ensure_train_video_layer() -> bool:
	if is_instance_valid(_train_video_layer) and is_instance_valid(_train_video_player):
		return true

	_train_video_layer = CanvasLayer.new()
	_train_video_layer.layer = 1000
	_train_video_layer.name = "TrainTravelVideoLayer"

	var background: = ColorRect.new()
	background.color = Color.BLACK
	background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	background.mouse_filter = Control.MOUSE_FILTER_STOP
	_train_video_layer.add_child(background)

	_train_video_player = VideoStreamPlayer.new()
	_train_video_player.name = "TrainTravelVideo"
	_train_video_player.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_train_video_player.expand = true
	_train_video_player.mouse_filter = Control.MOUSE_FILTER_STOP
	_train_video_layer.add_child(_train_video_player)
	get_tree().root.add_child(_train_video_layer)
	return is_instance_valid(_train_video_player)

func _play_train_video(video_name: String) -> bool:
	if not _train_travel_active or not TRAIN_VIDEO_PATHS.has(video_name):
		return false
	var video_path: String = TRAIN_VIDEO_PATHS[video_name]
	if not ResourceLoader.exists(video_path) or not _ensure_train_video_layer():
		printerr("[TrainTravel] 找不到車站影片：", video_path)
		return false

	var video_stream: = load(video_path) as VideoStream
	if video_stream == null:
		printerr("[TrainTravel] 無法載入車站影片：", video_path)
		return false

	_train_video_player.stream = video_stream
	_train_video_finished = false
	_train_video_player.finished.connect(_on_train_video_finished, CONNECT_ONE_SHOT)
	_train_video_player.play()
	await get_tree().process_frame
	if not _train_video_player.is_playing() and not _train_video_finished:
		printerr("[TrainTravel] 車站影片無法開始播放：", video_path)
		return false

	var started_usec: = Time.get_ticks_usec()
	while _train_travel_active and is_instance_valid(_train_video_player) and not _train_video_finished:
		if not _train_video_player.is_playing():
			printerr("[TrainTravel] 車站影片未正常播放完畢：", video_path)
			return false
		if float(Time.get_ticks_usec() - started_usec) / 1000000.0 >= TRAIN_VIDEO_TIMEOUT_SECONDS:
			_train_video_player.stop()
			printerr("[TrainTravel] 車站影片播放逾時：", video_path)
			return false
		await get_tree().process_frame

	return _train_travel_active and is_instance_valid(_train_video_player) and _train_video_finished

func _on_train_video_finished() -> void :
	_train_video_finished = true

func _finish_train_travel_after_map_change() -> void :
	if not _train_travel_active:
		return
	_train_travel_phase = "arrival"
	var arrival_played: = await _play_train_video(_train_destination_video)
	if not arrival_played:
		printerr("[TrainTravel] 目的地車站影片播放失敗，已安全解除操作鎖。")
	_complete_train_travel()

func _complete_train_travel() -> void :
	_restore_train_travel_input()
	_clear_train_video_layer()
	_train_travel_active = false
	_train_travel_id = ""
	_train_destination_video = ""
	_train_travel_phase = ""

func _cancel_train_travel(_reason: String) -> void :
	if not _train_travel_active and not is_instance_valid(_train_video_layer):
		return
	if is_instance_valid(_train_video_player):
		_train_video_player.stop()
	_restore_train_travel_input()
	_clear_train_video_layer()
	_train_travel_active = false
	_train_travel_id = ""
	_train_destination_video = ""
	_train_travel_phase = ""

func _clear_train_video_layer() -> void :
	if is_instance_valid(_train_video_layer):
		_train_video_layer.queue_free()
	_train_video_layer = null
	_train_video_player = null
	_train_video_finished = false


func _on_change_map_received(target_map_id: int, target_x: float, target_y: float) -> void :
	var source_map_id: = 0
	if is_instance_valid(current_player) and current_player.data_model:
		source_map_id = current_player.data_model.map_id
	if is_changing_map:
		_trace_map_change("ignored_while_changing", source_map_id, target_map_id)
		return
	if _train_travel_active:
		if _train_travel_phase == "awaiting_map":
			_train_travel_phase = "loading_map"
		else:
			_cancel_train_travel("unexpected_map_change")
	_map_transition_serial += 1
	_active_map_transition_id = _map_transition_serial
	_map_transition_started_usec = Time.get_ticks_usec()
	_map_transition_last_stage_usec = _map_transition_started_usec
	max_recorded_progress = 0.0
	var transition_id: = _active_map_transition_id
	_trace_map_change("received", source_map_id, target_map_id)


	is_changing_map = true
	log_performance_checkpoint("map_transition_begin")


	if is_instance_valid(current_player):
		current_player.set_physics_process(false)
		if "target_position" in current_player:
			current_player.target_position = current_player.global_position
		current_player.velocity = Vector2.ZERO
		if current_player.has_method("sync_animation"):
			current_player.current_state = "idle"
			current_player.sync_animation()


	if is_instance_valid(current_hud) and current_hud.has_method("show_map_loading"):
		current_hud.show_map_loading(MAP_LOADING_FADE_IN_SECONDS)

	var formatted_map_name = "Map%02d" % target_map_id
	var map_path = "res://maps/" + formatted_map_name + ".scn"

	if not ResourceLoader.exists(map_path):
		_trace_map_change("map_missing", source_map_id, target_map_id)
		printerr("❌ [GameManager] 嚴重錯誤！找不到切換的地圖檔案：", map_path)
		is_changing_map = false
		if is_instance_valid(current_player): current_player.set_physics_process(true)
		if is_instance_valid(current_hud) and current_hud.has_method("hide_map_loading"): current_hud.hide_map_loading()
		_cancel_train_travel("map_missing")
		return


	var load_request_error: = ResourceLoader.load_threaded_request(map_path)
	_trace_map_change("load_requested", source_map_id, target_map_id)
	if load_request_error != OK:
		_trace_map_change("load_request_error", source_map_id, target_map_id, {"error": load_request_error})


	await get_tree().create_timer(MAP_LOADING_COVER_DELAY_SECONDS).timeout
	if not _is_map_transition_current(transition_id):
		return


	var clear_started_usec: = Time.get_ticks_usec()
	var cleared_entities: Dictionary = {}
	if NetworkEntityManager:
		cleared_entities = NetworkEntityManager.clear_map_entities()
	_trace_map_change("entities_cleared", source_map_id, target_map_id, {
		"duration_ms": float(Time.get_ticks_usec() - clear_started_usec) / 1000.0, 
		"entities": cleared_entities, 
	})

	var map_resource: PackedScene = null


	while true:
		var progress = []
		var status = ResourceLoader.load_threaded_get_status(map_path, progress)

		if status == ResourceLoader.THREAD_LOAD_IN_PROGRESS:

			if progress[0] > max_recorded_progress:
				max_recorded_progress = progress[0]


			if is_instance_valid(current_hud) and current_hud.has_method("set_map_loading_progress"):
				current_hud.set_map_loading_progress(minf(
					MAP_LOADING_RESOURCE_PROGRESS_MAX,
					max_recorded_progress * MAP_LOADING_RESOURCE_PROGRESS_MAX
				))

			await get_tree().process_frame
			if not _is_map_transition_current(transition_id):
				return

		elif status == ResourceLoader.THREAD_LOAD_LOADED:

			var load_get_started_usec: = Time.get_ticks_usec()
			map_resource = ResourceLoader.load_threaded_get(map_path)
			_trace_map_change("load_completed", source_map_id, target_map_id, {
				"load_get_ms": float(Time.get_ticks_usec() - load_get_started_usec) / 1000.0, 
				"progress": max_recorded_progress, 
			})
			if is_instance_valid(current_hud) and current_hud.has_method("set_map_loading_progress"):
				current_hud.set_map_loading_progress(MAP_LOADING_RESOURCE_PROGRESS_MAX)
			break

		else:
			_trace_map_change("load_failed", source_map_id, target_map_id)
			printerr("❌ [GameManager] 地圖背景載入失敗！狀態碼：", status)
			is_changing_map = false


			if is_instance_valid(current_player): current_player.set_physics_process(true)
			if is_instance_valid(current_hud) and current_hud.has_method("hide_map_loading"): current_hud.hide_map_loading()
			_cancel_train_travel("map_load_failed")
			return




	if is_instance_valid(current_player):
		var player_parent: = current_player.get_parent()
		if is_instance_valid(player_parent):
			player_parent.remove_child(current_player)

	var old_map: = current_map_node
	current_map_node = null
	if is_instance_valid(old_map):
		var old_map_parent: = old_map.get_parent()
		if is_instance_valid(old_map_parent):
			old_map_parent.remove_child(old_map)
		old_map.queue_free()
	_trace_map_change("old_map_teardown_requested", source_map_id, target_map_id)

	await get_tree().process_frame
	if not _is_map_transition_current(transition_id):
		return
	# Keep reusable visual caches across normal map transitions. Clearing them
	# here forces large SpriteFrames/Texture rebuilds on the next map and can
	# stall the render driver while add_child/_ready runs.
	_trace_map_change("old_map_teardown_completed", source_map_id, target_map_id, {
		"old_map_still_valid": is_instance_valid(old_map), 
	})

	if is_instance_valid(current_hud) and current_hud.has_method("set_map_loading_progress"):
		current_hud.set_map_loading_progress(MAP_LOADING_INSTANTIATE_PROGRESS)
	await get_tree().process_frame
	if not _is_map_transition_current(transition_id):
		return

	var instantiate_started_usec: = Time.get_ticks_usec()
	current_map_node = map_resource.instantiate()
	var instantiate_ms: = float(Time.get_ticks_usec() - instantiate_started_usec) / 1000.0

	# Match the first-map loading path: Blocking.dat is authoritative, so the
	# legacy collision TileMapLayers are unused. Remove them BEFORE add_child
	# to avoid registering thousands of unnecessary physics cells on teleport.
	var removed_legacy_layers: Array[String] = []
	for collision_name: String in ["CollisionContainer", "CollisionLayer"]:
		var legacy_collision := current_map_node.get_node_or_null(collision_name)
		if is_instance_valid(legacy_collision):
			current_map_node.remove_child(legacy_collision)
			legacy_collision.free()
			removed_legacy_layers.append(collision_name)
	if OS.is_debug_build() and not removed_legacy_layers.is_empty():
		print("[MAP_TRANSITION_OPT] map=%d removed_collision_nodes=%s" % [
			target_map_id, ",".join(removed_legacy_layers)
		])

	if is_instance_valid(current_hud) and current_hud.has_method("set_map_loading_progress"):
		current_hud.set_map_loading_progress(MAP_LOADING_ADD_CHILD_PROGRESS)
	await get_tree().process_frame
	if not _is_map_transition_current(transition_id):
		return

	var add_child_started_usec: = Time.get_ticks_usec()
	get_tree().root.add_child(current_map_node)
	var add_child_ms: = float(Time.get_ticks_usec() - add_child_started_usec) / 1000.0
	_trace_map_change("new_map_added", source_map_id, target_map_id, {
		"instantiate_ms": instantiate_ms, 
		"add_child_ms": add_child_ms, 
	})
	log_performance_checkpoint("map_transition_map_added")

	if is_instance_valid(current_hud) and current_hud.has_method("set_map_loading_progress"):
		current_hud.set_map_loading_progress(MAP_LOADING_FINALIZE_PROGRESS)
	await get_tree().process_frame
	if not _is_map_transition_current(transition_id):
		return

	# Festival replacement may traverse a large map tree. Keep it out of the
	# blocking map-transition critical path so the loading UI remains responsive.
	call_deferred("_apply_current_festival_theme")

	if BuildingManager: call_deferred("_init_buildings_with_diagnostics", source_map_id, target_map_id)


	if is_instance_valid(current_player):
		if current_map_node.has_node("Entities"):
			current_map_node.get_node("Entities").add_child(current_player)
		else:
			current_map_node.add_child(current_player)

		var new_pos = Vector2(target_x, target_y)
		current_player.global_position = new_pos
		if "target_position" in current_player:
			current_player.target_position = new_pos
			if not current_player.get("is_dead"):
				current_player.current_state = "idle"
			current_player.velocity = Vector2.ZERO

		if current_player.has_method("reset_map_navigation_state"):
			current_player.reset_map_navigation_state(target_map_id, new_pos)
		elif "data_model" in current_player and current_player.data_model:
			current_player.data_model.map_id = target_map_id

		var camera = current_player.get_node_or_null("Camera2D")
		if camera:
			camera.make_current()
			camera.reset_smoothing()
			camera.force_update_scroll()

	if is_instance_valid(current_hud) and current_hud.has_method("update_map_info"):
		current_hud.update_map_info(target_map_id, current_map_node.name)

	if CursorManager:
		CursorManager.set_cursor_state("normal")

	var bgm_started_usec: = Time.get_ticks_usec()
	AudioManager.play_map_bgm(target_map_id)
	_trace_map_change("bgm_request_started", source_map_id, target_map_id, {
		"duration_ms": float(Time.get_ticks_usec() - bgm_started_usec) / 1000.0, 
	})


	await get_tree().process_frame
	if not _is_map_transition_current(transition_id):
		return

	if is_instance_valid(current_player) and not _train_travel_active:
		current_player.set_physics_process(true)


	if NetworkManager and NetworkManager.has_method("note_map_loading_complete"):
		NetworkManager.note_map_loading_complete(_active_map_transition_id, target_map_id)
	is_changing_map = false
	_trace_map_change("resumed", source_map_id, target_map_id)


	var loading_elapsed_seconds: = float(Time.get_ticks_usec() - _map_transition_started_usec) / 1000000.0
	var minimum_display_wait: = maxf(0.0, MAP_LOADING_MIN_VISIBLE_SECONDS - loading_elapsed_seconds)
	_trace_map_change("minimum_display_wait", source_map_id, target_map_id, {
		"elapsed_seconds": loading_elapsed_seconds, 
		"wait_seconds": minimum_display_wait, 
	})
	if minimum_display_wait > 0.0:
		await get_tree().create_timer(minimum_display_wait).timeout
		if _active_map_transition_id != transition_id or not is_inside_tree():
			return
	if is_instance_valid(current_hud) and current_hud.has_method("hide_map_loading"):
		current_hud.hide_map_loading()
	_trace_map_change("loading_hide_requested", source_map_id, target_map_id)
	log_performance_checkpoint("map_transition_ready")
	if _train_travel_active and _train_travel_phase == "loading_map":
		await _finish_train_travel_after_map_change()
