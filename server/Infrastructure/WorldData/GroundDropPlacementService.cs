using System.Collections.Concurrent;

namespace SGO.Server.Infrastructure.WorldData;

/// <summary>
/// Finds a legal, reachable position for a ground item BEFORE its DB row is
/// created. Uses the same Blocking.dat grid as monster/player navigation.
/// No DB, network, or full-map scans are performed.
/// </summary>
public sealed class GroundDropPlacementService
{
    private const int Cell = BlockingMapService.CellSize;
    private const int SearchRadiusCells = 5;
    // Bound requests from the client (including malicious coordinates)
    // while leaving normal drag-on-world item placement intact.
    private const double MaxRequestedDistance = 1024.0;
    private const double MaxRequestedDistanceSquared =
        MaxRequestedDistance * MaxRequestedDistance;

    private readonly BlockingMapService _blocking;
    private readonly MonsterNavigationService _navigation;
    private readonly ConcurrentDictionary<int, byte> _reportedMissingMaps = new();

    // The first missing-map warning is important; logging once for each
    // dropped item could overwhelm a server with 1000 active players.
    public bool ShouldReportMissingMap(int mapId)
        => _reportedMissingMaps.TryAdd(mapId, 0);

    public GroundDropPlacementService(BlockingMapService blocking)
    {
        _blocking = blocking;
        _navigation = new MonsterNavigationService(blocking);
    }

    /// <summary>
    /// Source is a monster death location or the server-authoritative player
    /// position. Fallback is the authoritative killer/player position.
    ///
    /// If the map has a Blocking.dat file, this never returns a blocked cell
    /// or a location across a straight-line obstacle from the anchor.
    /// If data is missing, keep legacy positions to avoid silently losing
    /// loot on a map whose collision data was not installed.
    /// </summary>
    public bool TryResolve(
        int mapId,
        double sourceX, double sourceY,
        double requestedX, double requestedY,
        double fallbackX, double fallbackY,
        out DropPlacement placement)
    {
        placement = default;
        if (!double.IsFinite(sourceX) || !double.IsFinite(sourceY) ||
            !double.IsFinite(fallbackX) || !double.IsFinite(fallbackY))
            return false;

        // Don't accept an unbounded client-supplied position. This does not
        // mutate the actual position of the player, monster or any item.
        if (!double.IsFinite(requestedX) || !double.IsFinite(requestedY) ||
            SquaredDistance(sourceX, sourceY, requestedX, requestedY) >
            MaxRequestedDistanceSquared)
        {
            requestedX = sourceX;
            requestedY = sourceY;
        }

        if (!_blocking.HasMap(mapId))
        {
            // Some recovered/test maps have no Blocking.dat. Keeping legacy
            // behavior preserves dropped assets; these cannot be certified
            // walkable until the map data exists.
            placement = new DropPlacement(requestedX, requestedY,
                Relocated: false, CollisionVerified: false);
            return true;
        }

        if (TryReachable(mapId, sourceX, sourceY,
                requestedX, requestedY, out var x, out var y))
        {
            placement = new DropPlacement(x, y,
                Relocated: x != requestedX || y != requestedY,
                CollisionVerified: true);
            return true;
        }

        // Monsters can die on an invalid spawn cell, or on the opposite side
        // of a wall. Place loot on the killer's reachable side rather than
        // in an inaccessible cell. Player drops use the same anchor twice.
        if (TryReachable(mapId, fallbackX, fallbackY,
                requestedX, requestedY, out x, out y) ||
            TryReachable(mapId, fallbackX, fallbackY,
                fallbackX, fallbackY, out x, out y))
        {
            placement = new DropPlacement(x, y,
                Relocated: true, CollisionVerified: true);
            return true;
        }

        // Caller must reject a manual drop BEFORE removing an inventory item.
        // Monster drop caller should explicitly report this exceptional map
        // inconsistency rather than persisting a known inaccessible position.
        return false;
    }

    private bool TryReachable(
        int mapId, double anchorX, double anchorY,
        double targetX, double targetY,
        out double x, out double y)
    {
        x = y = 0;
        if (_blocking.GetFlags(mapId, anchorX, anchorY) != 0 ||
            SquaredDistance(anchorX, anchorY, targetX, targetY) >
            MaxRequestedDistanceSquared)
            return false;

        if (IsReachable(mapId, anchorX, anchorY, targetX, targetY))
        {
            x = targetX;
            y = targetY;
            return true;
        }

        // Search nearby cell CENTERS, rather than random world offsets. This
        // ensures neither an obstacle tile nor an out-of-bounds location is
        // selected. A bounded spiral is deterministic and cheap per drop.
        var cx = (int)Math.Floor(targetX / Cell);
        var cy = (int)Math.Floor(targetY / Cell);
        for (var ring = 0; ring <= SearchRadiusCells; ring++)
        {
            for (var dy = -ring; dy <= ring; dy++)
            {
                for (var dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                        continue;
                    var candidateX = (cx + dx) * (double)Cell + Cell / 2.0;
                    var candidateY = (cy + dy) * (double)Cell + Cell / 2.0;
                    if (!IsReachable(mapId, anchorX, anchorY,
                            candidateX, candidateY))
                        continue;
                    x = candidateX;
                    y = candidateY;
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsReachable(
        int mapId, double anchorX, double anchorY,
        double targetX, double targetY)
        => _blocking.GetFlags(mapId, targetX, targetY) == 0 &&
           _navigation.IsDirectClear(mapId,
               anchorX, anchorY, targetX, targetY);

    private static double SquaredDistance(
        double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return dx * dx + dy * dy;
    }
}

/// <param name="CollisionVerified">
/// False only for legacy maps with no Blocking.dat; do not advertise these
/// as proven walkable.
/// </param>
public readonly record struct DropPlacement(
    double X, double Y, bool Relocated, bool CollisionVerified);
