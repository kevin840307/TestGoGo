"""Guard pickup failure packet compatibility and persistent AOI invariants.

This is a source-contract check, NOT a substitute for live WebSocket and
SQLite tests. The corresponding behavioral suites run under 'world' and
'concurrency' after a .NET/Godot runner is available.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent.parent


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8-sig")


class GroundPickupVisibilityContractTests(unittest.TestCase):
    def test_failed_pickup_only_despawns_confirmed_missing_row(self):
        router = read("server/Application/PacketRouter.cs")
        start = router.index('            case "PickUpItemRequest":')
        end = router.index("                var pickedName =", start)
        handling = router[start:end]

        self.assertIn('var groundGone = message is', handling)
        self.assertIn('"物品已不存在" or "物品已消失"', handling)
        self.assertNotIn('message.Contains("不存在"', handling)
        self.assertIn('state.GroundItems.Remove(entityId);', handling)
        self.assertIn("notifyObservers: true", handling)
        self.assertIn("skipObserver: state.ConnectionId", handling)
        self.assertIn('Type = "DespawnEntity"', handling)
        self.assertIn('Type = "SystemMessageFlash"', handling)
        self.assertIn("if (groundGone)", handling)

    def test_no_cache_missing_item_returns_local_despawn_only_for_ground_ids(self):
        router = read("server/Application/PacketRouter.cs")
        start = router.index('            case "PickUpItemRequest":')
        end = router.index("                var pickupTerrain =", start)
        fallback = router[start:end]
        self.assertIn("state.GroundItems.Remove(entityId);", fallback)
        self.assertIn("entityId >= 6_000_000", fallback)
        self.assertIn("entityId < 9_000_000", fallback)
        self.assertIn('Type = "DespawnEntity"', fallback)
        self.assertIn('Message = "物品已不存在"', fallback)

        wire = read("tests/SGO.Server.WireSmokeTests/Program.cs")
        self.assertIn(
            "duplicate pickup returns local DespawnEntity", wire
        )
        self.assertIn(
            "duplicate pickup reports row absent without re-granting", wire
        )

    def test_worldready_snapshot_cannot_recreate_consumed_drop(self):
        aoi = read("server/Application/Messaging/WorldEntitySpatialAoi.cs")
        router = read("server/Application/PacketRouter.cs")
        self.assertIn("_removedDuringSnapshots", aoi)
        self.assertIn("TryUpsertEntityFromSnapshot(", aoi)
        self.assertIn("_broadcasts.BeginWorldEntitySnapshot();", router)
        self.assertIn("_broadcasts.TryUpsertWorldEntityFromSnapshot(", router)
        self.assertIn("resolvedGround.IsExpired(DateTimeOffset.UtcNow)", router)

    def test_real_sqlite_inventory_conservation_is_in_both_suites(self):
        program = read("tests/SGO.Server.SmokeTests/Program.cs")
        suite = read(
            "tests/SGO.Server.SmokeTests/Integration/"
            "GroundSnapshotPersistenceRaceSmokeTests.cs"
        )
        self.assertEqual(
            program.count("GroundSnapshotPersistenceRaceSmokeTests.Run();"),
            2,
        )
        self.assertIn("store.PickUpGroundItem(", suite)
        self.assertIn("inventoryCount == 2", suite)


if __name__ == "__main__":
    unittest.main()
