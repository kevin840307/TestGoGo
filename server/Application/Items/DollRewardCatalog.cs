using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Items;

/// <summary>
/// Authoritative reward pools mirrored from
/// client/scripts/game_info_window.gd DOLL_INFOS / DOLL_REWARD_COUNTS.
/// The recovered client exposes no per-reward weights, so every listed entry
/// is treated as equally likely, matching the existing 幸福娃娃 behavior.
/// </summary>
public static class DollRewardCatalog
{
    private static readonly IReadOnlyDictionary<int, int[]> Pools =
        new Dictionary<int, int[]>
        {
            [2126] =
            [
                1011, 1012, 1013, 50101, 50102, 50103, 2615, 2616,
                19179, 19178, 19183, 19180, 19059, 19109, 19110, 19112,
                19003, 19008, 19094, 19093, 19111, 18101, 19019, 18029,
                18030, 18031, 18032, 18037, 18038, 18039, 19085, 19086,
                19087, 19088, 2130, 2128, 2129, 19061, 19062, 19063,
                19064, 19065, 19066, 19067, 19068, 19182, 19023, 19026,
                19029, 2401, 2402, 2403, 2404, 18021
            ],
            [2127] =
            [
                1011, 1012, 1013, 50101, 50102, 50103, 2615, 2616,
                18010, 18108, 18109, 18110, 19129, 19128, 19005, 19004,
                19060, 2769, 2205, 18033, 18034, 18035, 18040, 18041,
                18042, 19089, 19090, 19091, 19024, 19027, 19030, 2130,
                2128, 2129, 2405, 2406, 2407, 2408, 2409, 2410
            ],
            [2128] =
            [
                18053, 18054, 18055, 18056, 18057, 18058,
                18059, 18060, 18061, 18062, 18063, 18064,
                18065, 18066, 18067, 18068, 18069, 18070,
                18071, 18072, 18073, 18074, 18075, 18076
            ],
            [2129] =
            [
                18077, 18078, 18079, 18080, 18081, 18082,
                18083, 18084, 18085, 18086, 18087, 18088
            ],
            [2130] =
            [
                18089, 18090, 18091, 18092, 18093, 18094,
                18095, 18096, 18097, 18098, 18099, 18100
            ],
            [2131] = [2451, 30207],
            [2132] = [2452, 19247],
            [2133] =
            [
                2816,
                2006, 2007, 2008, 2009, 2010, 2011, 2012,
                2013, 2014, 2015, 2016, 2017, 2018, 2019,
                2020, 2021, 2022, 2023, 2024, 2025, 2026, 2027
            ],
            [2134] = [1070, 2821]
        };

    public static bool Supports(int sourceItemId)
        => Pools.ContainsKey(sourceItemId);

    public static IReadOnlyList<FestivalRandomReward> GetRewards(
        int sourceItemId)
    {
        if (!Pools.TryGetValue(
                sourceItemId,
                out var itemIds))
        {
            return [];
        }

        return itemIds
            .Select(itemId =>
            {
                var count =
                    itemId is >= 2006 and <= 2027
                        ? 30
                        : 1;

                return new FestivalRandomReward(
                    itemId,
                    count,
                    count);
            })
            .ToArray();
    }

    public static IReadOnlyCollection<int> SourceItemIds
        => Pools.Keys.ToArray();
}
