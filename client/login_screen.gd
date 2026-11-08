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

# Small debug-only window measuring the first seconds of the login UI.
# Never emit account/password text or individual input events to logs.
var _login_reveal_started_usec: int = 0
var _login_startup_gap_reports: int = 0
var _startup_form_interactive: bool = false
var _startup_video_fallback: bool = false
const LOGIN_STARTUP_MONITOR_SECONDS: float = 6.0
const LOGIN_STARTUP_GAP_WARN_SECONDS: float = 0.05
const LOGIN_STARTUP_MAX_GAP_REPORTS: int = 12


var ascii_regex: RegEx = RegEx.new()
var alphanumeric_regex: RegEx = RegEx.new()
var email_regex: RegEx = RegEx.new()

static func needs_non_ascii_rewrite(original: String, sanitized: String) -> bool:
	return original != sanitized


func prepare_form_behind_loading() -> void:
	# Show and layout the *real* login scene while EntryManager's opaque
	# loading overlay is above it. A video cannot be considered warmed if
	# its parent was invisible during the supposed startup prewarm.
	_startup_form_interactive = false
	visible = true
	modulate.a = 1.0
	login_panel.visible = true
	register_panel.visible = false
	user_edit.editable = false
	password_edit.editable = false
	login_button.disabled = true
	go_register_button.disabled = true
	_hide_account_suggestions()


func is_background_video_frame_ready() -> bool:
	if _startup_video_fallback or not is_instance_valid(bg_video) or bg_video.stream == null:
		return true
	# Playing alone is insufficient: the first actual OGV frame may still
	# require expensive decoding/texture allocation on the render thread.
	return bg_video.is_playing() and bg_video.get_stream_position() >= 0.12


func use_static_background_after_video_timeout() -> void:
	# A failed/slow codec must not hold the login form hostage indefinitely.
	# The existing black ColorRect is a safe fallback, not a broken video.
	_startup_video_fallback = true
	stop_background_video()


func reveal_form_after_warmup() -> void:
	# No input was possible while the opaque loading overlay covered the
	# scene. Enable it only after focus, video and rendered frames settle.
	_startup_form_interactive = true
	user_edit.editable = true
	password_edit.editable = true
	login_button.disabled = false
	go_register_button.disabled = false
	login_panel.visible = true


static func can_reveal_login(resources_ready: bool, focused: bool, video_ready: bool, stable_frames: int, minimum_stable_frames: int) -> bool:
	return resources_ready and focused and video_ready and stable_frames >= minimum_stable_frames


func warm_background_video() -> void:
	# Do not decode the looping OGV through the whole 1453-recipe prewarm.
	# The parent starts this shortly BEFORE displaying the login form,
	# leaving the original motion visible when the form is shown.
	if not is_instance_valid(bg_video) or bg_video.stream == null:
		return
	bg_video.loop = true
	if not bg_video.is_playing():
		bg_video.play()
	else:
		bg_video.paused = false


func stop_background_video() -> void:
	if is_instance_valid(bg_video) and bg_video.is_playing():
		bg_video.stop()


func mark_login_screen_revealed() -> void:
	_login_reveal_started_usec = Time.get_ticks_usec()
	_login_startup_gap_reports = 0


func _process(delta: float) -> void:
	if not OS.is_debug_build() or _login_reveal_started_usec <= 0:
		return
	var age_seconds: float = float(Time.get_ticks_usec() - _login_reveal_started_usec) / 1000000.0
	if age_seconds > LOGIN_STARTUP_MONITOR_SECONDS:
		_login_reveal_started_usec = 0
		return
	if delta >= LOGIN_STARTUP_GAP_WARN_SECONDS and _login_startup_gap_reports < LOGIN_STARTUP_MAX_GAP_REPORTS:
		_login_startup_gap_reports += 1
		var video_active: bool = is_instance_valid(bg_video) and bg_video.is_playing() and not bg_video.paused
		var input_focused: bool = (is_instance_valid(user_edit) and user_edit.has_focus()) or (is_instance_valid(password_edit) and password_edit.has_focus())
		print("[LOGIN_STARTUP_FRAME_GAP] frame_ms=%.2f age_s=%.2f video_playing=%s input_focused=%s %s" % [
			delta * 1000.0, age_seconds, str(video_active), str(input_focused),
			GameManager._focus_perf_context() if GameManager else "",
		])


func _ready() -> void :

	ascii_regex.compile("[^ -~]")
	alphanumeric_regex.compile("^[a-zA-Z0-9]+$")
	email_regex.compile("^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}$")

	if loading_overlay:
		loading_overlay.visible = false


	login_panel.visible = true
	register_panel.visible = false


	# EntryManager starts the video at the END of startup prewarm, while
	# the loading overlay still covers this screen. Do not waste CPU/GPU
	# decoding during item/skill/recipe indexing.


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
				var sanitized: String = _clean_non_ascii(new_text)
				# Most account/password keystrokes are already ASCII. Reassigning
				# identical LineEdit.text every key forces needless layout/caret
				# changes and can interfere with IME/focus input responsiveness.
				if needs_non_ascii_rewrite(new_text, sanitized):
					var caret_pos: int = edit.get_caret_column()
					edit.text = sanitized
					edit.set_caret_column(mini(caret_pos, sanitized.length()))
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
	if not _startup_form_interactive or is_submitting:
		return

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
		stop_background_video()
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
	# Loading progress is authoritative in EntryManager. Do not run a fake
	# 0->95 looping tween here; it races every real loading stage and was the
	# reason the bar repeatedly appeared stuck at 95%.
	var main_entry = get_tree().current_scene
	if main_entry and main_entry.has_method("_begin_login_select_loading_session"):
		main_entry._begin_login_select_loading_session()
	elif loading_overlay:
		loading_overlay.visible = true
		if loading_bar:
			loading_bar.value = 0.0

func _stop_loading_animation() -> void :
	if loading_tween and loading_tween.is_valid():
		loading_tween.kill()
	loading_tween = null
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
