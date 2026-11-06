
extends TextureRect

@onready var icon_rect = $IconRect
@onready var lbl_top = $LblTop
@onready var lbl_bottom = $LblBottom
@onready var lbl_price = $LblPrice


func setup(icon_tex: Texture2D, icon_mat: Material, top_text: String, bottom_text: String, top_color: Color, bottom_color: Color) -> void :
	if icon_rect:
		icon_rect.texture = icon_tex
		icon_rect.material = icon_mat

	if lbl_top:
		lbl_top.text = top_text
		lbl_top.add_theme_color_override("font_color", top_color)

	if lbl_bottom:
		lbl_bottom.text = bottom_text
		lbl_bottom.add_theme_color_override("font_color", bottom_color)

func set_price(price: int, custom_color: Color = Color.WHITE) -> void :
	if lbl_price:
		lbl_price.text = "$ " + str(price)
		lbl_price.add_theme_color_override("font_color", custom_color)
		lbl_price.visible = true
