using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SGO.Server.Application.Abstractions;
using SGO.Server.Application.World;
using SGO.Server.Infrastructure.Persistence.Sqlite;

static void Check(bool ok, string label)
{
    if (!ok)
        throw new InvalidOperationException("[POSITION_PERSIST_FAIL] " + label);
    Console.WriteLine("[POSITION_PERSIST_PASS] " + label);
}

var root = Path.Combine(Path.GetTempPath(), "sgo-position-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var dbPath = Path.Combine(root, "test.db");
try
{
    var store = new SqliteGameStore(dbPath);
    var registered = store.Register("position-probe", "secretpass", "probe@example.invalid");
    Check(registered.Success, "isolated SQLite test account created");
    var accountId = store.Login("position-probe", "secretpass");
    Check(accountId.HasValue, "test account authenticates");
    using var json = JsonDocument.Parse(
        """{"Name":"PositionProbe","Password":"","Race":0,"Str":1,"Dex":1,"Con":1,"Wis":1}""");
    var created = store.Create(accountId!.Value, json.RootElement);
    Check(created.Success, "test character created from current schema");
    var charId = Convert.ToInt64(store.Characters(accountId.Value)
        .First(c => Convert.ToInt64(c["DBID"]) > 0)["DBID"]);

    Check(store.UpdateLocation(accountId.Value, charId, 13, 100, 100),
        "test character starts on map 13");
    store.UpdatePositionsBatch([
        new CharacterPositionWrite(accountId.Value, charId, 13, 120, 120)
    ]);
    var location = store.GetCharacterLocation(accountId.Value, charId);
    Check(location.HasValue && location.Value.MapId == 13 &&
        location.Value.X == 120 && location.Value.Y == 120,
        "batched movement updates authoritative DB coordinates");

    Check(store.UpdateLocation(accountId.Value, charId, 15, 200, 200),
        "map transition commits map 15 spawn");
    store.UpdatePositionsBatch([
        new CharacterPositionWrite(accountId.Value, charId, 13, 999, 999)
    ]);
    location = store.GetCharacterLocation(accountId.Value, charId);
    Check(location.HasValue && location.Value.MapId == 15 &&
        location.Value.X == 200 && location.Value.Y == 200,
        "old-map buffered write cannot overwrite teleport coordinates");

    using var service = new PositionPersistenceService(
        store, NullLogger<PositionPersistenceService>.Instance);
    await service.StartAsync(CancellationToken.None);

    // 1000 requests stage only the latest snapshot: no SQLite call in the
    // synchronous packet handler, regardless of current database contention.
    var timer = Stopwatch.StartNew();
    for (var i = 1; i <= 1000; i++)
        service.Stage(accountId.Value, charId, 15, 200 + i, 200 + i);
    timer.Stop();
    Check(timer.ElapsedMilliseconds < 1000,
        "1000 periodic snapshots stage without a per-request SQLite write");
    service.Stage(accountId.Value, charId, 15, 1234, 1234);
    service.FlushQueued(accountId.Value, charId);
    location = store.GetCharacterLocation(accountId.Value, charId);
    Check(location.HasValue && location.Value.X == 1234 &&
        location.Value.Y == 1234,
        "queued snapshots coalesce to newest coordinate and flush");

    // SQLite has exactly one WAL writer. A background movement heartbeat
    // must back off when another writer holds the DB, WITHOUT delaying
    // monster damage under the gameplay hot monitor for 5 seconds.
    using (var competingWriter = new Microsoft.Data.Sqlite.SqliteConnection(
               new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
               { DataSource = dbPath }.ToString()))
    {
        competingWriter.Open();
        using var begin = competingWriter.CreateCommand();
        begin.CommandText = "BEGIN IMMEDIATE";
        begin.ExecuteNonQuery();

        var deferredStarted = Stopwatch.StartNew();
        var accepted = store.TryUpdatePositionsBatch([
            new CharacterPositionWrite(accountId.Value, charId, 15, 4444, 4444)
        ]);
        deferredStarted.Stop();
        Check(!accepted && deferredStarted.ElapsedMilliseconds < 1500,
            $"busy WAL writer defers background position in bounded time ({deferredStarted.ElapsedMilliseconds}ms)");
        location = store.GetCharacterLocation(accountId.Value, charId);
        Check(location.HasValue && location.Value.X == 1234 &&
            location.Value.Y == 1234,
            "deferred position batch never partially updates the database");

        // The periodic 100ms worker must restore and retry the newest staged
        // coordinates rather than treating BUSY as a completed write.
        var attemptsBeforeStage = store.BackgroundPositionMetrics.Attempts;
        service.Stage(accountId.Value, charId, 15, 1555, 1555);
        await Task.Delay(1400);
        var deferredMetrics = store.BackgroundPositionMetrics;
        Check(deferredMetrics.Attempts > attemptsBeforeStage &&
              deferredMetrics.Deferred >= 2,
            "background worker attempts and defers without losing staged position");
        using var rollback = competingWriter.CreateCommand();
        rollback.CommandText = "ROLLBACK";
        rollback.ExecuteNonQuery();
    }
    var retryClock = Stopwatch.StartNew();
    while (retryClock.Elapsed < TimeSpan.FromSeconds(4))
    {
        location = store.GetCharacterLocation(accountId.Value, charId);
        if (location.HasValue && location.Value.X == 1555 &&
            location.Value.Y == 1555)
            break;
        await Task.Delay(80);
    }
    Check(location.HasValue && location.Value.X == 1555 &&
          location.Value.Y == 1555,
        "newest background position commits after writer is released");

    var stateClock = Stopwatch.StartNew();
    for (var i = 0; i < 1000; i++)
        service.StageState(accountId.Value, charId,
            i % 2 == 0 ? "walk" : "idle");
    service.StageState(accountId.Value, charId, "crouch");
    stateClock.Stop();
    Check(stateClock.ElapsedMilliseconds < 1000,
        "1000 transient SyncState requests coalesce without synchronous SQL");
    service.FlushQueued(accountId.Value, charId);
    Check(Convert.ToString(store.GetCharacter(accountId.Value, charId)!["State"]) == "crouch",
        "latest coalesced state is durable before character selection");

    service.StageState(accountId.Value, charId, "walk");
    service.StageState(accountId.Value, charId, "idle");
    service.Stage(accountId.Value, charId, 15, 1250, 1250);
    service.FlushFinal(accountId.Value, charId, 1300, 1300, dirty: true);
    await Task.Delay(250);
    location = store.GetCharacterLocation(accountId.Value, charId);
    Check(location.HasValue && location.Value.X == 1300 &&
        location.Value.Y == 1300,
        "final logout write wins over pending older periodic snapshot");
    Check(Convert.ToString(store.GetCharacter(accountId.Value, charId)!["State"]) == "idle",
        "logout flush preserves final visual idle state without packet lock");

    service.Stage(accountId.Value, charId, 15, 1400, 1400);
    service.FenceAndDiscard(accountId.Value, charId);
    Check(store.UpdateLocation(accountId.Value, charId, 13, 300, 300),
        "fenced transition commits new map 13 spawn");
    await Task.Delay(250);
    location = store.GetCharacterLocation(accountId.Value, charId);
    Check(location.HasValue && location.Value.MapId == 13 &&
        location.Value.X == 300 && location.Value.Y == 300,
        "fenced periodic write cannot reappear after map transition");

    service.Stage(accountId.Value, charId, 13, 355, 355);
    service.FenceAndDiscard(accountId.Value, charId);
    Check(store.UpdateLocation(accountId.Value, charId, 13, 800, 800),
        "same-map GM warp commits an explicit position");
    await Task.Delay(250);
    location = store.GetCharacterLocation(accountId.Value, charId);
    Check(location.HasValue && location.Value.MapId == 13 &&
        location.Value.X == 800 && location.Value.Y == 800,
        "same-map teleport fence rejects stale same-map queued position");

    // A background state UPDATE must never resurrect the visual DB
    // state after a concurrent authoritative monster kill.
    store.ApplyDamage(accountId.Value, charId, int.MaxValue);
    Check(Convert.ToString(store.GetCharacter(accountId.Value, charId)!["State"]) == "death",
        "monster damage death state is persisted authoritatively");
    service.StageState(accountId.Value, charId, "walk");
    service.StageState(accountId.Value, charId, "idle");
    service.FlushQueued(accountId.Value, charId);
    Check(Convert.ToString(store.GetCharacter(accountId.Value, charId)!["State"]) == "death",
        "delayed cosmetic walk/idle cannot overwrite authoritative death");

    await service.StopAsync(CancellationToken.None);
    Console.WriteLine("[POSITION_PERSIST_TEST] ALL PASS");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    try { Directory.Delete(root, recursive: true); } catch (IOException) { }
}
