
extends Control


@onready var btn_close = $BtnClose


@onready var doll_container = $DollContainer
@onready var lbl_pet_name = $LblPetName
@onready var lbl_level = $LblLevel
@onready var lbl_type = $LblType
@onready var lbl_evo = $LblEvo
@onready var lbl_con = $LblCon
@onready var lbl_str = $LblStr
@onready var lbl_dex = $LblDex
@onready var lbl_wis = $LblWis
@onready var lbl_extra_str = $LblExtraStr
@onready var lbl_extra_dex = $LblExtraDex
@onready var lbl_extra_con = $LblExtraCon
@onready var lbl_extra_wis = $LblExtraWis


@onready var bar_hp = $BarHP
@onready var bar_mp = $BarMP
@onready var bar_exp = $BarExp

@onready var lbl_hp = $LblHP
@onready var lbl_mp = $LblMP
@onready var lbl_exp = $LblExp
@onready var lbl_life = $LblLife
@onready var lbl_description = $LblDescription


@onready var btn_add_str = $ButtonAddStr
@onready var btn_add_dex = $ButtonAddDex
@onready var btn_add_con = $ButtonAddCon
@onready var btn_add_wis = $ButtonAddWis
@onready var btn_add_life = $ButtonAddLife
@onready var btn_reset_attr = $BtnResetAttr

@onready var btn_feed = $BtnFeed
@onready var btn_equip = $BtnEquip
@onready var btn_summon = $BtnSummon
@onready var btn_unsummon = $BtnUnsummon
@onready var btn_edit_name = $BtnEditName


@onready var lbl_atk = $LblAtk
@onready var lbl_def = $LblDef
@onready var lbl_m_atk = $LblMAtk
@onready var lbl_m_def = $LblMDef
@onready var lbl_hit = $LblHit
@onready var lbl_dodge = $LblDodge
@onready var lbl_total_atk = $LblTotalAtk
@onready var lbl_total_def = $LblTotalDef


@onready var list_skill = $ScrollContainer / ListSkill


@onready var feed_window = $FeedWindow
@onready var lbl_feed = $FeedWindow / LblFeed
@onready var bar_feed = $FeedWindow / BarFeed if has_node("FeedWindow/BarFeed") else $FeedWindow.find_child("BarFeed", true, false)
@onready var list_recovery = $FeedWindow / ScrollContainer / ListRecovery
@onready var btn_feed_close = $FeedWindow / BtnClose


@onready var equip_window = $EquipWindow if has_node("EquipWindow") else find_child("EquipWindow", true, false)
@onready var btn_equip_close = $EquipWindow / BtnClose if has_node("EquipWindow/BtnClose") else (equip_window.find_child("BtnClose", true, false) if equip_window else null)
@onready var lbl_equip_capacity = $EquipWindow / LblCapacity if has_node("EquipWindow/LblCapacity") else (equip_window.find_child("LblCapacity", true, false) if equip_window else null)

@onready var btn_pet_weapon = $EquipWindow / BtnWeapon if has_node("EquipWindow/BtnWeapon") else (equip_window.find_child("BtnWeapon", true, false) if equip_window else null)
@onready var btn_pet_shield = $EquipWindow / BtnShield if has_node("EquipWindow/BtnShield") else (equip_window.find_child("BtnShield", true, false) if equip_window else null)
@onready var btn_pet_amulet = $EquipWindow / BtnAmulet if has_node("EquipWindow/BtnAmulet") else (equip_window.find_child("BtnAmulet", true, false) if equip_window else null)

@onready var lbl_pet_weapon = $EquipWindow / LblWeapon if has_node("EquipWindow/LblWeapon") else (equip_window.find_child("LblWeapon", true, false) if equip_window else null)
@onready var lbl_pet_shield = $EquipWindow / LblShield if has_node("EquipWindow/LblShield") else (equip_window.find_child("LblShield", true, false) if equip_window else null)
@onready var lbl_pet_amulet = $EquipWindow / LblAmulet if has_node("EquipWindow/LblAmulet") else (equip_window.find_child("LblAmulet", true, false) if equip_window else null)

@onready var list_pet_equip = $EquipWindow / ScrollContainer / ListEquip if has_node("EquipWindow/ScrollContainer/ListEquip") else (equip_window.find_child("ListEquip", true, false) if equip_window else null)

const SKILL_ROW_SCENE = preload("res://skill_item_row.tscn")
const INVENTORY_ROW_SCENE = preload("res://inventory_item_row.tscn")
const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}
var _palette_mat_cache: Dictionary = {}


var selected_pet_uid: int = -1
var _last_rendered_pet_uid: int = -1
var _last_rendered_evol_id: int = -1
var _local_feed: float = -1.0
var _last_feed_pet_uid: int = -1
var _last_feed_state: int = -1
var _last_feed_value: int = -1
var _last_feed_limit: int = -1

func _ready() -> void :
	visible = false
	if feed_window: feed_window.visible = false
	if equip_window: equip_window.visible = false
	visibility_changed.connect(_update_processing_state)
	if feed_window: feed_window.visibility_changed.connect(_update_processing_state)
	_update_processing_state()

	if btn_close:
		btn_close.pressed.connect( func():
			_hide_skill_tooltip()
			if feed_window: feed_window.visible = false
			if equip_window: equip_window.visible = false
			visible = false
		)
	if btn_add_str: btn_add_str.pressed.connect(_on_add_stat_pressed.bind("STR"))
	if btn_add_dex: btn_add_dex.pressed.connect(_on_add_stat_pressed.bind("DEX"))
	if btn_add_con: btn_add_con.pressed.connect(_on_add_stat_pressed.bind("CON"))
	if btn_add_wis: btn_add_wis.pressed.connect(_on_add_stat_pressed.bind("WIS"))
	if btn_add_life: btn_add_life.pressed.connect(_on_btn_add_life_pressed)
	if btn_reset_attr: btn_reset_attr.pressed.connect(_on_btn_reset_attr_pressed)
	if btn_edit_name: btn_edit_name.pressed.connect(_on_btn_edit_name_pressed)
	if btn_summon: btn_summon.pressed.connect(_on_btn_summon_pressed)
	if btn_unsummon: btn_unsummon.pressed.connect(_on_btn_unsummon_pressed)


	if btn_feed: btn_feed.pressed.connect(_on_btn_feed_pressed)
	if btn_feed_close: btn_feed_close.pressed.connect( func(): if feed_window: feed_window.visible = false)


	if btn_equip: btn_equip.pressed.connect(_on_btn_equip_pressed)
	if btn_equip_close: btn_equip_close.pressed.connect( func(): if equip_window: equip_window.visible = false)

	if btn_pet_weapon: btn_pet_weapon.pressed.connect(_on_pet_unequip_slot_pressed.bind(0))
	if btn_pet_shield: btn_pet_shield.pressed.connect(_on_pet_unequip_slot_pressed.bind(1))
	if btn_pet_amulet: btn_pet_amulet.pressed.connect(_on_pet_unequip_slot_pressed.bind(2))


	if NetworkManager.has_signal("pet_synced"):
		NetworkManager.pet_synced.connect(_on_pet_synced)
	if NetworkManager.has_signal("inventory_synced"):
		NetworkManager.inventory_synced.connect(_on_inventory_synced)
	if NetworkManager.has_signal("equip_synced"):
		NetworkManager.equip_synced.connect(_on_equip_synced)

func _update_processing_state() -> void :
	set_process(is_visible_in_tree() and feed_window != null and feed_window.is_visible_in_tree())


func open_pet(pet_uid: int) -> void :
	selected_pet_uid = pet_uid
	_invalidate_feed_display_cache()
	visible = true
	_refresh_ui()

func set_selected_pet(pet_uid: int) -> void :
	open_pet(pet_uid)

func _on_pet_synced(_pets: Array, _max_size: int) -> void :
	_invalidate_feed_display_cache()
	if visible:
		_refresh_ui()

func _on_inventory_synced(_items: Array, _max_size: int) -> void :
	if visible and feed_window and feed_window.visible:
		_refresh_feed_window()

func _on_equip_synced(_equips: Array, _max_size: int) -> void :
	if visible:
		_refresh_ui()


func _refresh_ui() -> void :
	var pet_data = _get_selected_pet_data()

	if pet_data.is_empty():
		_clear_ui()
		return

	var pet_uid = _get_val(pet_data, "uid", "UID", -1)
	var pet_id = _get_val(pet_data, "pet_id", "PETID", 0)
	var evol_id = _get_val(pet_data, "evol_id", "EvolID", 0)
	var main_skill_id = _get_val(pet_data, "main_skill_id", "MainSkillID", 0)
	var evol_cfg = _get_pet_evol_config(pet_id, evol_id)


	_update_basic_info(pet_data, evol_cfg)
	_update_combat_stats(pet_data)
	_update_bars(pet_data, pet_uid != _last_rendered_pet_uid)
	_update_buttons(pet_data)


	if pet_uid != _last_rendered_pet_uid or evol_id != _last_rendered_evol_id:
		_refresh_doll_container(evol_cfg)
		_refresh_skill_list(pet_uid, evol_cfg, main_skill_id)

		_last_rendered_pet_uid = pet_uid
		_last_rendered_evol_id = evol_id
	else:
		_update_skill_highlights(main_skill_id)


	if feed_window and feed_window.visible:
		_refresh_feed_window()
	if equip_window and equip_window.visible:
		_refresh_equip_window()



func _on_btn_equip_pressed() -> void :
	if selected_pet_uid <= 0: return
	if equip_window:
		equip_window.visible = !equip_window.visible
		if equip_window.visible:
			_refresh_equip_window()

func _refresh_equip_window() -> void :
	var pet_data = _get_selected_pet_data()
	if pet_data.is_empty():
		if equip_window: equip_window.visible = false
		return

	var player = GameManager.current_player if GameManager else null
	if not player or not player.data_model: return


	var used_equip = 0
	for eq in player.data_model.equip_list:
		if eq.get("in_bank", 0) == 0:
			used_equip += 1
	var remaining_cap = player.data_model.equip_max_size - used_equip
	if lbl_equip_capacity:
		lbl_equip_capacity.text = str(remaining_cap)


	var equip_uids = [
		int(_get_val(pet_data, "equip0", "Equip0", 0)), 
		int(_get_val(pet_data, "equip1", "Equip1", 0)), 
		int(_get_val(pet_data, "equip2", "Equip2", 0))
	]

	var slot_btns = [btn_pet_weapon, btn_pet_shield, btn_pet_amulet]
	var slot_lbls = [lbl_pet_weapon, lbl_pet_shield, lbl_pet_amulet]

	for slot in range(3):
		var eq_uid = equip_uids[slot]
		var btn = slot_btns[slot]
		var lbl = slot_lbls[slot]

		if btn:
			btn.texture_normal = null
			btn.material = null
			for conn in btn.mouse_entered.get_connections():
				btn.mouse_entered.disconnect(conn.callable)
			for conn in btn.mouse_exited.get_connections():
				btn.mouse_exited.disconnect(conn.callable)

		var eq_item = null
		if eq_uid > 0:
			for eq in player.data_model.equip_list:
				if int(eq.get("item_id", 0)) == eq_uid:
					eq_item = eq
					break

		if eq_uid > 0 and eq_item != null:
			var item_idx = int(eq_item.get("item_idx", 0))
			var item_name = ItemLoader.get_item_property(item_idx, "item_name", "未知裝備")
			var icon_id = int(ItemLoader.get_item_property(item_idx, "icon_id", 1)) - 1
			var tex = ItemLoader.get_icon(icon_id)
			var color_idx = int(eq_item.get("color", ItemLoader.get_item_property(item_idx, "color_index", 0)))
			var effect_type = int(eq_item.get("EffectType", eq_item.get("effect_type", ItemLoader.get_item_property(item_idx, "effect_type", 0))))
			var mat = _get_palette_mat(color_idx, effect_type)

			if btn:
				btn.texture_normal = tex
				if mat: btn.material = mat
				btn.mouse_entered.connect(_show_item_tooltip.bind(item_idx, SharedInventoryPanel.TabType.EQUIP, eq_item))
				btn.mouse_exited.connect(_hide_skill_tooltip)

			if lbl:
				lbl.text = item_name
		else:
			if btn:
				btn.texture_normal = null
				btn.material = null
			if lbl:
				lbl.text = "（無）"


	if not list_pet_equip: return
	for c in list_pet_equip.get_children():
		c.queue_free()

	var pet_level = int(_get_val(pet_data, "level", "Level", 1))


	var available_equips = []
	for eq in player.data_model.equip_list:
		var part = int(eq.get("part", 255))
		var in_bank = int(eq.get("in_bank", 0))
		var in_shop = int(eq.get("in_shop", 0))

		if part == 255 and in_bank == 0 and in_shop == 0:
			var item_idx = int(eq.get("item_idx", 0))
			var equip_slot = int(ItemLoader.get_item_property(item_idx, "equip_slot", ItemLoader.get_item_property(item_idx, "part", -1)))


			if equip_slot in [5, 6, 8]:
				available_equips.append(eq)


	available_equips.sort_custom( func(a, b):
		var idx_a = int(a.get("item_idx", 0))
		var idx_b = int(b.get("item_idx", 0))
		if idx_a == idx_b:
			return int(a.get("item_id", 0)) < int(b.get("item_id", 0))
		return idx_a < idx_b
	)

	for eq in available_equips:
		var item_idx = int(eq.get("item_idx", 0))
		var item_uid = int(eq.get("item_id", 0))
		var item_name = ItemLoader.get_item_property(item_idx, "item_name", "未知裝備")
		var icon_id = int(ItemLoader.get_item_property(item_idx, "icon_id", 1)) - 1
		var tex = ItemLoader.get_icon(icon_id)
		var color_idx = int(eq.get("color", ItemLoader.get_item_property(item_idx, "color_index", 0)))
		var effect_type = int(eq.get("EffectType", eq.get("effect_type", ItemLoader.get_item_property(item_idx, "effect_type", 0))))
		var mat = _get_palette_mat(color_idx, effect_type)

		var req_level = int(ItemLoader.get_item_property(item_idx, "req_level", 0))
		var equip_slot = int(ItemLoader.get_item_property(item_idx, "equip_slot", ItemLoader.get_item_property(item_idx, "part", -1)))


		var target_slot = -1
		if equip_slot == 5: target_slot = 0
		elif equip_slot == 6: target_slot = 1
		elif equip_slot == 8: target_slot = 2

		var row = INVENTORY_ROW_SCENE.instantiate()
		list_pet_equip.add_child(row)

		var is_equippable = (pet_level >= req_level)
		var c_yellow = Color(1.0, 0.941, 0.176, 1.0)
		var text_color = c_yellow if is_equippable else Color(0.7, 0.7, 0.7, 1.0)
		var top_text = "Lv." + str(req_level)

		row.setup(tex, mat, top_text, item_name, text_color, text_color)
		row.mouse_filter = Control.MOUSE_FILTER_PASS

		if target_slot != -1:
			row.gui_input.connect( func(event: InputEvent):
				if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
					_on_pet_equip_item_clicked(item_uid, target_slot)
			)

		row.mouse_entered.connect(_show_item_tooltip.bind(item_idx, SharedInventoryPanel.TabType.EQUIP, eq))
		row.mouse_exited.connect(_hide_skill_tooltip)

func _on_pet_equip_item_clicked(item_uid: int, target_slot: int) -> void :
	if selected_pet_uid <= 0: return
	NetworkManager.send_pet_action("Equip", selected_pet_uid, {"ItemUID": item_uid, "TargetSlot": target_slot})

func _on_pet_unequip_slot_pressed(target_slot: int) -> void :
	if selected_pet_uid <= 0: return
	NetworkManager.send_pet_action("Unequip", selected_pet_uid, {"TargetSlot": target_slot})

func _show_item_tooltip(item_index: int, tab_type: int, item_data: Dictionary = {}) -> void :
	if GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):
		GameManager.current_hud.shared_tooltip.show_item(item_index, tab_type, item_data)



func _on_btn_feed_pressed() -> void :
	if selected_pet_uid <= 0: return
	if feed_window:
		_invalidate_feed_display_cache()
		feed_window.visible = true
		_refresh_feed_window()

func _refresh_feed_window() -> void :
	var pet_data = _get_selected_pet_data()
	if pet_data.is_empty():
		if feed_window: feed_window.visible = false
		return

	_update_feed_value_display(pet_data)

	if not list_recovery: return
	for c in list_recovery.get_children():
		c.queue_free()

	var player = GameManager.current_player if GameManager else null
	if not player or not player.data_model: return

	var inventory = player.data_model.inventory
	for item in inventory:
		var count = int(item.get("count", 0))
		var in_bank = int(item.get("in_bank", 0))
		var in_shop = int(item.get("in_shop", 0))
		if count <= 0 or in_bank != 0 or in_shop != 0:
			continue

		var item_id = int(item.get("item_index", 0))
		var item_uid = int(item.get("item_uid", 0))

		var add_hp = int(ItemLoader.get_item_property(item_id, "recover_hp", 0))
		var add_mp = int(ItemLoader.get_item_property(item_id, "recover_mp", 0))
		var add_sp = int(ItemLoader.get_item_property(item_id, "recover_sp", 0))
		var total_rec = add_hp + add_mp + add_sp

		if total_rec <= 0:
			continue

		var item_name = item.get("name", item.get("Name", ""))
		if item_name == "":
			item_name = str(ItemLoader.get_item_property(item_id, "item_name", "未知"))

		var icon_id = int(ItemLoader.get_item_property(item_id, "icon_id", 1)) - 1
		var icon_tex = ItemLoader.get_icon(icon_id)

		var color_idx = int(item.get("color", item.get("Color", ItemLoader.get_item_property(item_id, "color_index", 0))))
		var effect_type = int(item.get("EffectType", item.get("effect_type", ItemLoader.get_item_property(item_id, "effect_type", 0))))
		var mat = _get_palette_mat(color_idx, effect_type)

		var row = INVENTORY_ROW_SCENE.instantiate()
		list_recovery.add_child(row)

		var c_yellow = Color(1.0, 0.941, 0.176, 1.0)
		var top_text = "x " + str(count)
		var bottom_text = item_name + (" (+%d)" % total_rec)

		row.setup(icon_tex, mat, top_text, bottom_text, c_yellow, c_yellow)
		row.mouse_filter = Control.MOUSE_FILTER_PASS

		row.gui_input.connect( func(event: InputEvent):
			if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
				_on_recovery_item_clicked(item_uid)
		)

		row.mouse_entered.connect( func():
			if GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):
				GameManager.current_hud.shared_tooltip.show_item(item_id, 0, item)
		)
		row.mouse_exited.connect( func():
			if GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):
				GameManager.current_hud.shared_tooltip.hide_tooltip()
		)

func _invalidate_feed_display_cache() -> void :
	_last_feed_pet_uid = -1
	_last_feed_state = -1
	_last_feed_value = -1
	_last_feed_limit = -1

func _update_feed_value_display(pet_data: Dictionary) -> void :
	var pet_uid = int(_get_val(pet_data, "uid", "UID", -1))
	var state = int(_get_val(pet_data, "state", "State", 0))
	var feed_val = int(_get_val(pet_data, "feed_value", "FeedValue", 0))
	var feed_limit = int(_get_val(pet_data, "feed_exp_limit", "FeedExpLimit", 43200))
	if feed_limit <= 0:
		feed_limit = 1
	_local_feed = float(feed_val)

	if pet_uid == _last_feed_pet_uid and state == _last_feed_state and feed_val == _last_feed_value and feed_limit == _last_feed_limit:
		return

	var pet_changed: bool = int(pet_uid) != _last_feed_pet_uid
	_last_feed_pet_uid = pet_uid
	_last_feed_state = state
	_last_feed_value = feed_val
	_last_feed_limit = feed_limit
	if lbl_feed:
		lbl_feed.text = "%d / %d" % [feed_val, feed_limit]
	if bar_feed:
		bar_feed.max_value = feed_limit
		if pet_changed and bar_feed.has_method("set_value_immediate"):
			bar_feed.set_value_immediate(feed_val)
		else:
			bar_feed.value = feed_val

func _on_recovery_item_clicked(item_uid: int) -> void :
	if selected_pet_uid <= 0: return
	NetworkManager.send_pet_action("Feed", selected_pet_uid, {"ItemUID": item_uid})



func _update_basic_info(data: Dictionary, cfg: Dictionary) -> void :
	var pet_name = str(_get_val(data, "name", "Name", "寵物"))
	var pet_id = int(_get_val(data, "pet_id", "PETID", 0))

	var base_cfg = PetLoader.get_pet_config(pet_id) if PetLoader else {}

	if lbl_pet_name: lbl_pet_name.text = pet_name
	if lbl_type: lbl_type.text = str(base_cfg.get("name", "未知品種"))
	if lbl_evo: lbl_evo.text = str(cfg.get("sub_name", "基本型"))
	if lbl_description: lbl_description.text = str(cfg.get("description", ""))
	if lbl_level: lbl_level.text = str(_get_val(data, "level", "Level", 1))
	if lbl_con: lbl_con.text = str(_get_val(data, "con", "CON", 0))
	if lbl_str: lbl_str.text = str(_get_val(data, "str", "STR", 0))
	if lbl_dex: lbl_dex.text = str(_get_val(data, "dex", "DEX", 0))
	if lbl_wis: lbl_wis.text = str(_get_val(data, "wis", "WIS", 0))
	if lbl_extra_str: lbl_extra_str.text = "+%d" % int(_get_val(data, "extra_str", "ExtraSTR", 0))
	if lbl_extra_dex: lbl_extra_dex.text = "+%d" % int(_get_val(data, "extra_dex", "ExtraDEX", 0))
	if lbl_extra_con: lbl_extra_con.text = "+%d" % int(_get_val(data, "extra_con", "ExtraCON", 0))
	if lbl_extra_wis: lbl_extra_wis.text = "+%d" % int(_get_val(data, "extra_wis", "ExtraWIS", 0))
	if lbl_life: lbl_life.text = str(_get_val(data, "life_count", "LifeCount", 0))

func _update_combat_stats(data: Dictionary) -> void :
	var atk = int(_get_val(data, "attack", "Attack", 0))
	var def = int(_get_val(data, "defense", "Defense", 0))
	var matk = int(_get_val(data, "magic_attack", "MagicAttack", 0))
	var mdef = int(_get_val(data, "magic_defense", "MagicDefense", 0))

	if lbl_atk: lbl_atk.text = str(atk)
	if lbl_def: lbl_def.text = str(def)
	if lbl_m_atk: lbl_m_atk.text = str(matk)
	if lbl_m_def: lbl_m_def.text = str(mdef)
	if lbl_hit: lbl_hit.text = str(_get_val(data, "hit", "Hit", 0))
	if lbl_dodge: lbl_dodge.text = str(_get_val(data, "dodge", "Dodge", 0))

	if lbl_total_atk: lbl_total_atk.text = str(atk + matk)
	if lbl_total_def: lbl_total_def.text = str(def + mdef)

func _update_bars(data: Dictionary, immediate: bool = false) -> void :
	var hp = int(_get_val(data, "hp", "HP", 0))
	var max_hp = int(_get_val(data, "max_hp", "MaxHP", 1))
	var mp = int(_get_val(data, "mp", "MP", 0))
	var max_mp = int(_get_val(data, "max_mp", "MaxMP", 1))
	var current_exp = int(_get_val(data, "exp", "Exp", 0))
	var max_exp = int(_get_val(data, "max_exp", "MaxExp", 1))
	if max_exp <= 0: max_exp = 1

	_set_resource_bar_value(bar_hp, hp, max_hp, immediate)
	_set_resource_bar_value(bar_mp, mp, max_mp, immediate)
	if lbl_hp: lbl_hp.text = "%d / %d" % [hp, max_hp]
	if lbl_mp: lbl_mp.text = "%d / %d" % [mp, max_mp]
	_set_resource_bar_value(bar_exp, current_exp, max_exp, immediate)
	if lbl_exp: lbl_exp.text = "%d / %d" % [current_exp, max_exp]

func _set_resource_bar_value(bar: Range, current_value: float, maximum_value: float, immediate: bool) -> void :
	if not bar:
		return
	bar.max_value = maximum_value
	if immediate and bar.has_method("set_value_immediate"):
		bar.set_value_immediate(current_value)
	else:
		bar.value = current_value

func _update_buttons(data: Dictionary) -> void :
	var use_exp = int(_get_val(data, "use_exp", "UseExp", 0))
	var show_add_btn = use_exp > 0
	if btn_add_str: btn_add_str.visible = show_add_btn
	if btn_add_dex: btn_add_dex.visible = show_add_btn
	if btn_add_con: btn_add_con.visible = show_add_btn
	if btn_add_wis: btn_add_wis.visible = show_add_btn

	var state = int(_get_val(data, "state", "State", 0))
	var life_count = int(_get_val(data, "life_count", "LifeCount", 0))
	var feed_value = int(_get_val(data, "feed_value", "FeedValue", 0))
	var summon_wait_count = int(_get_val(data, "summon_wait_count", "SummonWaitCount", 0))
	var pet_level = int(_get_val(data, "level", "Level", 1))

	var current_unix = Time.get_unix_time_from_system() * 1000.0
	var player_level = 1
	if GameManager and is_instance_valid(GameManager.current_player) and "data_model" in GameManager.current_player:
		player_level = GameManager.current_player.data_model.level

	var can_summon = (life_count > 0 and feed_value > 0 and current_unix >= summon_wait_count and player_level >= pet_level)

	if btn_summon:
		btn_summon.disabled = (state == 1) or not can_summon

		var reason = ""
		if state == 1:
			reason = "已召喚"
		elif life_count <= 0:
			reason = "壽命已盡，請先使用復活道具"
		elif feed_value <= 0:
			reason = "飽食度不足，無法召喚"
		elif summon_wait_count > current_unix:
			var remaining_sec = int(ceil((summon_wait_count - current_unix) / 1000.0))
			var mins = remaining_sec / 60
			var secs = remaining_sec % 60
			reason = "冷卻中（等待 %d 分 %d 秒後才能召喚）" % [mins, secs]
		elif player_level < pet_level:
			reason = "角色等級不足（需 Lv.%d）" % pet_level

		btn_summon.tooltip_text = reason

	if btn_unsummon:
		btn_unsummon.disabled = (state == 0)



func _get_val(data: Dictionary, snake_key: String, pascal_key: String, default_val: Variant) -> Variant:
	return data.get(snake_key, data.get(pascal_key, default_val))

func _get_selected_pet_data() -> Dictionary:
	var player = GameManager.current_player if GameManager else null
	if not player or not player.data_model: return {}

	var pet_list = player.data_model.pet_list
	if pet_list.is_empty(): return {}

	for p in pet_list:
		if int(_get_val(p, "uid", "UID", -1)) == selected_pet_uid:
			return p

	selected_pet_uid = int(_get_val(pet_list[0], "uid", "UID", -1))
	return pet_list[0]

func _get_pet_evol_config(pet_id: int, evol_id: int) -> Dictionary:
	var pet_cfg = PetLoader.get_pet_config(pet_id) if PetLoader else {}
	var evolutions = pet_cfg.get("evolutions", {})

	if evolutions.has(evol_id): return evolutions[evol_id]
	if evolutions.has(str(evol_id)): return evolutions[str(evol_id)]
	return {}



func _refresh_doll_container(evol_cfg: Dictionary) -> void :
	if not doll_container: return
	for child in doll_container.get_children():
		child.queue_free()

	var outlook = str(evol_cfg.get("outlook", ""))
	if outlook == "": return

	var frames = MonsterLoader.get_monster_frames(outlook) if MonsterLoader else null
	if frames:
		var sprite = AnimatedSprite2D.new()
		sprite.sprite_frames = frames
		sprite.centered = false
		doll_container.add_child(sprite)
		sprite.frame_changed.connect(_update_preview_sprite_offset.bind(sprite))

		var anim_name = "IDLE_1"
		if sprite.sprite_frames.has_animation(anim_name):
			sprite.play(anim_name)
		else:
			sprite.play()
		_update_preview_sprite_offset(sprite)

		var playing_anim = sprite.animation
		if sprite.sprite_frames.has_animation(playing_anim):
			var min_corner = Vector2(INF, INF)
			var max_corner = Vector2( - INF, - INF)
			for frame_index in sprite.sprite_frames.get_frame_count(playing_anim):
				var frame_texture = sprite.sprite_frames.get_frame_texture(playing_anim, frame_index)
				if frame_texture:
					var old_offset = frame_texture.get_meta("old_offset") if frame_texture.has_meta("old_offset") else Vector2.ZERO
					var frame_top_left = - old_offset
					var frame_bottom_right = frame_top_left + frame_texture.get_size()
					min_corner = min_corner.min(frame_top_left)
					max_corner = max_corner.max(frame_bottom_right)
			var preview_bounds = Rect2(min_corner, max_corner - min_corner)
			if preview_bounds.size.x > 0.0 and preview_bounds.size.y > 0.0:
				var preview_scale = minf(1.0, minf(doll_container.size.x / preview_bounds.size.x, doll_container.size.y / preview_bounds.size.y))
				sprite.scale = Vector2.ONE * preview_scale
				sprite.position = doll_container.size / 2.0 - preview_bounds.get_center() * preview_scale

		var color_index = int(evol_cfg.get("color", 0))
		if color_index > 0:
			var mat = ShaderMaterial.new()
			mat.shader = PALETTE_SHADER
			mat.set_shader_parameter("effect_type", 0)
			mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
			sprite.material = mat

func _update_preview_sprite_offset(sprite: AnimatedSprite2D) -> void :
	if not sprite.sprite_frames or not sprite.sprite_frames.has_animation(sprite.animation): return
	var frame_texture = sprite.sprite_frames.get_frame_texture(sprite.animation, sprite.frame)
	if frame_texture:
		var old_offset = frame_texture.get_meta("old_offset") if frame_texture.has_meta("old_offset") else Vector2.ZERO
		sprite.offset = - old_offset

func _refresh_skill_list(pet_uid: int, evol_cfg: Dictionary, main_skill_id: int) -> void :
	if not list_skill: return
	for child in list_skill.get_children():
		child.queue_free()

	var skills = evol_cfg.get("skills", [])
	if skills.is_empty():
		var lbl = Label.new()
		lbl.text = "這小傢伙還沒學會任何技能呢....."
		lbl.add_theme_font_size_override("font_size", 14)
		lbl.add_theme_color_override("font_color", Color.GRAY)
		list_skill.add_child(lbl)
		return

	for skill_info in skills:
		var s_id = int(skill_info.get("skill_id", 0))
		var s_lv = int(skill_info.get("level", 1))

		var row = SKILL_ROW_SCENE.instantiate()
		list_skill.add_child(row)
		row.mouse_filter = Control.MOUSE_FILTER_PASS

		var skill_name = "未知技能(%d)" % s_id
		var icon_tex: Texture2D = null

		if SkillLoader:
			var s_res = SkillLoader.get_skill_data(s_id)
			if s_res:
				skill_name = s_res.skill_name
				icon_tex = SkillLoader.get_icon(s_res.icon_id)

		if s_id == 9999:
			skill_name = "自動撿拾"
			s_lv = 1
			if SkillLoader:
				icon_tex = SkillLoader.get_icon(112)

		row.set_meta("skill_id", s_id)
		row.set_meta("skill_level", s_lv)
		row.set_meta("icon_tex", icon_tex)
		row.set_meta("base_name", skill_name)

		row.skill_clicked.connect(_on_skill_clicked)
		row.mouse_entered.connect(_show_skill_tooltip.bind(s_id, s_lv, icon_tex))
		row.mouse_exited.connect(_hide_skill_tooltip)

	_update_skill_highlights(main_skill_id)

func _update_skill_highlights(main_skill_id: int) -> void :
	if not list_skill: return
	for child in list_skill.get_children():
		if child.has_meta("skill_id"):
			var s_id = child.get_meta("skill_id")
			var s_lv = child.get_meta("skill_level")
			var icon_tex = child.get_meta("icon_tex")
			var base_name = child.get_meta("base_name")
			var is_main = (s_id == main_skill_id)

			var display_name = base_name + (" [主技能]" if is_main else "")
			var text_color = Color.YELLOW if is_main else Color.WHITE

			if child.has_method("setup"):
				child.setup(icon_tex, display_name, s_lv, text_color, s_id)

func _on_skill_clicked(skill_id: int) -> void :

	if skill_id == 9999:
		return

	if selected_pet_uid > 0:
		var pet_data = _get_selected_pet_data()
		var current_main_skill = int(_get_val(pet_data, "main_skill_id", "MainSkillID", 0))
		var send_skill_id = skill_id
		if current_main_skill == skill_id:
			send_skill_id = 0

		NetworkManager.send_pet_action("SetMainSkill", selected_pet_uid, {"SkillID": send_skill_id})

func _show_skill_tooltip(skill_id: int, level: int, icon_tex: Texture2D) -> void :
	if GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):
		GameManager.current_hud.shared_tooltip.show_skill(skill_id, level, icon_tex)

func _hide_skill_tooltip() -> void :
	if GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):
		GameManager.current_hud.shared_tooltip.hide_tooltip()

func _clear_ui() -> void :
	_last_rendered_pet_uid = -1
	_last_rendered_evol_id = -1

	if lbl_pet_name: lbl_pet_name.text = "無寵物"
	if lbl_description: lbl_description.text = ""

	if doll_container:
		for c in doll_container.get_children(): c.queue_free()
	if list_skill:
		for c in list_skill.get_children(): c.queue_free()

func _on_add_stat_pressed(stat_type: String) -> void :
	if selected_pet_uid <= 0: return
	NetworkManager.send_pet_action("AllocateStat", selected_pet_uid, {"StatType": stat_type})

func _on_btn_edit_name_pressed() -> void :
	if selected_pet_uid <= 0: return

	if GameManager and is_instance_valid(GameManager.current_hud) and GameManager.current_hud.has_method("show_input_box"):
		GameManager.current_hud.show_input_box(
			"變更寵物名稱", 
			func(new_name: String):
				var clean_name = new_name.strip_edges()
				if clean_name == "":
					if GameManager.current_hud.has_method("show_rejection_message_box"):
						GameManager.current_hud.show_rejection_message_box("寵物名稱不可為空白！")
					return

				NetworkManager.send_pet_action("Rename", selected_pet_uid, {"NewName": clean_name}), 
			Callable(), 
			"請輸入新寵物名稱", 
			8
		)

func _on_btn_summon_pressed() -> void :
	if selected_pet_uid <= 0: return
	NetworkManager.send_pet_action("Summon", selected_pet_uid)

func _on_btn_unsummon_pressed() -> void :
	if selected_pet_uid <= 0: return
	NetworkManager.send_pet_action("Recall", selected_pet_uid)


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

func _on_btn_add_life_pressed() -> void :
	if selected_pet_uid <= 0: return
	NetworkManager.send_pet_action("AddLife", selected_pet_uid)

func _process(_delta: float) -> void :
	if not visible or not feed_window or not feed_window.visible: return

	var pet_data = _get_selected_pet_data()
	var state = int(_get_val(pet_data, "state", "State", 0))


	if state == 1:
		_update_feed_value_display(pet_data)

func _on_btn_reset_attr_pressed() -> void :
	if selected_pet_uid <= 0: return


	if GameManager and is_instance_valid(GameManager.current_hud) and GameManager.current_hud.has_method("show_message_box"):
		GameManager.current_hud.show_message_box(
			"確定要重置這隻寵物的屬性配點嗎？\n(將會消耗 1 個精靈再生水晶)", 
			2, 
			func():

				NetworkManager.send_pet_action("ResetStat", selected_pet_uid)
		)
	else:

		NetworkManager.send_pet_action("ResetStat", selected_pet_uid)
