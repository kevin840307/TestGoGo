using System.Text.Json;

namespace SGO.Server.Infrastructure.GameData;

public sealed record QuestTrackingStep(
    int StepIndex,
    int TargetType,
    string TargetId,
    int TargetCount);

public sealed record QuestTrackingDefinition(
    int QuestId,
    IReadOnlyList<QuestTrackingStep> Steps);

public sealed class QuestTrackingCatalog
{
    private readonly Dictionary<int, QuestTrackingDefinition> _quests = new();

    public QuestTrackingCatalog(string path)
    {
        if (!File.Exists(path))
            return;

        using var document = JsonDocument.Parse(
            File.ReadAllText(path));

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!int.TryParse(property.Name, out var questId))
                continue;

            if (property.Value.TryGetProperty(
                    "QuestID",
                    out var questIdElement) &&
                questIdElement.TryGetInt32(out var configuredId) &&
                configuredId > 0)
            {
                questId = configuredId;
            }

            var steps = new List<QuestTrackingStep>();

            if (property.Value.TryGetProperty(
                    "Steps",
                    out var rawSteps) &&
                rawSteps.ValueKind == JsonValueKind.Array)
            {
                foreach (var rawStep in rawSteps.EnumerateArray())
                {
                    var stepIndex = rawStep.TryGetProperty(
                            "StepIndex",
                            out var stepIndexElement) &&
                        stepIndexElement.TryGetInt32(out var parsedStepIndex)
                            ? parsedStepIndex
                            : steps.Count;

                    var targetType = rawStep.TryGetProperty(
                            "TargetType",
                            out var targetTypeElement) &&
                        targetTypeElement.TryGetInt32(out var parsedTargetType)
                            ? parsedTargetType
                            : 99;

                    var targetId = rawStep.TryGetProperty(
                            "TargetID",
                            out var targetIdElement)
                        ? targetIdElement.ToString().Trim()
                        : "";

                    var targetCount = rawStep.TryGetProperty(
                            "TargetCount",
                            out var targetCountElement) &&
                        targetCountElement.TryGetInt32(out var parsedTargetCount)
                            ? Math.Max(0, parsedTargetCount)
                            : 0;

                    steps.Add(new QuestTrackingStep(
                        stepIndex,
                        targetType,
                        targetId,
                        targetCount));
                }
            }

            steps.Sort((a, b) =>
                a.StepIndex.CompareTo(b.StepIndex));

            _quests[questId] = new QuestTrackingDefinition(
                questId,
                steps);
        }
    }

    public QuestTrackingDefinition? Find(int questId)
        => _quests.TryGetValue(questId, out var quest)
            ? quest
            : null;

    public QuestTrackingStep? FindStep(
        int questId,
        int stepIndex)
        => Find(questId)?.Steps
            .FirstOrDefault(x => x.StepIndex == stepIndex);

    public int Count => _quests.Count;
}
