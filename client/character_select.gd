
extends Control


signal character_selected_and_verified(char_data: Dictionary, map_id: String, spawn_position: Vector2)

signal modify_character_password_requested(char_slot: int, dbid: int, new_pwd: String)

signal create_character_requested(char_name: String, race: int, hair: int, hair_color: int, pwd: String, str_pts: int, dex_pts: int, con_pts: int, wis_pts: int)
signal delete_character_requested(char_slot: int, dbid: int, pwd: String)


const DOLL_SCENE = preload("res://paper_doll_engine.tscn")

@onready var slots: Array[Control] = [$Slot0, $Slot1, $Slot2]

@onready var back_button: Button = $BackButton


@onready var char_password_dialog: Control = $CharPasswordDialog
@onready var password_edit: LineEdit = $CharPasswordDialog / PasswordEdit
@onready var pwd_confirm_button: Button = $CharPasswordDialog / ConfirmButton
@onready var pwd_cancel_button: Button = $CharPasswordDialog / CancelButton


@onready var modify_password_dialog: Control = $ModifyPasswordDialog
@onready var old_password_edit: LineEdit = $ModifyPasswordDialog / OldPasswordEdit
@onready var new_password_edit: LineEdit = $ModifyPasswordDialog / NewPasswordEdit
@onready var confirm_new_password_edit: LineEdit = $ModifyPasswordDialog / ConfirmNewPasswordEdit
@onready var modify_confirm_button: Button = $ModifyPasswordDialog / ModifyConfirmButton
@onready var modify_cancel_button: Button = $ModifyPasswordDialog / ModifyCancelButton




@onready var create_char_dialog: Control = $CreateCharDialog
@onready var create_char_preview: Control = $CreateCharDialog / CharPreview
@onready var create_name_edit: LineEdit = $CreateCharDialog / NameEdit
@onready var create_pwd_edit: LineEdit = $CreateCharDialog / PwdEdit
@onready var create_confirm_pwd_edit: LineEdit = $CreateCharDialog / ConfirmPwdEdit


@onready var btn_previous_race: TextureButton = $CreateCharDialog / PreviousRaceButton
@onready var create_race_label: Label = $CreateCharDialog / RaceValueLabel
@onready var btn_next_race: TextureButton = $CreateCharDialog / NextRaceButton
@onready var btn_previous_hair: TextureButton = $CreateCharDialog / PreviousHairStyleButton
@onready var create_hair_label: Label = $CreateCharDialog / HairStyleValueLabel
@onready var btn_next_hair: TextureButton = $CreateCharDialog / NextHairStyleButton
@onready var btn_previous_hair_color: TextureButton = $CreateCharDialog / PreviousHairColorButton
@onready var create_hair_color_label: Label = $CreateCharDialog / HairColorValueLabel
@onready var btn_next_hair_color: TextureButton = $CreateCharDialog / NextHairColorButton


@onready var create_total_pt_label: Label = $CreateCharDialog / TotalPointLabel
@onready var create_str_label: Label = $CreateCharDialog / StrLabel
@onready var create_dex_label: Label = $CreateCharDialog / DexLabel
@onready var create_con_label: Label = $CreateCharDialog / ConLabel
@onready var create_wis_label: Label = $CreateCharDialog / WisLabel


@onready var btn_add_str: TextureButton = $CreateCharDialog / AddStrButton
@onready var btn_minus_str: TextureButton = $CreateCharDialog / MinusStrButton
@onready var btn_add_dex: TextureButton = $CreateCharDialog / AddDexButton
@onready var btn_minus_dex: TextureButton = $CreateCharDialog / MinusDexButton
@onready var btn_add_con: TextureButton = $CreateCharDialog / AddConButton
@onready var btn_minus_con: TextureButton = $CreateCharDialog / MinusConButton
@onready var btn_add_wis: TextureButton = $CreateCharDialog / AddWisButton
@onready var btn_minus_wis: TextureButton = $CreateCharDialog / MinusWisButton

@onready var create_confirm_button: Button = $CreateCharDialog / ConfirmButton
@onready var create_cancel_button: Button = $CreateCharDialog / CancelButton



@onready var error_dialog: Control = $ErrorDialog
@onready var error_message_label: Label = $ErrorDialog / MessageLabel
@onready var error_confirm_button: Button = $ErrorDialog / ConfirmButton


var _pending_char_data: Dictionary = {}
var _modify_target_slot: int = -1
var _modify_target_char_data: Dictionary = {}

var _pending_delete_char_data: Dictionary = {}
var _pending_delete_slot: int = -1
var _is_confirming_delete: bool = false

const CREATE_RACE_NAMES: Array[String] = ["人族男", "人族女", "米古族", "機動靈", "亞斯人"]
const CREATE_HAIR_COLOR_COUNT: int = 8

var _create_race_index: int = 0
var _create_hair_index: int = 0
var _create_hair_color_index: int = 0
var _create_hair_count: int = 1


var _create_preview_player: Node2D = null
var _create_total_pts: int = GameConstants.CHARACTER_INIT_TOTAL_STAT_POINTS
var _create_str: int = 0
var _create_dex: int = 0
var _create_con: int = 0
var _create_wis: int = 0

func _ready() -> void :
	if char_password_dialog: char_password_dialog.visible = false
	if modify_password_dialog: modify_password_dialog.visible = false
	if error_dialog: error_dialog.visible = false
	if create_char_dialog: create_char_dialog.visible = false


	if pwd_confirm_button: pwd_confirm_button.pressed.connect(_on_pwd_confirm_pressed)
	if pwd_cancel_button: pwd_cancel_button.pressed.connect(_on_pwd_cancel_pressed)
	if password_edit: password_edit.text_submitted.connect( func(_text): _on_pwd_confirm_pressed())


	if modify_confirm_button: modify_confirm_button.pressed.connect(_on_modify_confirm_pressed)
	if modify_cancel_button: modify_cancel_button.pressed.connect(_on_modify_cancel_pressed)
	if confirm_new_password_edit: confirm_new_password_edit.text_submitted.connect( func(_text): _on_modify_confirm_pressed())


	_init_create_char_ui()


	if error_confirm_button: error_confirm_button.pressed.connect(_on_error_confirm_pressed)

	if back_button: back_button.pressed.connect(_on_back_button_pressed)

func request_character_list() -> void :
	_render_character_slots()

func _render_character_slots() -> void :
	var server_chars = GlobalTempData.character_list

	for i in range(3):
		var slot_node = slots[i]
		if not slot_node: continue

		var name_label = slot_node.get_node("NameLabel")
		var preview_container = slot_node.get_node("CharPreview")
		var password_btn = slot_node.get_node("PasswordButton") as Button
		var enter_btn = slot_node.get_node("EnterButton") as Button
		var delete_btn = slot_node.get_node("DeleteButton") as Button


		for child in preview_container.get_children():
			preview_container.remove_child(child)
			child.queue_free()


		_clear_all_connections(password_btn.pressed)
		_clear_all_connections(enter_btn.pressed)
		_clear_all_connections(delete_btn.pressed)
		_clear_all_connections(slot_node.gui_input)


		var is_valid_char = false
		var char_data = {}

		if i < server_chars.size():
			char_data = server_chars[i]
			if char_data.has("DBID") and int(char_data.get("DBID", 0)) > 0:
				is_valid_char = true

		if is_valid_char:
			name_label.text = str(char_data.get("NAME", "未命名角色"))

			password_btn.visible = true
			enter_btn.visible = true
			delete_btn.visible = true

			password_btn.pressed.connect(_on_password_setup_clicked.bind(i, char_data))
			enter_btn.pressed.connect(_on_enter_game_clicked.bind(char_data))
			delete_btn.pressed.connect(_on_delete_character_clicked.bind(i, char_data))


			var doll_preview = DOLL_SCENE.instantiate()
			preview_container.add_child(doll_preview)
			doll_preview.position = Vector2(0, 40)


			var race_idx = int(char_data.get("Race", 0))
			var race_name = GameConstants.RACE_ID_MAP.get(race_idx, "Boy")
			var hair_id = int(char_data.get("Hair", 0))
			var hair_name = "Hair%03d" % hair_id
			var hair_color = int(char_data.get("HairColor", 0))
			var body_color = int(char_data.get("EquipColor0", 0))


			var equips = {
				0: int(char_data.get("Bonnet", 0)), 
				1: int(char_data.get("Left_hand", 0)), 
				2: int(char_data.get("Right_hand", 0)), 
				3: int(char_data.get("Upper_armor", 0)), 
				4: int(char_data.get("Lower_armor", 0)), 
				5: int(char_data.get("Weapon", 0)), 
				6: int(char_data.get("Shield", 0)), 
				7: int(char_data.get("Back", 0))
			}



			if race_idx == 3:

				for part in [1, 2, 3, 4]:
					if equips[part] <= 0: equips[part] = 0

			doll_preview.build_human_model(race_name, hair_name, hair_color, body_color, equips)


			doll_preview.update_animation("idle", "1", false, 1.0, 1.0)

			slot_node.gui_input.connect(_on_slot_gui_input.bind(i, char_data))
			await get_tree().process_frame

		else:

			name_label.text = ""
			password_btn.visible = false
			enter_btn.visible = false
			delete_btn.visible = false

			var create_anim_sprite = AnimatedSprite2D.new()
			var frames_res = load("res://ui/create_anim/create_char_frames.tres")
			if frames_res:
				create_anim_sprite.sprite_frames = frames_res
				preview_container.add_child(create_anim_sprite)
				create_anim_sprite.play("default")


			slot_node.gui_input.connect(_on_slot_gui_input.bind(i, {}))

	var main_entry = get_tree().current_scene
	if main_entry and main_entry.has_method("hide_loading_overlay"):
		main_entry.hide_loading_overlay()

func _on_slot_gui_input(event: InputEvent, _slot_index: int, char_data: Dictionary) -> void :
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and not event.pressed:
		if not char_data.is_empty():

			_on_enter_game_clicked(char_data)
		else:

			_show_create_char_dialog()




func _init_create_char_ui() -> void :

	if create_confirm_button: create_confirm_button.pressed.connect(_on_create_confirm_pressed)
	if create_cancel_button: create_cancel_button.pressed.connect(_on_create_cancel_pressed)
	if create_name_edit:
		create_name_edit.max_length = GameConstants.CHARACTER_MAX_NAME_WIDTH
		create_name_edit.text_changed.connect(_on_create_name_text_changed)


	_update_create_option_labels()
	if btn_previous_race: btn_previous_race.pressed.connect(_change_create_option.bind("race", -1))
	if btn_next_race: btn_next_race.pressed.connect(_change_create_option.bind("race", 1))
	if btn_previous_hair: btn_previous_hair.pressed.connect(_change_create_option.bind("hair", -1))
	if btn_next_hair: btn_next_hair.pressed.connect(_change_create_option.bind("hair", 1))
	if btn_previous_hair_color: btn_previous_hair_color.pressed.connect(_change_create_option.bind("hair_color", -1))
	if btn_next_hair_color: btn_next_hair_color.pressed.connect(_change_create_option.bind("hair_color", 1))


	if btn_add_str: btn_add_str.pressed.connect(_change_stat.bind("STR", 1))
	if btn_minus_str: btn_minus_str.pressed.connect(_change_stat.bind("STR", -1))
	if btn_add_dex: btn_add_dex.pressed.connect(_change_stat.bind("DEX", 1))
	if btn_minus_dex: btn_minus_dex.pressed.connect(_change_stat.bind("DEX", -1))
	if btn_add_con: btn_add_con.pressed.connect(_change_stat.bind("CON", 1))
	if btn_minus_con: btn_minus_con.pressed.connect(_change_stat.bind("CON", -1))
	if btn_add_wis: btn_add_wis.pressed.connect(_change_stat.bind("WIS", 1))
	if btn_minus_wis: btn_minus_wis.pressed.connect(_change_stat.bind("WIS", -1))

func _show_create_char_dialog() -> void :
	if create_char_dialog:

		if create_name_edit: create_name_edit.text = ""
		if create_pwd_edit: create_pwd_edit.text = ""
		if create_confirm_pwd_edit: create_confirm_pwd_edit.text = ""


		_create_total_pts = GameConstants.CHARACTER_INIT_TOTAL_STAT_POINTS
		_create_str = 1
		_create_dex = 1
		_create_con = 1
		_create_wis = 1
		if create_total_pt_label: create_total_pt_label.text = str(_create_total_pts)
		if create_str_label: create_str_label.text = str(_create_str)
		if create_dex_label: create_dex_label.text = str(_create_dex)
		if create_con_label: create_con_label.text = str(_create_con)
		if create_wis_label: create_wis_label.text = str(_create_wis)


		_create_race_index = 0
		_create_hair_index = 0
		_create_hair_color_index = 0


		_update_hair_options(0)


		_update_create_preview()

		create_char_dialog.visible = true
		if create_name_edit: create_name_edit.grab_focus()

func _change_create_option(option_type: String, delta: int) -> void :
	match option_type:
		"race":
			_create_race_index = wrapi(_create_race_index + delta, 0, CREATE_RACE_NAMES.size())
			_update_hair_options(_create_race_index)
		"hair":
			if _create_race_index == 3:
				return
			_create_hair_index = wrapi(_create_hair_index + delta, 0, _create_hair_count)
		"hair_color":
			if _create_race_index == 3:
				return
			_create_hair_color_index = wrapi(_create_hair_color_index + delta, 0, CREATE_HAIR_COLOR_COUNT)
		_:
			return

	_update_create_option_labels()
	_update_create_preview()

func _update_create_option_labels() -> void :
	if create_race_label:
		create_race_label.text = CREATE_RACE_NAMES[_create_race_index]
	if create_hair_label:
		create_hair_label.text = "無" if _create_race_index == 3 else "髮型 " + str(_create_hair_index + 1)
	if create_hair_color_label:
		create_hair_color_label.text = "顏色 " + str(_create_hair_color_index + 1)


func _update_hair_options(race_idx: int) -> void :

	if race_idx == 3:
		_create_hair_count = 1
		_create_hair_index = 0
		_create_hair_color_index = 0
		btn_previous_hair.disabled = true
		btn_next_hair.disabled = true
		btn_previous_hair_color.disabled = true
		btn_next_hair_color.disabled = true
		_update_create_option_labels()
		return

	btn_previous_hair.disabled = false
	btn_next_hair.disabled = false
	btn_previous_hair_color.disabled = false
	btn_next_hair_color.disabled = false


	var race_name = GameConstants.RACE_ID_MAP.get(race_idx, "Boy")
	var race_prefix = GameConstants.RACE_PREFIX_MAP.get(race_name, "B")

	var hair_count = _count_hair_in_dir(race_prefix)



	if hair_count == 0 and race_prefix in ["G", "A", "Y"]:
		hair_count = _count_hair_in_dir("B")


	if hair_count == 0:
		hair_count = 1


	if race_idx in [0, 1, 2]:
		hair_count -= 1

	_create_hair_count = max(hair_count, 1)
	_create_hair_index = 0
	_update_create_option_labels()


func _count_hair_in_dir(r_prefix: String) -> int:
	var path = "res://Assets/Player/" + r_prefix + "/Standard/materials/"
	var dir = DirAccess.open(path)
	var found_bases = []

	if dir:
		dir.list_dir_begin()
		var f_name = dir.get_next()
		var target_prefix = r_prefix + "-Hair"

		while f_name != "":

			if not dir.current_is_dir() and f_name.begins_with(target_prefix):
				var base_name = f_name.get_slice(".", 0)
				if not found_bases.has(base_name):
					found_bases.append(base_name)
			f_name = dir.get_next()

	return found_bases.size()


func _update_create_preview() -> void :
	if not create_char_preview: return


	if not is_instance_valid(_create_preview_player):
		for c in create_char_preview.get_children():
			c.queue_free()


		_create_preview_player = DOLL_SCENE.instantiate()
		create_char_preview.add_child(_create_preview_player)

	var preview_size = create_char_preview.size
	var center_offset = Vector2(preview_size.x / 2.0, preview_size.y / 2.0 + 30.0)
	_create_preview_player.position = center_offset


	var race_idx = _create_race_index
	var hair_idx = _create_hair_index
	var color_idx = _create_hair_color_index

	var race_name = GameConstants.RACE_ID_MAP.get(race_idx, "Boy")
	var hair_name = "Hair%03d" % hair_idx


	var equips = {0: 0, 1: 0, 2: 0, 3: 0, 4: 0, 5: 0, 6: 0, 7: 0}


	if race_idx == 3:

		hair_name = "Hair000"
		equips[0] = -1


	_create_preview_player.build_human_model(race_name, hair_name, color_idx, 0, equips)
	_create_preview_player.update_animation("idle", "1", false, 1.0, 1.0)


func _change_stat(stat: String, delta: int) -> void :

	if delta > 0 and _create_total_pts < delta:
		return

	var changed = false
	match stat:
		"STR":
			if _create_str + delta >= 1:
				_create_str += delta
				if create_str_label: create_str_label.text = str(_create_str)
				changed = true
		"DEX":
			if _create_dex + delta >= 1:
				_create_dex += delta
				if create_dex_label: create_dex_label.text = str(_create_dex)
				changed = true
		"CON":
			if _create_con + delta >= 1:
				_create_con += delta
				if create_con_label: create_con_label.text = str(_create_con)
				changed = true
		"WIS":
			if _create_wis + delta >= 1:
				_create_wis += delta
				if create_wis_label: create_wis_label.text = str(_create_wis)
				changed = true


	if changed:
		_create_total_pts -= delta
		if create_total_pt_label: create_total_pt_label.text = str(_create_total_pts)

func _on_create_name_text_changed(new_text: String) -> void :
	var current_width: = 0
	var valid_text: = ""

	for i in range(new_text.length()):
		var c = new_text[i]

		var char_width = 2 if c.unicode_at(0) > 255 else 1

		if current_width + char_width <= GameConstants.CHARACTER_MAX_NAME_WIDTH:
			current_width += char_width
			valid_text += c
		else:
			break


	if valid_text != new_text:
		var old_caret = create_name_edit.caret_column
		create_name_edit.text = valid_text
		create_name_edit.caret_column = min(old_caret, valid_text.length())

func _on_create_confirm_pressed() -> void :

	var char_name = create_name_edit.text.strip_edges()
	var pwd = create_pwd_edit.text.strip_edges()
	var confirm_pwd = create_confirm_pwd_edit.text.strip_edges()
	var race = _create_race_index
	var hair = _create_hair_index
	var hair_color = _create_hair_color_index


	if char_name.is_empty():
		_show_error_popup("角色名稱不可為空！")
		return
	if _create_total_pts > 0:
		_show_error_popup("請先分配剩餘點數！")
		return
	if pwd.length() != GameConstants.CHARACTER_PASSWORD_LENGTH:
		_show_error_popup("請輸入%d位數角色密碼！" % GameConstants.CHARACTER_PASSWORD_LENGTH)
		return
	if pwd != confirm_pwd:
		_show_error_popup("兩次密碼輸入不一致！")
		return


	create_character_requested.emit(char_name, race, hair, hair_color, pwd, _create_str, _create_dex, _create_con, _create_wis)

func _on_create_cancel_pressed() -> void :
	if create_char_dialog:
		create_char_dialog.visible = false
	if is_instance_valid(_create_preview_player):
		_create_preview_player.queue_free()




func _on_password_setup_clicked(slot_index: int, char_data: Dictionary) -> void :
	_modify_target_slot = slot_index
	_modify_target_char_data = char_data

	if old_password_edit: old_password_edit.text = ""
	if new_password_edit: new_password_edit.text = ""
	if confirm_new_password_edit: confirm_new_password_edit.text = ""

	if modify_password_dialog:
		modify_password_dialog.visible = true
		if old_password_edit:
			old_password_edit.grab_focus()

func _on_modify_confirm_pressed() -> void :
	if not old_password_edit or not new_password_edit or not confirm_new_password_edit: return

	var input_old_pwd = old_password_edit.text.strip_edges()
	var input_new_pwd = new_password_edit.text.strip_edges()
	var input_confirm_pwd = confirm_new_password_edit.text.strip_edges()

	if input_old_pwd.is_empty() or input_new_pwd.is_empty() or input_confirm_pwd.is_empty():
		_show_error_popup("所有密碼欄位皆不能為空！")
		return

	var correct_old_pwd = str(_modify_target_char_data.get("CharacterPwd", ""))
	if input_old_pwd != correct_old_pwd:
		_show_error_popup("舊角色密碼輸入錯誤，請重新確認！")
		old_password_edit.text = ""
		old_password_edit.grab_focus()
		return

	if input_new_pwd != input_confirm_pwd:
		_show_error_popup("兩次輸入的新密碼不一致，請重新輸入！")
		new_password_edit.text = ""
		confirm_new_password_edit.text = ""
		new_password_edit.grab_focus()
		return

	if input_new_pwd == input_old_pwd:
		_show_error_popup("新密碼不可與目前使用的舊密碼相同！")
		new_password_edit.text = ""
		confirm_new_password_edit.text = ""
		new_password_edit.grab_focus()
		return

	var char_dbid: int = int(float(_modify_target_char_data.get("DBID", 0)))
	modify_character_password_requested.emit(_modify_target_slot, char_dbid, input_new_pwd)

	if modify_password_dialog:
		modify_password_dialog.visible = false
	_modify_target_slot = -1
	_modify_target_char_data = {}

func _on_modify_cancel_pressed() -> void :
	if modify_password_dialog:
		modify_password_dialog.visible = false
	_modify_target_slot = -1
	_modify_target_char_data = {}

func _on_enter_game_clicked(char_data: Dictionary) -> void :
	_pending_char_data = char_data
	if password_edit:
		password_edit.text = ""
		char_password_dialog.visible = true
		password_edit.grab_focus()

func _on_pwd_confirm_pressed() -> void :
	if not password_edit: return

	var input_pwd = password_edit.text.strip_edges()


	if not _pending_delete_char_data.is_empty():
		var correct_delete_pwd = str(_pending_delete_char_data.get("CharacterPwd", ""))
		if input_pwd == correct_delete_pwd:
			if char_password_dialog: char_password_dialog.visible = false
			var target_dbid = int(float(_pending_delete_char_data.get("DBID", 0)))
			delete_character_requested.emit(_pending_delete_slot, target_dbid, input_pwd)
			_pending_delete_char_data = {}
			_pending_delete_slot = -1
		else:
			password_edit.text = ""
			password_edit.grab_focus()
			_show_error_popup("角色安全密碼錯誤，請重新輸入！")
		return

	var correct_pwd = _pending_char_data.get("CharacterPwd", "")
	if input_pwd == correct_pwd:
		char_password_dialog.visible = false
		GlobalTempData.selected_player_data = _pending_char_data

		var target_map_id: String = GameConstants.DEFAULT_MAP_ID
		if _pending_char_data.has("LastMapID") and _pending_char_data["LastMapID"] != null:
			var map_int_id: int = int(float(_pending_char_data["LastMapID"]))
			if map_int_id > 0:
				target_map_id = "map" + str(map_int_id)

		var target_spawn_position: Vector2 = GameConstants.DEFAULT_SPAWN_POSITION
		if _pending_char_data.has("LastLocX") and _pending_char_data.has("LastLocY"):
			if _pending_char_data["LastLocX"] != null and _pending_char_data["LastLocY"] != null:
				var loc_x = int(float(_pending_char_data["LastLocX"]))
				var loc_y = int(float(_pending_char_data["LastLocY"]))
				target_spawn_position = Vector2(loc_x, loc_y)

		character_selected_and_verified.emit(_pending_char_data, target_map_id, target_spawn_position)
		_pending_char_data = {}
	else:
		password_edit.text = ""
		password_edit.grab_focus()
		_show_error_popup("角色密碼錯誤，請重新輸入！")

func _on_pwd_cancel_pressed() -> void :
	if char_password_dialog:
		char_password_dialog.visible = false
	_pending_char_data = {}
	_pending_delete_char_data = {}
	_pending_delete_slot = -1


func _show_error_popup(err_text: String) -> void :
	if error_message_label and error_dialog:
		error_message_label.text = err_text
		error_dialog.visible = true
		if error_confirm_button:
			error_confirm_button.grab_focus()

func _on_error_confirm_pressed() -> void :
	if error_dialog:
		error_dialog.visible = false

	if _is_confirming_delete:
		_is_confirming_delete = false
		if password_edit and char_password_dialog:
			password_edit.text = ""
			char_password_dialog.visible = true
			password_edit.grab_focus()
		return


	if create_char_dialog and create_char_dialog.visible:
		if create_name_edit and create_name_edit.text.is_empty():
			create_name_edit.grab_focus()
		elif create_pwd_edit and create_pwd_edit.text.is_empty():
			create_pwd_edit.grab_focus()
		elif create_confirm_pwd_edit:
			create_confirm_pwd_edit.grab_focus()
	elif modify_password_dialog and modify_password_dialog.visible:
		if old_password_edit and old_password_edit.text.is_empty():
			old_password_edit.grab_focus()
		elif new_password_edit:
			new_password_edit.grab_focus()

	elif char_password_dialog and char_password_dialog.visible and password_edit:
		password_edit.grab_focus()

func _on_delete_character_clicked(slot_index: int, char_data: Dictionary) -> void :
	_pending_delete_char_data = char_data
	_pending_delete_slot = slot_index
	_is_confirming_delete = true
	_show_error_popup("【警告】\n刪除後的角色資料將永久消失！\n若確定要刪除，請於點擊「確認」後，輸入角色密碼進行刪除。")

func _clear_all_connections(sig: Signal) -> void :
	for conn in sig.get_connections():
		sig.disconnect(conn.callable)


func _on_back_button_pressed() -> void :

	GlobalTempData.login_user = ""
	GlobalTempData.login_pwd = ""
	GlobalTempData.character_list = []
	GlobalTempData.selected_player_data = {}


	get_tree().change_scene_to_file("res://entry_manager.tscn")
