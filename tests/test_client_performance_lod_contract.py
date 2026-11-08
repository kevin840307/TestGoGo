"""Static contract guard for client performance LOD and per-account settings."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent.parent


def source(relative_path: str) -> str:
    return (ROOT / relative_path).read_text(encoding="utf-8-sig")


class ClientPerformanceLodContractTests(unittest.TestCase):
    def test_generic_monster_uses_shared_mid_size_visual_and_click_box(self):
        script = source("client/scripts/monster_entity.gd")
        self.assertIn('GENERIC_MONSTER_SPRITE := "EN032A"', script)
        self.assertIn("GENERIC_MONSTER_CLICK_SIZE := Vector2(112.0, 136.0)", script)
        self.assertIn("get_generic_monster_click_shape()", script)
        self.assertIn("set_performance_simplified(false)", script)

    def test_generic_remote_player_uses_base_boy_without_equipment_layers(self):
        script = source("client/network_player.gd")
        self.assertIn('GENERIC_PLAYER_RACE := "Boy"', script)
        self.assertIn('GENERIC_PLAYER_HAIR := "Hair000"', script)
        self.assertIn("GENERIC_PLAYER_EQUIPS", script)
        self.assertIn("set_performance_simplified(false)", script)

    def test_manager_promotes_combat_entities_and_reacts_to_profile_changes(self):
        script = source("client/scripts/network_entity_manager.gd")
        self.assertIn("performance_profile_changed.connect", script)
        self.assertIn("_should_simplify_player", script)
        self.assertIn("_should_simplify_monster", script)
        self.assertIn("_is_combat_monster_revealed", script)
        self.assertIn("_is_local_combat_target", script)

    def test_settings_are_persisted_per_login_account(self):
        script = source("client/game_hud.gd")
        self.assertIn("func _game_settings_section()", script)
        self.assertIn("GlobalTempData.login_user", script)
        self.assertIn('return "%s:%s"', script)
        self.assertIn("SETTING_KEY_PERFORMANCE_LOAD_MODE", script)
        self.assertIn("SETTING_KEY_COMBAT_RENDER_MODE", script)


if __name__ == "__main__":
    unittest.main()
