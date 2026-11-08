using SGO.Server.Infrastructure.WorldData;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.SmokeTests.Fixtures;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.World;

/// <summary>
/// Verify monster loot and player-discard positions against the same
/// Blocking.dat rules used by movement, without touching a real database.
/// </summary>
internal static class GroundDropPlacementSmokeTests
{
    public static void Run()
    {
        using var fixture = new TemporaryDirectoryFixture("sgo-ground-drop-");
        var folder = Path.Combine(fixture.Root, "Map", "Map01");
        Directory.CreateDirectory(folder);

        // Walkable 9x9 cells, a vertical full-height wall at x=4, an
        // obstacle at x=3,y=2, and special/mecha-only terrain at x=2,y=4.
        var cells = new byte[9 * 9];
        for (var y = 0; y < 9; y++)
            cells[y * 9 + 4] = 1;
        cells[2 * 9 + 3] = 1;
        cells[4 * 9 + 2] = 3;
        using (var writer = new BinaryWriter(
                   File.Create(Path.Combine(folder, "Blocking.dat"))))
        {
            writer.Write(9);
            writer.Write(9);
            writer.Write(cells);
        }

        var blocking = new BlockingMapService(fixture.Root);
        var placement = new GroundDropPlacementService(blocking);
        var nav = new MonsterNavigationService(blocking);

        Check(placement.CheckPickup(1, 40, 40, 40, 24) ==
              GroundPickupReachability.Allowed,
            "pickup within 100px succeeds on an unobstructed walkway");
        Check(placement.CheckPickup(1, 40, 40, 88, 40) ==
              GroundPickupReachability.Blocked,
            "pickup across a wall is blocked even when distance is under 100px");
        Check(placement.CheckPickup(1, 40, 40, 56, 40) ==
              GroundPickupReachability.Blocked,
            "pickup cannot claim an item previously left inside a blocked tile");
        Check(placement.CheckPickup(1, 40, 56, 40, 72) ==
              GroundPickupReachability.Blocked,
            "pickup cannot claim an item stored on a mecha-only terrain cell");
        Check(placement.CheckPickup(1, 40, 72, 40, 56) ==
              GroundPickupReachability.Blocked,
            "on-foot pickup cannot start from a special terrain cell");
        Check(placement.CheckPickup(1, 40, 72, 40, 56,
                  canCrossSpecialTerrain: true) ==
              GroundPickupReachability.Allowed,
            "a player riding an authorized mecha can pick up an adjacent normal tile");
        Check(placement.CheckPickup(1, 40, 56, 40, 88) ==
              GroundPickupReachability.Blocked &&
              placement.CheckPickup(1, 40, 56, 40, 88,
                  canCrossSpecialTerrain: true) ==
              GroundPickupReachability.Allowed,
            "only a permitted mecha can reach across special-terrain cells");
        Check(placement.CheckPickup(1, 40, 56, 88, 56,
                  canCrossSpecialTerrain: true) ==
              GroundPickupReachability.Blocked,
            "mecha permission never allows ground pickup through a solid wall");
        Check(placement.CheckPickup(1, 40, 40, 40, 141) ==
              GroundPickupReachability.TooFar,
            "existing 100px pickup range is unchanged");
        Check(placement.CheckPickup(1, double.NaN, 40, 40, 40) ==
              GroundPickupReachability.TooFar,
            "non-finite coordinates cannot claim a ground item");
        Check(placement.CheckPickup(99, 40, 40, 56, 40) ==
              GroundPickupReachability.MapNotVerified,
            "legacy map without Blocking.dat keeps distance-only pickup");
        Check(placement.CheckPickup(99, 40, 40, 204, 40) ==
              GroundPickupReachability.TooFar,
            "legacy map still enforces the 100px pickup distance");

        Check(placement.TryRecoverPersistedDrop(1, 56, 40, out var repaired) &&
              repaired.CollisionVerified && repaired.Relocated &&
              blocking.GetFlags(1, repaired.X, repaired.Y) == 0 &&
              Math.Abs(repaired.X - 56) <= 80 &&
              Math.Abs(repaired.Y - 40) <= 80,
            "legacy drop stored in a wall repairs near its original tile");
        Check(!placement.TryRecoverPersistedDrop(1, 40, 40, out _),
            "valid persisted drops are not moved or re-created");
        Check(!placement.TryRecoverPersistedDrop(99, 56, 40, out _),
            "legacy map without Blocking.dat cannot certify a repair");

        var persist = new SqliteGameStore(
            Path.Combine(fixture.Root, "ground-repair.db"));
        var oldGround = persist.CreateGroundItem(
            123, 0, 1001, 1, 1, 56, 40,
            protectSeconds: 15, lifetimeSeconds: 60);
        Check(oldGround is not null,
            "create the original blocked ground item with existing TTL");
        if (oldGround is not null)
        {
            var previous = persist.GetGroundItems(1)
                .Single(x => x.GroundItemId == oldGround.GroundItemId);
            var concurrent = Enumerable.Range(0, 6)
                .Select(_ => Task.Run(() =>
                    persist.RelocateGroundItemIfUnchanged(
                        oldGround.GroundItemId, 1,
                        oldGround.X, oldGround.Y,
                        repaired.X, repaired.Y)))
                .ToArray();
            Task.WaitAll(concurrent);
            var latest = persist.GetGroundItems(1)
                .Single(x => x.GroundItemId == oldGround.GroundItemId);
            Check(concurrent.All(x => x.Result is not null &&
                       x.Result.X == repaired.X &&
                       x.Result.Y == repaired.Y) &&
                  latest.X == repaired.X && latest.Y == repaired.Y,
                "six concurrent WorldReady repairs converge to one persisted location");
            Check(persist.GetGroundItems(1)
                    .Count(x => x.GroundItemId == oldGround.GroundItemId) == 1 &&
                  latest.OwnerDbid == previous.OwnerDbid &&
                  latest.ProtectUntil == previous.ProtectUntil &&
                  latest.ExpiresAt == previous.ExpiresAt &&
                  latest.CreatedAt == previous.CreatedAt,
                "repair preserves single DB row, owner, protection, creation and original 60s lifetime");
            Check(persist.RelocateGroundItemIfUnchanged(
                      oldGround.GroundItemId, 1, oldGround.X, oldGround.Y,
                      88, 88)?.X == repaired.X,
                "stale repair request cannot overwrite an already corrected position");
            Check(placement.CheckPickup(1, 40, 40,
                      latest.X, latest.Y) == GroundPickupReachability.Allowed,
                "a recovered old drop is now reachable for a normal pickup");

            var removed = persist.ClearGroundItems(1);
            Check(removed.Contains(oldGround.EntityId) &&
                  persist.RelocateGroundItemIfUnchanged(
                      oldGround.GroundItemId, 1,
                      oldGround.X, oldGround.Y, 88, 88) is null &&
                  !persist.GetGroundItems(1).Any(),
                "a ground item already removed/picked up cannot be resurrected by stale WorldReady repair");
        }

        Check(placement.TryResolve(1,
                  40, 40, 40, 40, 40, 40, out var original) &&
              original.CollisionVerified && !original.Relocated &&
              original.X == 40 && original.Y == 40,
            "walkable monster drop keeps its original world coordinate");

        Check(placement.TryResolve(1,
                  40, 40, 56, 40, 40, 40, out var obstacle) &&
              obstacle.CollisionVerified && obstacle.Relocated &&
              blocking.GetFlags(1, obstacle.X, obstacle.Y) == 0 &&
              nav.IsDirectClear(1, 40, 40, obstacle.X, obstacle.Y),
            "wall/obstacle drop moves to reachable walkable ground");

        Check(placement.TryResolve(1,
                  40, 40, 104, 40, 40, 40, out var wallSide) &&
              wallSide.X < 64 && wallSide.CollisionVerified &&
              nav.IsDirectClear(1, 40, 40, wallSide.X, wallSide.Y),
            "requested location across solid wall stays on accessible side");

        Check(placement.TryResolve(1,
                  40, 56, 40, 72, 40, 56, out var special) &&
              blocking.GetFlags(1, special.X, special.Y) == 0 &&
              special.Relocated,
            "monster loot never lands on special/mecha-only terrain");

        Check(placement.TryResolve(1,
                  40, 40, -1000000, 999999, 40, 40, out var extreme) &&
              extreme.X == 40 && extreme.Y == 40,
            "forged client coordinates outside the map cannot place loot far away");

        Check(placement.TryResolve(99,
                  40, 40, 56, 40, 40, 40, out var legacy) &&
              legacy.X == 56 && !legacy.CollisionVerified,
            "maps missing Blocking.dat use explicitly unverified legacy positioning");

        Check(placement.TryResolve(1,
                  56, 40, 56, 40, 40, 40, out var invalidMonsterOrigin) &&
              invalidMonsterOrigin.CollisionVerified &&
              blocking.GetFlags(1, invalidMonsterOrigin.X, invalidMonsterOrigin.Y) == 0 &&
              nav.IsDirectClear(1, 40, 40, invalidMonsterOrigin.X, invalidMonsterOrigin.Y),
            "monster dying inside a wall falls back to the player's safe side");

        Check(placement.ShouldReportMissingMap(99) &&
              !placement.ShouldReportMissingMap(99) &&
              placement.ShouldReportMissingMap(100),
            "missing Blocking.dat diagnostic is rate-limited once per map");

        Check(placement.TryResolve(1,
                  10000, 10000, 10012, 10012, 40, 40,
                  out var remoteInvalidMonster) &&
              remoteInvalidMonster.CollisionVerified &&
              blocking.GetFlags(1, remoteInvalidMonster.X,
                  remoteInvalidMonster.Y) == 0 &&
              Math.Abs(remoteInvalidMonster.X - 40) <= 16,
            "far-away invalid monster origin falls back near player, without a huge collision ray");

        var blockedFolder = Path.Combine(fixture.Root, "Map", "Map02");
        Directory.CreateDirectory(blockedFolder);
        using (var writer = new BinaryWriter(
                   File.Create(Path.Combine(blockedFolder, "Blocking.dat"))))
        {
            writer.Write(3);
            writer.Write(3);
            writer.Write(Enumerable.Repeat((byte)1, 9).ToArray());
        }

        Check(!placement.TryResolve(2,
                  8, 8, 24, 24, 8, 8, out _),
            "all-blocked map rejects player drop BEFORE inventory is deducted");
        Check(!placement.TryRecoverPersistedDrop(2, 8, 8, out _) &&
              placement.HasVerifiedBlockedPosition(2, 8, 8) &&
              placement.ShouldReportUnrepairableMap(2) &&
              !placement.ShouldReportUnrepairableMap(2),
            "fully blocked legacy map keeps item data and logs one diagnosis only");
        Check(!placement.HasVerifiedBlockedPosition(99, 8, 8),
            "missing map data is not incorrectly diagnosed as verified blocked");
    }
}
