extends SceneTree

# godot --headless --path client --script res://tests/afk_cursor_audio_regression.gd
# Verify custom cursor and silence transitions while preserving original state.
var failures: int = 0


func check(condition: bool, detail: String) -> void:
	if condition:
		print("[AFK_CURSOR_AUDIO_PASS] " + detail)
	else:
		failures += 1
		push_error("[AFK_CURSOR_AUDIO_FAIL] " + detail)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var original_mouse_mode: int = Input.mouse_mode
	var original_cursor_enabled: bool = CursorManager.is_afk_custom_cursor_enabled()
	var original_audio_silent: bool = AudioManager.is_afk_silent()

	CursorManager.set_afk_custom_cursor_enabled(true)
	check(CursorManager.is_afk_custom_cursor_enabled(), "AFK custom cursor mode is enabled")
	check(CursorManager.cursor_sprite.visible, "game cursor sprite remains visible during AFK")
	check(CursorManager.cursor_sprite.animation == "normal", "AFK cursor uses the ordinary UI animation")
	if DisplayServer.get_name() != "headless":
		check(Input.mouse_mode == Input.MOUSE_MODE_HIDDEN, "OS pointer stays hidden behind the game cursor")
	CursorManager.set_afk_custom_cursor_enabled(false)
	check(not CursorManager.is_afk_custom_cursor_enabled(), "normal game cursor mode can be restored")
	check(CursorManager.cursor_sprite.visible, "game cursor sprite remains available after AFK")

	AudioManager.set_afk_silent(true)
	check(AudioManager.is_afk_silent(), "AFK audio suppression is active")
	check(not AudioManager.play_se_by_name("SE0044"), "SE is never loaded or played during AFK")
	AudioManager.play_map_bgm(15)
	check(not AudioManager.bgm_player.playing, "map changes cannot restart BGM during AFK")
	AudioManager.set_afk_silent(false)
	check(not AudioManager.is_afk_silent(), "AFK audio suppression can be disabled")

	if original_cursor_enabled:
		CursorManager.set_afk_custom_cursor_enabled(true)
	if original_audio_silent:
		AudioManager.set_afk_silent(true)
	Input.mouse_mode = original_mouse_mode
	print("[AFK_CURSOR_AUDIO_REGRESSION] %s" % ("ALL PASS" if failures == 0 else "FAILURES=%d" % failures))
	quit(0 if failures == 0 else 1)
