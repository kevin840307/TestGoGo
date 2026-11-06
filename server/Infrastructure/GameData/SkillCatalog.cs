namespace SGO.Server.Infrastructure.GameData;

public sealed record SkillDefinition(
    int SkillId,
    string Name,
    string Description);

public sealed class SkillCatalog
{
    private readonly Dictionary<int, SkillDefinition> _skills = new();

    public SkillCatalog(CurrentClientDataCatalog current)
    {
        foreach (var skill in current.Skills)
        {
            _skills[skill.SkillId] = new SkillDefinition(
                skill.SkillId,
                string.IsNullOrWhiteSpace(skill.Name)
                    ? $"Skill {skill.SkillId}"
                    : skill.Name,
                string.Empty);
        }
    }

    public SkillDefinition? Find(int skillId)
        => _skills.TryGetValue(skillId, out var value)
            ? value
            : null;

    public IReadOnlyCollection<SkillDefinition> All
        => _skills.Values;
}
