
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

var _train_travel_active: bool = false
var _train_travel_id: String = ""
var _train_destination_video: String = ""
var _train_travel_phase: String = ""
var _train_video_layer: CanvasLayer = null
var _train_video_player: VideoStreamPlayer = null
var _train_video_finished: bool = false
var _train_player_process_state: Dictionary = {}
var _train_hud_process_state: Dictionary = {}

func set_frame_gap_stage(stage: String) -> void :
	_frame_gap_stage = stage
	_frame_gap_stage_since_usec = Time.get_ticks_usec()

func _process(_delta: float) -> void :
	var now_usec := Time.get_ticks_usec()
	if _frame_gap_last_usec > 0:
		var gap_usec := now_usec - _frame_gap_last_usec
		if gap_usec >= FRAME_GAP_WARN_USEC:
			var stage_age_ms := float(now_usec - _frame_gap_stage_since_usec) / 1000.0 if _frame_gap_stage_since_usec > 0 else -1.0
			var line := "[FRAME_GAP] gap_ms=%.2f stage=%s stage_age_ms=%.2f memory_mb=%.2f" % [
				float(gap_usec) / 1000.0,
				_frame_gap_stage,
				stage_age_ms,
				_get_process_memory_mb()
			]
			print(line)
			_append_transition_log(line)
	_frame_gap_last_usec = now_usec

func _get_process_memory_mb() -> float:
	return float(Performance.get_monitor(Performance.MEMORY_STATIC)) / 1048576.0

func _append_transition_log(line: String) -> void :
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





func _ready() -> void :

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
	return FestivalThemeManager.apply_theme_to_map(current_map_node, current_festival_theme)

func _init_buildings_with_diagnostics(source_map_id: int, target_map_id: int) -> void :
	if not BuildingManager:
		return
	var started_usec: = Time.get_ticks_usec()
	BuildingManager.init_buildings()
	_trace_map_change("buildings_initialized", source_map_id, target_map_id, {
		"duration_ms": float(Time.get_ticks_usec() - started_usec) / 1000.0, 
	})

func enter_game_world(char_data: Dictionary, map_id: String, spawn_position: Vector2) -> bool:
	var raw_num_str = map_id.replace("map", "")
	var target_map_id: int = int(raw_num_str)
	var formatted_map_name = "Map%02d" % target_map_id
	var map_path = "res://maps/" + formatted_map_name + ".scn"
	var initial_started_usec: int = Time.get_ticks_usec()

	_trace_map_change("initial_enter_started", 0, target_map_id)

	if not ResourceLoader.exists(map_path):
		_trace_map_change("initial_map_missing", 0, target_map_id)
		printerr("❌ [GameManager] 嚴重錯誤！找不到地圖檔案：", map_path)
		return false

	var request_error: int = ResourceLoader.load_threaded_request(map_path)
	if request_error != OK:
		_trace_map_change("initial_load_request_error", 0, target_map_id, {"error": request_error})
		return false

	var map_resource: PackedScene = null
	while true:
		var progress: Array = []
		var status: int = ResourceLoader.load_threaded_get_status(map_path, progress)
		if status == ResourceLoader.THREAD_LOAD_IN_PROGRESS:
			await get_tree().process_frame
			continue
		if status == ResourceLoader.THREAD_LOAD_LOADED:
			var load_get_started_usec: int = Time.get_ticks_usec()
			map_resource = ResourceLoader.load_threaded_get(map_path) as PackedScene
			_trace_map_change("initial_load_completed", 0, target_map_id, {
				"load_get_ms": float(Time.get_ticks_usec() - load_get_started_usec) / 1000.0,
			})
			break
		_trace_map_change("initial_load_failed", 0, target_map_id, {"status": status})
		return false

	if map_resource == null:
		printerr("[GameManager] 地圖資源載入失敗（可能有缺少或無法載入的相依資源）: ", map_path)
		return false

	await get_tree().process_frame
	var instantiate_started_usec: int = Time.get_ticks_usec()
	set_frame_gap_stage("initial_map_instantiate")
	current_map_node = map_resource.instantiate()
	var instantiate_ms: float = float(Time.get_ticks_usec() - instantiate_started_usec) / 1000.0
	_trace_map_change("initial_map_instantiated", 0, target_map_id, {
		"instantiate_ms": instantiate_ms,
	})

	await get_tree().process_frame
	var add_child_started_usec: int = Time.get_ticks_usec()
	set_frame_gap_stage("initial_map_add_child")
	get_tree().root.add_child(current_map_node)
	var add_child_ms: float = float(Time.get_ticks_usec() - add_child_started_usec) / 1000.0
	_trace_map_change("initial_map_added", 0, target_map_id, {
		"instantiate_ms": instantiate_ms,
		"add_child_ms": add_child_ms,
	})

	# Theme/building traversal can be expensive; keep them out of the blocking
	# initial-map critical path.
	call_deferred("_apply_current_festival_theme")
	if BuildingManager:
		BuildingManager.call_deferred("init_buildings")

	var player_scene = ResourceLoader.load("res://player.tscn")
	set_frame_gap_stage("player_instantiate")
	current_player = player_scene.instantiate()
	if current_player.has_method("parse_login_server_data"):
		current_player.parse_login_server_data(char_data)

	current_player.global_position = spawn_position
	if "target_position" in current_player:
		current_player.target_position = spawn_position

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

	var hud_scene = ResourceLoader.load("res://game_hud.tscn")
	set_frame_gap_stage("hud_instantiate")
	current_hud = hud_scene.instantiate()
	set_frame_gap_stage("hud_add_child")
	get_tree().root.add_child(current_hud)
	set_frame_gap_stage("hud_initialize")
	current_hud.initialize_hud(current_player)

	if CursorManager:
		CursorManager.set_cursor_state("normal")

	set_frame_gap_stage("world_running")
	AudioManager.play_map_bgm(map_id.to_int())
	_trace_map_change("initial_enter_completed", 0, target_map_id, {
		"total_ms": float(Time.get_ticks_usec() - initial_started_usec) / 1000.0,
	})
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


			if is_instance_valid(current_hud) and current_hud.loading_bar:
				current_hud.loading_bar.value = minf(
					MAP_LOADING_RESOURCE_PROGRESS_MAX,
					max_recorded_progress * MAP_LOADING_RESOURCE_PROGRESS_MAX
				)

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
			if is_instance_valid(current_hud) and current_hud.loading_bar:
				current_hud.loading_bar.value = MAP_LOADING_RESOURCE_PROGRESS_MAX
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

	if is_instance_valid(current_hud) and current_hud.loading_bar:
		current_hud.loading_bar.value = MAP_LOADING_INSTANTIATE_PROGRESS
	await get_tree().process_frame
	if not _is_map_transition_current(transition_id):
		return

	var instantiate_started_usec: = Time.get_ticks_usec()
	current_map_node = map_resource.instantiate()
	var instantiate_ms: = float(Time.get_ticks_usec() - instantiate_started_usec) / 1000.0

	if is_instance_valid(current_hud) and current_hud.loading_bar:
		current_hud.loading_bar.value = MAP_LOADING_ADD_CHILD_PROGRESS
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

	if is_instance_valid(current_hud) and current_hud.loading_bar:
		current_hud.loading_bar.value = MAP_LOADING_FINALIZE_PROGRESS
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

		if "data_model" in current_player and current_player.data_model:
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
	if _train_travel_active and _train_travel_phase == "loading_map":
		await _finish_train_travel_after_map_change()
