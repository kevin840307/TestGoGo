
extends Control


@onready var lbl_name: Label = $Background / LblName
@onready var lbl_title: Label = $Background / LblTitle
@onready var lbl_career: Label = $Background / LblCareer
@onready var lbl_str: Label = $Background / LblStr
@onready var lbl_dex: Label = $Background / LblDex
@onready var lbl_con: Label = $Background / LblCon
@onready var lbl_wis: Label = $Background / LblWis
@onready var lbl_atk: Label = $Background / LblAtk
@onready var lbl_def: Label = $Background / LblDef
@onready var lbl_matk: Label = $Background / LblMAtk
@onready var lbl_mdef: Label = $Background / LblMDef
@onready var lbl_hit: Label = $Background / LblHit
@onready var lbl_dodge: Label = $Background / LblDodg
@onready var lbl_lucky: Label = $Background / LblLuk
@onready var lbl_atk_total: Label = $Background / LblAtkTotal
@onready var lbl_def_total: Label = $Background / LblDefTotal
@onready var lbl_redman: Label = $Background / LblRedMan
@onready var lbl_money: Label = $Background / LblMoney
@onready var lbl_hp: Label = $Background / LblHP
@onready var lbl_mp: Label = $Background / LblMP
@onready var lbl_will: Label = $Background / LblWill
@onready var lbl_level: Label = $Background / LblLevel
@onready var lbl_expuse: Label = $Background / LblExpUse
@onready var lbl_exp: Label = $Background / LblExp
@onready var lbl_death: Label = $Background / LblDeath
@onready var bar_hp: ProgressBar = $Background / BarHP
@onready var bar_mp: ProgressBar = $Background / BarMP
@onready var bar_will: ProgressBar = $Background / BarWill
@onready var bar_exp: ProgressBar = $Background / BarExp
@onready var btn_add_str: TextureButton = $Background / ButtonAddStr
@onready var btn_add_dex: TextureButton = $Background / ButtonAddDex
@onready var btn_add_con: TextureButton = $Background / ButtonAddCon
@onready var btn_add_wis: TextureButton = $Background / ButtonAddWis
@onready var btn_title_list: Button = $Background / BtnTitleList
@onready var btn_close: BaseButton = $Background / BtnClose
@onready var title_panel: Control = $TitlePanel
@onready var lbl_title2: Label = $TitlePanel / LblTitle
@onready var title_vbox: VBoxContainer = $TitlePanel / ScrollContainer / TitleVBox
@onready var lbl_move_speed: Label = $Background / LblMoveSpeed
@onready var lbl_attack_speed: Label = $Background / LblAttackSpeed
@onready var lbl_crit_rate: Label = $Background / LblCritRate
@onready var lbl_crit_damage: Label = $Background / LblCritDamage

var current_race: String = "Boy"
var target_player: CharacterBody2D = null


func _ready() -> void :
	if btn_add_str: btn_add_str.pressed.connect(_on_add_stat_pressed.bind("STR"))
	if btn_add_dex: btn_add_dex.pressed.connect(_on_add_stat_pressed.bind("DEX"))
	if btn_add_con: btn_add_con.pressed.connect(_on_add_stat_pressed.bind("CON"))
	if btn_add_wis: btn_add_wis.pressed.connect(_on_add_stat_pressed.bind("WIS"))
	if btn_title_list: btn_title_list.pressed.connect(_on_btn_title_list_pressed)
	if title_panel: title_panel.visible = false
	if btn_close:
		btn_close.pressed.connect( func():
			if GameManager.current_hud and GameManager.current_hud.has_method("_close_window"):
				GameManager.current_hud._close_window(self)
			else:
				hide()
		)


func _on_add_stat_pressed(stat_type: String) -> void :
	if not is_instance_valid(target_player) or not "data_model" in target_player: return
	var data = target_player.data_model
	if data.exp_use <= 0: return




	const MAX_STAT_LIMIT = 100
	var is_max = false
	match stat_type:
		"STR": if data.strength >= MAX_STAT_LIMIT: is_max = true
		"DEX": if data.dexterity >= MAX_STAT_LIMIT: is_max = true
		"CON": if data.constitution >= MAX_STAT_LIMIT: is_max = true
		"WIS": if data.wisdom >= MAX_STAT_LIMIT: is_max = true

	if is_max:

		return


	data.exp_use -= 1
	match stat_type:
		"STR": data.strength += 1
		"DEX": data.dexterity += 1
		"CON": data.constitution += 1
		"WIS": data.wisdom += 1

	update_ui()


	if NetworkManager: NetworkManager.send_allocate_stat_request(stat_type)


func setup(player: CharacterBody2D) -> void :
	target_player = player


func update_ui() -> void :
	if not is_instance_valid(target_player) or not "data_model" in target_player: return
	var data = target_player.data_model
	if not data: return

	current_race = GameConstants.RACE_ID_MAP.get(int(data.race_id), "Boy")
	if lbl_name:
		var race_map = {"Boy": "[騎]", "Girl": "[騎]", "Migu": "[魔]", "Robot": "[機]", "Yas": "[遺]"}
		lbl_name.text = race_map.get(current_race, "[?]") + data.character_name

	if lbl_title:
		if data.title > 0:
			lbl_title.text = PlayerData.TITLE_MAP.get(data.title, "未知稱號")
		else:
			lbl_title.text = "無"
	if lbl_title2:
		if data.title > 0:
			lbl_title2.text = PlayerData.TITLE_MAP.get(data.title, "未知稱號")
		else:
			lbl_title2.text = "無"

	var career_text = "遊民"
	if data.career > 0 and GameConstants.CAREER_NAMES.has(data.career):
		var base_career_name = GameConstants.CAREER_NAMES[data.career]
		if data.job == 1: career_text = "見習" + base_career_name
		elif data.job == 2: career_text = base_career_name
		else: career_text = base_career_name

	if lbl_career: lbl_career.text = career_text

	if lbl_str: lbl_str.text = str(data.strength)
	if lbl_dex: lbl_dex.text = str(data.dexterity)
	if lbl_con: lbl_con.text = str(data.constitution)
	if lbl_wis: lbl_wis.text = str(data.wisdom)
	if lbl_atk: lbl_atk.text = str(data.attack)
	if lbl_def: lbl_def.text = str(data.defense)
	if lbl_matk: lbl_matk.text = str(data.magic_attack)
	if lbl_mdef: lbl_mdef.text = str(data.magic_defense)
	if lbl_hit: lbl_hit.text = str(data.hit)
	if lbl_dodge: lbl_dodge.text = str(data.dodge)
	if lbl_lucky: lbl_lucky.text = str(data.lucky)
	if lbl_atk_total: lbl_atk_total.text = str(data.attack + data.magic_attack)
	if lbl_def_total: lbl_def_total.text = str(data.defense + data.magic_defense)
	if lbl_redman:
		lbl_redman.text = str(data.red_man_kill_left)
		if data.red_man_kill_left > 0:
			lbl_redman.add_theme_color_override("font_color", Color(1.0, 0.116, 0.152, 1.0))
		else:
			lbl_redman.add_theme_color_override("font_color", Color(1.0, 1.0, 1.0, 1.0))
	if lbl_money: lbl_money.text = str(data.money)
	if lbl_hp: lbl_hp.text = "%d / %d" % [data.hp, data.max_hp]
	if lbl_mp: lbl_mp.text = "%d / %d" % [data.mp, data.max_mp]
	if lbl_will: lbl_will.text = "%d / %d" % [data.sp, data.max_sp]
	if lbl_level: lbl_level.text = str(data.level)
	if lbl_expuse: lbl_expuse.text = str(data.exp_use)
	var has_points: bool = data.exp_use > 0

	const MAX_STAT = 100
	if btn_add_str: btn_add_str.visible = has_points and data.strength < MAX_STAT
	if btn_add_dex: btn_add_dex.visible = has_points and data.dexterity < MAX_STAT
	if btn_add_con: btn_add_con.visible = has_points and data.constitution < MAX_STAT
	if btn_add_wis: btn_add_wis.visible = has_points and data.wisdom < MAX_STAT
	if data.max_exp > 0:
		var pct = (float(data.exp_acc) / float(data.max_exp)) * 100.0
		if lbl_exp: lbl_exp.text = "%.2f %%" % pct
		if bar_exp:
			bar_exp.max_value = data.max_exp
			bar_exp.value = data.exp_acc
			bar_exp.tooltip_text = "%d / %d" % [data.exp_acc, data.max_exp]
	else:
		if lbl_exp: lbl_exp.text = "0.00 %"
		if bar_exp:
			bar_exp.value = 0
			bar_exp.tooltip_text = "%d / 0" % data.exp_acc
	if lbl_death: lbl_death.text = str(data.exp_death)

	if bar_hp: bar_hp.max_value = data.max_hp;bar_hp.value = data.hp
	if bar_mp: bar_mp.max_value = data.max_mp;bar_mp.value = data.mp
	if bar_will: bar_will.max_value = data.max_sp;bar_will.value = data.sp

	if lbl_move_speed: lbl_move_speed.text = str(data.move_speed)
	if lbl_attack_speed: lbl_attack_speed.text = str(data.attack_speed)
	if lbl_crit_rate: lbl_crit_rate.text = "%d%%" % data.crit_rate
	if lbl_crit_damage: lbl_crit_damage.text = "%d%%" % data.crit_damage


func _on_btn_title_list_pressed() -> void :
	if not title_panel: return
	title_panel.visible = !title_panel.visible
	if title_panel.visible:
		_populate_title_list()


func _populate_title_list() -> void :
	if not title_vbox: return


	for child in title_vbox.get_children():
		child.queue_free()

	if not is_instance_valid(target_player) or not "data_model" in target_player: return
	var data = target_player.data_model


	var btn_none = Button.new()
	btn_none.custom_minimum_size.y = 38.0
	btn_none.text = "不使用稱號"
	btn_none.pressed.connect(_on_title_selected.bind(0))
	title_vbox.add_child(btn_none)


	for t_id in data.unlocked_titles:
		var btn = Button.new()
		btn.custom_minimum_size.y = 38.0
		var t_name = PlayerData.TITLE_MAP.get(int(t_id), "未知稱號")
		btn.text = t_name
		btn.pressed.connect(_on_title_selected.bind(int(t_id)))
		title_vbox.add_child(btn)


func _on_title_selected(t_id: int) -> void :

	if NetworkManager:
		NetworkManager.send_set_title_request(t_id)


	if is_instance_valid(target_player) and "data_model" in target_player:
		target_player.data_model.title = t_id
		update_ui()
