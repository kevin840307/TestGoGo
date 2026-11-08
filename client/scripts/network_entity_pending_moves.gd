extends RefCounted

# MoveEntity may be dispatched before its SpawnEntity when map and AI
# notifications are produced by different server tasks. Retain only the
# newest position for a short time; never instantiate an unknown entity.
# A Despawn marks the ID temporarily so a delayed move cannot revive it.
const PENDING_TTL_MSEC: int = 3000
const DESPAWN_TTL_MSEC: int = 10000
const MAX_PENDING: int = 256
const MAX_DESPAWNED: int = 512

var _pending: Dictionary = {}
var _despawned: Dictionary = {}


func record_move(entity_id: int, packet: Dictionary, now_msec: int) -> void:
	if entity_id <= 0:
		return
	_prune(now_msec)
	if _despawned.has(entity_id):
		return
	# Only coordinates and optional facing affect a newly spawned entity.
	# Do not retain entire untrusted packets/ExtraData in a global cache.
	_pending[entity_id] = {
		"X": float(packet.get("X", 0.0)),
		"Y": float(packet.get("Y", 0.0)),
		"Dir": str(packet.get("Dir", "")),
		"at": now_msec
	}
	if _pending.size() > MAX_PENDING:
		_trim_oldest(_pending, MAX_PENDING)


func consume_spawn_move(entity_id: int, now_msec: int) -> Dictionary:
	_prune(now_msec)
	# A new server SpawnEntity explicitly starts a new AOI incarnation.
	_despawned.erase(entity_id)
	var item: Dictionary = _pending.get(entity_id, {})
	_pending.erase(entity_id)
	if item.is_empty() or now_msec - int(item.get("at", 0)) > PENDING_TTL_MSEC:
		return {}
	return {
		"X": float(item.get("X", 0.0)),
		"Y": float(item.get("Y", 0.0)),
		"Dir": str(item.get("Dir", ""))
	}


func mark_despawned(entity_id: int, now_msec: int) -> void:
	if entity_id <= 0:
		return
	_pending.erase(entity_id)
	_despawned[entity_id] = now_msec
	_prune(now_msec)
	if _despawned.size() > MAX_DESPAWNED:
		_trim_oldest(_despawned, MAX_DESPAWNED)


func clear() -> void:
	_pending.clear()
	_despawned.clear()


func pending_count() -> int:
	return _pending.size()


func _prune(now_msec: int) -> void:
	# Bounded caches are tiny; prune on packet activity rather than every
	# frame. Expiry prevents a delayed move surviving a long scene load.
	if _pending.size() > 0:
		for entity_id in _pending.keys():
			var entry: Dictionary = _pending[entity_id] as Dictionary
			if now_msec - int(entry.get("at", 0)) > PENDING_TTL_MSEC:
				_pending.erase(entity_id)
	if _despawned.size() > 0:
		for entity_id in _despawned.keys():
			if now_msec - int(_despawned[entity_id]) > DESPAWN_TTL_MSEC:
				_despawned.erase(entity_id)


func _trim_oldest(items: Dictionary, keep_count: int) -> void:
	# Only runs when a pathological burst exceeds the cap, not on normal
	# movement. Drops the oldest first; never grows with session duration.
	while items.size() > keep_count:
		var oldest_key: Variant = null
		var oldest_at: int = 9223372036854775807
		for key in items.keys():
			var raw: Variant = items[key]
			var at: int = int((raw as Dictionary).get("at", 0)) if raw is Dictionary else int(raw)
			if at < oldest_at:
				oldest_at = at
				oldest_key = key
		if oldest_key == null:
			break
		items.erase(oldest_key)
