using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace SGO.Server.Infrastructure.GameData;

public sealed record EnemyDropDefinition(
    int LegacyItemId,
    int ItemId,
    string Name,
    int TabType,
    double Chance,
    bool ChecksLuck);

public sealed class EnemyDropCatalog
{
    private const int FirstDropColumn = 74;
    private const int CheckLuckItemCount = 30;
    private const int NoCheckLuckItemCount = 30;

    private readonly Dictionary<int, List<EnemyDropDefinition>> _drops = new();
    private readonly Dictionary<int, List<LegacyItemReference>> _legacyItems = new();
    private readonly CurrentClientDataCatalog _current;
    private int[] _luckThresholds = [0, 0, 50, 90, 140, 200];

    public EnemyDropCatalog(
        string dataRoot,
        CurrentClientDataCatalog current)
    {
        _current = current;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        LoadLegacyGeneral(Path.Combine(dataRoot, "Item", "General.itd"));
        LoadLegacyEquip(Path.Combine(dataRoot, "Item", "Equip.itd"));
        LoadLegacyWeapons(Path.Combine(dataRoot, "Item", "Weapon.itd"));
        LoadLuckConfig(ResolveConfigPath(dataRoot, "enemy.cfg"));
        LoadDrops(Path.Combine(dataRoot, "Enemy", "Enemy_Data.csv"));
    }

    public IReadOnlyList<EnemyDropDefinition> GetDrops(int enemyId)
        => _drops.TryGetValue(enemyId, out var drops)
            ? drops
            : Array.Empty<EnemyDropDefinition>();

    public int LuckRollCount(int lucky)
    {
        var highestTier = 1;

        for (var i = 1; i < _luckThresholds.Length; i++)
        {
            if (lucky >= _luckThresholds[i])
                highestTier = i;
        }

        // enemy.cfg contains Luck00=0 and Luck01=0. Treat tier 1 as the
        // baseline single roll; positive thresholds add extra independent
        // rolls for the 30 "CheckLuckItem" slots.
        return Math.Max(1, highestTier);
    }

    public IReadOnlyList<EnemyDropDefinition> RollDrops(
        int enemyId,
        int entityId,
        int killSequence,
        int lucky)
    {
        var drops = GetDrops(enemyId);
        if (drops.Count == 0)
            return Array.Empty<EnemyDropDefinition>();

        var result = new List<EnemyDropDefinition>();
        for (var i = 0; i < drops.Count; i++)
        {
            var drop = drops[i];
            var rollCount = drop.ChecksLuck
                ? LuckRollCount(Math.Max(0, lucky))
                : 1;

            for (var attempt = 0; attempt < rollCount; attempt++)
            {
                if (Roll01(
                        enemyId,
                        entityId,
                        killSequence,
                        drop.ItemId,
                        i,
                        attempt) >= drop.Chance)
                    continue;

                result.Add(drop);
                break;
            }
        }

        return result;
    }

    private void LoadLuckConfig(string path)
    {
        if (!File.Exists(path))
            return;

        var values = new SortedDictionary<int, int>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Split(
                    "//",
                    2,
                    StringSplitOptions.None)[0]
                .Trim();

            if (!line.StartsWith(
                    "Luck",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var equals = line.IndexOf('=');
            if (equals <= 4)
                continue;

            if (!int.TryParse(
                    line.AsSpan(4, equals - 4),
                    out var index) ||
                !int.TryParse(
                    line[(equals + 1)..].Trim(),
                    out var value))
                continue;

            values[index] = Math.Max(0, value);
        }

        if (values.Count >= 2)
            _luckThresholds = values.Values.ToArray();
    }

    private static string ResolveConfigPath(
        string dataRoot,
        string file)
    {
        var copied = Path.Combine(dataRoot, file);
        if (File.Exists(copied))
            return copied;

        var legacy = Path.Combine(
            Directory.GetParent(dataRoot)?.FullName ?? dataRoot,
            file);
        return legacy;
    }

    private void LoadDrops(string path)
    {
        if (!File.Exists(path))
            return;

        var big5 = Encoding.GetEncoding(950);
        foreach (var raw in File.ReadLines(path, big5))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var cols = raw.Split(',');
            if (cols.Length <= FirstDropColumn ||
                !int.TryParse(cols[0].Trim(), out var enemyId))
                continue;

            var mapped = new List<EnemyDropDefinition>();
            var totalSlots = CheckLuckItemCount + NoCheckLuckItemCount;

            for (var slot = 0; slot < totalSlots; slot++)
            {
                var itemColumn = FirstDropColumn + slot * 2;
                var rateColumn = itemColumn + 1;
                if (rateColumn >= cols.Length)
                    break;

                if (!int.TryParse(
                        cols[itemColumn].Trim(),
                        out var legacyItemId) ||
                    legacyItemId <= 0)
                    continue;

                if (!double.TryParse(
                        cols[rateColumn].Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var chance) ||
                    chance <= 0)
                    continue;

                var current = ResolveCurrentItem(legacyItemId);
                if (current is null)
                    continue;

                mapped.Add(new EnemyDropDefinition(
                    legacyItemId,
                    current.ItemId,
                    current.Name,
                    CurrentTabType(current.Category),
                    Math.Clamp(chance, 0.0, 1.0),
                    slot < CheckLuckItemCount));
            }

            _drops[enemyId] = mapped;
        }
    }

    private CurrentItemDefinition? ResolveCurrentItem(int legacyItemId)
    {
        if (!_legacyItems.TryGetValue(
                legacyItemId,
                out var legacyReferences))
            return null;

        foreach (var legacy in legacyReferences)
        {
            var sameId = _current.FindItem(legacyItemId);
            if (sameId is not null &&
                CategoryCompatible(
                    sameId.Category,
                    legacy.Category) &&
                SameName(sameId.Name, legacy.Name))
            {
                return sameId;
            }

            var byName = _current.FindItemByName(
                legacy.Name,
                legacy.Category);

            byName ??= _current.FindItemByName(legacy.Name);

            if (byName is not null &&
                CategoryCompatible(
                    byName.Category,
                    legacy.Category))
            {
                return byName;
            }
        }

        return null;
    }

    private void LoadLegacyGeneral(string path)
    {
        const int recordSize = 342;
        const int nameOffset = 46;
        const int nameLength = 20;

        if (!File.Exists(path))
            return;

        var bytes = File.ReadAllBytes(path);
        var big5 = Encoding.GetEncoding(950);

        for (var offset = 0;
             offset + recordSize <= bytes.Length;
             offset += recordSize)
        {
            if (bytes[offset] != 6)
                continue;

            var itemId = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(offset + 8, 4));
            var name = ReadFixedString(
                big5,
                bytes,
                offset + nameOffset,
                nameLength);

            AddLegacyItem(itemId, name, "general");
        }
    }

    private void LoadLegacyEquip(string path)
    {
        const int recordSize = 1123;
        const int nameOffset = 79;
        const int nameLength = 20;

        if (!File.Exists(path))
            return;

        var bytes = File.ReadAllBytes(path);
        var big5 = Encoding.GetEncoding(950);

        for (var offset = 0;
             offset + recordSize <= bytes.Length;
             offset += recordSize)
        {
            if (bytes[offset] != 0)
                continue;

            var itemId = BinaryPrimitives.ReadUInt16LittleEndian(
                bytes.AsSpan(offset + 23, 2));
            var name = ReadFixedString(
                big5,
                bytes,
                offset + nameOffset,
                nameLength);

            AddLegacyItem(itemId, name, "equip");
        }
    }

    private void LoadLegacyWeapons(string path)
    {
        const int recordSize = 1247;
        const int nameOffset = 103;
        const int nameLength = 20;

        if (!File.Exists(path))
            return;

        var bytes = File.ReadAllBytes(path);
        var big5 = Encoding.GetEncoding(950);

        for (var offset = 0;
             offset + recordSize <= bytes.Length;
             offset += recordSize)
        {
            if (bytes[offset] != 1)
                continue;

            var itemId = BinaryPrimitives.ReadUInt16LittleEndian(
                bytes.AsSpan(offset + 43, 2));
            var name = ReadFixedString(
                big5,
                bytes,
                offset + nameOffset,
                nameLength);

            AddLegacyItem(itemId, name, "weapon");
        }
    }

    private void AddLegacyItem(
        int itemId,
        string name,
        string category)
    {
        if (itemId <= 0 || string.IsNullOrWhiteSpace(name))
            return;

        if (!_legacyItems.TryGetValue(itemId, out var list))
            _legacyItems[itemId] = list = [];

        list.Add(new LegacyItemReference(
            itemId,
            name,
            category));
    }

    private static bool CategoryCompatible(
        string currentCategory,
        string legacyCategory)
    {
        var current = currentCategory.Trim().ToLowerInvariant();
        var legacy = legacyCategory.Trim().ToLowerInvariant();

        return legacy switch
        {
            "general" => current is "general" or "booster",
            "equip" => current == "equip",
            "weapon" => current == "weapon",
            _ => false
        };
    }

    private static bool SameName(string a, string b)
        => CurrentClientDataCatalog.NormalizeName(a) ==
           CurrentClientDataCatalog.NormalizeName(b);

    private static int CurrentTabType(string category)
        => category.Trim().ToLowerInvariant() switch
        {
            "equip" or "weapon" => 1,
            _ => 0
        };

    private static string ReadFixedString(
        Encoding encoding,
        byte[] bytes,
        int offset,
        int length)
    {
        var end = offset;
        var max = Math.Min(bytes.Length, offset + length);
        while (end < max && bytes[end] != 0)
            end++;

        return end <= offset
            ? string.Empty
            : encoding.GetString(
                bytes,
                offset,
                end - offset).Trim();
    }

    private static double Roll01(
        int enemyId,
        int entityId,
        int killSequence,
        int itemId,
        int slot,
        int attempt)
    {
        unchecked
        {
            uint x = 2166136261;
            x = (x ^ (uint)enemyId) * 16777619;
            x = (x ^ (uint)entityId) * 16777619;
            x = (x ^ (uint)killSequence) * 16777619;
            x = (x ^ (uint)itemId) * 16777619;
            x = (x ^ (uint)slot) * 16777619;
            x = (x ^ (uint)attempt) * 16777619;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;

            return (x & 0x00FF_FFFF) / 16_777_216.0;
        }
    }

    private sealed record LegacyItemReference(
        int ItemId,
        string Name,
        string Category);
}
