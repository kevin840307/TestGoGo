extends CanvasLayer
class_name AfkMonitorOverlay

var _entered_msec: int = 0
var _status: Label
var _details: Label
var _restore: Button
var _timer: Timer


func _ready() -> void:
	layer = 250
	var shade := ColorRect.new()
	shade.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	shade.color = Color(0.015, 0.02, 0.03, 0.96)
	add_child(shade)

	var center := CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	shade.add_child(center)

	var panel := PanelContainer.new()
	panel.custom_minimum_size = Vector2(430, 210)
	center.add_child(panel)

	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 12)
	panel.add_child(box)

	var title := Label.new()
	title.text = "低記憶體掛機模式"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_size_override("font_size", 22)
	box.add_child(title)

	_status = Label.new()
	_status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(_status)

	_details = Label.new()
	_details.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_details.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_details)

	_restore = Button.new()
	_restore.text = "返回遊戲"
	_restore.custom_minimum_size = Vector2(180, 42)
	_restore.pressed.connect(_on_restore_pressed)
	box.add_child(_restore)

	_timer = Timer.new()
	_timer.wait_time = 1.0
	_timer.timeout.connect(_refresh)
	add_child(_timer)
	_timer.start()
	_refresh()


func setup(started_msec: int) -> void:
	_entered_msec = started_msec
	_refresh()


func _refresh() -> void:
	if not GameManager or not GameManager.is_afk_mode_active():
		return
	var connected: bool = NetworkManager and NetworkManager.is_connected_to_server
	var player = GameManager.current_player
	var elapsed: int = maxi(0, int((Time.get_ticks_msec() - _entered_msec) / 1000))
	_status.text = "● %s　掛機 %02d:%02d:%02d" % [
		"已連線" if connected else "未連線",
		int(elapsed / 3600), int(elapsed / 60) % 60, elapsed % 60
	]
	if is_instance_valid(player) and "data_model" in player and player.data_model:
		var d = player.data_model
		_details.text = "%s　Lv.%d\nHP %d/%d　MP %d/%d\n地圖 %d　X %d　Y %d" % [
			d.character_name, d.level, d.hp, d.max_hp, d.mp, d.max_mp,
			d.map_id, int(player.global_position.x), int(player.global_position.y)
		]
	else:
		_details.text = "等待角色同步…"
	_restore.disabled = GameManager.is_changing_map or GameManager.is_afk_mode_restoring()


func _on_restore_pressed() -> void:
	if not GameManager or not GameManager.is_afk_mode_active():
		return
	_restore.disabled = true
	_status.text = "正在還原遊戲畫面…"
	var ok: bool = await GameManager.finish_afk_mode()
	if not ok and is_inside_tree():
		_restore.disabled = false
		_refresh()
