using System.Text;
using SGO.Server.Infrastructure.Legacy.Data;

namespace SGO.Server.Infrastructure.Legacy.World;

public sealed class LegacyWorldService
{
    private static readonly HashSet<int> CityMaps = [15];

    private readonly string _dataRoot;
    private readonly LegacyEnemyCatalog _enemyCatalog;
    private readonly Dictionary<int, IReadOnlyList<LegacyWorldNpc>> _npcCache = new();
    private readonly Dictionary<int, IReadOnlyList<LegacyWorldEnemy>> _enemyCache = new();

    public LegacyWorldService(string dataRoot)
    {
        _dataRoot = dataRoot;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _enemyCatalog = new LegacyEnemyCatalog(
            Path.Combine(_dataRoot, "Enemy", "Enemy_Data.csv"));
    }

    public IReadOnlyList<LegacyWorldNpc> GetNpcs(int mapId)
    {
        if (_npcCache.TryGetValue(mapId, out var cached))
            return cached;

        var path = Path.Combine(
            _dataRoot,
            "Map",
            $"Map{mapId:D2}",
            "NPC.dat");

        if (!File.Exists(path))
            return _npcCache[mapId] = Array.Empty<LegacyWorldNpc>();

        var raw = LegacyNpcDatReader.Read(path);
        var big5 = Encoding.GetEncoding(950);

        var list = raw.Select((npc, index) => new LegacyWorldNpc
        {
            EntityId = 2_000_000 + mapId * 10_000 + index,
            MapId = mapId,
            NpcTypeId = npc.NpcTypeId,
            X = npc.X,
            Y = npc.Y,
            Script = npc.Script,
            Name = DecodeName(big5, npc.NameBytes),
            Sprite = $"NPC{npc.NpcTypeId:D3}.png",
            IsPortal = IsPortalScript(npc.Script)
        }).ToArray();

        return _npcCache[mapId] = list;
    }

    public IReadOnlyList<LegacyWorldEnemy> GetEnemies(int mapId)
    {
        if (_enemyCache.TryGetValue(mapId, out var cached))
            return cached;

        var path = Path.Combine(
            _dataRoot,
            "Map",
            $"Map{mapId:D2}",
            "Enemy.dat");

        if (!File.Exists(path))
            return _enemyCache[mapId] = Array.Empty<LegacyWorldEnemy>();

        var raw = LegacyEnemyDatReader.Read(path);
        var list = new List<LegacyWorldEnemy>(raw.Count);

        for (var index = 0; index < raw.Count; index++)
        {
            var spawn = raw[index];
            var def = _enemyCatalog.Find(spawn.EnemyId);
            if (def is null)
                continue;

            list.Add(new LegacyWorldEnemy
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
                LegacyFlags = spawn.LegacyFlags
            });
        }

        return _enemyCache[mapId] = list;
    }

    public bool IsCityMap(int mapId) => CityMaps.Contains(mapId);

    public LegacyWorldNpc? FindNpc(int mapId, int entityId)
        => GetNpcs(mapId).FirstOrDefault(x => x.EntityId == entityId);

    public IReadOnlyList<object> BuildMapSpawnPackets(int mapId)
    {
        var packets = new List<object>();

        packets.AddRange(GetNpcs(mapId).Select(BuildNpcSpawnPacket));

        // Map15 is the main city. Its legacy Enemy.dat contains special records,
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

    private static object BuildNpcSpawnPacket(LegacyWorldNpc npc)
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
                LegacyNpcTypeID = npc.NpcTypeId
            }
        };

    private static object BuildEnemySpawnPacket(LegacyWorldEnemy enemy)
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
                LegacyEnemyID = enemy.EnemyId
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

public sealed class LegacyWorldNpc
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
}

public sealed class LegacyWorldEnemy
{
    public int EntityId { get; init; }
    public int MapId { get; init; }
    public int EnemyId { get; init; }
    public int LegacyFlags { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public string Name { get; init; } = "";
    public string Sprite { get; init; } = "";
    public int Level { get; init; }
    public int MaxHp { get; init; }
    public int CurrentHp { get; set; }
}
