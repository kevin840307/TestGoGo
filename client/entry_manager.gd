
extends Control

@onready var login_screen: Control = $LoginScreen
@onready var character_select_layer: CanvasLayer = $CharacterSelectLayer


@onready var error_dialog: Control = %ErrorDialog
@onready var message_label: Label = %ErrorDialog / MessageLabel
@onready var confirm_button: Button = %ErrorDialog / ConfirmButton

var _loading_session_id: int = 0
var _loading_progress_value: float = 0.0
var __login_select_session_id_id: int = -1
var _world_loading_session_id: int = -1

# The account form stays locked until this many truly rendered foreground
# frames pass without a long first-use shader/video/UI spike.
const LOGIN_REVEAL_STABLE_FRAMES: int = 18
const LOGIN_REVEAL_MAX_FRAME_USEC: int = 42000
const LOGIN_VIDEO_READY_TIMEOUT_USEC: int = 3500000
const LOGIN_SETTLE_VIDEO_FALLBACK_USEC: int = 6000000

func _begin_loading_session() -> int:
	_loading_session_id += 1
	_loading_progress_value = 0.0
	var overlay = get_node_or_null("%LoadingOverlay")
	if overlay:
		overlay.visible = true
		var bar = overlay.get_node_or_null("LoadingBar") as SmoothLoadingBar
		if bar:
			bar.reset_progress()
	return _loading_session_id

func _begin_login_select_loading_session() -> int:
	__login_select_session_id_id = _begin_loading_session()
	_set_loading_progress(0.0, __login_select_session_id_id)
	return __login_select_session_id_id

func _debug_perf_checkpoint(checkpoint: String) -> void:
	if not OS.is_debug_build():
		return
	if GameManager and GameManager.has_method("log_performance_checkpoint"):
		GameManager.log_performance_checkpoint(checkpoint)


func _set_loading_progress(value: float, session_id: int = -1) -> void :
	if session_id >= 0 and session_id != _loading_session_id:
		return
	var overlay = get_node_or_null("%LoadingOverlay")
	if not overlay:
		return
	_loading_progress_value = maxf(_loading_progress_value, clampf(value, 0.0, 100.0))
	var bar = overlay.get_node_or_null("LoadingBar") as SmoothLoadingBar
	if bar:
		if _loading_progress_value >= 100.0:
			bar.finish_progress()
		else:
			bar.set_target_progress(_loading_progress_value)

func _set_startup_loading_status(status: String) -> void:
	var overlay = get_node_or_null("%LoadingOverlay")
	if not overlay:
		return
	var label := overlay.get_node_or_null("StatusLabel") as Label
	if label:
		label.text = status


func _ready() -> void :
	if login_screen: login_screen.visible = false
	if character_select_layer: character_select_layer.visible = false
	if error_dialog: error_dialog.visible = false
	# Display the loading overlay before any expensive startup work.
	# This is also visible directly in entry_manager.tscn, so startup is
	# never an unlabelled black rectangle while _ready() is executing.
	var overlay := get_node_or_null("%LoadingOverlay")
	if overlay:
		overlay.visible = true
	# Before initial login is ready, permit low-rate loading-bar redraw even
	# if Windows briefly considers the newly launched app unfocused.
	if GameManager and GameManager.has_method("set_startup_loading_overlay_active"):
		GameManager.set_startup_loading_overlay_active(true)
	_set_loading_progress(1.0)
	_set_startup_loading_status("正在準備遊戲資源…")

	if confirm_button: confirm_button.pressed.connect(_on_error_confirm_pressed)

	NetworkManager.login_finished.connect(_on_global_login_finished)
	if NetworkManager.has_signal("character_password_modify_finished"): NetworkManager.character_password_modify_finished.connect(_on_global_password_modify_finished)
	if NetworkManager.has_signal("create_character_response_received"): NetworkManager.create_character_response_received.connect(_on_server_create_character_response)
	if NetworkManager.has_signal("enter_world_success"): NetworkManager.enter_world_success.connect(_on_server_enter_world_success)

	_connect_character_select_signals()

	# Do not start heavy resource indexing until the first loading frame has
	# actually had a chance to render. Startup must not look like a hang.
	call_deferred("_bootstrap_before_login")

# Optional crafting UI is deliberately excluded: building 1453 recipe rows
# can cost 18 seconds during cold startup, even when the player never crafts.
const GAME_HUD_INSTANCE_PREWARM: Dictionary = {
	"game_info": "res://game_info_window.tscn",
}

const GAME_HUD_PREWARM_PATHS: Array[String] = [
	"res://player.tscn",
	"res://game_hud.tscn",
	"res://quest_window.tscn",
	"res://mall_window.tscn",
	"res://announcement_window.tscn",
	"res://game_info_window.tscn",
	"res://big_map_window.tscn",
	"res://world_map_window.tscn",
]

func _prewarm_game_hud_resources() -> void :
	for path: String in GAME_HUD_PREWARM_PATHS:
		if ResourceLoader.has_cached(path):
			continue
		var request_error: int = ResourceLoader.load_threaded_request(path)
		if request_error != OK:
			continue

	# Poll in the background without blocking login input/rendering. Calling
	# load_threaded_get once LOADED places the PackedScene and dependencies in
	# ResourceLoader's cache for GameHUD to reuse later.
	var pending: Array[String] = GAME_HUD_PREWARM_PATHS.duplicate()
	var total_resources: int = pending.size()
	while not pending.is_empty() and is_inside_tree():
		for index: int in range(pending.size() - 1, -1, -1):
			var path: String = pending[index]
			if ResourceLoader.has_cached(path):
				pending.remove_at(index)
				continue
			var status: int = ResourceLoader.load_threaded_get_status(path)
			if status == ResourceLoader.THREAD_LOAD_LOADED:
				ResourceLoader.load_threaded_get(path)
				pending.remove_at(index)
			elif status == ResourceLoader.THREAD_LOAD_FAILED or status == ResourceLoader.THREAD_LOAD_INVALID_RESOURCE:
				pending.remove_at(index)
		var completed_resources: int = total_resources - pending.size()
		if total_resources > 0:
			_set_loading_progress(30.0 + (float(completed_resources) / float(total_resources)) * 25.0)
			_set_startup_loading_status("載入遊戲視窗資源 %d/%d…" % [completed_resources, total_resources])
		await get_tree().process_frame

	_set_loading_progress(55.0)
	# Resource preloading alone is not enough for the two expensive windows:
	# their first SceneTree entry/layout is the real hitch. Pay that cost here,
	# while the player is still on login/character selection, and keep the same
	# instances alive for GameHUD to adopt later.
	for kind_value in GAME_HUD_INSTANCE_PREWARM.keys():
		if not is_inside_tree():
			return
		var kind: String = str(kind_value)
		if GameManager and GameManager.has_method("has_prewarmed_hud_window") and GameManager.has_prewarmed_hud_window(kind):
			continue
		var path: String = str(GAME_HUD_INSTANCE_PREWARM[kind])
		var scene := ResourceLoader.load(path) as PackedScene
		if scene == null:
			continue
		var started_usec: int = Time.get_ticks_usec()
		var window := scene.instantiate() as Control
		if window == null:
			continue
		window.visible = false
		window.modulate.a = 0.0
		if GameManager and GameManager.has_method("cache_prewarmed_hud_window"):
			GameManager.cache_prewarmed_hud_window(kind, window)

		if kind == "game_info":
			_set_loading_progress(92.0)
			_set_startup_loading_status("準備角色資訊視窗…")

		var elapsed_ms: float = float(Time.get_ticks_usec() - started_usec) / 1000.0
		if OS.is_debug_build():
			print("[LOGIN_HUD_PREWARM] kind=%s total_ms=%.2f" % [kind, elapsed_ms])
		await get_tree().process_frame

static func is_login_frame_stable(focused: bool, rendering: bool, drawn: bool, frame_usec: int) -> bool:
	return focused and rendering and drawn and frame_usec > 0 and frame_usec <= LOGIN_REVEAL_MAX_FRAME_USEC


static func can_count_login_stable_frame(focused: bool, rendering: bool, drawn: bool, video_ready: bool, frame_usec: int) -> bool:
	return video_ready and is_login_frame_stable(focused, rendering, drawn, frame_usec)


static func can_reveal_login(focused: bool, video_ready: bool, stable_frames: int) -> bool:
	return focused and video_ready and stable_frames >= LOGIN_REVEAL_STABLE_FRAMES


func _wait_for_login_visual_ready() -> void:
	var started_usec: int = Time.get_ticks_usec()
	var foreground_started_usec: int = 0
	var previous_usec: int = started_usec
	var previous_drawn: int = Engine.get_frames_drawn()
	var stable_frames: int = 0
	var fallback_used: bool = false
	var previous_status: String = ""
	while is_inside_tree():
		# The signal runs before a frame is drawn; measure the preceding
		# complete frame, not merely the VideoStreamPlayer.play() call.
		await get_tree().process_frame
		var now_usec: int = Time.get_ticks_usec()
		var frame_usec: int = now_usec - previous_usec
		previous_usec = now_usec
		if GameManager and GameManager.has_method("reconcile_startup_window_focus"):
			GameManager.reconcile_startup_window_focus()
		var focused: bool = DisplayServer.window_is_focused()
		if GameManager:
			focused = focused and bool(GameManager.get("_window_has_focus"))
		if focused:
			if foreground_started_usec == 0:
				foreground_started_usec = now_usec
		else:
			foreground_started_usec = 0
		var foreground_elapsed_usec: int = now_usec - foreground_started_usec if foreground_started_usec > 0 else 0
		var drawn: int = Engine.get_frames_drawn()
		var advanced: bool = drawn > previous_drawn
		previous_drawn = drawn
		var video_ready: bool = true
		if is_instance_valid(login_screen) and login_screen.has_method("is_background_video_frame_ready"):
			video_ready = bool(login_screen.call("is_background_video_frame_ready"))
		var elapsed_usec: int = now_usec - started_usec
		if not fallback_used and focused and (
			(not video_ready and foreground_elapsed_usec >= LOGIN_VIDEO_READY_TIMEOUT_USEC)
			or (stable_frames < LOGIN_REVEAL_STABLE_FRAMES and foreground_elapsed_usec >= LOGIN_SETTLE_VIDEO_FALLBACK_USEC)
		):
			fallback_used = true
			if is_instance_valid(login_screen) and login_screen.has_method("use_static_background_after_video_timeout"):
				login_screen.call("use_static_background_after_video_timeout")
			video_ready = true
			stable_frames = 0
			if OS.is_debug_build():
				print("[LOGIN_VIDEO_FALLBACK] elapsed_ms=%.1f" % (float(elapsed_usec) / 1000.0))
		# Stable frames counted before the first decoded OGV frame do not
		# establish video/UI readiness. Begin the consecutive-frame gate
		# only AFTER media is ready (or the static fallback is selected).
		if can_count_login_stable_frame(focused, RenderingServer.render_loop_enabled, advanced, video_ready, frame_usec):
			stable_frames += 1
		else:
			stable_frames = 0
		var status: String
		if not focused:
			status = "載入完成，等待切回遊戲視窗…"
		elif not video_ready:
			status = "正在解碼登入背景畫面…"
		else:
			status = "正在確認登入畫面流暢度… %d/%d" % [stable_frames, LOGIN_REVEAL_STABLE_FRAMES]
		if status != previous_status:
			_set_startup_loading_status(status)
			previous_status = status
		if can_reveal_login(focused, video_ready, stable_frames):
			if OS.is_debug_build():
				print("[LOGIN_VISUAL_READY] elapsed_ms=%.1f stable_frames=%d video_fallback=%s frame_ms=%.2f" % [
					float(elapsed_usec) / 1000.0, stable_frames, str(fallback_used), float(frame_usec) / 1000.0,
				])
			return


func _bootstrap_before_login() -> void :
	var overlay = get_node_or_null("%LoadingOverlay")
	if overlay:
		overlay.visible = true
	if login_screen:
		login_screen.visible = false
	if character_select_layer:
		character_select_layer.visible = false

	# Draw the loading bar before indexing; crafting remains first-open only.
	await get_tree().process_frame
	await get_tree().process_frame
	if not is_inside_tree():
		return

	_set_startup_loading_status("索引物品資料…")
	if ItemLoader: ItemLoader.preload_all_item_data()
	_set_loading_progress(10.0)
	_set_startup_loading_status("索引技能資料…")
	if SkillLoader: SkillLoader.preload_all_skill_data()
	_set_loading_progress(20.0)
	_set_startup_loading_status("索引寵物資料…")
	if PetLoader: PetLoader.preload_all_pet_data()
	_set_loading_progress(30.0)

	_set_startup_loading_status("準備遊戲視窗…")
	await _prewarm_game_hud_resources()

	# Render the real video and login layout BEHIND the opaque loading
	# overlay while the fields remain locked. Two process frames alone
	# never guaranteed a decoded video frame or recovered 60 FPS focus.
	_set_loading_progress(95.0)
	_set_startup_loading_status("準備登入背景及音樂…")
	if is_instance_valid(login_screen):
		if login_screen.has_method("prepare_form_behind_loading"):
			login_screen.call("prepare_form_behind_loading")
		if login_screen.has_method("warm_background_video"):
			login_screen.call("warm_background_video")
	if AudioManager and AudioManager.has_method("play_login_bgm"):
		AudioManager.play_login_bgm()
	_set_loading_progress(99.0)
	await _wait_for_login_visual_ready()
	if not is_inside_tree():
		return
	_set_startup_loading_status("登入畫面準備完成")
	_set_loading_progress(100.0)
	# Keep the loading overlay visible while the bar completes its tween.
	await get_tree().create_timer(0.30).timeout
	if not is_inside_tree():
		return
	var final_foreground: bool = DisplayServer.window_is_focused() and RenderingServer.render_loop_enabled
	if GameManager:
		final_foreground = final_foreground and bool(GameManager.get("_window_has_focus"))
	if not final_foreground:
		await _wait_for_login_visual_ready()
	if is_inside_tree():
		_start_game_entry_flow()


func _start_game_entry_flow() -> void :
	# The form has been laid out and rendered for stable foreground frames.
	# The input stays locked until this final hand-off.
	_show_login_screen()
	hide_loading_overlay()
	if GameManager and GameManager.has_method("set_startup_loading_overlay_active"):
		GameManager.set_startup_loading_overlay_active(false)


func _show_login_screen() -> void :
	_debug_perf_checkpoint("login_screen_ready")
	if login_screen:
		login_screen.visible = true
		login_screen.modulate.a = 1.0
		if login_screen.has_method("reveal_form_after_warmup"):
			login_screen.call("reveal_form_after_warmup")
		if login_screen.has_method("mark_login_screen_revealed"):
			login_screen.call("mark_login_screen_revealed")
		var account_input := login_screen.get_node_or_null("LoginPanel/AccountInput") as LineEdit
		if account_input and DisplayServer.window_is_focused():
			account_input.grab_focus()


func _connect_character_select_signals() -> void :
	if not character_select_layer: return
	var select_script_node = character_select_layer.get_node_or_null("CharacterSelect")
	if select_script_node:
		if not select_script_node.character_selected_and_verified.is_connected(_on_character_verified_and_enter_game):
			select_script_node.character_selected_and_verified.connect(_on_character_verified_and_enter_game)

		if select_script_node.has_signal("modify_character_password_requested"):
			if not select_script_node.modify_character_password_requested.is_connected(_on_modify_character_password_requested):
				select_script_node.modify_character_password_requested.connect(_on_modify_character_password_requested)

		if select_script_node.has_signal("create_character_requested"):
			if not select_script_node.create_character_requested.is_connected(_on_create_character_requested):
				select_script_node.create_character_requested.connect(_on_create_character_requested)


		if select_script_node.has_signal("delete_character_requested"):
			if not select_script_node.delete_character_requested.is_connected(_on_delete_character_requested):
				select_script_node.delete_character_requested.connect(_on_delete_character_requested)

func _on_global_login_finished(success: bool, _msg: String) -> void :
	if success:
		# Independent loading session #1 starts when the user presses Login.
		# Continue that same session here instead of resetting it on success.
		var overlay = get_node_or_null("%LoadingOverlay")
		if __login_select_session_id_id < 0 or __login_select_session_id_id != _loading_session_id:
			__login_select_session_id_id = _begin_loading_session()
		_set_loading_progress(3.0, __login_select_session_id_id)
		var tween = create_tween()
		if login_screen:
			tween.tween_property(login_screen, "modulate:a", 0.0, 0.5).set_trans(Tween.TRANS_SINE)

		tween.tween_callback( func():
			if login_screen: login_screen.visible = false
			if character_select_layer:
				var select_node = character_select_layer.get_node_or_null("CharacterSelect")
				if select_node: select_node.modulate.a = 0.0
				character_select_layer.visible = true
				_set_loading_progress(6.0)
				call_deferred("_start_character_selection_load")
		)

func _start_character_selection_load() -> void :
	_set_loading_progress(8.0)
	await get_tree().process_frame
	_set_loading_progress(12.0)

	var select_script_node = character_select_layer.get_node_or_null("CharacterSelect")
	if select_script_node and select_script_node.has_method("request_character_list"):
		_connect_character_select_signals()
		await select_script_node.request_character_list()
		_debug_perf_checkpoint("character_select_ready")

		if is_instance_valid(select_script_node):
			select_script_node.modulate.a = 1.0

func hide_loading_overlay() -> void :
	var overlay = get_node_or_null("%LoadingOverlay")
	if overlay: overlay.visible = false

func show_error_popup(err_text: String) -> void :

	if character_select_layer and character_select_layer.visible:
		var select_script_node = character_select_layer.get_node_or_null("CharacterSelect")
		if select_script_node and select_script_node.has_method("_show_error_popup"):
			select_script_node._show_error_popup(err_text)
			return

	if error_dialog and message_label:
		message_label.text = err_text


		if error_dialog.get_parent():
			error_dialog.get_parent().move_child(error_dialog, -1)

		error_dialog.visible = true
		if confirm_button: confirm_button.grab_focus()

func _on_error_confirm_pressed() -> void :
	if error_dialog: error_dialog.visible = false

	if login_screen and login_screen.visible:
		var pwd_edit = login_screen.get_node_or_null("PasswordInput") as LineEdit
		if pwd_edit:
			pwd_edit.grab_focus()
			pwd_edit.select_all()
	elif character_select_layer and character_select_layer.visible:
		var select_script_node = character_select_layer.get_node_or_null("CharacterSelect")
		if select_script_node:

			if "create_char_dialog" in select_script_node and select_script_node.create_char_dialog.visible:
				if "create_name_edit" in select_script_node and select_script_node.create_name_edit.text.is_empty():
					select_script_node.create_name_edit.grab_focus()
				elif "create_pwd_edit" in select_script_node and select_script_node.create_pwd_edit.text.is_empty():
					select_script_node.create_pwd_edit.grab_focus()
				return

			if "modify_password_dialog" in select_script_node and select_script_node.modify_password_dialog.visible:
				if "old_password_edit" in select_script_node and select_script_node.old_password_edit.text.is_empty():
					select_script_node.old_password_edit.grab_focus()
				elif "new_password_edit" in select_script_node:
					select_script_node.new_password_edit.grab_focus()
				return

			if "password_edit" in select_script_node and "char_password_dialog" in select_script_node:
				var p_edit = select_script_node.password_edit as LineEdit
				if p_edit and select_script_node.char_password_dialog.visible:
					p_edit.text = ""
					p_edit.grab_focus()


func _on_create_character_requested(char_name: String, race: int, hair: int, hair_color: int, pwd: String, str_pts: int, dex_pts: int, con_pts: int, wis_pts: int) -> void :

	var acc_id = GlobalTempData.character_list[0]["ACCID"]
	NetworkManager.send_create_character_request(acc_id, char_name, race, hair, hair_color, pwd, str_pts, dex_pts, con_pts, wis_pts)

func _on_server_create_character_response(success: bool, message: String) -> void :
	if success:
		var select_script_node = character_select_layer.get_node_or_null("CharacterSelect")
		if select_script_node:

			if "create_char_dialog" in select_script_node:
				select_script_node.create_char_dialog.visible = false


			var user = GlobalTempData.login_user
			var pwd = GlobalTempData.login_pwd
			if user != "":
				NetworkManager.send_login_request(user, pwd)
			else:
				show_error_popup("創建成功，但無法自動重整列表，請重新登入！")
	else:
		show_error_popup(message)


func _on_delete_character_requested(char_slot: int, dbid: int, pwd: String) -> void :
	var acc_id = 0

	if GlobalTempData.character_list.size() > char_slot and char_slot >= 0:
		acc_id = int(GlobalTempData.character_list[char_slot].get("ACCID", 0))
	elif GlobalTempData.character_list.size() > 0:
		acc_id = int(GlobalTempData.character_list[0].get("ACCID", 0))

	NetworkManager.send_delete_character_request(acc_id, dbid, pwd, char_slot)

func _on_server_delete_character_response(success: bool, message: String, char_slot: int, deleted_dbid: int) -> void :
	show_error_popup(message)
	if success:

		var user = GlobalTempData.login_user
		var pwd = GlobalTempData.login_pwd
		if user != "":
			NetworkManager.send_login_request(user, pwd)
		else:
			show_error_popup("角色已刪除！但無法自動重整列表，請重新登入！")


func _on_modify_character_password_requested(char_slot: int, dbid: int, new_pwd: String) -> void :
	NetworkManager.send_modify_password_request(char_slot, dbid, new_pwd)

func _on_global_password_modify_finished(success: bool, msg: String, char_slot: int, new_pwd: String) -> void :
	if success:
		show_error_popup("角色安全密碼修改成功！")
		var select_script_node = character_select_layer.get_node_or_null("CharacterSelect")
		if GlobalTempData.character_list.size() > char_slot:
			GlobalTempData.character_list[char_slot]["CharacterPwd"] = new_pwd
	else:
		show_error_popup("修改失敗：" + msg)




func _on_character_verified_and_enter_game(char_data: Dictionary, _map_id: String, _spawn_position: Vector2) -> void :
	# Independent loading session #2: character select -> world.
	var overlay = get_node_or_null("%LoadingOverlay")
	var select_script_node = character_select_layer.get_node_or_null("CharacterSelect")
	_world_loading_session_id = _begin_loading_session()
	_debug_perf_checkpoint("world_load_start")


	if overlay:
		for child in overlay.get_children():
			if "modulate" in child:
				child.modulate.a = 0.0
		_set_loading_progress(0.0, _world_loading_session_id)


	var tween = create_tween()
	tween.set_parallel(true)

	if select_script_node:
		tween.tween_property(select_script_node, "modulate:a", 0.0, 0.5).set_trans(Tween.TRANS_SINE)

	if overlay:
		for child in overlay.get_children():
			if "modulate" in child:
				tween.tween_property(child, "modulate:a", 1.0, 0.5).set_trans(Tween.TRANS_SINE)


	await tween.finished
	await get_tree().process_frame
	await get_tree().process_frame


	_set_loading_progress(3.0, _world_loading_session_id)
	var char_dbid: int = int(float(char_data.get("DBID", 0)))
	NetworkManager.send_select_character_request(char_dbid)


func _on_server_enter_world_success(server_full_data: Dictionary) -> void :
	if login_screen: login_screen.visible = false
	if character_select_layer: character_select_layer.visible = false

	var overlay = get_node_or_null("%LoadingOverlay")
	_set_loading_progress(5.0, _world_loading_session_id)


	var default_map_int = GameConstants.DEFAULT_MAP_ID.replace("map", "").to_int()
	var map_id = "map" + str(server_full_data.get("MapID", default_map_int))
	var spawn_pos = Vector2(
		float(server_full_data.get("LocX", GameConstants.DEFAULT_SPAWN_POSITION.x)), 
		float(server_full_data.get("LocY", GameConstants.DEFAULT_SPAWN_POSITION.y))
	)


	var progress_callback := func(value: float, _stage: String) -> void:
		# GameManager owns the heavy world-building work, but it must not consume
		# the final 5% because WorldReady/final presentation still happens here.
		var mapped_value: float = 5.0 + clampf(value, 0.0, 100.0) * 0.90
		_set_loading_progress(mapped_value, _world_loading_session_id)

	var load_success = await GameManager.enter_game_world(server_full_data, map_id, spawn_pos, progress_callback)

	if not load_success:
		show_error_popup("遊戲地圖資源遺失，無法切換場景！")
		if overlay: overlay.visible = false
		if character_select_layer:
			character_select_layer.visible = true
			var select_script_node = character_select_layer.get_node_or_null("CharacterSelect")
			if select_script_node: select_script_node.modulate.a = 1.0
		return

	_set_loading_progress(96.0, _world_loading_session_id)

	# The map and entity containers are fully ready at this point. Tell the
	# server now, while the loading overlay is still covering the world, so the
	# authoritative ground-item snapshot can arrive and instantiate before the
	# player sees the map.
	if NetworkManager and NetworkManager.has_method("send_world_ready"):
		NetworkManager.send_world_ready(int(server_full_data.get("MapID", 0)))
	_set_loading_progress(97.0, _world_loading_session_id)
	await get_tree().process_frame
	_set_loading_progress(99.0, _world_loading_session_id)
	await get_tree().process_frame
	_set_loading_progress(100.0, _world_loading_session_id)
	await get_tree().process_frame

	var fade_world_tween = create_tween()
	fade_world_tween.set_parallel(true)
	var has_fade_target = false

	if overlay:
		for child in overlay.get_children():
			if "modulate" in child:
				fade_world_tween.tween_property(child, "modulate:a", 0.0, 0.5).set_trans(Tween.TRANS_SINE)
				has_fade_target = true

		if has_fade_target:
			await fade_world_tween.finished

		overlay.visible = false

	_debug_perf_checkpoint("world_ready")
	queue_free()
