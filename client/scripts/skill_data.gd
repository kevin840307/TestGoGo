class_name SkillData
extends Resource

@export var id: int = 0
@export var skill_name: String = ""
@export var description: String = ""
@export var skill_type: int = 0
@export var damage_type: int = 0
@export var icon_id: int = 0


@export var fx_use: String = ""
@export var fx_hit: String = ""
@export var fx_extra: String = ""

@export var special_skill: String = ""


@export var levels: Array[SkillLevelData] = []


func get_level_data(lvl: int) -> SkillLevelData:
	if lvl > 0 and lvl <= levels.size():
		return levels[lvl - 1]
	return null
