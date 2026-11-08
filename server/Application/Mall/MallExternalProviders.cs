namespace SGO.Server.Application.Mall;

public sealed record PayPalDonationOrderResult(
    bool Success,
    string Message,
    string ApprovalUrl = "",
    string ExternalOrderId = "");

public interface IPayPalDonationProvider
{
    PayPalDonationOrderResult CreateOrder(
        long accountId,
        int amount,
        string requestId);
}

public sealed class NullPayPalDonationProvider :
    IPayPalDonationProvider
{
    public PayPalDonationOrderResult CreateOrder(
        long accountId,
        int amount,
        string requestId)
        => new(
            false,
            "伺服器尚未設定 PayPal 金流，未建立任何付款訂單");
}

public sealed record LegacyPointQueryResult(
    bool Success,
    string Message,
    long LegacyPoints);

public sealed record LegacyPointWithdrawalResult(
    bool Success,
    string Message,
    long LegacyPoints,
    long TransferredPoints = 0,
    string ExternalTransactionId = "");

public interface ILegacyPointProvider
{
    LegacyPointQueryResult Query(
        string loginName,
        string password);

    LegacyPointWithdrawalResult Withdraw(
        string loginName,
        string password,
        int amount,
        string requestId);
}

public sealed class NullLegacyPointProvider :
    ILegacyPointProvider
{
    public LegacyPointQueryResult Query(
        string loginName,
        string password)
        => new(
            false,
            "尚未設定舊服點數資料來源，未進行任何點數查詢",
            0);

    public LegacyPointWithdrawalResult Withdraw(
        string loginName,
        string password,
        int amount,
        string requestId)
        => new(
            false,
            "尚未設定舊服點數資料來源，未進行任何點數移轉",
            0);
}
