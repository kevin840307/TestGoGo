"""Client-only performance visual LOD / account FPS static integration guards.

These tests complement the Godot behavioral tests (they do not replace them).
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def source(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


class ClientVisualPerformanceContractTests(unittest.TestCase):
    def test_quality_presets_do_not_throttle_fps(self):
        profile = source("client/scripts/client_performance_profile.gd")
        self.assertIn("static func sanitize_fps_limit", profile)
        self.assertIn("static func fps_options()", profile)
        self.assertIn("range(30, 121, 5)", profile)
        self.assertIn('profile["monster_aoi_enter_radius"] = 1250.0', profile)
        self.assertIn('profile["monster_aoi_enter_radius"] = 950.0', profile)
        self.assertIn('profile["effect_budget_percent"] = 15', profile)
        for cap in ('profile["foreground_fps"] = 45', 'profile["foreground_fps"] = 120'):
            self.assertNotIn(cap, profile)

    def test_enhancement_is_quality_only(self):
        manager = source("client/scripts/game_manager.gd")
        hud = source("client/game_hud.gd")
        scene = source("client/game_hud.tscn")
        self.assertIn(
            "set_world_enhancement_enabled(current_performance_load_mode == ClientPerformanceProfile.LOAD_QUALITY)",
            manager,
        )
        self.assertNotIn("_load_world_enhancement_setting()", manager)
        self.assertNotIn("current_quality_enhancement_enabled", hud)
        self.assertNotIn("option_quality_enhancement", hud)
        self.assertNotIn('name="OptionQualityEnhancement"', scene)
        self.assertIn('popup/item_1/text = "一般"', scene)

    def test_fps_spinbox_has_real_editing_and_step_controls(self):
        profile = source("client/scripts/client_performance_profile.gd")
        scene = source("client/game_hud.tscn")
        hud = source("client/game_hud.gd")
        self.assertIn('name="OptionFpsLimit" type="SpinBox"', scene)
        self.assertIn('min_value = 0.0', scene)
        self.assertIn('max_value = 144.0', scene)
        self.assertIn('step = 5.0', scene)
        self.assertIn("static func normalize_fps_spin_step", profile)
        self.assertIn("option_fps_limit.value_changed.connect(", hud)
        self.assertIn("option_fps_limit.editable = true", hud)
        self.assertIn("option_fps_limit.set_value_no_signal(float(selected))", hud)
        self.assertNotIn("option_fps_limit is OptionButton", hud)

    def test_fps_per_account_and_logging(self):
        hud = source("client/game_hud.gd")
        manager = source("client/scripts/game_manager.gd")
        self.assertIn("SETTING_KEY_FOREGROUND_FPS_LIMIT", hud)
        self.assertIn("GameManager.set_foreground_fps_limit(current_fps_limit)", hud)
        self.assertIn('current_performance_profile["foreground_fps"] = current_foreground_fps_limit', manager)
        self.assertIn("cpu_process_time_ms=", manager)
        self.assertIn("monster_aoi_radius=", manager)
        self.assertIn("ground_aoi_radius=", manager)
        self.assertIn("gpu_memory_metric=godot_resources", manager)

    def test_packet_paths_are_preserved(self):
        manager = source("client/scripts/network_entity_manager.gd")
        self.assertIn("_monster_spawn_templates[entity_id] = cached", manager)
        self.assertIn("_ground_spawn_templates[entity_id] = cached_ground", manager)
        self.assertIn("if bool(drop.get_meta(\"is_requesting\", false)):", manager)
        self.assertIn("_is_local_combat_target(monster)", manager)
        self.assertIn("_reveal_combat_monster", manager)
        self.assertIn("resolve_visual_aoi_radii(", manager)
        self.assertIn("_pending_aoi_moves.record_move(", manager)

    def test_behavioral_scripts_wired_to_runner(self):
        runner = source("tests/run_protocol_checks.ps1")
        for test in (
            "performance_ui_settings_regression.gd",
            "performance_visual_aoi_regression.gd",
        ):
            self.assertIn(test, runner)
            self.assertTrue((ROOT / "client" / "tests" / test).exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
