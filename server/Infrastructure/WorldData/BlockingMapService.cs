using System.Collections.Concurrent;
using System.Buffers.Binary;

namespace SGO.Server.Infrastructure.WorldData;

/// <summary>
/// Reads the original map Blocking.dat format used by the legacy client.
/// The file is the single authoritative collision source for client pathing,
/// Godot runtime collision generation, and server movement validation.
/// </summary>
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
    {
        var grid = GetGrid(mapId);
        if (grid is null)
            return false;

        var dx = toX - fromX;
        var dy = toY - fromY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var samples = Math.Max(1, (int)Math.Ceiling(distance / SampleStep));

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
