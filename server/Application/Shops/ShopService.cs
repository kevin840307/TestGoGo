using SGO.Server.Application.Abstractions;
using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Shops;

public sealed class ShopService
{
    private readonly IGameStore _store;
    private readonly ShopCatalog _catalog;
    private readonly CurrentClientDataCatalog _current;

    public ShopService(
        IGameStore store,
        ShopCatalog catalog,
        CurrentClientDataCatalog current)
    {
        _store = store;
        _catalog = catalog;
        _current = current;
    }

    public object BuildOpenShopPacket(
        int shopId,
        string shopName,
        int priceRate)
    {
        var safeRate = Math.Clamp(priceRate, 1, 1000);
        var items = _catalog.GetShopItems(shopId)
            .Select(item => new
            {
                ItemIndex = item.ItemIndex,
                Price = Math.Max(
                    1,
                    checked(item.Price * safeRate / 100)),
                TabType = item.TabType,
                Name = item.Name
            })
            .Cast<object>()
            .ToArray();

        return new
        {
            Type = "NpcShopViewResponse",
            ShopID = shopId,
            ShopName = shopName,
            Items = items
        };
    }

    public IReadOnlyList<object> Buy(
        long accountId,
        long characterId,
        int shopId,
        int itemIndex,
        int count)
    {
        var item = _catalog.FindInShop(shopId, itemIndex);
        if (item is null)
            return Failure("此商店沒有這個商品");

        if (count <= 0 || count > (item.TabType == 0 ? 9999 : 1))
            return Failure("購買數量無效");

        var currentItem = _current.FindItem(item.ItemIndex);
        if (currentItem is null)
            return Failure(
                $"新版 GameData 找不到 ItemIndex={item.ItemIndex}");

        var kind = currentItem.Category.ToLowerInvariant() switch
        {
            "booster" or "general" or "recovery"
                => AssetPurchaseKind.General,
            "equip" or "weapon"
                => AssetPurchaseKind.Equipment,
            "horse"
                => AssetPurchaseKind.Horse,
            "horseequip"
                => AssetPurchaseKind.HorseEquipment,
            "horsebattery"
                => AssetPurchaseKind.HorseBattery,
            _ => AssetPurchaseKind.General
        };

        var character = _store.GetCharacter(accountId, characterId);
        if (character is null)
            return Failure("角色不存在");

        var rate = Convert.ToInt32(character["Career"]) == 9 ? 80 : 100;
        var unitPrice = Math.Max(1, item.Price * rate / 100);

        var result = _store.BuyAsset(
            accountId,
            characterId,
            kind,
            item.ItemIndex,
            count,
            unitPrice,
            currentItem.EquipSlot);

        if (!result.Success)
            return Failure(result.Message);

        return BuildAssetRefreshPackets(
            accountId,
            characterId,
            result.Money,
            result.Message);
    }

    public IReadOnlyList<object> Sell(
        long accountId,
        long characterId,
        long uid,
        int tabType,
        int count)
    {
        if (uid <= 0 || count <= 0)
            return Failure("出售資料無效");

        var assets = _store.GetCharacterAssets(
            accountId,
            characterId);

        var itemIndex = FindItemIndexByUid(
            assets,
            uid,
            tabType);

        if (itemIndex <= 0)
            return Failure("找不到要出售的物品");

        var item = _catalog.FindItem(itemIndex);
        if (item is null || !item.CanSell)
            return Failure("此物品無法出售");

        var unitPrice = Math.Max(1, item.Price / 2);
        var success = _store.SellAsset(
            accountId,
            characterId,
            uid,
            tabType,
            count,
            unitPrice,
            out var money);

        if (!success)
            return Failure("出售失敗");

        return BuildAssetRefreshPackets(
            accountId,
            characterId,
            money,
            "出售成功");
    }

    private IReadOnlyList<object> BuildAssetRefreshPackets(
        long accountId,
        long characterId,
        long money,
        string message)
    {
        var assets = _store.GetCharacterAssets(
            accountId,
            characterId);

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
                MaxHorseEquipSize = assets.HorseEquipMaxSize
            },
            new
            {
                Type = "SystemMessageFlash",
                Message = message
            }
        ];
    }

    private static int FindItemIndexByUid(
        CharacterAssets assets,
        long uid,
        int tabType)
    {
        switch (tabType)
        {
            case 0:
            {
                var item = assets.Inventory.FirstOrDefault(
                    x => Convert.ToInt64(x["ItemUID"]) == uid);
                return item is null
                    ? 0
                    : Convert.ToInt32(item["ItemIndex"]);
            }

            case 1:
            {
                var item = assets.Equips.FirstOrDefault(
                    x => Convert.ToInt64(x["ItemID"]) == uid);
                return item is null
                    ? 0
                    : Convert.ToInt32(item["ItemIdx"]);
            }

            case 3:
            {
                var item = assets.HorseEquips.FirstOrDefault(
                    x => Convert.ToInt64(x["EquipUID"]) == uid);
                return item is null
                    ? 0
                    : Convert.ToInt32(item["EquipIdx"]);
            }

            default:
                return 0;
        }
    }

    private static IReadOnlyList<object> Failure(
        string message)
        =>
        [
            new
            {
                Type = "SystemMessageFlash",
                Message = message
            }
        ];
}
