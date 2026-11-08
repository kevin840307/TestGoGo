using System.Text.RegularExpressions;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Items;

/// <summary>
/// Detects recovered reward descriptions that identify a legacy reward family
/// but cannot uniquely select one current-client item. These are data gaps,
/// not safe opportunities to guess a reward.
/// </summary>
public static partial class AmbiguousRewardCatalog
{
    [GeneratedRegex("『([^』]+)』")]
    private static partial Regex QuotedRewardRegex();

    public static bool IsAmbiguous(
        CurrentClientDataCatalog current,
        int itemIndex)
    {
        var source =
            current.FindItem(
                itemIndex);

        if (source is null ||
            !source.Category.Equals(
                "general",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(
                source.Description))
        {
            return false;
        }

        var quotedNames =
            QuotedRewardRegex()
                .Matches(
                    source.Description)
                .Select(match =>
                    match.Groups[1]
                        .Value
                        .Trim())
                .Where(name =>
                    !string.IsNullOrWhiteSpace(
                        name))
                .Distinct(
                    StringComparer.Ordinal)
                .ToArray();

        foreach (var quotedName in
                 quotedNames)
        {
            if (current.FindItemByName(
                    quotedName) is not null)
            {
                continue;
            }

            var normalized =
                CurrentClientDataCatalog.NormalizeName(
                    quotedName);

            if (string.IsNullOrWhiteSpace(
                    normalized))
            {
                continue;
            }

            var candidates =
                current.Items
                    .Where(item =>
                        item.ItemId !=
                            itemIndex &&
                        CurrentClientDataCatalog
                            .NormalizeName(
                                item.Name)
                            .StartsWith(
                                normalized,
                                StringComparison.Ordinal))
                    .Select(item =>
                        item.ItemId)
                    .Distinct()
                    .Take(2)
                    .Count();

            if (candidates >= 2)
                return true;
        }

        return false;
    }
}
