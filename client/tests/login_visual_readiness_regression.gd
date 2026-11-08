extends SceneTree

# godot --headless --path client --script res://tests/login_visual_readiness_regression.gd
# Does not start a server, preload recipes, inspect passwords or render a game.
const ENTRY_SCRIPT = preload("res://entry_manager.gd")
const LOGIN_SCENE = preload("res://LoginScreen.tscn")
var _failures: int = 0


func _check(ok: bool, label: String) -> void:
	if ok:
		print("[LOGIN_VISUAL_READY_PASS] " + label)
	else:
		_failures += 1
		push_error("[LOGIN_VISUAL_READY_FAIL] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_check(ENTRY_SCRIPT.is_login_frame_stable(true, true, true, 16000),
		"a drawn 16ms foreground frame counts as stable")
	_check(ENTRY_SCRIPT.is_login_frame_stable(true, true, true, 33000),
		"a drawn 33ms foreground frame can qualify on 30 FPS monitors")
	_check(not ENTRY_SCRIPT.is_login_frame_stable(false, true, true, 10000),
		"10 FPS background bootstrap cannot reveal an editable login form")
	_check(not ENTRY_SCRIPT.is_login_frame_stable(true, false, true, 16000),
		"disabled render loop cannot qualify as visually ready")
	_check(not ENTRY_SCRIPT.is_login_frame_stable(true, true, false, 16000),
		"a process frame with no GPU drawing cannot qualify")
	_check(not ENTRY_SCRIPT.is_login_frame_stable(true, true, true, 149000),
		"an initial 149ms decoder/GPU frame resets stability")
	_check(not ENTRY_SCRIPT.can_reveal_login(false, true, 100),
		"even many good frames cannot reveal while Windows is unfocused")
	_check(not ENTRY_SCRIPT.can_reveal_login(true, false, 100),
		"form cannot show before video decoding is ready")
	_check(not ENTRY_SCRIPT.can_reveal_login(true, true, 17),
		"17 steady foreground frames are insufficient")
	_check(ENTRY_SCRIPT.can_reveal_login(true, true, 18),
		"18 steady foreground frames allow login form reveal")

	# Test actual control state changes without playing the heavyweight OGV.
	var screen: Control = LOGIN_SCENE.instantiate() as Control
	_check(screen != null, "original LoginScreen scene still instantiates")
	if screen == null:
		quit(1)
		return
	screen.visible = false
	root.add_child(screen)
	await process_frame
	screen.call("prepare_form_behind_loading")
	var account: LineEdit = screen.get_node("LoginPanel/AccountInput") as LineEdit
	var password: LineEdit = screen.get_node("LoginPanel/PasswordInput") as LineEdit
	var submit: Button = screen.get_node("LoginPanel/LoginButton") as Button
	var register: Button = screen.get_node("LoginPanel/GoRegisterButton") as Button
	_check(screen.visible and screen.modulate.a > 0.99,
		"actual login scene is shown behind the opaque overlay for first render")
	_check(not account.editable and not password.editable,
		"both credential fields are locked throughout video warmup")
	_check(submit.disabled and register.disabled,
		"submit and register buttons cannot be used before readiness")
	_check(not bool(screen.get("_startup_form_interactive")),
		"the form's internal readiness flag starts disabled")
	screen.call("reveal_form_after_warmup")
	_check(account.editable and password.editable,
		"both credential fields become editable only at final reveal")
	_check(not submit.disabled and not register.disabled,
		"original login/register UI becomes interactive after ready")
	_check(bool(screen.get("_startup_form_interactive")),
		"login request flow is enabled only after readiness")
	screen.call("use_static_background_after_video_timeout")
	_check(bool(screen.call("is_background_video_frame_ready")),
		"failed or absent videos use a safe static background fallback")

	screen.queue_free()
	await process_frame
	print("[LOGIN_VISUAL_READY_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	quit(0 if _failures == 0 else 1)
