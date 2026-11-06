
extends Control


signal option_selected(target_node: String)

@onready var background: TextureRect = $Background
@onready var question_label: RichTextLabel = $QuestionLabel

@onready var options_container: VBoxContainer = $ScrollContainer / OptionsContainer


func show_dialog(question_text: String, options: Array) -> void :
	self.visible = true
	question_label.text = question_text


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
