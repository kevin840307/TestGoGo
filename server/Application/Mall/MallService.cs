using Microsoft.Extensions.Configuration;
using SGO.Server.Application.Abstractions;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Mall;

public sealed class MallService
{
    private readonly IMallStore _store;
    private readonly IConfiguration? _configuration;
    private readonly CurrentClientDataCatalog? _current;

    public MallService(
        IMallStore store,
        IConfiguration? configuration = null,
        CurrentClientDataCatalog? current = null)
    {
        _store = store;
        _configuration = configuration;
        _current = current;
    }

    public IReadOnlyList<object> Handle(
        long accountId,
        long dbid,
        string action,
        int mallId,
        int count,
        int amount,
        int rewardId)
    {
        action = action.Trim();

        switch (action)
        {
            case "OpenMall":
                return [BuildMallList(accountId)];

            case "BuyItem":
                return BuyItem(
                    accountId,
                    dbid,
                    mallId,
                    count);

            case "CreatePayPalDonation":
                return [new
                {
                    Type = "PayPalDonationCreated",
                    Success = false,
                    Message = amount is >= 500 and <= 50_000
                        ? "伺服器尚未設定 PayPal 金流，未建立任何付款訂單"
                        : "贊助金額必須介於 500 至 50000",
                    ApprovalUrl = ""
                }];

            case "QueryLegacyPoints":
            case "WithdrawLegacyPoints":
                return [new
                {
                    Type = "LegacyPointWithdrawalResponse",
                    Success = false,
                    Operation = action == "QueryLegacyPoints"
                        ? "Query"
                        : "Withdraw",
                    Message = "尚未設定舊服點數資料來源，未進行任何點數移轉",
                    LegacyPoints = 0
                }];

            case "ClaimDonationGift":
                return
                [
                    new
                    {
                        Type = "SystemMessageFlash",
                        Message = rewardId > 0
                            ? "目前沒有可領取的贊助滿額禮"
                            : "贊助獎勵編號無效"
                    },
                    BuildMallList(accountId)
                ];

            default:
                return [new
                {
                    Type = "SystemMessageFlash",
                    Message = "不支援的商城操作"
                }];
        }
    }

    private IReadOnlyList<object> BuyItem(
        long accountId,
        long dbid,
        int mallId,
        int quantity)
    {
        var product = MallCatalog.Read(
                _configuration,
                _current)
            .FirstOrDefault(x =>
                x.MallId == mallId &&
                x.IsActive);

        if (product is null)
        {
            return
            [
                Flash(mallId > 0
                    ? "商城商品不存在或已下架"
                    : "商城商品編號無效"),
                Points(accountId)
            ];
        }

        if (quantity <= 0 ||
            quantity > product.MaxQuantity)
        {
            return
            [
                Flash(
                    $"單次購買數量必須介於 1 至 {product.MaxQuantity}"),
                Points(accountId)
            ];
        }

        long totalPrice;
        int assetCount;
        try
        {
            totalPrice = checked(
                product.Price * quantity);
            assetCount = checked(
                product.Count * quantity);
        }
        catch (OverflowException)
        {
            return
            [
                Flash("商城購買數量過大"),
                Points(accountId)
            ];
        }

        if (assetCount <= 0 ||
            assetCount > 999)
        {
            return
            [
                Flash("商城商品數量超過單次發放上限"),
                Points(accountId)
            ];
        }

        var result = _store.PurchaseMallAsset(
            accountId,
            dbid,
            product.Kind,
            product.ItemIndex,
            assetCount,
            totalPrice,
            product.AssetName,
            product.HorseType,
            product.InitialEnergy,
            product.ItemPart,
            product.ItemDamage);

        var packets = new List<object>
        {
            Flash(
                result.Success
                    ? $"已購買 {product.Name} x {assetCount}"
                    : result.Message),
            new
            {
                Type = "MallPointsSync",
                Points = result.NewBalance
            }
        };

        if (!result.Success)
            return packets;

        var assets = _store.GetCharacterAssets(
            accountId,
            dbid);

        packets.Add(new
        {
            Type = "InventorySync",
            Items = assets.Inventory,
            MaxSize = assets.InventoryMaxSize
        });
        packets.Add(new
        {
            Type = "EquipSync",
            Equips = assets.Equips,
            MaxSize = assets.EquipMaxSize
        });
        packets.Add(new
        {
            Type = "PetSync",
            Pets = assets.Pets,
            MaxSize = assets.PetMaxSize
        });
        packets.Add(new
        {
            Type = "HorseSync",
            Horses = assets.Horses,
            HorseEquips = assets.HorseEquips,
            MaxHorseEquipSize = assets.HorseEquipMaxSize
        });

        return packets;
    }

    private object BuildMallList(long accountId)
        => new
        {
            Type = "MallListResponse",
            Points = _store.GetMallPoints(accountId),
            Items = MallCatalog.Read(
                    _configuration,
                    _current)
                .Select(item => new
                {
                    MallID = item.MallId,
                    ItemIndex = item.ItemIndex,
                    Name = item.Name,
                    Category = item.Category,
                    Price = item.Price,
                    OriginalPrice = item.OriginalPrice,
                    Tag = item.Tag,
                    Count = item.Count,
                    IsActive = item.IsActive
                })
                .Cast<object>()
                .ToArray(),
            Gifts = Array.Empty<object>()
        };

    private object Points(long accountId)
        => new
        {
            Type = "MallPointsSync",
            Points = _store.GetMallPoints(accountId)
        };

    private static object Flash(string message)
        => new
        {
            Type = "SystemMessageFlash",
            Message = message
        };
}
