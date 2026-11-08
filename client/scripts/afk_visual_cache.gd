extends RefCounted
class_name AfkVisualCache

# AFK visual-only resource boundary. Do not free/reparent world, collision,
# monsters, player or network-owned entities: combat/loot targets depend on
# those nodes staying alive. Only recoverable static map textures are released.
# Snapshot stores paths + WeakRefs, never strong Resource references.
var _resources: Array[Dictionary] = []
var _map_ref: WeakRef = null
var _skipped_unrestorable: int = 0
var _last_scan_stats: Dictionary = {}


func get_last_scan_stats() -> Dictionary:
	return _last_scan_stats.duplicate()



func tracked_map() -> Node:
	return _map_ref.get_ref() if _map_ref != null else null


func discard() -> void:
	_resources.clear()
	_map_ref = null
	_skipped_unrestorable = 0
	_last_scan_stats.clear()


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
	var visited: int = 0
	var visual_candidates: int = 0
	var resource_candidates: int = 0
	var excluded_entities: int = 0
	var excluded_collision: int = 0
	var excluded_scripted: int = 0
	var retained_embedded: int = 0
	var retained_generated: int = 0
	var retained_missing: int = 0
	var pending: Array[Node] = [map_node]
	while not pending.is_empty():
		var node: Node = pending.pop_back()
		if not is_instance_valid(node) or node.is_queued_for_deletion():
			continue
		visited += 1
		# These subtrees are deliberately kept for movement, monster AI,
		# networking, combat targeting, pickup and quest state.
		if node != map_node:
			if node.name == "Entities":
				excluded_entities += 1
				continue
			if node is CollisionObject2D:
				excluded_collision += 1
				continue
			if node.get_script() != null:
				excluded_scripted += 1
				continue
		var property_name: String = _visual_property(node)
		if property_name != "":
			visual_candidates += 1
			var current: Variant = node.get(property_name)
			if current is Resource and is_instance_valid(current):
				resource_candidates += 1
				var resource_path: String = (current as Resource).resource_path
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
					if resource_path.contains("::"):
						retained_embedded += 1
					elif resource_path.is_empty() or not resource_path.begins_with("res://"):
						retained_generated += 1
					else:
						retained_missing += 1
		for child in node.get_children():
			if child is Node:
				pending.append(child)
	_skipped_unrestorable += skipped
	_last_scan_stats = {
		"released": released,
		"tracked": _resources.size(),
		"skipped": skipped,
		"visited_nodes": visited,
		"visual_candidates": visual_candidates,
		"resource_candidates": resource_candidates,
		"excluded_entities": excluded_entities,
		"excluded_collision": excluded_collision,
		"excluded_scripted": excluded_scripted,
		"retained_embedded": retained_embedded,
		"retained_generated": retained_generated,
		"retained_missing": retained_missing,
	}
	# Do not conflate released property references with reclaimed bytes:
	# maps, ResourceLoader and other nodes may still hold shared resources.
	return _last_scan_stats.duplicate()


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
