using System.Text.Json;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.Trade;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.SmokeTests.Fixtures;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class ConcurrencySmokeTests
{
    public static void Run(string repoRoot)
    {
        using var temp =
            new TemporaryDirectoryFixture("sgo-concurrency-");
        var store = new SqliteGameStore(
            Path.Combine(temp.Root, "game.db"));

        var accounts = new List<long>();
        var characters = new List<long>();

        for (var i = 0; i < 10; i++)
        {
            var username = $"concurrent{i:D2}";
            var password = "test-password";
            var registered = store.Register(
                username,
                password,
                "");
            Check(
                registered.Success,
                $"register concurrent player {i}");

            var accountId = store.Login(
                username,
                password);
            Check(
                accountId.HasValue,
                $"login concurrent player {i}");

            using var create = Json(
                JsonSerializer.Serialize(new
                {
                    Name = $"Concurrent{i:D2}",
                    Password = "1234",
                    Race = i % 2,
                    Hair = 1,
                    HairColor = 1,
                    Str = 5,
                    Dex = 5,
                    Con = 5,
                    Wis = 5
                }));

            var created = store.Create(
                accountId!.Value,
                create.RootElement);
            Check(
                created.Success,
                $"create concurrent character {i}");

            var dbid = store.Characters(accountId.Value)
                .Select(x => Convert.ToInt64(x["DBID"]))
                .First(x => x > 0);

            accounts.Add(accountId.Value);
            characters.Add(dbid);
        }

        ConcurrentGroundPickup(
            store,
            accounts,
            characters);

        ConcurrentTradeFinalConfirm(
            repoRoot,
            store,
            accounts,
            characters);
    }

    private static void ConcurrentGroundPickup(
        SqliteGameStore store,
        IReadOnlyList<long> accounts,
        IReadOnlyList<long> characters)
    {
        const int itemIndex = 999_901;
        var ground = store.CreateGroundItem(
            characters[0],
            0,
            itemIndex,
            1,
            15,
            1000,
            1000);

        Check(
            ground is not null,
            "create one shared ground item for concurrent pickup");

        var attempts = Enumerable.Range(0, 10)
            .Select(index => Task.Run(() =>
                store.PickUpGroundItem(
                    accounts[index],
                    characters[index],
                    ground!.GroundItemId)))
            .ToArray();

        Task.WaitAll(attempts);

        var results = attempts
            .Select(x => x.Result)
            .ToArray();

        Check(
            results.Count(x => x.Success) == 1,
            "exactly one of ten concurrent players claims the same ground item");

        var totalCopies = characters
            .Select((dbid, index) =>
                store.GetCharacterAssets(
                        accounts[index],
                        dbid)
                    .Inventory
                    .Where(x =>
                        Convert.ToInt32(x["ItemIndex"]) ==
                        itemIndex)
                    .Sum(x =>
                        Convert.ToInt32(x["Count"])))
            .Sum();

        Check(
            totalCopies == 1 &&
            !store.GetGroundItems(15).Any(x =>
                x.GroundItemId ==
                ground!.GroundItemId),
            "concurrent pickup conserves exactly one item and removes ground row");
    }

    private static void ConcurrentTradeFinalConfirm(
        string repoRoot,
        SqliteGameStore store,
        IReadOnlyList<long> accounts,
        IReadOnlyList<long> characters)
    {
        var catalog = new CurrentClientDataCatalog(
            Path.Combine(repoRoot, "client"));
        var broadcasts = new ServerBroadcastHub();
        var trade = new TradeService(
            store,
            catalog,
            broadcasts);

        var firstReg = broadcasts.Register();
        var secondReg = broadcasts.Register();

        var firstSession = new ClientSession
        {
            ConnectionId = firstReg.ConnectionId,
            AccountId = accounts[0],
            CharacterId = characters[0],
            CurrentMapId = 15
        };
        var secondSession = new ClientSession
        {
            ConnectionId = secondReg.ConnectionId,
            AccountId = accounts[1],
            CharacterId = characters[1],
            CurrentMapId = 15
        };

        broadcasts.UpdatePresence(
            firstReg.ConnectionId,
            15,
            characters[0],
            accounts[0],
            1000,
            1000);
        broadcasts.UpdatePresence(
            secondReg.ConnectionId,
            15,
            characters[1],
            accounts[1],
            1010,
            1000);

        const int tradeItemIndex = 4103;
        var itemUid = store.AddInventoryItem(
            accounts[0],
            characters[0],
            tradeItemIndex,
            2);
        Check(
            itemUid > 0,
            "seed tradable stack for concurrent final confirm");

        var firstBefore = CountInventory(
            store,
            accounts[0],
            characters[0],
            tradeItemIndex);
        var secondBefore = CountInventory(
            store,
            accounts[1],
            characters[1],
            tradeItemIndex);

        trade.Request(
            firstSession,
            accounts[0],
            characters[1],
            DateTimeOffset.UtcNow);
        trade.Reply(
            secondSession,
            accounts[1],
            characters[0],
            true,
            DateTimeOffset.UtcNow);

        using (var add = Json(
            JsonSerializer.Serialize(new
            {
                TabType = 0,
                UID = itemUid,
                Count = 1
            })))
        {
            trade.Action(
                firstSession,
                "TradeAddItem",
                add.RootElement);
        }

        using (var empty = Json("{}"))
        {
            trade.Action(
                firstSession,
                "TradeLock",
                empty.RootElement);
            trade.Action(
                secondSession,
                "TradeLock",
                empty.RootElement);
            trade.Action(
                firstSession,
                "TradeFinalConfirm",
                empty.RootElement);
        }

        while (firstReg.Queue.TryDequeue(out _)) { }
        while (secondReg.Queue.TryDequeue(out _)) { }

        var confirms = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() =>
            {
                using var request = Json("{}");
                trade.Action(
                    secondSession,
                    "TradeFinalConfirm",
                    request.RootElement);
            }))
            .ToArray();

        Task.WaitAll(confirms);

        var firstAfter = CountInventory(
            store,
            accounts[0],
            characters[0],
            tradeItemIndex);
        var secondAfter = CountInventory(
            store,
            accounts[1],
            characters[1],
            tradeItemIndex);

        var firstPackets = firstReg.Queue
            .Select(x => JsonSerializer.Serialize(x))
            .ToArray();
        var secondPackets = secondReg.Queue
            .Select(x => JsonSerializer.Serialize(x))
            .ToArray();

        Check(
            firstAfter == firstBefore - 1 &&
            secondAfter == secondBefore + 1,
            "duplicate concurrent final confirms transfer offered item exactly once");

        Check(
            firstPackets.Count(x =>
                x.Contains("\"Type\":\"TradeSuccess\"")) == 1 &&
            secondPackets.Count(x =>
                x.Contains("\"Type\":\"TradeSuccess\"")) == 1 &&
            !firstPackets.Any(x =>
                x.Contains("\"Type\":\"TradeCancel\"")) &&
            !secondPackets.Any(x =>
                x.Contains("\"Type\":\"TradeCancel\"")),
            "duplicate concurrent final confirms emit one success and no post-success cancel");
    }

    private static int CountInventory(
        SqliteGameStore store,
        long accountId,
        long characterId,
        int itemIndex)
        => store.GetCharacterAssets(
                accountId,
                characterId)
            .Inventory
            .Where(x =>
                Convert.ToInt32(x["ItemIndex"]) ==
                itemIndex)
            .Sum(x =>
                Convert.ToInt32(x["Count"]));
}
