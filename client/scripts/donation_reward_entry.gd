extends Panel

signal claim_requested(reward_data: Dictionary)

@onready var icon_rect: TextureRect = $IconRect
@onready var name_label: Label = $NameLabel
@onready var threshold_label: Label = $ThresholdLabel
@onready var status_label: Label = $StatusLabel
@onready var claim_button: Button = $ClaimButton

const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}

var _reward_data: Dictionary = {}

func _ready() -> void :
	claim_button.pressed.connect(_on_claim_button_pressed)
	icon_rect.mouse_filter = Control.MOUSE_FILTER_PASS
	icon_rect.mouse_entered.connect(_show_tooltip)
	icon_rect.mouse_exited.connect(_hide_tooltip)

func setup(reward_data: Dictionary) -> void :
	_reward_data = reward_data
	var item_name: = str(reward_data.get("Name", "未知物品"))
	var count: = int(reward_data.get("Count", 1))
	var required_donation: = int(reward_data.get("RequiredDonation", 0))
	name_label.text = item_name + (" x %d" % count if count > 1 else "")
	threshold_label.text = "累計贊助 %d 點" % required_donation
	_update_claim_status(str(reward_data.get("ClaimStatus", "Unclaimed")))
	_update_icon()

func _update_claim_status(status: String) -> void :
	match status:
		"Claimed":
			status_label.text = ""
			status_label.add_theme_color_override("font_color", Color(0.65, 0.65, 0.65))
			claim_button.text = "已領取"
			claim_button.disabled = true
		"Claiming":
			status_label.text = "處理異常，請聯絡客服"
			status_label.add_theme_color_override("font_color", Color(1.0, 0.45, 0.35))
			claim_button.text = "核對中"
			claim_button.disabled = true
		_:
			status_label.text = "可領取"
			status_label.add_theme_color_override("font_color", Color(0.35, 1.0, 0.55))
			claim_button.text = "領取"
			claim_button.disabled = false

func _update_icon() -> void :
	var item_index: = int(_reward_data.get("ItemIndex", 0))
	var item_category: = int(_reward_data.get("ItemCategory", -1))
	icon_rect.material = null

	if item_category == 2:
		_update_pet_icon(item_index)
		return

	if not ItemLoader:
		return
	var raw_photo_id: = int(ItemLoader.get_item_property(
		item_index, 
		"photo_id", 
		ItemLoader.get_item_property(item_index, "icon_id", 1)
	))
	icon_rect.texture = ItemLoader.get_photo(max(0, raw_photo_id - 1))
	var color_index: = int(ItemLoader.get_item_property(item_index, "color_index", 0))
	var effect_type: = int(ItemLoader.get_item_property(item_index, "effect_type", 0))
	_apply_icon_material(color_index, effect_type)

func _update_pet_icon(pet_id: int) -> void :
	icon_rect.texture = load("res://Assets/Items/Photos/Photo0375.png")
	if not PetLoader:
		return
	var config = PetLoader.get_pet_config(pet_id)
	if not config or not config.has("evolutions"):
		return
	var evolutions: Dictionary = config.get("evolutions", {})
	if evolutions.is_empty():
		return
	var evol_key = "0" if evolutions.has("0") else evolutions.keys()[0]
	var evol_data: Dictionary = evolutions.get(evol_key, {})
	var outlook: = str(evol_data.get("outlook", ""))
	if not outlook.is_empty():
		var photo_path: = "res://Assets/Pets/Photo/" + outlook + ".png"
		if ResourceLoader.exists(photo_path):
			icon_rect.texture = load(photo_path)
	_apply_icon_material(int(evol_data.get("color", 0)), 0)

func _apply_icon_material(color_index: int, effect_type: int) -> void :
	if color_index <= 0 and effect_type <= 0:
		icon_rect.material = null
		return
	var material: = ShaderMaterial.new()
	material.shader = PALETTE_SHADER
	material.set_shader_parameter("effect_type", effect_type)
	material.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
	icon_rect.material = material

func _on_claim_button_pressed() -> void :
	if claim_button.disabled:
		return
	claim_requested.emit(_reward_data)

func _show_tooltip() -> void :
	var hud = GameManager.current_hud if GameManager else null
	if not is_instance_valid(hud) or not hud.get("shared_tooltip"):
		return
	var item_index: = int(_reward_data.get("ItemIndex", 0))
	var item_category: = int(_reward_data.get("ItemCategory", -1))
	var tab_type: = 0
	if item_category == 1:
		tab_type = 1
	elif item_category == 2:
		tab_type = 2
	elif item_category == 3 or item_category == 4:
		tab_type = 3
	var tooltip_data: = _reward_data.duplicate()
	if item_category == 4:
		tooltip_data["equip_idx"] = item_index
	hud.shared_tooltip.show_item(item_index, tab_type, tooltip_data)

func _hide_tooltip() -> void :
	var hud = GameManager.current_hud if GameManager else null
	if is_instance_valid(hud) and hud.get("shared_tooltip"):
		hud.shared_tooltip.hide_tooltip()
