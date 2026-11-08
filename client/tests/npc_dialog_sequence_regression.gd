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
	seq.note_presented(first)
	_check(not seq.is_awaiting_choice(), "Say is not a pending choice")
	_check(not seq.push_or_begin(second), "second Say waits for click")
	_check(not seq.push_or_begin(final_ask), "Ask waits behind preceding Say")
	_check(seq.defer_close(), "QuestEnd is deferred instead of erasing dialogue")
	_check(seq.has_pending(), "subsequent lines are queued")
	_check(seq.take_next().get("Text") == "玩家回應",
		"click advances to next Say in order")
	var queued_ask: Dictionary = seq.take_next()
	_check(queued_ask.get("Text") == "選擇", "Ask appears only after both Say lines")
	seq.note_presented(queued_ask)
	_check(seq.is_awaiting_choice(), "Ask stays open for player selection")
	var choices: Array = queued_ask.get("Options", [])
	_check(choices.size() == 2 and choices[1].get("TargetNode") == "Answer#1",
		"choice destinations and ordering are untouched")
	_check(seq.take_next().get("DialogType") == "Close",
		"deferred QuestEnd is consumed at the end")
	_check(not seq.has_pending(), "queue is empty when consumed")
	seq.reset()
	_check(not seq.is_active() and not seq.is_awaiting_choice(),
		"closing resets active quest state and choice state")

	_check(seq.push_or_begin(first), "new interaction opens normally")
	_check(not seq.push_or_begin(second), "old dialogue has pending lines")
	var other_npc := {"EntityID": 20, "ScriptName": "Other.txt",
		"DialogType": "Dialog", "Text": "新 NPC"}
	_check(seq.push_or_begin(other_npc), "switching NPC replaces stale dialogue")
	_check(not seq.has_pending(), "switching NPC clears old messages")
	seq.reset()
	_check(not seq.defer_close(), "no active dialogue means close is immediate")

	var batch_a := {"EntityID": 31, "ScriptName": "NQ024_A.txt",
		"DialogBatchId": "execution-A", "DialogType": "ActionDialog", "Text": "第一批"}
	var batch_b := {"EntityID": 31, "ScriptName": "NQ024_A.txt",
		"DialogBatchId": "execution-B", "DialogType": "ActionDialog", "Text": "第二批"}
	_check(seq.push_or_begin(batch_a), "batch A shows immediately")
	_check(not seq.push_or_begin(batch_a), "same execution is queued")
	_check(seq.push_or_begin(batch_b), "new execution from the same NPC supersedes old")
	_check(not seq.has_pending(), "old batch queued lines are discarded")
	_check(seq.is_stale_close({"DialogBatchId": "execution-A"}),
		"late close from prior Lua execution does not dismiss the new one")
	_check(not seq.is_stale_close({"DialogBatchId": "execution-B"}),
		"close for the active Lua batch remains valid")
	_check(not seq.is_stale_close({}),
		"older close packets without batch ID retain compatible behavior")
	seq.reset()

	var continuation := {"EntityID": 31, "ScriptName": "MS008_A.txt",
		"DialogBatchId": "execution-next", "DialogType": "ActionDialog",
		"Text": "請繼續", "NextNode": "Step02"}
	_check(seq.push_or_begin(continuation), "explicit Say continuation begins")
	_check(seq.defer_close(), "terminal QuestEnd can be queued")
	_check(seq.only_terminal_close_pending(),
		"sole terminal close is distinguishable from actual speech")
	seq.push_or_begin({"EntityID": 31, "ScriptName": "MS008_A.txt",
		"DialogBatchId": "execution-next", "DialogType": "ActionDialog",
		"Text": "後續台詞"})
	_check(not seq.only_terminal_close_pending(),
		"actual queued dialogue keeps priority over terminal close")
	seq.reset()

	# Real quest pattern: NQ024_A.txt calls Ask() then QuestEnd().
	# Client must NOT close the ticket choices just because the Lua function ends.
	var train := {"EntityID": 31, "ScriptName": "NQ024_A.txt",
		"DialogType": "Dialog", "Text": "你要買哪種票?",
		"Options": [{"Text": "單程", "TargetNode": "BuyTicket0Reply#0"},
			{"Text": "取消", "TargetNode": "BuyTicket0Reply#1"}]}
	_check(seq.push_or_begin(train), "train ticket question begins as a new dialogue")
	seq.note_presented(train)
	_check(seq.is_awaiting_choice(),
		"Ask then QuestEnd must keep the options clickable")
	_check(not seq.has_pending(), "no artificial close was queued for Ask")
	seq.reset()
	_check(not seq.is_awaiting_choice(), "selection cleanup clears choice hold")
	_check(seq.push_or_begin(first), "dialogue may begin after a completed choice")
	seq.note_presented({"DialogType": "Dialog", "Options": []})
	_check(not seq.is_awaiting_choice(), "non-interactive dialogue does not hold close")
	seq.reset()

	var hud_source := FileAccess.get_file_as_string("res://game_hud.gd")
	var manager_source := FileAccess.get_file_as_string(
		"res://scripts/network_entity_manager.gd")
	var network_source := FileAccess.get_file_as_string(
		"res://scripts/network_manager.gd")
	_check(hud_source.contains("func has_legacy_dialog_line_waiting()") and
		hud_source.contains("_hide_current_dialog_bubbles()") and
		hud_source.contains("_on_npc_dialog_received(next_packet, true)") and
		hud_source.contains("_legacy_dialog_sequence.defer_close()") and
		hud_source.contains("_legacy_dialog_sequence.is_awaiting_choice()") and
		hud_source.contains("_legacy_dialog_sequence.is_stale_close(packet)") and
		hud_source.contains("_legacy_dialog_sequence.only_terminal_close_pending()"),
		"HUD hides prior speech, consumes queued lines, and defers server close")
	_check(manager_source.contains("hud.has_legacy_dialog_line_waiting()") and
		manager_source.contains("hud.request_next_dialog_sentence()"),
		"existing click-to-continue hook remains connected")
	_check(network_source.contains("npc_dialog_close_packet.emit(data)") and
		network_source.contains("npc_dialog_closed.emit()"),
		"batch-aware close metadata does not remove original close signal")

	print("[NPC_SEQUENCE] ", "ALL PASS" if failures == 0 else
		"%d FAILURES" % failures)
	quit(0 if failures == 0 else 1)
