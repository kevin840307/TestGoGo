using System.Text;

namespace SGO.Server.Legacy;

public sealed class LegacyWorldService
{
    private readonly string _dataRoot;
    private readonly Dictionary<int, IReadOnlyList<LegacyWorldNpc>> _npcCache = new();

    public LegacyWorldService(string dataRoot)
    {
        _dataRoot = dataRoot;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public IReadOnlyList<LegacyWorldNpc> GetNpcs(int mapId)
    {
        if (_npcCache.TryGetValue(mapId, out var cached))
            return cached;

        var path = Path.Combine(_dataRoot, "Map", $"Map{mapId:D2}", "NPC.dat");
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

    public LegacyWorldNpc? FindNpc(int mapId, int entityId)
        => GetNpcs(mapId).FirstOrDefault(x => x.EntityId == entityId);

    public IReadOnlyList<object> BuildNpcSpawnPackets(int mapId)
        => GetNpcs(mapId).Select(npc => (object)new
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
        }).ToArray();

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
        if (string.IsNullOrWhiteSpace(script)) return false;
        var s = script.ToLowerInvariant();
        return s.Contains("_to_") || s.Contains("to_") || s.Contains("_to");
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
