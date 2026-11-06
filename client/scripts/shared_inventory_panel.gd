
extends Control
class_name SharedInventoryPanel


signal item_clicked(event: InputEvent, item_data: Dictionary, tab_type: int, tex: Texture2D, mat: Material)
signal equip_slot_clicked(slot_index: int)
signal tab_changed(new_tab: int)
signal closed()


enum DisplayMode{FULL_INVENTORY, SIMPLE_LIST}
enum TabType{GENERAL = 0, EQUIP = 1, PET = 2, MECHA = 3}

const INVENTORY_ROW_SCENE = preload("res://inventory_item_row.tscn")
const PLAYER_SCENE = preload("res://player.tscn")
const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}
var _palette_mat_cache: Dictionary = {}


var tex_tab_general_on: Texture2D = preload("res://ui/B_ITEM_ITEM2.png")
var tex_tab_general_off: Texture2D = preload("res://ui/B_ITEM_ITEM1.png")
var tex_tab_equip_on: Texture2D = preload("res://ui/B_ITEM_EQUIP2.png")
var tex_tab_equip_off: Texture2D = preload("res://ui/B_ITEM_EQUIP1.png")
var tex_tab_pet_on: Texture2D = preload("res://ui/B_ITEM_PET2.png")
var tex_tab_pet_off: Texture2D = preload("res://ui/B_ITEM_PET1.png")
var tex_tab_mecha_on: Texture2D = preload("res://ui/B_ITEM_RIDE2.png")
var tex_tab_mecha_off: Texture2D = preload("res://ui/B_ITEM_RIDE1.png")


var tex_filter_on: Texture2D = preload("res://ui/B_EQUIP_CHANGE2.png")
var tex_filter_off: Texture2D = preload("res://ui/B_EQUIP_CHANGE1.png")

var _tab_capacities: Dictionary = {}
var _current_equip_filter_part: int = -1
var _cached_equip_items: Array = []
var _target_player: CharacterBody2D = null
var show_sell_price: bool = false
var show_repair_price: bool = false


@onready var btn_tab_general: TextureButton = $BtnTabGeneral
@onready var btn_tab_equip: TextureButton = $BtnTabEquip
@onready var btn_tab_pet: TextureButton = $BtnTabPet
@onready var btn_tab_mecha: TextureButton = $BtnTabMecha
@onready var btn_close: TextureButton = $BtnClose


@onready var panel_general: Control = $TabPanels / PanelGeneral
@onready var list_general: VBoxContainer = $TabPanels / PanelGeneral / ScrollContainer / ListGeneral


@onready var panel_equip_simple: Control = $TabPanels / PanelEquipSimple
@onready var list_equip_simple: VBoxContainer = $TabPanels / PanelEquipSimple / ScrollContainer / ListEquip

@onready var panel_equip_detailed: Control = $TabPanels / PanelEquipDetailed
@onready var list_equip_detailed: VBoxContainer = $TabPanels / PanelEquipDetailed / ScrollContainer / ListEquip


@onready var btn_equip_bonnet: TextureButton = $TabPanels / PanelEquipDetailed / BtnBonnet
@onready var btn_equip_left: TextureButton = $TabPanels / PanelEquipDetailed / BtnLeft
@onready var btn_equip_right: TextureButton = $TabPanels / PanelEquipDetailed / BtnRight
@onready var btn_equip_body: TextureButton = $TabPanels / PanelEquipDetailed / BtnBody
@onready var btn_equip_leg: TextureButton = $TabPanels / PanelEquipDetailed / BtnLeg
@onready var btn_equip_weapon: TextureButton = $TabPanels / PanelEquipDetailed / BtnWeapon
@onready var btn_equip_shield: TextureButton = $TabPanels / PanelEquipDetailed / BtnShield
@onready var btn_equip_back: TextureButton = $TabPanels / PanelEquipDetailed / BtnBack
@onready var btn_equip_amulet: TextureButton = $TabPanels / PanelEquipDetailed / BtnAmulet


@onready var btn_equip_filter_bonnet: TextureButton = $TabPanels / PanelEquipDetailed / BtnFilterBonnet
@onready var btn_equip_filter_left: TextureButton = $TabPanels / PanelEquipDetailed / BtnFilterLeft
@onready var btn_equip_filter_right: TextureButton = $TabPanels / PanelEquipDetailed / BtnFilterRight
@onready var btn_equip_filter_body: TextureButton = $TabPanels / PanelEquipDetailed / BtnFilterBody
@onready var btn_equip_filter_leg: TextureButton = $TabPanels / PanelEquipDetailed / BtnFilterLeg
@onready var btn_equip_filter_weapon: TextureButton = $TabPanels / PanelEquipDetailed / BtnFilterWeapon
@onready var btn_equip_filter_shield: TextureButton = $TabPanels / PanelEquipDetailed / BtnFilterShield
@onready var btn_equip_filter_back: TextureButton = $TabPanels / PanelEquipDetailed / BtnFilterBack
@onready var btn_equip_filter_amulet: TextureButton = $TabPanels / PanelEquipDetailed / BtnFilterAmulet


@onready var panel_mecha_simple: Control = $TabPanels / PanelMechaSimple
@onready var list_mecha_simple: VBoxContainer = $TabPanels / PanelMechaSimple / ScrollContainer / ListHorseEquip

@onready var panel_mecha_detailed: Control = $TabPanels / PanelMechaDetailed
@onready var scroll_horse_in_use: ScrollContainer = $TabPanels / PanelMechaDetailed / ScrollHorseInUse
@onready var list_horse_in_use: VBoxContainer = $TabPanels / PanelMechaDetailed / ScrollHorseInUse / ListHorseInUse
@onready var scroll_horse: ScrollContainer = $TabPanels / PanelMechaDetailed / ScrollHorse
@onready var list_horse: VBoxContainer = $TabPanels / PanelMechaDetailed / ScrollHorse / ListHorse
@onready var scroll_battery: ScrollContainer = $TabPanels / PanelMechaDetailed / ScrollBattery
@onready var list_battery: VBoxContainer = $TabPanels / PanelMechaDetailed / ScrollBattery / ListBattery
@onready var list_horse_equip_detailed: VBoxContainer = $TabPanels / PanelMechaDetailed / ScrollHorseEquip / ListHorseEquip


@onready var panel_pet: Control = $TabPanels / PanelPet if has_node("TabPanels/PanelPet") else null
@onready var lbl_capacity: Label = $LblCapacity

var current_mode: DisplayMode = DisplayMode.SIMPLE_LIST
var current_tab: TabType = TabType.GENERAL

@onready var _equip_buttons_map = {
	0: btn_equip_bonnet, 1: btn_equip_left, 2: btn_equip_right, 
	3: btn_equip_body, 4: btn_equip_leg, 5: btn_equip_weapon, 
	6: btn_equip_shield, 7: btn_equip_back, 8: btn_equip_amulet
}

@onready var _filter_buttons_map = {
	0: btn_equip_filter_bonnet, 1: btn_equip_filter_left, 2: btn_equip_filter_right, 
	3: btn_equip_filter_body, 4: btn_equip_filter_leg, 5: btn_equip_filter_weapon, 
	6: btn_equip_filter_shield, 7: btn_equip_filter_back, 8: btn_equip_filter_amulet
}


func _ready() -> void :
	if btn_tab_general: btn_tab_general.pressed.connect(_switch_tab.bind(TabType.GENERAL))
	if btn_tab_equip: btn_tab_equip.pressed.connect(_switch_tab.bind(TabType.EQUIP))
	if btn_tab_mecha: btn_tab_mecha.pressed.connect(_switch_tab.bind(TabType.MECHA))
	if btn_tab_pet: btn_tab_pet.pressed.connect(_switch_tab.bind(TabType.PET))
	if btn_close: btn_close.pressed.connect( func(): closed.emit())


	for part in _equip_buttons_map.keys():
		var btn = _equip_buttons_map[part]
		if btn: btn.pressed.connect(_on_equipped_btn_pressed.bind(part))


	for part in _filter_buttons_map.keys():
		var btn = _filter_buttons_map[part]
		if btn: btn.pressed.connect(_on_filter_btn_pressed.bind(part))

	_update_filter_buttons_ui()


func setup_player(player: CharacterBody2D) -> void :
	_target_player = player


func setup_mode(mode: DisplayMode, allowed_tabs: Array = [TabType.GENERAL, TabType.EQUIP, TabType.MECHA]) -> void :
	current_mode = mode

	if btn_tab_general: btn_tab_general.disabled = not allowed_tabs.has(TabType.GENERAL)
	if btn_tab_equip: btn_tab_equip.disabled = not allowed_tabs.has(TabType.EQUIP)
	if btn_tab_pet: btn_tab_pet.disabled = not allowed_tabs.has(TabType.PET)
	if btn_tab_mecha: btn_tab_mecha.disabled = not allowed_tabs.has(TabType.MECHA)

	var default_tab = allowed_tabs[0] if allowed_tabs.size() > 0 else TabType.GENERAL
	_switch_tab(default_tab)

func _switch_tab(tab: TabType) -> void :
	current_tab = tab
	_hide_item_tooltip()

	if panel_general: panel_general.visible = false
	if panel_equip_simple: panel_equip_simple.visible = false
	if panel_equip_detailed: panel_equip_detailed.visible = false
	if panel_mecha_simple: panel_mecha_simple.visible = false
	if panel_mecha_detailed: panel_mecha_detailed.visible = false
	if panel_pet: panel_pet.visible = false

	if tab == TabType.GENERAL:
		if panel_general: panel_general.visible = true
	elif tab == TabType.EQUIP:
		if current_mode == DisplayMode.SIMPLE_LIST:
			if panel_equip_simple: panel_equip_simple.visible = true
		else:
			if panel_equip_detailed: panel_equip_detailed.visible = true
	elif tab == TabType.MECHA:
		if current_mode == DisplayMode.SIMPLE_LIST:
			if panel_mecha_simple: panel_mecha_simple.visible = true
		else:
			if panel_mecha_detailed: panel_mecha_detailed.visible = true
	elif tab == TabType.PET:
		if panel_pet: panel_pet.visible = true

	if btn_tab_general and not btn_tab_general.disabled:
		btn_tab_general.texture_normal = tex_tab_general_on if tab == TabType.GENERAL else tex_tab_general_off
	if btn_tab_equip and not btn_tab_equip.disabled:
		btn_tab_equip.texture_normal = tex_tab_equip_on if tab == TabType.EQUIP else tex_tab_equip_off
	if btn_tab_pet and not btn_tab_pet.disabled:
		btn_tab_pet.texture_normal = tex_tab_pet_on if tab == TabType.PET else tex_tab_pet_off
	if btn_tab_mecha and not btn_tab_mecha.disabled:
		btn_tab_mecha.texture_normal = tex_tab_mecha_on if tab == TabType.MECHA else tex_tab_mecha_off

	_refresh_capacity_label()
	tab_changed.emit(current_tab)

func update_capacity(tab: TabType, used: int, max_size: int) -> void :
	_tab_capacities[tab] = max_size - used
	if current_tab == tab:
		_refresh_capacity_label()



func _on_equipped_btn_pressed(target_slot: int) -> void :


	equip_slot_clicked.emit(target_slot)
	_hide_item_tooltip()

func _on_filter_btn_pressed(part: int) -> void :
	if _current_equip_filter_part == part:
		reset_equip_filter()
	else:
		filter_equip_by_part(part)

func filter_equip_by_part(part: int) -> void :
	_current_equip_filter_part = part
	_update_filter_buttons_ui()
	_apply_equip_filter()

func reset_equip_filter() -> void :
	_current_equip_filter_part = -1
	_update_filter_buttons_ui()
	_apply_equip_filter()

func _update_filter_buttons_ui() -> void :
	for part in _filter_buttons_map.keys():
		var btn = _filter_buttons_map[part]
		if btn:
			var is_on = (_current_equip_filter_part == part)
			btn.texture_normal = tex_filter_on if is_on else tex_filter_off

func _apply_equip_filter() -> void :
	if current_mode == DisplayMode.SIMPLE_LIST: return


	var filtered_items = []
	for item_data in _cached_equip_items:
		var item_idx = int(item_data.get("ItemIndex", item_data.get("item_index", item_data.get("ItemIdx", item_data.get("item_idx", item_data.get("EquipIdx", item_data.get("equip_idx", 0)))))))
		var target_part = ItemLoader.get_item_property(item_idx, "equip_slot", ItemLoader.get_item_property(item_idx, "part", -1))

		if _current_equip_filter_part == -1 or target_part == _current_equip_filter_part:
			filtered_items.append(item_data)

	_update_item_list(filtered_items, list_equip_detailed, TabType.EQUIP)



func _check_equip_conditions(item_index: int, player_data) -> bool:
	var req_level = ItemLoader.get_item_property(item_index, "req_level", 0)
	var req_race = ItemLoader.get_item_property(item_index, "req_race", "無")
	var req_job = ItemLoader.get_item_property(item_index, "req_job", "無")
	var req_str = ItemLoader.get_item_property(item_index, "req_str", 0)
	var req_dex = ItemLoader.get_item_property(item_index, "req_dex", 0)
	var req_con = ItemLoader.get_item_property(item_index, "req_con", 0)
	var req_wis = ItemLoader.get_item_property(item_index, "req_wis", 0)

	var p_level = player_data.level if "level" in player_data else 1
	var p_race = player_data.race_id if "race_id" in player_data else 255
	var p_job = player_data.job if "job" in player_data else 0
	var p_str = player_data.strength if "strength" in player_data else 0
	var p_dex = player_data.dexterity if "dexterity" in player_data else 0
	var p_con = player_data.constitution if "constitution" in player_data else 0
	var p_wis = player_data.wisdom if "wisdom" in player_data else 0

	if p_level < req_level: return false
	if p_str < req_str: return false
	if p_dex < req_dex: return false
	if p_con < req_con: return false
	if p_wis < req_wis: return false

	var req_race_str = str(req_race).strip_edges()
	if req_race_str != "" and req_race_str != "無":
		var race_name_map = {0: "人族男", 1: "人族女", 2: "米古族", 3: "機動靈", 4: "亞斯人"}
		if race_name_map.has(p_race):
			if not (race_name_map[p_race] in req_race_str.split("、")): return false
		else: return false

	var req_job_str = str(req_job).strip_edges()
	if req_job_str != "" and req_job_str != "無":
		if GameConstants.CAREER_NAMES.has(p_job):
			if not (GameConstants.CAREER_NAMES[p_job] in req_job_str.split("、")): return false
		else: return false

	return true



func render_general_items(items: Array) -> void :
	var sorted_items = _sort_items_by_index_and_uid(
		items, 
		["item_index", "ItemIndex", "item_idx", "ItemIdx"], 
		["item_uid", "ItemUID", "item_id", "ItemID"]
	)
	_update_item_list(sorted_items, list_general, TabType.GENERAL)

func render_equip_items(items: Array) -> void :
	var sorted_items = _sort_items_by_index_and_uid(
		items, 
		["item_idx", "ItemIdx", "item_index", "ItemIndex"], 
		["item_id", "ItemID", "equip_uid", "EquipUID"]
	)
	if current_mode == DisplayMode.SIMPLE_LIST:
		_update_item_list(sorted_items, list_equip_simple, TabType.EQUIP)
	else:
		_cached_equip_items = sorted_items
		_apply_equip_filter()

func render_pet_items(items: Array) -> void :
	if not panel_pet: return
	var list_pet = panel_pet.get_node_or_null("ScrollContainer/ListPet")
	if not list_pet: return
	var sorted_items = _sort_items_by_index_and_uid(
		items, 
		["pet_id", "PETID"], 
		["uid", "UID"]
	)

	var existing_children = list_pet.get_children()
	var used_count = 0

	for i in range(sorted_items.size()):
		var pet_data = sorted_items[i]
		var row: Control
		if i < existing_children.size():

			row = existing_children[i]
			row.visible = true
			_disconnect_all_signals(row, "mouse_entered")
			_disconnect_all_signals(row, "mouse_exited")
			_disconnect_all_signals(row, "gui_input")
		else:

			row = INVENTORY_ROW_SCENE.instantiate()
			list_pet.add_child(row)


		var pet_id = int(pet_data.get("pet_id", 0))
		var pet_name = str(pet_data.get("name", "未知寵物"))
		var pet_lv = int(pet_data.get("level", 1))
		var state = int(pet_data.get("state", 0))
		var evol_id = int(pet_data.get("evol_id", 0))

		var tex = null
		var mat = null

		if ItemLoader:
			tex = ItemLoader.get_icon(375)


		if PetLoader:
			var config = PetLoader.get_pet_config(pet_id)
			if config and config.has("evolutions"):
				var evol_key = str(evol_id)
				if config.evolutions.has(evol_key):
					var evol_info = config.evolutions[evol_key]
					var color_index = int(evol_info.get("color", 0))
					if color_index > 0:
						mat = _get_palette_mat(color_index)

					var outlook = str(evol_info.get("outlook", ""))
					if outlook != "":
						var photo_path = "res://Assets/Pets/Photo/" + outlook + ".png"
						if ResourceLoader.exists(photo_path):
							tex = load(photo_path)


		row.setup(tex, mat, "Lv." + str(pet_lv), pet_name, Color.WHITE, Color.WHITE)
		row.mouse_filter = Control.MOUSE_FILTER_PASS


		row.mouse_entered.connect(_show_item_tooltip.bind(pet_id, TabType.PET, pet_data))
		row.mouse_exited.connect(_hide_item_tooltip)
		row.gui_input.connect(_on_row_gui_input.bind(pet_data, TabType.PET, tex, mat))
		used_count += 1


	for i in range(used_count, existing_children.size()):
		existing_children[i].visible = false

func render_mecha_equips(items: Array) -> void :
	var target_list = list_mecha_simple if current_mode == DisplayMode.SIMPLE_LIST else list_horse_equip_detailed
	var sorted_items = _sort_items_by_index_and_uid(
		items, 
		["equip_idx", "EquipIdx", "item_idx", "ItemIdx", "item_index", "ItemIndex"], 
		["equip_uid", "EquipUID", "item_id", "ItemID"]
	)
	_update_item_list(sorted_items, target_list, TabType.MECHA)

func render_mecha_bodies(in_use_horses: Array, all_horses: Array) -> void :
	var sorted_in_use_horses = _sort_items_by_index_and_uid(
		in_use_horses, 
		["horse_idx", "HorseIdx"], 
		["horse_uid", "HorseUID"]
	)
	var sorted_all_horses = _sort_items_by_index_and_uid(
		all_horses, 
		["horse_idx", "HorseIdx"], 
		["horse_uid", "HorseUID"]
	)
	_update_item_list(sorted_in_use_horses, list_horse_in_use, TabType.MECHA)
	_update_item_list(sorted_all_horses, list_horse, TabType.MECHA)

func render_mecha_batteries(items: Array) -> void :
	var sorted_items = _sort_items_by_index_and_uid(
		items, 
		["item_index", "ItemIndex", "item_idx", "ItemIdx"], 
		["item_uid", "ItemUID", "item_id", "ItemID"]
	)
	_update_item_list(sorted_items, list_battery, TabType.GENERAL)


func render_equipped_slots(equipped_map: Dictionary, data_model = null) -> void :
	if current_mode == DisplayMode.SIMPLE_LIST: return

	for part in _equip_buttons_map.keys():
		var target_btn = _equip_buttons_map[part]
		if not target_btn: continue

		target_btn.texture_normal = null
		target_btn.material = null
		for conn in target_btn.mouse_entered.get_connections(): target_btn.mouse_entered.disconnect(conn.callable)
		for conn in target_btn.mouse_exited.get_connections(): target_btn.mouse_exited.disconnect(conn.callable)

		var safe_part_key = part if equipped_map.has(part) else str(part)
		if equipped_map.has(safe_part_key):
			var eq_data = equipped_map[safe_part_key]
			var item_idx = int(eq_data.get("ItemIndex", eq_data.get("item_index", eq_data.get("ItemIdx", eq_data.get("item_idx", eq_data.get("EquipIdx", eq_data.get("equip_idx", 0)))))))
			var icon_id = int(ItemLoader.get_item_property(item_idx, "icon_id", 1)) - 1
			var tex = ItemLoader.get_icon(icon_id)
			var color_idx = int(eq_data.get("Color", eq_data.get("color", ItemLoader.get_item_property(item_idx, "color_index", 0))))
			var effect_type = int(eq_data.get("EffectType", eq_data.get("effect_type", ItemLoader.get_item_property(item_idx, "effect_type", 0))))
			var mat = _get_palette_mat(color_idx, effect_type)

			target_btn.texture_normal = tex
			if mat: target_btn.material = mat

			target_btn.mouse_entered.connect(_show_item_tooltip.bind(item_idx, TabType.EQUIP, eq_data))
			target_btn.mouse_exited.connect(_hide_item_tooltip)


	if panel_equip_detailed:
		var player_preview = panel_equip_detailed.get_node_or_null("EquipPreview")
		if not player_preview:
			player_preview = PLAYER_SCENE.instantiate()
			player_preview.name = "EquipPreview"
			panel_equip_detailed.add_child(player_preview)
			player_preview.position = Vector2(178, 180)

			if player_preview.has_method("set_physics_process"):
				player_preview.set_physics_process(false)
				player_preview.set_process(false)

			if NetworkManager:
				if NetworkManager.change_map_received.is_connected(player_preview._on_change_map_received):
					NetworkManager.change_map_received.disconnect(player_preview._on_change_map_received)

			var head_name_tag = player_preview.get_node_or_null("UIContainer")
			if head_name_tag: head_name_tag.queue_free()

		var model = data_model if data_model else (_target_player.data_model if is_instance_valid(_target_player) else null)
		if is_instance_valid(player_preview) and model:
			if is_instance_valid(_target_player) and "current_race" in _target_player:
				player_preview.current_race = _target_player.current_race
				player_preview.current_hair = _target_player.current_hair
				player_preview.current_hair_color_index = _target_player.current_hair_color_index
				player_preview.current_body_color_index = _target_player.current_body_color_index

			if "data_model" in player_preview and player_preview.data_model:
				player_preview.data_model.bonnet_id = model.bonnet_id
				player_preview.data_model.left_hand_id = model.left_hand_id
				player_preview.data_model.right_hand_id = model.right_hand_id
				player_preview.data_model.upper_armor_id = model.upper_armor_id
				player_preview.data_model.lower_armor_id = model.lower_armor_id
				player_preview.data_model.weapon_id = model.weapon_id
				player_preview.data_model.shield_id = model.shield_id
				player_preview.data_model.back_id = model.back_id

			if player_preview.has_method("update_paper_doll_part"):
				player_preview.update_paper_doll_part(0, model.bonnet_id)
				player_preview.update_paper_doll_part(1, model.left_hand_id)
				player_preview.update_paper_doll_part(2, model.right_hand_id)
				player_preview.update_paper_doll_part(3, model.upper_armor_id)
				player_preview.update_paper_doll_part(4, model.lower_armor_id)
				player_preview.update_paper_doll_part(5, model.weapon_id)
				player_preview.update_paper_doll_part(6, model.shield_id)
				player_preview.update_paper_doll_part(7, model.back_id)

			player_preview.current_dir = "1"
			if player_preview.has_method("get_combat_idle_state"):
				player_preview.current_state = player_preview.get_combat_idle_state()
			else:
				player_preview.current_state = "idle_weapon_melee"

			if "is_dead" in player_preview: player_preview.is_dead = false
			if is_instance_valid(player_preview.get("death_effect")):
				player_preview.death_effect.visible = false
				player_preview.death_effect.stop()
			if is_instance_valid(player_preview.get("doll_engine")):
				player_preview.doll_engine.visible = true

			if player_preview.has_method("setup_layered_paper_dolls"): player_preview.setup_layered_paper_dolls()
			if player_preview.has_method("sync_animation"): player_preview.sync_animation()

			player_preview.position = Vector2(178, 180)




func _sort_items_by_index_and_uid(items: Array, index_keys: Array, uid_keys: Array) -> Array:
	var sorted_items = items.duplicate()
	sorted_items.sort_custom( func(a, b):
		var index_a = _get_first_int(a, index_keys)
		var index_b = _get_first_int(b, index_keys)
		if index_a == index_b:
			return _get_first_int(a, uid_keys) < _get_first_int(b, uid_keys)
		return index_a < index_b
	)
	return sorted_items

func _get_first_int(item: Dictionary, keys: Array) -> int:
	for key in keys:
		if item.has(key):
			return int(item[key])
	return 0

func _update_item_list(items: Array, parent_container: VBoxContainer, tab: TabType) -> void :
	if not parent_container: return

	var existing_children = parent_container.get_children()
	var used_count = 0

	for i in range(items.size()):
		var item_data = items[i]
		var row: Control

		if i < existing_children.size():
			row = existing_children[i]
			row.visible = true

			var old_data = row.get_meta("cached_item_data", {})
			var old_tab = row.get_meta("cached_tab_type", -1)
			if old_tab == tab and old_data.hash() == item_data.hash():
				used_count += 1
				continue

			_disconnect_all_signals(row, "mouse_entered")
			_disconnect_all_signals(row, "mouse_exited")
			_disconnect_all_signals(row, "gui_input")
		else:
			row = INVENTORY_ROW_SCENE.instantiate()
			parent_container.add_child(row)

		_setup_row_data(row, item_data, tab)



		row.set_meta("cached_item_data", item_data)
		row.set_meta("cached_tab_type", tab)

		used_count += 1

	for i in range(used_count, existing_children.size()):
		existing_children[i].visible = false
		existing_children[i].set_meta("cached_item_data", {})

func _disconnect_all_signals(node: Node, sig_name: String) -> void :
	if node.has_signal(sig_name):
		var conns = node.get_signal_connection_list(sig_name)
		for c in conns:
			node.disconnect(sig_name, c.callable)

func _setup_row_data(row: Control, item_data: Dictionary, tab: TabType) -> void :
	var item_index = int(item_data.get("ItemIndex", item_data.get("item_index", item_data.get("ItemIdx", item_data.get("item_idx", item_data.get("EquipIdx", item_data.get("equip_idx", item_data.get("horse_idx", 0))))))))
	var count = int(item_data.get("Count", item_data.get("count", 1)))

	var item_res = null
	if ItemLoader: item_res = ItemLoader.get_item_data(item_index)

	var item_name = item_data.get("Name", item_data.get("name", ""))
	if item_name == "":
		item_name = item_res.item_name if item_res and "item_name" in item_res else "未知"

	var icon_id = (item_res.icon_id if item_res and "icon_id" in item_res else 1) - 1
	var tex = ItemLoader.get_icon(icon_id)

	var color_idx = int(item_data.get("Color", item_data.get("color", item_res.color_index if item_res and "color_index" in item_res else 0)))
	var effect_type = int(item_data.get("EffectType", item_data.get("effect_type", item_res.effect_type if item_res and "effect_type" in item_res else 0)))
	var mat = _get_palette_mat(color_idx, effect_type)

	var top_text = ""
	var is_equippable = true
	var c_yellow = Color(1.0, 0.941, 0.176, 1.0)

	if tab == TabType.GENERAL:
		top_text = "x " + str(count)
	elif tab == TabType.EQUIP or tab == TabType.MECHA:
		var req_level = item_res.req_level if item_res and "req_level" in item_res else 0
		top_text = "Lv." + str(req_level)
		if tab == TabType.EQUIP and is_instance_valid(_target_player) and "data_model" in _target_player and _target_player.data_model:
			is_equippable = _check_equip_conditions(item_index, _target_player.data_model)

	var text_color = Color.WHITE
	if tab == TabType.EQUIP:
		text_color = c_yellow if is_equippable else Color(0.7, 0.7, 0.7, 1.0)
		row.setup(tex, mat, top_text, item_name, text_color, text_color)
	else:
		row.setup(tex, mat, top_text, item_name, c_yellow, c_yellow)


	if show_sell_price and row.has_method("set_price"):
		var base_price = ItemLoader.get_item_property(item_index, "price", 0)
		row.set_price(base_price / 2, Color.YELLOW)
	elif show_repair_price and row.has_method("set_price"):
		var player_money = _target_player.data_model.money if is_instance_valid(_target_player) and "data_model" in _target_player else 0
		var durability = ItemLoader.get_item_property(item_index, "durability", 0)
		var price = ItemLoader.get_item_property(item_index, "price", 0)
		var damage = int(item_data.get("Damage", item_data.get("damage", 0)))

		if durability > 0 and damage < durability:
			var used = durability - damage
			var cost = int(floor(float(used) / float(durability) * float(price)))
			if cost < 1: cost = 1

			var col = Color.WHITE if cost > player_money else Color.YELLOW
			row.set_price(cost, col)
		else:
			row.set_price(0, Color.YELLOW)

	row.mouse_filter = Control.MOUSE_FILTER_PASS
	row.mouse_entered.connect(_show_item_tooltip.bind(item_index, tab, item_data))
	row.mouse_exited.connect(_hide_item_tooltip)
	row.gui_input.connect(_on_row_gui_input.bind(item_data, tab, tex, mat))

func _on_row_gui_input(event: InputEvent, item_data: Dictionary, tab: TabType, tex: Texture2D, mat: Material) -> void :
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		if tab == TabType.PET and current_mode == DisplayMode.FULL_INVENTORY:
			var pet_uid = int(item_data.get("uid", item_data.get("UID", -1)))
			if pet_uid > 0 and GameManager and GameManager.current_hud:
				if GameManager.current_hud.has_method("show_pet_window"):
					GameManager.current_hud.show_pet_window(pet_uid)

	item_clicked.emit(event, item_data, tab, tex, mat)

func _get_palette_mat(color_idx: int, effect_type: int = 0) -> Material:
	if color_idx <= 0 and effect_type <= 0: return null

	var cache_key = str(color_idx) + "_" + str(effect_type)
	if _palette_mat_cache.has(cache_key):
		return _palette_mat_cache[cache_key]

	var mat = ShaderMaterial.new()
	mat.shader = PALETTE_SHADER
	mat.set_shader_parameter("effect_type", effect_type)
	mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_idx, 0.0))
	_palette_mat_cache[cache_key] = mat
	return mat

func _show_item_tooltip(item_index: int, tab_type: int, item_data: Dictionary = {}) -> void :
	if GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):
		GameManager.current_hud.shared_tooltip.show_item(item_index, tab_type, item_data)

func _hide_item_tooltip() -> void :
	if GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):
		GameManager.current_hud.shared_tooltip.hide_tooltip()

func _refresh_capacity_label() -> void :
	if lbl_capacity:
		if _tab_capacities.has(current_tab):
			lbl_capacity.text = str(_tab_capacities[current_tab])
		else:
			lbl_capacity.text = "0"
