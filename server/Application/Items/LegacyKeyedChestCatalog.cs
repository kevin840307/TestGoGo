using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Items;

public sealed record LegacyKeyedChestDefinition(
    int ChestItemId,
    int KeyItemId,
    string ChestName,
    string KeyName);

/// <summary>
/// The recovered client preserves the nine old chest/key tiers but not their
/// original reward table. The key pairing itself is deterministic and safe to
/// expose; rewards remain intentionally unavailable until authoritative data
/// is recovered.
/// </summary>
public static class LegacyKeyedChestCatalog
{
    private static readonly IReadOnlyDictionary<int, int> ChestToKey =
        Enumerable.Range(0, 9)
            .ToDictionary(
                index => 2117 + index,
                index => 2108 + index);

    public static LegacyKeyedChestDefinition? Resolve(
        CurrentClientDataCatalog current,
        int chestItemId)
    {
        if (!ChestToKey.TryGetValue(
                chestItemId,
                out var keyItemId))
        {
            return null;
        }

        var chest = current.FindItem(chestItemId);
        var key = current.FindItem(keyItemId);
        if (chest is null ||
            key is null ||
            !chest.Category.Equals(
                "general",
                StringComparison.OrdinalIgnoreCase) ||
            !key.Category.Equals(
                "general",
                StringComparison.OrdinalIgnoreCase) ||
            !chest.Name.EndsWith(
                "寶箱",
                StringComparison.Ordinal) ||
            !key.Name.EndsWith(
                "鑰匙",
                StringComparison.Ordinal))
        {
            return null;
        }

        var chestTier =
            chest.Name[..^2]
                .Replace(
                    "的",
                    "",
                    StringComparison.Ordinal);
        var keyTier =
            key.Name[..^2]
                .Replace(
                    "的",
                    "",
                    StringComparison.Ordinal);

        if (!chestTier.Equals(
                keyTier,
                StringComparison.Ordinal))
        {
            return null;
        }

        return new(
            chest.ItemId,
            key.ItemId,
            chest.Name,
            key.Name);
    }
}
