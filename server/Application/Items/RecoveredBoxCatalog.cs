using System.Text.RegularExpressions;
using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Items;

public sealed record RecoveredBoxReward(
    int ItemIndex,
    string Name,
    AssetPurchaseKind Kind,
    int Part,
    int Damage);

public sealed record RecoveredBoxDefinition(
    int BoxItemIndex,
    string BoxName,
    IReadOnlyList<RecoveredBoxReward> Rewards);

/// <summary>
/// Conservatively restores simple fixed-reward boxes from the authoritative
/// current-client item description. Choice/random boxes are intentionally
/// excluded so the server never grants a guessed reward.
/// </summary>
public static partial class RecoveredBoxCatalog
{
    private static readonly string[] AmbiguousMarkers =
    [
        "自由選擇",
        "選擇",
        "隨機",
        "其中",
        "任一",
        "一款",
        "機率",
        "隨機獲得"
    ];

    [GeneratedRegex("『([^』]+)』")]
    private static partial Regex QuotedRewardRegex();

    public static RecoveredBoxDefinition? Resolve(
        CurrentClientDataCatalog current,
        int itemIndex)
    {
        var source = current.FindItem(itemIndex);
        if (source is null ||
            !source.Category.Equals(
                "general",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(source.Description))
        {
            return null;
        }

        var description = source.Description.Trim();
        if (!description.Contains(
                "打開",
                StringComparison.Ordinal) &&
            !description.Contains(
                "開啟",
                StringComparison.Ordinal))
        {
            return null;
        }

        if (AmbiguousMarkers.Any(marker =>
                description.Contains(
                    marker,
                    StringComparison.Ordinal)))
        {
            return null;
        }

        var names = QuotedRewardRegex()
            .Matches(description)
            .Select(match =>
                match.Groups[1].Value.Trim())
            .Where(name =>
                !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (names.Length == 0)
            return null;

        var rewards = new List<RecoveredBoxReward>();
        foreach (var name in names)
        {
            var reward = current.FindItemByName(name);
            if (reward is not null &&
                reward.ItemId != itemIndex &&
                TryResolveKind(
                    reward,
                    out var kind,
                    out var part))
            {
                rewards.Add(new(
                    reward.ItemId,
                    reward.Name,
                    kind,
                    part,
                    Math.Max(0, reward.Durability)));
                continue;
            }

            var setRewards =
                ResolveExplicitSet(
                    current,
                    name,
                    description);

            if (setRewards.Count == 0)
                return null;

            rewards.AddRange(setRewards);
        }

        rewards = rewards
            .GroupBy(x => x.ItemIndex)
            .Select(x => x.First())
            .ToList();

        return rewards.Count == 0
            ? null
            : new(
                source.ItemId,
                source.Name,
                rewards);
    }

    private static IReadOnlyList<RecoveredBoxReward> ResolveExplicitSet(
        CurrentClientDataCatalog current,
        string quotedName,
        string description)
    {
        if (!quotedName.EndsWith(
                "套裝",
                StringComparison.Ordinal) ||
            !description.Contains(
                "整套",
                StringComparison.Ordinal))
        {
            return [];
        }

        var baseName =
            quotedName[..^2];
        var normalizedBase =
            CurrentClientDataCatalog.NormalizeName(
                baseName);

        if (string.IsNullOrWhiteSpace(
                normalizedBase))
        {
            return [];
        }

        var candidates =
            new List<RecoveredBoxReward>();

        foreach (var item in current.Items)
        {
            if (item.Category is not (
                    "equip" or
                    "weapon") &&
                !item.Category.Equals(
                    "equip",
                    StringComparison.OrdinalIgnoreCase) &&
                !item.Category.Equals(
                    "weapon",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedName =
                CurrentClientDataCatalog.NormalizeName(
                    item.Name);

            if (!normalizedName.StartsWith(
                    normalizedBase,
                    StringComparison.Ordinal) ||
                !TryResolveKind(
                    item,
                    out var kind,
                    out var part))
            {
                continue;
            }

            candidates.Add(new(
                item.ItemId,
                item.Name,
                kind,
                part,
                Math.Max(
                    0,
                    item.Durability)));
        }

        // "整套" is accepted only when the current catalog yields a
        // substantial, slot-unique set. Duplicate visual/equipment slots would
        // make the recovered family ambiguous and therefore unsafe to grant.
        if (candidates.Count < 3 ||
            candidates
                .Select(x => x.ItemIndex)
                .Distinct()
                .Count() != candidates.Count ||
            candidates
                .GroupBy(x => x.Part)
                .Any(group =>
                    group.Count() != 1))
        {
            return [];
        }

        return candidates
            .OrderBy(x => x.Part)
            .ThenBy(x => x.ItemIndex)
            .ToArray();
    }

    private static bool TryResolveKind(
        CurrentItemDefinition item,
        out AssetPurchaseKind kind,
        out int part)
    {
        part = item.EquipSlot;

        switch (item.Category.ToLowerInvariant())
        {
            case "general":
            case "recovery":
            case "booster":
                kind = AssetPurchaseKind.General;
                return true;

            case "horsebattery":
                kind = AssetPurchaseKind.HorseBattery;
                return true;

            case "equip":
            case "weapon":
                kind = AssetPurchaseKind.Equipment;
                return true;

            case "horseequip":
                kind = AssetPurchaseKind.HorseEquipment;
                return true;

            default:
                kind = default;
                return false;
        }
    }
}
