using SGO.Server.Application.Abstractions;

namespace SGO.Server.Application.Mall;

public sealed class MallService
{
    private readonly IMallStore _store;

    public MallService(IMallStore store)
    {
        _store = store;
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
                return
                [
                    new
                    {
                        Type = "SystemMessageFlash",
                        Message = mallId > 0
                            ? "商城商品資料尚未設定，已拒絕扣點"
                            : "商城商品編號無效"
                    },
                    new
                    {
                        Type = "MallPointsSync",
                        Points = _store.GetMallPoints(accountId)
                    }
                ];

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

    private object BuildMallList(long accountId)
        => new
        {
            Type = "MallListResponse",
            Points = _store.GetMallPoints(accountId),
            Items = Array.Empty<object>(),
            Gifts = Array.Empty<object>()
        };
}
