extends SceneTree

# Run: godot --headless --path client --script res://tests/display_settings_regression.gd
# Presentation-only policies: packets, combat and authoritative drop snapshots stay intact.
const POLICY = preload("res://scripts/client_performance_profile.gd")
var failures: int = 0

func check(ok: bool, description: String) -> void:
	if ok:
		print("[DISPLAY_SETTINGS_PASS] " + description)
	else:
		failures += 1
		push_error("[DISPLAY_SETTINGS_FAIL] " + description)

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	check(POLICY.should_show_other_player(POLICY.PLAYER_SHOW_ALL, false, false), "all players visible")
	check(POLICY.should_show_other_player(POLICY.PLAYER_SHOW_ENEMIES, false, true), "hostile player visible")
	check(not POLICY.should_show_other_player(POLICY.PLAYER_SHOW_ENEMIES, true, false), "party hidden in enemies-only")
	check(POLICY.should_show_other_player(POLICY.PLAYER_SHOW_PARTY, true, false), "party player visible")
	check(not POLICY.should_show_other_player(POLICY.PLAYER_SHOW_PARTY, false, true), "enemy hidden in party-only")
	check(not POLICY.should_show_other_player(POLICY.PLAYER_SHOW_NONE, true, true), "hide all remote players")
	check(POLICY.should_show_ordinary_monster(POLICY.MONSTER_SHOW_ALL, false), "ordinary monsters shown")
	check(not POLICY.should_show_ordinary_monster(POLICY.MONSTER_HIDE_ORDINARY, false), "ordinary monsters hidden")
	check(POLICY.should_show_ordinary_monster(POLICY.MONSTER_HIDE_ORDINARY, true), "boss remains visible")

	var low: Dictionary = POLICY.resolve(POLICY.LOAD_LOW, POLICY.COMBAT_RESPONSE)
	var quality: Dictionary = POLICY.resolve(POLICY.LOAD_QUALITY, POLICY.COMBAT_NORMAL)
	var low_visible: Dictionary = POLICY.apply_visibility_overrides(low, POLICY.PLAYER_SHOW_PARTY, POLICY.MONSTER_HIDE_ORDINARY)
	var quality_visible: Dictionary = POLICY.apply_visibility_overrides(quality, POLICY.PLAYER_SHOW_PARTY, POLICY.MONSTER_HIDE_ORDINARY)
	check(low_visible["other_players_visibility"] == quality_visible["other_players_visibility"], "player visibility independent from quality")
	check(low_visible["ordinary_monsters_visibility"] == quality_visible["ordinary_monsters_visibility"], "monster visibility independent from quality")

	var records: Dictionary = {}
	for i in range(7):
		records[10 + i] = {"LocX": float(i * 12), "LocY": 0.0, "ExtraData": {"ItemIndex": 101, "Count": 1}}
	records[17] = {"LocX": 30.0, "LocY": 0.0, "ExtraData": {"ItemIndex": 102, "Count": 1}}
	check(POLICY.should_show_duplicate_drop(14, records[14], records, Vector2.ZERO, 1000.0), "fifth same-item drop visible")
	check(not POLICY.should_show_duplicate_drop(15, records[15], records, Vector2.ZERO, 1000.0), "sixth same-item drop suppressed")
	check(not POLICY.should_show_duplicate_drop(16, records[16], records, Vector2.ZERO, 1000.0), "seventh same-item drop suppressed")
	check(POLICY.should_show_duplicate_drop(17, records[17], records, Vector2.ZERO, 1000.0), "different item type unaffected")
	check(POLICY.should_show_duplicate_drop(15, records[15], records, Vector2.ZERO, 5.0), "drops outside observation radius not counted")
	check(records.size() == 8 and records.has(15) and records.has(16), "no drop records removed")

	var scene_source: String = FileAccess.get_file_as_string("res://game_hud.tscn")
	check(scene_source.contains('[node name="Tabs" type="TabContainer"'), "system setting categories have tabs")
	check(scene_source.contains('anchor_left = 0.5') and scene_source.contains('anchor_right = 0.5'), "setting window centered")
	check(scene_source.contains('popup/item_3/text = "都不顯示"'), "fixed four-state other-player visibility")
	check(scene_source.contains('[node name="OptionSimplifyDrops" type="OptionButton"'), "duplicate ground drop selector exists")
	check(scene_source.contains('[node name="OptionShowDamage" type="OptionButton"'), "damage number selector exists")
	check(scene_source.contains('[node name="OptionSimplifyEffects" type="OptionButton"'), "effect simplification selector exists")
	print("[DISPLAY_SETTINGS_REGRESSION] %s" % ("ALL PASS" if failures == 0 else "FAILURES=%d" % failures))
	quit(0 if failures == 0 else 1)
