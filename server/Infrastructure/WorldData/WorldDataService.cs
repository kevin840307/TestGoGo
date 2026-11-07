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

    public EnemyDefinition? FindEnemyByName(string name)
        => _enemyCatalog.FindByName(name);

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
