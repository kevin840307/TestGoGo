namespace SGO.Server.Infrastructure.GameData;

public enum ShopAssetKind
{
    General = 0,
    Equipment = 1,
    Horse = 3,
    HorseEquipment = 4,
    HorseBattery = 5
}

public sealed record NpcShopItem(
    int ShopId,
    int ItemIndex,
    int Price,
    int TabType,
    ShopAssetKind Kind,
    string Name);

public sealed class NpcShopCatalog
{
    private readonly Dictionary<int, IReadOnlyList<NpcShopItem>> _shops;

    public NpcShopCatalog()
    {
        _shops = new Dictionary<int, IReadOnlyList<NpcShopItem>>
        {
            [1] =
            [
                new(1, 10046, 50, 0, ShopAssetKind.General, "一般物品 10046"),
                new(1, 10060, 80, 0, ShopAssetKind.General, "一般物品 10060"),
                new(1, 10070, 120, 0, ShopAssetKind.General, "一般物品 10070")
            ],
            [2] =
            [
                new(2, 10046, 100, 0, ShopAssetKind.General, "測試商品 10046")
            ],
            [3] =
            [
                new(3, 10060, 100, 0, ShopAssetKind.General, "測試商品 10060")
            ],
            [4] =
            [
                new(4, 10070, 100, 0, ShopAssetKind.General, "測試商品 10070")
            ],
            [5] =
            [
                new(5, 10102, 150, 0, ShopAssetKind.General, "測試商品 10102")
            ],
            [6] =
            [
                new(6, 31004, 200, 0, ShopAssetKind.General, "材料 31004")
            ],
            [7] =
            [
                new(7, 31008, 200, 0, ShopAssetKind.General, "材料 31008")
            ],
            [8] =
            [
                new(8, 31012, 100, 0, ShopAssetKind.General, "材料 31012"),
                new(8, 31016, 100, 0, ShopAssetKind.General, "材料 31016"),
                new(8, 31020, 100, 0, ShopAssetKind.General, "材料 31020")
            ],
            [10] =
            [
                new(10, 6001, 1000, 3, ShopAssetKind.Horse, "機甲卡 6001"),
                new(10, 6201, 500, 4, ShopAssetKind.HorseEquipment, "機甲裝備 6201"),
                new(10, 6905, 100, 5, ShopAssetKind.HorseBattery, "魔靈電池")
            ]
        };
    }

    public IReadOnlyList<NpcShopItem> GetItems(int shopId)
        => _shops.TryGetValue(shopId, out var items)
            ? items
            : Array.Empty<NpcShopItem>();

    public NpcShopItem? Find(int shopId, int itemIndex)
        => GetItems(shopId).FirstOrDefault(x => x.ItemIndex == itemIndex);

    public IReadOnlyList<object> BuildClientItems(int shopId, int priceRate)
    {
        var rate = Math.Clamp(priceRate, 1, 1000);
        return GetItems(shopId)
            .Select(x => (object)new
            {
                ItemIndex = x.ItemIndex,
                Price = Math.Max(1, x.Price * rate / 100),
                TabType = x.TabType,
                Name = x.Name
            })
            .ToArray();
    }
}
