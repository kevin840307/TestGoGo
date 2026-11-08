using System.Text.RegularExpressions;
using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Items;

public sealed record ChoiceRewardOption(
    int ItemIndex,
    string Name,
    AssetPurchaseKind Kind,
    int Part,
    int Damage);

public sealed record ChoiceRewardDefinition(
    int SourceItemIndex,
    string SourceName,
    IReadOnlyList<ChoiceRewardOption> Options);

/// <summary>
/// Restores only explicit player-choice boxes whose current-client description
/// names every selectable reward. Random/probability wording is deliberately
/// rejected: unknown legacy RNG semantics must never be replaced by guessed
/// equal odds.
/// </summary>
public static partial class ChoiceRewardCatalog
{
    private static readonly string[] ChoiceMarkers =
    [
        "自由選擇",
        "任君挑選",
        "任選",
        "選擇其中",
        "選擇一",
        "選擇１",
        "選擇1",
        "任一"
    ];

    private static readonly string[] RandomMarkers =
    [
        "隨機",
        "機率",
        "概率",
        "抽取",
        "隨機獲得"
    ];

    [GeneratedRegex("『([^』]+)』")]
    private static partial Regex QuotedRewardRegex();

    public static ChoiceRewardDefinition? Resolve(
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
        if (!ChoiceMarkers.Any(marker =>
                description.Contains(
                    marker,
                    StringComparison.Ordinal)))
        {
            return null;
        }

        if (RandomMarkers.Any(marker =>
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

        if (names.Length < 2)
            return null;

        var options =
            new List<ChoiceRewardOption>(
                names.Length);

        foreach (var name in names)
        {
            var reward =
                current.FindItemByName(name);

            if (reward is null ||
                reward.ItemId == itemIndex ||
                !TryResolveKind(
                    reward,
                    out var kind,
                    out var part))
            {
                return null;
            }

            options.Add(new(
                reward.ItemId,
                reward.Name,
                kind,
                part,
                Math.Max(
                    0,
                    reward.Durability)));
        }

        return options
            .Select(x => x.ItemIndex)
            .Distinct()
            .Count() == options.Count
                ? new(
                    source.ItemId,
                    source.Name,
                    options)
                : null;
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
