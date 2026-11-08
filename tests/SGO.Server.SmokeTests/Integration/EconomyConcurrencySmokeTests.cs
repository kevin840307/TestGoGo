using System.Text.Json;
using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.SmokeTests.Fixtures;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class EconomyConcurrencySmokeTests
{
    private sealed record Player(
        long AccountId,
        long CharacterId);

    public static void Run()
    {
        using var temp =
            new TemporaryDirectoryFixture("sgo-economy-concurrency-");
        var store = new SqliteGameStore(
            Path.Combine(temp.Root, "game.db"));

        ConcurrentNpcPurchasesNeverOverspend(store);
        ConcurrentSellPaysOnce(store);
        ConcurrentBankTransferConservesStack(store);
        ConcurrentPlayerShopPurchaseConservesAssetsAndMoney(store);
        ConcurrentDirectTransferConservesMoney(store);
        ConcurrentBidirectionalTransfersConserveMoney(store);
    }

    private static void ConcurrentNpcPurchasesNeverOverspend(
        SqliteGameStore store)
    {
        var player = CreatePlayer(store, "npcbuyer");
        Check(
            store.AdjustMoney(
                player.AccountId,
                player.CharacterId,
                100) is not null,
            "seed NPC-shop concurrency money");

        const int itemIndex = 999_920;
        var attempts = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() =>
                store.BuyAsset(
                    player.AccountId,
                    player.CharacterId,
                    AssetPurchaseKind.General,
                    itemIndex,
                    1,
                    30)))
            .ToArray();

        Task.WaitAll(attempts);

        Check(
            attempts.Count(x => x.Result.Success) == 3 &&
            Money(store, player) == 10 &&
            CountInventory(store, player, itemIndex) == 3,
            "ten concurrent NPC purchases cannot overspend or mint items");
    }

    private static void ConcurrentSellPaysOnce(
        SqliteGameStore store)
    {
        var player = CreatePlayer(store, "seller");
        const int itemIndex = 999_921;
        var uid = store.AddInventoryItem(
            player.AccountId,
            player.CharacterId,
            itemIndex,
            1);
        Check(uid > 0, "seed one item for concurrent sell");

        var attempts = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() =>
            {
                var success = store.SellAsset(
                    player.AccountId,
                    player.CharacterId,
                    uid,
                    AssetPurchaseKind.General,
                    1,
                    17,
                    out long _);
                return success;
            }))
            .ToArray();

        Task.WaitAll(attempts);

        Check(
            attempts.Count(x => x.Result) == 1 &&
            Money(store, player) == 17 &&
            CountInventory(store, player, itemIndex) == 0,
            "ten concurrent sells of one UID pay exactly once");
    }

    private static void ConcurrentBankTransferConservesStack(
        SqliteGameStore store)
    {
        var player = CreatePlayer(store, "banker");
        const int itemIndex = 999_922;
        var uid = store.AddInventoryItem(
            player.AccountId,
            player.CharacterId,
            itemIndex,
            10);
        Check(uid > 0, "seed bank concurrency stack");

        var attempts = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() =>
                store.BankTransfer(
                    player.AccountId,
                    player.CharacterId,
                    uid,
                    0,
                    1,
                    true)))
            .ToArray();

        Task.WaitAll(attempts);

        var assets = store.GetCharacterAssets(
            player.AccountId,
            player.CharacterId);
        var outside = assets.Inventory
            .Where(x =>
                Convert.ToInt32(x["ItemIndex"]) == itemIndex &&
                Convert.ToInt32(x["InBank"]) == 0)
            .Sum(x => Convert.ToInt32(x["Count"]));
        var banked = assets.Inventory
            .Where(x =>
                Convert.ToInt32(x["ItemIndex"]) == itemIndex &&
                Convert.ToInt32(x["InBank"]) == 1)
            .Sum(x => Convert.ToInt32(x["Count"]));

        Check(
            attempts.Count(x => x.Result.Success) == 10 &&
            outside == 0 &&
            banked == 10,
            "ten concurrent bank moves conserve the complete stack");
    }

    private static void ConcurrentPlayerShopPurchaseConservesAssetsAndMoney(
        SqliteGameStore store)
    {
        var seller = CreatePlayer(store, "pseller");
        var buyers = Enumerable.Range(0, 10)
            .Select(i => CreatePlayer(
                store,
                $"pbuyer{i:D2}"))
            .ToArray();

        foreach (var buyer in buyers)
        {
            Check(
                store.AdjustMoney(
                    buyer.AccountId,
                    buyer.CharacterId,
                    100) is not null,
                "seed player-shop buyer money");
        }

        const int itemIndex = 999_923;
        var uid = store.AddInventoryItem(
            seller.AccountId,
            seller.CharacterId,
            itemIndex,
            5);
        Check(uid > 0, "seed player-shop stack");

        var reserved = store.SetPlayerShopAssetState(
            seller.AccountId,
            seller.CharacterId,
            uid,
            0,
            5,
            true);
        Check(reserved.Success, "reserve one player-shop stack");

        var attempts = buyers
            .Select(buyer => Task.Run(() =>
                store.BuyPlayerShopAsset(
                    buyer.AccountId,
                    buyer.CharacterId,
                    seller.AccountId,
                    seller.CharacterId,
                    reserved.ReservedUid,
                    0,
                    1,
                    20)))
            .ToArray();

        Task.WaitAll(attempts);

        var buyerMoney = buyers.Sum(x => Money(store, x));
        var buyerCopies = buyers.Sum(x =>
            CountInventory(store, x, itemIndex));

        Check(
            attempts.Count(x => x.Result.Success) == 5 &&
            buyerCopies == 5 &&
            CountInventory(store, seller, itemIndex) == 0,
            "ten buyers racing one five-count player-shop listing receive exactly five items");
        Check(
            Money(store, seller) == 100 &&
            buyerMoney == 900 &&
            Money(store, seller) + buyerMoney == 1000,
            "concurrent player-shop purchase conserves total money");
    }

    private static void ConcurrentDirectTransferConservesMoney(
        SqliteGameStore store)
    {
        var sender = CreatePlayer(store, "sender");
        var receiver = CreatePlayer(store, "receiver");
        Check(
            store.AdjustMoney(
                sender.AccountId,
                sender.CharacterId,
                100) is not null,
            "seed direct-transfer money");

        var attempts = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() =>
                store.TransferMoney(
                    sender.AccountId,
                    sender.CharacterId,
                    receiver.CharacterId,
                    15)))
            .ToArray();

        Task.WaitAll(attempts);

        var senderMoney = Money(store, sender);
        var receiverMoney = Money(store, receiver);
        Check(
            attempts.Count(x => x.Result.Success) == 6 &&
            senderMoney == 10 &&
            receiverMoney == 90 &&
            senderMoney + receiverMoney == 100,
            "ten concurrent direct transfers never overspend and conserve money");
    }

    // Concurrent transfers in BOTH directions exercise lock ordering and
    // cross-account atomicity; a one-way overspend test cannot catch deadlocks.
    private static void ConcurrentBidirectionalTransfersConserveMoney(
        SqliteGameStore store)
    {
        var first = CreatePlayer(store, "cross_a");
        var second = CreatePlayer(store, "cross_b");
        Check(
            store.AdjustMoney(first.AccountId, first.CharacterId, 1000) is not null &&
            store.AdjustMoney(second.AccountId, second.CharacterId, 1000) is not null,
            "seed both accounts for bidirectional-transfer race");

        using var ready = new CountdownEvent(20);
        using var start = new ManualResetEventSlim(false);

        Task<bool> Send(Player from, Player to, int amount) =>
            Task.Run(() =>
            {
                ready.Signal();
                if (!start.Wait(TimeSpan.FromSeconds(20)))
                    throw new TimeoutException("bidirectional transfer start barrier timed out");
                return store.TransferMoney(
                    from.AccountId,
                    from.CharacterId,
                    to.CharacterId,
                    amount).Success;
            });

        var attempts = Enumerable.Range(0, 10)
            .SelectMany(_ => new[]
            {
                Send(first, second, 7),
                Send(second, first, 11)
            })
            .ToArray();

        try
        {
            Check(
                ready.Wait(TimeSpan.FromSeconds(20)),
                "all bidirectional-transfer contenders reached start barrier");
        }
        finally
        {
            start.Set();
        }

        Check(
            Task.WaitAll(attempts, TimeSpan.FromSeconds(30)),
            "twenty opposite-direction transfers terminate without deadlock");

        Check(
            attempts.All(x => x.Result),
            "all fully funded opposite-direction transfers succeed");

        var firstMoney = Money(store, first);
        var secondMoney = Money(store, second);
        Check(
            firstMoney == 1040 &&
            secondMoney == 960 &&
            firstMoney + secondMoney == 2000,
            "opposite-direction transfers preserve both balances and total money");
    }

    private static Player CreatePlayer(
        SqliteGameStore store,
        string suffix)
    {
        var username = $"eco_{suffix}";
        const string password = "test-password";
        var registered = store.Register(username, password, "");
        Check(registered.Success, $"register economy player {suffix}");

        var accountId = store.Login(username, password);
        Check(accountId.HasValue, $"login economy player {suffix}");

        var characterName = $"E{suffix}";
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
        Check(created.Success, $"create economy character {suffix}");

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

    private static int CountInventory(
        SqliteGameStore store,
        Player player,
        int itemIndex)
        => store.GetCharacterAssets(
                player.AccountId,
                player.CharacterId)
            .Inventory
            .Where(x =>
                Convert.ToInt32(x["ItemIndex"]) == itemIndex)
            .Sum(x =>
                Convert.ToInt32(x["Count"]));
}
