using System.Text.Json;
using SGO.Server.Application.Mall;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.SmokeTests.Fixtures;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class MallExternalIntegrationSmokeTests
{
    public static void Run()
    {
        using var temp =
            new TemporaryDirectoryFixture(
                "sgo-mall-external-");
        var store =
            new SqliteGameStore(
                Path.Combine(
                    temp.Root,
                    "game.db"));

        var username =
            "mall-" +
            Guid.NewGuid().ToString("N");
        const string password =
            "test-password";

        var registered =
            store.Register(
                username,
                password,
                "");
        Check(
            registered.Success,
            "register account for external mall settlement");

        var accountId =
            store.Login(
                username,
                password);
        Check(
            accountId.HasValue,
            "login account for external mall settlement");

        var payPal =
            new FakePayPalProvider();
        var legacy =
            new FakeLegacyPointProvider();

        var mall =
            new MallService(
                store,
                configuration: null,
                current: null,
                payPal: payPal,
                legacyPoints: legacy);

        var createOrder =
            mall.Handle(
                accountId!.Value,
                0,
                "CreatePayPalDonation",
                0,
                1,
                500,
                0,
                requestId:
                    "paypal-request-1");

        var createOrderJson =
            JsonSerializer.Serialize(
                createOrder);

        Check(
            payPal.LastAccountId ==
                accountId.Value &&
            payPal.LastAmount == 500 &&
            payPal.LastRequestId ==
                "paypal-request-1" &&
            createOrderJson.Contains(
                "\"Type\":\"PayPalDonationCreated\"") &&
            createOrderJson.Contains(
                "\"Success\":true") &&
            createOrderJson.Contains(
                "https://www.paypal.com/checkoutnow?token=test") &&
            store.GetMallPoints(
                accountId.Value) == 0 &&
            store.GetLifetimeDonation(
                accountId.Value) == 0,
            "PayPal order creation forwards request id but never credits before settlement");

        var firstSettlement =
            mall.SettlePayPalDonation(
                accountId.Value,
                "paypal-order-1",
                500);

        var repeatedSettlement =
            mall.SettlePayPalDonation(
                accountId.Value,
                "paypal-order-1",
                500);

        var conflictingSettlement =
            mall.SettlePayPalDonation(
                accountId.Value,
                "paypal-order-1",
                600);

        Check(
            firstSettlement.Success &&
            !firstSettlement.AlreadyApplied &&
            firstSettlement.NewBalance == 500 &&
            firstSettlement.LifetimeDonation == 500 &&
            repeatedSettlement.Success &&
            repeatedSettlement.AlreadyApplied &&
            repeatedSettlement.NewBalance == 500 &&
            repeatedSettlement.LifetimeDonation == 500 &&
            !conflictingSettlement.Success &&
            store.GetMallPoints(
                accountId.Value) == 500 &&
            store.GetLifetimeDonation(
                accountId.Value) == 500,
            "PayPal settlement is idempotent and conflicting transaction reuse is rejected");

        var query =
            mall.Handle(
                accountId.Value,
                0,
                "QueryLegacyPoints",
                0,
                1,
                0,
                0,
                legacyLoginName:
                    "legacy-user",
                legacyPassword:
                    "legacy-secret");

        var queryJson =
            JsonSerializer.Serialize(
                query);

        Check(
            legacy.LastLoginName ==
                "legacy-user" &&
            legacy.LastPassword ==
                "legacy-secret" &&
            queryJson.Contains(
                "\"Operation\":\"Query\"") &&
            queryJson.Contains(
                "\"LegacyPoints\":1000"),
            "legacy query forwards exact credentials to provider");

        var withdraw =
            mall.Handle(
                accountId.Value,
                0,
                "WithdrawLegacyPoints",
                0,
                1,
                200,
                0,
                legacyLoginName:
                    "legacy-user",
                legacyPassword:
                    "legacy-secret",
                requestId:
                    "legacy-request-1");

        var withdrawJson =
            JsonSerializer.Serialize(
                withdraw);

        Check(
            legacy.LastLoginName ==
                "legacy-user" &&
            legacy.LastPassword ==
                "legacy-secret" &&
            legacy.LastAmount == 200 &&
            legacy.LastRequestId ==
                "legacy-request-1" &&
            withdrawJson.Contains(
                "\"Operation\":\"Withdraw\"") &&
            withdrawJson.Contains(
                "\"Success\":true") &&
            withdrawJson.Contains(
                "\"Type\":\"MallPointsSync\"") &&
            withdrawJson.Contains(
                "\"Points\":700") &&
            store.GetMallPoints(
                accountId.Value) == 700 &&
            store.GetLifetimeDonation(
                accountId.Value) == 500,
            "legacy withdrawal credits current points without counting as donation");

        var repeatedWithdraw =
            mall.Handle(
                accountId.Value,
                0,
                "WithdrawLegacyPoints",
                0,
                1,
                200,
                0,
                legacyLoginName:
                    "legacy-user",
                legacyPassword:
                    "legacy-secret",
                requestId:
                    "legacy-request-1");

        var repeatedJson =
            JsonSerializer.Serialize(
                repeatedWithdraw);

        Check(
            repeatedJson.Contains(
                "\"Success\":true") &&
            repeatedJson.Contains(
                "未重複入帳") &&
            store.GetMallPoints(
                accountId.Value) == 700 &&
            store.GetLifetimeDonation(
                accountId.Value) == 500,
            "replayed legacy withdrawal with same external transaction does not double credit");

        var nullProviders =
            new MallService(store);

        var blockedPayPal =
            JsonSerializer.Serialize(
                nullProviders.Handle(
                    accountId.Value,
                    0,
                    "CreatePayPalDonation",
                    0,
                    1,
                    500,
                    0,
                    requestId:
                        "blocked-paypal"));

        var blockedLegacy =
            JsonSerializer.Serialize(
                nullProviders.Handle(
                    accountId.Value,
                    0,
                    "QueryLegacyPoints",
                    0,
                    1,
                    0,
                    0,
                    legacyLoginName:
                        "legacy-user",
                    legacyPassword:
                        "legacy-secret"));

        Check(
            blockedPayPal.Contains(
                "\"Success\":false") &&
            blockedLegacy.Contains(
                "\"Success\":false"),
            "null external providers fail closed without fake orders or balances");
    }

    private sealed class FakePayPalProvider :
        IPayPalDonationProvider
    {
        public long LastAccountId { get; private set; }
        public int LastAmount { get; private set; }
        public string LastRequestId { get; private set; } = "";

        public PayPalDonationOrderResult CreateOrder(
            long accountId,
            int amount,
            string requestId)
        {
            LastAccountId = accountId;
            LastAmount = amount;
            LastRequestId = requestId;

            return new(
                true,
                "PayPal 測試訂單已建立",
                "https://www.paypal.com/checkoutnow?token=test",
                "paypal-order-1");
        }
    }

    private sealed class FakeLegacyPointProvider :
        ILegacyPointProvider
    {
        public string LastLoginName { get; private set; } = "";
        public string LastPassword { get; private set; } = "";
        public int LastAmount { get; private set; }
        public string LastRequestId { get; private set; } = "";

        public LegacyPointQueryResult Query(
            string loginName,
            string password)
        {
            LastLoginName = loginName;
            LastPassword = password;

            return new(
                true,
                "查詢成功",
                1000);
        }

        public LegacyPointWithdrawalResult Withdraw(
            string loginName,
            string password,
            int amount,
            string requestId)
        {
            LastLoginName = loginName;
            LastPassword = password;
            LastAmount = amount;
            LastRequestId = requestId;

            return new(
                true,
                "提領成功",
                800,
                amount,
                "legacy-tx-1");
        }
    }
}
