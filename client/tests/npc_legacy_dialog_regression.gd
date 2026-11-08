extends SceneTree

# Offline coverage for legacy Lua prefix and BBCode presentation.
# godot --headless --path client --script res://tests/npc_legacy_dialog_regression.gd

var failures: int = 0


func _check(ok: bool, description: String) -> void:
	if ok:
		print("[NPC_DIALOG_PASS] ", description)
	else:
		failures += 1
		push_error("[NPC_DIALOG_FAIL] " + description)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	await process_frame
	var script = load("res://scripts/npc_dialog_panel.gd") as GDScript
	_check(script != null, "NPC panel script loads")
	if script == null:
		quit(1)
		return

	var panel := Control.new()
	panel.set_script(script)
	var background := TextureRect.new()
	background.name = "Background"
	panel.add_child(background)
	var question := RichTextLabel.new()
	question.name = "QuestionLabel"
	panel.add_child(question)
	var scroll := ScrollContainer.new()
	scroll.name = "ScrollContainer"
	panel.add_child(scroll)
	var options := VBoxContainer.new()
	options.name = "OptionsContainer"
	scroll.add_child(options)
	get_root().add_child(panel)
	await process_frame

	var chosen: Array[String] = []
	panel.option_selected.connect(func(node: String) -> void: chosen.append(node))
	panel.show_dialog(
		"確定要去[color=#002DC6]尼尼亞皇城[/color]嗎？",
		[{"Text": "確定", "TargetNode": "Confirm#0"}, {"Text": "取消", "TargetNode": "Confirm#1"}],
		"p", "Default"
	)
	_check(question.bbcode_enabled and question.text.contains("[color=#002DC6]"),
		"Lua RGB markup is retained on NPC dialog")
	_check(question.get_parsed_text().contains("尼尼亞皇城"),
		"BBCode is interpreted as readable dialogue")
	_check(options.get_child_count() == 2,
		"choice options are still visible")
	if options.get_child_count() == 2:
		(options.get_child(1) as Button).pressed.emit()
	_check(chosen.size() == 1 and chosen[0] == "Confirm#1",
		"existing choice callback is not changed")

	panel.show_dialog("第一段\n\n第二段", [{"Text": "完成", "TargetNode": "Help#0"}], "h", "Help")
	_check(question.scroll_active and question.text.contains("\n\n"),
		"long help text retains paragraph breaks and scrolling")
	_check(question.vertical_alignment == VERTICAL_ALIGNMENT_TOP,
		"help mode top-aligns long documentation")
	panel.show_dialog("普通對話", [{"Text": "好", "TargetNode": "Next#0"}], "n", "Default")
	_check(question.vertical_alignment == VERTICAL_ALIGNMENT_CENTER,
		"normal dialogue resets help-page alignment")

	var bubble := NinePatchRect.new()
	var label := Label.new()
	label.position = Vector2(5, 5)
	label.size = Vector2(150, 35)
	bubble.add_child(label)
	get_root().add_child(bubble)
	var bubble_formatter = load("res://scripts/legacy_dialog_bubble.gd") as GDScript
	_check(bubble_formatter != null, "rich bubble renderer loads")
	if bubble_formatter != null:
		bubble_formatter.display(bubble, label, "去找[color=#002DC6]大牧師[/color]", true)
		var rich = bubble.get_node_or_null("LegacyDialogRichText") as RichTextLabel
		_check(rich != null and rich.visible and not label.visible,
			"legacy Say() color displays in rich bubble")
		bubble_formatter.display(bubble, label, "一般聊天訊息", false)
		_check(label.visible and label.text == "一般聊天訊息" and not rich.visible,
			"ordinary chat keeps its original Label")
	bubble.queue_free()
	panel.queue_free()
	print("[NPC_DIALOG] ", "ALL PASS" if failures == 0 else "%d FAILURES" % failures)
	quit(0 if failures == 0 else 1)
