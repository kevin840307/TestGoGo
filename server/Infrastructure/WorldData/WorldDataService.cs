using System.Collections.Concurrent;
using System.Text;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Infrastructure.WorldData;

public sealed class WorldDataService
{
    private static readonly HashSet<int> CityMaps = [15];

    private readonly string _dataRoot;
    private readonly EnemyCatalog _enemyCatalog;
    private readonly LegacyNpcVisualCatalog _npcVisuals;
    private readonly ConcurrentDictionary<int, Lazy<IReadOnlyList<WorldNpc>>> _npcCache = new();
    private readonly ConcurrentDictionary<int, Lazy<IReadOnlyList<WorldEnemy>>> _enemyCache = new();
    private readonly ConcurrentDictionary<int, Lazy<BlockingGrid?>> _blockingCache = new();

    public WorldDataService(string dataRoot)
        : this(
            dataRoot,
            new LegacyNpcVisualCatalog(string.Empty))
    {
    }

    public WorldDataService(
        string dataRoot,
        LegacyNpcVisualCatalog npcVisuals)
    {
        _dataRoot = dataRoot;
        _npcVisuals = npcVisuals;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _enemyCatalog = new EnemyCatalog(
            Path.Combine(_dataRoot, "Enemy", "Enemy_Data.csv"));
    }

    public IReadOnlyList<WorldNpc> GetNpcs(int mapId)
        => _npcCache.GetOrAdd(
            mapId,
            id => new Lazy<IReadOnlyList<WorldNpc>>(
                () => LoadNpcs(id),
                LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;

    private IReadOnlyList<WorldNpc> LoadNpcs(int mapId)
    {
        var path = Path.Combine(
            _dataRoot,
            "Map",
            $"Map{mapId:D2}",
            "NPC.dat");

        if (!File.Exists(path))
            return Array.Empty<WorldNpc>();

        var raw = NpcDatReader.Read(path);
        var big5 = Encoding.GetEncoding(950);

        return raw.Select((npc, index) =>
        {
            var isMerchant = IsMerchantScript(npc.Script);
            return new WorldNpc
            {
                EntityId = 2_000_000 + mapId * 10_000 + index,
                MapId = mapId,
                NpcTypeId = npc.NpcTypeId,
                X = npc.X,
                Y = npc.Y,
                Script = npc.Script,
                Name = DecodeName(big5, npc.NameBytes),
                Sprite = _npcVisuals.FindSprite(npc.NpcTypeId)
                    ?? $"NPC{GetNpcSpriteId(npc.NpcTypeId):D3}.png",
                IsPortal = IsPortalScript(npc.Script),
                IsMerchant = isMerchant
            };
        }).ToArray();
    }

    public IReadOnlyList<WorldEnemy> GetEnemies(int mapId)
        => _enemyCache.GetOrAdd(
            mapId,
            id => new Lazy<IReadOnlyList<WorldEnemy>>(
                () => LoadEnemies(id),
                LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;

    private IReadOnlyList<WorldEnemy> LoadEnemies(int mapId)
    {
        var path = Path.Combine(
            _dataRoot,
            "Map",
            $"Map{mapId:D2}",
            "Enemy.dat");

        if (!File.Exists(path))
            return Array.Empty<WorldEnemy>();

        var raw = EnemyDatReader.Read(path);
        var list = new List<WorldEnemy>(raw.Count);

        for (var index = 0; index < raw.Count; index++)
        {
            var spawn = raw[index];
            var def = _enemyCatalog.Find(spawn.EnemyId);
            if (def is null)
                continue;

            list.Add(new WorldEnemy
            {
                EntityId = 1_000_000 + mapId * 10_000 + index,
                MapId = mapId,
                EnemyId = spawn.EnemyId,
                X = spawn.X,
                Y = spawn.Y,
                Name = def.Name,
                Sprite = def.Sprite,
                Level = def.Level,
                MaxHp = def.MaxHp,
                CurrentHp = def.MaxHp,
                SourceFlags = spawn.SourceFlags
            });
        }

        return list;
    }

    public bool IsPositionBlocked(
        int mapId,
        double worldX,
        double worldY)
    {
        var grid = GetBlockingGrid(mapId);
        return grid is not null &&
               grid.IsBlockedWorld(worldX, worldY);
    }

    public bool TryResolveMove(
        int mapId,
        double startX,
        double startY,
        double targetX,
        double targetY,
        double maxDistance,
        out double nextX,
        out double nextY)
    {
        nextX = startX;
        nextY = startY;

        if (maxDistance <= 0.0)
            return false;

        var grid = GetBlockingGrid(mapId);

        // Maps without the legacy blocking file keep the old straight-line
        // behaviour instead of becoming immobile.
        if (grid is null)
            return TryMoveStraight(
                startX,
                startY,
                targetX,
                targetY,
                maxDistance,
                out nextX,
                out nextY);

        if (grid.IsSegmentWalkable(
                startX,
                startY,
                targetX,
                targetY))
        {
            return TryMoveStraight(
                startX,
                startY,
                targetX,
                targetY,
                maxDistance,
                out nextX,
                out nextY);
        }

        var start = grid.WorldToCell(startX, startY);
        var goal = grid.WorldToCell(targetX, targetY);

        if (!grid.IsInside(start.X, start.Y))
            return false;

        if (!grid.IsInside(goal.X, goal.Y) ||
            grid.IsBlocked(goal.X, goal.Y))
        {
            if (!grid.TryFindNearestWalkable(
                    goal.X,
                    goal.Y,
                    8,
                    out goal))
                return false;
        }

        var path = grid.FindPath(start, goal);
        if (path.Count < 2)
            return false;

        var bestX = startX;
        var bestY = startY;
        var found = false;

        // Follow as many original 16 px path cells as this AI tick allows.
        // Looking ahead reduces visible zig-zagging while preserving the
        // blocking-aware route.
        for (var i = 1; i < path.Count; i++)
        {
            var waypoint = grid.CellCenter(path[i].X, path[i].Y);
            var dx = waypoint.X - startX;
            var dy = waypoint.Y - startY;
            var distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance > maxDistance + 0.001)
                break;

            if (!grid.IsSegmentWalkable(
                    startX,
                    startY,
                    waypoint.X,
                    waypoint.Y))
                break;

            bestX = waypoint.X;
            bestY = waypoint.Y;
            found = true;
        }

        if (found)
        {
            nextX = bestX;
            nextY = bestY;
            return true;
        }

        var first = grid.CellCenter(path[1].X, path[1].Y);
        return TryMoveStraight(
            startX,
            startY,
            first.X,
            first.Y,
            maxDistance,
            out nextX,
            out nextY);
    }

    private BlockingGrid? GetBlockingGrid(int mapId)
        => _blockingCache.GetOrAdd(
            mapId,
            id => new Lazy<BlockingGrid?>(
                () => LoadBlockingGrid(id),
                LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;

    private BlockingGrid? LoadBlockingGrid(int mapId)
    {
        var path = Path.Combine(
            _dataRoot,
            "Map",
            $"Map{mapId:D2}",
            "Blocking.dat");

        if (!File.Exists(path))
            return null;

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 8)
                return null;

            var width = BitConverter.ToInt32(bytes, 0);
            var height = BitConverter.ToInt32(bytes, 4);
            var cellCount = (long)width * height;

            if (width <= 0 ||
                height <= 0 ||
                cellCount <= 0 ||
                cellCount > int.MaxValue ||
                bytes.Length < 8 + cellCount)
                return null;

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
        catch
        {
            return null;
        }
    }

    private static bool TryMoveStraight(
        double startX,
        double startY,
        double targetX,
        double targetY,
        double maxDistance,
        out double nextX,
        out double nextY)
    {
        nextX = startX;
        nextY = startY;

        var dx = targetX - startX;
        var dy = targetY - startY;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance <= 0.001)
            return false;

        var step = Math.Min(maxDistance, distance);
        nextX = startX + dx / distance * step;
        nextY = startY + dy / distance * step;
        return true;
    }

    private sealed class BlockingGrid
    {
        private const int CellSize = 16;
        private const int SearchWindowCells = 50;

        private static readonly (int Dx, int Dy, int Cost)[] Neighbors =
        [
            (1, 0, 10),
            (-1, 0, 10),
            (0, 1, 10),
            (0, -1, 10),
            (1, 1, 14),
            (1, -1, 14),
            (-1, 1, 14),
            (-1, -1, 14)
        ];

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

        public (int X, int Y) WorldToCell(
            double worldX,
            double worldY)
            => (
                (int)Math.Floor(worldX / CellSize),
                (int)Math.Floor(worldY / CellSize));

        public (double X, double Y) CellCenter(
            int x,
            int y)
            => (
                x * CellSize + CellSize / 2.0,
                y * CellSize + CellSize / 2.0);

        public bool IsInside(int x, int y)
            => x >= 0 &&
               y >= 0 &&
               x < _width &&
               y < _height;

        public bool IsBlocked(int x, int y)
        {
            if (!IsInside(x, y))
                return true;

            // Original client CSGOMap::IsBlocked checks only the low three
            // bits of Blocking.dat.
            return (_cells[y * _width + x] & 0x07) != 0;
        }

        public bool IsBlockedWorld(
            double worldX,
            double worldY)
        {
            var cell = WorldToCell(worldX, worldY);
            return IsBlocked(cell.X, cell.Y);
        }

        public bool IsSegmentWalkable(
            double startX,
            double startY,
            double endX,
            double endY)
        {
            var dx = endX - startX;
            var dy = endY - startY;
            var distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance <= 0.001)
                return !IsBlockedWorld(startX, startY);

            // Half-cell sampling catches thin blocked strips without turning
            // every movement check into a full path search.
            var samples = Math.Max(
                1,
                (int)Math.Ceiling(distance / (CellSize / 2.0)));

            for (var i = 0; i <= samples; i++)
            {
                var t = i / (double)samples;
                if (IsBlockedWorld(
                        startX + dx * t,
                        startY + dy * t))
                    return false;
            }

            return true;
        }

        public bool TryFindNearestWalkable(
            int originX,
            int originY,
            int radius,
            out (int X, int Y) result)
        {
            if (IsInside(originX, originY) &&
                !IsBlocked(originX, originY))
            {
                result = (originX, originY);
                return true;
            }

            for (var r = 1; r <= radius; r++)
            {
                for (var y = originY - r; y <= originY + r; y++)
                {
                    for (var x = originX - r; x <= originX + r; x++)
                    {
                        if (Math.Abs(x - originX) != r &&
                            Math.Abs(y - originY) != r)
                            continue;

                        if (IsInside(x, y) &&
                            !IsBlocked(x, y))
                        {
                            result = (x, y);
                            return true;
                        }
                    }
                }
            }

            result = default;
            return false;
        }

        public IReadOnlyList<(int X, int Y)> FindPath(
            (int X, int Y) start,
            (int X, int Y) goal)
        {
            if (start == goal)
                return [start];

            // The original CSGOMap path search operates on a local 50x50
            // blocking window. Keep the same bound so chasing cannot trigger
            // an unbounded whole-map search.
            var minX = Math.Max(
                0,
                Math.Min(start.X, goal.X) - 3);
            var minY = Math.Max(
                0,
                Math.Min(start.Y, goal.Y) - 3);
            var maxX = Math.Min(
                _width - 1,
                Math.Max(start.X, goal.X) + 3);
            var maxY = Math.Min(
                _height - 1,
                Math.Max(start.Y, goal.Y) + 3);

            if (maxX - minX + 1 > SearchWindowCells)
            {
                var center = (start.X + goal.X) / 2;
                minX = Math.Max(
                    0,
                    center - SearchWindowCells / 2);
                maxX = Math.Min(
                    _width - 1,
                    minX + SearchWindowCells - 1);
                minX = Math.Max(
                    0,
                    maxX - SearchWindowCells + 1);
            }

            if (maxY - minY + 1 > SearchWindowCells)
            {
                var center = (start.Y + goal.Y) / 2;
                minY = Math.Max(
                    0,
                    center - SearchWindowCells / 2);
                maxY = Math.Min(
                    _height - 1,
                    minY + SearchWindowCells - 1);
                minY = Math.Max(
                    0,
                    maxY - SearchWindowCells + 1);
            }

            if (start.X < minX ||
                start.X > maxX ||
                start.Y < minY ||
                start.Y > maxY ||
                goal.X < minX ||
                goal.X > maxX ||
                goal.Y < minY ||
                goal.Y > maxY)
                return Array.Empty<(int X, int Y)>();

            var frontier =
                new PriorityQueue<(int X, int Y), int>();
            var cameFrom =
                new Dictionary<(int X, int Y), (int X, int Y)>();
            var costSoFar =
                new Dictionary<(int X, int Y), int>
                {
                    [start] = 0
                };

            frontier.Enqueue(
                start,
                Heuristic(start, goal));

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                if (current == goal)
                    return ReconstructPath(
                        cameFrom,
                        start,
                        goal);

                foreach (var neighbor in Neighbors)
                {
                    var next = (
                        X: current.X + neighbor.Dx,
                        Y: current.Y + neighbor.Dy);

                    if (next.X < minX ||
                        next.X > maxX ||
                        next.Y < minY ||
                        next.Y > maxY ||
                        IsBlocked(next.X, next.Y))
                        continue;

                    if (neighbor.Dx != 0 &&
                        neighbor.Dy != 0 &&
                        (IsBlocked(
                             current.X + neighbor.Dx,
                             current.Y) ||
                         IsBlocked(
                             current.X,
                             current.Y + neighbor.Dy)))
                        continue;

                    var newCost =
                        costSoFar[current] + neighbor.Cost;

                    if (costSoFar.TryGetValue(
                            next,
                            out var oldCost) &&
                        oldCost <= newCost)
                        continue;

                    costSoFar[next] = newCost;
                    cameFrom[next] = current;
                    frontier.Enqueue(
                        next,
                        newCost + Heuristic(next, goal));
                }
            }

            return Array.Empty<(int X, int Y)>();
        }

        private static int Heuristic(
            (int X, int Y) a,
            (int X, int Y) b)
        {
            var dx = Math.Abs(a.X - b.X);
            var dy = Math.Abs(a.Y - b.Y);
            var diagonal = Math.Min(dx, dy);
            var straight = Math.Max(dx, dy) - diagonal;
            return diagonal * 14 + straight * 10;
        }

        private static IReadOnlyList<(int X, int Y)> ReconstructPath(
            Dictionary<
                (int X, int Y),
                (int X, int Y)> cameFrom,
            (int X, int Y) start,
            (int X, int Y) goal)
        {
            var path = new List<(int X, int Y)> { goal };
            var current = goal;

            while (current != start)
            {
                if (!cameFrom.TryGetValue(
                        current,
                        out current))
                    return Array.Empty<(int X, int Y)>();

                path.Add(current);
            }

            path.Reverse();
            return path;
        }
    }

    public bool IsCityMap(int mapId) => CityMaps.Contains(mapId);

    public WorldNpc? FindNpc(int mapId, int entityId)
        => GetNpcs(mapId).FirstOrDefault(x => x.EntityId == entityId);

    public IReadOnlyList<object> BuildMapSpawnPackets(int mapId)
    {
        var packets = new List<object>();

        packets.AddRange(GetNpcs(mapId).Select(BuildNpcSpawnPacket));

        // Map15 is the main city. Its Enemy.dat contains special records,
        // but normal combat monsters are intentionally not spawned there.
        if (!CityMaps.Contains(mapId))
        {
            packets.AddRange(
                GetEnemies(mapId)
                    .Where(x => x.Level < 65535)
                    .Select(BuildEnemySpawnPacket));
        }

        return packets;
    }

    public IReadOnlyList<object> BuildNpcSpawnPackets(int mapId)
        => GetNpcs(mapId)
            .Select(BuildNpcSpawnPacket)
            .Cast<object>()
            .ToArray();

    private static object BuildNpcSpawnPacket(WorldNpc npc)
        => new
        {
            Type = "SpawnEntity",
            EntityID = npc.EntityId,
            EntityType = 2,
            Name = npc.Name,
            LocX = npc.X,
            LocY = npc.Y,
            ExtraData = new
            {
                Sprite = npc.Sprite,
                Script = npc.Script,
                IsPortal = npc.IsPortal,
                IsMerchant = npc.IsMerchant,
                NpcTypeID = npc.NpcTypeId
            }
        };

    private static object BuildEnemySpawnPacket(WorldEnemy enemy)
        => new
        {
            Type = "SpawnEntity",
            EntityID = enemy.EntityId,
            EntityType = 1,
            Name = enemy.Name,
            LocX = enemy.X,
            LocY = enemy.Y,
            ExtraData = new
            {
                Sprite = enemy.Sprite,
                Level = Math.Max(1, enemy.Level),
                MaxHP = enemy.MaxHp,
                CurrentHP = enemy.CurrentHp,
                Scale = 1.0,
                RequireCtrlToAttack = false,
                IsWorldBoss = false,
                EnemyID = enemy.EnemyId
            }
        };

    private static string DecodeName(Encoding big5, byte[] bytes)
    {
        if (bytes.Length == 0) return "NPC";

        try
        {
            var name = big5.GetString(bytes).Trim();
            return string.IsNullOrWhiteSpace(name) ? "NPC" : name;
        }
        catch
        {
            return "NPC";
        }
    }

    private static int GetNpcSpriteId(int npcTypeId)
    {
        // NPC.dat stores additional type bits above the low byte.
        // The client asset index is the low 8-bit sprite id.
        return npcTypeId & 0xFF;
    }

    private static bool IsMerchantScript(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return false;

        var s = script.ToLowerInvariant();
        return s.StartsWith("shop_") ||
               s.Contains("_shop") ||
               s.Contains("all_shop");
    }

    private static bool IsPortalScript(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return false;

        var s = script.ToLowerInvariant();
        return s.Contains("_to_") ||
               s.Contains("to_") ||
               s.Contains("_to");
    }
}

public sealed class WorldNpc
{
    public int EntityId { get; init; }
    public int MapId { get; init; }
    public int NpcTypeId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public string Script { get; init; } = "";
    public string Name { get; init; } = "";
    public string Sprite { get; init; } = "";
    public bool IsPortal { get; init; }
    public bool IsMerchant { get; init; }
}

public sealed class WorldEnemy
{
    public int EntityId { get; init; }
    public int MapId { get; init; }
    public int EnemyId { get; init; }
    public int SourceFlags { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public string Name { get; init; } = "";
    public string Sprite { get; init; } = "";
    public int Level { get; init; }
    public int MaxHp { get; init; }
    public int CurrentHp { get; set; }
}
