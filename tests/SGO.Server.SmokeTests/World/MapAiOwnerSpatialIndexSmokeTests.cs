using SGO.Server.Application.World;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.World;

/// <summary>
/// Pure, deterministic nearest-owner tests: no live sockets or SQLite.
/// Compare the optimized index against the previous full scan to prevent
/// changed monster targets, ghost pets, or wrong tie ordering under load.
/// </summary>
internal static class MapAiOwnerSpatialIndexSmokeTests
{
    public static void Run()
    {
        EmptyAndInvalidSnapshotsHaveNoOwner();
        NearFarAndEquidistantOwnersKeepOriginalSemantics();
        PetsCanOwnMonstersWithoutAnAlivePlayer();
        LargeSparseWorldMatchesFullScan();
        ParallelQueriesNeverMutateIndex();
        Console.WriteLine("Shared monster AI spatial owner index smoke passed.");
    }

    private static void EmptyAndInvalidSnapshotsHaveNoOwner()
    {
        var empty = new MapAiOwnerSpatialIndex([]);
        Check(empty.FindNearestOwnerIndex(0, 0) == -1,
            "no alive players or pets leaves AI owner to fallback");
        var invalid = new MapAiOwnerSpatialIndex(
        [
            new(-1, 0, 0),
            new(0, double.NaN, 0),
            new(1, 0, double.PositiveInfinity)
        ]);
        Check(invalid.FindNearestOwnerIndex(0, 0) == -1 &&
              invalid.FindNearestOwnerIndex(double.NaN, 0) == -1,
            "invalid coordinates never crash or misassign shared AI");
    }

    private static void NearFarAndEquidistantOwnersKeepOriginalSemantics()
    {
        // Indices are ordered by CharacterId, NOT insertion order or X.
        var points = new[]
        {
            new MapAiOwnerPoint(3, 100, 0),
            new MapAiOwnerPoint(0, -100, 0),
            new MapAiOwnerPoint(1, 5000, 0),
            new MapAiOwnerPoint(2, 400, 0)
        };
        var index = new MapAiOwnerSpatialIndex(points);
        Check(index.FindNearestOwnerIndex(0, 0) == 0,
            "equal nearest distance prefers smallest sorted player index");
        Check(index.FindNearestOwnerIndex(4900, 0) == 1,
            "far-away idle monster still gets its true nearest owner");
        Check(index.FindNearestOwnerIndex(260, 0) == 2,
            "nearest owner unaffected by input enumeration order");
    }

    private static void PetsCanOwnMonstersWithoutAnAlivePlayer()
    {
        // A dead owner's pet remains eligible; an owner with neither alive
        // character nor active pet must have NO point in the index.
        var points = new[]
        {
            new MapAiOwnerPoint(0, 8000, 8000), // alive player's position
            new MapAiOwnerPoint(1, 101, 100),   // dead player's pet
            new MapAiOwnerPoint(1, 200, 200),   // another summoned pet
            new MapAiOwnerPoint(2, 7000, 7000)
        };
        var index = new MapAiOwnerSpatialIndex(points);
        Check(index.FindNearestOwnerIndex(100, 100) == 1,
            "active pet can own nearby monster when player is dead");
        Check(index.FindNearestOwnerIndex(198, 201) == 1,
            "multiple pets of one player resolve to same scheduler owner");
        Check(index.FindNearestOwnerIndex(7200, 7000) == 2,
            "other owners still win near their own position");
    }

    private static void LargeSparseWorldMatchesFullScan()
    {
        const int owners = 1024;
        var random = new Random(20261108);
        var points = new List<MapAiOwnerPoint>();
        for (var i = 0; i < owners; i++)
        {
            // Asymmetric, sparse world to exercise both branches of the
            // spatial tree, including negative coordinates and pets.
            var x = random.Next(-200000, 200000);
            var y = random.Next(-200000, 200000);
            if (i % 11 != 0) // Some dead owners have pets but no body point.
                points.Add(new MapAiOwnerPoint(i, x, y));
            if (i % 7 == 0)
                points.Add(new MapAiOwnerPoint(i, x + 15, y - 20));
        }

        // Exact nearest means no AOI-radius cutoff: 1000-player worlds
        // must match a full scan even outside everyone's sight range.
        var index = new MapAiOwnerSpatialIndex(points);
        for (var query = 0; query < 2000; query++)
        {
            var x = random.Next(-300000, 300000);
            var y = random.Next(-300000, 300000);
            Check(index.FindNearestOwnerIndex(x, y) ==
                  ReferenceScan(points, x, y),
                $"spatial owner lookup matches full scan query {query}");
        }

        // Test the large-tree path with exact equal-distance ties.
        var ties = Enumerable.Range(0, 64)
            .Select(i => new MapAiOwnerPoint(i, i * 1000, 10000))
            .Concat([
                new MapAiOwnerPoint(70, -100, 0),
                new MapAiOwnerPoint(4, 100, 0)
            ]).ToArray();
        Check(new MapAiOwnerSpatialIndex(ties)
                  .FindNearestOwnerIndex(0, 0) == 4,
            "large spatial tree also preserves CharacterId-order ties");
    }

    private static void ParallelQueriesNeverMutateIndex()
    {
        var points = Enumerable.Range(0, 300)
            .Select(i => new MapAiOwnerPoint(i, i * 100, -i * 50))
            .ToArray();
        var index = new MapAiOwnerSpatialIndex(points);
        var errors = 0;
        Parallel.For(0, 1000, i =>
        {
            var x = i * 70 - 500;
            var y = i * -30 + 150;
            if (index.FindNearestOwnerIndex(x, y) !=
                ReferenceScan(points, x, y))
                Interlocked.Increment(ref errors);
        });
        Check(errors == 0,
            "shared immutable index supports concurrent nearest-owner queries");
    }

    private static int ReferenceScan(
        IEnumerable<MapAiOwnerPoint> points, double x, double y)
    {
        var bestDistance = double.MaxValue;
        var bestIndex = int.MaxValue;
        foreach (var point in points)
        {
            var dx = point.X - x;
            var dy = point.Y - y;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance ||
                (distance == bestDistance && point.OwnerIndex < bestIndex))
            {
                bestDistance = distance;
                bestIndex = point.OwnerIndex;
            }
        }

        return bestIndex == int.MaxValue ? -1 : bestIndex;
    }
}
