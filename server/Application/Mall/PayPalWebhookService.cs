namespace SGO.Server.Application.Mall;

public enum PayPalWebhookOutcome
{
    Applied,
    Duplicate,
    Rejected,
    Unavailable,
    SettlementFailed
}

public sealed record PayPalWebhookHandleResult(
    PayPalWebhookOutcome Outcome,
    string Message,
    long NewBalance = 0,
    long LifetimeDonation = 0)
{
    public bool Success =>
        Outcome is
            PayPalWebhookOutcome.Applied or
            PayPalWebhookOutcome.Duplicate;
}

public sealed class PayPalWebhookService
{
    private readonly IPayPalDonationProvider _provider;
    private readonly MallService _mall;

    public PayPalWebhookService(
        IPayPalDonationProvider provider,
        MallService mall)
    {
        _provider = provider;
        _mall = mall;
    }

    public async Task<PayPalWebhookHandleResult> HandleAsync(
        IReadOnlyDictionary<string, string> headers,
        string body,
        CancellationToken cancellationToken)
    {
        PayPalWebhookVerificationResult verified;
        try
        {
            verified =
                await _provider.VerifyWebhookAsync(
                    headers,
                    body,
                    cancellationToken);
        }
        catch
        {
            return new(
                PayPalWebhookOutcome.Unavailable,
                "PayPal webhook 驗證服務暫時無法使用");
        }

        if (!verified.ProviderAvailable)
        {
            return new(
                PayPalWebhookOutcome.Unavailable,
                verified.Message);
        }

        if (!verified.Verified)
        {
            return new(
                PayPalWebhookOutcome.Rejected,
                string.IsNullOrWhiteSpace(
                    verified.Message)
                    ? "PayPal webhook 驗證失敗"
                    : verified.Message);
        }

        if (verified.AccountId <= 0 ||
            verified.Amount is < 500 or > 50_000 ||
            string.IsNullOrWhiteSpace(
                verified.ExternalTransactionId) ||
            verified.ExternalTransactionId.Length > 200)
        {
            return new(
                PayPalWebhookOutcome.Rejected,
                "PayPal webhook 交易資料無效");
        }

        var settlement =
            _mall.SettlePayPalDonation(
                verified.AccountId,
                verified.ExternalTransactionId,
                verified.Amount);

        if (!settlement.Success)
        {
            return new(
                PayPalWebhookOutcome.SettlementFailed,
                settlement.Message,
                settlement.NewBalance,
                settlement.LifetimeDonation);
        }

        return new(
            settlement.AlreadyApplied
                ? PayPalWebhookOutcome.Duplicate
                : PayPalWebhookOutcome.Applied,
            settlement.Message,
            settlement.NewBalance,
            settlement.LifetimeDonation);
    }
}
