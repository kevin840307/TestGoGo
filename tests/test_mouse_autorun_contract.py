"""Static integration guard for Godot's one-second cursor-follow autorun.

Behavior is exercised in client/tests/mouse_autorun_regression.gd. These
assertions protect source wiring on CI hosts without a Godot binary.
"""
from pathlib import Path
import struct
import unittest

ROOT = Path(__file__).resolve().parent.parent


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8-sig")


class MouseAutorunContractTests(unittest.TestCase):
    def test_one_second_latch_and_next_terrain_click(self):
        state = read("client/scripts/mouse_autorun_state.gd")
        player = read("client/player.gd")
        self.assertIn("HOLD_TO_LATCH_MSEC: int = 1000", state)
        self.assertIn("func poll_long_hold(", state)
        self.assertIn("var was_latched: bool = _mouse_autorun.terrain_press(", player)
        self.assertIn("is_mouse_holding = not was_latched", player)
        # First left-click after a latch stops movement without starting a
        # second route; release clears the same-click suppression.
        self.assertIn("var _autorun_left_press_cancelled: bool = false", player)
        self.assertIn("_autorun_left_press_cancelled = true", player)
        self.assertIn("_autorun_left_press_cancelled = false", player)
        self.assertIn("if _autorun_left_press_cancelled:", player)
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
        self.assertIn("MOUSE_BUTTON_LEFT", handler)
        self.assertIn("elif _mouse_autorun.latched:", handler)
        self.assertIn("cancel_autorun_tracking(true)", handler)
        self.assertIn("CursorManager.current_hovered_entity", handler)
        self.assertIn("gui_get_hovered_control()", handler)
        self.assertIn("_autorun_click_has_priority(", handler)
        self.assertIn("Control.MOUSE_FILTER_STOP", player)
        self.assertIn("hovered_ui is BaseButton", player)
        self.assertNotIn("set_input_as_handled", handler)
        self.assertIn("show_player_context_menu(", network_player)

    def test_cursor_icon_preserves_interactable_priority(self):
        cursor = read("client/scripts/cursor_manager.gd")
        sheet = ROOT / "client" / "ui" / "cursor" / "C_AUTOWALK.spr.png"
        self.assertTrue(sheet.is_file(), "Generated cursor PNG is committed")
        png = sheet.read_bytes()
        self.assertEqual(png[:8], bytes.fromhex("89504e470d0a1a0a"))
        # PNG IHDR width/height must match the SpriteFrames atlas.
        self.assertEqual(struct.unpack(">II", png[16:24]), (320, 32))
        # Palette transparency (tRNS) or RGBA preserves alpha.
        self.assertTrue(png[25] == 6 or (png[25] == 3 and b"tRNS" in png))
        self.assertIn('AUTORUN_FRAME_COUNT: int = 10', cursor)
        self.assertIn('AUTORUN_FRAME_SIZE: Vector2i = Vector2i(32, 32)', cursor)
        self.assertIn("AtlasTexture.new()", cursor)
        self.assertIn('frames.set_animation_speed("autorun", 10.0)', cursor)
        self.assertIn('frames.add_frame("autorun", frame_texture)', cursor)
        self.assertIn('AUTORUN_VISUAL_SCALE: float = 1.0', cursor)
        self.assertIn('cursor_sprite.scale = _original_cursor_scale', cursor)
        self.assertIn('cursor_sprite.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST', cursor)
        self.assertIn('locked walking uses native 1:1 sprite scale', read("client/tests/mouse_autorun_regression.gd"))
        self.assertNotIn("_autorun_status_label", cursor)
        self.assertNotIn("自動走路 ON", cursor)
        self.assertNotIn("右鍵停止", cursor)
        self.assertIn("cursor_sprite.scale = _original_cursor_scale", cursor)
        self.assertIn('Auto-walk cursor sprite sheet must be 320x32 pixels.', cursor)
        self.assertIn("set_autorun_indicator(", cursor)
        self.assertIn('visual_state == "normal"', cursor)
        self.assertIn("not _is_game_window_hovered", cursor)
        self.assertNotIn("autorun_cursor_indicator.gd", cursor)
        self.assertIn('["attack", "check", "player", "pick"]', cursor)

    def test_gameplay_and_lifecycle_cancel_latch(self):
        player = read("client/player.gd")
        npc = read("client/scripts/npc_entity.gd")
        monster = read("client/scripts/monster_entity.gd")
        drop = read("client/scripts/ground_item_entity.gd")
        remote_player = read("client/network_player.gd")
        self.assertIn("func cancel_autorun_tracking(", player)
        for source in (npc, monster, drop, remote_player):
            self.assertIn("cancel_autorun_tracking(true)", source)

    def test_offline_godot_behavior_is_in_both_runners(self):
        case = read("client/tests/mouse_autorun_regression.gd")
        windows = read("tests/run_protocol_checks.ps1")
        ci = read(".github/workflows/server-build.yml")
        self.assertIn("terrain requires one full second", case)
        self.assertIn("right-click stops latched movement", case)
        self.assertIn("first bare-terrain left click stops latched walking", case)
        self.assertIn("release clears one-click suppression", case)
        self.assertIn("player right-click menu behavior remains present", case)
        self.assertIn("mouse_autorun_regression.gd", windows)
        self.assertIn("mouse_autorun_regression.gd", ci)
        self.assertIn("autorun animation has no text badge", case)
        self.assertIn("restores regular pointer without text", case)


if __name__ == "__main__":
    unittest.main()
