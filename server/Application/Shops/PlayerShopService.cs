using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Messaging;
using SGO.Server.Domain.Assets;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Shops;

public sealed class PlayerShopService
{
    private sealed class Listing
    {
        public required long Uid { get; init; }
        public required int TabType { get; init; }
        public required int ItemIndex { get; init; }
        public required long UnitPrice { get; init; }
        public int Count { get; set; }
        public required Dictionary<string, object> View { get; set; }
    }

    private sealed class ShopRuntime
    {
        public required long AccountId { get; init; }
        public required long CharacterId { get; init; }
        public string Name { get; set; } = "我的商店";
        public bool IsOpen { get; set; }
        public List<Listing> Listings { get; } = new();
    }

    private readonly object _sync = new();
    private readonly IPlayerShopStore _store;
    private readonly ICharacterStore _characters;
    private readonly CurrentClientDataCatalog _current;
    private readonly ServerBroadcastHub _broadcasts;
    private readonly Dictionary<long, ShopRuntime> _shops = new();

    public PlayerShopService(
        IPlayerShopStore store,
        ICharacterStore characters,
        CurrentClientDataCatalog current,
        ServerBroadcastHub broadcasts)
    {
        _store = store;
        _characters = characters;
        _current = current;
        _broadcasts = broadcasts;
    }

    public IReadOnlyList<object> Handle(
        ClientSession session,
        long accountId,
        string action,
        long uid,
        int tabType,
        int count,
        long price,
        long targetDbid,
        string shopName)
    {
        if (!session.CharacterId.HasValue)
            return [];

        var selfDbid = session.CharacterId.Value;

        return action switch
        {
            "SyncMyShop" => SyncMine(
                accountId,
                selfDbid),
            "AddItem" => AddItem(
                accountId,
                selfDbid,
                uid,
                tabType,
                count,
                price),
            "RemoveItem" => RemoveItem(
                accountId,
                selfDbid,
                uid,
                tabType),
            "OpenShop" => Open(
                session,
                accountId,
                selfDbid,
                shopName),
            "CloseShop" => Close(
                session,
                accountId,
                selfDbid),
            "ViewShop" => View(
                session,
                targetDbid),
            "BuyItem" => Buy(
                session,
                accountId,
                selfDbid,
                targetDbid,
                uid,
                count),
            _ =>
            [
                Flash($"玩家商店操作尚未支援：{action}")
            ]
        };
    }

    public void Disconnect(
        Guid connectionId,
        int mapId,
        long accountId,
        long characterId)
    {
        if (characterId <= 0)
            return;

        ShopRuntime? shop;
        lock (_sync)
        {
            if (!_shops.TryGetValue(
                    characterId,
                    out shop))
                return;

            _shops.Remove(characterId);
        }

        _store.ClearPlayerShopAssets(
            accountId,
            characterId);

        if (shop.IsOpen && mapId > 0)
        {
            _broadcasts.PublishToMap(
                mapId,
                Board(
                    characterId,
                    false,
                    shop.Name),
                connectionId);
        }
    }

    private IReadOnlyList<object> SyncMine(
        long accountId,
        long characterId)
    {
        var shop = GetOrCreate(
            accountId,
            characterId);

        lock (_sync)
            return [BuildShopSync(shop)];
    }

    private IReadOnlyList<object> AddItem(
        long accountId,
        long characterId,
        long uid,
        int tabType,
        int count,
        long price)
    {
        if (uid <= 0 ||
            tabType is < 0 or > 3 ||
            count <= 0 ||
            price <= 0)
            return [Flash("上架資料無效")];

        var shop = GetOrCreate(
            accountId,
            characterId);

        lock (_sync)
        {
            if (shop.IsOpen)
                return [Flash("營業中不能修改商品")];

            if (shop.Listings.Any(x =>
                    x.Uid == uid &&
                    x.TabType == tabType))
            {
                return [Flash("此商品已上架")];
            }

            var item = ResolveOwnedAsset(
                accountId,
                characterId,
                uid,
                tabType,
                count,
                out var itemIndex,
                out var error);

            if (item is null)
                return [Flash(error)];

            var definition =
                _current.FindItem(itemIndex);

            if (tabType != 2 &&
                (definition is null ||
                 definition.CanTrade != 1))
            {
                return [Flash("此物品無法交易")];
            }

            var reserved =
                _store.SetPlayerShopAssetState(
                    accountId,
                    characterId,
                    uid,
                    tabType,
                    count,
                    true);

            if (!reserved.Success)
                return [Flash("商品上架失敗")];

            var reservedView = ResolveReservedAsset(
                accountId,
                characterId,
                reserved.ReservedUid,
                tabType,
                count,
                price,
                out itemIndex);

            if (reservedView is null)
            {
                _store.SetPlayerShopAssetState(
                    accountId,
                    characterId,
                    reserved.ReservedUid,
                    tabType,
                    count,
                    false);
                return [Flash("商品上架後資料同步失敗")];
            }

            shop.Listings.Add(new Listing
            {
                Uid = reserved.ReservedUid,
                TabType = tabType,
                ItemIndex = itemIndex,
                Count = count,
                UnitPrice = price,
                View = reservedView
            });

            return BuildOwnerSyncPackets(
                shop,
                accountId,
                characterId);
        }
    }

    private IReadOnlyList<object> RemoveItem(
        long accountId,
        long characterId,
        long uid,
        int tabType)
    {
        var shop = GetOrCreate(
            accountId,
            characterId);

        lock (_sync)
        {
            if (shop.IsOpen)
                return [Flash("營業中不能修改商品")];

            var listing = shop.Listings
                .FirstOrDefault(x =>
                    x.Uid == uid &&
                    x.TabType == tabType);

            if (listing is null)
                return [Flash("找不到上架商品")];

            if (!_store.SetPlayerShopAssetState(
                    accountId,
                    characterId,
                    listing.Uid,
                    listing.TabType,
                    listing.Count,
                    false).Success)
            {
                return [Flash("商品下架失敗")];
            }

            shop.Listings.Remove(listing);
            return BuildOwnerSyncPackets(
                shop,
                accountId,
                characterId);
        }
    }

    private IReadOnlyList<object> Open(
        ClientSession session,
        long accountId,
        long characterId,
        string shopName)
    {
        var shop = GetOrCreate(
            accountId,
            characterId);

        lock (_sync)
        {
            if (shop.Listings.Count == 0)
                return [Flash("請先上架至少一件商品")];

            shop.Name = string.IsNullOrWhiteSpace(
                    shopName)
                ? "我的商店"
                : shopName.Trim()[..Math.Min(
                    30,
                    shopName.Trim().Length)];
            shop.IsOpen = true;
        }

        var board = Board(
            characterId,
            true,
            shop.Name);

        _broadcasts.PublishToMap(
            session.CurrentMapId,
            board,
            session.ConnectionId);

        return [board];
    }

    private IReadOnlyList<object> Close(
        ClientSession session,
        long accountId,
        long characterId)
    {
        ShopRuntime shop;

        lock (_sync)
        {
            if (!_shops.TryGetValue(
                    characterId,
                    out shop!))
            {
                return
                [
                    Board(
                        characterId,
                        false,
                        "我的商店")
                ];
            }

            _shops.Remove(characterId);
        }

        _store.ClearPlayerShopAssets(
            accountId,
            characterId);

        var board = Board(
            characterId,
            false,
            shop.Name);

        _broadcasts.PublishToMap(
            session.CurrentMapId,
            board,
            session.ConnectionId);

        var assets = _store.GetCharacterAssets(
            accountId,
            characterId);

        return
        [
            board,
            new
            {
                Type = "ShopSync",
                Items = Array.Empty<object>()
            },
            BuildAssetSync(
                assets,
                0),
            BuildAssetSync(
                assets,
                1),
            BuildAssetSync(
                assets,
                2),
            BuildAssetSync(
                assets,
                3)
        ];
    }

    private IReadOnlyList<object> View(
        ClientSession session,
        long targetDbid)
    {
        if (targetDbid <= 0)
            return [Flash("商店目標無效")];

        var target =
            _broadcasts.FindCharacter(
                targetDbid);

        if (target is null ||
            target.MapId != session.CurrentMapId)
        {
            return [Flash("商店主人不在附近或已離線")];
        }

        lock (_sync)
        {
            if (!_shops.TryGetValue(
                    targetDbid,
                    out var shop) ||
                !shop.IsOpen)
            {
                return [Flash("此玩家目前沒有營業中的商店")];
            }

            return
            [
                new
                {
                    Type = "ShopViewResponse",
                    ShopName = shop.Name,
                    OwnerDBID = targetDbid,
                    Items = shop.Listings
                        .Select(x => x.View)
                        .ToArray()
                }
            ];
        }
    }

    private IReadOnlyList<object> Buy(
        ClientSession session,
        long buyerAccountId,
        long buyerDbid,
        long sellerDbid,
        long uid,
        int count)
    {
        if (sellerDbid <= 0 ||
            sellerDbid == buyerDbid ||
            uid <= 0 ||
            count <= 0)
            return [Flash("購買資料無效")];

        var sellerPresence =
            _broadcasts.FindCharacter(
                sellerDbid);

        if (sellerPresence is null ||
            sellerPresence.MapId != session.CurrentMapId)
        {
            return [Flash("商店主人不在附近或已離線")];
        }

        lock (_sync)
        {
            if (!_shops.TryGetValue(
                    sellerDbid,
                    out var shop) ||
                !shop.IsOpen)
            {
                return [Flash("商店已關閉")];
            }

            var listing = shop.Listings
                .FirstOrDefault(x =>
                    x.Uid == uid);

            if (listing is null ||
                count > listing.Count)
                return [Flash("商品數量不足")];

            var result =
                _store.BuyPlayerShopAsset(
                    buyerAccountId,
                    buyerDbid,
                    shop.AccountId,
                    sellerDbid,
                    listing.Uid,
                    listing.TabType,
                    count,
                    listing.UnitPrice);

            if (!result.Success)
                return [Flash(result.Message)];

            listing.Count -= count;

            if (result.SoldOut ||
                listing.Count <= 0)
            {
                shop.Listings.Remove(listing);
            }
            else
            {
                listing.View["Count"] =
                    listing.Count;
            }

            var buyerAssets =
                _store.GetCharacterAssets(
                    buyerAccountId,
                    buyerDbid);
            var sellerAssets =
                _store.GetCharacterAssets(
                    shop.AccountId,
                    sellerDbid);

            PublishSellerUpdate(
                shop,
                sellerAssets,
                result.SellerMoney);

            if (shop.Listings.Count == 0)
            {
                shop.IsOpen = false;
                _broadcasts.PublishToMap(
                    session.CurrentMapId,
                    Board(
                        sellerDbid,
                        false,
                        shop.Name));
            }

            return
            [
                new
                {
                    Type = "SyncMoney",
                    Money = result.BuyerMoney
                },
                BuildAssetSync(
                    buyerAssets,
                    listing.TabType),
                new
                {
                    Type = "SystemMessageFlash",
                    Message = "購買成功"
                }
            ];
        }
    }

    private ShopRuntime GetOrCreate(
        long accountId,
        long characterId)
    {
        lock (_sync)
        {
            if (_shops.TryGetValue(
                    characterId,
                    out var existing))
                return existing;

            var shop = new ShopRuntime
            {
                AccountId = accountId,
                CharacterId = characterId
            };
            _shops[characterId] = shop;
            return shop;
        }
    }

    private Dictionary<string, object>? ResolveOwnedAsset(
        long accountId,
        long characterId,
        long uid,
        int tabType,
        int count,
        out int itemIndex,
        out string error)
    {
        itemIndex = 0;
        error = "";
        var assets = _store.GetCharacterAssets(
            accountId,
            characterId);

        switch (tabType)
        {
            case 0:
            {
                var item = assets.Inventory.FirstOrDefault(x =>
                    Convert.ToInt64(x["ItemUID"]) == uid &&
                    Convert.ToInt32(x["InBank"]) == 0 &&
                    Convert.ToInt32(x["InShop"]) == 0);

                if (item is null ||
                    Convert.ToInt32(item["Count"]) < count)
                {
                    error = "物品數量不足";
                    return null;
                }

                itemIndex =
                    Convert.ToInt32(item["ItemIndex"]);
                return item;
            }

            case 1:
            {
                var item = assets.Equips.FirstOrDefault(x =>
                    Convert.ToInt64(x["ItemID"]) == uid &&
                    Convert.ToInt32(x["Part"]) == 255 &&
                    Convert.ToInt32(x["InBank"]) == 0 &&
                    Convert.ToInt32(x["InShop"]) == 0);

                if (item is null || count != 1)
                {
                    error = "裝備需卸下後才能上架";
                    return null;
                }

                itemIndex =
                    Convert.ToInt32(item["ItemIdx"]);
                return item;
            }

            case 2:
            {
                var item = assets.Pets.FirstOrDefault(x =>
                    Convert.ToInt64(x["UID"]) == uid &&
                    Convert.ToInt32(x["State"]) == 0 &&
                    Convert.ToInt32(x["InShop"]) == 0);

                if (item is null ||
                    count != 1 ||
                    Convert.ToInt64(item["Equip0"]) > 0 ||
                    Convert.ToInt64(item["Equip1"]) > 0 ||
                    Convert.ToInt64(item["Equip2"]) > 0)
                {
                    error = "寵物需收回並卸下裝備後才能上架";
                    return null;
                }

                itemIndex =
                    Convert.ToInt32(item["PETID"]);
                return item;
            }

            case 3:
            {
                var item = assets.HorseEquips.FirstOrDefault(x =>
                    Convert.ToInt64(x["EquipUID"]) == uid &&
                    Convert.ToInt64(x["HorseUID"]) == -1 &&
                    Convert.ToInt32(x["InBank"]) == 0 &&
                    Convert.ToInt32(x["InShop"]) == 0);

                if (item is null || count != 1)
                {
                    error = "機甲裝備需卸下後才能上架";
                    return null;
                }

                itemIndex =
                    Convert.ToInt32(item["EquipIdx"]);
                return item;
            }
        }

        error = "不支援的商品類型";
        return null;
    }

    private Dictionary<string, object>? ResolveReservedAsset(
        long accountId,
        long characterId,
        long uid,
        int tabType,
        int count,
        long price,
        out int itemIndex)
    {
        itemIndex = 0;
        var assets = _store.GetCharacterAssets(
            accountId,
            characterId);
        Dictionary<string, object>? source = tabType switch
        {
            0 => assets.Inventory.FirstOrDefault(x =>
                Convert.ToInt64(x["ItemUID"]) == uid &&
                Convert.ToInt32(x["InShop"]) == 1),
            1 => assets.Equips.FirstOrDefault(x =>
                Convert.ToInt64(x["ItemID"]) == uid &&
                Convert.ToInt32(x["InShop"]) == 1),
            2 => assets.Pets.FirstOrDefault(x =>
                Convert.ToInt64(x["UID"]) == uid &&
                Convert.ToInt32(x["InShop"]) == 1),
            3 => assets.HorseEquips.FirstOrDefault(x =>
                Convert.ToInt64(x["EquipUID"]) == uid &&
                Convert.ToInt32(x["InShop"]) == 1),
            _ => null
        };

        if (source is null)
            return null;

        itemIndex = tabType switch
        {
            0 => Convert.ToInt32(source["ItemIndex"]),
            1 => Convert.ToInt32(source["ItemIdx"]),
            2 => Convert.ToInt32(source["PETID"]),
            3 => Convert.ToInt32(source["EquipIdx"]),
            _ => 0
        };

        var view =
            new Dictionary<string, object>(source)
            {
                ["TabType"] = tabType,
                ["UID"] = uid,
                ["ItemIndex"] = itemIndex,
                ["Count"] = count,
                ["Price"] = price,
                ["Name"] =
                    _current.FindItem(itemIndex)?.Name ??
                    (tabType == 2
                        ? source.GetValueOrDefault(
                                "Name",
                                $"Pet {itemIndex}")
                            ?.ToString() ??
                          $"Pet {itemIndex}"
                        : $"Item {itemIndex}")
            };

        if (tabType == 2)
            view["ExtraData"] =
                new Dictionary<string, object>(source);

        return view;
    }

    private IReadOnlyList<object> BuildOwnerSyncPackets(
        ShopRuntime shop,
        long accountId,
        long characterId)
    {
        var assets =
            _store.GetCharacterAssets(
                accountId,
                characterId);

        return
        [
            BuildShopSync(shop),
            BuildAssetSync(assets, 0),
            BuildAssetSync(assets, 1),
            BuildAssetSync(assets, 2),
            BuildAssetSync(assets, 3)
        ];
    }

    private object BuildShopSync(
        ShopRuntime shop)
        => new
        {
            Type = "ShopSync",
            Items = shop.Listings
                .Select(x => x.View)
                .ToArray()
        };

    private static object BuildAssetSync(
        CharacterAssets assets,
        int tabType)
        => tabType switch
        {
            0 => new
            {
                Type = "InventorySync",
                Items = (object)assets.Inventory,
                MaxSize = assets.InventoryMaxSize
            },
            1 => new
            {
                Type = "EquipSync",
                Equips = (object)assets.Equips,
                MaxSize = assets.EquipMaxSize
            },
            2 => new
            {
                Type = "PetSync",
                Pets = (object)assets.Pets,
                MaxSize = assets.PetMaxSize
            },
            _ => new
            {
                Type = "HorseSync",
                Horses = (object)assets.Horses,
                HorseEquips = (object)assets.HorseEquips,
                MaxHorseEquipSize =
                    assets.HorseEquipMaxSize
            }
        };

    private void PublishSellerUpdate(
        ShopRuntime shop,
        CharacterAssets assets,
        long money)
    {
        _broadcasts.PublishToCharacter(
            shop.CharacterId,
            new
            {
                Type = "SyncMoney",
                Money = money
            });
        _broadcasts.PublishToCharacter(
            shop.CharacterId,
            BuildShopSync(shop));
        _broadcasts.PublishToCharacter(
            shop.CharacterId,
            BuildAssetSync(assets, 0));
        _broadcasts.PublishToCharacter(
            shop.CharacterId,
            BuildAssetSync(assets, 1));
        _broadcasts.PublishToCharacter(
            shop.CharacterId,
            BuildAssetSync(assets, 2));
        _broadcasts.PublishToCharacter(
            shop.CharacterId,
            BuildAssetSync(assets, 3));
    }

    private static object Board(
        long characterId,
        bool isOpen,
        string shopName)
        => new
        {
            Type = "SyncShopBoard",
            EntityID = characterId,
            IsOpen = isOpen,
            ShopName = shopName
        };

    private static object Flash(string message)
        => new
        {
            Type = "SystemMessageFlash",
            Message = message
        };
}
