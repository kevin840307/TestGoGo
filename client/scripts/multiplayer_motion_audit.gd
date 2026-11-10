extends RefCounted
class_name MultiplayerMotionAudit

# Debug-only aggregated sampling: no per-packet printing or network changes.
# Coordinate delta is the distance to the latest server waypoint on receipt,
# NOT authoritative gameplay position error or actual network RTT.
const ACTOR_LIMIT: int = 2048
const REPORT_INTERVAL_MSEC: int = 10000
const GAP_BUCKET_UPPER_MSEC: Array[int] = [50, 100, 200, 350, 500, 1000]
const DISTANCE_BUCKET_UPPER_PX: Array[int] = [8, 16, 32, 64, 128, 256]

var _last_received_by_actor: Dictionary = {}
var _stats_by_type: Dictionary = {}
var _report_start_msec: int = -1


func reset() -> void:
	_last_received_by_actor.clear()
	_stats_by_type.clear()
	_report_start_msec = -1


func forget_entity(entity_id: int) -> void:
	_last_received_by_actor.erase(entity_id)


func record_move(entity_id: int, entity_type: int, now_msec: int,
	server_seconds: float, waypoint_distance_px: float,
	snapshot_span_seconds: float = 0.0, coalesced_moves: int = 1) -> void:
	if entity_id <= 0:
		return
	if _report_start_msec < 0:
		_report_start_msec = now_msec

	var group: String = "players" if entity_type == 0 else (
		"monsters" if entity_type == 1 else "other"
	)
	var stats: Dictionary = _stats_by_type.get(group, {})
	if stats.is_empty():
		stats = {
			"count": 0, "gap_count": 0, "gap_sum": 0, "gap_max": 0,
			"gap_hist": [0, 0, 0, 0, 0, 0, 0],
			"distance_sum": 0.0, "distance_max": 0.0,
			"distance_hist": [0, 0, 0, 0, 0, 0, 0],
			"server_sum_ms": 0.0, "server_min_ms": 999999.0,
			"server_max_ms": 0.0,
			"coalesced_packets": 0, "coalesced_moves_total": 0,
			"coalesced_span_max_ms": 0.0
		}
	stats["count"] = int(stats["count"]) + 1

	var last_msec: int = int(_last_received_by_actor.get(entity_id, -1))
	if last_msec >= 0 and now_msec >= last_msec:
		var gap: int = now_msec - last_msec
		stats["gap_count"] = int(stats["gap_count"]) + 1
		stats["gap_sum"] = int(stats["gap_sum"]) + gap
		stats["gap_max"] = maxi(int(stats["gap_max"]), gap)
		var gaps: Array = stats["gap_hist"]
		gaps[_bucket_index(float(gap), GAP_BUCKET_UPPER_MSEC)] += 1
	_last_received_by_actor[entity_id] = now_msec

	var distance: float = maxf(waypoint_distance_px, 0.0)
	stats["distance_sum"] = float(stats["distance_sum"]) + distance
	stats["distance_max"] = maxf(float(stats["distance_max"]), distance)
	var distances: Array = stats["distance_hist"]
	distances[_bucket_index(distance, DISTANCE_BUCKET_UPPER_PX)] += 1

	var server_ms: float = maxf(server_seconds, 0.0) * 1000.0
	stats["server_sum_ms"] = float(stats["server_sum_ms"]) + server_ms
	stats["server_min_ms"] = minf(float(stats["server_min_ms"]), server_ms)
	stats["server_max_ms"] = maxf(float(stats["server_max_ms"]), server_ms)
	if coalesced_moves > 1:
		stats["coalesced_packets"] = int(stats["coalesced_packets"]) + 1
		stats["coalesced_moves_total"] = int(stats["coalesced_moves_total"]) + coalesced_moves
		stats["coalesced_span_max_ms"] = maxf(
			float(stats["coalesced_span_max_ms"]), maxf(0.0, snapshot_span_seconds) * 1000.0
		)
	_stats_by_type[group] = stats

	# Long sessions cannot retain every historical AOI incarnation.
	if _last_received_by_actor.size() > ACTOR_LIMIT:
		_last_received_by_actor.clear()


static func _bucket_index(value: float, upper_bounds: Array[int]) -> int:
	for index in upper_bounds.size():
		if value <= float(upper_bounds[index]):
			return index
	return upper_bounds.size()


static func _approx_p95(histogram: Array, upper_bounds: Array[int]) -> String:
	var total: int = 0
	for count in histogram:
		total += int(count)
	if total <= 0:
		return "-"
	var threshold: int = int(ceil(float(total) * 0.95))
	var accumulated: int = 0
	for i in histogram.size():
		accumulated += int(histogram[i])
		if accumulated >= threshold:
			return ("<=%d" % upper_bounds[i]) if i < upper_bounds.size() else (
			">%d" % upper_bounds[upper_bounds.size() - 1]
			)
	return "-"


func snapshot() -> Dictionary:
	var result: Dictionary = {}
	for group in _stats_by_type.keys():
		var stats: Dictionary = _stats_by_type[group]
		var count: int = int(stats.get("count", 0))
		var gap_count: int = int(stats.get("gap_count", 0))
		result[group] = {
			"moves": count,
			"receive_gap_avg_ms": float(stats["gap_sum"]) / float(maxi(gap_count, 1)),
			"receive_gap_p95_bucket_ms": _approx_p95(
				stats["gap_hist"], GAP_BUCKET_UPPER_MSEC
			),
			"receive_gap_max_ms": int(stats["gap_max"]),
			"server_interval_avg_ms": float(stats["server_sum_ms"]) / float(maxi(count, 1)),
			"server_interval_min_ms": float(stats["server_min_ms"]),
			"server_interval_max_ms": float(stats["server_max_ms"]),
			"coalesced_packets": int(stats["coalesced_packets"]),
			"coalesced_moves_total": int(stats["coalesced_moves_total"]),
			"coalesced_span_max_ms": float(stats["coalesced_span_max_ms"]),
			"waypoint_distance_avg_px": float(stats["distance_sum"]) / float(maxi(count, 1)),
			"waypoint_distance_p95_bucket_px": _approx_p95(
				stats["distance_hist"], DISTANCE_BUCKET_UPPER_PX
			),
			"waypoint_distance_max_px": float(stats["distance_max"])
		}
	return result


func report_if_due(now_msec: int) -> void:
	if _report_start_msec < 0 or now_msec - _report_start_msec < REPORT_INTERVAL_MSEC:
		return
	if not _stats_by_type.is_empty():
		print("[MULTIPLAYER_MOTION_AUDIT] ", JSON.stringify(snapshot()))
	_stats_by_type.clear()
	_report_start_msec = now_msec
