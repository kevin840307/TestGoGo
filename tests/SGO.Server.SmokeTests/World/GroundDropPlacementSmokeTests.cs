using SGO.Server.Infrastructure.WorldData;
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
    }
}
