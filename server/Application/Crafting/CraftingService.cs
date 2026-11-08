using SGO.Server.Application.Abstractions;
using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Crafting;

public sealed class CraftingService
{
    private readonly ICraftingStore _store;
    private readonly RecipeCatalog _recipes;
    private readonly CurrentClientDataCatalog _current;

    public CraftingService(
        ICraftingStore store,
        RecipeCatalog recipes,
        CurrentClientDataCatalog current)
    {
        _store = store;
        _recipes = recipes;
        _current = current;
    }

    public IReadOnlyList<object> Craft(
        long accountId,
        long dbid,
        int recipeId,
        int batchCount)
    {
        batchCount = Math.Clamp(
            batchCount,
            1,
            9999);

        var recipe = _recipes.Find(recipeId);
        if (recipe is null ||
            !recipe.ActionType.Equals(
                "Craft",
                StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                CraftResponse(
                    false,
                    "找不到合成配方",
                    Array.Empty<object>())
            ];
        }

        var ingredients = recipe.Inputs
            .Select(x => new CraftIngredient(
                x.ItemId,
                checked(x.Count * batchCount)))
            .ToList();

        // The current client treats BlueprintID as one more consumable
        // ingredient and multiplies it by the requested batch count.
        if (recipe.BlueprintId > 0)
        {
            ingredients.Add(new CraftIngredient(
                recipe.BlueprintId,
                batchCount));
        }

        var successfulAttempts = 0;
        for (var i = 0; i < batchCount; i++)
        {
            if (recipe.SuccessRate >= 100 ||
                Random.Shared.Next(100) <
                recipe.SuccessRate)
            {
                successfulAttempts++;
            }
        }

        var grants = new List<CraftGrant>();
        if (successfulAttempts > 0)
        {
            foreach (var output in recipe.Outputs)
            {
                var grant = ResolveGrant(
                    output.ItemId,
                    checked(
                        output.Count *
                        successfulAttempts));

                if (grant is null)
                {
                    return
                    [
                        CraftResponse(
                            false,
                            $"合成產物資料不存在：{output.ItemId}",
                            Array.Empty<object>())
                    ];
                }

                grants.Add(grant);
            }
        }

        var moneyCost = checked(
            (long)recipe.MoneyCost *
            batchCount);

        var committed = _store.CommitCraft(
            accountId,
            dbid,
            moneyCost,
            ingredients,
            grants);

        if (!committed.Success)
        {
            return
            [
                CraftResponse(
                    false,
                    committed.Message,
                    Array.Empty<object>())
            ];
        }

        var resultItems = grants
            .GroupBy(x => x.ItemId)
            .Select(x => (object)new
            {
                ItemID = x.Key,
                Count = x.Sum(y => y.Count)
            })
            .ToArray();

        var packets = BuildAssetSyncPackets(
            accountId,
            dbid,
            committed.Money);

        packets.Insert(
            0,
            CraftResponse(
                successfulAttempts > 0,
                successfulAttempts > 0
                    ? $"合成完成：成功 {successfulAttempts}/{batchCount}"
                    : "合成失敗",
                resultItems));

        return packets;
    }

    public IReadOnlyList<object> Dismantle(
        long accountId,
        long dbid,
        IReadOnlyList<long> itemUids,
        string itemType)
    {
        var distinctUids = itemUids
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

        if (distinctUids.Length == 0)
        {
            return
            [
                DismantleResponse(
                    false,
                    "未選擇分解物品",
                    Array.Empty<object>())
            ];
        }

        var assets = _store.GetCharacterAssets(
            accountId,
            dbid);

        var targets =
            new List<DismantleTarget>();
        var grants =
            new List<CraftGrant>();
        long totalCost = 0;

        foreach (var uid in distinctUids)
        {
            int itemId;
            int requiredCount;
            int tabType;

            if (itemType.Equals(
                    "Equipment",
                    StringComparison.OrdinalIgnoreCase))
            {
                var equipment =
                    assets.Equips.FirstOrDefault(x =>
                        Convert.ToInt64(
                            x["ItemID"]) == uid &&
                        Convert.ToInt32(
                            x["Part"]) == 255 &&
                        Convert.ToInt32(
                            x["InBank"]) == 0 &&
                        Convert.ToInt32(
                            x["InShop"]) == 0);

                if (equipment is null)
                {
                    return
                    [
                        DismantleResponse(
                            false,
                            "裝備狀態已變更",
                            Array.Empty<object>())
                    ];
                }

                itemId = Convert.ToInt32(
                    equipment["ItemIdx"]);
                requiredCount = 1;
                tabType = 1;
            }
            else if (itemType.Equals(
                         "Material",
                         StringComparison.OrdinalIgnoreCase))
            {
                var item =
                    assets.Inventory.FirstOrDefault(x =>
                        Convert.ToInt64(
                            x["ItemUID"]) == uid &&
                        Convert.ToInt32(
                            x["InBank"]) == 0 &&
                        Convert.ToInt32(
                            x["InShop"]) == 0);

                if (item is null)
                {
                    return
                    [
                        DismantleResponse(
                            false,
                            "材料狀態已變更",
                            Array.Empty<object>())
                    ];
                }

                itemId = Convert.ToInt32(
                    item["ItemIndex"]);
                tabType = 0;

                var recipe =
                    _recipes.FindDismantle(
                        itemId);

                if (recipe is null ||
                    recipe.Inputs.Count != 1)
                {
                    return
                    [
                        DismantleResponse(
                            false,
                            "找不到分解配方",
                            Array.Empty<object>())
                    ];
                }

                requiredCount =
                    recipe.Inputs[0].Count;

                if (Convert.ToInt32(
                        item["Count"]) <
                    requiredCount)
                {
                    return
                    [
                        DismantleResponse(
                            false,
                            "材料數量不足",
                            Array.Empty<object>())
                    ];
                }
            }
            else
            {
                return
                [
                    DismantleResponse(
                        false,
                        "不支援的分解物品類型",
                        Array.Empty<object>())
                ];
            }

            var dismantleRecipe =
                _recipes.FindDismantle(
                    itemId);

            if (dismantleRecipe is null ||
                !dismantleRecipe.ActionType.Equals(
                    "Dismantle",
                    StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    DismantleResponse(
                        false,
                        "找不到分解配方",
                        Array.Empty<object>())
                ];
            }

            targets.Add(new DismantleTarget(
                tabType,
                uid,
                itemId,
                requiredCount));

            totalCost = checked(
                totalCost +
                dismantleRecipe.MoneyCost);

            foreach (var output in
                     dismantleRecipe.Outputs)
            {
                var grant = ResolveGrant(
                    output.ItemId,
                    output.Count);

                if (grant is null)
                {
                    return
                    [
                        DismantleResponse(
                            false,
                            $"分解產物資料不存在：{output.ItemId}",
                            Array.Empty<object>())
                    ];
                }

                grants.Add(grant);
            }
        }

        var mergedGrants = grants
            .GroupBy(x => new
            {
                x.ItemId,
                x.Kind,
                x.Part,
                x.Durability
            })
            .Select(x => new CraftGrant(
                x.Key.ItemId,
                x.Sum(y => y.Count),
                x.Key.Kind,
                x.Key.Part,
                x.Key.Durability))
            .ToArray();

        var committed =
            _store.CommitDismantle(
                accountId,
                dbid,
                totalCost,
                targets,
                mergedGrants);

        if (!committed.Success)
        {
            return
            [
                DismantleResponse(
                    false,
                    committed.Message,
                    Array.Empty<object>())
            ];
        }

        var resultItems = mergedGrants
            .GroupBy(x => x.ItemId)
            .Select(x => (object)new
            {
                ItemID = x.Key,
                Count = x.Sum(y => y.Count)
            })
            .ToArray();

        var packets = BuildAssetSyncPackets(
            accountId,
            dbid,
            committed.Money);

        packets.Insert(
            0,
            DismantleResponse(
                true,
                "分解完成",
                resultItems));

        return packets;
    }

    private CraftGrant? ResolveGrant(
        int itemId,
        int count)
    {
        if (itemId <= 0 ||
            count <= 0)
            return null;

        var item = _current.FindItem(itemId);
        if (item is null)
            return null;

        var category =
            item.Category.ToLowerInvariant();

        var kind = category switch
        {
            "equip" or
            "weapon" =>
                AssetPurchaseKind.Equipment,

            "horseequip" =>
                AssetPurchaseKind.HorseEquipment,

            "horsebattery" =>
                AssetPurchaseKind.HorseBattery,

            "general" or
            "recovery" or
            "booster" or
            "horse" =>
                AssetPurchaseKind.General,

            _ => (AssetPurchaseKind?)null
        };

        if (!kind.HasValue)
            return null;

        return new CraftGrant(
            itemId,
            count,
            kind.Value,
            item.EquipSlot,
            item.Durability);
    }

    private List<object> BuildAssetSyncPackets(
        long accountId,
        long dbid,
        long money)
    {
        var assets = _store.GetCharacterAssets(
            accountId,
            dbid);

        return
        [
            new
            {
                Type = "SyncMoney",
                Money = money
            },
            new
            {
                Type = "InventorySync",
                Items = assets.Inventory,
                MaxSize = assets.InventoryMaxSize
            },
            new
            {
                Type = "EquipSync",
                Equips = assets.Equips,
                MaxSize = assets.EquipMaxSize
            },
            new
            {
                Type = "HorseSync",
                Horses = assets.Horses,
                HorseEquips = assets.HorseEquips,
                MaxHorseEquipSize =
                    assets.HorseEquipMaxSize
            }
        ];
    }

    private static object CraftResponse(
        bool success,
        string message,
        IReadOnlyList<object> resultItems)
        => new
        {
            Type = "CraftItemResponse",
            Success = success,
            Message = message,
            ResultItems = resultItems
        };

    private static object DismantleResponse(
        bool success,
        string message,
        IReadOnlyList<object> resultItems)
        => new
        {
            Type = "DismantleItemResponse",
            Success = success,
            Message = message,
            ResultItems = resultItems
        };
}
