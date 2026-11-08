namespace SGO.Server.Application.World;

/// <summary>
/// One point representing an alive player or one of that player's pets.
/// OwnerIndex is the position in the SharedMapAiTicker's CharacterId-sorted
/// player list; lower indices win equal-distance ties, as with the old scan.
/// </summary>
public readonly record struct MapAiOwnerPoint(int OwnerIndex, double X, double Y);

/// <summary>
/// Immutable nearest-owner index built once per active map tick. Its inputs are
/// snapshots; no session, combat or SQLite state is kept here. Nearest-neighbor
/// queries are exact (not limited to AOI radius), so an idle monster with no
/// nearby player still has the same owner as before the optimization.
/// </summary>
public sealed class MapAiOwnerSpatialIndex
{
    private sealed class Node
    {
        public Node(MapAiOwnerPoint point, int axis, Node? lower, Node? higher)
        {
            Point = point;
            Axis = axis;
            Lower = lower;
            Higher = higher;
        }

        public MapAiOwnerPoint Point { get; }
        public int Axis { get; }
        public Node? Lower { get; }
        public Node? Higher { get; }
    }

    private readonly MapAiOwnerPoint[] _small;
    private readonly Node? _root;

    public MapAiOwnerSpatialIndex(IEnumerable<MapAiOwnerPoint> points)
    {
        var valid = points
            .Where(p => p.OwnerIndex >= 0 &&
                        double.IsFinite(p.X) &&
                        double.IsFinite(p.Y))
            .ToArray();

        // Do not allocate/sort a tree for the common small-party case.
        if (valid.Length <= 16)
        {
            _small = valid;
            return;
        }

        _small = Array.Empty<MapAiOwnerPoint>();
        _root = Build(valid, 0, valid.Length, 0);
    }

    public int FindNearestOwnerIndex(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            return -1;

        var bestDistance = double.MaxValue;
        var bestIndex = int.MaxValue;

        if (_root is null)
        {
            foreach (var point in _small)
                Consider(point, x, y, ref bestDistance, ref bestIndex);
        }
        else
        {
            Search(_root, x, y, ref bestDistance, ref bestIndex);
        }

        return bestIndex == int.MaxValue ? -1 : bestIndex;
    }

    private static Node? Build(
        MapAiOwnerPoint[] points, int start, int end, int depth)
    {
        if (start >= end)
            return null;

        var axis = depth % 2;
        Array.Sort(
            points,
            start,
            end - start,
            Comparer<MapAiOwnerPoint>.Create((left, right) =>
            {
                var primary = axis == 0
                    ? left.X.CompareTo(right.X)
                    : left.Y.CompareTo(right.Y);
                if (primary != 0)
                    return primary;

                var secondary = axis == 0
                    ? left.Y.CompareTo(right.Y)
                    : left.X.CompareTo(right.X);
                return secondary != 0
                    ? secondary
                    : left.OwnerIndex.CompareTo(right.OwnerIndex);
            }));

        var middle = start + (end - start) / 2;
        // Construct children after capturing the median; sorting a child
        // segment cannot change the parent's selected point.
        var median = points[middle];
        var lower = Build(points, start, middle, depth + 1);
        var higher = Build(points, middle + 1, end, depth + 1);
        return new Node(median, axis, lower, higher);
    }

    private static void Search(
        Node? node, double x, double y,
        ref double bestDistance, ref int bestIndex)
    {
        if (node is null)
            return;

        Consider(node.Point, x, y, ref bestDistance, ref bestIndex);

        var delta = node.Axis == 0
            ? x - node.Point.X
            : y - node.Point.Y;
        var near = delta <= 0 ? node.Lower : node.Higher;
        var far = delta <= 0 ? node.Higher : node.Lower;

        Search(near, x, y, ref bestDistance, ref bestIndex);

        // A point across the splitting plane may be equally close; ties
        // must still favor the earlier CharacterId-sorted owner.
        if (delta * delta <= bestDistance)
            Search(far, x, y, ref bestDistance, ref bestIndex);
    }

    private static void Consider(
        MapAiOwnerPoint point, double x, double y,
        ref double bestDistance, ref int bestIndex)
    {
        var dx = point.X - x;
        var dy = point.Y - y;
        var distance = dx * dx + dy * dy;
        if (distance >= double.MaxValue)
            return;

        if (distance < bestDistance ||
            (distance == bestDistance && point.OwnerIndex < bestIndex))
        {
            bestDistance = distance;
            bestIndex = point.OwnerIndex;
        }
    }
}
