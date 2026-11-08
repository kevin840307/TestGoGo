using SGO.Server.Application.Abstractions;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Items;

public sealed record CosmeticTransformationDefinition(
    int SourceItemId,
    string Name,
    bool ConsumeSource,
    IReadOnlyList<CharacterCosmeticOverride> Overrides);

/// <summary>
/// Current-client appearance-only transformation devices. These mappings are
/// restricted to item descriptions/equipment sets that are unambiguous in the
/// recovered client. Race-specific Wafuu sets are resolved from the current
/// client equipment data; the flower-ball weapon choice remains unresolved.
/// </summary>
public static class CosmeticTransformationCatalog
{
    private sealed record Mapping(
        int? RequiredRace,
        int[] ItemIds);

    private static readonly IReadOnlyDictionary<int, int[]> WafuuByRace =
        new Dictionary<int, int[]>
        {
            // Human male / Asura-like male set.
            [0] = [19272, 19276, 19280, 19284],
            // Human female set.
            [1] = [19273, 19277, 19281, 19285],
            // Migu set.
            [2] = [19274, 19278, 19282, 19286],
            // Robot set.
            [3] = [19275, 19279, 19283, 19287]
        };

    private static readonly IReadOnlyDictionary<int, Mapping> Mappings =
        new Dictionary<int, Mapping>
        {
            // Full Summer / Winter sets (human female).
            [2004] = new(1, [19288, 19289, 19290, 19291]),
            [2005] = new(1, [19292, 19293, 19294, 19295]),

            // Human male formal set pieces.
            [2006] = new(0, [19297]),
            [2007] = new(0, [19298]),
            [2008] = new(0, [19299, 19300]),

            // Human female wedding set pieces.
            [2009] = new(1, [19302]),
            [2010] = new(1, [19303]),
            [2011] = new(1, [19304, 19305]),
            [2012] = new(1, [19301]),

            // Migu formal / wedding pieces.
            [2013] = new(2, [19307]),
            [2014] = new(2, [19308]),
            [2015] = new(2, [19309, 19310]),
            [2016] = new(2, [19312]),
            [2017] = new(2, [19313]),
            [2018] = new(2, [19314, 19315]),
            [2019] = new(2, [19311]),

            // Robot formal / wedding pieces.
            [2020] = new(3, [19317]),
            [2021] = new(3, [19318]),
            [2022] = new(3, [19319, 19320]),
            [2023] = new(3, [19316]),
            [2024] = new(3, [19322]),
            [2025] = new(3, [19323]),
            [2026] = new(3, [19324, 19325]),
            [2027] = new(3, [19321]),

            // Permanent noble / pirate appearance pieces.
            [2028] = new(null, [19326]),
            [2029] = new(null, [19327]),
            [2030] = new(null, [19328, 19329]),
            [2031] = new(null, [19330]),
            [2032] = new(null, [19331]),
            [2033] = new(null, [19332, 19333]),

            // One-use romantic full sets.
            [2034] = new(null, [19334, 19335, 19336, 19337, 19338]),
            [2035] = new(null, [19339, 19340, 19341, 19342, 19343]),

            // Permanent battle-spirit outfit. No matching "戰魂頭飾" exists
            // in the current transformation set; only the four exact pieces
            // are applied.
            [2036] = new(null, [19344, 19345, 19346, 19347])
        };

    public static bool Supports(int itemId)
        => itemId == 2003 ||
           Mappings.ContainsKey(itemId);

    public static int? RequiredRace(int itemId)
        => itemId == 2003
            ? null
            : Mappings.TryGetValue(itemId, out var mapping)
                ? mapping.RequiredRace
                : null;

    public static CosmeticTransformationDefinition? Resolve(
        CurrentClientDataCatalog current,
        int itemId,
        int race)
    {
        int[]? targetItemIds;
        int? requiredRace;

        if (itemId == 2003)
        {
            if (!WafuuByRace.TryGetValue(
                    race,
                    out targetItemIds))
            {
                return null;
            }

            requiredRace = null;
        }
        else
        {
            if (!Mappings.TryGetValue(
                    itemId,
                    out var mapping))
            {
                return null;
            }

            requiredRace =
                mapping.RequiredRace;

            if (requiredRace.HasValue &&
                requiredRace.Value != race)
            {
                return null;
            }

            targetItemIds =
                mapping.ItemIds;
        }

        var source = current.FindItem(itemId);
        if (source is null ||
            !source.Category.Equals(
                "general",
                StringComparison.OrdinalIgnoreCase) ||
            !source.ScriptName.EndsWith(
                "變身器",
                StringComparison.Ordinal))
        {
            return null;
        }

        var overrides = new List<CharacterCosmeticOverride>();
        foreach (var targetId in targetItemIds!)
        {
            var target = current.FindItem(targetId);
            if (target is null ||
                !target.Category.Equals(
                    "equip",
                    StringComparison.OrdinalIgnoreCase) ||
                target.EquipSlot is < 0 or > 8)
            {
                return null;
            }

            overrides.Add(new(
                target.EquipSlot,
                target.ItemId,
                Math.Max(0, target.ColorIndex),
                source.ItemId));
        }

        // Two target records must never silently fight for the same visual
        // slot; that would mean the recovered mapping is ambiguous.
        if (overrides
            .GroupBy(x => x.Part)
            .Any(group => group.Count() != 1))
        {
            return null;
        }

        return new(
            source.ItemId,
            source.Name,
            !source.Description.Contains(
                "永久道具",
                StringComparison.Ordinal),
            overrides);
    }
}
