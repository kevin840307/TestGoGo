"""Normal-world RAM contract: optional HUD instances must never be eagerly held.

Structural checks protect the Godot lifecycle. They do not claim Windows RAM
has dropped: that must be measured on the target Windows workstation.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]


def source(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def function_body(text: str, name: str) -> str:
    match = re.search(r"(?m)^func " + re.escape(name) + r"\b[^\n]*\n", text)
    if match is None:
        raise AssertionError(f"missing GDScript function: {name}")
    tail = text[match.end():]
    next_func = re.search(r"(?m)^(?:func |static func |class_name |const |var )", tail)
    return tail[:next_func.start()] if next_func else tail


class OptionalHudMemoryContract(unittest.TestCase):
    def setUp(self) -> None:
        self.entry = source("client/entry_manager.gd")
        self.hud = source("client/game_hud.gd")
        self.game = source("client/scripts/game_manager.gd")

    def test_cold_login_only_prewarms_essential_scenes(self):
        instance_decl = re.search(
            r"const GAME_HUD_INSTANCE_PREWARM: Dictionary = (\{[^}]*\})",
            self.entry,
        )
        self.assertIsNotNone(instance_decl)
        self.assertEqual(instance_decl.group(1), "{}")
        path_block = self.entry.split(
            "const GAME_HUD_PREWARM_PATHS: Array[String] = [", 1
        )[1].split("]", 1)[0]
        self.assertIn('"res://player.tscn"', path_block)
        self.assertIn('"res://game_hud.tscn"', path_block)
        for optional in (
            "quest_window.tscn", "mall_window.tscn",
            "announcement_window.tscn", "game_info_window.tscn",
            "big_map_window.tscn", "world_map_window.tscn",
        ):
            self.assertNotIn(optional, path_block)
        self.assertIn("await _prewarm_game_hud_resources()", self.entry)

    def test_world_enter_does_not_instantiate_unopened_optional_windows(self):
        body = function_body(self.hud, "prepare_lazy_windows_before_reveal")
        self.assertIn("[OPTIONAL_HUD_LAZY]", body)
        self.assertNotIn("_ensure_lazy_window(", body)
        self.assertNotIn("scene.instantiate(", body)
        self.assertIn('deferred_windows=%d', body)
        self.assertIn("await current_hud.prepare_lazy_windows_before_reveal()", self.game)

    def test_on_demand_buttons_still_instantiate_their_windows(self):
        create = function_body(self.hud, "_ensure_lazy_window")
        self.assertIn("ResourceLoader.load_threaded_request(path)", create)
        self.assertIn("ResourceLoader.load_threaded_get(path)", create)
        self.assertIn("scene.instantiate()", create)
        self.assertIn("_configure_lazy_window(kind, window)", create)
        for kind in ("quest", "mall", "announcement", "game_info",
                     "craft", "big_map", "world_map"):
            self.assertIn(f'"{kind}": "res://', self.hud)
        self.assertIn('await _ensure_lazy_window("big_map")', self.hud)
        self.assertIn('await _ensure_lazy_window("world_map")', self.hud)
        self.assertIn('await _ensure_lazy_window("mall")', self.hud)
        self.assertIn('await _ensure_lazy_window("announcement")', self.hud)
        self.assertIn("var window := await _ensure_lazy_window(kind)", self.hud)

    def test_logout_detaches_hud_resources_before_losing_reference(self):
        cleanup = function_body(self.game, "cleanup_and_return_to_login")
        release = cleanup.index("current_hud.release_afk_visual_preloads()")
        queue = cleanup.index("current_hud.queue_free()")
        clear = cleanup.index("current_hud = null")
        self.assertLess(release, queue)
        self.assertLess(queue, clear)
        self.assertIn("NetworkEntityManager.clear_map_entities()", cleanup)
        self.assertIn("_clear_visual_runtime_caches()", cleanup)
        self.assertIn('change_scene_to_file("res://entry_manager.tscn")', cleanup)

    def test_no_network_or_physics_throttling_added(self):
        self.assertIn("func _physics_process(", source("client/scripts/network_manager.gd"))
        self.assertNotIn("Engine.time_scale =", self.game)
        self.assertNotIn("Engine.physics_ticks_per_second =", self.game)
        self.assertIn("func release_afk_visual_preloads() -> int:", self.hud)


if __name__ == "__main__":
    unittest.main()
