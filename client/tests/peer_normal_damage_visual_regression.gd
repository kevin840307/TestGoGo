extends SceneTree
# Self-contained protocol/presentation policy regression, no art assets.
const PEER_POLICY = preload("res://scripts/peer_damage_visual_policy.gd")
var failed: int = 0

func _check(ok: bool, label: String) -> void:
	if ok:
		print("[PEER_DAMAGE_VISUAL_PASS] ", label)
	else:
		failed += 1
		push_error("[PEER_DAMAGE_VISUAL_FAIL] " + label)

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	_check(PEER_POLICY.is_remote_normal_hit(1, 0, 75, 105, 100),
		"visible peer's normal attack uses direct authoritative number")
	_check(not PEER_POLICY.is_remote_normal_hit(1, 0, 75, 100, 100),
		"own normal attack retains its existing impact timing")
	_check(not PEER_POLICY.is_remote_normal_hit(1, 12101, 75, 105, 100),
		"other-player skills preserve their skill hit timeline")
	_check(not PEER_POLICY.is_remote_normal_hit(1, 0, 0, 105, 100),
		"miss never invents damage")
	_check(not PEER_POLICY.is_remote_normal_hit(0, 0, 75, 105, 100),
		"damage to a player uses the hurt animation path")
	_check(not PEER_POLICY.is_remote_normal_hit(1, 0, 75, 1000050, 100),
		"monster-origin damage does not look like peer normal attack")
	_check(PEER_POLICY.is_remote_normal_hit(1, 0, 75, 105, 0),
		"unknown local ID does not suppress real peer normal damage")
	print("[PEER_DAMAGE_VISUAL_TEST] ", "ALL PASS" if failed == 0 else "%d FAILURES" % failed)
	quit(0 if failed == 0 else 1)
