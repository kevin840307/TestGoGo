using System.Text.Json;
using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.SmokeTests.Fixtures;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

/// <summary>
/// Persistence guardrails for malformed NPC shop requests. Public game
/// handlers validate these too, but a future handler must not accidentally
/// turn an invalid quantity into a free item or an involuntary sale.
/// </summary>
internal static class EconomyRequestGuardSmokeTests
{
    public static void Run()
    {
        using var temp = new TemporaryDirectoryFixture(
            "sgo-economy-request-guards-");
        var store = new SqliteGameStore(Path.Combine(
            temp.Root, "game.db"));

        var username = $"guard_{Guid.NewGuid():N}";
        const string password = "test-password";
        Check(store.Register(username, password, "").Success,
            "register economy request-guard account");
        var accountId = store.Login(username, password);
        Check(accountId.HasValue,
            "login economy request-guard account");

        using var create = JsonSerializer.SerializeToDocument(new
        {
            Name = "GuardTester",
            Password = "1234",
            Race = 0,
            Hair = 1,
            HairColor = 1,
            Str = 5,
            Dex = 5,
            Con = 5,
            Wis = 5
        });
        Check(store.Create(accountId!.Value, create.RootElement).Success,
            "create economy request-guard character");
        var characterId = store.Characters(accountId.Value)
            .Select(x => Convert.ToInt64(x["DBID"]))
            .First(x => x > 0);

        ExactMoneyFixture.Set(store, accountId.Value, characterId, 100,
            "malformed economy request tests");

        const int itemIndex = 999_990;
        int InventoryCount() => store.GetCharacterAssets(
                accountId.Value, characterId)
            .Inventory
            .Where(x => Convert.ToInt32(x["ItemIndex"]) == itemIndex)
            .Sum(x => Convert.ToInt32(x["Count"]));
        long Money() => Convert.ToInt64(store.GetCharacter(
            accountId.Value, characterId)!["Money"]);

        foreach (var invalid in new[]
        {
            (Count: 0, UnitPrice: 25),
            (Count: -1, UnitPrice: 25),
            (Count: 1, UnitPrice: -25),
            (Count: int.MinValue, UnitPrice: int.MinValue)
        })
        {
            var result = store.BuyAsset(
                accountId.Value, characterId,
                AssetPurchaseKind.General,
                itemIndex, invalid.Count, invalid.UnitPrice);
            Check(!result.Success &&
                  Money() == 100 &&
                  InventoryCount() == 0,
                $"malformed NPC purchase ({invalid.Count},{invalid.UnitPrice}) leaves money and items unchanged");
        }

        // Previously int * int in checked context could throw before a
        // failed-funds response. It must never create a huge asset stack.
        var huge = store.BuyAsset(
            accountId.Value, characterId,
            AssetPurchaseKind.General,
            itemIndex, int.MaxValue, int.MaxValue);
        Check(!huge.Success &&
              Money() == 100 &&
              InventoryCount() == 0,
            "oversized quantity/price cannot overflow purchase or mint items");

        var bought = store.BuyAsset(
            accountId.Value, characterId,
            AssetPurchaseKind.General,
            itemIndex, 1, 25);
        Check(bought.Success &&
              Money() == 75 &&
              InventoryCount() == 1,
            "valid NPC purchase still transfers one paid item");

        var negativeSale = store.SellAsset(
            accountId.Value, characterId,
            bought.CreatedUid,
            AssetPurchaseKind.General,
            0, 9, out _);
        var freeSale = store.SellAsset(
            accountId.Value, characterId,
            bought.CreatedUid,
            AssetPurchaseKind.General,
            1, -9, out _);
        Check(!negativeSale && !freeSale &&
              Money() == 75 &&
              InventoryCount() == 1,
            "invalid sell count or negative price cannot consume an owned item");

        Check(store.SellAsset(
                accountId.Value, characterId,
                bought.CreatedUid,
                AssetPurchaseKind.General,
                1, 9, out var moneyAfter) &&
              moneyAfter == 84 &&
              Money() == 84 &&
              InventoryCount() == 0,
            "valid NPC sale credits exactly once");

        Check(!store.SellAsset(
                accountId.Value, characterId,
                bought.CreatedUid,
                AssetPurchaseKind.General,
                1, 9, out _) &&
              Money() == 84 &&
              InventoryCount() == 0,
            "repeated sale of consumed UID never mints money");

        Console.WriteLine("Economy request guard smoke passed.");
    }
}
