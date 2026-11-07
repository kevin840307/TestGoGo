using System.Collections.Concurrent;
using System.Buffers.Binary;

namespace SGO.Server.Infrastructure.WorldData;

/// <summary>
/// Reads the original map Blocking.dat format used by the legacy client.
/// The file is the single authoritative collision source for client pathing,
/// Godot runtime collision generation, and server movement validation.
/// </summary>
public sealed record MoveValidationResult(
    bool Allowed,
    string Reason,
    int FromCellX,
    int FromCellY,
    int ToCellX,
    int ToCellY);

public sealed class BlockingMapService
{
    public const int CellSize = 16;
    private const byte BlockingMask = 0x07;
    private const byte SpecialTerrainFlag = 0x03;
    private const double SampleStep = CellSize / 2.0;

    private readonly string _dataRoot;
    private readonly ConcurrentDictionary<int, Lazy<BlockingGrid?>> _cache = new();

    public BlockingMapService(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    public bool HasMap(int mapId) => GetGrid(mapId) is not null;

    public bool IsMoveAllowed(
        int mapId,
        double fromX,
        double fromY,
        double toX,
        double toY,
        bool canCrossSpecialTerrain)
        => ValidateMove(
            mapId,
            fromX,
            fromY,
            toX,
            toY,
            canCrossSpecialTerrain).Allowed;

    public MoveValidationResult ValidateMove(
        int mapId,
        double fromX,
        double fromY,
        double toX,
        double toY,
        bool canCrossSpecialTerrain)
    {
        var grid = GetGrid(mapId);
        if (grid is null)
        {
            return new MoveValidationResult(
                false,
                "map_missing",
                -1,
                -1,
                -1,
                -1);
        }

        var fromCellX = (int)Math.Floor(fromX / CellSize);
        var fromCellY = (int)Math.Floor(fromY / CellSize);
        var toCellX = (int)Math.Floor(toX / CellSize);
        var toCellY = (int)Math.Floor(toY / CellSize);

        if (!grid.IsWalkable(
                fromCellX,
                fromCellY,
                canCrossSpecialTerrain))
        {
            return new MoveValidationResult(
                false,
                "start_blocked",
                fromCellX,
                fromCellY,
                toCellX,
                toCellY);
        }

        if (!grid.IsWalkable(
                toCellX,
                toCellY,
                canCrossSpecialTerrain))
        {
            return new MoveValidationResult(
                false,
                "end_blocked",
                fromCellX,
                fromCellY,
                toCellX,
                toCellY);
        }

        if (IsStraightSegmentWalkable(
                grid,
                fromX,
                fromY,
                toX,
                toY,
                canCrossSpecialTerrain))
        {
            return new MoveValidationResult(
                true,
                "straight",
                fromCellX,
                fromCellY,
                toCellX,
                toCellY);
        }

        // Client movement follows Blocking.dat waypoints. Around a corner,
        // two consecutive network samples can form a chord that intersects
        // a blocked cell even though the real travelled polyline was legal.
        // Validate local grid connectivity instead of requiring that chord
        // itself to be clear. The bounded search keeps this inexpensive and
        // still rejects true wall tunnelling.
        var hasLocalRoute = HasShortLocalRoute(
            grid,
            fromCellX,
            fromCellY,
            toCellX,
            toCellY,
            canCrossSpecialTerrain);

        return new MoveValidationResult(
            hasLocalRoute,
            hasLocalRoute
                ? "local_route"
                : "local_route_failed",
            fromCellX,
            fromCellY,
            toCellX,
            toCellY);
    }

    private static bool IsStraightSegmentWalkable(
        BlockingGrid grid,
        double fromX,
        double fromY,
        double toX,
        double toY,
        bool canCrossSpecialTerrain)
    {
        var dx = toX - fromX;
        var dy = toY - fromY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var samples = Math.Max(
            1,
            (int)Math.Ceiling(distance / SampleStep));

        for (var i = 0; i <= samples; i++)
        {
            var t = (double)i / samples;
            var x = fromX + dx * t;
            var y = fromY + dy * t;

            var cellX = (int)Math.Floor(x / CellSize);
            var cellY = (int)Math.Floor(y / CellSize);

            if (!grid.IsWalkable(
                    cellX,
                    cellY,
                    canCrossSpecialTerrain))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasShortLocalRoute(
        BlockingGrid grid,
        int startX,
        int startY,
        int goalX,
        int goalY,
        bool canCrossSpecialTerrain)
    {
        if (startX == goalX && startY == goalY)
            return true;

        const int margin = 3;
        const int maxSearchWindowCells = 50;

        var minX = Math.Min(startX, goalX) - margin;
        var maxX = Math.Max(startX, goalX) + margin;
        var minY = Math.Min(startY, goalY) - margin;
        var maxY = Math.Max(startY, goalY) + margin;

        if (maxX - minX + 1 > maxSearchWindowCells ||
            maxY - minY + 1 > maxSearchWindowCells)
        {
            return false;
        }

        var queue = new Queue<(int X, int Y)>();
        var visited = new HashSet<(int X, int Y)>();

        queue.Enqueue((startX, startY));
        visited.Add((startX, startY));

        var directions = new (int X, int Y)[]
        {
            (1, 0), (-1, 0), (0, 1), (0, -1),
            (1, 1), (1, -1), (-1, 1), (-1, -1)
        };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var direction in directions)
            {
                var nextX = current.X + direction.X;
                var nextY = current.Y + direction.Y;

                if (nextX < minX ||
                    nextX > maxX ||
                    nextY < minY ||
                    nextY > maxY)
                {
                    continue;
                }

                if (!grid.IsWalkable(
                        nextX,
                        nextY,
                        canCrossSpecialTerrain))
                {
                    continue;
                }

                // Match the client pathfinder: diagonal movement cannot cut
                // through the corner between two orthogonally blocked cells.
                if (direction.X != 0 &&
                    direction.Y != 0 &&
                    (!grid.IsWalkable(
                         current.X + direction.X,
                         current.Y,
                         canCrossSpecialTerrain) ||
                     !grid.IsWalkable(
                         current.X,
                         current.Y + direction.Y,
                         canCrossSpecialTerrain)))
                {
                    continue;
                }

                if (nextX == goalX &&
                    nextY == goalY)
                {
                    return true;
                }

                if (visited.Add((nextX, nextY)))
                    queue.Enqueue((nextX, nextY));
            }
        }

        return false;
    }

    public byte GetFlags(int mapId, double worldX, double worldY)
    {
        var grid = GetGrid(mapId);
        if (grid is null)
            return byte.MaxValue;

        var cellX = (int)Math.Floor(worldX / CellSize);
        var cellY = (int)Math.Floor(worldY / CellSize);
        return grid.GetFlags(cellX, cellY);
    }

    private BlockingGrid? GetGrid(int mapId)
        => _cache.GetOrAdd(
                mapId,
                id => new Lazy<BlockingGrid?>(
                    () => Load(id),
                    LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;

    private BlockingGrid? Load(int mapId)
    {
        var path = Path.Combine(
            _dataRoot,
            "Map",
            $"Map{mapId:D2}",
            "Blocking.dat");

        if (!File.Exists(path))
            return null;

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 8)
            return null;

        var width = BinaryPrimitives.ReadInt32LittleEndian(
            bytes.AsSpan(0, 4));
        var height = BinaryPrimitives.ReadInt32LittleEndian(
            bytes.AsSpan(4, 4));

        if (width <= 0 || height <= 0)
            return null;

        var cellCount = (long)width * height;
        if (cellCount > int.MaxValue ||
            bytes.LongLength < 8L + cellCount)
        {
            return null;
        }

        var cells = new byte[(int)cellCount];
        Buffer.BlockCopy(
            bytes,
            8,
            cells,
            0,
            cells.Length);

        return new BlockingGrid(
            width,
            height,
            cells);
    }

    private sealed class BlockingGrid
    {
        private readonly int _width;
        private readonly int _height;
        private readonly byte[] _cells;

        public BlockingGrid(
            int width,
            int height,
            byte[] cells)
        {
            _width = width;
            _height = height;
            _cells = cells;
        }

        public byte GetFlags(int x, int y)
        {
            if (x < 0 ||
                y < 0 ||
                x >= _width ||
                y >= _height)
            {
                return byte.MaxValue;
            }

            return (byte)(_cells[y * _width + x] & BlockingMask);
        }

        public bool IsWalkable(
            int x,
            int y,
            bool canCrossSpecialTerrain)
        {
            var flags = GetFlags(x, y);

            if (flags == 0)
                return true;

            return canCrossSpecialTerrain &&
                   flags == SpecialTerrainFlag;
        }
    }
}
