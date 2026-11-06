
extends Control

const RECIPE_PATH: = "res://Assets/Recipe.json"

@onready var btn_close = $Background / BtnClose
@onready var btn_tab_craft = $Background / BtnTabCraft
@onready var btn_tab_dismantle = $Background / BtnTabDismantle

@onready var crafting_panel = $Background / ContentContainer / CraftingPanel
@onready var dismantling_panel = $Background / ContentContainer / DismantlingPanel

func _ready() -> void :
	var recipe_data: = _load_recipe_data()
	if crafting_panel and crafting_panel.has_method("set_recipe_data"):
		crafting_panel.set_recipe_data(recipe_data)
	if dismantling_panel and dismantling_panel.has_method("set_recipe_data"):
		dismantling_panel.set_recipe_data(recipe_data)

	if btn_close:
		btn_close.pressed.connect( func():
			if GameManager.current_hud and GameManager.current_hud.has_method("_close_window"):
				GameManager.current_hud._close_window(self)
			else:
				hide()
		)

	if btn_tab_craft:
		btn_tab_craft.pressed.connect(_show_crafting)
	if btn_tab_dismantle:
		btn_tab_dismantle.pressed.connect(_show_dismantling)


	if NetworkManager.has_signal("inventory_synced"):
		NetworkManager.inventory_synced.connect(_on_inventory_synced)

	_show_crafting()

func _load_recipe_data() -> Array:
	if not FileAccess.file_exists(RECIPE_PATH):
		return []
	var file: = FileAccess.open(RECIPE_PATH, FileAccess.READ)
	if file == null:
		return []
	var parsed = JSON.parse_string(file.get_as_text())
	return parsed if parsed is Array else []

func _show_crafting() -> void :
	if crafting_panel: crafting_panel.visible = true
	if dismantling_panel: dismantling_panel.visible = false


	if btn_tab_craft: btn_tab_craft.modulate = Color(1.0, 1.0, 1.0, 1.0)
	if btn_tab_dismantle: btn_tab_dismantle.modulate = Color(0.5, 0.5, 0.5, 1.0)

	if crafting_panel and crafting_panel.has_method("update_ui"):
		crafting_panel.update_ui()

func _show_dismantling() -> void :
	if crafting_panel: crafting_panel.visible = false
	if dismantling_panel: dismantling_panel.visible = true

	if btn_tab_craft: btn_tab_craft.modulate = Color(0.5, 0.5, 0.5, 1.0)
	if btn_tab_dismantle: btn_tab_dismantle.modulate = Color(1.0, 1.0, 1.0, 1.0)

	if dismantling_panel and dismantling_panel.has_method("update_ui"):
		dismantling_panel.update_ui()

func _on_inventory_synced(_items: Array, _max_size: int) -> void :
	if visible:
		await get_tree().process_frame
		update_ui()


func update_ui() -> void :
	if crafting_panel and crafting_panel.visible and crafting_panel.has_method("update_ui"):
		crafting_panel.update_ui()
	elif dismantling_panel and dismantling_panel.visible and dismantling_panel.has_method("update_ui"):
		dismantling_panel.update_ui()
