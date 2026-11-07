using SGO.Server.Application.Abstractions;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Quest;

public sealed class QuestProgressService
{
    private readonly IQuestStore _quests;
    private readonly ICharacterAssetReader _assets;
    private readonly QuestTrackingCatalog _catalog;

    public QuestProgressService(
        IQuestStore quests,
        ICharacterAssetReader assets,
        QuestTrackingCatalog catalog)
    {
        _quests = quests;
        _assets = assets;
        _catalog = catalog;
    }

    public IReadOnlyList<object> OnMonsterKilled(
        long accountId,
        long dbid,
        string monsterName)
    {
        if (dbid <= 0 || string.IsNullOrWhiteSpace(monsterName))
            return [];

        var packets = new List<object>();

        foreach (var quest in ActiveQuests(accountId, dbid))
        {
            var questId = Convert.ToInt32(quest["QuestID"]);
            var stepIndex = Convert.ToInt32(quest["CurrentStep"]);
            var step = _catalog.FindStep(questId, stepIndex);

            if (step is null ||
                step.TargetType != 1 ||
                !string.Equals(
                    step.TargetId,
                    monsterName.Trim(),
                    StringComparison.Ordinal))
            {
                continue;
            }

            var targetCount = Math.Max(1, step.TargetCount);
            var progress = Math.Min(
                targetCount,
                Convert.ToInt32(quest["StepProgress"]) + 1);

            if (progress >= targetCount)
            {
                packets.AddRange(
                    CompleteCurrentStep(
                        accountId,
                        dbid,
                        questId,
                        stepIndex,
                        progress));
            }
            else if (_quests.UpsertQuest(
                         accountId,
                         dbid,
                         questId,
                         1,
                         stepIndex,
                         progress))
            {
                packets.Add(BuildUpdate(
                    questId,
                    1,
                    stepIndex,
                    progress));
            }
        }

        return packets;
    }

    public IReadOnlyList<object> RefreshCollectedItems(
        long accountId,
        long dbid,
        int? changedItemId = null)
    {
        if (dbid <= 0)
            return [];

        var packets = new List<object>();
        var inventory = _assets.GetCharacterAssets(
                accountId,
                dbid)
            .Inventory;

        var itemCounts = inventory
            .Where(x =>
                Convert.ToInt32(x["InBank"]) == 0 &&
                Convert.ToInt32(x["InShop"]) == 0)
            .GroupBy(x => Convert.ToInt32(x["ItemIndex"]))
            .ToDictionary(
                x => x.Key,
                x => x.Sum(v =>
                    Math.Max(
                        0,
                        Convert.ToInt32(v["Count"]))));

        foreach (var quest in ActiveQuests(accountId, dbid))
        {
            var questId = Convert.ToInt32(quest["QuestID"]);
            var stepIndex = Convert.ToInt32(quest["CurrentStep"]);

            for (var guard = 0; guard < 32; guard++)
            {
                var step = _catalog.FindStep(questId, stepIndex);
                if (step is null || step.TargetType != 2)
                    break;

                if (!int.TryParse(step.TargetId, out var itemId) ||
                    itemId <= 0)
                    break;

                if (changedItemId.HasValue &&
                    changedItemId.Value != itemId &&
                    guard == 0)
                    break;

                var targetCount = Math.Max(1, step.TargetCount);
                var progress = Math.Min(
                    targetCount,
                    itemCounts.GetValueOrDefault(itemId));

                if (progress < targetCount)
                {
                    if (_quests.UpsertQuest(
                            accountId,
                            dbid,
                            questId,
                            1,
                            stepIndex,
                            progress))
                    {
                        packets.Add(BuildUpdate(
                            questId,
                            1,
                            stepIndex,
                            progress));
                    }

                    break;
                }

                var definition = _catalog.Find(questId);
                if (definition is null)
                    break;

                var nextStep = definition.Steps
                    .FirstOrDefault(x =>
                        x.StepIndex > stepIndex);

                if (nextStep is null)
                {
                    if (_quests.UpsertQuest(
                            accountId,
                            dbid,
                            questId,
                            2,
                            stepIndex,
                            progress))
                    {
                        packets.Add(BuildUpdate(
                            questId,
                            2,
                            stepIndex,
                            progress));
                    }

                    break;
                }

                stepIndex = nextStep.StepIndex;

                if (!_quests.UpsertQuest(
                        accountId,
                        dbid,
                        questId,
                        1,
                        stepIndex,
                        0))
                    break;

                packets.Add(BuildUpdate(
                    questId,
                    1,
                    stepIndex,
                    0));

                if (nextStep.TargetType != 2)
                    break;
            }
        }

        return packets;
    }

    private IReadOnlyList<object> CompleteCurrentStep(
        long accountId,
        long dbid,
        int questId,
        int stepIndex,
        int completedProgress)
    {
        var definition = _catalog.Find(questId);
        if (definition is null)
            return [];

        var nextStep = definition.Steps
            .FirstOrDefault(x => x.StepIndex > stepIndex);

        if (nextStep is null)
        {
            if (!_quests.UpsertQuest(
                    accountId,
                    dbid,
                    questId,
                    2,
                    stepIndex,
                    completedProgress))
                return [];

            return [BuildUpdate(
                questId,
                2,
                stepIndex,
                completedProgress)];
        }

        if (!_quests.UpsertQuest(
                accountId,
                dbid,
                questId,
                1,
                nextStep.StepIndex,
                0))
            return [];

        return [BuildUpdate(
            questId,
            1,
            nextStep.StepIndex,
            0)];
    }

    private IEnumerable<Dictionary<string, object>> ActiveQuests(
        long accountId,
        long dbid)
        => _quests.GetQuests(accountId, dbid)
            .Where(x =>
                Convert.ToInt32(x["Status"]) == 1);

    private static object BuildUpdate(
        int questId,
        int status,
        int currentStep,
        int stepProgress)
        => new
        {
            Type = "QuestUpdate",
            QuestID = questId,
            Status = status,
            CurrentStep = currentStep,
            StepProgress = stepProgress
        };
}
