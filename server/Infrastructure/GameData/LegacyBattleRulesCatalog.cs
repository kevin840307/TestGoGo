namespace SGO.Server.Infrastructure.GameData;

public sealed class LegacyBattleRulesCatalog
{
    private readonly List<(int LevelDiff, int Ratio)> _expRatios = [];
    private int _expMultiplier = 2;

    public LegacyBattleRulesCatalog(string dataRoot)
    {
        Load(ResolveConfigPath(dataRoot, "battle.cfg"));
    }

    public int ExpMultiplier => _expMultiplier;

    public int ExperienceRatio(
        int playerLevel,
        int monsterLevel)
    {
        if (_expRatios.Count == 0)
            return 100;

        var diff = Math.Max(1, playerLevel) -
                   Math.Max(1, monsterLevel);

        var first = _expRatios[0];
        var last = _expRatios[^1];

        if (diff <= first.LevelDiff)
            return first.Ratio;

        if (diff > last.LevelDiff)
            return 0;

        var exact = _expRatios.FirstOrDefault(x =>
            x.LevelDiff == diff);

        return exact == default
            ? 0
            : Math.Clamp(exact.Ratio, 0, 100);
    }

    public int ExperienceForKill(
        int playerLevel,
        int monsterLevel)
    {
        var ratio = ExperienceRatio(
            playerLevel,
            monsterLevel);

        if (ratio <= 0)
            return 0;

        var baseExp =
            Math.Max(1, monsterLevel) *
            Math.Max(1, _expMultiplier);

        return Math.Max(
            1,
            baseExp * ratio / 100);
    }

    private void Load(string path)
    {
        if (!File.Exists(path))
            return;

        var currentSection = "";
        int? pendingDiff = null;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Split(
                    "//",
                    2,
                    StringSplitOptions.None)[0]
                .Trim();

            if (line.Length == 0)
                continue;

            if (line.StartsWith('[') &&
                line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                pendingDiff = null;
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            var key = line[..equals].Trim();
            var rawValue = line[(equals + 1)..].Trim();

            if (!int.TryParse(rawValue, out var value))
                continue;

            if (currentSection.Equals(
                    "Exp",
                    StringComparison.OrdinalIgnoreCase) &&
                key.Equals(
                    "ExpX",
                    StringComparison.OrdinalIgnoreCase))
            {
                _expMultiplier = Math.Max(1, value);
                continue;
            }

            if (!currentSection.StartsWith(
                    "ExpRatio",
                    StringComparison.OrdinalIgnoreCase) ||
                currentSection.Equals(
                    "ExpRatio",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            if (key.Equals(
                    "LvDiff",
                    StringComparison.OrdinalIgnoreCase))
            {
                pendingDiff = value;
                continue;
            }

            if (key.Equals(
                    "Ratio",
                    StringComparison.OrdinalIgnoreCase) &&
                pendingDiff.HasValue)
            {
                _expRatios.Add((
                    pendingDiff.Value,
                    Math.Clamp(value, 0, 100)));
                pendingDiff = null;
            }
        }

        _expRatios.Sort((a, b) =>
            a.LevelDiff.CompareTo(b.LevelDiff));
    }

    private static string ResolveConfigPath(
        string dataRoot,
        string file)
    {
        var copied = Path.Combine(dataRoot, file);
        if (File.Exists(copied))
            return copied;

        return Path.Combine(
            Directory.GetParent(dataRoot)?.FullName ?? dataRoot,
            file);
    }
}
