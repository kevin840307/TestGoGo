extends RefCounted
## Evict only idle, older Resource/SpriteFrames references. The dictionary
## owns one reference and the local 'resource' holds a second reference while
## checking. An AnimatedSprite2D, Control, in-flight effect, etc. keeps a
## third reference and MUST survive, with no reload needed on focus-in.
## Newly inserted entries are kept as a small warm cache for instant recovery.
static func trim_unused(cache: Dictionary, keep_newest: int = 8) -> int:
	var keys: Array = cache.keys()
	var oldest_count: int = maxi(0, keys.size() - maxi(0, keep_newest))
	var removed: int = 0
	for index in range(oldest_count):
		var key: Variant = keys[index]
		var resource: RefCounted = cache.get(key) as RefCounted
		# Do not evict non-resource metadata or unknown reference types.
		if resource == null:
			continue
		if resource.get_reference_count() <= 2:
			cache.erase(key)
			removed += 1
	return removed
