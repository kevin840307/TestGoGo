
extends TextureRect

signal skill_clicked(skill_id: int)

@onready var icon_rect = $IconRect
@onready var lbl_name = $LblName
@onready var lbl_level = $LblLevel

var _skill_id: int = 0

func _ready() -> void :
	if not gui_input.is_connected(_on_gui_input):
		gui_input.connect(_on_gui_input)


func setup(icon_tex: Texture2D, skill_name: String, skill_level: int, text_color: Color = Color.WHITE, skill_id: int = 0) -> void :
	_skill_id = skill_id
	if icon_rect and icon_tex:
		icon_rect.texture = icon_tex

	if lbl_name:
		lbl_name.text = skill_name
		lbl_name.add_theme_color_override("font_color", text_color)

	if lbl_level:
		lbl_level.text = "Lv." + str(skill_level)
		lbl_level.add_theme_color_override("font_color", text_color)

func _on_gui_input(event: InputEvent) -> void :
	if event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_LEFT:

			if DropItemManager and DropItemManager.is_dragging:
				DropItemManager.cancel_drag()
				return
			skill_clicked.emit(_skill_id)

		elif event.button_index == MOUSE_BUTTON_RIGHT:

			if DropItemManager:
				if DropItemManager.is_dragging:
					DropItemManager.cancel_drag()
				else:
					DropItemManager.start_drag({"skill_id": _skill_id}, 100, icon_rect.texture, null)
