using System.Collections.Concurrent;
using System.Threading;

namespace SGO.Server.Infrastructure.GameData;

public sealed record LegacyPlayerGrowthStats(
    int MaxHP,
    int MaxMP,
    int MaxSP,
    int Attack,
    int Defense,
    int MagicAttack,
    int MagicDefense,
    int Hit,
    int Dodge,
    int MoveSpeed);

public sealed class LegacyPlayerGrowthCatalog
{
    private readonly string _root;
    // Multiple websocket sessions can issue /gm level or compute growth
    // concurrently. A normal Dictionary corrupted itself when the first
    // concurrent requests lazily loaded CSVs on the same catalog instance.
    // Lazy<> also ensures each file's parsing is performed only once.
    private readonly ConcurrentDictionary<string, Lazy<int[][]>> _tables =
        new(StringComparer.OrdinalIgnoreCase);

    public LegacyPlayerGrowthCatalog(string gameDataRoot)
    {
        _root = Path.Combine(gameDataRoot, "Player");
    }

    public int AbilityPointsPerLevel(int level)
        => ReadSingle("ExpUse.csv", level, fallback: 2);

    // Player_Up_Lv.csv stores the legacy accumulated EXP threshold for
    // advancing from the supplied level to the next one.
    public int ExperienceThresholdForLevel(int level)
    {
        level = Math.Clamp(level, 1, 255);
        if (level >= 255)
            return int.MaxValue;

        return ReadSingle(
            "Player_Up_Lv.csv",
            level,
            fallback: FallbackExperienceThreshold(level));
    }

    public int ResolveLevel(
        int currentLevel,
        int accumulatedExperience)
    {
        var level = Math.Clamp(currentLevel, 1, 255);
        var exp = Math.Max(0, accumulatedExperience);

        while (level < 255 &&
               exp >= ExperienceThresholdForLevel(level))
        {
            level++;
        }

        return level;
    }

    public int AbilityPointsGained(
        int fromLevel,
        int toLevel)
    {
        var from = Math.Clamp(fromLevel, 1, 255);
        var to = Math.Clamp(toLevel, from, 255);
        var points = 0;

        for (var level = from; level < to; level++)
            points += Math.Max(0, AbilityPointsPerLevel(level));

        return points;
    }

    public LegacyPlayerGrowthStats Calculate(
        Dictionary<string, object> character)
    {
        var level = Math.Max(1, Convert.ToInt32(character["Level"]));
        var race = Convert.ToInt32(character["Race"]);
        var career = Convert.ToInt32(character["Career"]);
        var str = Math.Clamp(Convert.ToInt32(character["Str"]), 0, 200);
        var dex = Math.Clamp(Convert.ToInt32(character["Dex"]), 0, 200);
        var con = Math.Clamp(Convert.ToInt32(character["Con"]), 0, 200);
        var wis = Math.Clamp(Convert.ToInt32(character["Wis"]), 0, 200);

        var baseRow = ReadRow(ResolveAttBase(career, race), level);
        var conHp = ReadSingle($"CON_{ResolveRaceName(race)}.csv", con);
        var strAttack = ReadSingle($"STR_{ResolveRaceName(race)}.csv", str);
        var dexRow = ReadRow($"DEX_{ResolveRaceName(race)}.csv", dex);
        var wisRow = ReadRow($"WIS_{ResolveRaceName(race)}.csv", wis);

        // Old data layout:
        // AttBase = HP,MP,ATK,DEF,MATK,MDEF,HIT,DODGE,SP.
        // CON table supplies HP; STR supplies physical attack;
        // DEX supplies dodge + move speed; WIS supplies MP + magic attack.
        // Original gameplay rule additionally grants DEF from CON itself.
        return new LegacyPlayerGrowthStats(
            MaxHP: Get(baseRow, 0, 100) + conHp,
            MaxMP: Get(baseRow, 1, 50) + Get(wisRow, 0),
            MaxSP: Get(baseRow, 8, 100),
            Attack: Get(baseRow, 2, 10) + strAttack,
            Defense: Get(baseRow, 3, 5) + con,
            MagicAttack: Get(baseRow, 4, 5) + Get(wisRow, 1),
            MagicDefense: Get(baseRow, 5, 5),
            Hit: Get(baseRow, 6, 100),
            Dodge: Get(baseRow, 7) + Get(dexRow, 0),
            MoveSpeed: 100 + Get(dexRow, 1));
    }

    private string ResolveAttBase(int career, int race)
    {
        if (career <= 0)
            return "AttBase_Newbie.csv";

        if (career == 9)
            return "AttBase_ItemMaster.csv";
        if (career == 10)
            return "AttBase_PetMaster.csv";

        var suffix = race switch
        {
            1 => "G",
            2 => "M",
            3 => "R",
            _ => "H"
        };

        return career is >= 5 and <= 8
            ? $"AttBase_Mage{suffix}.csv"
            : $"AttBase_War{suffix}.csv";
    }

    private static string ResolveRaceName(int race)
        => race switch
        {
            1 => "Girl",
            2 => "Migu",
            3 => "Robot",
            _ => "Boy"
        };

    private int ReadSingle(string file, int oneBasedIndex, int fallback = 0)
        => Get(ReadRow(file, oneBasedIndex), 0, fallback);

    private int[] ReadRow(string file, int oneBasedIndex)
    {
        if (oneBasedIndex <= 0)
            return [];

        if (!_tables.TryGetValue(file, out var cached))
        {
            // Preserve the old behavior for absent files: do not cache an
            // empty lookup; content may be installed after server startup.
            if (!File.Exists(Path.Combine(_root, file)))
                return [];

            cached = _tables.GetOrAdd(file, key =>
                new Lazy<int[][]>(
                    () => LoadRows(key),
                    LazyThreadSafetyMode.ExecutionAndPublication));
        }

        var rows = cached.Value;
        if (rows.Length == 0)
            return [];

        return rows[Math.Clamp(oneBasedIndex - 1, 0, rows.Length - 1)];
    }

    private int[][] LoadRows(string file)
        => File.ReadLines(Path.Combine(_root, file))
            .Select(x => x.Trim())
            .TakeWhile(x => !x.Equals("[end]", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Length > 0)
            .Select(x => x.Split(',')
                .Select(v => int.TryParse(v.Trim(), out var n) ? n : 0)
                .ToArray())
            .ToArray();
    }

    private static int FallbackExperienceThreshold(int level)
    {
        var n = (long)Math.Max(1, level);
        var value = 2L * n * n * n * n;
        return (int)Math.Min(200_000_000L, value);
    }

    private static int Get(int[] row, int index, int fallback = 0)
        => index >= 0 && index < row.Length ? row[index] : fallback;
}
