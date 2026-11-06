
extends PanelContainer

@onready var tooltip_icon: TextureRect = $MarginContainer / VBoxContainer / CenterContainer / TooltipIcon
@onready var tooltip_name_desc: RichTextLabel = $MarginContainer / VBoxContainer / TooltipNameDesc
@onready var tooltip_stats: RichTextLabel = $MarginContainer / VBoxContainer / TooltipStats

const PALETTE_SHADER = preload("res://palette_swap.gdshader")
const COLOR_INDEX_TO_DEGREES = {0: 0.0, 1: 90.0, 2: 180.0, 3: 270.0, 4: 45.0, 5: 135.0, 6: 225.0, 7: 315.0}

func _ready() -> void :
	visible = false
	visibility_changed.connect(_on_visibility_changed)
	set_process(false)
	top_level = true
	z_index = 100
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	for child in find_children("*", "Control", true, false):
		child.mouse_filter = Control.MOUSE_FILTER_IGNORE

func _on_visibility_changed() -> void :
	set_process(is_visible_in_tree())


func _process(_delta: float) -> void :
	if visible:
		var mouse_pos = get_global_mouse_position()
		var tooltip_size = size
		var viewport_size = get_viewport_rect().size
		var offset = Vector2(20, 20)
		var target_pos = mouse_pos + offset

		if target_pos.x + tooltip_size.x > viewport_size.x: target_pos.x = mouse_pos.x - tooltip_size.x - offset.x
		if target_pos.y + tooltip_size.y > viewport_size.y: target_pos.y = mouse_pos.y - tooltip_size.y - offset.y
		global_position = target_pos

func hide_tooltip() -> void :
	visible = false


func show_item(item_index: int, tab_type: int, item_data: Dictionary = {}) -> void :
	if not tooltip_icon or not tooltip_name_desc or not tooltip_stats: return



	if tab_type == 2:
		_update_pet_tooltip(item_index, item_data)
		size = Vector2.ZERO
		visible = true
		return

	var icon_id = ItemLoader.get_item_property(item_index, "icon_id", 1) - 1
	var tex = ItemLoader.get_photo(icon_id)

	var mat = null
	var color_index = int(item_data.get("Color", item_data.get("color", ItemLoader.get_item_property(item_index, "color_index", 0))))
	var effect_type = int(item_data.get("EffectType", item_data.get("effect_type", ItemLoader.get_item_property(item_index, "effect_type", 0))))

	if color_index > 0 or effect_type > 0:
		mat = ShaderMaterial.new()
		mat.shader = PALETTE_SHADER
		mat.set_shader_parameter("effect_type", effect_type)
		mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))

	tooltip_icon.texture = tex
	tooltip_icon.material = mat

	match tab_type:
		0: _update_general_tooltip(item_index, item_data)
		1: _update_equip_tooltip(item_index, item_data)
		3: _update_mecha_tooltip(item_index, item_data)


	size = Vector2.ZERO
	visible = true


func show_skill(skill_id: int, level: int, icon_tex: Texture2D) -> void :
	if not tooltip_icon or not tooltip_name_desc or not tooltip_stats: return
	tooltip_icon.texture = icon_tex
	tooltip_icon.material = null

	var s_res = null
	if SkillLoader:
		s_res = SkillLoader.get_skill_data(skill_id)

	if not s_res: return

	var nd_info = "[center][font_size=15][color=#FFFFFF]%s[/color][/font_size][/center]\n" % s_res.skill_name
	if s_res.description != "": nd_info += "\n[color=#FFFFFF]%s[/color]\n" % s_res.description
	tooltip_name_desc.text = nd_info

	var stat_info = ""
	var type_str = "未知"
	match s_res.skill_type:
		0: type_str = "單體攻擊"
		1: type_str = "範圍攻擊"
		2: type_str = "單體治療"
		3: type_str = "群體治療"
		4, 5: type_str = "狀態增益"
		6: type_str = "復活"
		7: type_str = "特殊功能"
	stat_info += "[color=#FFFFFF]技能類型：[/color]%s\n" % type_str

	var level_data = s_res.get_level_data(level)
	if level_data:
		if level_data.cost_hp > 0: stat_info += "[color=#FFFFFF]消耗HP：[/color]%d\n" % level_data.cost_hp
		if level_data.cost_mp > 0: stat_info += "[color=#FFFFFF]消耗MP：[/color]%d\n" % level_data.cost_mp
		if level_data.cost_sp > 0: stat_info += "[color=#FFFFFF]消耗SP：[/color]%d\n" % level_data.cost_sp
		if level_data.cast_range > 0: stat_info += "[color=#FFFFFF]施放距離：[/color]%d\n" % level_data.cast_range
		if level_data.aoe_radius > 0: stat_info += "[color=#FFFFFF]技能範圍：[/color]%d\n" % level_data.aoe_radius
		if level_data.cooldown > 0: stat_info += "[color=#FFFFFF]冷卻時間：[/color]%d 秒\n" % level_data.cooldown
		if level_data.duration > 0: stat_info += "[color=#FFFFFF]持續時間：[/color]%d 秒\n" % level_data.duration


		var buff_info = ""
		var build_buff_line = func(stat_name, val, is_pct = false):
			if val == 0: return ""
			var pct_str = "%" if is_pct else ""
			var val_str = ("+%d%s" % [val, pct_str]) if val > 0 else ("%d%s" % [val, pct_str])
			return "[color=#FFFFFF]%s：[/color]%s\n" % [stat_name, val_str]

		buff_info += build_buff_line.call("生命上限", level_data.max_hp_bonus)
		buff_info += build_buff_line.call("魔力上限", level_data.max_mp_bonus)
		buff_info += build_buff_line.call("意志上限", level_data.max_sp_bonus)
		buff_info += build_buff_line.call("攻擊", level_data.atk_bonus)
		buff_info += build_buff_line.call("防禦", level_data.def_bonus)
		buff_info += build_buff_line.call("魔攻", level_data.matk_bonus)
		buff_info += build_buff_line.call("魔防", level_data.mdef_bonus)
		buff_info += build_buff_line.call("命中", level_data.hit_bonus)
		buff_info += build_buff_line.call("迴避", level_data.flee_bonus)
		buff_info += build_buff_line.call("幸運", level_data.luk_bonus)
		buff_info += build_buff_line.call("攻速", level_data.aspd_bonus)
		buff_info += build_buff_line.call("移速", level_data.move_speed_bonus)
		buff_info += build_buff_line.call("暴擊率", level_data.crit_rate_bonus, true)
		buff_info += build_buff_line.call("暴擊傷害", level_data.crit_damage_bonus, true)

		if buff_info != "":
			stat_info += "\n" + buff_info

	tooltip_stats.text = stat_info
	size.y = 0
	visible = true


func show_shortcut(type: String, target_id: int, tex: Texture2D, mat: Material = null) -> void :
	if not tooltip_icon or not tooltip_name_desc or not tooltip_stats: return
	tooltip_icon.texture = tex
	tooltip_icon.material = mat

	if type == "ITEM":
		var cat = 0
		if ItemLoader:
			cat = ItemLoader.get_item_category(target_id)

		if cat == 1:
			_update_equip_tooltip(target_id, {})
		else:
			_update_general_tooltip(target_id, {})

		size.y = 0
		visible = true
		return

	elif type == "MECHA":
		var p_data = GameManager.current_player.data_model if GameManager and is_instance_valid(GameManager.current_player) and "data_model" in GameManager.current_player else null
		var horse_data = null
		if p_data and "horse_list" in p_data:
			for horse in p_data.horse_list:
				if int(horse.get("horse_uid", horse.get("HorseUID", -1))) == target_id:
					horse_data = horse
					break

		if horse_data:
			var horse_idx = int(horse_data.get("horse_idx", horse_data.get("HorseIdx", 0)))
			_update_mecha_tooltip(horse_idx, horse_data)
			size.y = 0
			visible = true
		return

	elif type == "PET":
		var p_data = GameManager.current_player.data_model if GameManager and is_instance_valid(GameManager.current_player) and "data_model" in GameManager.current_player else null
		var pet_data = null
		if p_data and "pet_list" in p_data:
			for p in p_data.pet_list:
				if int(p.get("uid", p.get("UID", 0))) == target_id:
					pet_data = p
					break

		if pet_data:
			var pet_id = int(pet_data.get("pet_id", pet_data.get("PETID", 0)))
			_update_pet_tooltip(pet_id, pet_data)
			size.y = 0
			visible = true
		return

	var nd_info = ""
	var stat_info = ""

	if type == "SKILL":
		if SkillLoader:
			var s_res = SkillLoader.get_skill_data(target_id)
			if s_res:
				nd_info = "[center][font_size=15][color=#FFFFFF]%s[/color][/font_size][/center]\n" % s_res.skill_name
				if s_res.description != "": nd_info += "\n[color=#FFFFFF]%s[/color]\n" % s_res.description

				var p_level = 1
				var p_data = GameManager.current_player.data_model if GameManager and is_instance_valid(GameManager.current_player) and "data_model" in GameManager.current_player else null
				if p_data and "skill_list" in p_data:
					for s in p_data.skill_list:
						if s.get("skill_id", 0) == target_id:
							p_level = s.get("level", 1)
							break

				stat_info += "[color=#FFFFFF]目前等級：Lv.%d[/color]\n" % p_level
				var level_data = s_res.get_level_data(p_level)
				if level_data:
					if level_data.cost_hp > 0: stat_info += "[color=#FFFFFF]消耗HP：[/color]%d\n" % level_data.cost_hp
					if level_data.cost_mp > 0: stat_info += "[color=#FFFFFF]消耗MP：[/color]%d\n" % level_data.cost_mp
					if level_data.cost_sp > 0: stat_info += "[color=#FFFFFF]消耗SP：[/color]%d\n" % level_data.cost_sp
					if level_data.cooldown > 0: stat_info += "[color=#FFFFFF]冷卻時間：[/color]%d 秒\n" % level_data.cooldown
					if level_data.duration > 0: stat_info += "[color=#FFFFFF]持續時間：[/color]%d 秒\n" % level_data.duration

					var buff_info = ""
					var build_buff_line = func(stat_name, val, is_pct = false):
						if val == 0: return ""
						var pct_str = "%" if is_pct else ""
						var val_str = ("+%d%s" % [val, pct_str]) if val > 0 else ("%d%s" % [val, pct_str])
						return "[color=#FFFFFF]%s：[/color]%s\n" % [stat_name, val_str]

					buff_info += build_buff_line.call("生命上限", level_data.max_hp_bonus)
					buff_info += build_buff_line.call("魔力上限", level_data.max_mp_bonus)
					buff_info += build_buff_line.call("意志上限", level_data.max_sp_bonus)
					buff_info += build_buff_line.call("攻擊", level_data.atk_bonus)
					buff_info += build_buff_line.call("防禦", level_data.def_bonus)
					buff_info += build_buff_line.call("魔攻", level_data.matk_bonus)
					buff_info += build_buff_line.call("魔防", level_data.mdef_bonus)
					buff_info += build_buff_line.call("命中", level_data.hit_bonus)
					buff_info += build_buff_line.call("迴避", level_data.flee_bonus)
					buff_info += build_buff_line.call("幸運", level_data.luk_bonus)
					buff_info += build_buff_line.call("攻速", level_data.aspd_bonus)
					buff_info += build_buff_line.call("移速", level_data.move_speed_bonus)
					buff_info += build_buff_line.call("暴擊率", level_data.crit_rate_bonus, true)
					buff_info += build_buff_line.call("暴擊傷害", level_data.crit_damage_bonus, true)

					if buff_info != "":
						stat_info += "\n" + buff_info

	tooltip_name_desc.text = nd_info
	tooltip_stats.text = stat_info
	size.y = 0
	visible = true


func show_buff(type: String, target_id: int, tex: Texture2D, mat: Material = null) -> void :
	if not tooltip_icon or not tooltip_name_desc or not tooltip_stats: return
	tooltip_icon.texture = tex
	tooltip_icon.material = mat

	if type == "ITEM":
		var cat = 0
		if ItemLoader:
			cat = ItemLoader.get_item_category(target_id)

		if cat == 1:
			_update_equip_tooltip(target_id, {})
		else:
			_update_general_tooltip(target_id, {})

		size.y = 0
		visible = true
		return

	var nd_info = ""
	var stat_info = ""

	if type == "SKILL" and SkillLoader:
		var s_res = SkillLoader.get_skill_data(target_id)
		if s_res:
			nd_info = "[center][font_size=15][color=#FFFFFF]%s[/color][/font_size][/center]\n" % s_res.skill_name
			if s_res.description != "": nd_info += "\n[color=#FFFFFF]%s[/color]\n" % s_res.description


			var p_level = 1
			var p_data = GameManager.current_player.data_model if GameManager and is_instance_valid(GameManager.current_player) and "data_model" in GameManager.current_player else null
			if p_data and "skill_list" in p_data:
				for s in p_data.skill_list:
					if s.get("skill_id", 0) == target_id:
						p_level = s.get("level", 1)
						break

			var level_data = s_res.get_level_data(p_level)
			if level_data:
				var buff_info = ""
				var build_buff_line = func(stat_name, val, is_pct = false):
					if val == 0: return ""
					var pct_str = "%" if is_pct else ""
					var val_str = ("+%d%s" % [val, pct_str]) if val > 0 else ("%d%s" % [val, pct_str])
					return "[color=#FFFFFF]%s：[/color]%s\n" % [stat_name, val_str]

				buff_info += build_buff_line.call("生命上限", level_data.max_hp_bonus)
				buff_info += build_buff_line.call("魔力上限", level_data.max_mp_bonus)
				buff_info += build_buff_line.call("意志上限", level_data.max_sp_bonus)
				buff_info += build_buff_line.call("攻擊", level_data.atk_bonus)
				buff_info += build_buff_line.call("防禦", level_data.def_bonus)
				buff_info += build_buff_line.call("魔攻", level_data.matk_bonus)
				buff_info += build_buff_line.call("魔防", level_data.mdef_bonus)
				buff_info += build_buff_line.call("命中", level_data.hit_bonus)
				buff_info += build_buff_line.call("迴避", level_data.flee_bonus)
				buff_info += build_buff_line.call("幸運", level_data.luk_bonus)
				buff_info += build_buff_line.call("攻速", level_data.aspd_bonus)
				buff_info += build_buff_line.call("移速", level_data.move_speed_bonus)
				buff_info += build_buff_line.call("暴擊率", level_data.crit_rate_bonus, true)
				buff_info += build_buff_line.call("暴擊傷害", level_data.crit_damage_bonus, true)

				if buff_info != "":
					stat_info += "\n" + buff_info

	tooltip_name_desc.text = nd_info
	tooltip_stats.text = stat_info
	size.y = 0
	visible = true


func _update_general_tooltip(item_index: int, item_data: Dictionary = {}) -> void :
	var item_name = ItemLoader.get_item_property(item_index, "item_name", "未知道具")
	var item_desc = ItemLoader.get_item_property(item_index, "description", "")
	var item_can_trade = ItemLoader.get_item_property(item_index, "can_trade", 0)
	var item_can_sell = ItemLoader.get_item_property(item_index, "can_sell", 0)
	var item_can_store = ItemLoader.get_item_property(item_index, "can_store", 0)
	var price = ItemLoader.get_item_property(item_index, "price", 0)
	var battery = ItemLoader.get_item_property(item_index, "battery", 0)
	var req_level = ItemLoader.get_item_property(item_index, "req_level", 0)
	var rec_hp = ItemLoader.get_item_property(item_index, "recover_hp", 0)
	var rec_mp = ItemLoader.get_item_property(item_index, "recover_mp", 0)
	var rec_sp = ItemLoader.get_item_property(item_index, "recover_sp", 0)

	var b_hp = ItemLoader.get_item_property(item_index, "max_hp_bonus", 0)
	var b_mp = ItemLoader.get_item_property(item_index, "max_mp_bonus", 0)
	var b_atk = ItemLoader.get_item_property(item_index, "atk_bonus", 0)
	var b_def = ItemLoader.get_item_property(item_index, "def_bonus", 0)
	var b_matk = ItemLoader.get_item_property(item_index, "matk_bonus", 0)
	var b_mdef = ItemLoader.get_item_property(item_index, "mdef_bonus", 0)
	var b_hit = ItemLoader.get_item_property(item_index, "hit_bonus", 0)
	var b_flee = ItemLoader.get_item_property(item_index, "flee_bonus", 0)
	var b_luk = ItemLoader.get_item_property(item_index, "luk_bonus", 0)
	var b_aspd = ItemLoader.get_item_property(item_index, "atk_speed_bonus", ItemLoader.get_item_property(item_index, "atk_speed", ItemLoader.get_item_property(item_index, "aspd", 0)))
	var b_move = ItemLoader.get_item_property(item_index, "move_speed_bonus", ItemLoader.get_item_property(item_index, "move_speed", 0))
	var b_crit_rate = ItemLoader.get_item_property(item_index, "crit_rate_bonus", ItemLoader.get_item_property(item_index, "crit_rate", 0))
	var b_crit_dmg = ItemLoader.get_item_property(item_index, "crit_damage_bonus", ItemLoader.get_item_property(item_index, "crit_damage", 0))

	var nd_info = "[center][font_size=15][color=#FFFFFF]%s[/color][/font_size][/center]\n" % item_name
	if item_desc != "": nd_info += "\n[color=#FFFFFF]%s[/color]\n" % item_desc

	var restrictions = []
	if item_can_trade == 0: restrictions.append("不可交易")
	if item_can_sell == 0: restrictions.append("不可出售")
	if item_can_store == 0: restrictions.append("不可存放倉庫")
	if restrictions.size() > 0: nd_info += "\n[color=#FFFFFF]其他資訊：%s[/color]\n" % "、".join(restrictions)
	if battery > 0: nd_info += "\n[color=#FFFFFF]電量：%d[/color]\n" % battery

	tooltip_name_desc.text = nd_info

	var stat_info = ""
	if req_level > 0: stat_info += "[color=#FFFFFF]等級限制：[/color]%d\n\n" % req_level
	if rec_hp > 0: stat_info += "[color=#FFFFFF]回復生命：[/color]+%d\n" % rec_hp
	if rec_mp > 0: stat_info += "[color=#FFFFFF]回復魔力：[/color]+%d\n" % rec_mp
	if rec_sp > 0: stat_info += "[color=#FFFFFF]回復意志：[/color]+%d\n" % rec_sp

	var build_stat_line = func(stat_name, val, is_pct = false):
		if val == 0: return ""
		var pct_str = "%" if is_pct else ""
		var val_str = ("+%d%s" % [val, pct_str]) if val > 0 else ("%d%s" % [val, pct_str])
		return "[color=#FFFFFF]%s：[/color]%s\n" % [stat_name, val_str]

	stat_info += build_stat_line.call("生命上限", b_hp)
	stat_info += build_stat_line.call("魔力上限", b_mp)
	stat_info += build_stat_line.call("攻擊", b_atk)
	stat_info += build_stat_line.call("防禦", b_def)
	stat_info += build_stat_line.call("魔攻", b_matk)
	stat_info += build_stat_line.call("魔防", b_mdef)
	stat_info += build_stat_line.call("命中", b_hit)
	stat_info += build_stat_line.call("迴避", b_flee)
	stat_info += build_stat_line.call("幸運", b_luk)
	stat_info += build_stat_line.call("攻速", b_aspd)
	stat_info += build_stat_line.call("移速", b_move)
	stat_info += build_stat_line.call("暴擊率", b_crit_rate, true)
	stat_info += build_stat_line.call("暴擊傷害", b_crit_dmg, true)

	if stat_info != "": stat_info += "\n"
	tooltip_stats.text = stat_info

func _update_equip_tooltip(item_index: int, item_data: Dictionary = {}) -> void :
	var item_name = ItemLoader.get_item_property(item_index, "item_name", "未知裝備")
	var req_level = ItemLoader.get_item_property(item_index, "req_level", 0)
	var req_race = ItemLoader.get_item_property(item_index, "req_race", "無")
	var req_job = ItemLoader.get_item_property(item_index, "req_job", "無")
	var special_effect = ItemLoader.get_item_property(item_index, "special_effect", "")
	var req_str = ItemLoader.get_item_property(item_index, "req_str", 0)
	var req_dex = ItemLoader.get_item_property(item_index, "req_dex", 0)
	var req_con = ItemLoader.get_item_property(item_index, "req_con", 0)
	var req_wis = ItemLoader.get_item_property(item_index, "req_wis", 0)
	var item_desc = ItemLoader.get_item_property(item_index, "description", "")
	var item_can_trade = ItemLoader.get_item_property(item_index, "can_trade", 0)
	var item_can_sell = ItemLoader.get_item_property(item_index, "can_sell", 0)
	var item_can_store = ItemLoader.get_item_property(item_index, "can_store", 0)
	var item_can_repair = ItemLoader.get_item_property(item_index, "can_repair", 1)
	var price = ItemLoader.get_item_property(item_index, "price", 0)
	var durability = ItemLoader.get_item_property(item_index, "durability", 0)


	var damage = int(item_data.get("Damage", item_data.get("damage", 0)))
	var target_player = GameManager.current_player if GameManager else null


	if damage == 0 and not item_data.has("Damage") and not item_data.has("damage"):
		if is_instance_valid(target_player) and "data_model" in target_player and target_player.data_model:
			var eq_list = target_player.data_model.equip_list if "equip_list" in target_player.data_model else []
			for eq in eq_list:
				var uid = int(item_data.get("UID", item_data.get("uid", item_data.get("ItemID", 0))))
				if uid > 0 and int(eq.get("item_id", 0)) == uid:
					damage = int(eq.get("damage", 0))
					break
				elif uid == 0 and int(eq.get("item_idx", eq.get("item_index", 0))) == item_index:
					damage = int(eq.get("damage", 0))
					break

	var nd_info = "[center][font_size=15][color=#FFFFFF]%s[/color][/font_size][/center]\n" % item_name
	if item_desc != "": nd_info += "\n[color=#FFFFFF]%s[/color]\n\n" % item_desc
	else: nd_info += "\n"

	var spec_fx_str = str(special_effect).strip_edges()
	if spec_fx_str != "" and spec_fx_str != "0" and spec_fx_str != "無":
		nd_info += "\n[color=#FFD700]特殊效果：%s[/color]\n" % spec_fx_str

	nd_info += "[color=#FFFFFF]耗損：%d / %d[/color]\n" % [damage, durability]

	var req_level_str = str(req_level).strip_edges()
	if req_level_str != "" and req_level_str != "1": nd_info += "[color=#FFFFFF]等級限制：%s[/color]\n" % req_level_str
	var req_job_str = str(req_job).strip_edges()
	if req_job_str != "" and req_job_str != "無": nd_info += "[color=#FFFFFF]職業限制：%s[/color]\n" % req_job_str
	var req_race_str = str(req_race).strip_edges()
	if req_race_str != "" and req_race_str != "無": nd_info += "[color=#FFFFFF]種族限制：%s[/color]\n" % req_race_str

	if req_str > 0 or req_dex > 0 or req_con > 0 or req_wis > 0:
		nd_info += "[color=#FFFFFF]"
		if req_str > 0: nd_info += "力量：%d " % req_str
		if req_dex > 0: nd_info += "敏捷：%d " % req_dex
		if req_con > 0: nd_info += "體質：%d " % req_con
		if req_wis > 0: nd_info += "智慧：%d " % req_wis
		nd_info += "[/color]\n"

	var restrictions = []
	if item_can_trade == 0: restrictions.append("不可交易")
	if item_can_sell == 0: restrictions.append("不可出售")
	if item_can_store == 0: restrictions.append("不可存放倉庫")
	if item_can_repair == 0: restrictions.append("不可修理")
	if restrictions.size() > 0: nd_info += "[color=#FFFFFF]其他資訊：%s[/color]" % "、".join(restrictions)
	tooltip_name_desc.text = nd_info

	var hover_hp = ItemLoader.get_item_property(item_index, "max_hp_bonus", 0)
	var hover_mp = ItemLoader.get_item_property(item_index, "max_mp_bonus", 0)
	var hover_sp = ItemLoader.get_item_property(item_index, "max_sp_bonus", 0)
	var hover_atk = ItemLoader.get_item_property(item_index, "atk", 0)
	var hover_def = ItemLoader.get_item_property(item_index, "def", 0)
	var hover_matk = ItemLoader.get_item_property(item_index, "matk", 0)
	var hover_mdef = ItemLoader.get_item_property(item_index, "mdef", 0)
	var hover_hit = ItemLoader.get_item_property(item_index, "hit", 0)
	var hover_flee = ItemLoader.get_item_property(item_index, "flee", 0)
	var hover_luk = ItemLoader.get_item_property(item_index, "luk", 0)
	var hover_atk_range = ItemLoader.get_item_property(item_index, "attack_range", 0)
	var hover_atk_speed = ItemLoader.get_item_property(item_index, "aspd", 0)
	var hover_move_speed = ItemLoader.get_item_property(item_index, "move_speed", 0)
	var hover_crit_rate = ItemLoader.get_item_property(item_index, "crit_rate", 0)
	var hover_crit_dmg = ItemLoader.get_item_property(item_index, "crit_damage", 0)

	var target_part = ItemLoader.get_item_property(item_index, "equip_slot", ItemLoader.get_item_property(item_index, "part", -1))

	var eq_hp = 0; var eq_mp = 0; var eq_sp = 0; var eq_atk = 0; var eq_def = 0;
	var eq_matk = 0; var eq_mdef = 0; var eq_hit = 0; var eq_flee = 0; var eq_luk = 0;
	var eq_atk_speed = 0; var eq_move_speed = 0; var eq_crit_rate = 0; var eq_crit_dmg = 0; var eq_atk_range = 0;
	var has_equipped_item = false

	if target_part >= 0 and is_instance_valid(target_player) and "data_model" in target_player and target_player.data_model:
		var eq_list = target_player.data_model.equip_list if "equip_list" in target_player.data_model else []
		for eq in eq_list:
			if eq.get("part", 255) == target_part:
				var equipped_idx = eq.get("item_index", eq.get("item_idx", eq.get("equip_idx", 0)))
				eq_hp = ItemLoader.get_item_property(equipped_idx, "max_hp_bonus", 0)
				eq_mp = ItemLoader.get_item_property(equipped_idx, "max_mp_bonus", 0)
				eq_sp = ItemLoader.get_item_property(equipped_idx, "max_sp_bonus", 0)
				eq_atk = ItemLoader.get_item_property(equipped_idx, "atk", 0)
				eq_def = ItemLoader.get_item_property(equipped_idx, "def", 0)
				eq_matk = ItemLoader.get_item_property(equipped_idx, "matk", 0)
				eq_mdef = ItemLoader.get_item_property(equipped_idx, "mdef", 0)
				eq_hit = ItemLoader.get_item_property(equipped_idx, "hit", 0)
				eq_flee = ItemLoader.get_item_property(equipped_idx, "flee", 0)
				eq_luk = ItemLoader.get_item_property(equipped_idx, "luk", 0)
				eq_atk_range = ItemLoader.get_item_property(equipped_idx, "attack_range", 0)
				eq_atk_speed = ItemLoader.get_item_property(equipped_idx, "aspd", 0)
				eq_move_speed = ItemLoader.get_item_property(equipped_idx, "move_speed", 0)
				eq_crit_rate = ItemLoader.get_item_property(equipped_idx, "crit_rate", 0)
				eq_crit_dmg = ItemLoader.get_item_property(equipped_idx, "crit_damage", 0)
				has_equipped_item = true
				break

	var stat_info = ""
	var should_show_stats = (hover_hp > 0 or hover_mp > 0 or hover_sp > 0 or hover_atk > 0 or hover_def > 0 or hover_matk > 0 or hover_mdef > 0 or hover_hit > 0 or hover_flee > 0 or hover_luk > 0 or hover_atk_speed > 0 or hover_move_speed > 0 or hover_crit_rate > 0 or hover_crit_dmg > 0 or hover_atk_range > 0 or has_equipped_item)

	if should_show_stats:
		var build_stat_line = func(stat_name, h_val, e_val, is_pct = false):
			if h_val == 0 and e_val == 0: return ""
			var pct_str = "%" if is_pct else ""
			var diff = h_val - e_val
			var diff_str = ""

			if diff > 0: diff_str = " [color=#66FF66] (+%d%s)[/color]" % [diff, pct_str]
			elif diff < 0: diff_str = " [color=#FF4444] (%d%s)[/color]" % [diff, pct_str]
			else: diff_str = " [color=#888888] (-)[/color]"

			var val_str = ("+%d" % h_val) if h_val > 0 else str(h_val)
			return "[color=#FFFFFF]%s：[/color]%s%s%s\n" % [stat_name, val_str, pct_str, diff_str]

		stat_info += build_stat_line.call("攻擊距離", hover_atk_range, eq_atk_range)
		stat_info += build_stat_line.call("生命上限", hover_hp, eq_hp)
		stat_info += build_stat_line.call("魔力上限", hover_mp, eq_mp)
		stat_info += build_stat_line.call("意志上限", hover_sp, eq_sp)
		stat_info += build_stat_line.call("攻擊", hover_atk, eq_atk)
		stat_info += build_stat_line.call("防禦", hover_def, eq_def)
		stat_info += build_stat_line.call("魔攻", hover_matk, eq_matk)
		stat_info += build_stat_line.call("魔防", hover_mdef, eq_mdef)
		stat_info += build_stat_line.call("命中", hover_hit, eq_hit)
		stat_info += build_stat_line.call("迴避", hover_flee, eq_flee)
		stat_info += build_stat_line.call("幸運", hover_luk, eq_luk)
		stat_info += build_stat_line.call("攻速", hover_atk_speed, eq_atk_speed)
		stat_info += build_stat_line.call("移速", hover_move_speed, eq_move_speed)
		stat_info += build_stat_line.call("暴擊率", hover_crit_rate, eq_crit_rate, true)
		stat_info += build_stat_line.call("暴擊傷害", hover_crit_dmg, eq_crit_dmg, true)

	tooltip_stats.text = stat_info

func _update_pet_tooltip(pet_id: int, pet_data: Dictionary = {}) -> void :

	var pet_name = str(pet_data.get("Name", pet_data.get("name", "未知寵物")))
	var pet_lv = int(pet_data.get("Level", pet_data.get("level", 1)))
	var pet_hp = int(pet_data.get("HP", pet_data.get("hp", 0)))
	var pet_hp_max = int(pet_data.get("MaxHP", pet_data.get("max_hp", 0)))
	var pet_mp = int(pet_data.get("MP", pet_data.get("mp", 0)))
	var pet_mp_max = int(pet_data.get("MaxMP", pet_data.get("max_mp", 0)))
	var pet_str = int(pet_data.get("STR", pet_data.get("str", 0)))
	var pet_dex = int(pet_data.get("DEX", pet_data.get("dex", 0)))
	var pet_con = int(pet_data.get("CON", pet_data.get("con", 0)))
	var pet_wis = int(pet_data.get("WIS", pet_data.get("wis", 0)))
	var evol_id = int(pet_data.get("EvolID", pet_data.get("evol_id", 0)))
	var use_exp = int(pet_data.get("UseExp", pet_data.get("use_exp", 0)))
	var feed_value = int(pet_data.get("FeedValue", pet_data.get("feed_value", 0)))
	var feed_limit = int(pet_data.get("FeedExpLimit", pet_data.get("feed_exp_limit", 0)))
	var life_count = int(pet_data.get("LifeCount", pet_data.get("life_count", 0)))
	var summon_wait_count = int(pet_data.get("SummonWaitCount", pet_data.get("summon_wait_count", 0)))
	var current_exp = int(pet_data.get("Exp", pet_data.get("exp", 0)))
	var max_exp = int(pet_data.get("MaxExp", pet_data.get("max_exp", 1)))


	var config = {}
	if PetLoader:
		config = PetLoader.get_pet_config(pet_id)

	var evols = config.get("evolutions", {})
	var evol_data = evols.get(str(evol_id), evols.get(float(evol_id), {}))
	var sub_name = str(evol_data.get("sub_name", "未知形態"))
	var desc = str(evol_data.get("description", ""))


	var has_custom_icon = false
	var color_index = int(evol_data.get("color", 0))

	if config and config.has("evolutions"):
		var evol_key = str(evol_id)
		if config.evolutions.has(evol_key):
			var outlook = config.evolutions[evol_key].get("outlook", "")
			if outlook != "":
				var photo_path = "res://Assets/Pets/Photo/" + outlook + ".png"
				if ResourceLoader.exists(photo_path):
					tooltip_icon.texture = load(photo_path)
					has_custom_icon = true

	if not has_custom_icon:
		tooltip_icon.texture = load("res://Assets/Items/Photos/Photo0375.png")

	if color_index > 0:
		var mat = ShaderMaterial.new()
		mat.shader = PALETTE_SHADER
		mat.set_shader_parameter("effect_type", 0)
		mat.set_shader_parameter("hue_shift", COLOR_INDEX_TO_DEGREES.get(color_index, 0.0))
		tooltip_icon.material = mat
	else:
		tooltip_icon.material = null


	var current_unix = Time.get_unix_time_from_system() * 1000.0
	var cd_text = ""
	if summon_wait_count > current_unix:
		var remaining_sec = int(ceil((summon_wait_count - current_unix) / 1000.0))
		var mins = remaining_sec / 60
		var secs = remaining_sec % 60
		cd_text = "下次可召喚時間：%d分%d秒後" % [mins, secs]

	var is_preview = not pet_data.has("uid") and not pet_data.has("UID")


	var nd_info = "[center][font_size=15][color=#FFFFFF]%s[/color][/font_size][/center]\n\n" % pet_name
	if desc != "": nd_info += "[color=#FFFFFF]%s[/color]\n\n" % desc
	nd_info += "[color=#FFFFFF]品種：%s[/color]\n" % config.get("name", "未知的寵物")
	nd_info += "[color=#FFFFFF]型態：%s[/color]\n" % sub_name

	if not is_preview:

		nd_info += "[color=#FFFFFF]壽命：%d[/color]\n" % life_count
		nd_info += "[color=#FFFFFF]經驗：%d / %d[/color]\n" % [current_exp, max_exp]
		nd_info += "[color=#FFFFFF]飽食度：%d / %d[/color]\n" % [feed_value, feed_limit]
		if cd_text != "": nd_info += "[color=#FFFFFF]%s[/color]\n" % cd_text

	tooltip_name_desc.text = nd_info


	var stat_info = ""
	if not is_preview:
		stat_info += "[color=#FFFFFF]等級：[/color]%d\n" % pet_lv
		stat_info += "[color=#FFFFFF]生命：[/color]%d / %d\n" % [pet_hp, pet_hp_max]
		stat_info += "[color=#FFFFFF]魔力：[/color]%d / %d\n\n" % [pet_mp, pet_mp_max]

		stat_info += "[color=#FFFFFF]力量：[/color]%d\n" % pet_str
		stat_info += "[color=#FFFFFF]敏捷：[/color]%d\n" % pet_dex
		stat_info += "[color=#FFFFFF]體質：[/color]%d\n" % pet_con
		stat_info += "[color=#FFFFFF]智慧：[/color]%d\n" % pet_wis

		if use_exp > 0:
			stat_info += "\n[color=#00FF00]可用配點： %d 點[/color]\n" % use_exp

	tooltip_stats.text = stat_info

func _update_mecha_tooltip(item_index: int, item_data: Dictionary = {}) -> void :

	var is_horse_equip = item_data.has("equip_uid") or item_data.has("equip_idx")
	if ItemLoader:
		var cat = ItemLoader.get_item_category(item_index)
		if cat == 4:
			is_horse_equip = true
		elif cat == 3:
			is_horse_equip = false

	var is_horse = not is_horse_equip

	var base_name = ItemLoader.get_item_property(item_index, "item_name", "未知機甲組件")
	var custom_name = item_data.get("name", "")
	var item_name = base_name

	if custom_name != "":
		item_name = base_name.replace("%s", custom_name) if "%s" in base_name else custom_name

	var item_desc = ItemLoader.get_item_property(item_index, "description", "")
	var item_can_trade = ItemLoader.get_item_property(item_index, "can_trade", 0)
	var item_can_sell = ItemLoader.get_item_property(item_index, "can_sell", 0)
	var item_can_store = ItemLoader.get_item_property(item_index, "can_store", 0)
	var item_can_repair = ItemLoader.get_item_property(item_index, "can_repair", 1)

	var nd_info = "[center][font_size=15][color=#FFFFFF]%s[/color][/font_size][/center]\n" % item_name
	if item_desc != "": nd_info += "\n[color=#FFFFFF]%s[/color]\n" % item_desc

	if is_horse_equip:
		var restrictions = []
		if item_can_trade == 0: restrictions.append("不可交易")
		if item_can_sell == 0: restrictions.append("不可出售")
		if item_can_store == 0: restrictions.append("不可存放倉庫")
		if item_can_repair == 0: restrictions.append("不可修理")
		if restrictions.size() > 0: nd_info += "\n[color=#FFFFFF]其他資訊：%s[/color]\n" % "、".join(restrictions)

	tooltip_name_desc.text = nd_info

	var stat_info = ""
	if is_horse:
		var level = item_data.get("level", ItemLoader.get_item_property(item_index, "req_level", 1))
		var max_battery = ItemLoader.get_item_property(item_index, "max_battery", 0)

		var energy_left = item_data.get("energy_left", max_battery)
		stat_info += "[color=#FFFFFF]等級：[/color]%d\n" % level
		stat_info += "[color=#FFFFFF]剩餘電量：[/color]%d / %d\n" % [energy_left, max_battery]
	else:



		var damage = int(item_data.get("Damage", item_data.get("damage", 0)))
		var target_player = GameManager.current_player if GameManager else null


		if damage == 0 and not item_data.has("Damage") and not item_data.has("damage"):
			if is_instance_valid(target_player) and "data_model" in target_player and target_player.data_model:
				var heq_list = target_player.data_model.horse_equip_list if "horse_equip_list" in target_player.data_model else []
				for heq in heq_list:
					var uid = int(item_data.get("UID", item_data.get("uid", item_data.get("EquipUID", item_data.get("equip_uid", 0)))))
					if uid > 0 and int(heq.get("equip_uid", 0)) == uid:
						damage = int(heq.get("damage", 0))
						break
					elif uid == 0 and int(heq.get("equip_idx", 0)) == item_index:
						damage = int(heq.get("damage", 0))
						break


		var durability = ItemLoader.get_item_property(item_index, "durability", 0)
		var req_level = ItemLoader.get_item_property(item_index, "req_level", 0)
		var equip_slot = ItemLoader.get_item_property(item_index, "equip_slot", 0)
		var slot_name = "未知"
		match equip_slot:
			0: slot_name = "主體"
			1: slot_name = "武裝"
			2: slot_name = "引擎"
			3: slot_name = "強化卡 1"
			4: slot_name = "強化卡 2"
			5: slot_name = "強化卡 3"
			6: slot_name = "守護石"

		stat_info += "[color=#FFFFFF]耗損值：[/color]%d / %d\n" % [damage, durability]
		stat_info += "[color=#FFFFFF]等級限制：[/color]%d\n" % req_level
		stat_info += "[color=#FFFFFF]裝備位置：[/color]%s\n\n" % slot_name

		var hover_hp = ItemLoader.get_item_property(item_index, "max_hp_bonus", 0)
		var hover_mp = ItemLoader.get_item_property(item_index, "max_mp_bonus", 0)
		var hover_sp = ItemLoader.get_item_property(item_index, "max_sp_bonus", 0)
		var hover_atk = ItemLoader.get_item_property(item_index, "atk", 0)
		var hover_def = ItemLoader.get_item_property(item_index, "def", 0)
		var hover_matk = ItemLoader.get_item_property(item_index, "matk", 0)
		var hover_mdef = ItemLoader.get_item_property(item_index, "mdef", 0)
		var hover_hit = ItemLoader.get_item_property(item_index, "hit", 0)
		var hover_flee = ItemLoader.get_item_property(item_index, "flee", 0)
		var hover_atk_speed = ItemLoader.get_item_property(item_index, "atk_speed", 0)
		var hover_move_speed = ItemLoader.get_item_property(item_index, "move_speed", 0)
		var hover_crit_rate = ItemLoader.get_item_property(item_index, "crit_rate", 0)
		var hover_crit_dmg = ItemLoader.get_item_property(item_index, "crit_damage", 0)
		var hover_atk_range = ItemLoader.get_item_property(item_index, "attack_range", 0)


		var equipped_item_idx = -1

		var is_currently_equipped = (item_data.get("horse_uid", -1) != -1)

		if not is_currently_equipped and is_horse_equip:
			if is_instance_valid(target_player) and "data_model" in target_player and target_player.data_model:
				var p_data = target_player.data_model



				var target_horse_uid = item_data.get("preview_target_horse_uid", p_data.summoned_horse_uid)

				if target_horse_uid != -1:
					for heq in p_data.horse_equip_list:
						if heq.get("horse_uid", -1) == target_horse_uid and heq.get("in_bank", 0) == 0:
							var e_idx = heq.get("equip_idx", 0)
							var part = heq.get("part", ItemLoader.get_item_property(e_idx, "equip_slot", -1))

							if part == equip_slot:
								equipped_item_idx = e_idx
								break


		var build_stat_line = func(stat_name, val, stat_key, is_pct = false):
			if val == 0: return ""
			var pct_str = "%" if is_pct else ""
			var val_str = ("+%d" % val) if val > 0 else str(val)

			var diff_str = ""

			if equipped_item_idx != -1 and equipped_item_idx != item_index:
				var eq_val = ItemLoader.get_item_property(equipped_item_idx, stat_key, 0)
				var diff = val - eq_val

				if diff > 0:
					diff_str = " [color=#00FF00](+%d%s)[/color]" % [diff, pct_str]
				elif diff < 0:
					diff_str = " [color=#FF0000](%d%s)[/color]" % [diff, pct_str]

			return "[color=#FFFFFF]%s：[/color]%s%s%s\n" % [stat_name, val_str, pct_str, diff_str]


		stat_info += build_stat_line.call("攻擊距離", hover_atk_range, "attack_range")
		stat_info += build_stat_line.call("生命上限", hover_hp, "max_hp_bonus")
		stat_info += build_stat_line.call("魔力上限", hover_mp, "max_mp_bonus")
		stat_info += build_stat_line.call("意志上限", hover_sp, "max_sp_bonus")
		stat_info += build_stat_line.call("攻擊", hover_atk, "atk")
		stat_info += build_stat_line.call("防禦", hover_def, "def")
		stat_info += build_stat_line.call("魔攻", hover_matk, "matk")
		stat_info += build_stat_line.call("魔防", hover_mdef, "mdef")
		stat_info += build_stat_line.call("命中", hover_hit, "hit")
		stat_info += build_stat_line.call("迴避", hover_flee, "flee")
		stat_info += build_stat_line.call("攻速", hover_atk_speed, "atk_speed")
		stat_info += build_stat_line.call("移速", hover_move_speed, "move_speed")
		stat_info += build_stat_line.call("暴擊率", hover_crit_rate, "crit_rate", true)
		stat_info += build_stat_line.call("暴擊傷害", hover_crit_dmg, "crit_damage", true)

	tooltip_stats.text = stat_info
