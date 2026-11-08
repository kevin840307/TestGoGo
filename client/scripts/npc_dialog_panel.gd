
extends Control


signal option_selected(target_node: String)

@onready var background: TextureRect = $Background
@onready var question_label: RichTextLabel = $QuestionLabel

@onready var options_container: VBoxContainer = $ScrollContainer / OptionsContainer


var _legacy_caption: Label = null


func show_dialog(question_text: String, options: Array, legacy_prefix: String = "", dialog_layout: String = "Default") -> void :
	self.visible = true
	var help_mode := dialog_layout == "Help" or legacy_prefix.begins_with("h") or legacy_prefix.begins_with("b")
	question_label.bbcode_enabled = true
	question_label.scroll_active = true
	question_label.vertical_alignment = VERTICAL_ALIGNMENT_TOP if help_mode else VERTICAL_ALIGNMENT_CENTER
	question_label.add_theme_font_size_override("normal_font_size", 13 if help_mode else 14)
	question_label.text = question_text
	question_label.scroll_to_line(0)
	_update_legacy_caption(legacy_prefix, dialog_layout)


	for child in options_container.get_children():
		child.queue_free()


	for opt in options:
		var btn = Button.new()
		btn.text = str(opt.get("Text", ""))
		btn.custom_minimum_size = Vector2(0, 35)
		btn.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND


		btn.add_theme_color_override("font_color", Color.BLACK)
		btn.add_theme_color_override("font_hover_color", Color.BLACK)
		btn.add_theme_font_size_override("font_size", 14)

		var style_normal = StyleBoxFlat.new()
		style_normal.bg_color = Color(0, 0, 0, 0)
		style_normal.corner_radius_top_left = 4;style_normal.corner_radius_top_right = 4
		style_normal.corner_radius_bottom_left = 4;style_normal.corner_radius_bottom_right = 4
		btn.add_theme_stylebox_override("normal", style_normal)

		var style_hover = style_normal.duplicate()
		style_hover.bg_color = Color(0.463, 0.81, 0.997, 0.7)
		btn.add_theme_stylebox_override("hover", style_hover)

		var style_pressed = style_normal.duplicate()
		style_pressed.bg_color = Color(0.53, 0.81, 0.92, 0.9)
		btn.add_theme_stylebox_override("pressed", style_pressed)


		var target_node = str(opt.get("TargetNode", ""))


		btn.pressed.connect( func():
			option_selected.emit(target_node)
		)

		options_container.add_child(btn)


func _update_legacy_caption(prefix: String, layout: String) -> void:
	var caption := ""
	if layout == "Help" or prefix.begins_with("h") or prefix.begins_with("b"):
		caption = "說明"
	elif layout == "Narration" or prefix.ends_with("7"):
		caption = "事件"
	elif prefix.begins_with("p"):
		caption = "玩家"
	elif prefix.begins_with("s"):
		caption = "特殊對話"

	if not is_instance_valid(_legacy_caption):
		_legacy_caption = Label.new()
		_legacy_caption.name = "LegacyCaption"
		_legacy_caption.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_legacy_caption.position = Vector2(24, 8)
		_legacy_caption.add_theme_color_override("font_color", Color(0.29, 0.34, 0.46))
		_legacy_caption.add_theme_font_size_override("font_size", 12)
		background.add_child(_legacy_caption)

	_legacy_caption.text = caption
	_legacy_caption.visible = caption != ""
