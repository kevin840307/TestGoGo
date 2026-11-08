using System.Text.Json;
using SGO.Server.Application.Abstractions;
using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.SmokeTests.Fixtures;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

/// <summary>
/// Races different gameplay features against the same durable player state.
/// Per-feature tests cannot detect snapshot/lock bugs caused by Trade, direct
/// transfer, shop and other money mutations interleaving.
/// </summary>
internal static class CrossFeatureConcurrencySmokeTests
{
    private sealed record Player(long AccountId, long CharacterId);

    public static void Run()
    {
        using var temp =
            new TemporaryDirectoryFixture("sgo-cross-feature-concurrency-");
        var store = new SqliteGameStore(
            Path.Combine(temp.Root, "game.db"));

        TradeAndDirectTransferConserveMoney(store);
        TradeAndPlayerShopPurchaseConserveMoney(store);
        CraftAndDirectTransferComposeAtomically(store);
    }

    private static void TradeAndDirectTransferConserveMoney(
        SqliteGameStore store)
    {
        var first = CreatePlayer(store, "trade_a");
        var second = CreatePlayer(store, "trade_b");
        var third = CreatePlayer(store, "sender_c");

        foreach (var player in new[] { first, second, third })
        {
            Check(
                store.AdjustMoney(
                    player.AccountId,
                    player.CharacterId,
                    100) is not null,
                "seed cross-feature concurrency money");
        }

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);

        var tradeTask = Task.Run(() =>
        {
            ready.Signal();
            if (!start.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("cross-feature trade barrier timed out");

            return store.CommitTrade(
                first.AccountId,
                first.CharacterId,
                30,
                Array.Empty<SGO.Server.Application.Abstractions.TradeAssetOffer>(),
                second.AccountId,
                second.CharacterId,
                20,
                Array.Empty<SGO.Server.Application.Abstractions.TradeAssetOffer>());
        });

        var transferTask = Task.Run(() =>
        {
            ready.Signal();
            if (!start.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("cross-feature transfer barrier timed out");

            return store.TransferMoney(
                third.AccountId,
                third.CharacterId,
                first.CharacterId,
                15);
        });

        try
        {
            Check(
                ready.Wait(TimeSpan.FromSeconds(10)),
                "trade and transfer reached the same start barrier");
        }
        finally
        {
            start.Set();
        }

        Check(
            Task.WaitAll(
                new Task[] { tradeTask, transferTask },
                TimeSpan.FromSeconds(20)),
            "trade plus direct transfer terminate without SQLite lock failure");

        Check(
            tradeTask.Result.Success && transferTask.Result.Success,
            "trade and direct transfer both commit exactly once");

        var firstMoney = Money(store, first);
        var secondMoney = Money(store, second);
        var thirdMoney = Money(store, third);

        Check(
            firstMoney == 105 &&
            secondMoney == 110 &&
            thirdMoney == 85,
            "trade and direct-transfer deltas compose regardless of commit order");

        Check(
            firstMoney + secondMoney + thirdMoney == 300,
            "mixed multiplayer money operations conserve total currency");
    }

    private static void TradeAndPlayerShopPurchaseConserveMoney(
        SqliteGameStore store)
    {
        var buyer = CreatePlayer(store, "shop_trade_a");
        var tradePeer = CreatePlayer(store, "shop_trade_b");
        var seller = CreatePlayer(store, "shop_seller");

        foreach (var player in new[] { buyer, tradePeer, seller })
        {
            Check(
                store.AdjustMoney(
                    player.AccountId,
                    player.CharacterId,
                    100) is not null,
                "seed trade plus player-shop concurrency money");
        }

        const int itemIndex = 999_950;
        var uid = store.AddInventoryItem(
            seller.AccountId,
            seller.CharacterId,
            itemIndex,
            1);
        Check(uid > 0, "seed player-shop item for mixed trade race");

        var reserved = store.SetPlayerShopAssetState(
            seller.AccountId,
            seller.CharacterId,
            uid,
            0,
            1,
            true);
        Check(reserved.Success, "reserve player-shop item for mixed trade race");

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);

        var tradeTask = Task.Run(() =>
        {
            ready.Signal();
            if (!start.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException(
                    "trade/player-shop trade barrier timed out");

            return store.CommitTrade(
                buyer.AccountId,
                buyer.CharacterId,
                30,
                Array.Empty<SGO.Server.Application.Abstractions.TradeAssetOffer>(),
                tradePeer.AccountId,
                tradePeer.CharacterId,
                20,
                Array.Empty<SGO.Server.Application.Abstractions.TradeAssetOffer>());
        });

        var shopTask = Task.Run(() =>
        {
            ready.Signal();
            if (!start.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException(
                    "trade/player-shop purchase barrier timed out");

            return store.BuyPlayerShopAsset(
                buyer.AccountId,
                buyer.CharacterId,
                seller.AccountId,
                seller.CharacterId,
                reserved.ReservedUid,
                0,
                1,
                15);
        });

        try
        {
            Check(
                ready.Wait(TimeSpan.FromSeconds(10)),
                "trade and player-shop purchase reached start barrier");
        }
        finally
        {
            start.Set();
        }

        Check(
            Task.WaitAll(
                new Task[] { tradeTask, shopTask },
                TimeSpan.FromSeconds(20)),
            "trade plus player-shop purchase terminate without SQLite lock failure");

        Check(
            tradeTask.Result.Success && shopTask.Result.Success,
            "trade and player-shop purchase both commit exactly once");

        var buyerMoney = Money(store, buyer);
        var peerMoney = Money(store, tradePeer);
        var sellerMoney = Money(store, seller);

        Check(
            buyerMoney == 75 &&
            peerMoney == 110 &&
            sellerMoney == 115,
            "trade and player-shop money deltas compose regardless of commit order");

        Check(
            buyerMoney + peerMoney + sellerMoney == 300,
            "trade plus player-shop purchase conserve total currency");

        var buyerCopies = store.GetCharacterAssets(
                buyer.AccountId,
                buyer.CharacterId)
            .Inventory
            .Where(x => Convert.ToInt32(x["ItemIndex"]) == itemIndex)
            .Sum(x => Convert.ToInt32(x["Count"]));

        var sellerCopies = store.GetCharacterAssets(
                seller.AccountId,
                seller.CharacterId)
            .Inventory
            .Where(x => Convert.ToInt32(x["ItemIndex"]) == itemIndex)
            .Sum(x => Convert.ToInt32(x["Count"]));

        Check(
            buyerCopies == 1 && sellerCopies == 0,
            "mixed trade/player-shop race transfers exactly one listed item");
    }

    private static void CraftAndDirectTransferComposeAtomically(
        SqliteGameStore store)
    {
        var crafter = CreatePlayer(store, "craft_a");
        var sender = CreatePlayer(store, "craft_b");

        Check(
            store.AdjustMoney(
                crafter.AccountId,
                crafter.CharacterId,
                100) is not null &&
            store.AdjustMoney(
                sender.AccountId,
                sender.CharacterId,
                100) is not null,
            "seed craft plus transfer concurrency money");

        const int ingredientItem = 999_960;
        const int resultItem = 999_961;
        var ingredientUid = store.AddInventoryItem(
            crafter.AccountId,
            crafter.CharacterId,
            ingredientItem,
            1);
        Check(
            ingredientUid > 0,
            "seed ingredient for craft plus transfer race");

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);

        var craftTask = Task.Run(() =>
        {
            ready.Signal();
            if (!start.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException(
                    "craft/transfer craft barrier timed out");

            return store.CommitCraft(
                crafter.AccountId,
                crafter.CharacterId,
                30,
                new[]
                {
                    new CraftIngredient(
                        ingredientItem,
                        1)
                },
                new[]
                {
                    new CraftGrant(
                        resultItem,
                        1,
                        AssetPurchaseKind.General,
                        255,
                        0)
                });
        });

        var transferTask = Task.Run(() =>
        {
            ready.Signal();
            if (!start.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException(
                    "craft/transfer money barrier timed out");

            return store.TransferMoney(
                sender.AccountId,
                sender.CharacterId,
                crafter.CharacterId,
                15);
        });

        try
        {
            Check(
                ready.Wait(TimeSpan.FromSeconds(10)),
                "craft and transfer reached the same start barrier");
        }
        finally
        {
            start.Set();
        }

        Check(
            Task.WaitAll(
                new Task[] { craftTask, transferTask },
                TimeSpan.FromSeconds(20)),
            "craft plus transfer terminate without partial durable state");

        Check(
            craftTask.Result.Success &&
            transferTask.Result.Success,
            "craft and direct transfer both commit exactly once");

        var crafterMoney = Money(store, crafter);
        var senderMoney = Money(store, sender);
        Check(
            crafterMoney == 85 &&
            senderMoney == 85 &&
            crafterMoney + senderMoney == 170,
            "craft cost and incoming transfer compose with exactly one 30-money sink");

        var assets = store.GetCharacterAssets(
            crafter.AccountId,
            crafter.CharacterId);
        var ingredientCount = assets.Inventory
            .Where(x =>
                Convert.ToInt32(x["ItemIndex"]) == ingredientItem)
            .Sum(x =>
                Convert.ToInt32(x["Count"]));
        var resultCount = assets.Inventory
            .Where(x =>
                Convert.ToInt32(x["ItemIndex"]) == resultItem)
            .Sum(x =>
                Convert.ToInt32(x["Count"]));

        Check(
            ingredientCount == 0 &&
            resultCount == 1,
            "craft plus transfer consumes one ingredient and grants one result");
    }

    private static Player CreatePlayer(
        SqliteGameStore store,
        string suffix)
    {
        var username = $"cross_{suffix}";
        const string password = "test-password";
        var registered = store.Register(username, password, "");
        Check(registered.Success, $"register cross-feature player {suffix}");

        var accountId = store.Login(username, password);
        Check(accountId.HasValue, $"login cross-feature player {suffix}");

        var characterName = $"X{suffix}";
        if (characterName.Length > 12)
            characterName = characterName[..12];

        using var create = Json(
            JsonSerializer.Serialize(new
            {
                Name = characterName,
                Password = "1234",
                Race = 0,
                Hair = 1,
                HairColor = 1,
                Str = 5,
                Dex = 5,
                Con = 5,
                Wis = 5
            }));

        var created = store.Create(accountId!.Value, create.RootElement);
        Check(created.Success, $"create cross-feature character {suffix}");

        var dbid = store.Characters(accountId.Value)
            .Select(x => Convert.ToInt64(x["DBID"]))
            .First(x => x > 0);

        return new Player(accountId.Value, dbid);
    }

    private static long Money(
        SqliteGameStore store,
        Player player)
        => Convert.ToInt64(
            store.GetCharacter(
                player.AccountId,
                player.CharacterId)!["Money"]);
}
