
extends Control

const RECIPE_PATH: = "res://Assets/Recipe.json"

@onready var btn_close = $Background / BtnClose
@onready var btn_tab_craft = $Background / BtnTabCraft
@onready var btn_tab_dismantle = $Background / BtnTabDismantle

@onready var crafting_panel = $Background / ContentContainer / CraftingPanel
@onready var dismantling_panel = $Background / ContentContainer / DismantlingPanel

var _initial_prepared: bool = false
var _static_prepared: bool = false
var _cached_recipe_data: Array = []
var _dismantle_prepared: bool = false
var _dismantle_preparing: bool = false

func _ready() -> void :

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

	_show_crafting(false)

func prepare_static_async() -> void :
	if _static_prepared:
		return
	var started_usec: int = Time.get_ticks_usec()
	_cached_recipe_data = _load_recipe_data()
	if crafting_panel and crafting_panel.has_method("prepare_static_tree_async"):
		await crafting_panel.prepare_static_tree_async(_cached_recipe_data)
	_static_prepared = true
	print("[CRAFT_STATIC_PREWARM] recipes=%d total_ms=%.2f" % [
		_cached_recipe_data.size(),
		float(Time.get_ticks_usec() - started_usec) / 1000.0,
	])

func prepare_initial_async() -> void :
	if _initial_prepared:
		return
	var total_started_usec: int = Time.get_ticks_usec()
	if not _static_prepared:
		await prepare_static_async()
	var recipe_data: Array = _cached_recipe_data

	await get_tree().process_frame
	var craft_started_usec: int = Time.get_ticks_usec()
	if crafting_panel and crafting_panel.has_method("refresh_availability_async"):
		await crafting_panel.refresh_availability_async()
	elif crafting_panel and crafting_panel.has_method("set_recipe_data"):
		crafting_panel.set_recipe_data(recipe_data)
	var craft_ms: float = float(Time.get_ticks_usec() - craft_started_usec) / 1000.0

	_initial_prepared = true
	print("[CRAFT_INIT_TIMING] recipes=%d craft_ms=%.2f dismantle_ms=0.00 total_ms=%.2f deferred_dismantle=true" % [
		recipe_data.size(),
		craft_ms,
		float(Time.get_ticks_usec() - total_started_usec) / 1000.0,
	])

func prepare_dismantle_background_async() -> void :
	if _dismantle_prepared:
		return
	if _dismantle_preparing:
		while _dismantle_preparing and is_inside_tree():
			await get_tree().process_frame
		return
	_dismantle_preparing = true
	var started_usec: int = Time.get_ticks_usec()
	if not _static_prepared:
		await prepare_static_async()
	await get_tree().process_frame
	if dismantling_panel and dismantling_panel.has_method("set_recipe_data_initial_async"):
		await dismantling_panel.set_recipe_data_initial_async(_cached_recipe_data)
	elif dismantling_panel and dismantling_panel.has_method("set_recipe_data"):
		dismantling_panel.set_recipe_data(_cached_recipe_data)
	_dismantle_prepared = true
	_dismantle_preparing = false
	print("[DISMANTLE_BACKGROUND_PREWARM] recipes=%d total_ms=%.2f" % [
		_cached_recipe_data.size(),
		float(Time.get_ticks_usec() - started_usec) / 1000.0,
	])

func is_initial_prepared() -> bool:
	return _initial_prepared

func _load_recipe_data() -> Array:
	if not FileAccess.file_exists(RECIPE_PATH):
		return []
	var file: = FileAccess.open(RECIPE_PATH, FileAccess.READ)
	if file == null:
		return []
	var parsed = JSON.parse_string(file.get_as_text())
	return parsed if parsed is Array else []

func _show_crafting(refresh_ui: bool = true) -> void :
	if crafting_panel: crafting_panel.visible = true
	if dismantling_panel: dismantling_panel.visible = false


	if btn_tab_craft: btn_tab_craft.modulate = Color(1.0, 1.0, 1.0, 1.0)
	if btn_tab_dismantle: btn_tab_dismantle.modulate = Color(0.5, 0.5, 0.5, 1.0)

	if refresh_ui and crafting_panel and crafting_panel.has_method("update_ui"):
		crafting_panel.update_ui()

func _show_dismantling() -> void :
	if not _dismantle_prepared:
		await prepare_dismantle_background_async()

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
