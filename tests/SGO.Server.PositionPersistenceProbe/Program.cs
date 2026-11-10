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

    await using var service = new PositionPersistenceService(
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

    service.Stage(accountId.Value, charId, 15, 1250, 1250);
    service.FlushFinal(accountId.Value, charId, 1300, 1300, dirty: true);
    await Task.Delay(250);
    location = store.GetCharacterLocation(accountId.Value, charId);
    Check(location.HasValue && location.Value.X == 1300 &&
        location.Value.Y == 1300,
        "final logout write wins over pending older periodic snapshot");

    service.Stage(accountId.Value, charId, 15, 1400, 1400);
    service.FenceAndDiscard(accountId.Value, charId);
    Check(store.UpdateLocation(accountId.Value, charId, 13, 300, 300),
        "fenced transition commits new map 13 spawn");
    await Task.Delay(250);
    location = store.GetCharacterLocation(accountId.Value, charId);
    Check(location.HasValue && location.Value.MapId == 13 &&
        location.Value.X == 300 && location.Value.Y == 300,
        "fenced periodic write cannot reappear after map transition");

    await service.StopAsync(CancellationToken.None);
    Console.WriteLine("[POSITION_PERSIST_TEST] ALL PASS");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    try { Directory.Delete(root, recursive: true); } catch (IOException) { }
}
