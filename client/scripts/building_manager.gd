
extends Node


func init_buildings() -> void :

	var buildings = get_tree().get_nodes_in_group("Building")

	for building in buildings:

		var out_sprite = building.get_node_or_null("Sprite_OUT") as Node2D
		var in_sprite = building.get_node_or_null("Sprite_IN") as Node2D
		var detect_area = building.get_node_or_null("DetectionArea") as Area2D


		if not out_sprite or not in_sprite or not detect_area:
			continue


		out_sprite.visible = true
		in_sprite.visible = false


		if not detect_area.body_entered.is_connected(_on_player_entered_building):
			detect_area.body_entered.connect(_on_player_entered_building.bind(out_sprite, in_sprite))

		if not detect_area.body_exited.is_connected(_on_player_exited_building):
			detect_area.body_exited.connect(_on_player_exited_building.bind(out_sprite, in_sprite))


func _on_player_entered_building(body: Node2D, out_sprite: Node2D, in_sprite: Node2D) -> void :
	if body.collision_layer & 2:
		out_sprite.visible = false
		in_sprite.visible = true


func _on_player_exited_building(body: Node2D, out_sprite: Node2D, in_sprite: Node2D) -> void :
	if body.collision_layer & 2:
		out_sprite.visible = true
		in_sprite.visible = false
