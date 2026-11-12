
class_name PlayerData
extends RefCounted

signal identity_labels_changed


const TITLE_MAP: Dictionary = {
	1: "見習靈鬥士", 
	2: "見習守護劍士", 
	3: "見習聖劍士", 
	4: "見習銃戰士", 
	5: "見習魔動使", 
	6: "見習守護使", 
	7: "見習聖靈使", 
	8: "見習魔銃使", 
	9: "見習道具使", 
	10: "見習噬魂使", 
	11: "靈鬥士", 
	12: "守護劍士", 
	13: "聖劍士", 
	14: "銃戰士", 
	15: "魔動使", 
	16: "守護使", 
	17: "聖靈使", 
	18: "魔銃使", 
	19: "道具使", 
	20: "噬魂使", 
	21: "星火守護者", 
	22: "一個好人orz", 
	23: "甜蜜的愛情", 
	24: "蒼之引導者", 
	25: "好心人", 
	26: "幸福使者"
}


var acc_id: int = 0
var db_id: int = 0
var character_name: String = ""


var title: int = 0:
	set(value):
		if title == value: return
		title = value
		identity_labels_changed.emit()
var unlocked_titles: Array = []
var career: int = 0
var job: int = 0
var red_man: int = 0
var red_man_kill_left: int = 0
var pk_status: int = 1
var pk_pink_until_utc_ms: int = 0

var summoned_horse_uid: int = -1
var mecha_type: String = "MS"


var party_id: String = ""
var party_leader_dbid: int = 0
var party_members: Array = []


var is_shop_open: bool = false
var shop_name: String = ""

var is_auto_saint_on: bool = false


var transform_type: int = 0
var transform_id: String = ""
var transform_color_index: int = 0


var race_id: int = 0
var hair_id: int = 0
var hair_color_id: int = 0
var bonnet_id: int = 0
var left_hand_id: int = 0
var right_hand_id: int = 0
var upper_armor_id: int = 0
var lower_armor_id: int = 0
var weapon_id: int = 0
var shield_id: int = 0
var back_id: int = 0
var amulet_id: int = 0


var level: int = 1
var hp: int = 0
var max_hp: int = 100
var mp: int = 0
var max_mp: int = 50
var sp: int = 0
var max_sp: int = 100
var money: int = 0


var exp_use: int = 0
var exp_acc: int = 0
var exp_death: int = 0
var max_exp: int = 1000
var job_counter: int = 0


var attack: int = 0
var defense: int = 0
var magic_attack: int = 0
var magic_defense: int = 0
var hit: int = 0
var dodge: int = 0
var lucky: int = 0
var move_speed: int = 100
var attack_speed: int = 100
var attack_range: int = 80
var crit_rate: int = 0
var crit_damage: int = 125


var strength: int = 0
var dexterity: int = 0
var constitution: int = 0
var wisdom: int = 0


var map_id: int = 0
var loc_x: float = 0.0
var loc_y: float = 0.0


var inventory_max_size: int = 200
var inventory: Array = []

var equip_max_size: int = 200
var equip_list: Array = []

var pet_max_size: int = 50
var pet_summon_max: int = 1
var pet_list: Array = []

var horse_max_size: int = 10
var horse_list: Array = []

var horse_equip_max_size: int = 100
var horse_equip_list: Array = []


var rider_state: Dictionary = {"SummonedHorseUID": -1}


var club_id: int = 0:
	set(value):
		if club_id == value: return
		club_id = value
		identity_labels_changed.emit()
var club_position: int = 0:
	set(value):
		if club_position == value: return
		club_position = value
		identity_labels_changed.emit()
var club_donation: int = 0
var club_name: String = "":
	set(value):
		if club_name == value: return
		club_name = value
		identity_labels_changed.emit()

var skill_list: Array = []

var player_quests: Dictionary = {}

var shortcuts: Array = []


var _backup_equip_list: Array = []
var _backup_paper_doll: Dictionary = {}


func load_from_json(json_dict: Dictionary) -> void :
	acc_id = int(json_dict.get("ACCID", 0))
	db_id = int(json_dict.get("DBID", 0))
	character_name = str(json_dict.get("NAME", "Unknown"))

	title = int(json_dict.get("Title", 0))
	unlocked_titles = json_dict.get("UnlockedTitles", [])
	career = int(json_dict.get("Career", 0))
	job = int(json_dict.get("Job", 0))
	red_man = int(json_dict.get("RedMan", 0))
	red_man_kill_left = int(json_dict.get("RedManKillLeft", 0))
	pk_status = int(json_dict.get("PKStatus", 1))
	pk_pink_until_utc_ms = int(json_dict.get("PKPinkUntilUtcMs", 0))

	is_shop_open = bool(json_dict.get("IsShopOpen", json_dict.get("is_shop_open", false)))
	shop_name = str(json_dict.get("ShopName", json_dict.get("shop_name", "")))

	club_id = int(json_dict.get("ClubID", 0))
	club_position = int(json_dict.get("ClubPosition", 0))
	club_donation = int(json_dict.get("ClubDonation", 0))
	club_name = str(json_dict.get("ClubName", ""))

	race_id = int(json_dict.get("Race", 0))
	hair_id = int(json_dict.get("Hair", 0))
	hair_color_id = int(json_dict.get("HairColor", 0))

	bonnet_id = int(json_dict.get("Bonnet", 0))
	left_hand_id = int(json_dict.get("Left_hand", 0))
	right_hand_id = int(json_dict.get("Right_hand", 0))
	upper_armor_id = int(json_dict.get("Upper_armor", 0))
	lower_armor_id = int(json_dict.get("Lower_armor", 0))
	weapon_id = int(json_dict.get("Weapon", 0))
	shield_id = int(json_dict.get("Shield", 0))
	back_id = int(json_dict.get("Back", 0))
	amulet_id = int(json_dict.get("Amulet", 0))

	level = int(json_dict.get("Level", 1))

	hp = int(json_dict.get("HP", 100))
	max_hp = int(json_dict.get("MaxHP", hp if hp > 0 else 100))
	if hp > max_hp: hp = max_hp

	mp = int(json_dict.get("MP", 50))
	max_mp = int(json_dict.get("MaxMP", mp if mp > 0 else 50))
	if mp > max_mp: mp = max_mp

	sp = int(json_dict.get("SP", 100))
	max_sp = int(json_dict.get("MaxSP", sp if sp > 0 else 100))
	if sp > max_sp: sp = max_sp

	money = int(json_dict.get("Money", 0))

	exp_use = int(json_dict.get("ExpUse", 0))
	exp_acc = int(json_dict.get("ExpAcc", 0))
	exp_death = int(json_dict.get("ExpDeath", 0))
	max_exp = int(json_dict.get("MaxExp", 1000))
	job_counter = int(json_dict.get("JobCounter", 0))

	attack = int(json_dict.get("Attack", 0))
	defense = int(json_dict.get("Defense", 0))
	magic_attack = int(json_dict.get("MagicAttack", 0))
	magic_defense = int(json_dict.get("MagicDefense", 0))
	hit = int(json_dict.get("Hit", 0))
	dodge = int(json_dict.get("Dodge", 0))
	lucky = int(json_dict.get("Lucky", 0))
	move_speed = int(json_dict.get("MoveSpeed", 100))

	var default_atk_spd = 100
	if race_id == 2: default_atk_spd = 115
	elif race_id == 3: default_atk_spd = 85
	attack_speed = int(json_dict.get("AttackSpeed", default_atk_spd))
	attack_range = int(json_dict.get("AttackRange", 80))
	crit_rate = int(json_dict.get("CritRate", 0))
	crit_damage = int(json_dict.get("CritDamage", 125))

	strength = int(json_dict.get("Str", 10))
	dexterity = int(json_dict.get("Dex", 10))
	constitution = int(json_dict.get("Con", 10))
	wisdom = int(json_dict.get("Wis", 10))

	map_id = int(json_dict.get("MapID", 1))
	loc_x = float(json_dict.get("LocX", 0.0))
	loc_y = float(json_dict.get("LocY", 0.0))

	party_id = str(json_dict.get("PartyID", ""))
	party_leader_dbid = int(json_dict.get("PartyLeaderDBID", 0))
	party_members = json_dict.get("PartyMembers", [])

	mecha_type = str(json_dict.get("MechaType", "MS"))

	summoned_horse_uid = int(json_dict.get("SummonedHorseUID", -1))
	rider_state["SummonedHorseUID"] = summoned_horse_uid

	transform_type = int(json_dict.get("TransformType", 0))
	transform_id = str(json_dict.get("TransformID", ""))
	transform_color_index = int(json_dict.get("TransformColorIndex", 0))

	if json_dict.has("Quests"): update_quests(json_dict.get("Quests", []))

	shortcuts = json_dict.get("Shortcuts", json_dict.get("shortcuts", []))

	update_inventory(json_dict.get("Inventory", []), json_dict.get("InventoryMaxSize", 200))
	update_equips(json_dict.get("EquipList", []), json_dict.get("EquipMaxSize", 200))
	pet_summon_max = int(json_dict.get("PetSummonMax", 1))
	update_pets(json_dict.get("PetList", []), json_dict.get("PetMaxSize", 50))
	update_horses(json_dict.get("HorseList", []), json_dict.get("HorseEquipList", []), json_dict.get("HorseEquipMaxSize", 100))
	update_skills(json_dict.get("SkillList", []))


func update_inventory(items: Array, max_size: int = 200) -> void :
	inventory_max_size = max_size
	inventory.clear()
	for item in items:
		inventory.append({
			"in_bank": int(item.get("InBank", item.get("in_bank", 0))), 
			"in_shop": int(item.get("InShop", item.get("in_shop", 0))), 
			"item_uid": int(item.get("ItemUID", item.get("item_uid", 0))), 
			"item_index": int(item.get("ItemIndex", item.get("item_index", 0))), 
			"count": int(item.get("Count", item.get("count", 0)))
		})

func update_equips(equips: Array, max_size: int = 200) -> void :
	equip_max_size = max_size
	equip_list.clear()
	for eq in equips:
		equip_list.append({
			"item_id": int(eq.get("ItemID", eq.get("item_id", 0))), 
			"item_idx": int(eq.get("ItemIdx", eq.get("item_idx", 0))), 
			"damage": int(eq.get("Damage", eq.get("damage", 0))), 
			"color": int(eq.get("Color", eq.get("color", 0))), 
			"in_bank": int(eq.get("InBank", eq.get("in_bank", 0))), 
			"in_shop": int(eq.get("InShop", eq.get("in_shop", 0))), 
			"part": int(eq.get("Part", eq.get("part", 0)))
		})

func update_pets(pets: Array, max_size: int = 50) -> void :
	pet_max_size = max_size
	pet_list.clear()
	for pet in pets:
		pet_list.append({
			"name": str(pet.get("Name", pet.get("name", ""))), 
			"pet_id": int(pet.get("PETID", pet.get("pet_id", 0))), 
			"hp": int(pet.get("HP", pet.get("hp", 0))), 
			"max_hp": int(pet.get("MaxHP", pet.get("max_hp", 100))), 
			"mp": int(pet.get("MP", pet.get("mp", 0))), 
			"max_mp": int(pet.get("MaxMP", pet.get("max_mp", 50))), 
			"attack": int(pet.get("Attack", pet.get("attack", 0))), 
			"defense": int(pet.get("Defense", pet.get("defense", 0))), 
			"magic_attack": int(pet.get("MagicAttack", pet.get("magic_attack", 0))), 
			"magic_defense": int(pet.get("MagicDefense", pet.get("magic_defense", 0))), 
			"hit": int(pet.get("Hit", pet.get("hit", 0))), 
			"dodge": int(pet.get("Dodge", pet.get("dodge", 0))), 
			"con": int(pet.get("CON", pet.get("con", 0))), 
			"str": int(pet.get("STR", pet.get("str", 0))), 
			"wis": int(pet.get("WIS", pet.get("wis", 0))), 
			"dex": int(pet.get("DEX", pet.get("dex", 0))), 
			"extra_str": int(pet.get("ExtraSTR", pet.get("extra_str", 0))), 
			"extra_dex": int(pet.get("ExtraDEX", pet.get("extra_dex", 0))), 
			"extra_con": int(pet.get("ExtraCON", pet.get("extra_con", 0))), 
			"extra_wis": int(pet.get("ExtraWIS", pet.get("extra_wis", 0))), 
			"life_count": int(pet.get("LifeCount", pet.get("life_count", 0))), 
			"recovery_count": int(pet.get("RecoveryCount", pet.get("recovery_count", 0))), 
			"summon_wait_count": int(pet.get("SummonWaitCount", pet.get("summon_wait_count", 0))), 
			"exp": int(pet.get("Exp", pet.get("exp", 0))), 
			"max_exp": int(pet.get("MaxExp", pet.get("max_exp", 1))), 
			"level": int(pet.get("Level", pet.get("level", 1))), 
			"use_exp": int(pet.get("UseExp", pet.get("use_exp", 0))), 
			"feed_exp_limit": int(pet.get("FeedExpLimit", pet.get("feed_exp_limit", 0))), 
			"evol_id": int(pet.get("EvolID", pet.get("evol_id", 0))), 
			"uid": int(pet.get("UID", pet.get("uid", 0))), 
			"equip0": int(pet.get("Equip0", pet.get("equip0", 0))), 
			"equip1": int(pet.get("Equip1", pet.get("equip1", 0))), 
			"equip2": int(pet.get("Equip2", pet.get("equip2", 0))), 
			"feed_value": int(pet.get("FeedValue", pet.get("feed_value", 0))), 
			"exact_feed": float(pet.get("FeedValue", pet.get("feed_value", 0))), 
			"state": int(pet.get("State", pet.get("state", 0))), 
			"main_skill_id": int(pet.get("MainSkillID", pet.get("main_skill_id", 0))), 
			"in_shop": int(pet.get("InShop", pet.get("in_shop", 0))), 
			"shop_price": int(pet.get("ShopPrice", pet.get("shop_price", 0)))
		})

func update_horses(horses: Array, equips: Array, max_horse_eq_size: int = 100) -> void :
	horse_equip_max_size = max_horse_eq_size
	horse_list.clear()
	for horse in horses:
		horse_list.append({
			"horse_uid": int(horse.get("HorseUID", horse.get("horse_uid", 0))), 
			"horse_idx": int(horse.get("HorseIdx", horse.get("horse_idx", 0))), 
			"exp": int(horse.get("Exp", horse.get("exp", 0))), 
			"max_exp": int(horse.get("MaxExp", horse.get("max_exp", 1000))), 
			"energy_left": int(horse.get("EnergyLeft", horse.get("energy_left", 0))), 
			"exact_energy": float(horse.get("EnergyLeft", horse.get("energy_left", 0))), 
			"level": int(horse.get("Level", horse.get("level", 1))), 
			"name": str(horse.get("Name", horse.get("name", ""))), 
			"horse_type": str(horse.get("HorseType", horse.get("horse_type", "MS"))), 
			"equip_color0": int(horse.get("EquipColor0", horse.get("equip_color0", 0))), 
			"equip_color1": int(horse.get("EquipColor1", horse.get("equip_color1", 0))), 
			"equip_color2": int(horse.get("EquipColor2", horse.get("equip_color2", 0))), 
			"equip_color3": int(horse.get("EquipColor3", horse.get("equip_color3", 0))), 
			"angry": int(horse.get("Angry", horse.get("angry", 0)))
		})

	horse_equip_list.clear()
	for heq in equips:
		horse_equip_list.append({
			"equip_uid": int(heq.get("EquipUID", heq.get("equip_uid", 0))), 
			"equip_idx": int(heq.get("EquipIdx", heq.get("equip_idx", 0))), 
			"damage": int(heq.get("Damage", heq.get("damage", 0))), 
			"color": int(heq.get("Color", heq.get("color", 0))), 
			"horse_uid": int(heq.get("HorseUID", heq.get("horse_uid", -1))), 
			"part": int(heq.get("Part", heq.get("part", 255))), 
			"in_bank": int(heq.get("InBank", heq.get("in_bank", 0))), 
			"in_shop": int(heq.get("InShop", heq.get("in_shop", 0)))
		})



func _save_optimistic_backup() -> void :
	_backup_equip_list = equip_list.duplicate(true)
	_backup_paper_doll = {
		"bonnet": bonnet_id, "left": left_hand_id, "right": right_hand_id, 
		"upper": upper_armor_id, "lower": lower_armor_id, "weapon": weapon_id, 
		"shield": shield_id, "back": back_id, "amulet": amulet_id
	}

func rollback_optimistic() -> void :
	if _backup_equip_list.is_empty(): return

	equip_list = _backup_equip_list.duplicate(true)
	bonnet_id = _backup_paper_doll.get("bonnet", 0)
	left_hand_id = _backup_paper_doll.get("left", 0)
	right_hand_id = _backup_paper_doll.get("right", 0)
	upper_armor_id = _backup_paper_doll.get("upper", 0)
	lower_armor_id = _backup_paper_doll.get("lower", 0)
	weapon_id = _backup_paper_doll.get("weapon", 0)
	shield_id = _backup_paper_doll.get("shield", 0)
	back_id = _backup_paper_doll.get("back", 0)
	amulet_id = _backup_paper_doll.get("amulet", 0)

	_backup_equip_list.clear()
	_backup_paper_doll.clear()

func optimistic_equip(target_item_id: int, target_part: int) -> void :
	_save_optimistic_backup()

	var target_item = null
	for eq in equip_list:
		if eq.get("item_id", 0) == target_item_id:
			target_item = eq
			break

	if target_item:
		var item_idx = target_item.get("item_idx", 0)
		for eq in equip_list:
			if eq.get("part", 255) == target_part:
				eq["part"] = 255

		target_item["part"] = target_part
		_update_paper_doll_from_equip_list()

func optimistic_unequip(target_part: int) -> void :
	_save_optimistic_backup()
	for eq in equip_list:
		if eq.get("part", 255) == target_part:
			eq["part"] = 255
	_update_paper_doll_from_equip_list()

func _update_paper_doll_from_equip_list() -> void :
	bonnet_id = 0
	left_hand_id = 0
	right_hand_id = 0
	upper_armor_id = 0
	lower_armor_id = 0
	weapon_id = 0
	shield_id = 0
	back_id = 0
	amulet_id = 0

	for eq in equip_list:
		var p = eq.get("part", 255)
		var idx = eq.get("item_idx", 0)
		match p:
			0: bonnet_id = idx
			1: left_hand_id = idx
			2: right_hand_id = idx
			3: upper_armor_id = idx
			4: lower_armor_id = idx
			5: weapon_id = idx
			6: shield_id = idx
			7: back_id = idx
			8: amulet_id = idx


func apply_equip_response(data: Dictionary) -> void :
	_backup_equip_list.clear()
	_backup_paper_doll.clear()

	bonnet_id = int(data.get("Bonnet", bonnet_id))
	left_hand_id = int(data.get("Left_hand", left_hand_id))
	right_hand_id = int(data.get("Right_hand", right_hand_id))
	upper_armor_id = int(data.get("Upper_armor", upper_armor_id))
	lower_armor_id = int(data.get("Lower_armor", lower_armor_id))
	weapon_id = int(data.get("Weapon", weapon_id))
	shield_id = int(data.get("Shield", shield_id))
	back_id = int(data.get("Back", back_id))
	amulet_id = int(data.get("Amulet", amulet_id))

	max_hp = int(data.get("MaxHP", max_hp))
	max_mp = int(data.get("MaxMP", max_mp))
	max_sp = int(data.get("MaxSP", max_sp))
	attack = int(data.get("Attack", attack))
	defense = int(data.get("Defense", defense))
	magic_attack = int(data.get("MagicAttack", magic_attack))
	magic_defense = int(data.get("MagicDefense", magic_defense))
	hit = int(data.get("Hit", hit))
	dodge = int(data.get("Dodge", dodge))
	lucky = int(data.get("Lucky", lucky))
	move_speed = int(data.get("MoveSpeed", move_speed))
	attack_speed = int(data.get("AttackSpeed", attack_speed))
	attack_range = int(data.get("AttackRange", attack_range))
	crit_rate = int(data.get("CritRate", crit_rate))
	crit_damage = int(data.get("CritDamage", crit_damage))

	var action = data.get("Action", "")
	var target_item_id = int(data.get("ItemID", 0))

	if action == "Unequip":
		for eq in equip_list:
			if eq.get("item_id", 0) == target_item_id:
				eq["part"] = 255
				break
	elif action == "Equip":
		var target_item = null
		for eq in equip_list:
			if eq.get("item_id", 0) == target_item_id:
				target_item = eq
				break

		if target_item:
			var item_idx = target_item.get("item_idx", 0)
			var target_part = ItemLoader.get_item_property(item_idx, "equip_slot", ItemLoader.get_item_property(item_idx, "part", -1))

			for eq in equip_list:
				if eq.get("part", 255) == target_part:
					eq["part"] = 255

			target_item["part"] = target_part

func update_skills(skills: Array) -> void :
	skill_list.clear()
	for s in skills:
		skill_list.append({
			"skill_id": int(s.get("SkillID", s.get("skill_id", 0))), 
			"level": int(s.get("Level", s.get("level", 1)))
		})

func update_quests(quest_array: Array) -> void :
	player_quests.clear()
	for q in quest_array:
		var q_id = int(q.get("QuestID", q.get("quest_id", 0)))
		if q_id > 0:
			player_quests[q_id] = q

func apply_allocate_stat_response(data: Dictionary) -> void :
	exp_use = int(data.get("ExpUse", exp_use))
	strength = int(data.get("STR", strength))
	dexterity = int(data.get("DEX", dexterity))
	constitution = int(data.get("CON", constitution))
	wisdom = int(data.get("WIS", wisdom))

	max_hp = int(data.get("MaxHP", max_hp))
	max_mp = int(data.get("MaxMP", max_mp))
	max_sp = int(data.get("MaxSP", max_sp))
	attack = int(data.get("Attack", attack))
	defense = int(data.get("Defense", defense))
	magic_attack = int(data.get("MagicAttack", magic_attack))
	magic_defense = int(data.get("MagicDefense", magic_defense))
	hit = int(data.get("Hit", hit))
	dodge = int(data.get("Dodge", dodge))
	crit_rate = int(data.get("CritRate", crit_rate))
	crit_damage = int(data.get("CritDamage", crit_damage))
	attack_range = int(data.get("AttackRange", attack_range))
