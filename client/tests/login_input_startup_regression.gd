extends SceneTree

# godot --headless --path client --script res://tests/login_input_startup_regression.gd
# Runs without a login server, never reads or records real credentials.
const LOGIN_SCRIPT = preload("res://login_screen.gd")
const LOGIN_SCENE = preload("res://LoginScreen.tscn")
var _failures: int = 0


func _check(ok: bool, label: String) -> void:
	if ok:
		print("[LOGIN_INPUT_PASS] " + label)
	else:
		_failures += 1
		push_error("[LOGIN_INPUT_FAIL] " + label)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_check(not LOGIN_SCRIPT.needs_non_ascii_rewrite("test123", "test123"),
		"normal ASCII input must not rewrite the LineEdit or caret")
	_check(not LOGIN_SCRIPT.needs_non_ascii_rewrite("Pass123!", "Pass123!"),
		"normal ASCII password input must not rewrite the LineEdit")
	_check(LOGIN_SCRIPT.needs_non_ascii_rewrite("ABC中", "ABC"),
		"non-ASCII input still triggers original validation path")

	var screen: Control = LOGIN_SCENE.instantiate() as Control
	_check(screen != null, "login screen can be instantiated")
	if screen == null:
		quit(1)
		return
	# Parent EntryManager starts hidden during prewarm; hidden login video
	# must not begin decoding until explicit warm_background_video().
	screen.visible = false
	root.add_child(screen)
	await process_frame
	var video: VideoStreamPlayer = screen.get_node_or_null("IntroVideoWrapper/AspectRatioContainer/BackgroundVideo")
	_check(video != null, "original background video node remains present")
	if video != null:
		_check(not video.is_playing(), "hidden login video is not decoding during recipe prewarm")
		if video.stream != null:
			screen.call("warm_background_video")
			_check(video.is_playing(), "video can start behind the final loading overlay")
			screen.call("stop_background_video")
			_check(not video.is_playing(), "video decoding stops after successful login")
	var account: LineEdit = screen.get_node("LoginPanel/AccountInput") as LineEdit
	account.text = "ABC123"
	account.set_caret_column(3)
	account.text_changed.emit("ABC123")
	_check(account.text == "ABC123" and account.get_caret_column() == 3,
		"normal typing keeps existing text and cursor position")

	screen.free()
	print("[LOGIN_INPUT_REGRESSION] %s" % (
		"ALL PASS" if _failures == 0 else "FAILURES=%d" % _failures
	))
	quit(0 if _failures == 0 else 1)
