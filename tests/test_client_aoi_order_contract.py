"""Static CI guard for the Godot pre-spawn AOI ordering regression.

The Godot headless regression is the executable behavioral test; this file
only checks source integration so future refactors cannot silently disable it.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent.parent


def source(relative_path: str) -> str:
    return (ROOT / relative_path).read_text(encoding="utf-8-sig")


class ClientAoiOrderContractTests(unittest.TestCase):
    def test_pending_move_state_is_bounded_and_expires(self):
        script = source("client/scripts/network_entity_pending_moves.gd")
        self.assertIn("MAX_PENDING: int = 256", script)
        self.assertIn("PENDING_TTL_MSEC: int = 3000", script)
        self.assertIn("MAX_DESPAWNED: int = 512", script)
        self.assertIn("mark_despawned(", script)
        self.assertIn("consume_spawn_move(", script)
        self.assertIn("if _despawned.has(entity_id):", script)
        self.assertIn("_last_prune_msec", script)

    def test_manager_merges_move_spawn_and_discards_old_incarnations(self):
        script = source("client/scripts/network_entity_manager.gd")
        self.assertIn("network_entity_pending_moves.gd", script)
        self.assertIn("_pending_aoi_moves.record_move(", script)
        self.assertGreaterEqual(
            script.count("_pending_aoi_moves.consume_spawn_move("), 2
        )
        self.assertIn("_pending_aoi_moves.mark_despawned(", script)
        self.assertGreaterEqual(script.count("_pending_aoi_moves.clear()"), 2)
        self.assertIn("latest[\"LocX\"] = buffered.get(\"X\"", script)
        self.assertIn('cached["_aoi_has_move"] = true', script)

    def test_behavioral_godot_script_and_runner_wiring(self):
        regression = source("client/tests/aoi_pre_spawn_move_regression.gd")
        runner = source("tests/run_protocol_checks.ps1")
        self.assertIn("Move before first Spawn wins", regression)
        self.assertIn(
            "queued player also receives most recent position", regression
        )
        self.assertIn("malicious unknown-entity move burst", regression)
        self.assertIn(
            "fresh AOI re-entry ignores stale movement", regression
        )
        self.assertIn("aoi_pre_spawn_move_regression.gd", runner)


if __name__ == "__main__":
    unittest.main()
