extends RefCounted
class_name AfkRenderShell

var _shell: Node2D = null
var _entities: Node = null
var _map_name: String = ""
var _container_name: String = ""


func is_active() -> bool:
	return is_instance_valid(_shell) and is_instance_valid(_entities)


func current_shell() -> Node2D:
	return _shell if is_active() else null


func preserved_entities() -> Node:
	return _entities if is_active() else null


func discard() -> void:
	_shell = null
	_entities = null
	_map_name = ""
	_container_name = ""


func enter(map_node: Node2D, entities: Node) -> Dictionary:
	if not is_instance_valid(map_node) or not is_instance_valid(entities):
		return {"ok": false, "reason": "invalid_map_or_entities"}
	if entities == map_node or not map_node.is_ancestor_of(entities):
		return {"ok": false, "reason": "entities_not_under_map"}
	var parent: Node = map_node.get_parent()
	if not is_instance_valid(parent):
		return {"ok": false, "reason": "map_has_no_parent"}

	_map_name = str(map_node.name)
	_container_name = str(entities.name)
	var before_nodes: int = _count_nodes(map_node)

	var shell := Node2D.new()
	shell.name = map_node.name
	shell.visible = false
	parent.add_child(shell)
	shell.global_transform = map_node.global_transform

	# Reparenting the whole dynamic subtree preserves the exact CharacterBody,
	# collision, timers, packet-owned entity identity and physics state.
	entities.reparent(shell, true)
	entities.name = "Entities"
	_shell = shell
	_entities = entities

	# The map is already hidden and live entities have moved into the shell.
	# Queue recursive destruction so the existing AFK overlay can be drawn
	# before node teardown, instead of making the click look like a freeze.
	map_node.queue_free()
	return {
		"ok": true,
		"map_name": _map_name,
		"freed_static_nodes": maxi(0, before_nodes - _count_nodes(entities) - 1),
		"preserved_entity_nodes": _count_nodes(entities),
	}


func attach_to_restored_map(restored_map: Node2D) -> Dictionary:
	if not is_active() or not is_instance_valid(restored_map):
		return {"ok": false, "reason": "inactive_or_invalid_restore"}
	var parent: Node = _shell.get_parent()
	if not is_instance_valid(parent):
		return {"ok": false, "reason": "shell_has_no_parent"}

	restored_map.visible = false
	parent.add_child(restored_map)
	restored_map.global_transform = _shell.global_transform

	# Generated map scenes normally contain an empty Entities placeholder.
	# Replace only that presentation-owned placeholder; never recreate the
	# preserved dynamic subtree or its client/server state.
	var placeholder: Node = restored_map.get_node_or_null("Entities")
	if is_instance_valid(placeholder) and placeholder != _entities:
		restored_map.remove_child(placeholder)
		placeholder.free()

	_entities.reparent(restored_map, true)
	_entities.name = "Entities"
	var old_shell: Node2D = _shell
	var restored_entity_nodes: int = _count_nodes(_entities)
	discard()
	if is_instance_valid(old_shell):
		old_shell.free()
	return {
		"ok": true,
		"restored_entity_nodes": restored_entity_nodes,
	}


static func _count_nodes(root: Node) -> int:
	if not is_instance_valid(root):
		return 0
	var total: int = 0
	var pending: Array[Node] = [root]
	while not pending.is_empty():
		var node: Node = pending.pop_back()
		if not is_instance_valid(node):
			continue
		total += 1
		for child in node.get_children():
			if child is Node:
				pending.append(child)
	return total
