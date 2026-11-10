extends SceneTree

# Run: godot --headless --path client --script res://tests/multiplayer_motion_audit_regression.gd
# Debug packet statistics must be bounded and never mutate gameplay state.

var failures: int = 0


func _check(ok: bool, description: String) -> void:
	if ok:
		print("[MOTION_AUDIT_PASS] ", description)
	else:
		failures += 1
		push_error("[MOTION_AUDIT_FAIL] " + description)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	await process_frame
	var audit := MultiplayerMotionAudit.new()
	audit.record_move(101, 0, 1000, 0.1, 10.0)
	audit.record_move(101, 0, 1100, 0.1, 20.0)
	audit.record_move(1200101, 1, 1000, 0.25, 35.0)
	audit.record_move(1200101, 1, 1250, 0.25, 45.0)
	var stats: Dictionary = audit.snapshot()
	var players: Dictionary = stats.get("players", {})
	var monsters: Dictionary = stats.get("monsters", {})
	_check(int(players.get("moves", 0)) == 2
		and int(monsters.get("moves", 0)) == 2,
		"remote player and monster statistics remain separated")
	_check(is_equal_approx(float(players.get("receive_gap_avg_ms", 0.0)), 100.0)
		and is_equal_approx(float(monsters.get("receive_gap_avg_ms", 0.0)), 250.0),
		"receive gap measures actor time, not the global mixed-packet rate")
	_check(is_equal_approx(float(monsters.get("server_interval_avg_ms", 0.0)), 250.0),
		"server SpeedTime is tracked independently of receive gap")
	_check(str(monsters.get("receive_gap_p95_bucket_ms", "")) == "<=350",
		"p95 reports an explicit approximate latency bucket")

	# Despawn/reset must not preserve stale actor jitter or long-session state.
	audit.forget_entity(101)
	audit.record_move(101, 0, 1600, 0.1, 5.0)
	stats = audit.snapshot()
	players = stats.get("players", {})
	_check(int(players.get("receive_gap_max_ms", 0)) == 100,
		"despawned actors do not introduce a spurious 500ms receive gap")
	audit.reset()
	_check(audit.snapshot().is_empty(), "map transition resets debug metrics")
	audit.record_move(101, 0, 10000, 0.1, 10.0)
	audit.report_if_due(20000)
	_check(audit.snapshot().is_empty(), "10s report flushes aggregation")

	print("[MOTION_AUDIT] ", "ALL PASS" if failures == 0 else "%d FAILURES" % failures)
	quit(0 if failures == 0 else 1)
