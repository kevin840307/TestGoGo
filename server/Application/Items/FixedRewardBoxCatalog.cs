using SGO.Server.Domain.Assets;

namespace SGO.Server.Application.Items;

public sealed record FixedRewardBoxDefinition(
    int BoxItemId,
    int RewardItemId,
    string RewardName,
    AssetPurchaseKind RewardKind);

public static class FixedRewardBoxCatalog
{
    private static readonly IReadOnlyDictionary<int, FixedRewardBoxDefinition>
        Boxes = new Dictionary<int, FixedRewardBoxDefinition>
        {
            [2218] = new(2218, 19251, "王者悍戰印記．40", AssetPurchaseKind.Equipment),
            [2219] = new(2219, 19252, "王者悍戰印記．60", AssetPurchaseKind.Equipment),
            [2220] = new(2220, 19253, "王者悍戰印記．80", AssetPurchaseKind.Equipment),
            [2221] = new(2221, 19254, "王者悍戰印記．90", AssetPurchaseKind.Equipment),
            [2222] = new(2222, 19255, "王者術法印記．40", AssetPurchaseKind.Equipment),
            [2223] = new(2223, 19256, "王者術法印記．60", AssetPurchaseKind.Equipment),
            [2224] = new(2224, 19257, "王者術法印記．80", AssetPurchaseKind.Equipment),
            [2225] = new(2225, 19258, "王者術法印記．90", AssetPurchaseKind.Equipment),
            [2226] = new(2226, 19259, "王者神聖印記．40", AssetPurchaseKind.Equipment),
            [2227] = new(2227, 19260, "王者神聖印記．60", AssetPurchaseKind.Equipment),
            [2228] = new(2228, 19261, "王者神聖印記．80", AssetPurchaseKind.Equipment),
            [2229] = new(2229, 19262, "王者神聖印記．90", AssetPurchaseKind.Equipment)
        };

    public static FixedRewardBoxDefinition? Find(int boxItemId)
        => Boxes.GetValueOrDefault(boxItemId);
}
