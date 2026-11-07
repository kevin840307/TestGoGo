
extends Control

@onready var login_screen: Control = $LoginScreen
@onready var character_select_layer: CanvasLayer = $CharacterSelectLayer


@onready var error_dialog: Control = %ErrorDialog
@onready var message_label: Label = %ErrorDialog / MessageLabel
@onready var confirm_button: Button = %ErrorDialog / ConfirmButton

func _ready() -> void :

	if login_screen: login_screen.visible = false
	if character_select_layer: character_select_layer.visible = false
	if error_dialog: error_dialog.visible = false

	if confirm_button: confirm_button.pressed.connect(_on_error_confirm_pressed)

	NetworkManager.login_finished.connect(_on_global_login_finished)
	if NetworkManager.has_signal("character_password_modify_finished"): NetworkManager.character_password_modify_finished.connect(_on_global_password_modify_finished)
	if NetworkManager.has_signal("create_character_response_received"): NetworkManager.create_character_response_received.connect(_on_server_create_character_response)
	if NetworkManager.has_signal("enter_world_success"): NetworkManager.enter_world_success.connect(_on_server_enter_world_success)

	if ItemLoader: ItemLoader.preload_all_item_data()
	if SkillLoader: SkillLoader.preload_all_skill_data()
	if PetLoader: PetLoader.preload_all_pet_data()

	_connect_character_select_signals()

	# Prewarm heavy HUD scenes while the player is still on login / character
	# selection screens. This keeps first in-world window opening free of disk
	# and dependency loading stalls.
	call_deferred("_prewarm_game_hud_resources")

	_start_game_entry_flow()

const GAME_HUD_PREWARM_PATHS: Array[String] = [
	"res://quest_window.tscn",
	"res://mall_window.tscn",
	"res://announcement_window.tscn",
	"res://game_info_window.tscn",
	"res://craft_dismantle_window.tscn",
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
		await get_tree().process_frame

func _start_game_entry_flow() -> void :
	hide_loading_overlay()
	_show_login_screen()

func _show_login_screen() -> void :
	if AudioManager and AudioManager.has_method("play_login_bgm"):
		AudioManager.play_login_bgm()

	if login_screen:
		login_screen.modulate.a = 0.0
		login_screen.visible = true
		var account_input: = login_screen.get_node_or_null("LoginPanel/AccountInput") as LineEdit
		if account_input:
			account_input.call_deferred("grab_focus")


		var tween = create_tween()
		tween.tween_property(login_screen, "modulate:a", 1.0, 1.0).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_OUT)

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
		var overlay = get_node_or_null("%LoadingOverlay")
		if overlay: overlay.visible = true

		var tween = create_tween()
		if login_screen:
			tween.tween_property(login_screen, "modulate:a", 0.0, 0.5).set_trans(Tween.TRANS_SINE)

		tween.tween_callback( func():
			if login_screen: login_screen.visible = false
			if character_select_layer:
				var select_node = character_select_layer.get_node_or_null("CharacterSelect")
				if select_node: select_node.modulate.a = 0.0
				character_select_layer.visible = true
				call_deferred("_start_character_selection_load")
		)

func _start_character_selection_load() -> void :
	var select_script_node = character_select_layer.get_node_or_null("CharacterSelect")
	if select_script_node and select_script_node.has_method("request_character_list"):
		_connect_character_select_signals()
		select_script_node.request_character_list()

		var tween = create_tween()
		tween.tween_property(select_script_node, "modulate:a", 1.0, 0.6).set_trans(Tween.TRANS_SINE)

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
	var overlay = get_node_or_null("%LoadingOverlay")
	var select_script_node = character_select_layer.get_node_or_null("CharacterSelect")


	if overlay:
		overlay.visible = true
		for child in overlay.get_children():
			if "modulate" in child:
				child.modulate.a = 0.0
		var bar = overlay.get_node_or_null("LoadingBar") as ProgressBar
		if bar: bar.value = 10.0


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


	var char_dbid: int = int(float(char_data.get("DBID", 0)))
	NetworkManager.send_select_character_request(char_dbid)


func _on_server_enter_world_success(server_full_data: Dictionary) -> void :
	if login_screen: login_screen.visible = false
	if character_select_layer: character_select_layer.visible = false

	var overlay = get_node_or_null("%LoadingOverlay")
	if overlay:
		var bar = overlay.get_node_or_null("LoadingBar") as ProgressBar
		if bar: bar.value = 50.0


	var default_map_int = GameConstants.DEFAULT_MAP_ID.replace("map", "").to_int()
	var map_id = "map" + str(server_full_data.get("MapID", default_map_int))
	var spawn_pos = Vector2(
		float(server_full_data.get("LocX", GameConstants.DEFAULT_SPAWN_POSITION.x)), 
		float(server_full_data.get("LocY", GameConstants.DEFAULT_SPAWN_POSITION.y))
	)


	var progress_callback := func(value: float, _stage: String) -> void:
		if not is_instance_valid(overlay):
			return
		var progress_bar := overlay.get_node_or_null("LoadingBar") as ProgressBar
		if progress_bar:
			progress_bar.value = value

	var load_success = await GameManager.enter_game_world(server_full_data, map_id, spawn_pos, progress_callback)

	if not load_success:
		show_error_popup("遊戲地圖資源遺失，無法切換場景！")
		if overlay: overlay.visible = false
		if character_select_layer:
			character_select_layer.visible = true
			var select_script_node = character_select_layer.get_node_or_null("CharacterSelect")
			if select_script_node: select_script_node.modulate.a = 1.0
		return

	if overlay:
		var bar = overlay.get_node_or_null("LoadingBar") as ProgressBar
		if bar: bar.value = 100.0


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

	if NetworkManager and NetworkManager.has_method("send_world_ready"):
		NetworkManager.send_world_ready(int(server_full_data.get("MapID", 0)))

	queue_free()
