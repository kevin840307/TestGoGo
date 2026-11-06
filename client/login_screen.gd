extends Control

const REMEMBERED_ACCOUNTS_PATH: = "user://remembered_accounts.cfg"
const REMEMBERED_ACCOUNTS_SECTION: = "Accounts"
const REMEMBERED_ACCOUNTS_KEY: = "entries"
const REMEMBERED_ACCOUNTS_PASSWORD: = "Paradise.RememberedAccounts.v1"
const ACCOUNT_SUGGESTION_MIN_LENGTH: = 3


@onready var bg_video: VideoStreamPlayer = $IntroVideoWrapper / AspectRatioContainer / BackgroundVideo


@onready var login_panel: Control = $LoginPanel
@onready var user_edit: LineEdit = $LoginPanel / AccountInput
@onready var password_edit: LineEdit = $LoginPanel / PasswordInput
@onready var account_suggestions: ItemList = $LoginPanel / AccountSuggestions
@onready var login_button: Button = $LoginPanel / LoginButton
@onready var go_register_button: Button = $LoginPanel / GoRegisterButton


@onready var register_panel: Control = $RegisterPanel
@onready var reg_user_edit: LineEdit = $RegisterPanel / RegAccountInput
@onready var reg_password_edit: LineEdit = $RegisterPanel / RegPasswordInput
@onready var reg_email_edit: LineEdit = $RegisterPanel / RegEmailInput
@onready var register_button: Button = $RegisterPanel / RegisterButton
@onready var back_to_login_button: Button = $RegisterPanel / BackToLoginButton


@onready var loading_overlay: CanvasLayer = %LoadingOverlay
@onready var loading_bar: ProgressBar = %LoadingOverlay / LoadingBar

var is_submitting: bool = false
var loading_tween: Tween
var remembered_accounts: Array[Dictionary] = []
var submitted_username: String = ""
var submitted_password: String = ""


var ascii_regex: RegEx = RegEx.new()
var alphanumeric_regex: RegEx = RegEx.new()
var email_regex: RegEx = RegEx.new()

func _ready() -> void :

	ascii_regex.compile("[^ -~]")
	alphanumeric_regex.compile("^[a-zA-Z0-9]+$")
	email_regex.compile("^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}$")

	if loading_overlay:
		loading_overlay.visible = false


	login_panel.visible = true
	register_panel.visible = false


	if bg_video and bg_video.stream != null:
		bg_video.loop = true
		bg_video.play()


	_setup_input_fields()
	_load_remembered_accounts()


	login_button.pressed.connect(_on_login_button_pressed)
	go_register_button.pressed.connect(_on_go_register_pressed)

	register_button.pressed.connect(_on_register_button_pressed)
	back_to_login_button.pressed.connect(_on_back_to_login_pressed)

	user_edit.text_submitted.connect(_on_account_text_submitted)
	password_edit.text_submitted.connect( func(_t): _commit_login_flow())
	user_edit.text_changed.connect(_on_account_text_changed)
	user_edit.gui_input.connect(_on_account_input_gui_input)
	account_suggestions.item_selected.connect(_on_account_suggestion_selected)
	account_suggestions.item_activated.connect(_on_account_suggestion_selected)


	NetworkManager.login_finished.connect(_on_login_result_received)
	if NetworkManager.has_signal("register_account_response_received"):
		NetworkManager.register_account_response_received.connect(_on_register_account_response)

func _setup_input_fields() -> void :

	user_edit.max_length = 16
	password_edit.max_length = 16
	reg_user_edit.max_length = 16
	reg_password_edit.max_length = 16


	var non_ime_edits = [user_edit, password_edit, reg_user_edit, reg_password_edit]
	for edit in non_ime_edits:
		if edit:
			edit.focus_entered.connect( func(): DisplayServer.window_set_ime_active(false))
			edit.text_changed.connect( func(new_text: String):
				var caret_pos = edit.get_caret_column()
				edit.text = _clean_non_ascii(new_text)
				edit.set_caret_column(caret_pos)
			)


func _on_go_register_pressed() -> void :
	_hide_account_suggestions()
	login_panel.visible = false
	register_panel.visible = true
	reg_user_edit.grab_focus()

func _on_back_to_login_pressed() -> void :
	register_panel.visible = false
	login_panel.visible = true
	user_edit.grab_focus()


func _on_login_button_pressed() -> void :
	_commit_login_flow()

func _commit_login_flow() -> void :
	if is_submitting: return

	var username = user_edit.text.strip_edges()
	var password = password_edit.text.strip_edges()

	if username == "" or password == "":
		_show_error_popup("帳號或密碼欄位不可為空！")
		return

	is_submitting = true
	user_edit.editable = false
	password_edit.editable = false
	login_button.disabled = true

	GlobalTempData.login_user = username
	GlobalTempData.login_pwd = password
	submitted_username = username
	submitted_password = password

	_hide_account_suggestions()
	_start_loading_animation()
	NetworkManager.send_login_request(username, password)

func _on_login_result_received(success: bool, msg: String) -> void :
	if success:
		_remember_successful_login(submitted_username, submitted_password)
	else:
		_stop_loading_animation()
		user_edit.editable = true
		password_edit.editable = true
		login_button.disabled = false
		_show_error_popup(msg)


func _on_register_button_pressed() -> void :
	var reg_user = reg_user_edit.text.strip_edges()
	var reg_pass = reg_password_edit.text.strip_edges()
	var reg_email = reg_email_edit.text.strip_edges()


	if reg_user == "" or reg_pass == "":
		_show_error_popup("帳號與密碼不能為空！")
		return


	if reg_user.length() < 8 or reg_user.length() > 16:
		_show_error_popup("帳號長度必須介於 8 到 16 碼之間！")
		return
	if reg_pass.length() < 8 or reg_pass.length() > 16:
		_show_error_popup("密碼長度必須介於 8 到 16 碼之間！")
		return


	if alphanumeric_regex.search(reg_user) == null:
		_show_error_popup("帳號只能包含英文或數字！")
		return
	if alphanumeric_regex.search(reg_pass) == null:
		_show_error_popup("密碼只能包含英文或數字！")
		return


	if reg_email == "" or email_regex.search(reg_email) == null:
		_show_error_popup("請輸入有效的電子信箱格式！")
		return

	register_button.disabled = true
	NetworkManager.send_register_account_request(reg_user, reg_pass, reg_email)

func _on_register_account_response(success: bool, msg: String) -> void :
	register_button.disabled = false
	_show_error_popup(msg)

	if success:

		user_edit.text = reg_user_edit.text.strip_edges()
		password_edit.text = reg_password_edit.text.strip_edges()


		reg_user_edit.text = ""
		reg_password_edit.text = ""
		reg_email_edit.text = ""


		_on_back_to_login_pressed()


func _start_loading_animation() -> void :
	is_submitting = true
	if loading_overlay:
		loading_overlay.visible = true
		loading_bar.value = 0
		loading_tween = create_tween()
		loading_tween.set_loops()
		loading_tween.tween_property(loading_bar, "value", 95.0, 2.0).set_trans(Tween.TRANS_LINEAR)

func _stop_loading_animation() -> void :
	if loading_tween and loading_tween.is_valid():
		loading_tween.kill()
	if loading_overlay:
		loading_overlay.visible = false
	is_submitting = false

func _show_error_popup(err_text: String) -> void :
	var main_entry = get_tree().current_scene
	if main_entry and main_entry.has_method("show_error_popup"):
		main_entry.show_error_popup(err_text)

func _clean_non_ascii(input_string: String) -> String:
	return ascii_regex.sub(input_string, "", true)


func _load_remembered_accounts() -> void :
	remembered_accounts.clear()
	var config: = ConfigFile.new()
	if config.load_encrypted_pass(REMEMBERED_ACCOUNTS_PATH, REMEMBERED_ACCOUNTS_PASSWORD) != OK:
		return

	var stored_entries: Variant = config.get_value(
		REMEMBERED_ACCOUNTS_SECTION, 
		REMEMBERED_ACCOUNTS_KEY, 
		[]
	)
	if not stored_entries is Array:
		return

	for entry: Variant in stored_entries:
		if not entry is Dictionary:
			continue
		var account: = str(entry.get("account", ""))
		var password: = str(entry.get("password", ""))
		if not account.is_empty() and not password.is_empty():
			remembered_accounts.append({"account": account, "password": password})

func _remember_successful_login(account: String, password: String) -> void :
	if account.is_empty() or password.is_empty():
		return

	var found_index: = -1
	for index in remembered_accounts.size():
		if str(remembered_accounts[index].get("account", "")).nocasecmp_to(account) == 0:
			found_index = index
			break

	var entry: = {"account": account, "password": password}
	if found_index >= 0:
		remembered_accounts[found_index] = entry
	else:
		remembered_accounts.append(entry)

	var config: = ConfigFile.new()
	config.set_value(REMEMBERED_ACCOUNTS_SECTION, REMEMBERED_ACCOUNTS_KEY, remembered_accounts)
	var save_error: = config.save_encrypted_pass(REMEMBERED_ACCOUNTS_PATH, REMEMBERED_ACCOUNTS_PASSWORD)
	if save_error != OK:
		push_warning("無法儲存登入帳號記錄，錯誤碼：%d" % save_error)

func _on_account_text_changed(account_prefix: String) -> void :
	account_suggestions.clear()
	if account_prefix.length() < ACCOUNT_SUGGESTION_MIN_LENGTH:
		_hide_account_suggestions()
		return

	var matches: Array[Dictionary] = []
	var normalized_prefix: = account_prefix.to_lower()
	for entry in remembered_accounts:
		var account: = str(entry.get("account", ""))
		if account.to_lower().begins_with(normalized_prefix):
			matches.append(entry)

	matches.sort_custom( func(left: Dictionary, right: Dictionary) -> bool:
		return str(left.get("account", "")).naturalnocasecmp_to(str(right.get("account", ""))) < 0
	)

	for entry in matches:
		var item_index: = account_suggestions.add_item(str(entry.get("account", "")))
		account_suggestions.set_item_metadata(item_index, entry)

	account_suggestions.visible = account_suggestions.item_count > 0
	if account_suggestions.visible:
		account_suggestions.select(0)

func _on_account_input_gui_input(event: InputEvent) -> void :
	if not account_suggestions.visible or not event is InputEventKey:
		return

	var key_event: = event as InputEventKey
	if not key_event.pressed or key_event.echo:
		return

	var selected_items: = account_suggestions.get_selected_items()
	var selected_index: = selected_items[0] if not selected_items.is_empty() else 0
	match key_event.keycode:
		KEY_DOWN:
			account_suggestions.select(mini(selected_index + 1, account_suggestions.item_count - 1))
			account_suggestions.ensure_current_is_visible()
			user_edit.accept_event()
		KEY_UP:
			account_suggestions.select(maxi(selected_index - 1, 0))
			account_suggestions.ensure_current_is_visible()
			user_edit.accept_event()
		KEY_ESCAPE:
			_hide_account_suggestions()
			user_edit.accept_event()

func _on_account_text_submitted(_text: String) -> void :
	if account_suggestions.visible and account_suggestions.item_count > 0:
		var selected_items: = account_suggestions.get_selected_items()
		var selected_index: = selected_items[0] if not selected_items.is_empty() else 0
		_on_account_suggestion_selected(selected_index)
		return

	password_edit.grab_focus()

func _on_account_suggestion_selected(index: int) -> void :
	if index < 0 or index >= account_suggestions.item_count:
		return
	var entry: Variant = account_suggestions.get_item_metadata(index)
	if not entry is Dictionary:
		return

	user_edit.text = str(entry.get("account", ""))
	password_edit.text = str(entry.get("password", ""))
	_hide_account_suggestions()
	password_edit.grab_focus()

func _hide_account_suggestions() -> void :
	account_suggestions.visible = false
