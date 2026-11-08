using System.Text.Json;
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
