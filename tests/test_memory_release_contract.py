from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
def read(path): return (ROOT / path).read_text(encoding="utf-8")

def test_login_is_world_resource_cold():
    entry = read("client/entry_manager.gd")
    boot = entry[entry.index("func _bootstrap_before_login()"):entry.index("func _start_game_entry_flow()")]
    assert "preload_all_item_data()" not in boot
    assert "preload_all_skill_data()" not in boot
    assert "preload_all_pet_data()" not in boot
    assert "_prepare_world_runtime_resources" not in boot
    selected = entry[entry.index("func _on_character_verified_and_enter_game"):entry.index("func _on_server_enter_world_success")]
    assert "await _prepare_world_runtime_resources" in selected

def test_entity_packed_scenes_are_releasable():
    manager = read("client/scripts/network_entity_manager.gd")
    for scene in ["network_player.tscn","monster_entity.tscn","npc_entity.tscn","ground_item_entity.tscn","pet_entity.tscn"]:
        assert f'preload("res://{scene}")' not in manager
    assert "var _entity_scene_cache: Dictionary = {}" in manager
    assert "func release_entity_scene_cache()" in manager

def test_logout_clears_world_client_refs():
    game = read("client/scripts/game_manager.gd")
    block = game[game.index("func cleanup_and_return_to_login()"):game.index("func _clear_visual_runtime_caches()")]
    assert "clear_map_entities(true)" in block
    assert "ItemLoader.clear_world_data()" in block
    assert "SkillLoader.clear_world_data()" in block
    assert "PetLoader.clear_world_data()" in block
    visual = game[game.index("func _clear_visual_runtime_caches()"):game.index("func _apply_low_resource_world_visual_policy()")]
    assert "release_entity_scene_cache" in visual

def test_optional_hud_scenes_are_lazy():
    hud = read("client/game_hud.gd")
    assert "func _instantiate_lazy_hud_scene(path: String)" in hud
    for scene in ["bank_window.tscn","pet_window.tscn","rank_window.tscn","trade_window.tscn"]:
        assert f'preload("res://{scene}")' not in hud

def test_character_select_is_lazy_after_login():
    entry = read("client/entry_manager.gd")
    scene = read("client/entry_manager.tscn")
    assert 'CHARACTER_SELECT_SCENE_PATH: String = "res://character_select.tscn"' in entry
    assert "func _ensure_character_select_layer()" in entry
    assert "_ensure_character_select_layer()" in entry[entry.index("func _on_global_login_finished"):]
    assert 'path="res://character_select.tscn"' not in scene
    assert 'name="CharacterSelectLayer"' not in scene
