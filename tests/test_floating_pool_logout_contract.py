"""Contract: pooled combat graphics must not remain expanded after logout.

This test is structural; target Windows RAM remains a separate acceptance
criterion (Private Working Set and Private Bytes, not Godot MEMORY_STATIC).
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]


def func(source: str, name: str) -> str:
    m = re.search(rf"(?m)^(?:static )?func {re.escape(name)}\([^\n]*\n", source)
    if m is None:
        raise AssertionError(f"Missing GDScript function {name}")
    tail = source[m.end():]
    nxt = re.search(r"(?m)^(?:func |static func |var |const )", tail)
    return tail[:nxt.start()] if nxt else tail


class FloatingPoolLogoutContract(unittest.TestCase):
    def setUp(self):
        self.pool = (ROOT / "client/scripts/floating_text_manager.gd").read_text(
            encoding="utf-8")
        self.game = (ROOT / "client/scripts/game_manager.gd").read_text(
            encoding="utf-8")

    def test_cold_pool_reserve_is_bounded(self):
        self.assertIn("const INITIAL_POOL_SIZE = 16", self.pool)
        self.assertIn("const POOL_SIZE = 100", self.pool)
        startup = func(self.pool, "_ready")
        self.assertIn("range(INITIAL_POOL_SIZE)", startup)
        self.assertIn("_create_pooled_label()", startup)
        self.assertIn("_create_pooled_effect()", startup)

    def test_logout_stops_current_effects_before_releasing_expansion(self):
        cleanup = func(self.pool, "release_world_runtime_for_login")
        self.assertIn("_damage_queue.clear()", cleanup)
        self.assertIn("set_process(false)", cleanup)
        self.assertIn("effect.stop()", cleanup)
        self.assertIn("effect.sprite_frames = null", cleanup)
        self.assertIn("_reset_label_tweens(label)", cleanup)
        self.assertIn("_shrink_combat_pool(_text_pool, INITIAL_POOL_SIZE)", cleanup)
        self.assertIn("_shrink_combat_pool(_effect_pool, INITIAL_POOL_SIZE)", cleanup)
        self.assertLess(cleanup.index("effect.stop()"), cleanup.index("_shrink_combat_pool("))

    def test_only_surplus_nodes_are_freed(self):
        shrink = func(self.pool, "_shrink_combat_pool")
        self.assertIn("pool.size() > maxi(0, keep_count)", shrink)
        self.assertIn("pool.pop_back()", shrink)
        self.assertIn("node.queue_free()", shrink)
        self.assertNotIn("pool.clear()", shrink)
        self.assertNotIn("node.free()", shrink)
        self.assertNotIn("remove_child(", shrink)

    def test_still_reused_during_gameplay_and_uses_logout_hook(self):
        self.assertIn("if _text_pool.size() < POOL_SIZE:", self.pool)
        self.assertIn("if _effect_pool.size() < POOL_SIZE:", self.pool)
        logout = func(self.game, "cleanup_and_return_to_login")
        self.assertIn("FloatingTextManager.release_world_runtime_for_login()", logout)
        self.assertIn("NetworkEntityManager.clear_map_entities()", logout)
        self.assertNotIn("Engine.time_scale =", self.game)


if __name__ == "__main__":
    unittest.main()
