using System.Collections.Generic;

namespace SGO.Server.Infrastructure.WorldData;

/// <summary>
/// Strict monster movement on the same Blocking.dat grid as player movement.
/// Unlike ValidateMove, this never accepts a straight-line shortcut through
/// walls merely because a path exists around them.
/// </summary>
public sealed class MonsterNavigationService
{
    private const int Cell = BlockingMapService.CellSize;
    private const int MaxExpandedCells = 1400;
    private readonly BlockingMapService _blocking;

    private static readonly (int X, int Y)[] Directions =
    [
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (1, -1), (-1, 1), (-1, -1)
    ];

    public MonsterNavigationService(BlockingMapService blocking)
    {
        _blocking = blocking;
    }

    private bool Walkable(int mapId, int x, int y)
        => _blocking.GetFlags(mapId, x * Cell + Cell / 2.0,
            y * Cell + Cell / 2.0) == 0;

    public bool IsDirectClear(
        int mapId, double fromX, double fromY, double toX, double toY)
    {
        if (!double.IsFinite(fromX) || !double.IsFinite(fromY) ||
            !double.IsFinite(toX) || !double.IsFinite(toY))
            return false;

        var dx = toX - fromX;
        var dy = toY - fromY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var count = Math.Max(1, (int)Math.Ceiling(distance / (Cell / 4.0)));
        var previousX = (int)Math.Floor(fromX / Cell);
        var previousY = (int)Math.Floor(fromY / Cell);
        if (!Walkable(mapId, previousX, previousY))
            return false;

        for (var i = 1; i <= count; i++)
        {
            var ratio = (double)i / count;
            var x = (int)Math.Floor((fromX + dx * ratio) / Cell);
            var y = (int)Math.Floor((fromY + dy * ratio) / Cell);
            if (!Walkable(mapId, x, y))
                return false;

            if (x != previousX && y != previousY &&
                (!Walkable(mapId, x, previousY) ||
                 !Walkable(mapId, previousX, y)))
                return false;

            previousX = x;
            previousY = y;
        }
        return true;
    }

    public bool TryNextStep(
        int mapId, double fromX, double fromY,
        double targetX, double targetY, double maxStep,
        out double nextX, out double nextY)
    {
        nextX = fromX;
        nextY = fromY;
        if (maxStep <= 0 || !double.IsFinite(maxStep) ||
            !double.IsFinite(targetX) || !double.IsFinite(targetY) ||
            !double.IsFinite(fromX) || !double.IsFinite(fromY))
            return false;

        var dx = targetX - fromX;
        var dy = targetY - fromY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < 0.01)
            return false;

        var step = Math.Min(maxStep, distance);
        var directX = fromX + dx / distance * step;
        var directY = fromY + dy / distance * step;
        if (IsDirectClear(mapId, fromX, fromY, directX, directY))
        {
            nextX = directX;
            nextY = directY;
            return true;
        }

        (int X, int Y) start = (
            (int)Math.Floor(fromX / Cell),
            (int)Math.Floor(fromY / Cell));
        (int X, int Y) goal = (
            (int)Math.Floor(targetX / Cell),
            (int)Math.Floor(targetY / Cell));

        if (!Walkable(mapId, start.Item1, start.Item2) ||
            !Walkable(mapId, goal.Item1, goal.Item2) ||
            start == goal)
            return false;

        var open = new PriorityQueue<(int X, int Y), double>();
        var scores = new Dictionary<(int X, int Y), double> { [start] = 0 };
        var parents = new Dictionary<(int X, int Y), (int X, int Y)>();
        var closed = new HashSet<(int X, int Y)>();
        open.Enqueue(start, 0);

        var reached = false;
        while (open.Count > 0 && closed.Count < MaxExpandedCells)
        {
            var cell = open.Dequeue();
            if (!closed.Add(cell))
                continue;
            if (cell == goal)
            {
                reached = true;
                break;
            }

            foreach (var (sx, sy) in Directions)
            {
                var next = (cell.X + sx, cell.Y + sy);
                if (closed.Contains(next) ||
                    !Walkable(mapId, next.Item1, next.Item2))
                    continue;
                // Do not cut through the corner of two obstructed cells.
                if (sx != 0 && sy != 0 &&
                    (!Walkable(mapId, cell.X + sx, cell.Y) ||
                     !Walkable(mapId, cell.X, cell.Y + sy)))
                    continue;

                var cost = scores[cell] + (sx != 0 && sy != 0 ? 1.41421356237 : 1.0);
                if (scores.TryGetValue(next, out var existing) && cost >= existing)
                    continue;

                scores[next] = cost;
                parents[next] = cell;
                var hx = next.Item1 - goal.Item1;
                var hy = next.Item2 - goal.Item2;
                open.Enqueue(next, cost + Math.Sqrt(hx * hx + hy * hy));
            }
        }

        if (!reached)
            return false;

        // Reconstruct the local A* route and steer toward the furthest
        // visible waypoint instead of always aiming at the next cell centre.
        // The old one-cell steering made monsters zig-zag and visibly stutter
        // on diagonal stairs because the route was recalculated every AI tick.
        var path = new List<(int X, int Y)> { goal };
        var cursor = goal;
        while (parents.TryGetValue(cursor, out var parent))
        {
            path.Add(parent);
            if (parent == start)
                break;
            cursor = parent;
        }
        path.Reverse();

        var waypoint = path.Count > 1
            ? path[1]
            : goal;

        for (var index = 2; index < path.Count; index++)
        {
            var candidate = path[index];
            var candidateX =
                candidate.X * Cell + Cell / 2.0;
            var candidateY =
                candidate.Y * Cell + Cell / 2.0;

            if (!IsDirectClear(
                    mapId,
                    fromX,
                    fromY,
                    candidateX,
                    candidateY))
            {
                break;
            }

            waypoint = candidate;
        }

        var waypointX = waypoint.X * Cell + Cell / 2.0;
        var waypointY = waypoint.Y * Cell + Cell / 2.0;
        var wayDx = waypointX - fromX;
        var wayDy = waypointY - fromY;
        var wayDistance = Math.Sqrt(wayDx * wayDx + wayDy * wayDy);
        if (wayDistance < 0.01)
            return false;

        var move = Math.Min(maxStep, wayDistance);
        var x2 = fromX + wayDx / wayDistance * move;
        var y2 = fromY + wayDy / wayDistance * move;
        if (!IsDirectClear(mapId, fromX, fromY, x2, y2))
        {
            // LOS smoothing can choose a far waypoint whose full segment is
            // clear while a sampled partial step touches a blocked corner due
            // to cell-boundary rounding. Never fail the chase there: first
            // fall back to the next A* cell, which is guaranteed to be a
            // legal non-corner-cut edge from the current grid cell.
            var safeWaypoint = path.Count > 1
                ? path[1]
                : goal;
            var safeX = safeWaypoint.X * Cell + Cell / 2.0;
            var safeY = safeWaypoint.Y * Cell + Cell / 2.0;
            var safeDx = safeX - fromX;
            var safeDy = safeY - fromY;
            var safeDistance = Math.Sqrt(
                safeDx * safeDx + safeDy * safeDy);

            if (safeDistance >= 0.01)
            {
                var safeStep = Math.Min(maxStep, safeDistance);
                x2 = fromX + safeDx / safeDistance * safeStep;
                y2 = fromY + safeDy / safeDistance * safeStep;

                if (IsDirectClear(mapId, fromX, fromY, x2, y2))
                {
                    nextX = x2;
                    nextY = y2;
                    return true;
                }
            }

            // If we are between cell centres, first return to the current
            // walkable cell centre before trying the next A* edge.
            var centerX = start.Item1 * Cell + Cell / 2.0;
            var centerY = start.Item2 * Cell + Cell / 2.0;
            var centerDx = centerX - fromX;
            var centerDy = centerY - fromY;
            var centerDistance = Math.Sqrt(
                centerDx * centerDx + centerDy * centerDy);
            if (centerDistance < 0.01)
                return false;

            var centerStep = Math.Min(maxStep, centerDistance);
            x2 = fromX + centerDx / centerDistance * centerStep;
            y2 = fromY + centerDy / centerDistance * centerStep;
            if (!IsDirectClear(mapId, fromX, fromY, x2, y2))
                return false;
        }

        nextX = x2;
        nextY = y2;
        return true;
    }
}
