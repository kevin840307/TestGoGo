using System.Text.Json;

namespace SGO.Server.Infrastructure.GameData;

public sealed record FestivalRandomReward(
    int ItemId,
    int MinCount,
    int MaxCount);

/// <summary>
/// Reads the recovered client's festival random-reward documentation.
/// Only groups with source_item_id represent inventory boxes that can be
/// opened directly; encounter/NPC reward groups without a source item are
/// intentionally ignored.
/// </summary>
public sealed class FestivalRewardCatalog
{
    private readonly Dictionary<int, FestivalRandomReward[]> _rewards = new();

    public FestivalRewardCatalog(string? clientRoot)
    {
        if (string.IsNullOrWhiteSpace(clientRoot) ||
            clientRoot == "<in-memory>")
            return;

        var path = Path.Combine(
            clientRoot,
            "Assets",
            "GameInfo",
            "Festival_Data.json");

        if (!File.Exists(path))
            return;

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(path));

            if (!document.RootElement.TryGetProperty(
                    "festivals", out var festivals) ||
                festivals.ValueKind != JsonValueKind.Array)
                return;

            var collected =
                new Dictionary<int, List<FestivalRandomReward>>();

            foreach (var festival in festivals.EnumerateArray())
            {
                if (!festival.TryGetProperty(
                        "random_reward_groups",
                        out var groups) ||
                    groups.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var group in groups.EnumerateArray())
                {
                    if (!group.TryGetProperty(
                            "source_item_id",
                            out var sourceElement) ||
                        !sourceElement.TryGetInt32(out var sourceItemId) ||
                        sourceItemId <= 0 ||
                        !group.TryGetProperty(
                            "items",
                            out var items) ||
                        items.ValueKind != JsonValueKind.Array)
                        continue;

                    if (!collected.TryGetValue(
                            sourceItemId,
                            out var rewards))
                    {
                        rewards = [];
                        collected[sourceItemId] = rewards;
                    }

                    JsonElement countRanges = default;
                    var hasCountRanges =
                        group.TryGetProperty(
                            "item_count_ranges",
                            out countRanges) &&
                        countRanges.ValueKind == JsonValueKind.Object;

                    foreach (var item in items.EnumerateArray())
                    {
                        if (!item.TryGetInt32(out var itemId) ||
                            itemId <= 0)
                            continue;

                        var min = 1;
                        var max = 1;
                        if (hasCountRanges &&
                            countRanges.TryGetProperty(
                                itemId.ToString(),
                                out var range) &&
                            range.ValueKind == JsonValueKind.Array &&
                            range.GetArrayLength() >= 2)
                        {
                            var values = range.EnumerateArray().ToArray();
                            if (values[0].TryGetInt32(out var parsedMin) &&
                                values[1].TryGetInt32(out var parsedMax))
                            {
                                min = Math.Clamp(parsedMin, 1, 999);
                                max = Math.Clamp(parsedMax, min, 999);
                            }
                        }

                        rewards.Add(new(
                            itemId,
                            min,
                            max));
                    }
                }
            }

            foreach (var pair in collected)
            {
                var rewards = pair.Value
                    .GroupBy(x => x.ItemId)
                    .Select(x => x.First())
                    .ToArray();

                if (rewards.Length > 0)
                    _rewards[pair.Key] = rewards;
            }
        }
        catch (JsonException)
        {
            // A malformed optional documentation file must not prevent
            // the game server from starting. Unsupported boxes simply
            // fall back to their normal unsupported-item response.
        }
    }

    public bool Supports(int sourceItemId)
        => _rewards.ContainsKey(sourceItemId);

    public IReadOnlyList<FestivalRandomReward> GetRewards(
        int sourceItemId)
        => _rewards.TryGetValue(sourceItemId, out var rewards)
            ? rewards
            : [];
}
