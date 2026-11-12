
extends Node

const UNFOCUS_PRESENTATION_POLICY = preload("res://scripts/unfocus_presentation_policy.gd")

const MAP_LOADING_FADE_IN_SECONDS: = 0.3
const MAP_LOADING_COVER_DELAY_SECONDS: = 0.35
const MAP_LOADING_MIN_VISIBLE_SECONDS: = 0.55
const MAP_LOADING_RESOURCE_PROGRESS_MAX: = 90.0
const MAP_LOADING_INSTANTIATE_PROGRESS: = 94.0
const MAP_LOADING_ADD_CHILD_PROGRESS: = 97.0
const MAP_LOADING_FINALIZE_PROGRESS: = 99.0
# Loading must not wait forever for a continuously refilled spawn queue.
# Remaining entities stay in the live queue and continue spawning normally.
const INITIAL_SPAWN_GATE_MAX_MSEC: int = 12000
const TRAIN_TRAVEL_MAP_TIMEOUT_SECONDS: = 15.0
const TRAIN_VIDEO_TIMEOUT_SECONDS: = 120.0
const WORLD_ENHANCEMENT_SCENE: = preload("res://world_enhancement.tscn")
const AFK_VISUAL_CACHE = preload("res://scripts/afk_visual_cache.gd")
const AFK_RENDER_SHELL = preload("res://scripts/afk_render_shell.gd")
const WORLD_VISUAL_AUDIT = preload("res://scripts/world_visual_audit.gd")
const AFK_MEMORY_AUDIT = preload("res://scripts/afk_memory_audit.gd")
const MAP_VISUAL_AOI_STREAMER = preload("res://scripts/map_visual_aoi_streamer.gd")
const AFK_MONITOR_FPS: int = 20
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
signal performance_profile_changed(profile: Dictionary)

var current_map_node: Node2D = null
var current_player: CharacterBody2D = null
var current_hud: CanvasLayer = null
var _afk_active: bool = false
var _afk_restoring: bool = false
var _afk_started_msec: int = 0
var _afk_map_cache = AFK_VISUAL_CACHE.new()
var _afk_render_shell = AFK_RENDER_SHELL.new()
var _afk_map_path: String = ""
var _afk_saved_profile: Dictionary = {}
var _afk_visual_stats: Dictionary = {}
var _afk_master_was_muted_before: bool = false
var _afk_memory_baseline: Dictionary = {}
var _afk_forced_draw_count: int = 0
var _last_world_map_audit_path: String = ""
var _pending_afk_working_set_trim: bool = false
var _windows_trim_inflight: bool = false
var _map_visual_aoi_streamer = MAP_VISUAL_AOI_STREAMER.new()
var _map_visual_aoi_log_elapsed: float = 0.0



func log_world_visual_audit(phase: String) -> void:
	if not OS.is_debug_build():
		return
	var total_nodes: int = int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var active_entity_count: int = NetworkEntityManager.active_entities.size() if NetworkEntityManager else 0
	var expensive_phase: bool = phase in ["logout_before", "afk_settled_3s", "afk_restore_complete"]
	if (expensive_phase and total_nodes > 2000) or total_nodes > 5000:
		print("[WORLD_VISUAL_AUDIT] phase=%s pid=%d skipped_heavy=true total_nodes=%d active_entities=%d reason=protect_fixed_cadence" % [
			phase, OS.get_process_id(), total_nodes, active_entity_count,
		])
		return
	var map_data: Dictionary = WORLD_VISUAL_AUDIT.snapshot(current_map_node)
	var hud_data: Dictionary = WORLD_VISUAL_AUDIT.snapshot(current_hud)
	var player_data: Dictionary = WORLD_VISUAL_AUDIT.snapshot(current_player)
	var entry_data: Dictionary = WORLD_VISUAL_AUDIT.snapshot(get_tree().current_scene)
	var active_entities: int = NetworkEntityManager.active_entities.size() if NetworkEntityManager else 0
	var cached_map: bool = (
		not _last_world_map_audit_path.is_empty()
		and ResourceLoader.has_cached(_last_world_map_audit_path)
	)
	var audit_texture_mb: float = float(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED)) / 1048576.0
	var audit_buffer_mb: float = float(Performance.get_monitor(Performance.RENDER_BUFFER_MEM_USED)) / 1048576.0
	var audit_video_mb: float = float(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)) / 1048576.0
	var audit_misc_mb: float = maxf(0.0, audit_video_mb - audit_texture_mb - audit_buffer_mb)
	print("[WORLD_VISUAL_AUDIT] phase=%s pid=%d map_nodes=%d map_sprite=%d map_animated=%d map_texture_nodes=%d map_unique_textures=%d map_sprite_frames=%d hud_nodes=%d hud_controls=%d hud_viewports=%d hud_texture_nodes=%d player_nodes=%d player_animated=%d entry_nodes=%d active_entities=%d prewarmed_windows=%d cached_map=%s cached_player=%s cached_hud=%s godot_static_mb=%.2f godot_texture_mb=%.2f godot_buffer_mb=%.2f godot_video_misc_mb=%.2f godot_video_mb=%.2f metric_scope=godot_not_windows_working_set" % [
		phase, OS.get_process_id(),
		int(map_data["nodes"]), int(map_data["sprite2d"]),
		int(map_data["animated_sprites"]), int(map_data["texture_rects"]),
		int(map_data["unique_textures"]), int(map_data["unique_sprite_frames"]),
		int(hud_data["nodes"]), int(hud_data["controls"]), int(hud_data["subviewports"]),
		int(hud_data["texture_rects"]),
		int(player_data["nodes"]), int(player_data["animated_sprites"]),
		int(entry_data["nodes"]), active_entities,
		_prewarmed_hud_windows.size(),
		str(cached_map),
		str(ResourceLoader.has_cached("res://player.tscn")),
		str(ResourceLoader.has_cached("res://game_hud.tscn")),
		_get_process_memory_mb(),
		audit_texture_mb,
		audit_buffer_mb,
		audit_misc_mb,
		audit_video_mb,
	])
	# A named list of top retained visual resource paths makes the next
	# memory profile actionable without changing render or network behavior.
	print("[WORLD_VISUAL_ASSETS] phase=%s map=%s hud=%s player=%s entry=%s" % [
		phase,
		WORLD_VISUAL_AUDIT.top_live_visual_resources(current_map_node),
		WORLD_VISUAL_AUDIT.top_live_visual_resources(current_hud),
		WORLD_VISUAL_AUDIT.top_live_visual_resources(current_player),
		WORLD_VISUAL_AUDIT.top_live_visual_resources(get_tree().current_scene)
	])


func _windows_memory_trim_helper_path() -> String:
	if OS.get_name() != "Windows":
		return ""

	var packaged: String = OS.get_executable_path().get_base_dir().path_join("SGO.WorkingSetTrimmer.exe")
	if FileAccess.file_exists(packaged):
		return packaged

	var client_root: String = ProjectSettings.globalize_path("res://")
	var helper_root: String = client_root.path_join("../tools/SGO.WorkingSetTrimmer").simplify_path()
	var candidates := PackedStringArray([
		helper_root.path_join("bin/Release/net8.0/SGO.WorkingSetTrimmer.exe"),
		helper_root.path_join("bin/Debug/net8.0/SGO.WorkingSetTrimmer.exe"),
		helper_root.path_join("bin/Release/net8.0/win-x64/publish/SGO.WorkingSetTrimmer.exe"),
	])
	for candidate in candidates:
		var normalized: String = candidate.simplify_path()
		if FileAccess.file_exists(normalized):
			return normalized
	return ""


func _collect_windows_memory_trim_result(reason: String, result_path: String) -> void:
	var tree: SceneTree = get_tree()
	if tree == null:
		_windows_trim_inflight = false
		return
	for _attempt in range(30):
		await tree.create_timer(0.1).timeout
		if FileAccess.file_exists(result_path):
			var raw: String = FileAccess.get_file_as_string(result_path).strip_edges()
			var parts: PackedStringArray = raw.split("|", true, 5)
			var success: bool = parts.size() >= 5 and parts[0].to_lower() == "true"
			var before_bytes: float = float(parts[1]) if parts.size() >= 2 else 0.0
			var after_bytes: float = float(parts[2]) if parts.size() >= 3 else 0.0
			var private_before_bytes: float = float(parts[3]) if parts.size() >= 4 else 0.0
			var private_after_bytes: float = float(parts[4]) if parts.size() >= 5 else 0.0
			var error_text: String = parts[5] if parts.size() >= 6 else ""
			if OS.is_debug_build():
				print("[OS_MEMORY_TRIM_RESULT] reason=%s success=%s working_set_mb=%.2f->%.2f ws_released_mb=%.2f private_mb=%.2f->%.2f private_released_mb=%.2f error=%s" % [
					reason,
					str(success),
					before_bytes / 1048576.0,
					after_bytes / 1048576.0,
					maxf(0.0, before_bytes - after_bytes) / 1048576.0,
					private_before_bytes / 1048576.0,
					private_after_bytes / 1048576.0,
					maxf(0.0, private_before_bytes - private_after_bytes) / 1048576.0,
					error_text,
				])
			DirAccess.remove_absolute(result_path)
			_windows_trim_inflight = false
			return
	if OS.is_debug_build():
		print("[OS_MEMORY_TRIM_RESULT] reason=%s success=false error=timeout" % reason)
	_windows_trim_inflight = false


func _request_windows_memory_trim(reason: String) -> bool:
	if OS.get_name() != "Windows" or _windows_trim_inflight:
		return false
	var helper_path: String = _windows_memory_trim_helper_path()
	if helper_path.is_empty():
		if OS.is_debug_build():
			print("[OS_MEMORY_TRIM] reason=%s requested=false helper_missing=true backend=native_only" % reason)
		return false

	var result_path: String = ProjectSettings.globalize_path(
		"user://native_working_set_trim_%d_%d.txt" % [OS.get_process_id(), Time.get_ticks_msec()]
	)
	var helper_pid: int = OS.create_process(
		helper_path,
		PackedStringArray([str(OS.get_process_id()), result_path]),
		false
	)
	if helper_pid <= 0:
		if OS.is_debug_build():
			print("[OS_MEMORY_TRIM] reason=%s requested=false helper_launch_failed=true backend=native" % reason)
		return false

	_windows_trim_inflight = true
	if OS.is_debug_build():
		print("[OS_MEMORY_TRIM] reason=%s requested=true backend=native helper_pid=%d target_pid=%d main_thread_wait=false" % [
			reason, helper_pid, OS.get_process_id(),
		])
	_collect_windows_memory_trim_result(reason, result_path)
	return true

func _run_pending_afk_trim_after_focus_out() -> void:
	var tree: SceneTree = get_tree()
	if tree == null:
		return
	await tree.create_timer(1.0).timeout
	if not _pending_afk_working_set_trim or not _afk_active or not _background_mode_active:
		return
	_pending_afk_working_set_trim = false
	_request_windows_memory_trim("afk_unfocused")


func _settle_released_memory(reason: String) -> void:
	# Never launch an external helper or force renderer synchronization here.
	# Real-device logs showed those attempts caused 1.5-2.4 s frame gaps and
	# pulled fixed physics below 60 Hz. At this point we only record Godot's
	# already-settled live allocation after one deferred frame.
	var tree: SceneTree = get_tree()
	if tree == null:
		return
	await tree.process_frame
	var measured: Dictionary = _afk_memory_sample()
	if OS.is_debug_build():
		print("[MEMORY_SETTLE] reason=%s static_mb=%.2f texture_mb=%.2f buffer_mb=%.2f video_misc_mb=%.2f video_mb=%.2f external_trim=deferred_native renderer_force_sync=false" % [
			reason,
			float(measured.get("godot_static_mb", 0.0)),
			float(measured.get("godot_texture_mb", 0.0)),
			float(measured.get("godot_buffer_mb", 0.0)),
			float(measured.get("godot_video_misc_mb", 0.0)),
			float(measured.get("godot_video_mb", 0.0)),
		])

	if reason == "afk_settled_3s":
		if not _background_mode_active:
			_pending_afk_working_set_trim = true
			if OS.is_debug_build():
				print("[OS_MEMORY_TRIM] reason=afk_settled_3s deferred_until_unfocused=true")
		else:
			_request_windows_memory_trim("afk_settled_unfocused")
	elif reason == "logout_settled":
		_request_windows_memory_trim("logout_settled")

func _audit_login_after_logout() -> void:
	# The freed world and a newly instantiated EntryManager do not settle in
	# the same frame. Record after deferred Node destruction has completed.
	await get_tree().process_frame
	await get_tree().process_frame
	await get_tree().create_timer(0.5).timeout
	log_world_visual_audit("logout_settled")
	_log_afk_memory_audit("logout_settled")
	await _settle_released_memory("logout_settled")


func _afk_memory_sample() -> Dictionary:
	# These are Godot allocator/resource counters, NOT Windows working set
	# and NOT OS-level process dedicated VRAM. Split the video total so a
	# post-world high-water mark can be distinguished from live textures.
	var texture_mb: float = float(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED)) / 1048576.0
	var buffer_mb: float = float(Performance.get_monitor(Performance.RENDER_BUFFER_MEM_USED)) / 1048576.0
	var video_mb: float = float(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)) / 1048576.0
	return {
		"godot_static_mb": float(Performance.get_monitor(Performance.MEMORY_STATIC)) / 1048576.0,
		"godot_peak_mb": float(Performance.get_monitor(Performance.MEMORY_STATIC_MAX)) / 1048576.0,
		"godot_texture_mb": texture_mb,
		"godot_buffer_mb": buffer_mb,
		"godot_video_mb": video_mb,
		"godot_video_misc_mb": maxf(0.0, video_mb - texture_mb - buffer_mb),
		"resource_objects": int(Performance.get_monitor(Performance.OBJECT_RESOURCE_COUNT)),
		"node_objects": int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT)),
	}


func _log_afk_memory(stage: String) -> void:
	if not OS.is_debug_build():
		return
	var measured: Dictionary = _afk_memory_sample()
	var before: Dictionary = _afk_memory_baseline
	var stats: Dictionary = _afk_visual_stats
	print("[AFK_MEMORY] stage=%s pid=%d godot_static_mb=%.2f delta_static_mb=%+.2f godot_static_peak_mb=%.2f godot_texture_mb=%.2f delta_texture_mb=%+.2f godot_buffer_mb=%.2f godot_video_misc_mb=%.2f godot_video_mb=%.2f delta_video_mb=%+.2f resource_objects=%d delta_resources=%+d node_objects=%d released_refs=%d retained_embedded=%d retained_generated=%d retained_missing=%d excluded_scripted_subtrees=%d excluded_collision_subtrees=%d excluded_entity_subtrees=%d visited_nodes=%d visual_candidates=%d resource_candidates=%d metric_scope=godot_not_windows_working_set" % [
		stage, OS.get_process_id(),
		float(measured.get("godot_static_mb", 0.0)),
		float(measured.get("godot_static_mb", 0.0)) - float(before.get("godot_static_mb", 0.0)),
		float(measured.get("godot_peak_mb", 0.0)),
		float(measured.get("godot_texture_mb", 0.0)),
		float(measured.get("godot_texture_mb", 0.0)) - float(before.get("godot_texture_mb", 0.0)),
		float(measured.get("godot_buffer_mb", 0.0)),
		float(measured.get("godot_video_misc_mb", 0.0)),
		float(measured.get("godot_video_mb", 0.0)),
		float(measured.get("godot_video_mb", 0.0)) - float(before.get("godot_video_mb", 0.0)),
		int(measured.get("resource_objects", 0)),
		int(measured.get("resource_objects", 0)) - int(before.get("resource_objects", 0)),
		int(measured.get("node_objects", 0)),
		int(stats.get("released", 0)),
		int(stats.get("retained_embedded", 0)),
		int(stats.get("retained_generated", 0)),
		int(stats.get("retained_missing", 0)),
		int(stats.get("excluded_scripted", 0)),
		int(stats.get("excluded_collision", 0)),
		int(stats.get("excluded_entities", 0)),
		int(stats.get("visited_nodes", 0)),
		int(stats.get("visual_candidates", 0)),
		int(stats.get("resource_candidates", 0))
	])


func _afk_memory_audit_cache_sources() -> Array:
	var sources: Array = [
		{"name": "GameManager._prewarmed_hud_windows", "value": _prewarmed_hud_windows},
	]
	for spec in [
		["MonsterLoader._cache", MonsterLoader, "_cache"],
		["DollLoader._cache", DollLoader, "_cache"],
		["DollLoader._sr_cache", DollLoader, "_sr_cache"],
		["DollLoader._sr_effect_cache", DollLoader, "_sr_effect_cache"],
		["PetLoader._frames_cache", PetLoader, "_frames_cache"],
		["EffectLoader._effect_cache", EffectLoader, "_effect_cache"],
		["ItemLoader._icon_cache", ItemLoader, "_icon_cache"],
		["ItemLoader._photo_cache", ItemLoader, "_photo_cache"],
		["ItemLoader._ground_cache", ItemLoader, "_ground_cache"],
		["SkillLoader._icon_cache", SkillLoader, "_icon_cache"],
		["AudioManager.se_cache", AudioManager, "se_cache"],
		["AudioManager._map_bgm_cache", AudioManager, "_map_bgm_cache"],
	]:
		var owner: Object = spec[1] as Object
		if is_instance_valid(owner):
			sources.append({"name": str(spec[0]), "value": owner.get(str(spec[2]))})
	sources.append({
		"name": "PaperDollEngine.visual_runtime",
		"value": PaperDollEngine.visual_runtime_cache_for_audit(),
	})
	sources.append({
		"name": "FestivalThemeManager._texture_cache",
		"value": FestivalThemeManager.texture_cache_for_audit(),
	})
	return sources


func _log_afk_memory_audit(stage: String) -> void:
	if not OS.is_debug_build():
		return
	var total_nodes: int = int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var active_entity_count: int = NetworkEntityManager.active_entities.size() if NetworkEntityManager else 0
	var expensive_stage: bool = stage in [
		"afk_settled_3s", "afk_restore_complete", "logout_before"
	]
	if (expensive_stage and total_nodes > 2000) or total_nodes > 5000:
		print("[AFK_MEMORY_AUDIT] stage=%s pid=%d skipped_heavy=true total_nodes=%d active_entities=%d reason=protect_fixed_cadence" % [
			stage, OS.get_process_id(), total_nodes, active_entity_count,
		])
		return
	var roots: Dictionary = {
		"map": current_map_node,
		"hud": current_hud,
		"player": current_player,
		"scene": get_tree().current_scene,
	}
	var report: Dictionary = AFK_MEMORY_AUDIT.capture(
		roots, _afk_memory_audit_cache_sources(), 16
	)
	print("[AFK_MEMORY_AUDIT] stage=%s pid=%d resources=%d textures=%d sprite_frames=%d estimated_texture_mb=%.2f note=uncompressed_texture_estimate_not_windows_working_set" % [
		stage, OS.get_process_id(),
		int(report.get("resources", 0)),
		int(report.get("texture_resources", 0)),
		int(report.get("sprite_frame_resources", 0)),
		float(report.get("estimated_texture_mb", 0.0)),
	])
	var rank: int = 0
	for raw_entry in report.get("top", []):
		var entry: Dictionary = raw_entry
		rank += 1
		print("[AFK_MEMORY_AUDIT] stage=%s rank=%d kind=%s class=%s estimated_mb=%.2f refs=%d source_count=%d path=%s owners=%s" % [
			stage, rank,
			str(entry.get("kind", "resource")),
			str(entry.get("class", "Resource")),
			float(entry.get("estimated_bytes", 0)) / 1048576.0,
			int(entry.get("reference_count", 0)),
			int(entry.get("reference_sources", 0)),
			str(entry.get("path", "")),
			str(entry.get("owners", [])),
		])


func _log_afk_memory_after_settle(started_msec: int) -> void:
	var tree: SceneTree = get_tree()
	if tree == null:
		return
	await tree.create_timer(3.0).timeout
	if not is_inside_tree() or not _afk_active or _afk_started_msec != started_msec:
		return
	_log_afk_memory("afk_settled_3s")
	log_world_visual_audit("afk_settled_3s")
	_log_afk_memory_audit("afk_settled_3s")
	await _settle_released_memory("afk_settled_3s")


var current_world_enhancement: CanvasLayer = null
var current_day_night_overlay: CanvasLayer = null
var world_enhancement_enabled: bool = GameConstants.SETTING_DEFAULT_QUALITY_ENHANCEMENT_ENABLED
var current_festival_theme: String = FestivalThemeManager.NORMAL
var current_festival_activity: String = ""
var current_performance_load_mode: int = ClientPerformanceProfile.LOAD_BALANCED
var current_combat_render_mode: int = ClientPerformanceProfile.COMBAT_NORMAL
var current_player_attack_mode: int = GameConstants.SETTING_DEFAULT_PLAYER_ATTACK_MODE
var current_foreground_fps_limit: int = 60
var current_simplify_players_mode: int = ClientPerformanceProfile.PLAYER_SHOW_ALL
var current_simplify_monsters_mode: int = ClientPerformanceProfile.MONSTER_SHOW_ALL
var current_effects_simplified: bool = false
var current_ground_drop_display_mode: int = ClientPerformanceProfile.GROUND_DROPS_ALL
var current_damage_numbers_visible: bool = true
var current_performance_profile: Dictionary = ClientPerformanceProfile.apply_visibility_overrides(
	ClientPerformanceProfile.resolve(
		current_performance_load_mode,
		current_combat_render_mode
	),
	current_simplify_players_mode,
	current_simplify_monsters_mode
)

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
var _last_physics_tick_count: int = -1
var _last_physics_tick_sample_msec: int = 0
var _focus_last_change_usec: int = 0
var _focus_change_count: int = 0
var _prewarmed_hud_windows: Dictionary = {}
var _skip_next_login_instance_prewarm: bool = false

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


func consume_skip_login_instance_prewarm() -> bool:
	var skip: bool = _skip_next_login_instance_prewarm
	_skip_next_login_instance_prewarm = false
	return skip


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


static func foreground_from_window_state(focused: bool, minimized: bool) -> bool:
	# Minimized must behave exactly like an unfocused window for rendering,
	# visual processing and audio. Keep this pure helper covered by headless tests.
	return focused and not minimized


func _is_main_window_minimized() -> bool:
	if DisplayServer.get_name() == "headless":
		return false
	return DisplayServer.window_get_mode() == DisplayServer.WINDOW_MODE_MINIMIZED


func _observe_window_foreground() -> bool:
	return foreground_from_window_state(
		DisplayServer.window_is_focused(),
		_is_main_window_minimized()
	)


func _focus_perf_context() -> String:
	var focus_name: String = "foreground" if _window_has_focus else "background"
	var render_enabled: bool = RenderingServer.render_loop_enabled
	var master_bus_index: int = AudioServer.get_bus_index("Master")
	var audio_muted: bool = master_bus_index >= 0 and AudioServer.is_bus_mute(master_bus_index)
	var focus_age_ms: int = -1
	if _focus_last_change_usec > 0:
		focus_age_ms = int((Time.get_ticks_usec() - _focus_last_change_usec) / 1000)
	return "focus=%s focus_actual=%s minimized=%s power_save=%s fps_cap=%d render_loop=%s audio_muted=%s vsync=%d focus_age_ms=%d startup_overlay=%s" % [
		focus_name, str(DisplayServer.window_is_focused()), str(_is_main_window_minimized()),
		str(_background_mode_active), Engine.max_fps, str(render_enabled), str(audio_muted),
		DisplayServer.window_get_vsync_mode() if DisplayServer.get_name() != "headless" else -1,
		focus_age_ms, str(_startup_loading_overlay_active),
	]


func _log_focus_power_save(event_name: String, trimmed_caches: bool = false) -> void:
	if not OS.is_debug_build():
		return
	# OS window-focus queries may lag a notification by one event/frame.
	# Keep both readings, explicitly marking this transition-time race.
	var query_disagrees: bool = _observe_window_foreground() != _window_has_focus
	print("[FOCUS_POWER_SAVE] event=%s %s focus_changes=%d caches_trimmed=%s focus_query_disagrees=%s" % [
		event_name, _focus_perf_context(), _focus_change_count,
		str(trimmed_caches), str(query_disagrees),
	])


func _process(delta: float) -> void :
	# Focus/render recovery also runs in release exports. RenderingServer
	# can stay disabled after a missed Windows focus notification otherwise.
	_reconcile_world_render_policy(delta)
	_update_map_visual_aoi(delta)
	# Diagnostics are intentionally excluded from release exports.
	if not OS.is_debug_build():
		return

	var now_usec := Time.get_ticks_usec()
	if _frame_gap_last_usec > 0:
		var gap_usec := now_usec - _frame_gap_last_usec
		if gap_usec >= FRAME_GAP_WARN_USEC:
			var gap_kind: String = classify_frame_gap(gap_usec, not _background_mode_active, Engine.max_fps)
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

func _update_map_visual_aoi(delta: float) -> void:
	if (
		_afk_active
		or _afk_restoring
		or is_changing_map
		or not is_instance_valid(current_map_node)
		or not is_instance_valid(current_player)
	):
		return
	if not _map_visual_aoi_streamer.is_bound_to(current_map_node):
		_map_visual_aoi_streamer.bind_map(current_map_node)
	_map_visual_aoi_streamer.update(
		current_player.global_position,
		current_performance_profile,
		delta
	)
	if OS.is_debug_build():
		_map_visual_aoi_log_elapsed += delta
		if _map_visual_aoi_log_elapsed >= 5.0:
			_map_visual_aoi_log_elapsed = 0.0
			var aoi_stats: Dictionary = _map_visual_aoi_streamer.stats()
			print("[MAP_VISUAL_AOI] indexed=%d loaded=%d unloaded=%d pending_paths=%d enter_radius=%.0f exit_radius=%.0f texture_mb=%.1f buffer_mb=%.1f" % [
				int(aoi_stats.get("indexed", 0)),
				int(aoi_stats.get("loaded", 0)),
				int(aoi_stats.get("unloaded", 0)),
				int(aoi_stats.get("pending_paths", 0)),
				float(aoi_stats.get("enter_radius", 0.0)),
				float(aoi_stats.get("exit_radius", 0.0)),
				float(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED)) / 1048576.0,
				float(Performance.get_monitor(Performance.RENDER_BUFFER_MEM_USED)) / 1048576.0,
			])


func _bind_map_visual_aoi() -> void:
	if is_instance_valid(current_map_node):
		var stats: Dictionary = _map_visual_aoi_streamer.bind_map(current_map_node)
		if OS.is_debug_build():
			print("[MAP_VISUAL_AOI] event=bind indexed=%d" % int(stats.get("indexed", 0)))


func _clear_map_visual_aoi() -> void:
	_map_visual_aoi_streamer.clear()
	_map_visual_aoi_log_elapsed = 0.0


func _prime_map_visual_aoi(progress_callback: Callable = Callable()) -> void:
	if not is_instance_valid(current_map_node) or not is_instance_valid(current_player):
		return
	if not _map_visual_aoi_streamer.is_bound_to(current_map_node):
		_bind_map_visual_aoi()
	if progress_callback.is_valid():
		progress_callback.call(50.0, "載入附近地圖素材")
	var prime_stats: Dictionary = await _map_visual_aoi_streamer.prime_near(
		get_tree(),
		current_player.global_position,
		current_performance_profile
	)
	if OS.is_debug_build():
		print("[MAP_VISUAL_AOI] event=prime indexed=%d loaded=%d unloaded=%d pending_paths=%d texture_mb=%.1f" % [
			int(prime_stats.get("indexed", 0)),
			int(prime_stats.get("loaded", 0)),
			int(prime_stats.get("unloaded", 0)),
			int(prime_stats.get("pending_paths", 0)),
			float(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED)) / 1048576.0,
		])


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
	var simplified_monsters := 0
	var primitive_monsters := 0
	var simplified_players := 0
	var ground_items_total := 0
	var ground_items_visible_nearby := 0
	var ground_known: int = 0
	var ground_deferred: int = 0
	var monster_aoi_scans: int = 0
	var monster_aoi_skips: int = 0
	var ground_aoi_scans: int = 0
	var ground_aoi_skips: int = 0
	var monster_aoi_enter_radius: float = 0.0
	var ground_aoi_enter_radius: float = 0.0
	var paper_doll_layers := 0
	var minimap_world_redraws: int = 0
	var minimap_world_skips: int = 0
	if is_instance_valid(current_hud) and current_hud.has_method("get_minimap_world_redraw_stats"):
		var minimap_stats: Dictionary = current_hud.get_minimap_world_redraw_stats()
		minimap_world_redraws = int(minimap_stats.get("redraws", 0))
		minimap_world_skips = int(minimap_stats.get("skipped", 0))

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
				0:
					visible_players += 1
					if entity.has_method("is_performance_simplified") and entity.is_performance_simplified():
						simplified_players += 1
				1:
					visible_monsters += 1
					if entity.has_method("is_performance_simplified") and entity.is_performance_simplified():
						simplified_monsters += 1
					if entity.has_method("is_primitive_simplified") and entity.is_primitive_simplified():
						primitive_monsters += 1
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
	var ground_display_selected: int = -1
	var ground_display_pending_trim: int = 0
	var ground_index_cells: int = 0
	var ground_index_examined: int = 0
	var ground_index_rebuilds: int = 0
	if NetworkEntityManager:
		if NetworkEntityManager.has_method("get_aoi_active_monster_count"):
			aoi_active_monsters = int(NetworkEntityManager.get_aoi_active_monster_count())
		if NetworkEntityManager.has_method("get_aoi_deferred_monster_count"):
			aoi_deferred_monsters = int(NetworkEntityManager.get_aoi_deferred_monster_count())
		if NetworkEntityManager.has_method("get_ground_aoi_stats"):
			var ground_aoi: Dictionary = NetworkEntityManager.get_ground_aoi_stats()
			ground_known = int(ground_aoi.get("known", 0))
			ground_deferred = int(ground_aoi.get("deferred", 0))
			ground_display_selected = int(ground_aoi.get("selected", -1))
			ground_display_pending_trim = int(ground_aoi.get("pending_trim", 0))
			ground_index_cells = int(ground_aoi.get("ground_index_cells", 0))
			ground_index_examined = int(ground_aoi.get("ground_index_last_visited", 0))
			ground_index_rebuilds = int(ground_aoi.get("ground_index_rebuilds", 0))
			monster_aoi_scans = int(ground_aoi.get("monster_full_scans", 0))
			monster_aoi_skips = int(ground_aoi.get("monster_skipped_scans", 0))
			ground_aoi_scans = int(ground_aoi.get("ground_full_scans", 0))
			ground_aoi_skips = int(ground_aoi.get("ground_skipped_scans", 0))
			monster_aoi_enter_radius = float(ground_aoi.get("monster_enter_radius", 0.0))
			ground_aoi_enter_radius = float(ground_aoi.get("ground_enter_radius", 0.0))

	var render_texture_mb: float = float(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED)) / 1048576.0
	var render_buffer_mb: float = float(Performance.get_monitor(Performance.RENDER_BUFFER_MEM_USED)) / 1048576.0
	var render_video_mb: float = float(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)) / 1048576.0
	var render_misc_mb: float = maxf(0.0, render_video_mb - render_texture_mb - render_buffer_mb)
	print("[RENDER_STATS] mode=%s combat=%s fps=%d draw_calls=%d objects=%d primitives=%d canvas_nodes=%d video_mem_mb=%.1f texture_mem_mb=%.1f buffer_mem_mb=%.1f video_misc_mb=%.1f visible_monsters=%d simplified_monsters=%d primitive_monsters=%d aoi_active_monsters=%d aoi_deferred_monsters=%d visible_players=%d simplified_players=%d visible_pets=%d ground_items=%d ground_nearby_visible=%d ground_known=%d ground_deferred=%d ground_mode=%d ground_selected=%d ground_pending_trim=%d ground_index_cells=%d ground_index_examined=%d ground_index_rebuilds=%d monster_aoi_scans=%d monster_aoi_skips=%d ground_aoi_scans=%d ground_aoi_skips=%d monster_aoi_radius=%.0f ground_aoi_radius=%.0f paper_doll_layers=%d minimap_world_redraws=%d minimap_world_skips=%d render_stats_valid=%s %s %s" % [
		ClientPerformanceProfile.load_mode_name(current_performance_load_mode),
		ClientPerformanceProfile.combat_mode_name(current_combat_render_mode),
		int(Performance.get_monitor(Performance.TIME_FPS)),
		int(Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)),
		int(Performance.get_monitor(Performance.RENDER_TOTAL_OBJECTS_IN_FRAME)),
		int(Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME)),
		int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT)),
		render_video_mb,
		render_texture_mb,
		render_buffer_mb,
		render_misc_mb,
		visible_monsters,
		simplified_monsters,
		primitive_monsters,
		aoi_active_monsters,
		aoi_deferred_monsters,
		visible_players,
		simplified_players,
		visible_pets,
		ground_items_total,
		ground_items_visible_nearby,
		ground_known,
		ground_deferred,
		current_ground_drop_display_mode,
		ground_display_selected,
		ground_display_pending_trim,
		ground_index_cells,
		ground_index_examined,
		ground_index_rebuilds,
		monster_aoi_scans,
		monster_aoi_skips,
		ground_aoi_scans,
		ground_aoi_skips,
		monster_aoi_enter_radius,
		ground_aoi_enter_radius,
		paper_doll_layers,
		minimap_world_redraws,
		minimap_world_skips,
		str(not _background_mode_active and RenderingServer.render_loop_enabled),
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
	var buffer_mem_mb: float = float(Performance.get_monitor(Performance.RENDER_BUFFER_MEM_USED)) / 1048576.0
	var video_mem_mb: float = float(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)) / 1048576.0
	var video_misc_mb: float = maxf(0.0, video_mem_mb - texture_mem_mb - buffer_mem_mb)
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

	print("[PERF_ANALYSIS] mode=%s combat=%s checkpoint=%s fps=%.1f process_ms=%.3f physics_ms=%.3f cpu_frame_pct=%.1f static_mem_mb=%.1f static_peak_mb=%.1f video_mem_mb=%.1f texture_mem_mb=%.1f buffer_mem_mb=%.1f video_misc_mb=%.1f objects=%d nodes=%d orphan_nodes=%d active_entities=%d active_monsters=%d deferred_monsters=%d combat_fx_enabled=%s cpu_metric=frame_budget_ratio cpu_process_time_ms=%.3f quality_enhanced=%s %s %s" % [
		ClientPerformanceProfile.load_mode_name(current_performance_load_mode),
		ClientPerformanceProfile.combat_mode_name(current_combat_render_mode),
		checkpoint,
		fps,
		process_ms,
		physics_ms,
		cpu_frame_pct,
		static_mem_mb,
		static_peak_mb,
		video_mem_mb,
		texture_mem_mb,
		buffer_mem_mb,
		video_misc_mb,
		object_count,
		node_count,
		orphan_count,
		active_entities,
		active_monsters,
		deferred_monsters,
		str(should_render_combat_effects()),
		process_ms + physics_ms,
		str(world_enhancement_enabled),
		_focus_perf_context(),
		_multiclient_gpu_context(),
	])
	# FPS is presentation. Compare physics ticks per real second to catch
	# accidental simulation slowdowns when switching 30/60/unlimited.
	var tick_now_msec: int = Time.get_ticks_msec()
	var tick_now_count: int = Engine.get_physics_frames()
	if _last_physics_tick_count >= 0:
		var tick_elapsed_msec: int = tick_now_msec - _last_physics_tick_sample_msec
		if tick_elapsed_msec >= 1000:
			var measured_hz: float = float(tick_now_count - _last_physics_tick_count) * 1000.0 / float(tick_elapsed_msec)
			print("[PHYSICS_CADENCE] pid=%d fps_cap=%d configured_hz=%d measured_hz=%.1f sample_ms=%d physics_ticks=%d network_fixed_tick=%s" % [
				OS.get_process_id(), Engine.max_fps, Engine.physics_ticks_per_second,
				measured_hz, tick_elapsed_msec, tick_now_count,
				str(NetworkManager.is_physics_processing()) if NetworkManager else "false",
			])
			_last_physics_tick_count = tick_now_count
			_last_physics_tick_sample_msec = tick_now_msec
	else:
		_last_physics_tick_count = tick_now_count
		_last_physics_tick_sample_msec = tick_now_msec


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
# Focus-loss optimization is optional; minimizing the window is NEVER optional.
var _unfocus_power_save_enabled: bool = true
var _background_mode_active: bool = false
var _focused_max_fps: int = 0
var _saved_vsync_mode: int = -1
var _master_bus_was_muted_before_focus_loss: bool = false
var _startup_loading_overlay_active: bool = false
var _world_ready_for_background_saving: bool = false
var _world_presented_msec: int = 0
var _focus_policy_poll_elapsed: float = 0.0
var _focus_poll_last_observed: bool = true
var _focus_poll_same_readings: int = 0
const WORLD_RENDER_GRACE_MSEC: int = 1200
const WORLD_FOCUS_POLL_INTERVAL_SECONDS: float = 0.25
var _startup_focus_observed_frames: int = 0
var _last_background_cache_trim_msec: int = 0
var _background_focus_lost_msec: int = 0
var _background_memory_checkpoint_stage: int = 0
const BACKGROUND_CACHE_TRIM_DELAY_MSEC: int = 12000
const BACKGROUND_CACHE_TRIM_COOLDOWN_MSEC: int = 60000
const UNFOCUSED_MAX_FPS := 30


func is_afk_mode_active() -> bool:
	return _afk_active


func is_game_window_focused() -> bool:
	# HUD visual scheduling needs presentation policy, not raw Win32 focus:
	# an Alt+Tabbed window remains fully active if Unfocus is disabled.
	return not _background_mode_active


func is_unfocus_power_save_enabled() -> bool:
	return _unfocus_power_save_enabled


static func should_apply_unfocus_policy(os_focused: bool, minimized: bool, unfocus_enabled: bool) -> bool:
	return UNFOCUS_PRESENTATION_POLICY.should_background(os_focused, minimized, unfocus_enabled)


func _background_policy_required() -> bool:
	return should_apply_unfocus_policy(
		_window_has_focus, _is_main_window_minimized(), _unfocus_power_save_enabled
	)


func set_unfocus_power_save_enabled(enabled: bool) -> void:
	if _unfocus_power_save_enabled == enabled:
		return
	_unfocus_power_save_enabled = enabled
	_sync_background_presentation_state()
	_refresh_focus_render_policy()
	if OS.is_debug_build():
		print("[UNFOCUS_SETTING] enabled=%s minimized=%s background=%s render=%s" % [
			str(enabled), str(_is_main_window_minimized()),
			str(_background_mode_active), str(RenderingServer.render_loop_enabled)
		])


func is_afk_mode_restoring() -> bool:
	return _afk_restoring


func afk_visual_stats() -> Dictionary:
	return _afk_visual_stats.duplicate()


func _log_afk_state(event_name: String, released: int = 0, missing: int = 0) -> void:
	if not OS.is_debug_build():
		return
	print("[AFK_MODE] event=%s active=%s restoring=%s map_textures_released=%d missing=%d tracked=%d memory_mb=%.1f video_mem_mb=%.1f nodes=%d fps_cap=%d render_loop=%s forced_draws=%d physics_hz=%d connected=%s" % [
		event_name, str(_afk_active), str(_afk_restoring), released, missing,
		_afk_map_cache.suspended_count(), _get_process_memory_mb(),
		float(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)) / 1048576.0,
		int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT)), Engine.max_fps,
		str(RenderingServer.render_loop_enabled), _afk_forced_draw_count,
		Engine.physics_ticks_per_second,
		str(NetworkManager.is_connected_to_server) if NetworkManager else "false"
	])


func _enter_afk_audio_silence() -> void:
	var bus: int = AudioServer.get_bus_index("Master")
	if bus >= 0:
		_afk_master_was_muted_before = (
			AudioServer.is_bus_mute(bus) if not _background_mode_active
			else _master_bus_was_muted_before_focus_loss
		)
		AudioServer.set_bus_mute(bus, true)
	if AudioManager and AudioManager.has_method("set_afk_silent"):
		AudioManager.set_afk_silent(true)


func _leave_afk_audio_silence(restart_map_music: bool) -> void:
	if AudioManager and AudioManager.has_method("set_afk_silent"):
		AudioManager.set_afk_silent(false)
	var bus: int = AudioServer.get_bus_index("Master")
	if bus >= 0:
		if not _background_mode_active:
			AudioServer.set_bus_mute(bus, _afk_master_was_muted_before)
		else:
			# Focus-out must stay silent; the next focus-in restores the
			# original pre-AFK user mute preference, not the forced mute.
			_master_bus_was_muted_before_focus_loss = _afk_master_was_muted_before
			AudioServer.set_bus_mute(bus, true)
	if restart_map_music and AudioManager and is_instance_valid(current_player):
		if current_player.data_model:
			AudioManager.play_map_bgm(current_player.data_model.map_id)



func _resolve_map_scene_path(map_id: int) -> String:
	var streamed_path: String = "res://maps/streamed/Map%02d_base.scn" % map_id
	if ResourceLoader.exists(streamed_path):
		return streamed_path
	return "res://maps/Map%02d.scn" % map_id


func _afk_current_map_path() -> String:
	if is_instance_valid(current_player) and current_player.data_model:
		var map_id: int = int(current_player.data_model.map_id)
		if map_id > 0:
			return _resolve_map_scene_path(map_id)
	return ""


func _afk_dynamic_container() -> Node:
	if not is_instance_valid(current_map_node):
		return null
	var container: Node = current_map_node.get_node_or_null("Entities")
	# Never preserve an old ObjectContainer wholesale: legacy maps can mix
	# hundreds/thousands of static decorative nodes into it. If there is no
	# dedicated Entities node, create a clean runtime-only container and move
	# only the player + authoritative NetworkEntityManager entities below.
	if not is_instance_valid(container):
		container = Node2D.new()
		container.name = "Entities"
		current_map_node.add_child(container)

	if is_instance_valid(current_player) and not container.is_ancestor_of(current_player):
		current_player.reparent(container, true)
	if NetworkEntityManager:
		for entity in NetworkEntityManager.active_entities.values():
			if is_instance_valid(entity) and not container.is_ancestor_of(entity):
				entity.reparent(container, true)
	return container


func _set_afk_dynamic_visuals_suspended(suspended: bool) -> int:
	var changed: int = 0
	if is_instance_valid(current_player) and current_player.has_method("set_afk_visual_suspended"):
		current_player.set_afk_visual_suspended(suspended)
		changed += 1
	if NetworkEntityManager:
		for entity in NetworkEntityManager.active_entities.values():
			if is_instance_valid(entity) and entity.has_method("set_afk_visual_suspended"):
				entity.set_afk_visual_suspended(suspended)
				changed += 1
	return changed


func _set_afk_dynamic_visuals_suspended_batched(
	suspended: bool,
	progress_callback: Callable = Callable()
) -> int:
	var entities: Array[Node] = []
	if is_instance_valid(current_player):
		entities.append(current_player)
	if NetworkEntityManager:
		for entity in NetworkEntityManager.active_entities.values():
			if is_instance_valid(entity):
				entities.append(entity)
	var total: int = entities.size()
	var changed: int = 0
	var frame_started_usec: int = Time.get_ticks_usec()
	for entity in entities:
		if is_instance_valid(entity) and entity.has_method("set_afk_visual_suspended"):
			entity.set_afk_visual_suspended(suspended)
			changed += 1
		if changed % 8 == 0 or Time.get_ticks_usec() - frame_started_usec >= 4000:
			_emit_afk_restore_progress(
				progress_callback,
				18.0 + 30.0 * float(changed) / float(maxi(1, total)),
				"卸載角色與怪物視覺 (%d/%d)" % [changed, total]
			)
			await get_tree().process_frame
			frame_started_usec = Time.get_ticks_usec()
	return changed


func _clear_visual_runtime_caches_batched(
	progress_callback: Callable = Callable()
) -> void:
	var released_hud_nodes: int = 0
	if is_instance_valid(current_hud) and current_hud.has_method("release_afk_rebuildable_ui_batched"):
		_emit_afk_restore_progress(progress_callback, 65.0, "卸載可重建介面")
		released_hud_nodes = int(await current_hud.call("release_afk_rebuildable_ui_batched"))
		if OS.is_debug_build():
			print("[AFK_UI_RELEASE] rebuildable_nodes=%d" % released_hud_nodes)
		await get_tree().process_frame

	if DollLoader:
		if DollLoader.has_method("clear_runtime_cache_batched"):
			await DollLoader.clear_runtime_cache_batched()
		elif DollLoader.has_method("clear_runtime_cache"):
			DollLoader.clear_runtime_cache()
	_emit_afk_restore_progress(progress_callback, 69.0, "釋放角色紙娃娃快取")
	await get_tree().process_frame

	var stage_index: int = 0
	var loaders: Array = [
		["怪物", MonsterLoader],
		["特效", EffectLoader],
		["寵物", PetLoader],
		["物品", ItemLoader],
		["技能", SkillLoader],
		["音效", AudioManager],
	]
	for spec in loaders:
		var loader: Object = spec[1] as Object
		if is_instance_valid(loader) and loader.has_method("clear_runtime_cache"):
			loader.call("clear_runtime_cache")
		stage_index += 1
		_emit_afk_restore_progress(
			progress_callback,
			70.0 + 18.0 * float(stage_index) / float(loaders.size()),
			"釋放%s快取" % str(spec[0])
		)
		await get_tree().process_frame

	PaperDollEngine.clear_visual_runtime_cache()
	FestivalThemeManager.clear_texture_cache()
	_emit_afk_restore_progress(progress_callback, 91.0, "整理共用視覺快取")
	await get_tree().process_frame

	for kind in _prewarmed_hud_windows.keys().duplicate():
		var window = _prewarmed_hud_windows.get(kind, null)
		if is_instance_valid(window):
			window.free()
		_prewarmed_hud_windows.erase(kind)
		await get_tree().process_frame
	if is_instance_valid(current_hud) and current_hud.has_method("release_afk_visual_preloads"):
		current_hud.release_afk_visual_preloads()
	_emit_afk_restore_progress(progress_callback, 95.0, "整理掛機介面")
	await get_tree().process_frame


func _enter_afk_render_shell() -> Dictionary:
	var container: Node = _afk_dynamic_container()
	if not is_instance_valid(container):
		return {"ok": false, "reason": "missing_dynamic_container"}
	_afk_map_path = _afk_current_map_path()
	if _afk_map_path.is_empty() or not ResourceLoader.exists(_afk_map_path):
		return {"ok": false, "reason": "missing_map_path"}
	var result: Dictionary = _afk_render_shell.enter(current_map_node, container)
	if bool(result.get("ok", false)):
		current_map_node = _afk_render_shell.current_shell()
	return result


func _emit_afk_restore_progress(callback: Callable, percent: float, stage: String) -> void:
	# UI-only reporting. The callback never sends packets or pauses physics.
	if callback.is_valid():
		callback.call(clampf(percent, 0.0, 100.0), stage)


func _restore_afk_entity_visuals_batched(callback: Callable) -> void:
	# Restore the exact existing scene/physics objects. Recreating entities
	# here would change their identity and potentially break target/loot state.
	var entities: Array[Node] = []
	if is_instance_valid(current_player):
		entities.append(current_player)
	if NetworkEntityManager:
		for entity in NetworkEntityManager.active_entities.values():
			if is_instance_valid(entity):
				entities.append(entity)
	var total: int = entities.size()
	var restored: int = 0
	var batch_started_usec: int = Time.get_ticks_usec()
	for node in entities:
		if is_instance_valid(node):
			if node.has_method("restore_afk_visual_async"):
				await node.restore_afk_visual_async()
			elif node.has_method("set_afk_visual_suspended"):
				node.set_afk_visual_suspended(false)
		restored += 1
		if restored % 4 == 0 or Time.get_ticks_usec() - batch_started_usec >= 6000:
			_emit_afk_restore_progress(callback,
				86.0 + 12.0 * float(restored) / float(maxi(1, total)),
				"恢復角色、怪物與特效 (%d/%d)" % [restored, total])
			await get_tree().process_frame
			batch_started_usec = Time.get_ticks_usec()
	_emit_afk_restore_progress(callback, 98.0, "角色視覺恢復完成")


func _restore_afk_render_shell(progress_callback: Callable = Callable()) -> Dictionary:
	if not _afk_render_shell.is_active():
		return {"ok": true, "restored_static_map": false}
	var path: String = _afk_map_path
	_emit_afk_restore_progress(progress_callback, 4.0, "確認地圖資源")
	if path.is_empty() or not ResourceLoader.exists(path):
		return {"ok": false, "reason": "missing_map_resource"}

	var existing_status: int = ResourceLoader.load_threaded_get_status(path)
	if existing_status not in [
		ResourceLoader.THREAD_LOAD_IN_PROGRESS,
		ResourceLoader.THREAD_LOAD_LOADED
	]:
		var request_error: int = ResourceLoader.load_threaded_request(
			path, "", false, ResourceLoader.CACHE_MODE_IGNORE_DEEP
		)
		if request_error != OK:
			return {"ok": false, "reason": "load_request", "error": request_error}
	var packed: PackedScene = null
	while true:
		var progress: Array = []
		var status: int = ResourceLoader.load_threaded_get_status(path, progress)
		if status == ResourceLoader.THREAD_LOAD_IN_PROGRESS:
			_emit_afk_restore_progress(progress_callback,
				8.0 + 57.0 * (float(progress[0]) if not progress.is_empty() else 0.0),
				"載入地圖貼圖與場景")
			await get_tree().process_frame
			continue
		if status == ResourceLoader.THREAD_LOAD_LOADED:
			_emit_afk_restore_progress(progress_callback, 65.0, "地圖資源載入完成")
			packed = ResourceLoader.load_threaded_get(path) as PackedScene
			break
		return {"ok": false, "reason": "load_failed", "status": status}
	if packed == null:
		return {"ok": false, "reason": "null_packed_scene"}

	_emit_afk_restore_progress(progress_callback, 68.0, "重建地圖視覺")
	await get_tree().process_frame
	var restored := packed.instantiate() as Node2D
	if not is_instance_valid(restored):
		return {"ok": false, "reason": "instantiate_failed"}
	for collision_name: String in ["CollisionContainer", "CollisionLayer"]:
		var legacy := restored.get_node_or_null(collision_name)
		if is_instance_valid(legacy):
			restored.remove_child(legacy)
			legacy.free()

	var attached: Dictionary = _afk_render_shell.attach_to_restored_map(restored)
	if not bool(attached.get("ok", false)):
		restored.free()
		return attached
	current_map_node = restored
	_emit_afk_restore_progress(progress_callback, 76.0, "還原角色節點與地圖")
	_apply_current_festival_theme(true)
	if BuildingManager:
		_emit_afk_restore_progress(progress_callback, 81.0, "初始化地圖裝飾")
		await _init_buildings_with_diagnostics(
			int(current_player.data_model.map_id) if is_instance_valid(current_player) and current_player.data_model else 0,
			int(current_player.data_model.map_id) if is_instance_valid(current_player) and current_player.data_model else 0
		)
	attached["restored_static_map"] = true
	_emit_afk_restore_progress(progress_callback, 85.0, "地圖視覺完成")
	return attached


func begin_afk_mode(progress_callback: Callable = Callable()) -> bool:
	if _afk_active or _afk_restoring or is_changing_map:
		return false
	if not is_instance_valid(current_map_node) or not is_instance_valid(current_player):
		return false
	var enter_started_usec: int = Time.get_ticks_usec()
	set_frame_gap_stage("afk_enter_prepare")
	_afk_saved_profile = {
		"load": current_performance_load_mode,
		"combat": current_combat_render_mode,
		"players": current_simplify_players_mode,
		"monsters": current_simplify_monsters_mode,
	}
	_afk_memory_baseline = _afk_memory_sample()
	_afk_forced_draw_count = 0
	_pending_afk_working_set_trim = false
	_afk_active = true
	_afk_started_msec = Time.get_ticks_msec()
	_log_afk_memory("enter_before")
	_log_afk_state("enter_before")
	_emit_afk_restore_progress(progress_callback, 5.0, "準備掛機執行環境")
	_enter_afk_audio_silence()

	if current_player.has_method("begin_afk_navigation_anchor"):
		current_player.begin_afk_navigation_anchor()
	current_map_node.visible = false
	_clear_map_visual_aoi()
	_remove_day_night_overlay()
	_remove_world_enhancement()
	_emit_afk_restore_progress(progress_callback, 12.0, "隱藏遊戲畫面")
	await get_tree().process_frame
	set_frame_gap_stage("afk_enter_ground_data_only")
	_emit_afk_restore_progress(progress_callback, 14.0, "將地上物品切換為純資料")
	var ground_data_only: Dictionary = {}
	if NetworkEntityManager and NetworkEntityManager.has_method("compact_ground_items_for_afk_batched"):
		ground_data_only = await NetworkEntityManager.compact_ground_items_for_afk_batched()
	if OS.is_debug_build():
		print("[AFK_DATA_ONLY_GROUND] event=enter released=%d preserved_requesting=%d queued_to_data=%d known=%d deferred=%d" % [
			int(ground_data_only.get("released_nodes", 0)),
			int(ground_data_only.get("preserved_requesting", 0)),
			int(ground_data_only.get("queued_to_data", 0)),
			int(ground_data_only.get("known", 0)),
			int(ground_data_only.get("deferred", 0)),
		])
	_emit_afk_restore_progress(progress_callback, 18.0, "地上物品資料化完成")

	set_frame_gap_stage("afk_enter_monster_data_only")
	_emit_afk_restore_progress(progress_callback, 20.0, "將怪物切換為純資料")
	var monster_data_only: Dictionary = {}
	if NetworkEntityManager and NetworkEntityManager.has_method("compact_monsters_for_afk_batched"):
		monster_data_only = await NetworkEntityManager.compact_monsters_for_afk_batched()
	if OS.is_debug_build():
		print("[AFK_DATA_ONLY_MONSTER] event=enter released=%d preserved_target=%d queued_to_data=%d known=%d deferred=%d active_nodes=%d" % [
			int(monster_data_only.get("released_nodes", 0)),
			int(monster_data_only.get("preserved_combat_target", 0)),
			int(monster_data_only.get("queued_to_data", 0)),
			int(monster_data_only.get("known", 0)),
			int(monster_data_only.get("deferred", 0)),
			int(monster_data_only.get("active_nodes", 0)),
		])
	_emit_afk_restore_progress(progress_callback, 28.0, "怪物資料化完成")
	set_frame_gap_stage("afk_enter_suspend_entities")

	set_performance_modes(ClientPerformanceProfile.LOAD_MULTIBOX, ClientPerformanceProfile.COMBAT_MASS)
	# AFK's renderer suspension must not overwrite account visibility choices.
	var suspended_entities: int = await _set_afk_dynamic_visuals_suspended_batched(
		true, progress_callback
	)
	_emit_afk_restore_progress(progress_callback, 52.0, "切換低記憶體地圖外殼")
	await get_tree().process_frame
	set_frame_gap_stage("afk_enter_shell")

	_afk_visual_stats = _enter_afk_render_shell()
	_afk_visual_stats["suspended_entity_visuals"] = suspended_entities
	_afk_visual_stats["data_only_ground_items"] = int(ground_data_only.get("released_nodes", 0))
	_afk_visual_stats["data_only_monsters"] = int(monster_data_only.get("released_nodes", 0))
	_afk_visual_stats["released"] = int(_afk_visual_stats.get("freed_static_nodes", 0))
	# AfkRenderShell queues the old static map. Settle that frame before
	# dropping loader caches so the two destruction waves cannot combine.
	await get_tree().process_frame
	set_frame_gap_stage("afk_enter_cache_release")
	_emit_afk_restore_progress(progress_callback, 64.0, "地圖視覺已卸載")
	await _clear_visual_runtime_caches_batched(progress_callback)

	set_frame_gap_stage("afk_active")
	_refresh_focus_render_policy()
	_emit_afk_restore_progress(progress_callback, 100.0, "掛機模式已啟用")
	_log_afk_state("enter_after", int(_afk_visual_stats.get("released", 0)))
	_log_afk_memory("enter_after")
	if OS.is_debug_build():
		print("[AFK_ENTER_TIMING] total_ms=%.2f batched=true network_fixed_tick=%s" % [
			float(Time.get_ticks_usec() - enter_started_usec) / 1000.0,
			str(NetworkManager.is_physics_processing()) if NetworkManager else "false",
		])
		_log_afk_memory_after_settle(_afk_started_msec)
	return true


func finish_afk_mode(progress_callback: Callable = Callable()) -> bool:
	if not _afk_active or _afk_restoring or is_changing_map:
		return false
	if not is_instance_valid(current_map_node) or not is_instance_valid(current_player):
		return false
	_afk_restoring = true
	_log_afk_state("restore_begin")
	_emit_afk_restore_progress(progress_callback, 1.0, "準備恢復畫面")
	var restore_result: Dictionary = await _restore_afk_render_shell(progress_callback)
	if is_changing_map or not bool(restore_result.get("ok", false)):
		_afk_restoring = false
		_afk_visual_stats["restore_error"] = str(restore_result.get("reason", "map_change"))
		_log_afk_state("restore_retry_needed")
		return false
	_afk_active = false
	_afk_restoring = false
	_pending_afk_working_set_trim = false
	_leave_afk_audio_silence(true)
	if is_instance_valid(current_player) and current_player.has_method("end_afk_navigation_anchor"):
		current_player.end_afk_navigation_anchor()
	# Restore user selection, not a hard-coded normal mode. The HUD's
	# persistent per-account FPS setting was never modified.
	set_performance_modes(
		int(_afk_saved_profile.get("load", ClientPerformanceProfile.LOAD_BALANCED)),
		int(_afk_saved_profile.get("combat", ClientPerformanceProfile.COMBAT_NORMAL))
	)
	set_visual_simplification_modes(
		int(_afk_saved_profile.get("players", ClientPerformanceProfile.PLAYER_SHOW_ALL)),
		int(_afk_saved_profile.get("monsters", ClientPerformanceProfile.MONSTER_SHOW_ALL))
	)
	var monster_restore: Dictionary = {}
	if NetworkEntityManager and NetworkEntityManager.has_method("restore_monsters_after_afk"):
		monster_restore = NetworkEntityManager.restore_monsters_after_afk()
	if OS.is_debug_build():
		print("[AFK_DATA_ONLY_MONSTER] event=restore queued=%d known=%d deferred=%d" % [
			int(monster_restore.get("queued", 0)),
			int(monster_restore.get("known", 0)),
			int(monster_restore.get("deferred", 0)),
		])

	var ground_restore: Dictionary = {}
	if NetworkEntityManager and NetworkEntityManager.has_method("restore_ground_items_after_afk"):
		ground_restore = NetworkEntityManager.restore_ground_items_after_afk()
	if OS.is_debug_build():
		print("[AFK_DATA_ONLY_GROUND] event=restore queued=%d known=%d deferred=%d" % [
			int(ground_restore.get("queued", 0)),
			int(ground_restore.get("known", 0)),
			int(ground_restore.get("deferred", 0)),
		])
	await _restore_afk_entity_visuals_batched(progress_callback)
	_bind_map_visual_aoi()
	await _prime_map_visual_aoi(progress_callback)
	_apply_low_resource_world_visual_policy()
	if is_instance_valid(current_map_node):
		current_map_node.visible = true
	_emit_afk_restore_progress(progress_callback, 99.0, "顯示遊戲畫面")
	await get_tree().process_frame
	_afk_saved_profile.clear()
	_afk_visual_stats.clear()
	_refresh_focus_render_policy()
	_log_afk_state("restore_complete", int(restore_result.get("restored_entity_nodes", 0)), 0)
	_log_afk_memory("restore_complete")
	log_world_visual_audit("afk_restore_complete")
	_log_afk_memory_audit("afk_restore_complete")
	_afk_memory_baseline.clear()
	_emit_afk_restore_progress(progress_callback, 100.0, "畫面已恢復")
	return true


func _refresh_afk_map_visuals_after_change() -> void:
	if not _afk_active or not is_instance_valid(current_map_node):
		return
	set_frame_gap_stage("afk_map_change_suspend")
	_afk_map_cache.discard()
	_afk_render_shell.discard()
	current_map_node.visible = false
	await get_tree().process_frame

	var suspended_entities: int = await _set_afk_dynamic_visuals_suspended_batched(true)
	if not _afk_active or not is_instance_valid(current_map_node):
		return
	set_frame_gap_stage("afk_map_change_shell")
	await get_tree().process_frame
	_afk_visual_stats = _enter_afk_render_shell()
	_afk_visual_stats["suspended_entity_visuals"] = suspended_entities
	_afk_visual_stats["released"] = int(_afk_visual_stats.get("freed_static_nodes", 0))
	await get_tree().process_frame

	set_frame_gap_stage("afk_map_change_cache_release")
	await _clear_visual_runtime_caches_batched()
	set_frame_gap_stage("afk_active")
	_log_afk_state("map_changed_unloaded", int(_afk_visual_stats.get("released", 0)))
	_log_afk_memory("afk_map_changed")


func _reset_afk_mode() -> void:
	if _afk_active:
		_leave_afk_audio_silence(false)
	_afk_active = false
	_afk_restoring = false
	_pending_afk_working_set_trim = false
	_afk_saved_profile.clear()
	_afk_visual_stats.clear()
	_afk_map_cache.discard()
	_afk_render_shell.discard()
	_afk_map_path = ""
	_afk_memory_baseline.clear()


func _rebuild_performance_profile() -> void:
	current_performance_profile = ClientPerformanceProfile.apply_visibility_overrides(
		ClientPerformanceProfile.resolve(
			current_performance_load_mode,
			current_combat_render_mode
		),
		current_simplify_players_mode,
		current_simplify_monsters_mode
	)
	current_performance_profile["foreground_fps"] = current_foreground_fps_limit
	current_performance_profile["effects_simplified"] = current_effects_simplified
	current_performance_profile["ground_drop_display_mode"] = current_ground_drop_display_mode
	current_performance_profile["show_damage_numbers"] = current_damage_numbers_visible
	if current_effects_simplified:
		current_performance_profile["combat_effects_enabled"] = false
		current_performance_profile["effect_budget_percent"] = 0

static func needs_vsync_disabled(cap: int, refresh_hz: float) -> bool:
	# Engine.max_fps=0 alone does not unlock FPS when VSync is on.
	return cap == 0 or (refresh_hz > 0.0 and float(cap) > refresh_hz + 0.5)


func _refresh_fps_vsync_policy() -> void:
	if DisplayServer.get_name() == "headless":
		return
	if _saved_vsync_mode < 0:
		_saved_vsync_mode = DisplayServer.window_get_vsync_mode()
	var cap: int = configured_foreground_fps(current_performance_profile)
	var refresh_hz: float = DisplayServer.screen_get_refresh_rate(DisplayServer.window_get_current_screen())
	var preferred_mode: int = DisplayServer.VSYNC_DISABLED if needs_vsync_disabled(cap, refresh_hz) else _saved_vsync_mode
	if DisplayServer.window_get_vsync_mode() != preferred_mode:
		DisplayServer.window_set_vsync_mode(preferred_mode)
		if OS.is_debug_build():
			print("[FPS_VSYNC] cap=%d refresh_hz=%.1f vsync=%d original=%d" % [
				cap, refresh_hz, preferred_mode, _saved_vsync_mode
			])


func should_render_combat_effects() -> bool:
	return bool(current_performance_profile.get("combat_effects_enabled", true))


func _stop_nonessential_combat_effects() -> void:
	# Called on preset changes, never per frame; do not stop combat logic.
	if is_instance_valid(current_player) and current_player.has_method("stop_combat_visual_effects"):
		current_player.stop_combat_visual_effects()
	if NetworkEntityManager:
		for actor in NetworkEntityManager.active_entities.values():
			if is_instance_valid(actor) and actor.has_method("stop_combat_visual_effects"):
				actor.stop_combat_visual_effects()
	if FloatingTextManager and FloatingTextManager.has_method("stop_combat_visual_effects"):
		FloatingTextManager.stop_combat_visual_effects()


func set_player_attack_mode(mode: int) -> void:
	current_player_attack_mode = clampi(
		mode,
		GameConstants.SETTING_PLAYER_ATTACK_GENERAL,
		GameConstants.SETTING_PLAYER_ATTACK_COMBAT
	)

func is_player_attack_input_allowed() -> bool:
	return (
		current_player_attack_mode == GameConstants.SETTING_PLAYER_ATTACK_COMBAT
		or Input.is_key_pressed(KEY_CTRL)
	)

func set_foreground_fps_limit(value: int) -> void:
	current_foreground_fps_limit = ClientPerformanceProfile.sanitize_fps_limit(value)
	_rebuild_performance_profile()
	_refresh_focus_render_policy()
	if OS.is_debug_build():
		print("[FPS_LIMIT] cap=%d mode=%s" % [current_foreground_fps_limit, ClientPerformanceProfile.load_mode_name(current_performance_load_mode)])

func set_performance_modes(load_mode: int, combat_mode: int) -> void:
	var previously_low: bool = current_performance_load_mode == ClientPerformanceProfile.LOAD_LOW
	current_performance_load_mode = ClientPerformanceProfile.sanitize_load_mode(load_mode)
	current_combat_render_mode = ClientPerformanceProfile.sanitize_combat_mode(combat_mode)
	_rebuild_performance_profile()
	if is_instance_valid(current_map_node):
		set_world_enhancement_enabled(current_performance_load_mode == ClientPerformanceProfile.LOAD_QUALITY)
	else:
		world_enhancement_enabled = current_performance_load_mode == ClientPerformanceProfile.LOAD_QUALITY
	_apply_performance_profile()
	# The visual simplification signal above has already detached SpriteFrames
	# from ordinary monsters. Drop unreferenced animation/effect cache entries
	# on entering low-resource mode, not on every frame or FPS change.
	if current_performance_load_mode == ClientPerformanceProfile.LOAD_LOW and not previously_low:
		if MonsterLoader and MonsterLoader.has_method("clear_runtime_cache"):
			MonsterLoader.clear_runtime_cache()
		if EffectLoader and EffectLoader.has_method("clear_runtime_cache"):
			EffectLoader.clear_runtime_cache()

func set_entity_visibility_modes(player_mode: int, monster_mode: int) -> void:
	current_simplify_players_mode = ClientPerformanceProfile.sanitize_player_visibility(player_mode)
	current_simplify_monsters_mode = ClientPerformanceProfile.sanitize_monster_visibility(monster_mode)
	_rebuild_performance_profile()
	_apply_performance_profile()

# Compatibility with older call sites; now controls display visibility only.
func set_visual_simplification_modes(player_mode: int, monster_mode: int) -> void:
	set_entity_visibility_modes(player_mode, monster_mode)

func set_visual_detail_options(simplify_effects: bool, ground_drop_mode: int, show_damage: bool) -> void:
	var sanitized_mode: int = ClientPerformanceProfile.sanitize_ground_drop_display_mode(ground_drop_mode)
	if current_effects_simplified == simplify_effects and current_ground_drop_display_mode == sanitized_mode and current_damage_numbers_visible == show_damage:
		return
	current_effects_simplified = simplify_effects
	current_ground_drop_display_mode = sanitized_mode
	current_damage_numbers_visible = show_damage
	_rebuild_performance_profile()
	_apply_performance_profile()
	if not show_damage and FloatingTextManager and FloatingTextManager.has_method("hide_damage_numbers"):
		FloatingTextManager.hide_damage_numbers()

func should_show_damage_numbers() -> bool:
	return current_damage_numbers_visible

func get_simplify_players_mode() -> int:
	return current_simplify_players_mode

func get_simplify_monsters_mode() -> int:
	return current_simplify_monsters_mode

func get_performance_profile() -> Dictionary:
	return current_performance_profile.duplicate(true)

func get_performance_load_mode() -> int:
	return current_performance_load_mode

func get_combat_render_mode() -> int:
	return current_combat_render_mode

func _current_background_fps() -> int:
	return maxi(1, int(current_performance_profile.get("background_fps", UNFOCUSED_MAX_FPS)))

static func should_throttle_background_visuals(has_focus: bool, _world_active: bool) -> bool:
	# Global policy: login, character select, world and AFK all treat
	# unfocused/minimized windows as background presentation.
	return not has_focus


static func should_draw_afk_dashboard(has_focus: bool) -> bool:
	# AFK never runs a continuous render loop. This only decides whether the
	# focused dashboard may request an occasional manual frame.
	return has_focus


func request_afk_dashboard_frame() -> bool:
	# Compatibility hook for HUD/tests. AFK focused mode uses a normal 20 FPS
	# render loop; unfocused mode renders nothing.
	return _afk_active and not _background_mode_active


static func fps_cap_for_focus(profile: Dictionary, has_focus: bool, world_active: bool) -> int:
	if should_throttle_background_visuals(has_focus, world_active):
		return maxi(1, int(profile.get("background_fps", UNFOCUSED_MAX_FPS)))
	return configured_foreground_fps(profile)


static func may_suspend_world_rendering(
	presentation_finished: bool, has_map: bool, has_player: bool,
	is_loading: bool, time_since_presentation_msec: int) -> bool:
	# The render loop must stay alive while a black/loading overlay is
	# fading, during the first world warmup, and throughout map changes.
	return presentation_finished and has_map and has_player \
		and not is_loading and time_since_presentation_msec >= WORLD_RENDER_GRACE_MSEC


func _has_active_world_visuals() -> bool:
	var since_presented: int = Time.get_ticks_msec() - _world_presented_msec \
		if _world_presented_msec > 0 else 0
	return may_suspend_world_rendering(
		_world_ready_for_background_saving,
		is_instance_valid(current_map_node), is_instance_valid(current_player),
		is_changing_map or _startup_loading_overlay_active, since_presented)


func notify_world_loading_overlay_hidden() -> void:
	# Called ONLY after EntryManager/HUD has actually removed its opaque
	# loading surface. Do not stop GPU drawing at the GameManager 100% mark.
	if is_changing_map or not is_instance_valid(current_map_node) \
		or not is_instance_valid(current_player):
		return
	_world_ready_for_background_saving = true
	_world_presented_msec = Time.get_ticks_msec()
	_refresh_focus_render_policy()
	if OS.is_debug_build():
		print("[WORLD_PRESENTED] render_loop=%s focus_internal=%s focus_os=%s grace_ms=%d" % [
			str(RenderingServer.render_loop_enabled), str(_window_has_focus),
			str(DisplayServer.window_is_focused()), WORLD_RENDER_GRACE_MSEC
		])


func _reconcile_world_render_policy(delta: float) -> void:
	_focus_policy_poll_elapsed += delta
	if _focus_policy_poll_elapsed < WORLD_FOCUS_POLL_INTERVAL_SECONDS:
		return
	_focus_policy_poll_elapsed = 0.0
	var observed_focus: bool = _observe_window_foreground()
	if observed_focus == _focus_poll_last_observed:
		_focus_poll_same_readings += 1
	else:
		_focus_poll_last_observed = observed_focus
		_focus_poll_same_readings = 1
	# Missing/reordered Win32 focus notifications must not strand the
	# renderer disabled until the player manually Alt+Tabs twice.
	if observed_focus != _window_has_focus and _focus_poll_same_readings >= 2:
		_set_window_focus_power_save(observed_focus)
		if OS.is_debug_build():
			_log_focus_power_save("focus_poll_reconciled")
	# Minimized status may change while OS focus remains false. Always
	# reconcile the independent minimize/Unfocus presentation policy.
	_sync_background_presentation_state()
	# AFK dashboard renders at 20 FPS only while focused. Losing focus stops
	# the render loop completely; simulation/network/fixed physics keep running.
	var should_pause: bool = _background_mode_active
	var desired_rendering: bool = should_draw_afk_dashboard(not should_pause) if _afk_active else not should_pause
	var desired_fps: int = AFK_MONITOR_FPS if _afk_active else fps_cap_for_focus(
		current_performance_profile, not should_pause, _has_active_world_visuals())
	if RenderingServer.render_loop_enabled != desired_rendering or Engine.max_fps != desired_fps:
		_refresh_focus_render_policy()
		if OS.is_debug_build():
			_log_focus_power_save("render_policy_reconciled")
	# Never do a cache sweep in the focus event. A brief Alt+Tab must keep
	# all existing assets warm. Idle-only reclamation happens after 12s.
	_try_trim_idle_background_caches()
	_log_background_memory_checkpoint()


func _refresh_focus_render_policy() -> void:
	_sync_background_presentation_state()
	if _afk_active:
		# AFK is an explicit user mode; the separate Unfocus toggle only
		# decides whether Alt+Tab disables its dashboard. Minimize always does.
		Engine.max_fps = AFK_MONITOR_FPS
		RenderingServer.render_loop_enabled = not _background_mode_active
		_set_background_visual_processing(_background_mode_active)
		return
	var world_background: bool = _background_mode_active
	Engine.max_fps = fps_cap_for_focus(
		current_performance_profile, not world_background, _has_active_world_visuals())
	_refresh_fps_vsync_policy()
	RenderingServer.render_loop_enabled = not world_background
	_set_background_visual_processing(world_background)


static func configured_foreground_fps(profile: Dictionary) -> int:
	# Never derive the foreground cap from Engine.max_fps: the engine may
	# currently be limited to 10/20 FPS by the background/startup policy.
	return maxi(0, int(profile.get("foreground_fps", 0)))


func _apply_performance_profile() -> void:
	_focused_max_fps = configured_foreground_fps(current_performance_profile)
	_refresh_focus_render_policy()
	if not should_render_combat_effects():
		_stop_nonessential_combat_effects()
	performance_profile_changed.emit(get_performance_profile())
	_apply_low_resource_world_visual_policy()
	if OS.is_debug_build():
		print("[PERFORMANCE_PROFILE] mode=%s combat=%s player_simplify=%s monster_simplify=%s load=%d combat_id=%d foreground_fps=%d background_fps=%d players=%d monsters=%d effects=%d%% combat_fx_enabled=%s quality_enhanced=%s monster_aoi_base=%.0f ground_aoi_base=%.0f" % [
			ClientPerformanceProfile.load_mode_name(current_performance_load_mode),
			ClientPerformanceProfile.combat_mode_name(current_combat_render_mode),
			ClientPerformanceProfile.simplify_mode_name(current_simplify_players_mode),
			ClientPerformanceProfile.simplify_mode_name(current_simplify_monsters_mode),
			current_performance_load_mode,
			current_combat_render_mode,
			int(current_performance_profile.get("foreground_fps", 0)),
			int(current_performance_profile.get("background_fps", 30)),
			int(current_performance_profile.get("full_player_budget", 0)),
			int(current_performance_profile.get("full_monster_budget", 0)),
			int(current_performance_profile.get("effect_budget_percent", 100)),
			str(should_render_combat_effects()),
			str(current_performance_load_mode == ClientPerformanceProfile.LOAD_QUALITY),
			float(current_performance_profile.get("monster_aoi_enter_radius", 1800.0)),
			float(current_performance_profile.get("ground_aoi_enter_radius", 2000.0)),
		])

func set_startup_loading_overlay_active(active: bool) -> void:
	# Startup/login obey the same background/minimized presentation policy.
	if _startup_loading_overlay_active == active:
		return
	_startup_loading_overlay_active = active
	_refresh_focus_render_policy()
	if OS.is_debug_build():
		_log_focus_power_save("startup_overlay_on" if active else "startup_overlay_off")


# Some Windows launches deliver a focus-out notification while
# window_is_focused() already reports true. Until focus-in is delivered the
# new window is incorrectly capped at startup 10 FPS, making typing feel
# delayed. Reconcile only during bootstrap, requiring three consistent OS
# reads to avoid single-frame notification/query ordering races.
func reconcile_startup_window_focus() -> void:
	if not _startup_loading_overlay_active:
		return
	var observed_focus: bool = _observe_window_foreground()
	if observed_focus == _window_has_focus:
		_startup_focus_observed_frames = 0
		return
	_startup_focus_observed_frames += 1
	if _startup_focus_observed_frames >= 3:
		_startup_focus_observed_frames = 0
		_set_window_focus_power_save(observed_focus)
		if OS.is_debug_build():
			_log_focus_power_save("startup_focus_reconciled")


func _notification(what: int) -> void :
	if what == NOTIFICATION_APPLICATION_FOCUS_OUT:
		_set_window_focus_power_save(false)
	elif what == NOTIFICATION_APPLICATION_FOCUS_IN:
		# A restored/minimized Windows notification can arrive before the
		# window mode settles. Never treat MINIMIZED as foreground.
		_set_window_focus_power_save(_observe_window_foreground())

static func should_trim_background_caches(has_world: bool, last_trim_msec: int, now_msec: int) -> bool:
	return has_world and (last_trim_msec <= 0 or now_msec - last_trim_msec >= BACKGROUND_CACHE_TRIM_COOLDOWN_MSEC)


static func should_run_background_cache_sweep(
		has_focus: bool, has_world: bool, unfocused_ms: int,
		last_trim_msec: int, now_msec: int) -> bool:
	return not has_focus and unfocused_ms >= BACKGROUND_CACHE_TRIM_DELAY_MSEC \
		and should_trim_background_caches(has_world, last_trim_msec, now_msec)



# These snapshots are observability only. They do not evict/retain resources.
# get_reference_count() is indicative, not proof that Windows freed memory.
static func inspect_background_cache(cache: Dictionary, keep_recent: int) -> Dictionary:
	var keys: Array = cache.keys()
	var older: int = maxi(0, keys.size() - maxi(0, keep_recent))
	var stats: Dictionary = {
		"entries": keys.size(), "warm": keys.size() - older,
		"held": 0, "idle": 0, "unknown": 0
	}
	for i in range(older):
		var resource: RefCounted = cache.get(keys[i]) as RefCounted
		if resource == null:
			stats["unknown"] += 1
		elif resource.get_reference_count() <= 2:
			stats["idle"] += 1
		else:
			stats["held"] += 1
	return stats


func _background_memory_cache_report() -> Dictionary:
	var sources: Array = [
		["monster", MonsterLoader, "_cache", 12],
		["doll", DollLoader, "_cache", 12],
		["sr", DollLoader, "_sr_cache", 4],
		["sr_effect", DollLoader, "_sr_effect_cache", 2],
		["pet", PetLoader, "_frames_cache", 8],
		["effect", EffectLoader, "_effect_cache", 8],
		["icon", ItemLoader, "_icon_cache", 48],
		["photo", ItemLoader, "_photo_cache", 8],
		["ground", ItemLoader, "_ground_cache", 24],
		["skill", SkillLoader, "_icon_cache", 24],
		["sound", AudioManager, "se_cache", 32],
		["bgm", AudioManager, "_map_bgm_cache", 3],
	]
	var totals: Dictionary = {"entries": 0, "warm": 0, "held": 0, "idle": 0}
	var labels: PackedStringArray = []
	for spec in sources:
		var node: Node = spec[1] as Node
		if not is_instance_valid(node):
			continue
		var value: Variant = node.get(str(spec[2]))
		if not value is Dictionary:
			continue
		var cache: Dictionary = value
		var current: Dictionary = {"entries": 0, "warm": 0, "held": 0, "idle": 0}
		if str(spec[0]) == "doll":
			for category in cache.values():
				if category is Dictionary:
					var nested: Dictionary = inspect_background_cache(category, int(spec[3]))
					for field in current.keys():
						current[field] += int(nested[field])
		else:
			current = inspect_background_cache(cache, int(spec[3]))
		for field in totals.keys():
			totals[field] += int(current[field])
		labels.append("%s:%d/%d/%d/%d" % [
			str(spec[0]), int(current["entries"]), int(current["warm"]),
			int(current["held"]), int(current["idle"])
		])
	totals["detail"] = ",".join(labels)
	return totals


func _log_background_memory(event_name: String, removed: int = 0) -> void:
	if not OS.is_debug_build():
		return
	var info: Dictionary = _background_memory_cache_report()
	var age_ms: int = Time.get_ticks_msec() - _background_focus_lost_msec \
		if _background_focus_lost_msec > 0 else 0
	var entities: int = NetworkEntityManager.active_entities.size() \
		if NetworkEntityManager else 0
	print("[BACKGROUND_MEMORY] pid=%d event=%s age_ms=%d focus=%s world=%s afk=%s fps=%d render=%s static_mb=%.2f static_peak_mb=%.2f texture_mb=%.2f video_mb=%.2f objects=%d nodes=%d entities=%d cache_entries=%d cache_warm=%d cache_held=%d cache_idle=%d removed=%d loader_counts=%s" % [
		OS.get_process_id(), event_name, age_ms,
		str(_window_has_focus), str(_has_active_world_visuals()),
		str(_afk_active), Engine.max_fps, str(RenderingServer.render_loop_enabled),
		_get_process_memory_mb(),
		float(OS.get_static_memory_peak_usage()) / 1048576.0,
		float(Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED)) / 1048576.0,
		float(Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)) / 1048576.0,
		int(Performance.get_monitor(Performance.OBJECT_COUNT)),
		int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT)),
		entities, int(info["entries"]), int(info["warm"]),
		int(info["held"]), int(info["idle"]), removed, str(info["detail"])
	])


func _log_background_memory_checkpoint() -> void:
	if not OS.is_debug_build() or not _background_mode_active or _background_focus_lost_msec <= 0:
		return
	var elapsed: int = Time.get_ticks_msec() - _background_focus_lost_msec
	var thresholds: Array[int] = [15000, 60000, 180000]
	if _background_memory_checkpoint_stage >= thresholds.size():
		return
	var threshold: int = thresholds[_background_memory_checkpoint_stage]
	if elapsed >= threshold:
		_log_background_memory("background_%ds" % int(threshold / 1000))
		_background_memory_checkpoint_stage += 1


func _try_trim_idle_background_caches() -> void:
	if _background_focus_lost_msec <= 0 or not _background_mode_active:
		return
	var now_msec: int = Time.get_ticks_msec()
	if not should_run_background_cache_sweep(
			not _background_mode_active, _has_active_world_visuals(),
			now_msec - _background_focus_lost_msec,
			_last_background_cache_trim_msec, now_msec):
		return
	_last_background_cache_trim_msec = now_msec
	_log_background_memory("trim_before")
	var before_mb: float = _get_process_memory_mb()
	var removed: int = _trim_unfocused_runtime_caches()
	_log_background_memory("trim_after", removed)
	# This counts released cache entries, NOT reclaimed process RAM. The
	# allocator/OS may retain pages; never infer MB saved from this count.
	if OS.is_debug_build():
		print("[BACKGROUND_CACHE_TRIM] removed_entries=%d before_static_mb=%.2f after_static_mb=%.2f focus_age_ms=%d" % [
			removed, before_mb, _get_process_memory_mb(),
			now_msec - _background_focus_lost_msec
		])


func _sync_background_presentation_state() -> void:
	var desired_background: bool = _background_policy_required()
	if desired_background == _background_mode_active:
		return
	_background_mode_active = desired_background
	var master_bus_index: int = AudioServer.get_bus_index("Master")
	if desired_background:
		_background_focus_lost_msec = Time.get_ticks_msec()
		_background_memory_checkpoint_stage = 0
		_focused_max_fps = configured_foreground_fps(current_performance_profile)
		# Keep sound preferences; temporarily mute ONLY when minimization
		# or enabled Unfocus policy requests background presentation.
		if master_bus_index >= 0:
			if not _afk_active:
				_master_bus_was_muted_before_focus_loss = AudioServer.is_bus_mute(master_bus_index)
			AudioServer.set_bus_mute(master_bus_index, true)
		_log_background_memory("background_enter")
		if _afk_active and _pending_afk_working_set_trim:
			call_deferred("_run_pending_afk_trim_after_focus_out")
	else:
		_log_background_memory("background_leave_before")
		_background_focus_lost_msec = 0
		if master_bus_index >= 0:
			AudioServer.set_bus_mute(
				master_bus_index,
				_afk_active or _master_bus_was_muted_before_focus_loss
			)
		_log_background_memory("background_leave_after")


func _set_window_focus_power_save(has_focus: bool) -> void :
	if _window_has_focus == has_focus:
		# Minimize may have changed while Windows still reports no focus.
		_sync_background_presentation_state()
		return
	_window_has_focus = has_focus
	_focus_last_change_usec = Time.get_ticks_usec()
	_focus_change_count += 1
	_frame_gap_last_usec = _focus_last_change_usec
	_sync_background_presentation_state()
	_refresh_focus_render_policy()
	_log_focus_power_save("focus_in" if has_focus else "focus_out")


func _set_background_visual_processing(paused: bool) -> void :
	if CursorManager:
		# AFK keeps only the tiny custom cursor process alive while the focused
		# dashboard is visible. World/minimap/effect processing stays suspended.
		CursorManager.set_afk_custom_cursor_enabled(_afk_active)
		CursorManager.set_process(_afk_active or not paused)
	if is_instance_valid(current_day_night_overlay):
		current_day_night_overlay.set_process(not paused)
	if is_instance_valid(current_hud):
		# AFK never needs the 2D world minimap. Only the focused dashboard
		# itself may refresh; background focus suspends its timer entirely.
		if current_hud.has_method("set_background_visuals_paused"):
			current_hud.set_background_visuals_paused(paused or _afk_active)
		if current_hud.has_method("set_afk_dashboard_updates_enabled"):
			current_hud.set_afk_dashboard_updates_enabled(_afk_active and not _background_mode_active)

func _release_background_optional_ui() -> int:
	# Unfocused is intentionally lighter than AFK. Keep the active map,
	# entities and live textures warm, but discard optional hidden windows.
	var released: int = 0
	if is_instance_valid(current_hud) and current_hud.has_method("release_afk_visual_preloads"):
		released += int(current_hud.release_afk_visual_preloads())
	for kind in _prewarmed_hud_windows.keys().duplicate():
		var window: Control = _prewarmed_hud_windows.get(kind, null) as Control
		if is_instance_valid(window):
			window.free()
			released += 1
		_prewarmed_hud_windows.erase(kind)
	return released


func _trim_unfocused_runtime_caches() -> int:
	# Leave map/entity presentation, inventory data, networking and combat
	# simulation alone. AFK is the only mode that strips live presentation.
	var removed: int = _release_background_optional_ui()
	for loader in [DollLoader, MonsterLoader, PetLoader, EffectLoader,
			ItemLoader, SkillLoader, AudioManager]:
		if loader and loader.has_method("trim_unreferenced_runtime_cache"):
			removed += int(loader.trim_unreferenced_runtime_cache())
	return removed

func _ready() -> void :
	_window_has_focus = _observe_window_foreground()
	_focused_max_fps = Engine.max_fps
	if DisplayServer.get_name() != "headless":
		_saved_vsync_mode = DisplayServer.window_get_vsync_mode()
	_apply_performance_profile()
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

func _apply_current_festival_theme(fresh_normal_scene: bool = false) -> Dictionary:
	if not is_instance_valid(current_map_node):
		return {
			"theme": current_festival_theme, 
			"matched": 0, 
			"changed": 0, 
			"failed": 0, 
		}
	var normalized_theme: String = FestivalThemeManager.normalize_theme(current_festival_theme)
	if fresh_normal_scene and normalized_theme == FestivalThemeManager.NORMAL:
		if OS.is_debug_build():
			print("[FESTIVAL_THEME] skipped_fresh_normal=true")
		return {
			"theme": normalized_theme,
			"matched": 0,
			"changed": 0,
			"failed": 0,
		}
	set_frame_gap_stage("festival_theme_apply")
	var started_usec := Time.get_ticks_usec()
	var result: Dictionary = FestivalThemeManager.apply_theme_to_map(current_map_node, normalized_theme)
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

static func should_release_initial_spawn_gate(elapsed_msec: int) -> bool:
	return elapsed_msec >= INITIAL_SPAWN_GATE_MAX_MSEC


func enter_game_world(char_data: Dictionary, map_id: String, spawn_position: Vector2, progress_callback: Callable = Callable()) -> bool:
	# Reset inherited background state before the first visible loading
	# frame: a previous focus-out must not black out this startup overlay.
	_world_ready_for_background_saving = false
	_world_presented_msec = 0
	_refresh_focus_render_policy()
	_initial_loading_progress_value = 0.0
	var raw_num_str = map_id.replace("map", "")
	var target_map_id: int = int(raw_num_str)
	var formatted_map_name = "Map%02d" % target_map_id
	var map_path = _resolve_map_scene_path(target_map_id)
	_last_world_map_audit_path = map_path
	if OS.is_debug_build():
		print("[MAP_STREAM_SOURCE] map=%d source=%s path=%s" % [
			target_map_id,
			"aoi_base" if map_path.contains("/streamed/") else "legacy_full",
			map_path,
		])
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

	var request_error: int = ResourceLoader.load_threaded_request(
		map_path, "", false, ResourceLoader.CACHE_MODE_IGNORE_DEEP
	)
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
	# Release the legacy PackedScene's transient texture graph before HUD
	# warmup. The active map itself owns its near-field visual resources.
	map_resource = null
	if is_instance_valid(current_map_node):
		var early_detach: Dictionary = _map_visual_aoi_streamer.detach_far_before_attach(
			current_map_node, spawn_position, current_performance_profile
		)
		if OS.is_debug_build():
			print("[MAP_INITIAL_TEXTURE_DETACH] map=%d indexed=%d detached=%d" % [
				target_map_id, int(early_detach.get("indexed", 0)), int(early_detach.get("detached", 0))
			])

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

	_bind_map_visual_aoi()
	await _prime_map_visual_aoi(progress_callback)
	_apply_low_resource_world_visual_policy()
	set_world_enhancement_enabled(current_performance_load_mode == ClientPerformanceProfile.LOAD_QUALITY)

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

	# The HUD still subscribes to every gameplay packet, but optional reference
	# windows are now created on first open rather than retained unseen.
	if is_instance_valid(current_hud) and current_hud.has_method("prepare_lazy_windows_before_reveal"):
		_report_initial_loading_progress(progress_callback, 77.0, "準備按需載入功能視窗")
		await current_hud.prepare_lazy_windows_before_reveal()

	_report_initial_loading_progress(progress_callback, 79.0, "套用地圖環境")
	await get_tree().process_frame
	_apply_current_festival_theme(true)
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

	var spawn_gate_started_msec: int = Time.get_ticks_msec()
	while NetworkEntityManager and NetworkEntityManager.has_method("is_initial_spawn_queue_idle") and not NetworkEntityManager.is_initial_spawn_queue_idle():
		if should_release_initial_spawn_gate(Time.get_ticks_msec() - spawn_gate_started_msec):
			var still_queued: int = int(NetworkEntityManager.get_spawn_queue_remaining_count()) \
				if NetworkEntityManager.has_method("get_spawn_queue_remaining_count") else -1
			push_warning("[INITIAL_SPAWN_GATE_TIMEOUT] pending=%d elapsed_ms=%d; presenting world and draining existing queue without dropping entities" % [
				still_queued, Time.get_ticks_msec() - spawn_gate_started_msec
			])
			break
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
	# GameManager has built the world, but EntryManager still owns the black
	# loading overlay and must send WorldReady / fade it out. Keep rendering
	# enabled until notify_world_loading_overlay_hidden() is called.
	_trace_map_change("initial_enter_completed", 0, target_map_id, {
		"total_ms": float(Time.get_ticks_usec() - initial_started_usec) / 1000.0,
	})
	_report_initial_loading_progress(progress_callback, 100.0, "載入完成")
	await get_tree().process_frame
	log_world_visual_audit("world_enter_ready")
	return true

func cleanup_and_return_to_login() -> void :
	set_frame_gap_stage("logout_cleanup")
	log_world_visual_audit("logout_before")
	_log_afk_memory_audit("logout_before")
	_reset_afk_mode()
	# Break large dynamic visual references before queue_free reaches the
	# frame boundary. Logical entity state is untouched.
	_set_afk_dynamic_visuals_suspended(true)
	_world_ready_for_background_saving = false
	_cancel_active_map_transition("cleanup_and_return_to_login")
	_cancel_train_travel("cleanup_and_return_to_login")
	if AudioManager and AudioManager.has_method("release_world_audio_for_login"):
		AudioManager.release_world_audio_for_login()
	else:
		AudioManager.stop_bgm()


	if is_instance_valid(current_player):
		current_player.queue_free()
		current_player = null


	_clear_map_visual_aoi()
	if is_instance_valid(current_map_node):
		current_map_node.queue_free()
		current_map_node = null


	if is_instance_valid(current_hud):
		# The release hook must run while the HUD is still valid. Previously
		# _clear_visual_runtime_caches() tried this only after current_hud=null,
		# so it could never detach hidden optional windows at logout.
		if current_hud.has_method("release_afk_visual_preloads"):
			current_hud.release_afk_visual_preloads()
		current_hud.queue_free()
		current_hud = null
	_remove_day_night_overlay()
	_remove_world_enhancement()


	if NetworkEntityManager:
		NetworkEntityManager.clear_map_entities()
	if LegacyPathfinder and LegacyPathfinder.has_method("clear_runtime_cache"):
		LegacyPathfinder.clear_runtime_cache()
	if FloatingTextManager and FloatingTextManager.has_method("release_world_runtime_for_login"):
		FloatingTextManager.release_world_runtime_for_login()
	if DropItemManager and DropItemManager.has_method("release_world_runtime_for_login"):
		DropItemManager.release_world_runtime_for_login()
	_clear_visual_runtime_caches()


	if GlobalTempData:
		GlobalTempData.selected_player_data = {}
		GlobalTempData.character_list = []


	# Returning to login restores smooth controls even if unfocused.
	_world_presented_msec = 0
	_refresh_focus_render_policy()
	log_world_visual_audit("logout_cleanup_scheduled")
	# Do not rebuild GameInfo/festival item thumbnails immediately on return.
	# They are world/reference UI and can be created lazily on the next entry.
	_skip_next_login_instance_prewarm = true
	get_tree().change_scene_to_file("res://entry_manager.tscn")
	call_deferred("_audit_login_after_logout")

func _clear_visual_runtime_caches() -> void :
	if DollLoader and DollLoader.has_method("clear_runtime_cache"):
		DollLoader.clear_runtime_cache()
	if MonsterLoader and MonsterLoader.has_method("clear_runtime_cache"):
		MonsterLoader.clear_runtime_cache()
	if EffectLoader and EffectLoader.has_method("clear_runtime_cache"):
		EffectLoader.clear_runtime_cache()
	if PetLoader and PetLoader.has_method("clear_runtime_cache"):
		PetLoader.clear_runtime_cache()
	if ItemLoader and ItemLoader.has_method("clear_runtime_cache"):
		ItemLoader.clear_runtime_cache()
	if SkillLoader and SkillLoader.has_method("clear_runtime_cache"):
		SkillLoader.clear_runtime_cache()
	if AudioManager and AudioManager.has_method("clear_runtime_cache"):
		AudioManager.clear_runtime_cache()
	PaperDollEngine.clear_visual_runtime_cache()
	FestivalThemeManager.clear_texture_cache()
	for kind in _prewarmed_hud_windows.keys().duplicate():
		var window = _prewarmed_hud_windows.get(kind, null)
		if is_instance_valid(window):
			window.free()
	_prewarmed_hud_windows.clear()
	if is_instance_valid(current_hud) and current_hud.has_method("release_afk_visual_preloads"):
		current_hud.release_afk_visual_preloads()

func _apply_low_resource_world_visual_policy() -> void:
	# Day/night is purely visual; the server's world clock and player actions
	# continue unchanged. Do not instantiate its overlay in low-resource mode.
	if not is_instance_valid(current_map_node):
		return
	if current_performance_load_mode == ClientPerformanceProfile.LOAD_LOW:
		_remove_day_night_overlay()
	else:
		_ensure_day_night_overlay()


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
	# The HUD loading overlay and its 0.4s fade must remain drawable even
	# for a previously-unfocused client. Re-enable savings after HUD hides.
	_world_ready_for_background_saving = false
	_world_presented_msec = 0
	_refresh_focus_render_policy()
	set_frame_gap_stage("map_transition_loading")
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
	var map_path = _resolve_map_scene_path(target_map_id)
	_last_world_map_audit_path = map_path
	if OS.is_debug_build():
		print("[MAP_STREAM_SOURCE] map=%d source=%s path=%s" % [
			target_map_id,
			"aoi_base" if map_path.contains("/streamed/") else "legacy_full",
			map_path,
		])

	if not ResourceLoader.exists(map_path):
		_trace_map_change("map_missing", source_map_id, target_map_id)
		printerr("❌ [GameManager] 嚴重錯誤！找不到切換的地圖檔案：", map_path)
		is_changing_map = false
		if is_instance_valid(current_player): current_player.set_physics_process(true)
		if is_instance_valid(current_hud) and current_hud.has_method("hide_map_loading"): current_hud.hide_map_loading()
		_cancel_train_travel("map_missing")
		return


	var load_request_error: = ResourceLoader.load_threaded_request(
		map_path, "", false, ResourceLoader.CACHE_MODE_IGNORE_DEEP
	)
	_trace_map_change("load_requested", source_map_id, target_map_id)
	if load_request_error != OK:
		_trace_map_change("load_request_error", source_map_id, target_map_id, {"error": load_request_error})


	await get_tree().create_timer(MAP_LOADING_COVER_DELAY_SECONDS).timeout
	if not _is_map_transition_current(transition_id):
		return


	set_frame_gap_stage("map_transition_cleanup")
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

	_clear_map_visual_aoi()
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

	set_frame_gap_stage("map_transition_instantiate")
	var instantiate_started_usec: = Time.get_ticks_usec()
	current_map_node = map_resource.instantiate()
	map_resource = null
	if is_instance_valid(current_map_node) and not _afk_active:
		var early_detach: Dictionary = _map_visual_aoi_streamer.detach_far_before_attach(
			current_map_node, Vector2(target_x, target_y), current_performance_profile
		)
		if OS.is_debug_build():
			print("[MAP_INITIAL_TEXTURE_DETACH] map=%d indexed=%d detached=%d" % [
				target_map_id, int(early_detach.get("indexed", 0)), int(early_detach.get("detached", 0))
			])
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

	set_frame_gap_stage("map_transition_add_child")
	var add_child_started_usec: = Time.get_ticks_usec()
	get_tree().root.add_child(current_map_node)
	if _afk_active:
		current_map_node.visible = false
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


	set_frame_gap_stage("map_transition_player_reparent")
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

	_bind_map_visual_aoi()
	await _prime_map_visual_aoi()
	if is_instance_valid(current_hud) and current_hud.has_method("update_map_info"):
		current_hud.update_map_info(target_map_id, current_map_node.name)
	if CursorManager:
		CursorManager.set_cursor_state("normal")

	set_frame_gap_stage("map_transition_finalize")
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
	if _afk_active:
		call_deferred("_refresh_afk_map_visuals_after_change")


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
	set_frame_gap_stage("world_running")
	log_performance_checkpoint("map_transition_ready")
	if _train_travel_active and _train_travel_phase == "loading_map":
		await _finish_train_travel_after_map_change()
