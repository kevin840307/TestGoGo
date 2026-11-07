using System.Text.RegularExpressions;

namespace SGO.Server.Infrastructure.GameData;

public sealed record CurrentItemDefinition(
    int ItemId,
    string Name,
    string Category,
    int Price,
    int CanSell,
    int Atk,
    int Def,
    int MAtk,
    int MDef,
    int Hit,
    int Flee,
    int Luk,
    int Aspd,
    int MoveSpeed,
    int CritRate,
    int CritDamage,
    int MaxHpBonus,
    int MaxMpBonus,
    int MaxSpBonus,
    int EquipSlot,
    int AttackRange,
    int RecoverHp,
    int RecoverMp,
    int RecoverSp,
    int MaxBattery,
    int Battery,
    string HorseType);

public sealed record CurrentSkillDefinition(
    int SkillId,
    string Name);

public sealed class CurrentClientDataCatalog
{
    private static readonly string[] ItemFolders =
    [
        "booster",
        "equip",
        "general",
        "recovery",
        "weapon",
        "horse",
        "horseequip",
        "horsebattery"
    ];

    private readonly Dictionary<int, CurrentItemDefinition> _items = new();
    private readonly Dictionary<string, List<CurrentItemDefinition>> _itemsByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, CurrentSkillDefinition> _skills = new();
    private readonly Dictionary<string, List<CurrentSkillDefinition>> _skillsByName =
        new(StringComparer.OrdinalIgnoreCase);

    public string? Root { get; }
    public bool IsAvailable => Root is not null;

    public CurrentClientDataCatalog(string? configuredRoot)
    {
        Root = ResolveRoot(configuredRoot);
        if (Root is null)
            return;

        LoadItems(Root);
        LoadSkills(Root);
    }

    public CurrentClientDataCatalog(
        IEnumerable<CurrentItemDefinition> items,
        IEnumerable<CurrentSkillDefinition> skills)
    {
        Root = "<in-memory>";

        foreach (var item in items)
        {
            _items[item.ItemId] = item;
            AddByName(_itemsByName, item.Name, item);
        }

        foreach (var skill in skills)
        {
            _skills[skill.SkillId] = skill;
            AddByName(_skillsByName, skill.Name, skill);
        }
    }

    public CurrentItemDefinition? FindItem(int id)
        => _items.TryGetValue(id, out var item) ? item : null;

    public CurrentItemDefinition? FindItemByName(
        string name,
        string? preferredCategory = null)
    {
        var key = NormalizeName(name);
        if (string.IsNullOrWhiteSpace(key) ||
            !_itemsByName.TryGetValue(key, out var matches))
            return null;

        if (!string.IsNullOrWhiteSpace(preferredCategory))
        {
            var categoryMatch = matches.FirstOrDefault(x =>
                x.Category.Equals(
                    preferredCategory,
                    StringComparison.OrdinalIgnoreCase));
            if (categoryMatch is not null)
                return categoryMatch;
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    public CurrentSkillDefinition? FindSkill(int id)
        => _skills.TryGetValue(id, out var skill) ? skill : null;

    public CurrentSkillDefinition? FindSkillByName(string name)
    {
        var key = NormalizeName(name);
        if (string.IsNullOrWhiteSpace(key) ||
            !_skillsByName.TryGetValue(key, out var matches))
            return null;

        return matches.Count == 1 ? matches[0] : null;
    }

    public IReadOnlyCollection<CurrentItemDefinition> Items => _items.Values;
    public IReadOnlyCollection<CurrentSkillDefinition> Skills => _skills.Values;

    private static string? ResolveRoot(string? configuredRoot)
    {
        var candidates = new[]
        {
            configuredRoot,
            Environment.GetEnvironmentVariable("SGO_CLIENT_ROOT"),
            @"C:SGORecovered",
            @"C:SGO重置版本Recovered"
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var root = Path.GetFullPath(candidate);
            if (Directory.Exists(Path.Combine(root, "Items")) &&
                Directory.Exists(Path.Combine(root, "Skills")))
                return root;
        }

        return null;
    }

    private void LoadItems(string root)
    {
        foreach (var folder in ItemFolders)
        {
            var dir = Path.Combine(root, "Items", folder);
            if (!Directory.Exists(dir))
                continue;

            foreach (var path in Directory.EnumerateFiles(
                         dir,
                         "*.tres",
                         SearchOption.TopDirectoryOnly))
            {
                var text = File.ReadAllText(path);
                var id = ReadInt(text, "id");
                if (id <= 0 &&
                    !int.TryParse(
                        Path.GetFileNameWithoutExtension(path),
                        out id))
                    continue;
                var item = new CurrentItemDefinition(
                    id,
                    ReadString(text, "item_name"),
                    folder,
                    ReadInt(text, "price"),
                    ReadInt(text, "can_sell"),
                    ReadInt(text, "atk"),
                    ReadInt(text, "def"),
                    ReadInt(text, "matk"),
                    ReadInt(text, "mdef"),
                    ReadInt(text, "hit"),
                    ReadInt(text, "flee"),
                    ReadInt(text, "luk"),
                    ReadInt(text, "aspd"),
                    ReadInt(text, "move_speed"),
                    ReadInt(text, "crit_rate"),
                    ReadInt(text, "crit_damage"),
                    ReadInt(text, "max_hp_bonus"),
                    ReadInt(text, "max_mp_bonus"),
                    ReadInt(text, "max_sp_bonus"),
                    ReadInt(text, "equip_slot"),
                    ReadInt(text, "attack_range"),
                    ReadInt(text, "recover_hp"),
                    ReadInt(text, "recover_mp"),
                    ReadInt(text, "recover_sp"),
                    ReadInt(text, "max_battery"),
                    ReadInt(text, "battery"),
                    ReadString(text, "horse_type"));

                _items[id] = item;
                AddByName(_itemsByName, item.Name, item);
            }
        }
    }

    private void LoadSkills(string root)
    {
        var dir = Path.Combine(root, "Skills");
        if (!Directory.Exists(dir))
            return;

        foreach (var path in Directory.EnumerateFiles(
                     dir,
                     "*.tres",
                     SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(path);
            var id = ReadInt(text, "id");
            if (id <= 0 &&
                !int.TryParse(
                    Path.GetFileNameWithoutExtension(path),
                    out id))
                continue;
            var skill = new CurrentSkillDefinition(
                id,
                ReadString(text, "skill_name"));

            _skills[id] = skill;
            AddByName(_skillsByName, skill.Name, skill);
        }
    }

    private static void AddByName<T>(
        Dictionary<string, List<T>> map,
        string name,
        T value)
    {
        var key = NormalizeName(name);
        if (string.IsNullOrWhiteSpace(key))
            return;

        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];

        list.Add(value);
    }

    public static string NormalizeName(string value)
        => Regex.Replace(
                value ?? string.Empty,
                @"[sp{P}p{S}]+",
                string.Empty)
            .Trim()
            .ToLowerInvariant();

    private static int ReadInt(string text, string name)
    {
        var match = Regex.Match(
            text,
            $@"(?m)^s*{Regex.Escape(name)}s*=s*(-?d+)s*$");
        return match.Success && int.TryParse(match.Groups[1].Value, out var v)
            ? v
            : 0;
    }

    private static string ReadString(string text, string name)
    {
        var match = Regex.Match(
            text,
            $@"(?m)^s*{Regex.Escape(name)}s*=s*""((?:\.|[^""])*)""s*$");

        return match.Success
            ? Regex.Unescape(match.Groups[1].Value)
            : string.Empty;
    }
}
