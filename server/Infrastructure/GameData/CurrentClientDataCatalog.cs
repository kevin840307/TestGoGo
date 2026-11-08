using System.Text;
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
    string HorseType,
    int ReqLevel = 0,
    int Durability = 0,
    int CanRepair = 1,
    int CanTrade = 1,
    string ScriptName = "",
    string Description = "",
    int ColorIndex = 0,
    string WeaponStyle = "");

public sealed record CurrentSkillBookDefinition(
    int ItemId,
    int SkillId,
    int SkillLevel,
    string SkillName);

public sealed record CurrentExperienceCardDefinition(
    int ItemId,
    string Name,
    int MultiplierPercent,
    int DurationSeconds);

public sealed record CurrentSkillDefinition(
    int SkillId,
    string Name,
    string SpecialSkill = "");

public sealed record CurrentSkillLevelDefinition(
    int SkillId,
    int Level,
    int SkillType,
    int DamageType,
    int CastRange,
    int AoeRadius,
    int CostHp,
    int CostMp,
    int CostSp,
    int BaseDamage,
    int DamageMultiplier,
    int CooldownSeconds,
    int DurationSeconds,
    int TickCount,
    int MaxHpBonus,
    int MaxMpBonus,
    int MaxSpBonus,
    int AtkBonus,
    int DefBonus,
    int MAtkBonus,
    int MDefBonus,
    int HitBonus,
    int FleeBonus,
    int LukBonus,
    int AspdBonus,
    int MoveSpeedBonus,
    int CritRateBonus,
    int CritDamageBonus)
{
    public int MaxHpBonusPercent { get; init; }
    public int MaxMpBonusPercent { get; init; }
    public int MaxSpBonusPercent { get; init; }
    public int AtkBonusPercent { get; init; }
    public int DefBonusPercent { get; init; }
    public int MAtkBonusPercent { get; init; }
    public int MDefBonusPercent { get; init; }
    public int HitBonusPercent { get; init; }
    public int FleeBonusPercent { get; init; }
    public int LukBonusPercent { get; init; }
    public int AspdBonusPercent { get; init; }
    public int MoveSpeedBonusPercent { get; init; }
    public int CritRateBonusPercent { get; init; }
    public int CritDamageBonusPercent { get; init; }
    public int DeathExpReductionPercent { get; init; }
}

public sealed record CurrentPetEvolution(
    int EvolId,
    string Outlook,
    int Color,
    int AttackRange,
    int MinLevel = 0,
    int RequiredCon = 0,
    int RequiredStr = 0,
    int RequiredDex = 0,
    int RequiredWis = 0,
    IReadOnlyList<int>? NextEvolIds = null);

public sealed record CurrentPetDefinition(
    int PetId,
    string Name,
    IReadOnlyDictionary<int, CurrentPetEvolution> Evolutions);

public sealed record CurrentHorsePreset(
    int HorseId,
    int BodyId,
    int WeaponId,
    int EngineId);

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
    private readonly Dictionary<(int SkillId, int Level), CurrentSkillLevelDefinition>
        _skillLevels = new();
    private readonly Dictionary<string, List<CurrentSkillDefinition>> _skillsByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, CurrentPetDefinition> _pets = new();
    private readonly Dictionary<int, CurrentHorsePreset> _horsePresets = new();

    public string? Root { get; }
    public bool IsAvailable => Root is not null;

    public CurrentClientDataCatalog(string? configuredRoot)
    {
        Root = ResolveRoot(configuredRoot);
        if (Root is null)
            return;

        LoadItems(Root);
        LoadHorsePresets(Root);
        LoadSkills(Root);
        LoadSkillPercentageBonuses(Root);
        LoadPets(Root);
    }

    public CurrentClientDataCatalog(
        IEnumerable<CurrentItemDefinition> items,
        IEnumerable<CurrentSkillDefinition> skills,
        IEnumerable<CurrentPetDefinition>? pets = null,
        IEnumerable<CurrentSkillLevelDefinition>? skillLevels = null,
        IEnumerable<CurrentHorsePreset>? horsePresets = null)
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

        if (skillLevels is not null)
        {
            foreach (var skillLevel in skillLevels)
                _skillLevels[(skillLevel.SkillId, skillLevel.Level)] = skillLevel;
        }

        if (horsePresets is not null)
        {
            foreach (var preset in horsePresets)
                _horsePresets[preset.HorseId] = preset;
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

    public CurrentSkillLevelDefinition? FindSkillLevel(
        int skillId,
        int level)
    {
        if (_skillLevels.TryGetValue((skillId, level), out var exact))
            return exact;

        return _skillLevels
            .Where(x => x.Key.SkillId == skillId &&
                        x.Key.Level <= level)
            .OrderByDescending(x => x.Key.Level)
            .Select(x => x.Value)
            .FirstOrDefault();
    }

    public CurrentSkillDefinition? FindSkillByName(string name)
    {
        var key = NormalizeName(name);
        if (string.IsNullOrWhiteSpace(key) ||
            !_skillsByName.TryGetValue(key, out var matches))
            return null;

        return matches.Count == 1 ? matches[0] : null;
    }

    public CurrentSkillBookDefinition? ResolveSkillBook(
        int itemId)
    {
        var item = FindItem(itemId);
        if (item is null ||
            !item.Category.Equals(
                "general",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(item.ScriptName))
            return null;

        var (skillName, level) =
            ParseSkillBookName(item.ScriptName);

        var skill = FindSkillByName(skillName);
        if (skill is null ||
            FindSkillLevel(skill.SkillId, level) is null)
            return null;

        return new CurrentSkillBookDefinition(
            item.ItemId,
            skill.SkillId,
            level,
            skill.Name);
    }

    public CurrentExperienceCardDefinition? ResolveExperienceCard(
        int itemId)
    {
        var item = FindItem(itemId);
        if (item is null ||
            !item.Category.Equals(
                "general",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(item.ScriptName) ||
            !item.ScriptName.StartsWith(
                "雙倍經驗值卡(",
                StringComparison.Ordinal) ||
            !item.ScriptName.EndsWith(
                "小時)",
                StringComparison.Ordinal))
        {
            return null;
        }

        const string prefix = "雙倍經驗值卡(";
        const string suffix = "小時)";
        var hoursText = item.ScriptName[
            prefix.Length..
            ^suffix.Length];

        if (!int.TryParse(
                hoursText,
                out var hours) ||
            hours <= 0 ||
            hours > 24)
        {
            return null;
        }

        return new CurrentExperienceCardDefinition(
            item.ItemId,
            item.Name,
            200,
            checked(hours * 60 * 60));
    }

    public CurrentPetDefinition? FindPet(int petId)
        => _pets.TryGetValue(petId, out var pet) ? pet : null;

    public CurrentHorsePreset? FindHorsePreset(int horseId)
        => _horsePresets.TryGetValue(horseId, out var preset)
            ? preset
            : null;

    public CurrentPetEvolution? FindPetEvolution(int petId, int evolId)
        => FindPet(petId)?.Evolutions.TryGetValue(evolId, out var evolution) == true
            ? evolution
            : null;

    public CurrentPetEvolution? ResolveNextPetEvolution(
        int petId,
        int currentEvolId,
        int level,
        int con,
        int str,
        int dex,
        int wis)
    {
        var pet = FindPet(petId);
        if (pet is null ||
            !pet.Evolutions.TryGetValue(
                currentEvolId,
                out var current) ||
            current.NextEvolIds is null ||
            current.NextEvolIds.Count == 0)
        {
            return null;
        }

        return current.NextEvolIds
            .Select(id =>
                pet.Evolutions.TryGetValue(id, out var candidate)
                    ? candidate
                    : null)
            .Where(x =>
                x is not null &&
                level >= x.MinLevel &&
                con >= x.RequiredCon &&
                str >= x.RequiredStr &&
                dex >= x.RequiredDex &&
                wis >= x.RequiredWis)
            .OrderByDescending(x => x!.MinLevel)
            .ThenByDescending(x =>
                x!.RequiredCon +
                x.RequiredStr +
                x.RequiredDex +
                x.RequiredWis)
            .ThenBy(x => x!.EvolId)
            .FirstOrDefault();
    }

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
            @"C:\SGO\Recovered",
            @"C:\SGO\重置版本\Recovered"
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
                var isBooster =
                    folder.Equals(
                        "booster",
                        StringComparison.OrdinalIgnoreCase);

                var item = new CurrentItemDefinition(
                    id,
                    ReadString(text, "item_name"),
                    folder,
                    ReadInt(text, "price"),
                    ReadInt(text, "can_sell"),
                    ReadInt(text, isBooster ? "atk_bonus" : "atk"),
                    ReadInt(text, isBooster ? "def_bonus" : "def"),
                    ReadInt(text, isBooster ? "matk_bonus" : "matk"),
                    ReadInt(text, isBooster ? "mdef_bonus" : "mdef"),
                    ReadInt(text, isBooster ? "hit_bonus" : "hit"),
                    ReadInt(text, isBooster ? "flee_bonus" : "flee"),
                    ReadInt(text, isBooster ? "luk_bonus" : "luk"),
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
                    ReadString(text, "horse_type"),
                    ReadInt(text, "req_level"),
                    ReadInt(text, "durability"),
                    ReadInt(text, "can_repair", 1),
                    ReadInt(text, "can_trade", 1),
                    ReadString(text, "script_name"),
                    ReadString(text, "description"),
                    ReadInt(text, "color_index", 0),
                    ReadString(text, "note_4"));

                _items[id] = item;
                AddByName(_itemsByName, item.Name, item);
            }
        }
    }

    private void LoadHorsePresets(string root)
    {
        var dir = Path.Combine(root, "Items", "horse");
        if (!Directory.Exists(dir))
            return;

        foreach (var path in Directory.EnumerateFiles(
                     dir,
                     "*.tres",
                     SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(path);
            var id = ReadInt(text, "id");
            if (id <= 0)
                continue;

            _horsePresets[id] = new CurrentHorsePreset(
                id,
                ReadInt(text, "body_id"),
                ReadInt(text, "weapon_id"),
                ReadInt(text, "engine_id"));
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
                    var minLevel = 0;
                    var requiredCon = 0;
                    var requiredStr = 0;
                    var requiredDex = 0;
                    var requiredWis = 0;

                    if (evol.TryGetProperty(
                            "requirements",
                            out var requirements) &&
                        requirements.ValueKind ==
                            JsonValueKind.Object)
                    {
                        minLevel = requirements.TryGetProperty(
                                "level",
                                out var levelElement)
                            ? Math.Max(0, levelElement.GetInt32())
                            : 0;

                        if (requirements.TryGetProperty(
                                "stats",
                                out var statsElement) &&
                            statsElement.ValueKind ==
                                JsonValueKind.Object)
                        {
                            requiredCon = statsElement.TryGetProperty(
                                    "con",
                                    out var conElement)
                                ? Math.Max(0, conElement.GetInt32())
                                : 0;
                            requiredStr = statsElement.TryGetProperty(
                                    "str",
                                    out var strElement)
                                ? Math.Max(0, strElement.GetInt32())
                                : 0;
                            requiredDex = statsElement.TryGetProperty(
                                    "dex",
                                    out var dexElement)
                                ? Math.Max(0, dexElement.GetInt32())
                                : 0;
                            requiredWis = statsElement.TryGetProperty(
                                    "wis",
                                    out var wisElement)
                                ? Math.Max(0, wisElement.GetInt32())
                                : 0;
                        }
                    }

                    var nextEvolIds = new List<int>();
                    if (evol.TryGetProperty(
                            "next_evol_ids",
                            out var nextElement) &&
                        nextElement.ValueKind ==
                            JsonValueKind.Array)
                    {
                        foreach (var next in
                                 nextElement.EnumerateArray())
                        {
                            if (next.TryGetInt32(
                                    out var nextId) &&
                                nextId >= 0)
                            {
                                nextEvolIds.Add(nextId);
                            }
                        }
                    }

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
                            : 80,
                        minLevel,
                        requiredCon,
                        requiredStr,
                        requiredDex,
                        requiredWis,
                        nextEvolIds);
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
                ReadString(text, "skill_name"),
                ReadString(text, "special_skill"));

            _skills[id] = skill;
            AddByName(_skillsByName, skill.Name, skill);

            var skillType = ReadInt(text, "skill_type");
            var damageType = ReadInt(text, "damage_type");
            var blocks = Regex.Matches(
                text,
                @"\[sub_resource[^\]]*\]\s*(.*?)(?=\r?\n\[sub_resource|\r?\n\[resource\])",
                RegexOptions.Singleline);

            foreach (Match match in blocks)
            {
                // Every sub_resource in a SkillData .tres is a SkillLevelData
                // entry. Some valid levels only contain tick_count/cooldown or
                // rely entirely on percentage bonuses stored in Skill.csv, so
                // filtering by cast_range/cost_mp/base_damage drops real skills.
                var block = match.Groups[1].Value;

                var level = ReadInt(block, "level");
                if (level <= 0)
                    level = 1;

                _skillLevels[(id, level)] = new CurrentSkillLevelDefinition(
                    id,
                    level,
                    skillType,
                    damageType,
                    ReadInt(block, "cast_range"),
                    ReadInt(block, "aoe_radius"),
                    ReadInt(block, "cost_hp"),
                    ReadInt(block, "cost_mp"),
                    ReadInt(block, "cost_sp"),
                    ReadInt(block, "base_damage"),
                    ReadInt(block, "damage_multiplier"),
                    ReadInt(block, "cooldown"),
                    ReadInt(block, "duration"),
                    ReadInt(block, "tick_count"),
                    ReadInt(block, "max_hp_bonus"),
                    ReadInt(block, "max_mp_bonus"),
                    ReadInt(block, "max_sp_bonus"),
                    ReadInt(block, "atk_bonus"),
                    ReadInt(block, "def_bonus"),
                    ReadInt(block, "matk_bonus"),
                    ReadInt(block, "mdef_bonus"),
                    ReadInt(block, "hit_bonus"),
                    ReadInt(block, "flee_bonus"),
                    ReadInt(block, "luk_bonus"),
                    ReadInt(block, "aspd_bonus"),
                    ReadInt(block, "move_speed_bonus"),
                    ReadInt(block, "crit_rate_bonus"),
                    ReadInt(block, "crit_damage_bonus"));
            }
        }
    }

    private void LoadSkillPercentageBonuses(string root)
    {
        var path = Path.Combine(
            root,
            "Assets",
            "GameInfo",
            "Skill.csv");

        if (!File.Exists(path))
            return;

        var lines = File.ReadLines(path).GetEnumerator();
        if (!lines.MoveNext())
            return;

        var header = SplitCsvLine(lines.Current);
        var columns = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < header.Length; i++)
            columns[header[i].Trim()] = i;

        var required = new[]
        {
            "技能編號",
            "加最大HP比例",
            "加最大MP比例",
            "加最大SP比例",
            "加攻擊比例",
            "加防禦比例",
            "加魔攻比例",
            "加魔防比例",
            "加命中比例",
            "加迴避比例",
            "加幸運比例",
            "加攻速比例",
            "加移速比例",
            "加暴率比例",
            "加暴傷比例",
            "減免死經比例"
        };

        if (required.Any(x => !columns.ContainsKey(x)))
            return;

        var levelsBySkill = _skillLevels.Values
            .GroupBy(x => x.SkillId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(y => y.Level).ToArray());

        var rowIndexBySkill = new Dictionary<int, int>();

        while (lines.MoveNext())
        {
            var raw = lines.Current;
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var row = SplitCsvLine(raw);
            var skillId = CsvInt(
                row,
                columns["技能編號"]);

            if (skillId <= 0 ||
                !levelsBySkill.TryGetValue(
                    skillId,
                    out var levels))
                continue;

            var rowIndex =
                rowIndexBySkill.TryGetValue(
                    skillId,
                    out var currentIndex)
                    ? currentIndex
                    : 0;

            rowIndexBySkill[skillId] =
                rowIndex + 1;

            if (rowIndex >= levels.Length)
                continue;

            var level = levels[rowIndex];
            _skillLevels[(skillId, level.Level)] =
                level with
                {
                    MaxHpBonusPercent =
                        CsvInt(row, columns["加最大HP比例"]),
                    MaxMpBonusPercent =
                        CsvInt(row, columns["加最大MP比例"]),
                    MaxSpBonusPercent =
                        CsvInt(row, columns["加最大SP比例"]),
                    AtkBonusPercent =
                        CsvInt(row, columns["加攻擊比例"]),
                    DefBonusPercent =
                        CsvInt(row, columns["加防禦比例"]),
                    MAtkBonusPercent =
                        CsvInt(row, columns["加魔攻比例"]),
                    MDefBonusPercent =
                        CsvInt(row, columns["加魔防比例"]),
                    HitBonusPercent =
                        CsvInt(row, columns["加命中比例"]),
                    FleeBonusPercent =
                        CsvInt(row, columns["加迴避比例"]),
                    LukBonusPercent =
                        CsvInt(row, columns["加幸運比例"]),
                    AspdBonusPercent =
                        CsvInt(row, columns["加攻速比例"]),
                    MoveSpeedBonusPercent =
                        CsvInt(row, columns["加移速比例"]),
                    CritRateBonusPercent =
                        CsvInt(row, columns["加暴率比例"]),
                    CritDamageBonusPercent =
                        CsvInt(row, columns["加暴傷比例"]),
                    DeathExpReductionPercent =
                        CsvInt(row, columns["減免死經比例"])
                };
        }
    }

    private static int CsvInt(
        string[] row,
        int index)
    {
        if (index < 0 ||
            index >= row.Length)
            return 0;

        return int.TryParse(
            row[index].Trim(),
            out var value)
            ? value
            : 0;
    }

    private static string[] SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (ch == '"')
            {
                if (quoted &&
                    i + 1 < line.Length &&
                    line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                quoted = !quoted;
                continue;
            }

            if (ch == ',' && !quoted)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        values.Add(current.ToString());
        return values.ToArray();
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

    private static (string SkillName, int Level)
        ParseSkillBookName(string scriptName)
    {
        var value = scriptName.Trim();
        var suffixes =
            new (string Suffix, int Level)[]
            {
                ("．一級", 1),
                ("．二級", 2),
                ("．三級", 3),
                ("．四級", 4),
                ("．五級", 5),
                ("·一級", 1),
                ("·二級", 2),
                ("·三級", 3),
                ("·四級", 4),
                ("·五級", 5)
            };

        foreach (var suffix in suffixes)
        {
            if (!value.EndsWith(
                    suffix.Suffix,
                    StringComparison.Ordinal))
                continue;

            return (
                value[..^suffix.Suffix.Length].Trim(),
                suffix.Level);
        }

        return (value, 1);
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

    private static int ReadInt(
        string text,
        string name,
        int defaultValue = 0)
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

        return defaultValue;
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
