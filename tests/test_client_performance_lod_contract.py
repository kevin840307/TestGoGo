"""Static guards for client performance LOD, orb visuals and per-account settings."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent.parent


def source(relative_path: str) -> str:
    return (ROOT / relative_path).read_text(encoding="utf-8-sig")


class ClientPerformanceLodContractTests(unittest.TestCase):
    def test_monster_uses_one_spiky_orb_and_large_click_box(self):
        script = source("client/scripts/monster_entity.gd")
        self.assertIn("func _draw_simplified_monster", script)
        self.assertIn("SIMPLIFIED_ORB_CACHE.monster_texture()", script)
        self.assertIn("draw_texture(", script)
        self.assertIn("GENERIC_MONSTER_CLICK_SIZE := Vector2(112.0, 136.0)", script)
        self.assertIn("get_generic_monster_click_shape()", script)
        self.assertIn("sprite.sprite_frames = null", script)
        self.assertNotIn("GENERIC_MONSTER_SPRITE", script)

    def test_remote_player_uses_relation_colored_orb_without_paper_doll(self):
        script = source("client/network_player.gd")
        self.assertIn("func _draw_simplified_player_orb", script)
        self.assertIn("func _simplified_relation_color", script)
        self.assertIn("SIMPLIFIED_ORB_CACHE.player_texture()", script)
        self.assertIn("SIMPLIFIED_PLAYER_CLICK_SIZE", script)
        self.assertIn("doll_engine.clear_dolls()", script)
        self.assertNotIn("GENERIC_PLAYER_RACE", script)

    def test_shared_lazy_generated_orb_texture_cache(self):
        script = source("client/scripts/simplified_orb_cache.gd")
        self.assertIn("static var _monster_texture: Texture2D", script)
        self.assertIn("static var _player_texture: Texture2D", script)
        self.assertIn("if _monster_texture == null:", script)
        self.assertIn("if _player_texture == null:", script)
        self.assertEqual(script.count("ImageTexture.create_from_image(image)"), 1)

    def test_manager_keeps_simplification_through_combat_and_profile_changes(self):
        script = source("client/scripts/network_entity_manager.gd")
        self.assertIn("performance_profile_changed.connect", script)
        self.assertIn("_should_simplify_player", script)
        self.assertIn("_should_simplify_monster", script)
        self.assertIn("_is_local_combat_target", script)
        self.assertIn("Combat targeting, attacks, hits and death", script)

    def test_settings_are_persisted_per_login_account(self):
        script = source("client/game_hud.gd")
        self.assertIn("func _game_settings_section()", script)
        self.assertIn("GlobalTempData.login_user", script)
        self.assertIn('return "%s:%s"', script)
        self.assertIn("SETTING_KEY_PERFORMANCE_LOAD_MODE", script)
        self.assertIn("SETTING_KEY_COMBAT_RENDER_MODE", script)
        self.assertIn("SETTING_KEY_SIMPLIFY_PLAYERS_MODE", script)
        self.assertIn("SETTING_KEY_SIMPLIFY_MONSTERS_MODE", script)


if __name__ == "__main__":
    unittest.main()
