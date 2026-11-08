extends RefCounted
class_name AfkVisualCache

# AFK visual-only resource boundary. Do not free/reparent world, collision,
# monsters, player or network-owned entities: combat/loot targets depend on
# those nodes staying alive. Only recoverable static map textures are released.
# Snapshot stores paths + WeakRefs, never strong Resource references.
var _resources: Array[Dictionary] = []
var _map_ref: WeakRef = null
var _skipped_unrestorable: int = 0


func tracked_map() -> Node:
	return _map_ref.get_ref() if _map_ref != null else null


func discard() -> void:
	_resources.clear()
	_map_ref = null
	_skipped_unrestorable = 0


func suspended_count() -> int:
	return _resources.size()


static func _visual_property(node: Node) -> String:
	# Legacy AnimatedMapObject only adjusts frame offsets; it has no gameplay
	# behavior. Other script-backed nodes or animated event handlers stay live.
	if node is AnimatedSprite2D:
		var animated: AnimatedSprite2D = node as AnimatedSprite2D
		var script: Script = animated.get_script() as Script
		if script != null and script.resource_path != "res://animated_map_object.gd":
			return ""
		if not animated.animation_finished.get_connections().is_empty():
			return ""
		if script == null and not animated.frame_changed.get_connections().is_empty():
			return ""
		return "sprite_frames"
	if node.get_script() != null:
		return ""
	if node is Sprite2D or node is TextureRect or node is Polygon2D or node is NinePatchRect:
		return "texture"
	return ""


static func _restorable_path(resource: Resource) -> String:
	if not is_instance_valid(resource):
		return ""
	var path: String = resource.resource_path
	if not path.begins_with("res://"):
		return ""
	# Embedded scene subresources and runtime/generated textures are not safe
	# to reload by path. Leave these in place instead of losing visual state.
	if path.contains("::") or not ResourceLoader.exists(path):
		return ""
	return path


func suspend_static_map(map_node: Node) -> Dictionary:
	if not is_instance_valid(map_node):
		return {"released": 0, "skipped": 0}
	if tracked_map() != map_node:
		discard()
		_map_ref = weakref(map_node)
	var released: int = 0
	var skipped: int = 0
	var pending: Array[Node] = [map_node]
	while not pending.is_empty():
		var node: Node = pending.pop_back()
		if not is_instance_valid(node) or node.is_queued_for_deletion():
			continue
		# Do not traverse the live world entity container. Player movement,
		# targeting, AOI, combat and loot logic continue during AFK.
		if node != map_node and (
			node.name == "Entities" or node is CollisionObject2D or node.get_script() != null
		):
			continue
		var property_name: String = _visual_property(node)
		if property_name != "":
			var current: Variant = node.get(property_name)
			if current is Resource and is_instance_valid(current):
				var path: String = _restorable_path(current)
				if not path.is_empty():
					var record: Dictionary = {
						"node": weakref(node),
						"property": property_name,
						"path": path,
					}
					if node is AnimatedSprite2D:
						var animated: AnimatedSprite2D = node as AnimatedSprite2D
						record["animation"] = animated.animation
						record["frame"] = animated.frame
						record["playing"] = animated.is_playing()
						animated.stop()
					_resources.append(record)
					node.set(property_name, null)
					released += 1
				else:
					skipped += 1
		for child in node.get_children():
			if child is Node:
				pending.append(child)
	_skipped_unrestorable += skipped
	return {"released": released, "tracked": _resources.size(), "skipped": skipped}


func restore_static_map(batch_size: int = 24) -> Dictionary:
	var restored: int = 0
	var missing: int = 0
	var stale: int = 0
	var retry: Array[Dictionary] = []
	var offset: int = 0
	# Capture a stable snapshot: a server-initiated map transition can replace
	# the active map while we await render frames during restore.
	var restore_map: Node = tracked_map()
	var records: Array[Dictionary] = _resources.duplicate()
	var count: int = records.size()
	# Synchronous ResourceLoader calls are spread across idle frames; this
	# avoids the long freeze of loading all map visuals in one tick.
	while offset < count:
		var end: int = mini(offset + maxi(1, batch_size), count)
		for index in range(offset, end):
			var record: Dictionary = records[index]
			var reference: WeakRef = record["node"]
			var node: Node = reference.get_ref() as Node
			if not is_instance_valid(node) or node.is_queued_for_deletion():
				stale += 1
				continue
			var path: String = record["path"]
			var resource: Resource = ResourceLoader.load(path)
			if resource == null:
				missing += 1
				retry.append(record)
				continue
			# Only touch the property we unloaded; building/NPC animation state
			# and visibility are intentionally left at their latest value.
			node.set(record["property"], resource)
			if node is AnimatedSprite2D and resource is SpriteFrames:
				var animated: AnimatedSprite2D = node as AnimatedSprite2D
				var frames: SpriteFrames = resource as SpriteFrames
				var previous_animation: StringName = record.get("animation", &"default")
				if frames.has_animation(previous_animation):
					animated.animation = previous_animation
					var frame_count: int = frames.get_frame_count(previous_animation)
					if frame_count > 0:
						animated.frame = clampi(int(record.get("frame", 0)), 0, frame_count - 1)
						if bool(record.get("playing", false)):
							animated.play()
			restored += 1
		offset = end
		if offset < count:
			var tree: SceneTree = Engine.get_main_loop() as SceneTree
			if tree != null:
				await tree.process_frame
	if tracked_map() == restore_map:
		_resources = retry
	return {"restored": restored, "missing": missing, "stale": stale}
