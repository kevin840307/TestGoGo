
extends Panel

signal buy_requested(item_data: Dictionary, quantity: int)

@onready var icon_rect: TextureRect = $IconRect
@onready var tag_label: Label = $TagLabel
@onready var name_label: Label = $NameLabel
@onready var price_label: RichTextLabel = $PriceLabel
@onready var qty_spinbox: SpinBox = $QtySpinBox
@onready var buy_button: Button = $BuyButton

const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}

var _current_item_data: Dictionary = {}

func _ready() -> void :
	if buy_button:
		buy_button.pressed.connect(_on_buy_button_pressed)
		buy_button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND


	if icon_rect:
		icon_rect.mouse_filter = Control.MOUSE_FILTER_PASS
		icon_rect.mouse_entered.connect(_on_icon_mouse_entered)
		icon_rect.mouse_exited.connect(_on_icon_mouse_exited)

func setup(data: Dictionary) -> void :
	_current_item_data = data


	var count = int(data.get("count", data.get("Count", 1)))
	var base_name = str(data.get("name", data.get("Name", "未知商品")))
	if name_label:
		name_label.text = base_name + (" x %d" % count if count > 1 else "")


	var tag_text = str(data.get("tag", data.get("Tag", "")))
	if tag_label:
		tag_label.text = tag_text
		if tag_text == "NEW":
			tag_label.add_theme_color_override("font_color", Color(1.0, 0.941, 0.176, 1.0))
		elif tag_text == "HOT":
			tag_label.add_theme_color_override("font_color", Color(1.0, 0.196, 0.196, 1.0))
		else:

			tag_label.remove_theme_color_override("font_color")


	var price = int(data.get("price", data.get("Price", 0)))
	var original_price = int(data.get("original_price", data.get("OriginalPrice", price)))

	if price_label:
		if original_price > price:
			price_label.text = "[center][color=#888888][s]原價 %d 點[/s][/color]\n[color=#FF4444]特價 %d 點[/color][/center]" % [original_price, price]
		else:
			price_label.text = "[center][color=#FFF02D]售價 %d 點[/color][/center]" % price


	var category = str(data.get("category", data.get("Category", "")))
	var item_index = int(data.get("item_index", data.get("ItemIndex", 0)))

	if category == "寵物":
		if PetLoader and icon_rect:
			var pet_id = item_index
			var evol_id = int(data.get("evol_id", data.get("EvolID", 0)))
			var config = PetLoader.get_pet_config(pet_id)
			var has_custom_icon = false

			if config and config.has("evolutions"):
				var evol_key = str(evol_id)
				if config.evolutions.has(evol_key):
					var evol_info = config.evolutions[evol_key]
					var outlook = str(evol_info.get("outlook", ""))
					if outlook != "":
						var photo_path = "res://Assets/Pets/Photo/" + outlook + ".png"
						if ResourceLoader.exists(photo_path):
							icon_rect.texture = load(photo_path)
							has_custom_icon = true

					var color_index = int(evol_info.get("color", 0))
					if color_index > 0:
						var mat = ShaderMaterial.new()
						mat.shader = PALETTE_SHADER
						mat.set_shader_parameter("effect_type", 0)
						mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
						icon_rect.material = mat
					else:
						icon_rect.material = null


			if not has_custom_icon:
				icon_rect.texture = load("res://Assets/Items/Photos/Photo0375.png")
	else:

		if ItemLoader and icon_rect:
			var raw_id = int(ItemLoader.get_item_property(item_index, "photo_id", ItemLoader.get_item_property(item_index, "icon_id", 1)))
			var target_img_id = raw_id - 1
			icon_rect.texture = ItemLoader.get_photo(target_img_id)

			var color_index = int(data.get("color", data.get("Color", ItemLoader.get_item_property(item_index, "color_index", 0))))
			var effect_type = int(data.get("EffectType", data.get("effect_type", ItemLoader.get_item_property(item_index, "effect_type", 0))))
			if color_index > 0 or effect_type > 0:
				var mat = ShaderMaterial.new()
				mat.shader = PALETTE_SHADER
				mat.set_shader_parameter("effect_type", effect_type)
				mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
				icon_rect.material = mat
			else:
				icon_rect.material = null

	if qty_spinbox:
		qty_spinbox.value = 1

func _on_buy_button_pressed() -> void :
	if qty_spinbox:
		var qty = int(qty_spinbox.value)
		if qty > 0:
			buy_requested.emit(_current_item_data, qty)



func _on_icon_mouse_entered() -> void :
	if GameManager and GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):
		var item_index = int(_current_item_data.get("item_index", _current_item_data.get("ItemIndex", 0)))
		var category = str(_current_item_data.get("category", _current_item_data.get("Category", "")))


		var tab_type = 0
		if category == "護符":
			tab_type = 1
		elif category == "寵物":
			tab_type = 2


		GameManager.current_hud.shared_tooltip.show_item(item_index, tab_type, _current_item_data)

func _on_icon_mouse_exited() -> void :
	if GameManager and GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):
		GameManager.current_hud.shared_tooltip.hide_tooltip()
