using SGO.Server.Infrastructure.WorldData;
using SGO.Server.SmokeTests.Fixtures;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.World;

/// <summary>Collision map parser, special flags, monster line-of-sight and A* detours.</summary>
internal static class BlockingMapSmokeTests
{
    public static void Run()
    {
        using var blockingFixture = new TemporaryDirectoryFixture("sgo-blocking-");
        var blockingTestRoot = blockingFixture.Root;
        var blockingMapRoot = Path.Combine(
            blockingTestRoot,
            "Map",
            "Map01");
        Directory.CreateDirectory(blockingMapRoot);

        var blockingTestPath = Path.Combine(
            blockingMapRoot,
            "Blocking.dat");
        using (var writer = new BinaryWriter(
                   File.Open(
                       blockingTestPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None)))
        {
            writer.Write(5);
            writer.Write(1);
            writer.Write(new byte[] { 0, 0, 1, 0, 3 });
        }

        var blocking = new BlockingMapService(blockingTestRoot);
        Check(blocking.HasMap(1),
            "Blocking.dat parser loads original 8-byte header grid");
        Check(blocking.IsMoveAllowed(
                  1, 8, 8, 24, 8, false),
            "Blocking.dat movement accepts clear cells");
        Check(!blocking.IsMoveAllowed(
                  1, 8, 8, 40, 8, false),
            "Blocking.dat movement rejects a segment crossing blocked cell");

        using var cornerFixture = new TemporaryDirectoryFixture("sgo-blocking-corner-");
        var cornerRoot = cornerFixture.Root;
        var cornerMapRoot = Path.Combine(
            cornerRoot,
            "Map",
            "Map01");
        Directory.CreateDirectory(cornerMapRoot);

        using (var writer = new BinaryWriter(
                   File.Open(
                       Path.Combine(cornerMapRoot, "Blocking.dat"),
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None)))
        {
            writer.Write(3);
            writer.Write(3);
            writer.Write(new byte[]
            {
                0, 1, 0,
                0, 0, 0,
                0, 0, 0
            });
        }

        var cornerBlocking = new BlockingMapService(cornerRoot);
        Check(cornerBlocking.IsMoveAllowed(
                  1,
                  8,
                  8,
                  40,
                  8,
                  false),
            "Blocking.dat server validation allows a short legal route around a corner");

        // The disposable fixture cleans the corner-map directory on exit.

        // Monster movement must use strict line segments, not ValidateMove's
        // permissive local-route rule for received player positions.
        var navigator = new MonsterNavigationService(blocking);
        Check(!navigator.IsDirectClear(1, 8, 8, 40, 8),
            "monster cannot cross a blocked cell even if a local detour exists");
        Check(!navigator.IsDirectClear(1, 56, 8, 72, 8),
            "monster cannot cross special-terrain flag 3");
        Check(!navigator.TryNextStep(1, 56, 8, 72, 8, 16, out _, out _),
            "monster cannot route into special terrain without permission");
        Check(!navigator.IsDirectClear(99, 8, 8, 24, 8),
            "monster movement fails closed when Blocking.dat is missing");

        // A vertical wall with an opening at the bottom forces an A* detour.
        // Deliberately test the diagonal edge beside the wall too.
        var navMapRoot = Path.Combine(blockingTestRoot, "Map", "Map02");
        Directory.CreateDirectory(navMapRoot);
        using (var writer = new BinaryWriter(
                   File.Create(Path.Combine(navMapRoot, "Blocking.dat"))))
        {
            writer.Write(7);
            writer.Write(5);
            writer.Write(new byte[]
            {
                0,0,0,1,0,0,0,
                0,0,0,1,0,0,0,
                0,0,0,1,0,0,0,
                0,0,0,0,0,0,0,
                0,0,0,0,0,0,0
            });
        }
        Check(!navigator.IsDirectClear(2, 40, 24, 56, 24) &&
              !navigator.IsDirectClear(2, 40, 24, 56, 40),
            "monster cannot step through a wall or cut its diagonal corner");
        var navX = 24.0;
        var navY = 24.0;
        var followedValidRoute = true;
        for (var i = 0; i < 40 &&
                         Math.Sqrt(Math.Pow(88 - navX, 2) +
                                   Math.Pow(24 - navY, 2)) > 1; i++)
        {
            var oldX = navX;
            var oldY = navY;
            if (!navigator.TryNextStep(2, oldX, oldY, 88, 24, 16,
                                      out navX, out navY) ||
                !navigator.IsDirectClear(2, oldX, oldY, navX, navY))
            {
                followedValidRoute = false;
                break;
            }
        }
        Check(followedValidRoute &&
              Math.Abs(navX - 88) <= 1 &&
              Math.Abs(navY - 24) <= 1,
            "monster A-star chase walks around a wall without clipping through it");

        Check(!blocking.IsMoveAllowed(
                  1, 56, 8, 72, 8, false) &&
              blocking.IsMoveAllowed(
                  1, 56, 8, 72, 8, true),
            "Blocking flag 3 requires special-terrain capability");
        Check(!blocking.IsMoveAllowed(
                  99, 8, 8, 24, 8, false),
            "missing Blocking.dat fails closed");

        // Dismount must not remove terrain access when still standing on
        // mecha-only flag 3. DriveMechaRequest checks the current position
        // with a zero-length on-foot movement before updating the rider.
        Check(!blocking.IsMoveAllowed(
                   1, 72, 8, 72, 8, false) &&
              blocking.IsMoveAllowed(
                   1, 72, 8, 72, 8, true),
            "dismount rejects current cell when it is mecha-only terrain");
        Check(blocking.IsMoveAllowed(
                  1, 24, 8, 24, 8, false),
            "dismount accepts a regular walkable tile");

        // The fixture cleans up every temporary Blocking.dat when the suite exits.
    }
}
