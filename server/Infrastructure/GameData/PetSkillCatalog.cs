using System.Text.Json;

namespace SGO.Server.Infrastructure.GameData;

public sealed class PetSkillCatalog
{
    private readonly Dictionary<(int PetId, int EvolId), HashSet<int>>
        _skills = new();

    public PetSkillCatalog(string path)
    {
        if (!File.Exists(path))
            return;

        using var document = JsonDocument.Parse(
            File.ReadAllText(path));

        foreach (var pet in document.RootElement.EnumerateObject())
        {
            if (!int.TryParse(pet.Name, out var petId) ||
                !pet.Value.TryGetProperty(
                    "evolutions",
                    out var evolutions))
                continue;

            foreach (var evolution in evolutions.EnumerateObject())
            {
                if (!int.TryParse(evolution.Name, out var evolId))
                    continue;

                var set = new HashSet<int>();
                if (evolution.Value.TryGetProperty(
                        "skills",
                        out var skills) &&
                    skills.ValueKind == JsonValueKind.Array)
                {
                    foreach (var skill in skills.EnumerateArray())
                    {
                        if (skill.TryGetProperty(
                                "skill_id",
                                out var rawId) &&
                            rawId.TryGetInt32(out var skillId) &&
                            skillId > 0 &&
                            skillId != 9999)
                        {
                            set.Add(skillId);
                        }
                    }
                }

                _skills[(petId, evolId)] = set;
            }
        }
    }

    public bool CanUseAsMainSkill(
        int petId,
        int evolId,
        int skillId)
    {
        if (skillId == 0)
            return true;

        return _skills.TryGetValue(
                   (petId, evolId),
                   out var skills) &&
               skills.Contains(skillId);
    }
}
