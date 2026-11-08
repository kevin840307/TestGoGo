
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
var _initial_preparing: bool = false
var _initial_loading_overlay: ColorRect = null
var _initial_loading_progress: ProgressBar = null
var _initial_loading_label: Label = null

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

func prepare_static_async(progress_callback: Callable = Callable()) -> void :
	if _static_prepared:
		if progress_callback.is_valid():
			progress_callback.call(1.0)
		return
	var started_usec: int = Time.get_ticks_usec()
	if progress_callback.is_valid():
		progress_callback.call(0.0)
	_cached_recipe_data = _load_recipe_data()
	if crafting_panel and crafting_panel.has_method("prepare_static_tree_async"):
		await crafting_panel.prepare_static_tree_async(_cached_recipe_data, progress_callback)
	_static_prepared = true
	if progress_callback.is_valid():
		progress_callback.call(1.0)
	if OS.is_debug_build():
		print("[CRAFT_STATIC_PREWARM] recipes=%d total_ms=%.2f" % [
		_cached_recipe_data.size(),
		float(Time.get_ticks_usec() - started_usec) / 1000.0,
	])

func prepare_initial_async(progress_callback: Callable = Callable()) -> void :
	if _initial_prepared:
		if progress_callback.is_valid():
			progress_callback.call(1.0)
		return
	var total_started_usec: int = Time.get_ticks_usec()
	if not _static_prepared:
		var recipe_progress: Callable = Callable()
		if progress_callback.is_valid():
			recipe_progress = func(ratio: float) -> void:
				progress_callback.call(clampf(ratio, 0.0, 1.0) * 0.9)
		await prepare_static_async(recipe_progress)
	else:
		if progress_callback.is_valid():
			progress_callback.call(0.9)
	var recipe_data: Array = _cached_recipe_data

	await get_tree().process_frame
	var craft_started_usec: int = Time.get_ticks_usec()
	if crafting_panel and crafting_panel.has_method("refresh_availability_async"):
		var availability_progress: Callable = Callable()
		if progress_callback.is_valid():
			availability_progress = func(ratio: float) -> void:
				progress_callback.call(0.9 + clampf(ratio, 0.0, 1.0) * 0.1)
		await crafting_panel.refresh_availability_async(availability_progress)
	elif crafting_panel and crafting_panel.has_method("set_recipe_data"):
		crafting_panel.set_recipe_data(recipe_data)
	var craft_ms: float = float(Time.get_ticks_usec() - craft_started_usec) / 1000.0

	_initial_prepared = true
	if progress_callback.is_valid():
		progress_callback.call(1.0)
	if OS.is_debug_build():
		print("[CRAFT_INIT_TIMING] recipes=%d craft_ms=%.2f dismantle_ms=0.00 total_ms=%.2f deferred_dismantle=true" % [
		recipe_data.size(),
		craft_ms,
		float(Time.get_ticks_usec() - total_started_usec) / 1000.0,
	])

func _ensure_initial_loading_overlay() -> void:
	if is_instance_valid(_initial_loading_overlay):
		return
	# Put progress over CONTENT only. Preserve title bar and close button,
	# and never block SceneTree/game/network processing while recipes build.
	var content: Control = $Background / ContentContainer
	_initial_loading_overlay = ColorRect.new()
	_initial_loading_overlay.name = "CraftFirstOpenLoading"
	_initial_loading_overlay.color = Color(0.045, 0.09, 0.17, 0.96)
	_initial_loading_overlay.mouse_filter = Control.MOUSE_FILTER_STOP
	content.add_child(_initial_loading_overlay)
	_initial_loading_overlay.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var center: CenterContainer = CenterContainer.new()
	_initial_loading_overlay.add_child(center)
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var column: VBoxContainer = VBoxContainer.new()
	column.add_theme_constant_override("separation", 12)
	center.add_child(column)
	_initial_loading_label = Label.new()
	_initial_loading_label.text = "正在載入合成配方… 0%"
	_initial_loading_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_initial_loading_label.add_theme_font_size_override("font_size", 17)
	column.add_child(_initial_loading_label)
	_initial_loading_progress = ProgressBar.new()
	_initial_loading_progress.custom_minimum_size = Vector2(420.0, 22.0)
	_initial_loading_progress.min_value = 0.0
	_initial_loading_progress.max_value = 100.0
	_initial_loading_progress.show_percentage = false
	column.add_child(_initial_loading_progress)


func _set_initial_loading_progress(ratio: float) -> void:
	var percent: float = clampf(ratio, 0.0, 1.0) * 100.0
	if is_instance_valid(_initial_loading_progress):
		_initial_loading_progress.value = percent
	if is_instance_valid(_initial_loading_label):
		if percent < 90.0:
			_initial_loading_label.text = "正在載入合成配方… %d%%" % int(percent)
		else:
			_initial_loading_label.text = "正在更新可合成數量… %d%%" % int(percent)


func prepare_initial_with_progress_async() -> void:
	if _initial_prepared:
		return
	if _initial_preparing:
		while _initial_preparing and is_inside_tree():
			await get_tree().process_frame
		return
	_initial_preparing = true
	_ensure_initial_loading_overlay()
	_initial_loading_overlay.visible = true
	_set_initial_loading_progress(0.0)
	# A 1453-row recipe Tree can take several seconds on cold load.
	# Let the loading panel render FIRST, and use existing recipe frame
	# budgets for responsive progress updates. No pre-login allocation.
	await get_tree().process_frame
	await get_tree().process_frame
	if not is_inside_tree():
		_initial_preparing = false
		return
	var started_usec: int = Time.get_ticks_usec()
	var progress_callback: Callable = func(ratio: float) -> void:
		if is_inside_tree():
			_set_initial_loading_progress(ratio)
	await prepare_initial_async(progress_callback)
	_initial_preparing = false
	if is_instance_valid(_initial_loading_overlay):
		_initial_loading_overlay.visible = false
	if OS.is_debug_build():
		print("[CRAFT_FIRST_OPEN] recipes=%d total_ms=%.2f" % [
			_cached_recipe_data.size(),
			float(Time.get_ticks_usec() - started_usec) / 1000.0,
		])


func is_initial_preparing() -> bool:
	return _initial_preparing


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
	if OS.is_debug_build():
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
	# Avoid a second competing recipe tree build while first-open craft UI
	# is in progress. The player can choose this tab after it completes.
	if _initial_preparing:
		return
	if not _dismantle_prepared:
		var started_usec: int = Time.get_ticks_usec()
		await prepare_dismantle_background_async()
		if OS.is_debug_build():
			print("[DISMANTLE_ON_DEMAND] total_ms=%.2f" % (float(Time.get_ticks_usec() - started_usec) / 1000.0))

	if crafting_panel: crafting_panel.visible = false
	if dismantling_panel: dismantling_panel.visible = true

	if btn_tab_craft: btn_tab_craft.modulate = Color(0.5, 0.5, 0.5, 1.0)
	if btn_tab_dismantle: btn_tab_dismantle.modulate = Color(1.0, 1.0, 1.0, 1.0)

	if dismantling_panel and dismantling_panel.has_method("update_ui"):
		dismantling_panel.update_ui()

func _on_inventory_synced(_items: Array, _max_size: int) -> void :
	if visible and _initial_prepared and not _initial_preparing:
		await get_tree().process_frame
		update_ui()


func update_ui() -> void :
	if not _initial_prepared or _initial_preparing:
		return
	if crafting_panel and crafting_panel.visible and crafting_panel.has_method("update_ui"):
		crafting_panel.update_ui()
	elif dismantling_panel and dismantling_panel.visible and dismantling_panel.has_method("update_ui"):
		dismantling_panel.update_ui()
