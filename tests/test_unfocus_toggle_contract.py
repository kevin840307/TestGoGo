"""Unfocus is an optional visual-only setting. Minimize is unconditional."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]

def src(name):
    return (ROOT / name).read_text(encoding="utf-8-sig")

class UnfocusToggleContract(unittest.TestCase):
    def test_performance_panel_has_real_on_off_control(self):
        scene = src("client/game_hud.tscn")
        self.assertIn('[node name="OptionUnfocus" type="OptionButton" parent="WindowsContainer/SettingsWindow/Tabs/Performance"]', scene)
        self.assertIn('text = "視窗失焦省資源"', scene)
        self.assertIn('popup/item_0/text = "關閉"', scene)
        self.assertIn('popup/item_1/text = "開啟"', scene)

    def test_account_setting_round_trip(self):
        hud = src("client/game_hud.gd")
        constants = src("client/scripts/game_constants.gd")
        self.assertIn('SETTING_KEY_UNFOCUS_POWER_SAVE_ENABLED = "unfocus_power_save_enabled"', constants)
        self.assertIn("SETTING_DEFAULT_UNFOCUS_POWER_SAVE_ENABLED = true", constants)
        self.assertIn("current_unfocus_power_save_enabled = bool(config.get_value(", hud)
        self.assertIn("GameConstants.SETTING_KEY_UNFOCUS_POWER_SAVE_ENABLED", hud)
        self.assertIn("option_unfocus.select(1 if current_unfocus_power_save_enabled else 0)", hud)
        self.assertIn("option_unfocus.item_selected.connect(_on_opt_unfocus_item_selected)", hud)
        self.assertIn("func _on_opt_unfocus_item_selected(index: int)", hud)
        self.assertIn("GameManager.set_unfocus_power_save_enabled(current_unfocus_power_save_enabled)", hud)

    def test_minimize_is_independent_of_alt_tab_switch(self):
        policy = src("client/scripts/unfocus_presentation_policy.gd")
        gm = src("client/scripts/game_manager.gd")
        self.assertIn("return minimized or (not os_focused and unfocus_enabled)", policy)
        self.assertIn("UNFOCUS_PRESENTATION_POLICY.should_background(", gm)
        self.assertIn("_is_main_window_minimized(), _unfocus_power_save_enabled", gm)
        self.assertIn("var world_background: bool = _background_mode_active", gm)
        self.assertIn("var should_pause: bool = _background_mode_active", gm)
        self.assertIn("RenderingServer.render_loop_enabled = not world_background", gm)
        self.assertIn("_set_background_visual_processing(world_background)", gm)
        self.assertIn("func set_unfocus_power_save_enabled(enabled: bool)", gm)
        self.assertIn("_sync_background_presentation_state()", gm)
        self.assertIn("if desired_background == _background_mode_active:", gm)
        self.assertIn("_background_focus_lost_msec = 0", gm)
        self.assertIn("if _background_focus_lost_msec <= 0 or not _background_mode_active:", gm)

    def test_gameplay_network_and_afk_independent(self):
        gm = src("client/scripts/game_manager.gd")
        network = src("client/scripts/network_manager.gd")
        self.assertIn("func _physics_process(delta: float)", network)
        self.assertIn("socket.poll()", network)
        self.assertIn("func is_game_window_focused() -> bool:", gm)
        self.assertIn("return not _background_mode_active", gm)
        self.assertIn("if _afk_active:", gm)
        policy = src("client/scripts/unfocus_presentation_policy.gd")
        self.assertNotIn("NetworkManager", policy)
        self.assertNotIn("physics_ticks_per_second", policy)
        self.assertNotIn("SceneTree.paused", policy)

if __name__ == "__main__":
    unittest.main()
