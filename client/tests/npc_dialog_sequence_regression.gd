extends SceneTree

# Run without a server or game scene:
# godot --headless --path client --script res://tests/npc_dialog_sequence_regression.gd

var failures := 0


func _check(condition: bool, name: String) -> void:
	if condition:
		print("[NPC_SEQUENCE_PASS] ", name)
	else:
		failures += 1
		push_error("[NPC_SEQUENCE_FAIL] " + name)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var source := load("res://scripts/legacy_dialog_sequence.gd") as GDScript
	_check(source != null, "sequence helper parses")
	if source == null:
		quit(1)
		return
	var seq = source.new()
	var first := {"EntityID": 10, "ScriptName": "MS007_C.txt",
		"DialogType": "ActionDialog", "Text": "NPC 台詞", "NextNode": ""}
	var second := {"EntityID": 10, "ScriptName": "MS007_C.txt",
		"DialogType": "ActionDialog", "Text": "玩家回應", "NextNode": ""}
	var final_ask := {"EntityID": 10, "ScriptName": "MS007_C.txt",
		"DialogType": "Dialog", "Text": "選擇",
		"Options": [{"Text": "確定", "TargetNode": "Answer#0"},
			{"Text": "取消", "TargetNode": "Answer#1"}]}

	_check(seq.push_or_begin(first), "first Say appears immediately")
	_check(not seq.push_or_begin(second), "second Say waits for click")
	_check(not seq.push_or_begin(final_ask), "Ask waits behind preceding Say")
	_check(seq.defer_close(), "QuestEnd is deferred instead of erasing dialogue")
	_check(seq.has_pending(), "subsequent lines are queued")
	_check(seq.take_next().get("Text") == "玩家回應",
		"click advances to next Say in order")
	var queued_ask: Dictionary = seq.take_next()
	_check(queued_ask.get("Text") == "選擇", "Ask appears only after both Say lines")
	var choices: Array = queued_ask.get("Options", [])
	_check(choices.size() == 2 and choices[1].get("TargetNode") == "Answer#1",
		"choice destinations and ordering are untouched")
	_check(seq.take_next().get("DialogType") == "Close",
		"deferred QuestEnd is consumed at the end")
	_check(not seq.has_pending(), "queue is empty when consumed")
	seq.reset()
	_check(not seq.is_active(), "closing resets active quest state")

	_check(seq.push_or_begin(first), "new interaction opens normally")
	_check(not seq.push_or_begin(second), "old dialogue has pending lines")
	var other_npc := {"EntityID": 20, "ScriptName": "Other.txt",
		"DialogType": "Dialog", "Text": "新 NPC"}
	_check(seq.push_or_begin(other_npc), "switching NPC replaces stale dialogue")
	_check(not seq.has_pending(), "switching NPC clears old messages")
	seq.reset()
	_check(not seq.defer_close(), "no active dialogue means close is immediate")

	var hud_source := FileAccess.get_file_as_string("res://game_hud.gd")
	var manager_source := FileAccess.get_file_as_string(
		"res://scripts/network_entity_manager.gd")
	_check(hud_source.contains("func has_legacy_dialog_line_waiting()") and
		hud_source.contains("_on_npc_dialog_received(next_packet, true)") and
		hud_source.contains("_legacy_dialog_sequence.defer_close()"),
		"HUD consumes queued lines and defers server close")
	_check(manager_source.contains("hud.has_legacy_dialog_line_waiting()") and
		manager_source.contains("hud.request_next_dialog_sentence()"),
		"existing click-to-continue hook remains connected")

	print("[NPC_SEQUENCE] ", "ALL PASS" if failures == 0 else
		"%d FAILURES" % failures)
	quit(0 if failures == 0 else 1)
