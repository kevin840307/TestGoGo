using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Items;

public sealed record CraftingArtifactDefinition(
    int SourceItemId,
    string SourceName,
    int OutputItemId,
    string OutputName);

/// <summary>
/// Recognizes old blueprint/card/chip items even when the recovered description
/// lost its material list. A match is accepted only when stripping a known
/// crafting suffix resolves one unique current-client non-general item.
/// </summary>
public static class CraftingArtifactCatalog
{
    private static readonly string[] Suffixes =
    [
        "電腦卡",
        "設計圖",
        "藍圖",
        "晶片",
        "圖樣",
        "魔卡",
        "之信"
    ];

    public static CraftingArtifactDefinition? Resolve(
        CurrentClientDataCatalog current,
        int itemId)
    {
        var source = current.FindItem(itemId);
        if (source is null ||
            !source.Category.Equals(
                "general",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sourceName =
            string.IsNullOrWhiteSpace(source.ScriptName)
                ? source.Name
                : source.ScriptName;

        var suffix =
            Suffixes.FirstOrDefault(candidate =>
                sourceName.EndsWith(
                    candidate,
                    StringComparison.Ordinal));

        if (suffix is null ||
            sourceName.Length <= suffix.Length)
        {
            return null;
        }

        var outputName =
            sourceName[..^suffix.Length]
                .Trim();

        var output =
            current.FindItemByName(outputName);

        if (output is null ||
            output.ItemId == source.ItemId ||
            output.Category.Equals(
                "general",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new(
            source.ItemId,
            source.Name,
            output.ItemId,
            output.Name);
    }
}
