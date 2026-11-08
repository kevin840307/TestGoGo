using System.Text.Json;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.SmokeTests.Fixtures;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

/// <summary>
/// Concurrency checks for mutations that touch both an asset row and money.
/// These stay separate from marketplace/economy tests so future equipment,
/// repair, discard and bank races have one focused home.
/// </summary>
internal static class AssetMutationConcurrencySmokeTests
{
    private sealed record Player(long AccountId, long CharacterId);

    public static void Run()
    {
        using var temp =
            new TemporaryDirectoryFixture("sgo-asset-mutation-concurrency-");
        var store = new SqliteGameStore(
            Path.Combine(temp.Root, "game.db"));

        ConcurrentRepairsCannotUndercharge(store);
        ConcurrentDuplicateRepairChargesOnce(store);
    }

    private static void ConcurrentRepairsCannotUndercharge(
        SqliteGameStore store)
    {
        var player = CreatePlayer(store, "repair");
        Check(
            store.AdjustMoney(
                player.AccountId,
                player.CharacterId,
                100) is not null,
            "seed money for concurrent repairs");

        const int firstItemIndex = 990_101;
        const int secondItemIndex = 990_102;
        const int repairedDurability = 100;
        const long repairCost = 60;

        var firstUid = store.AddEquipment(
            player.AccountId,
            player.CharacterId,
            firstItemIndex,
            damage: 0);
        var secondUid = store.AddEquipment(
            player.AccountId,
            player.CharacterId,
            secondItemIndex,
            damage: 0);

        Check(
            firstUid > 0 && secondUid > 0,
            "seed two damaged equipment rows for concurrent repair");

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);

        Task<SGO.Server.Application.Abstractions.RepairAssetResult> Repair(long uid) =>
            Task.Run(() =>
            {
                ready.Signal();
                if (!start.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException(
                        "concurrent repair start barrier timed out");

                return store.RepairAsset(
                    player.AccountId,
                    player.CharacterId,
                    uid,
                    false,
                    repairedDurability,
                    repairCost);
            });

        var attempts = new[]
        {
            Repair(firstUid),
            Repair(secondUid)
        };

        try
        {
            Check(
                ready.Wait(TimeSpan.FromSeconds(10)),
                "both repair contenders reached start barrier");
        }
        finally
        {
            start.Set();
        }

        Check(
            Task.WaitAll(attempts, TimeSpan.FromSeconds(20)),
            "concurrent repairs terminate without SQLite deadlock");

        var results = attempts.Select(x => x.Result).ToArray();
        Check(
            results.Count(x => x.Success) == 1,
            "only one of two 60-cost repairs succeeds with 100 money");

        var character = store.GetCharacter(
            player.AccountId,
            player.CharacterId);
        var money = Convert.ToInt64(character!["Money"]);
        Check(
            money == 40,
            "concurrent repairs deduct exactly one successful repair cost");

        var equips = store.GetCharacterAssets(
                player.AccountId,
                player.CharacterId)
            .Equips
            .Where(x =>
                Convert.ToInt64(x["ItemID"]) == firstUid ||
                Convert.ToInt64(x["ItemID"]) == secondUid)
            .ToArray();

        Check(
            equips.Count(x =>
                Convert.ToInt32(x["Damage"]) == repairedDurability) == 1 &&
            equips.Count(x =>
                Convert.ToInt32(x["Damage"]) == 0) == 1,
            "failed concurrent repair rolls back its equipment mutation");
    }

    private static void ConcurrentDuplicateRepairChargesOnce(
        SqliteGameStore store)
    {
        var player = CreatePlayer(store, "repair_same");
        Check(
            store.AdjustMoney(
                player.AccountId,
                player.CharacterId,
                100) is not null,
            "seed money for duplicate concurrent repair");

        const int itemIndex = 990_103;
        const int repairedDurability = 100;
        const long repairCost = 60;

        var uid = store.AddEquipment(
            player.AccountId,
            player.CharacterId,
            itemIndex,
            damage: 0);
        Check(uid > 0, "seed one damaged equipment row for duplicate repair");

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);

        Task<SGO.Server.Application.Abstractions.RepairAssetResult> Repair() =>
            Task.Run(() =>
            {
                ready.Signal();
                if (!start.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException(
                        "duplicate repair start barrier timed out");

                return store.RepairAsset(
                    player.AccountId,
                    player.CharacterId,
                    uid,
                    false,
                    repairedDurability,
                    repairCost);
            });

        var attempts = new[] { Repair(), Repair() };

        try
        {
            Check(
                ready.Wait(TimeSpan.FromSeconds(10)),
                "both duplicate repair contenders reached start barrier");
        }
        finally
        {
            start.Set();
        }

        Check(
            Task.WaitAll(attempts, TimeSpan.FromSeconds(20)),
            "duplicate same-UID repairs terminate without SQLite lock failure");

        var results = attempts.Select(x => x.Result).ToArray();
        Check(
            results.Count(x => x.Success) == 1,
            "duplicate same-UID repair succeeds exactly once");

        var character = store.GetCharacter(
            player.AccountId,
            player.CharacterId);
        Check(
            Convert.ToInt64(character!["Money"]) == 40,
            "duplicate same-UID repair charges exactly once");

        var equip = store.GetCharacterAssets(
                player.AccountId,
                player.CharacterId)
            .Equips
            .Single(x => Convert.ToInt64(x["ItemID"]) == uid);

        Check(
            Convert.ToInt32(equip["Damage"]) == repairedDurability,
            "duplicate same-UID repair leaves one fully repaired item");
    }

    private static Player CreatePlayer(
        SqliteGameStore store,
        string suffix)
    {
        var username = $"asset_{suffix}";
        const string password = "test-password";

        var registered = store.Register(username, password, "");
        Check(registered.Success, $"register asset player {suffix}");

        var accountId = store.Login(username, password);
        Check(accountId.HasValue, $"login asset player {suffix}");

        using var create = Json(
            JsonSerializer.Serialize(new
            {
                Name = $"A{suffix}",
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
        Check(created.Success, $"create asset character {suffix}");

        var dbid = store.Characters(accountId.Value)
            .Select(x => Convert.ToInt64(x["DBID"]))
            .First(x => x > 0);

        return new Player(accountId.Value, dbid);
    }
}
