using System.Text;

namespace SGO.Server.Infrastructure.GameData;

public sealed record SkillDefinition(
    int SkillId,
    string Name,
    string Description);

public sealed class SkillCatalog
{
    private readonly Dictionary<int, SkillDefinition> _skills = new();

    public SkillCatalog(string path)
    {
        if (!File.Exists(path))
            return;

        foreach (var raw in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var cols = raw.Split(',');
            if (cols.Length < 2 ||
                !int.TryParse(cols[0].Trim(), out var skillId))
                continue;

            _skills.TryAdd(
                skillId,
                new SkillDefinition(
                    skillId,
                    cols[1].Trim(),
                    cols.Length > 2 ? cols[2].Trim() : string.Empty));
        }
    }

    public SkillDefinition? Find(int skillId)
        => _skills.TryGetValue(skillId, out var value)
            ? value
            : null;

    public IReadOnlyCollection<SkillDefinition> All
        => _skills.Values;
}
