using System.Text.Json;
using SGO.Server.Application.Messaging;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.SmokeTests.Fixtures;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

/// <summary>
/// Real SQLite asset claim interleaved with an older WorldReady SELECT.
/// The persisted row must remain consumed and the AOI must never revive it.
/// </summary>
internal static class GroundSnapshotPersistenceRaceSmokeTests
{
    public static void Run()
    {
        using var temp = new TemporaryDirectoryFixture(
            "sgo-ground-snapshot-race-");
        var store = new SqliteGameStore(Path.Combine(
            temp.Root, "game.db"));

        var user = $"snap_{Guid.NewGuid():N}";
        Check(store.Register(user, "test-password", "").Success,
            "register ground snapshot race account");
        var account = store.Login(user, "test-password");
        Check(account.HasValue, "login ground snapshot race account");
        using var create = JsonSerializer.SerializeToDocument(new
        {
            Name = "SnapshotRace",
            Password = "1234",
            Race = 0,
            Hair = 1,
            HairColor = 1,
            Str = 5,
            Dex = 5,
            Con = 5,
            Wis = 5
        });
        Check(store.Create(account!.Value, create.RootElement).Success,
            "create ground snapshot race character");
        var characterId = store.Characters(account.Value)
            .Select(c => Convert.ToInt64(c["DBID"]))
            .First(c => c > 0);

        const int mapId = 13;
        const int itemIndex = 999_993;
        var uid = store.AddInventoryItem(
            account.Value, characterId, itemIndex, 2);
        Check(uid > 0, "seed two inventory items");
        var dropped = store.DropAssetToGround(
            account.Value, characterId, uid, 0, 1, mapId, 100, 100)
            ?? throw new InvalidOperationException(
                "FAILED: commit one ground drop before snapshot");
        Check(dropped.EntityId > 0, "commit one ground drop before snapshot");

        var hub = new ServerBroadcastHub();
        var viewer = hub.Register();
        try
        {
            hub.UpdatePresence(viewer.ConnectionId, mapId,
                characterId, account.Value, 0, 0);
            hub.ReconcileWorldAoi(viewer.ConnectionId);
            using var snapshot = hub.BeginWorldEntitySnapshot();

            // Step A: SELECT sees the item before another socket's pickup.
            var dbSnapshot = store.GetGroundItems(mapId).ToArray();
            Check(dbSnapshot.Length == 1 &&
                  dbSnapshot[0].EntityId == dropped.EntityId,
                "WorldReady SELECT sees the initial persisted ground row");

            // Step B: the winner claims the SQLite row atomically and then
            // emits its AOI removal. No entity has been indexed yet.
            var picked = store.PickUpGroundItem(
                account.Value, characterId, dropped.GroundItemId);
            Check(picked.Success, "concurrent pickup commits a single claim");
            hub.RemoveWorldEntity(dropped.EntityId, notifyObservers: false);

            // Step A resumes after B. It must not recreate the consumed
            // entity based on an obsolete SELECT result.
            var old = dbSnapshot[0];
            var accepted = hub.TryUpsertWorldEntityFromSnapshot(
                new WorldAoiEntity(
                    old.EntityId, 3, old.MapId, old.X, old.Y,
                    () => new
                    {
                        Type = "SpawnEntity",
                        EntityID = old.EntityId,
                        EntityType = 3
                    }),
                snapshot,
                out var changes);

            var inventoryCount = store.GetCharacterAssets(
                    account.Value, characterId)
                .Inventory
                .Where(i => Convert.ToInt32(i["ItemIndex"]) == itemIndex)
                .Sum(i => Convert.ToInt32(i["Count"]));

            Check(!accepted && changes.Count == 0 &&
                  store.GetGroundItems(mapId).Count == 0 &&
                  hub.VisibleWorldEntities(viewer.ConnectionId).Count == 0 &&
                  inventoryCount == 2,
                "stale WorldReady snapshot cannot resurrect picked-up item or alter conserved inventory");
        }
        finally
        {
            hub.Unregister(viewer.ConnectionId);
        }

        Console.WriteLine("Ground snapshot persistence race smoke passed.");
    }
}
