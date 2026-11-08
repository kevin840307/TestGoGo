from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def test_map_loads_use_releasable_scene_cache_without_network_changes():
    code = (ROOT / "client/scripts/game_manager.gd").read_text(encoding="utf-8")
    assert code.count("ResourceLoader.CACHE_MODE_IGNORE") == 3
    assert "ResourceLoader.load_threaded_get_status(path, progress)" in code
    assert "ResourceLoader.load_threaded_get(map_path)" in code
    assert "ResourceLoader.load_threaded_get(path)" in code
    assert "func _restore_afk_entity_visuals_batched" in code
