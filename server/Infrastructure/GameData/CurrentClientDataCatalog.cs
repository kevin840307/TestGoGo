using System.Text.Json;
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

public sealed record CurrentPetEvolution(
    int EvolId,
    string Outlook,
    int Color,
    int AttackRange);

public sealed record CurrentPetDefinition(
    int PetId,
    string Name,
    IReadOnlyDictionary<int, CurrentPetEvolution> Evolutions);

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
    private readonly Dictionary<int, CurrentPetDefinition> _pets = new();

    public string? Root { get; }
    public bool IsAvailable => Root is not null;

    public CurrentClientDataCatalog(string? configuredRoot)
    {
        Root = ResolveRoot(configuredRoot);
        if (Root is null)
            return;

        LoadItems(Root);
        LoadSkills(Root);
        LoadPets(Root);
    }

    public CurrentClientDataCatalog(
        IEnumerable<CurrentItemDefinition> items,
        IEnumerable<CurrentSkillDefinition> skills,
        IEnumerable<CurrentPetDefinition>? pets = null)
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

        if (pets is not null)
        {
            foreach (var pet in pets)
                _pets[pet.PetId] = pet;
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

    public CurrentPetDefinition? FindPet(int petId)
        => _pets.TryGetValue(petId, out var pet) ? pet : null;

    public CurrentPetEvolution? FindPetEvolution(int petId, int evolId)
        => FindPet(petId)?.Evolutions.TryGetValue(evolId, out var evolution) == true
            ? evolution
            : null;

    public IReadOnlyCollection<CurrentItemDefinition> Items => _items.Values;
    public IReadOnlyCollection<CurrentSkillDefinition> Skills => _skills.Values;
    public IReadOnlyCollection<CurrentPetDefinition> Pets => _pets.Values;

    private static string? ResolveRoot(string? configuredRoot)
    {
        var candidates = new[]
        {
            configuredRoot,
            Environment.GetEnvironmentVariable("SGO_CLIENT_ROOT"),
            Path.Combine(Directory.GetCurrentDirectory(), "client"),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "client")),
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

    private void LoadPets(string root)
    {
        var path = Path.Combine(
            root,
            "Assets",
            "Pets",
            "Config.json");

        if (!File.Exists(path))
            return;

        using var doc = JsonDocument.Parse(
            File.ReadAllText(path));

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(property.Name, out var petId))
                continue;

            var value = property.Value;
            var name = value.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? $"Pet {petId}"
                : $"Pet {petId}";

            var evolutions =
                new Dictionary<int, CurrentPetEvolution>();

            if (value.TryGetProperty("evolutions", out var evolElement) &&
                evolElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var evolProperty in evolElement.EnumerateObject())
                {
                    if (!int.TryParse(evolProperty.Name, out var evolId))
                        continue;

                    var evol = evolProperty.Value;
                    evolutions[evolId] = new CurrentPetEvolution(
                        evolId,
                        evol.TryGetProperty("outlook", out var outlook)
                            ? outlook.GetString() ?? "EN010A"
                            : "EN010A",
                        evol.TryGetProperty("color", out var color)
                            ? color.GetInt32()
                            : 0,
                        evol.TryGetProperty("attack_range", out var range)
                            ? range.GetInt32()
                            : 80);
                }
            }

            _pets[petId] = new CurrentPetDefinition(
                petId,
                name,
                evolutions);
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
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(
                value
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToLowerInvariant)
                    .ToArray())
            .Trim();
    }

    private static int ReadInt(string text, string name)
    {
        foreach (var raw in text.Split(
                     new[] { "\r\n", "\n" },
                     StringSplitOptions.None))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq < 0)
                continue;

            var left = line[..eq].Trim();
            if (!left.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var value = line[(eq + 1)..].Trim();
            if (int.TryParse(value, out var parsed))
                return parsed;
        }

        return 0;
    }

    private static string ReadString(string text, string name)
    {
        foreach (var raw in text.Split(
                     new[] { "\r\n", "\n" },
                     StringSplitOptions.None))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq < 0)
                continue;

            var left = line[..eq].Trim();
            if (!left.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 &&
                value[0] == '"' &&
                value[^1] == '"')
            {
                value = value[1..^1];
            }

            return value
                .Replace("\\\"", "\"")
                .Replace("\\n", "\n")
                .Replace("\\\\", "\\");
        }

        return string.Empty;
    }

}
