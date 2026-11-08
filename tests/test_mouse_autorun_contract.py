"""Static integration guard for Godot's two-second cursor-follow autorun.

Behavior is exercised in client/tests/mouse_autorun_regression.gd. These
assertions protect source wiring on CI hosts without a Godot binary.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent.parent


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8-sig")


class MouseAutorunContractTests(unittest.TestCase):
    def test_two_second_latch_and_next_terrain_click(self):
        state = read("client/scripts/mouse_autorun_state.gd")
        player = read("client/player.gd")
        self.assertIn("HOLD_TO_LATCH_MSEC: int = 2000", state)
        self.assertIn("func poll_long_hold(", state)
        self.assertIn("var was_latched: bool = _mouse_autorun.terrain_press(", player)
        self.assertIn("is_mouse_holding = not was_latched", player)
        self.assertIn("_mouse_autorun.terrain_release(", player)
        self.assertIn("if is_mouse_holding or _mouse_autorun.latched:", player)
        self.assertIn("AUTORUN_BLOCKED_RETRY_INTERVAL: float = 0.4", player)

    def test_right_click_stop_never_swallows_player_context_menu(self):
        player = read("client/player.gd")
        network_player = read("client/network_player.gd")
        first = player.index("func _input(event: InputEvent) -> void:")
        last = player.index("func _unhandled_input(", first)
        handler = player[first:last]
        self.assertIn("MOUSE_BUTTON_RIGHT", handler)
        self.assertIn("cancel_autorun_tracking(true)", handler)
        self.assertNotIn("set_input_as_handled", handler)
        self.assertIn("show_player_context_menu(", network_player)

    def test_cursor_icon_preserves_interactable_priority(self):
        cursor = read("client/scripts/cursor_manager.gd")
        indicator = read("client/scripts/autorun_cursor_indicator.gd")
        self.assertIn("set_autorun_indicator(", cursor)
        self.assertIn('visual_state == "normal"', cursor)
        self.assertIn("not _is_game_window_hovered", cursor)
        self.assertIn("draw_colored_polygon(", indicator)
        self.assertIn('["attack", "check", "player", "pick"]', cursor)

    def test_gameplay_and_lifecycle_cancel_latch(self):
        player = read("client/player.gd")
        npc = read("client/scripts/npc_entity.gd")
        monster = read("client/scripts/monster_entity.gd")
        drop = read("client/scripts/ground_item_entity.gd")
        self.assertIn("func cancel_autorun_tracking(", player)
        for source in (npc, monster, drop):
            self.assertIn("cancel_autorun_tracking(true)", source)

    def test_offline_godot_behavior_is_in_both_runners(self):
        case = read("client/tests/mouse_autorun_regression.gd")
        windows = read("tests/run_protocol_checks.ps1")
        ci = read(".github/workflows/server-build.yml")
        self.assertIn("terrain requires two full seconds", case)
        self.assertIn("right-click stops latched movement", case)
        self.assertIn("player right-click menu behavior remains present", case)
        self.assertIn("mouse_autorun_regression.gd", windows)
        self.assertIn("mouse_autorun_regression.gd", ci)


if __name__ == "__main__":
    unittest.main()
