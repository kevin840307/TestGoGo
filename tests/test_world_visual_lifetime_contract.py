from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]

def read(path):
    return (ROOT / path).read_text(encoding="utf-8")

def test_original_login_prewarm_and_afk_ui_are_unchanged():
    entry = read("client/entry_manager.gd")
    assert 'ItemLoader.preload_all_item_data()' in entry
    assert 'SkillLoader.preload_all_skill_data()' in entry
    assert 'PetLoader.preload_all_pet_data()' in entry
    assert 'await _prewarm_game_hud_resources()' in entry
    scene = read("client/game_hud.tscn")
    assert '[node name="AFKOverlay" type="ColorRect"' in scene
    hud = read("client/game_hud.gd")
    assert 'func _on_afk_restore_requested()' in hud
    assert 'func _refresh_afk_dashboard()' in hud

def test_audio_stream_references_released_on_logout_only():
    audio = read("client/scripts/audio_manager.gd")
    game = read("client/scripts/game_manager.gd")
    segment = audio[audio.index("func release_world_audio_for_login()"):
                    audio.index("func set_se_volume(")]
    assert 'bgm_player.stream = null' in segment
    assert 'player.stream = null' in segment
    assert 'clear_runtime_cache()' in segment
    logout = game[game.index("func cleanup_and_return_to_login()"):
                  game.index("func _clear_visual_runtime_caches()")]
    assert "AudioManager.release_world_audio_for_login()" in logout

def test_visual_resource_audit_is_read_only_and_debug_only():
    audit = read("client/scripts/world_visual_audit.gd")
    game = read("client/scripts/game_manager.gd")
    assert 'func snapshot(root: Node) -> Dictionary:' in audit
    assert 'node.get_children()' in audit
    assert 'node.queue_free()' not in audit
    assert 'node.free()' not in audit
    assert 'TextureRect' in audit and 'Sprite2D' in audit
    assert 'if not OS.is_debug_build():' in game
    assert 'log_world_visual_audit("logout_before")' in game
    assert 'log_world_visual_audit("logout_settled")' in game
