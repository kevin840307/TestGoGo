extends RefCounted
class_name AfkMemoryAudit

# Debug-only strong-reference inventory used around AFK/logout transitions.
# Returned reports contain metadata only and never retain Resource objects.
# Texture bytes are an uncompressed RGBA estimate used for ranking, not an
# assertion about Windows working set or exact GPU allocation.
const BYTES_PER_PIXEL_ESTIMATE: int = 4
const MAX_OWNER_SAMPLES: int = 6
const MAX_SCAN_DEPTH: int = 5


static func capture(
	roots: Dictionary,
	cache_sources: Array,
	limit: int = 16
) -> Dictionary:
	var records: Dictionary = {}
	for raw_label in roots.keys():
		var root_node: Node = roots.get(raw_label, null) as Node
		if is_instance_valid(root_node):
			_scan_node_tree(root_node, str(raw_label), records)

	for raw_source in cache_sources:
		if not raw_source is Dictionary:
			continue
		var source: Dictionary = raw_source
		_scan_value(
			source.get("value", null),
			str(source.get("name", "cache")),
			records,
			0
		)

	var entries: Array = []
	var estimated_texture_bytes: int = 0
	var texture_resources: int = 0
	var sprite_frame_resources: int = 0
	for raw_record in records.values():
		var record: Dictionary = raw_record
		var owners_dict: Dictionary = record.get("owners", {})
		var owners: Array = owners_dict.keys()
		owners.sort()
		if owners.size() > MAX_OWNER_SAMPLES:
			owners.resize(MAX_OWNER_SAMPLES)
		var entry: Dictionary = {
			"instance_id": int(record.get("instance_id", 0)),
			"kind": str(record.get("kind", "resource")),
			"class": str(record.get("class", "Resource")),
			"path": str(record.get("path", "")),
			"estimated_bytes": int(record.get("estimated_bytes", 0)),
			"reference_count": int(record.get("reference_count", 0)),
			"reference_sources": owners_dict.size(),
			"owners": owners,
		}
		if entry["kind"] == "texture":
			texture_resources += 1
			estimated_texture_bytes += int(entry["estimated_bytes"])
		elif entry["kind"] == "sprite_frames":
			sprite_frame_resources += 1
		entries.append(entry)

	entries.sort_custom(func(a, b):
		var a_bytes: int = int(a.get("estimated_bytes", 0))
		var b_bytes: int = int(b.get("estimated_bytes", 0))
		if a_bytes != b_bytes:
			return a_bytes > b_bytes
		var a_sources: int = int(a.get("reference_sources", 0))
		var b_sources: int = int(b.get("reference_sources", 0))
		if a_sources != b_sources:
			return a_sources > b_sources
		return int(a.get("reference_count", 0)) > int(b.get("reference_count", 0))
	)
	var safe_limit: int = maxi(0, limit)
	if entries.size() > safe_limit:
		entries.resize(safe_limit)

	return {
		"resources": records.size(),
		"texture_resources": texture_resources,
		"sprite_frame_resources": sprite_frame_resources,
		"estimated_texture_bytes": estimated_texture_bytes,
		"estimated_texture_mb": float(estimated_texture_bytes) / 1048576.0,
		"top": entries,
	}


static func _scan_node_tree(root: Node, root_label: String, records: Dictionary) -> void:
	var pending: Array[Node] = [root]
	while not pending.is_empty():
		var node: Node = pending.pop_back()
		if not is_instance_valid(node):
			continue
		var node_path: String = str(node.get_path()) if node.is_inside_tree() else str(node.name)
		for property_info in node.get_property_list():
			var property_name: StringName = property_info.get("name", &"")
			if property_name == &"script":
				continue
			var usage: int = int(property_info.get("usage", 0))
			if (usage & PROPERTY_USAGE_STORAGE) == 0:
				continue
			var value: Variant = node.get(property_name)
			if value is Resource and not value is Script:
				_record_resource(
					value as Resource,
					"%s:%s.%s" % [root_label, node_path, str(property_name)],
					records
				)
		for child in node.get_children():
			pending.append(child)


static func _scan_value(
	value: Variant,
	source: String,
	records: Dictionary,
	depth: int
) -> void:
	if depth > MAX_SCAN_DEPTH:
		return
	if value is Resource:
		if not value is Script:
			_record_resource(value as Resource, source, records)
		return
	if value is Dictionary:
		var dictionary: Dictionary = value
		for key in dictionary.keys():
			_scan_value(
				dictionary.get(key),
				"%s[%s]" % [source, str(key)],
				records,
				depth + 1
			)
		return
	if value is Array:
		var array: Array = value
		for index in range(array.size()):
			_scan_value(
				array[index],
				"%s[%d]" % [source, index],
				records,
				depth + 1
			)


static func _record_resource(
	resource: Resource,
	source: String,
	records: Dictionary
) -> void:
	if not is_instance_valid(resource):
		return
	var instance_id: int = resource.get_instance_id()
	var record: Dictionary = records.get(instance_id, {})
	if record.is_empty():
		var path: String = resource.resource_path
		if path.is_empty():
			path = "[runtime:%s:%d]" % [resource.get_class(), instance_id]
		record = {
			"instance_id": instance_id,
			"kind": _resource_kind(resource),
			"class": resource.get_class(),
			"path": path,
			"estimated_bytes": _estimate_resource_bytes(resource),
			"reference_count": resource.get_reference_count(),
			"owners": {},
			"expanded": false,
		}
	var owners: Dictionary = record.get("owners", {})
	owners[source] = true
	record["owners"] = owners
	records[instance_id] = record

	if bool(record.get("expanded", false)):
		return
	record["expanded"] = true
	records[instance_id] = record

	# SpriteFrames often owns many AtlasTexture wrappers backed by one large
	# PNG. Attribute those frames to the backing atlas, which is the useful
	# memory target, instead of reporting dozens of tiny wrappers.
	if resource is SpriteFrames:
		var frames: SpriteFrames = resource as SpriteFrames
		for animation in frames.get_animation_names():
			for frame_index in range(frames.get_frame_count(animation)):
				var texture: Texture2D = frames.get_frame_texture(animation, frame_index)
				if not is_instance_valid(texture):
					continue
				var frame_source := "%s->%s[%d]" % [
					source, str(animation), frame_index
				]
				if texture is AtlasTexture:
					var atlas: Texture2D = (texture as AtlasTexture).atlas
					if is_instance_valid(atlas):
						_record_resource(atlas, frame_source + ".atlas", records)
				else:
					_record_resource(texture, frame_source, records)
	elif resource is AtlasTexture:
		var atlas_texture: Texture2D = (resource as AtlasTexture).atlas
		if is_instance_valid(atlas_texture):
			_record_resource(atlas_texture, source + ".atlas", records)


static func _resource_kind(resource: Resource) -> String:
	if resource is Texture2D:
		return "texture"
	if resource is SpriteFrames:
		return "sprite_frames"
	return "resource"


static func _estimate_resource_bytes(resource: Resource) -> int:
	if not resource is Texture2D:
		return 0
	if resource is AtlasTexture:
		# Wrapper is tiny; its backing atlas is recorded separately.
		return 0
	var texture: Texture2D = resource as Texture2D
	return maxi(0, texture.get_width()) * maxi(0, texture.get_height()) * BYTES_PER_PIXEL_ESTIMATE
