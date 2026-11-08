extends RefCounted
class_name WorldVisualAudit

# Debug-only inventory of client-side presentation. Counts *live references*
# and nodes, not authoritative server state or exact OS RAM allocation.
static func snapshot(root: Node) -> Dictionary:
	var stats: Dictionary = {
		"nodes": 0, "canvas": 0, "sprite2d": 0,
		"animated_sprites": 0, "texture_rects": 0,
		"tilemaps": 0, "subviewports": 0,
		"controls": 0, "collision": 0,
		"unique_textures": 0, "unique_sprite_frames": 0,
	}
	if not is_instance_valid(root):
		return stats
	var textures: Dictionary = {}
	var frames: Dictionary = {}
	var pending: Array[Node] = [root]
	while not pending.is_empty():
		var node: Node = pending.pop_back()
		if not is_instance_valid(node):
			continue
		stats["nodes"] += 1
		if node is CanvasItem:
			stats["canvas"] += 1
		if node is Control:
			stats["controls"] += 1
		if node is SubViewport:
			stats["subviewports"] += 1
		if node is CollisionObject2D:
			stats["collision"] += 1
		if node is TileMapLayer:
			stats["tilemaps"] += 1
		if node is AnimatedSprite2D:
			stats["animated_sprites"] += 1
			var sprite_frames := (node as AnimatedSprite2D).sprite_frames
			if is_instance_valid(sprite_frames):
				frames[sprite_frames.get_instance_id()] = true
		elif node is Sprite2D:
			stats["sprite2d"] += 1
			var texture := (node as Sprite2D).texture
			if is_instance_valid(texture):
				textures[texture.get_instance_id()] = true
		elif node is TextureRect:
			stats["texture_rects"] += 1
			var texture := (node as TextureRect).texture
			if is_instance_valid(texture):
				textures[texture.get_instance_id()] = true
		for child in node.get_children():
			pending.append(child)
	stats["unique_textures"] = textures.size()
	stats["unique_sprite_frames"] = frames.size()
	return stats
