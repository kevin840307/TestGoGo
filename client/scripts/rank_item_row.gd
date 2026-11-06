
extends Control
class_name RankItemRow

@onready var label_rank = $RankLabel
@onready var label_name = $NameLabel
@onready var label_value = $ValueLabel

var target_dbid: int = -1


func setup(rank: int, player_name: String, value: int, dbid: int, is_money_rank: bool) -> void :
	target_dbid = dbid
	label_rank.text = str(rank)
	label_name.text = player_name

	if is_money_rank:
		label_value.text = str(value)
	else:
		label_value.text = str(value)

	if GameManager.current_player != null and dbid == GameManager.current_player.data_model.db_id:
		label_rank.add_theme_color_override("font_color", Color(1.0, 0.94, 0.17))
		label_name.add_theme_color_override("font_color", Color(1.0, 0.94, 0.17))
		label_value.add_theme_color_override("font_color", Color(1.0, 0.94, 0.17))
