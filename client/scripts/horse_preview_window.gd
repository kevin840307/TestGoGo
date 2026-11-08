
extends Control

@onready var doll_container = $DollContainer
@onready var lbl_name = $LblName
@onready var lbl_type = $LblType
@onready var lbl_level = $LblLevel
@onready var lbl_battery = $LblBattery
@onready var lbl_hp = $LblHP
@onready var lbl_mp = $LblMP
@onready var lbl_atk = $LblAtk
@onready var lbl_def = $LblDef
@onready var lbl_matk = $LblMAtk
@onready var lbl_mdef = $LblMDef
@onready var lbl_hit = $LblHit
@onready var lbl_dodge = $LblDodge
@onready var lbl_move_speed = $LblMoveSpeed
@onready var lbl_atk_speed = $LblAtkSpeed
@onready var lbl_total_atk = $LblTotalAtk
@onready var lbl_total_def = $LblTotalDef
@onready var list_horse_equip: VBoxContainer = $ScrollContainer / ListHorseEquip
@onready var list_horse_equip_card: VBoxContainer = $ScrollContainer2 / ListHorseEquipCard
@onready var list_horse_battery: VBoxContainer = $ScrollContainer3 / ListHorseBattery
@onready var bar_energy: AnimatedResourceBar = $BarEnergy

@onready var btn_ride = $BtnRide
@onready var btn_leave = $BtnLeave
@onready var btn_close = $BtnClose
@onready var btn_body = $BtnBody
@onready var btn_weapon = $BtnWeapon
@onready var btn_engine = $BtnEngine
@onready var btn_card1 = $BtnCard1
@onready var btn_card2 = $BtnCard2
@onready var btn_card3 = $BtnCard3
@onready var btn_stone = $BtnStone

@onready var _equip_buttons_map = {
	0: btn_body, 
	1: btn_weapon, 
	2: btn_engine, 
	3: btn_card1, 
	4: btn_card2, 
	5: btn_card3, 
	6: btn_stone
}


const INVENTORY_ROW_SCENE = preload("res://inventory_item_row.tscn")
const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}
const ROYAL_TERRITORY_WAR_MAP_ID: = 67
var _palette_mat_cache: Dictionary = {}

var _local_energy: float = -1.0
var _drive_request_pending: bool = false
var _requested_drive_horse_uid: int = -2

var target_player: CharacterBody2D = null
var current_horse_data: Dictionary = {}
var paper_doll: PaperDollEngine = null

func _ready() -> void :
	visibility_changed.connect(_on_visibility_changed)
	set_process(is_visible_in_tree())
	if btn_close: btn_close.pressed.connect(hide)
	if btn_ride: btn_ride.pressed.connect(_on_btn_ride_pressed)
	if btn_leave: btn_leave.pressed.connect(_on_btn_leave_pressed)
	paper_doll = PaperDollEngine.new()
	if doll_container:
		doll_container.add_child(paper_doll)
		paper_doll.position = Vector2(100, 170)

	if NetworkManager:
		if not NetworkManager.horse_synced.is_connected(_on_horse_synced):
			NetworkManager.horse_synced.connect(_on_horse_synced)
		if not NetworkManager.inventory_synced.is_connected(_on_inventory_synced):
			NetworkManager.inventory_synced.connect(_on_inventory_synced)
		if not NetworkManager.sync_mecha_energy_received.is_connected(_on_sync_mecha_energy):
			NetworkManager.sync_mecha_energy_received.connect(_on_sync_mecha_energy)
		if not NetworkManager.drive_mecha_response_received.is_connected(_on_drive_mecha_response):
			NetworkManager.drive_mecha_response_received.connect(_on_drive_mecha_response)

func _on_visibility_changed() -> void :
	set_process(is_visible_in_tree())

func _on_horse_synced(_horses: Array, _equips: Array, _max_size: int) -> void :
	if visible: refresh_data()

func _on_inventory_synced(_items: Array, _max_size: int) -> void :
	if visible: refresh_data()

func refresh_data() -> void :
	if not target_player or not "data_model" in target_player: return
	var p_data = target_player.data_model
	var h_uid = current_horse_data.get("horse_uid", -1)


	if h_uid != -1:
		for h in p_data.horse_list:
			if h.get("horse_uid", -1) == h_uid:
				current_horse_data = h
				break

	_update_info()
	_build_preview_model()
	_update_buttons()
	_render_inventory_lists()
	_render_equipped_slots()

func _on_gui_input(event: InputEvent) -> void :
	if event is InputEventMouseButton:
		accept_event()

func setup(player: CharacterBody2D, horse_data: Dictionary) -> void :
	target_player = player
	current_horse_data = horse_data
	visible = true

	_update_info()
	_build_preview_model()
	_update_buttons()
	_render_inventory_lists()
	_render_equipped_slots()


func _render_inventory_lists() -> void :
	if not target_player or not "data_model" in target_player: return
	var p_data = target_player.data_model
	var horse_level = current_horse_data.get("level", 1)


	for child in list_horse_equip.get_children(): child.queue_free()
	for child in list_horse_equip_card.get_children(): child.queue_free()
	for child in list_horse_battery.get_children(): child.queue_free()

	var c_yellow = Color(1.0, 0.941, 0.176, 1.0)
	var c_white = Color.WHITE


	var equip_items = []
	for heq in p_data.horse_equip_list:
		if heq.get("in_bank", 0) == 0 and heq.get("horse_uid", -1) == -1:
			equip_items.append(heq)

	equip_items.sort_custom( func(a, b): return a.get("equip_idx", 0) < b.get("equip_idx", 0))

	for heq in equip_items:
		var e_idx = heq.get("equip_idx", 0)

		var part = heq.get("part", ItemLoader.get_item_property(e_idx, "equip_slot", 0))
		var req_level = ItemLoader.get_item_property(e_idx, "req_level", 0)

		var target_list = null
		if part in [0, 1, 2, 6]:
			target_list = list_horse_equip
		elif part in [3, 4, 5]:
			target_list = list_horse_equip_card

		if target_list:
			var row = INVENTORY_ROW_SCENE.instantiate()
			target_list.add_child(row)

			var base_name = ItemLoader.get_item_property(e_idx, "item_name", "未知道具")
			var custom_name = heq.get("name", "")
			var item_name = base_name
			if custom_name != "":
				item_name = base_name.replace("%s", custom_name) if "%s" in base_name else custom_name

			var icon_id = ItemLoader.get_item_property(e_idx, "icon_id", 1) - 1
			var tex = ItemLoader.get_icon(icon_id)
			var color_index = heq.get("color", ItemLoader.get_item_property(e_idx, "color_index", 0))
			var effect_type = int(heq.get("EffectType", heq.get("effect_type", ItemLoader.get_item_property(e_idx, "effect_type", 0))))
			var mat = _get_palette_mat(color_index, effect_type)


			var text_color = c_white if req_level > horse_level else c_yellow

			row.setup(tex, mat, "Lv." + str(req_level), item_name, text_color, text_color)
			row.mouse_filter = Control.MOUSE_FILTER_PASS
			row.mouse_entered.connect(_show_item_tooltip.bind(e_idx, 3, heq))
			row.mouse_exited.connect(_hide_item_tooltip)
			row.gui_input.connect(_on_equip_row_clicked.bind(heq, req_level <= horse_level))


	var battery_items = []
	for inv in p_data.inventory:
		if inv.get("in_bank", 0) == 0:
			var idx = inv.get("item_index", 0)

			var battery_val = ItemLoader.get_item_property(idx, "battery", 0)
			if battery_val > 0:
				battery_items.append(inv)

	battery_items.sort_custom( func(a, b): return a.get("item_index", 0) < b.get("item_index", 0))

	for bat in battery_items:
		var row = INVENTORY_ROW_SCENE.instantiate()
		list_horse_battery.add_child(row)

		var b_idx = bat.get("item_index", 0)
		var count = bat.get("count", 1)
		var item_name = ItemLoader.get_item_property(b_idx, "item_name", "未知電池")
		var icon_id = ItemLoader.get_item_property(b_idx, "icon_id", 1) - 1
		var tex = ItemLoader.get_icon(icon_id)
		var color_index = ItemLoader.get_item_property(b_idx, "color_index", 0)
		var effect_type = int(bat.get("EffectType", bat.get("effect_type", ItemLoader.get_item_property(b_idx, "effect_type", 0))))
		var mat = _get_palette_mat(color_index, effect_type)

		row.setup(tex, mat, "x " + str(count), item_name, c_yellow, c_yellow)
		row.mouse_filter = Control.MOUSE_FILTER_PASS
		row.mouse_entered.connect(_show_item_tooltip.bind(b_idx, 0, bat))
		row.mouse_exited.connect(_hide_item_tooltip)
		row.gui_input.connect(_on_battery_row_clicked.bind(bat))


func _render_equipped_slots() -> void :
	if not target_player or not "data_model" in target_player: return
	var p_data = target_player.data_model
	var h_uid = current_horse_data.get("horse_uid", -1)


	for part in _equip_buttons_map.keys():
		var target_btn = _equip_buttons_map[part]
		if not target_btn: continue

		target_btn.texture_normal = null
		target_btn.material = null

		for conn in target_btn.mouse_entered.get_connections():
			target_btn.mouse_entered.disconnect(conn.callable)
		for conn in target_btn.mouse_exited.get_connections():
			target_btn.mouse_exited.disconnect(conn.callable)
		for conn in target_btn.gui_input.get_connections():
			target_btn.gui_input.disconnect(conn.callable)


	var equipped_map = {}
	for heq in p_data.horse_equip_list:
		if heq.get("horse_uid", -1) == h_uid and heq.get("in_bank", 0) == 0:
			var e_idx = heq.get("equip_idx", 0)

			var part = heq.get("part", ItemLoader.get_item_property(e_idx, "equip_slot", -1))
			equipped_map[part] = heq


	for part in _equip_buttons_map.keys():
		if equipped_map.has(part):
			var target_btn = _equip_buttons_map[part]
			var eq_data = equipped_map[part]
			var e_idx = eq_data.get("equip_idx", 0)

			var icon_id = int(ItemLoader.get_item_property(e_idx, "icon_id", 1)) - 1
			var tex = ItemLoader.get_icon(icon_id)
			var color_idx = int(eq_data.get("color", ItemLoader.get_item_property(e_idx, "color_index", 0)))
			var effect_type = int(eq_data.get("EffectType", eq_data.get("effect_type", ItemLoader.get_item_property(e_idx, "effect_type", 0))))
			var mat = _get_palette_mat(color_idx, effect_type)

			target_btn.texture_normal = tex
			if mat: target_btn.material = mat


			target_btn.mouse_entered.connect(_show_item_tooltip.bind(e_idx, 3, eq_data))
			target_btn.mouse_exited.connect(_hide_item_tooltip)

			target_btn.gui_input.connect(_on_equipped_slot_clicked.bind(eq_data))

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

func _on_equip_row_clicked(event: InputEvent, heq_data: Dictionary, is_equippable: bool) -> void :
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if not is_equippable:
			if GameManager.current_hud:
				GameManager.current_hud.show_rejection_message_flash("機甲等級不足，無法裝備！")
			return


		var h_type = current_horse_data.get("horse_type", "MS")
		var eq_idx = heq_data.get("equip_idx", 0)
		var eq_type = ItemLoader.get_item_property(eq_idx, "horse_type", "MS")

		if h_type != eq_type:
			if GameManager.current_hud:
				if eq_type == "MS":
					GameManager.current_hud.show_rejection_message_flash("此裝備無法裝備於 [color=#FFF02D]形體系[/color] 機甲！")
				else:
					GameManager.current_hud.show_rejection_message_flash("此裝備無法裝備於 [color=#FFF02D]機體系[/color] 機甲！")
			return

		var h_uid = current_horse_data.get("horse_uid", -1)
		var eq_uid = heq_data.get("equip_uid", -1)

		if target_player and "data_model" in target_player and target_player.data_model.summoned_horse_uid == h_uid:
			if GameManager.current_hud:
				GameManager.current_hud.show_rejection_message_flash("駕駛中的機甲無法變更裝備，請先解除駕駛！")
			return

		if h_uid != -1 and eq_uid != -1:
			if NetworkManager:
				NetworkManager.send_secure_packet({
					"Type": "HorseEquipRequest", 
					"EquipUID": eq_uid, 
					"HorseUID": h_uid
				})

func _on_battery_row_clicked(event: InputEvent, battery_data: Dictionary) -> void :
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if not target_player or not "data_model" in target_player: return

		var b_idx = battery_data.get("item_index", 0)
		var battery_val = 0
		if ItemLoader: battery_val = ItemLoader.get_item_property(b_idx, "battery", 0)

		var h_idx = current_horse_data.get("horse_idx", 0)
		var max_battery = 0
		if ItemLoader: max_battery = ItemLoader.get_item_property(h_idx, "max_battery", 0)

		var energy_left = current_horse_data.get("energy_left", 0)


		if energy_left + battery_val > max_battery:
			if GameManager.current_hud:
				GameManager.current_hud.show_rejection_message_flash("機甲電量已滿或超出上限，無法使用電池！")
			return


		var item_uid = battery_data.get("item_uid", -1)
		var h_uid = current_horse_data.get("horse_uid", -1)

		if h_uid != -1 and item_uid != -1:
			if NetworkManager:
				NetworkManager.send_secure_packet({
					"Type": "HorseChargeRequest", 
					"HorseUID": h_uid, 
					"ItemUID": item_uid
				})

func _update_info() -> void :
	var h_uid = current_horse_data.get("horse_uid", 0)
	var h_idx = current_horse_data.get("horse_idx", 0)

	if lbl_name: lbl_name.text = current_horse_data.get("name", "未知機甲")
	if lbl_type:
		var mecha_type = current_horse_data.get("horse_type", "MS")
		lbl_type.text = "機體系" if mecha_type == "MS" else "形體系"
	if lbl_level: lbl_level.text = "%d" % current_horse_data.get("level", 1)

	var max_battery = 0
	if ItemLoader: max_battery = ItemLoader.get_item_property(h_idx, "max_battery", 0)
	var current_battery = current_horse_data.get("energy_left", 0)
	_local_energy = float(current_battery)

	if lbl_battery: lbl_battery.text = "%d / %d" % [current_horse_data.get("energy_left", 0), max_battery]
	if bar_energy:
		bar_energy.max_value = max_battery
		bar_energy.set_value_immediate(current_battery)


	var total_hp = 0; var total_mp = 0; var total_sp = 0
	var total_atk = 0; var total_def = 0
	var total_matk = 0; var total_mdef = 0
	var total_hit = 0; var total_dodge = 0
	var total_atk_spd = 0; var total_mov_spd = 0




	if target_player and "data_model" in target_player and target_player.data_model:

		for heq in target_player.data_model.horse_equip_list:
			if heq.get("horse_uid", 0) == h_uid and heq.get("in_bank", 0) == 0:
				var eq_idx = heq.get("equip_idx", 0)
				if ItemLoader:

					total_hp += ItemLoader.get_item_property(eq_idx, "max_hp_bonus", 0)
					total_mp += ItemLoader.get_item_property(eq_idx, "max_mp_bonus", 0)
					total_sp += ItemLoader.get_item_property(eq_idx, "max_sp_bonus", 0)
					total_atk += ItemLoader.get_item_property(eq_idx, "atk", 0)
					total_def += ItemLoader.get_item_property(eq_idx, "def", 0)
					total_matk += ItemLoader.get_item_property(eq_idx, "matk", 0)
					total_mdef += ItemLoader.get_item_property(eq_idx, "mdef", 0)
					total_hit += ItemLoader.get_item_property(eq_idx, "hit", 0)
					total_dodge += ItemLoader.get_item_property(eq_idx, "flee", 0)
					total_atk_spd += ItemLoader.get_item_property(eq_idx, "aspd", 0)
					total_mov_spd += ItemLoader.get_item_property(eq_idx, "move_speed", 0)


	if lbl_hp: lbl_hp.text = "+%d" % total_hp
	if lbl_mp: lbl_mp.text = "+%d" % total_mp
	if lbl_atk: lbl_atk.text = "+%d" % total_atk
	if lbl_def: lbl_def.text = "+%d" % total_def
	if lbl_matk: lbl_matk.text = "+%d" % total_matk
	if lbl_mdef: lbl_mdef.text = "+%d" % total_mdef
	if lbl_hit: lbl_hit.text = "+%d" % total_hit
	if lbl_dodge: lbl_dodge.text = "+%d" % total_dodge
	if lbl_atk_speed: lbl_atk_speed.text = "+%d%%" % total_atk_spd
	if lbl_move_speed: lbl_move_speed.text = "+%d%%" % total_mov_spd


	if lbl_total_atk: lbl_total_atk.text = "+%d" % (total_atk + total_matk)
	if lbl_total_def: lbl_total_def.text = "+%d" % (total_def + total_mdef)

func _build_preview_model() -> void :
	if not paper_doll or not target_player or not "data_model" in target_player: return
	paper_doll.position = Vector2(100, 170)

	var h_uid = current_horse_data.get("horse_uid", 0)
	var p_data = target_player.data_model

	var mecha_type = current_horse_data.get("horse_type", "MS")
	var h_idx = current_horse_data.get("horse_idx", 0)

	var body_id = 0
	var leg_id = 0
	var weapon_id = 0


	for heq in p_data.horse_equip_list:
		if heq.get("horse_uid", -1) == h_uid and heq.get("in_bank", 0) == 0:
			var e_idx = heq.get("equip_idx", 0)
			var part = heq.get("part", 0)
			if part == 0: body_id = e_idx
			elif part == 1: weapon_id = e_idx
			elif part == 2: leg_id = e_idx


	if mecha_type == "MA" and body_id == 0:
		body_id = h_idx
		if ItemLoader:
			var ma_body_id = ItemLoader.get_item_property(h_idx, "body_id", 0)
			if ma_body_id > 0:
				body_id = ma_body_id

	paper_doll.build_mecha_model(
		target_player.current_race, 
		target_player.current_hair, 
		target_player.current_hair_color_index, 
		p_data.bonnet_id, 
		body_id, 
		leg_id, 
		weapon_id, 
		mecha_type
	)

	paper_doll.update_animation("idle", "1", false, 100.0, 100.0)
	if mecha_type == "MA" and doll_container:
		var vertical_bounds: = _get_ma_idle_preview_vertical_bounds()
		if vertical_bounds != Vector2.ZERO:
			var bounds_center_y: = (vertical_bounds.x + vertical_bounds.y) * 0.5
			paper_doll.position.y = doll_container.size.y * 0.5 - bounds_center_y

func _get_ma_idle_preview_vertical_bounds() -> Vector2:
	if not paper_doll or not paper_doll.doll_sprites.has("MSBody"):
		return Vector2.ZERO

	var body_sprite = paper_doll.doll_sprites["MSBody"] as AnimatedSprite2D
	if not body_sprite or not body_sprite.sprite_frames:
		return Vector2.ZERO

	const IDLE_ANIMATION: = "idle_1"
	if not body_sprite.sprite_frames.has_animation(IDLE_ANIMATION):
		return Vector2.ZERO

	var frame_count: int = body_sprite.sprite_frames.get_frame_count(IDLE_ANIMATION)
	if frame_count <= 0:
		return Vector2.ZERO

	var minimum_y: = INF
	var maximum_y: = - INF
	for frame_index in frame_count:
		var frame_texture: Texture2D = body_sprite.sprite_frames.get_frame_texture(IDLE_ANIMATION, frame_index)
		if not frame_texture:
			continue

		var old_offset: = Vector2.ZERO
		if frame_texture.has_meta("old_offset"):
			old_offset = frame_texture.get_meta("old_offset")
		var frame_top: float = - old_offset.y
		var frame_bottom: float = frame_top + frame_texture.get_height()
		minimum_y = minf(minimum_y, frame_top)
		maximum_y = maxf(maximum_y, frame_bottom)

	if not is_finite(minimum_y) or not is_finite(maximum_y) or maximum_y <= minimum_y:
		return Vector2.ZERO

	return Vector2(minimum_y, maximum_y)

func _update_buttons() -> void :
	if not target_player or not "data_model" in target_player: return
	var h_uid = current_horse_data.get("horse_uid", 0)
	var is_riding = (target_player.data_model.summoned_horse_uid == h_uid)

	if btn_ride: btn_ride.visible = not is_riding
	if btn_leave: btn_leave.visible = is_riding

func _on_btn_ride_pressed() -> void :
	if not target_player or _drive_request_pending:
		return

	if "data_model" in target_player and target_player.data_model and int(target_player.data_model.map_id) == ROYAL_TERRITORY_WAR_MAP_ID:
		if GameManager.current_hud:
			GameManager.current_hud.show_rejection_message_flash("皇家領地戰區域內禁止駕駛機甲！")
		return

	if target_player.get("is_dead"):
		if GameManager.current_hud and GameManager.current_hud.has_method("show_system_message_flash"):
			GameManager.current_hud.show_rejection_message_flash("角色死亡狀態下無法駕駛機甲！")
		return

	if "data_model" in target_player and target_player.data_model:
		if target_player.data_model.summoned_horse_uid != -1:
			if GameManager.current_hud:
				GameManager.current_hud.show_rejection_message_flash("請先解除目前駕駛的機甲！")
			return

	var h_uid = int(current_horse_data.get("horse_uid", 0))
	var energy_left = int(current_horse_data.get("energy_left", 0))

	if h_uid <= 0:
		return
	if energy_left <= 0:
		if GameManager.current_hud:
			GameManager.current_hud.show_rejection_message_flash("機甲電量不足，無法駕駛！")
		return

	_begin_drive_request(h_uid)


func _on_btn_leave_pressed() -> void :
	if not target_player or _drive_request_pending:
		return
	_begin_drive_request(-1)


func _begin_drive_request(horse_uid: int) -> void :
	if not NetworkManager or not NetworkManager.is_connected_to_server:
		return
	_drive_request_pending = true
	_requested_drive_horse_uid = horse_uid
	if btn_ride:
		btn_ride.disabled = true
	if btn_leave:
		btn_leave.disabled = true
	NetworkManager.send_drive_mecha_request(horse_uid)


func _on_drive_mecha_response(success: bool, horse_uid: int, _message: String) -> void :
	if not _drive_request_pending:
		return

	# Only consume the acknowledgement for the operation this window sent.
	if success and horse_uid != _requested_drive_horse_uid:
		return

	_drive_request_pending = false
	_requested_drive_horse_uid = -2
	if btn_ride:
		btn_ride.disabled = false
	if btn_leave:
		btn_leave.disabled = false

	if not success:
		# The local model was never changed, so refreshing restores the exact
		# authoritative pre-request state without a visual rollback race.
		refresh_data()
		return

	if is_instance_valid(target_player):
		target_player.execute_mecha_transition(func():
			target_player.setup_layered_paper_dolls()
			target_player.sync_animation()
			_update_buttons()
		)

	var hud = GameManager.current_hud if GameManager else null
	if hud and hud.has_method("_refresh_inventory_if_open"):
		hud._refresh_inventory_if_open()

	hide()


func _on_sync_mecha_energy(h_uid: int, energy: int, exp: int, max_exp: int, level: int) -> void :
	if not visible: return
	if current_horse_data.get("horse_uid", -1) == h_uid:
		current_horse_data["energy_left"] = energy
		current_horse_data["exp"] = exp
		current_horse_data["max_exp"] = max_exp
		current_horse_data["level"] = level
		_update_info()

func _show_item_tooltip(item_index: int, type: int, item_data: Dictionary = {}) -> void :
	if GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):

		var data_copy = item_data.duplicate()
		data_copy["preview_target_horse_uid"] = current_horse_data.get("horse_uid", -1)

		GameManager.current_hud.shared_tooltip.show_item(item_index, type, data_copy)

func _hide_item_tooltip() -> void :
	if GameManager.current_hud and GameManager.current_hud.get("shared_tooltip"):
		GameManager.current_hud.shared_tooltip.hide_tooltip()


func _on_equipped_slot_clicked(event: InputEvent, heq_data: Dictionary) -> void :

	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:


		var part = heq_data.get("part", -1)
		if part == -1:
			var e_idx = heq_data.get("equip_idx", 0)
			if ItemLoader:
				part = ItemLoader.get_item_property(e_idx, "equip_slot", -1)


		if part in [0, 1, 2]:

			if GameManager.current_hud:
				GameManager.current_hud.show_rejection_message_flash("機甲的主體、武裝與引擎無法直接卸下！")
			return

		var h_uid = current_horse_data.get("horse_uid", -1)
		var eq_uid = heq_data.get("equip_uid", -1)

		if target_player and "data_model" in target_player and target_player.data_model.summoned_horse_uid == h_uid:
			if GameManager.current_hud:
				GameManager.current_hud.show_rejection_message_flash("駕駛中的機甲無法變更裝備，請先解除駕駛！")
			return

		if h_uid != -1 and eq_uid != -1:
			if NetworkManager:
				NetworkManager.send_secure_packet({
					"Type": "HorseUnequipRequest", 
					"EquipUID": eq_uid, 
					"HorseUID": h_uid
				})

func _process(_delta: float) -> void :
	if not visible or not target_player or not "data_model" in target_player: return

	var h_uid = current_horse_data.get("horse_uid", -1)


	if target_player.data_model.summoned_horse_uid == h_uid:
		var energy = current_horse_data.get("energy_left", 0)
		var exact_energy = current_horse_data.get("exact_energy", float(energy))

		var max_battery = bar_energy.max_value if bar_energy else 0.0
		if lbl_battery: lbl_battery.text = "%d / %d" % [energy, max_battery]
		if bar_energy: bar_energy.value = exact_energy
