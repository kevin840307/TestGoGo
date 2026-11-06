
extends Node

const QUEST_CONFIG_PATH = "res://Assets/Quest/QuestConfig.json"

var quest_database: Dictionary = {}
var is_data_loaded: bool = false

func _ready() -> void :
	load_all_quest_data()

func load_all_quest_data() -> void :
	if is_data_loaded: return

	if not FileAccess.file_exists(QUEST_CONFIG_PATH):
		print("⚠️ [QuestLoader] 找不到任務設定檔：", QUEST_CONFIG_PATH)
		return

	var file = FileAccess.open(QUEST_CONFIG_PATH, FileAccess.READ)
	var json_str = file.get_as_text()
	var json = JSON.new()

	if json.parse(json_str) == OK:
		var raw_data = json.get_data()
		if raw_data is Dictionary:
			for key in raw_data.keys():
				quest_database[int(key)] = raw_data[key]
		is_data_loaded = true
	else:
		printerr("❌ [QuestLoader] 解析 QuestConfig.json 失敗！")

func get_quest_config(quest_id: int) -> Dictionary:
	return quest_database.get(quest_id, {})
