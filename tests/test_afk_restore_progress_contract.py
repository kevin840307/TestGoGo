from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")

def test_original_afk_monitor_is_retained_with_real_progress():
    hud = read("client/game_hud.gd")
    scene = read("client/game_hud.tscn")
    game = read("client/scripts/game_manager.gd")
    assert 'name="AFKOverlay"' in scene
    assert 'name="MonitorCard" type="Panel" parent="AFKOverlay"' in scene
    assert 'name="RestoreProgress" type="ProgressBar" parent="AFKOverlay/MonitorCard"' in scene
    assert 'script = ExtResource("sgo_smoothbar")' in scene
    assert 'GameManager.finish_afk_mode(' in hud
    assert 'Callable(self, "_on_afk_restore_progress")' in hud
    assert 'afk_restore_progress.set_target_progress(percent)' in hud
    assert 'func _restore_afk_entity_visuals_batched(callback: Callable)' in game
    assert 'ResourceLoader.load_threaded_get_status(path, progress)' in game
    assert 'await _restore_afk_entity_visuals_batched(progress_callback)' in game

def test_afk_restore_progress_does_not_change_transport_or_entity_identity():
    game = read("client/scripts/game_manager.gd")
    segment = game[game.index("func _restore_afk_entity_visuals_batched("):
                   game.index("func begin_afk_mode()")]
    assert "NetworkManager.send_" not in segment
    assert "clear_map_entities" not in segment
    assert "instantiate_entity" not in segment
    assert "set_afk_visual_suspended(false)" in segment
    assert "await get_tree().process_frame" in segment
    assert 'current_player' in segment
    assert 'NetworkEntityManager.active_entities.values()' in segment
