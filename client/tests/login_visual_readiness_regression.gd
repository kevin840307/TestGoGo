extends Node

# Godot CLI project mode:
# godot --headless --path client --scene res://tests/login_visual_readiness_regression.tscn
# No account/server data is needed. Login must never wait for video FPS.
const ENTRY_SCENE = preload("res://entry_manager.tscn")
const LOGIN_SCENE = preload("res://LoginScreen.tscn")
var _failures: int = 0


func _check(ok: bool, label: String) -> void:
	if ok:
		print("[LOGIN_VISUAL_READY_PASS] " + label)
	else:
		_failures += 1
		push_error("[LOGIN_VISUAL_READY_FAIL] " + label)


func _ready() -> void:
	call_deferred("_run")


func _run() -> void:
	# Inspect initial scene without running EntryManager's heavy startup
	# indexing, network or HUD prewarm.
	var entry: Control = ENTRY_SCENE.instantiate() as Control
	_check(entry != null, "entry scene can be instantiated with its loading overlay")
	if entry != null:
		var loading_background: ColorRect = entry.get_node_or_null("LoadingOverlay/BlackBackground") as ColorRect
		_check(loading_background != null and loading_background.mouse_filter == Control.MOUSE_FILTER_STOP,
			"opaque startup loading overlay prevents premature input")
		entry.free()

	# Test actual input transitions without decoding the heavyweight OGV.
	var screen: Control = LOGIN_SCENE.instantiate() as Control
	_check(screen != null, "original LoginScreen scene still instantiates")
	if screen == null:
		get_tree().quit(1)
		return
	screen.visible = false
	get_tree().root.add_child(screen)
	await get_tree().process_frame
	screen.call("prepare_form_behind_loading")
	var account: LineEdit = screen.get_node("LoginPanel/AccountInput") as LineEdit
	var password: LineEdit = screen.get_node("LoginPanel/PasswordInput") as LineEdit
	var submit: Button = screen.get_node("LoginPanel/LoginButton") as Button
	var register: Button = screen.get_node("LoginPanel/GoRegisterButton") as Button
	_check(screen.visible and screen.modulate.a > 0.99,
		"real login UI can be prepared behind startup overlay")
	_check(not account.editable and not password.editable,
		"credential fields remain locked while UI is being prepared")
	_check(submit.disabled and register.disabled,
		"buttons remain locked until UI preparation is complete")
	_check(not bool(screen.get("_startup_form_interactive")),
		"startup form is initially locked")

	# No OGV frame, no FPS samples and no window-focus checks are required
	# to finish preparation. This is what previously hung at 0/18 forever.
	screen.call("reveal_form_after_warmup")
	_check(account.editable and password.editable,
		"prepared login fields are editable without waiting for 18 frames")
	_check(not submit.disabled and not register.disabled,
		"login and registration become available immediately after preparation")
	_check(bool(screen.get("_startup_form_interactive")),
		"login request flow becomes interactive before video is decoded")

	# A failed video is an independent cosmetic fallback, not a blocker.
	_check(screen.has_method("start_login_video_fallback_watchdog"),
		"missing first video frame is monitored asynchronously")
	screen.call("use_static_background_after_video_timeout")
	_check(bool(screen.call("is_background_video_frame_ready")),
		"static fallback is treated as an available background")
	_check(account.editable and password.editable and not submit.disabled,
		"switching to static background does not lock login controls")

	screen.queue_free()
	await get_tree().process_frame
	print("[LOGIN_VISUAL_READY_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	get_tree().quit(0 if _failures == 0 else 1)
