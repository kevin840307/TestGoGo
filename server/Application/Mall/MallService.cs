using Microsoft.Extensions.Configuration;
using SGO.Server.Application.Abstractions;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Mall;

public sealed class MallService
{
    private readonly IMallStore _store;
    private readonly IConfiguration? _configuration;
    private readonly CurrentClientDataCatalog? _current;
    private readonly IPayPalDonationProvider _payPal;
    private readonly ILegacyPointProvider _legacyPoints;

    public MallService(
        IMallStore store,
        IConfiguration? configuration = null,
        CurrentClientDataCatalog? current = null,
        IPayPalDonationProvider? payPal = null,
        ILegacyPointProvider? legacyPoints = null)
    {
        _store = store;
        _configuration = configuration;
        _current = current;
        _payPal =
            payPal ??
            new NullPayPalDonationProvider();
        _legacyPoints =
            legacyPoints ??
            new NullLegacyPointProvider();
    }

    public IReadOnlyList<object> Handle(
        long accountId,
        long dbid,
        string action,
        int mallId,
        int count,
        int amount,
        int rewardId,
        string legacyLoginName = "",
        string legacyPassword = "",
        string requestId = "")
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
                return CreatePayPalDonation(
                    accountId,
                    amount,
                    requestId);

            case "QueryLegacyPoints":
                return QueryLegacyPoints(
                    legacyLoginName,
                    legacyPassword);

            case "WithdrawLegacyPoints":
                return WithdrawLegacyPoints(
                    accountId,
                    legacyLoginName,
                    legacyPassword,
                    amount,
                    requestId);

            case "ClaimDonationGift":
                return ClaimDonationGift(
                    accountId,
                    dbid,
                    rewardId);

            default:
                return [new
                {
                    Type = "SystemMessageFlash",
                    Message = "不支援的商城操作"
                }];
        }
    }

    public ExternalMallCreditResult SettlePayPalDonation(
        long accountId,
        string externalTransactionId,
        long amount)
        => _store.CreditExternalMallPoints(
            accountId,
            "paypal",
            externalTransactionId,
            amount,
            amount);

    private IReadOnlyList<object> CreatePayPalDonation(
        long accountId,
        int amount,
        string requestId)
    {
        if (amount is < 500 or > 50_000)
        {
            return [new
            {
                Type = "PayPalDonationCreated",
                Success = false,
                Message = "贊助金額必須介於 500 至 50000",
                ApprovalUrl = ""
            }];
        }

        requestId = NormalizeRequestId(
            requestId,
            "paypal",
            accountId);

        var result = _payPal.CreateOrder(
            accountId,
            amount,
            requestId);

        return [new
        {
            Type = "PayPalDonationCreated",
            Success = result.Success,
            Message = result.Message,
            ApprovalUrl = result.Success
                ? result.ApprovalUrl
                : ""
        }];
    }

    private IReadOnlyList<object> QueryLegacyPoints(
        string loginName,
        string password)
    {
        var result = _legacyPoints.Query(
            loginName,
            password);

        return [new
        {
            Type = "LegacyPointWithdrawalResponse",
            Success = result.Success,
            Operation = "Query",
            Message = result.Message,
            LegacyPoints = Math.Max(
                0,
                result.LegacyPoints)
        }];
    }

    private IReadOnlyList<object> WithdrawLegacyPoints(
        long accountId,
        string loginName,
        string password,
        int amount,
        string requestId)
    {
        if (amount <= 0)
        {
            return [new
            {
                Type = "LegacyPointWithdrawalResponse",
                Success = false,
                Operation = "Withdraw",
                Message = "提領點數必須大於 0",
                LegacyPoints = 0
            }];
        }

        requestId = NormalizeRequestId(
            requestId,
            "legacy",
            accountId);

        var external = _legacyPoints.Withdraw(
            loginName,
            password,
            amount,
            requestId);

        if (!external.Success)
        {
            return [new
            {
                Type = "LegacyPointWithdrawalResponse",
                Success = false,
                Operation = "Withdraw",
                Message = external.Message,
                LegacyPoints = Math.Max(
                    0,
                    external.LegacyPoints)
            }];
        }

        if (external.TransferredPoints <= 0 ||
            string.IsNullOrWhiteSpace(
                external.ExternalTransactionId))
        {
            return [new
            {
                Type = "LegacyPointWithdrawalResponse",
                Success = false,
                Operation = "Withdraw",
                Message = "舊服點數來源回傳的交易資料無效，未進行本服入帳",
                LegacyPoints = Math.Max(
                    0,
                    external.LegacyPoints)
            }];
        }

        var credit = _store.CreditExternalMallPoints(
            accountId,
            "legacy",
            external.ExternalTransactionId,
            external.TransferredPoints,
            0);

        if (!credit.Success)
        {
            return [new
            {
                Type = "LegacyPointWithdrawalResponse",
                Success = false,
                Operation = "Withdraw",
                Message =
                    $"{external.Message}；本服入帳失敗：{credit.Message}",
                LegacyPoints = Math.Max(
                    0,
                    external.LegacyPoints)
            }];
        }

        return
        [
            new
            {
                Type = "LegacyPointWithdrawalResponse",
                Success = true,
                Operation = "Withdraw",
                Message = credit.AlreadyApplied
                    ? "此提領交易已完成，未重複入帳"
                    : external.Message,
                LegacyPoints = Math.Max(
                    0,
                    external.LegacyPoints)
            },
            new
            {
                Type = "MallPointsSync",
                Points = credit.NewBalance
            }
        ];
    }

    private static string NormalizeRequestId(
        string requestId,
        string prefix,
        long accountId)
    {
        requestId = requestId.Trim();
        if (!string.IsNullOrWhiteSpace(requestId) &&
            requestId.Length <= 200)
            return requestId;

        return
            $"{prefix}-{accountId}-{Guid.NewGuid():N}";
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

    private IReadOnlyList<object> ClaimDonationGift(
        long accountId,
        long dbid,
        int rewardId)
    {
        var gift = MallGiftCatalog.Read(
                _configuration,
                _current)
            .FirstOrDefault(x =>
                x.RewardId == rewardId &&
                x.IsActive);

        if (gift is null)
        {
            return
            [
                Flash(rewardId > 0
                    ? "贊助滿額禮不存在或已下架"
                    : "贊助獎勵編號無效"),
                BuildMallList(accountId)
            ];
        }

        var result = _store.ClaimMallGift(
            accountId,
            dbid,
            gift.RewardId,
            gift.RequiredDonation,
            gift.Kind,
            gift.ItemIndex,
            gift.Count,
            gift.AssetName,
            gift.HorseType,
            gift.InitialEnergy,
            gift.ItemPart,
            gift.ItemDamage);

        var packets = new List<object>
        {
            Flash(
                result.Success
                    ? $"已領取滿額禮：{gift.Name} x {gift.Count}"
                    : result.Message)
        };

        if (result.Success)
        {
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
        }

        packets.Add(BuildMallList(accountId));
        return packets;
    }

    private object BuildMallList(long accountId)
    {
        var lifetimeDonation =
            _store.GetLifetimeDonation(accountId);
        var claimed =
            _store.GetClaimedMallGiftIds(accountId);

        var gifts = MallGiftCatalog.Read(
                _configuration,
                _current)
            .Where(gift =>
                gift.IsActive &&
                lifetimeDonation >= gift.RequiredDonation)
            .Select(gift => new
            {
                RewardID = gift.RewardId,
                ItemIndex = gift.ItemIndex,
                Name = gift.Name,
                Count = gift.Count,
                RequiredDonation = gift.RequiredDonation,
                ItemCategory = gift.ItemCategory,
                ClaimStatus = claimed.Contains(gift.RewardId)
                    ? "Claimed"
                    : "Unclaimed"
            })
            .Cast<object>()
            .ToArray();

        return new
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
            Gifts = gifts
        };
    }

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
