using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Text.Json;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.Application.Messaging;
using SGO.Server.Protocol;
using System.Text;
using System.Collections.Concurrent;

static void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException("[AOI_PRIORITY_FAIL] " + description);
    Console.WriteLine("[AOI_PRIORITY_PASS] " + description);
}

var hub = new ServerBroadcastHub();
var fast = hub.Register(prioritizePlayerEvents: true);
var normal = hub.Register();

// Non-player broadcasts preserve their original FIFO order; high-priority
// events are separated only for sockets that opted in.
var monsterMove = new { Type = "MoveEntity", EntityID = 8_130_045L, X = 10, Y = 12 };
var itemSpawn = new { Type = "SpawnEntity", EntityType = 3, EntityID = 5_000_123L };
var playerMove = new { Type = "MoveEntity", EntityID = 19L, X = 20, Y = 30 };
var playerSpawn = new { Type = "SpawnEntity", EntityType = 0, EntityID = 19L };
var playerDamage = new { Type = "EntityDamage", TargetType = 0, TargetID = 19L };
var monsterDamage = new { Type = "EntityDamage", TargetType = 1, TargetID = 8_130_045L };
object[] packets =
[
    monsterMove, itemSpawn, playerMove, playerSpawn, monsterDamage, playerDamage
];
foreach (var packet in packets)
{
    Check(hub.PublishToConnection(fast.ConnectionId, packet), "fast-lane target registered");
    Check(hub.PublishToConnection(normal.ConnectionId, packet), "legacy target registered");
}

Check(hub.HasPendingPlayerPriority(fast.ConnectionId), "urgent player queue notified");
Check(hub.PlayerPriorityBacklogCount(fast.ConnectionId) == 3, "only player movement/spawn/damage prioritized");
Check(hub.TryDequeuePlayerPriority(fast.ConnectionId, out var p1) && ReferenceEquals(p1, playerMove),
    "player move overtakes earlier monster movement");
Check(hub.TryDequeuePlayerPriority(fast.ConnectionId, out var p2) && ReferenceEquals(p2, playerSpawn),
    "player spawn keeps ordering inside its own lane");
Check(hub.TryDequeuePlayerPriority(fast.ConnectionId, out var p3) && ReferenceEquals(p3, playerDamage),
    "player damage keeps ordering inside its own lane");
Check(!hub.HasPendingPlayerPriority(fast.ConnectionId), "fast lane drained");
var playerHitsMonster = new
{
    Type = "EntityDamage",
    TargetType = 1,
    TargetID = 8_130_045L,
    AttackerID = 19L,
    Damage = 1
};
hub.PublishToConnection(fast.ConnectionId, playerHitsMonster);
Check(hub.TryDequeuePlayerPriority(fast.ConnectionId, out var playerHit) &&
      ReferenceEquals(playerHit, playerHitsMonster),
    "player-origin damage to a monster takes urgent lane without delaying combat");


Check(hub.TryDequeueOrdinary(fast.ConnectionId, out var low1) && ReferenceEquals(low1, monsterMove),
    "single monster movement is preserved exactly");
Check(hub.TryDequeueOrdinary(fast.ConnectionId, out var low2) && ReferenceEquals(low2, itemSpawn),
    "ground item spawn is preserved");
Check(hub.TryDequeueOrdinary(fast.ConnectionId, out var low3) && ReferenceEquals(low3, monsterDamage),
    "monster damage is preserved");
Check(fast.Queue.IsEmpty, "no duplicate packets or lost ordinary packets");

foreach (var packet in packets)
{
    Check(normal.Queue.TryDequeue(out var received) && ReferenceEquals(received, packet),
        "legacy queue preserves exact FIFO ordering");
}
Check(!hub.HasPendingPlayerPriority(normal.ConnectionId), "legacy mode has no hidden second queue");

// A busy WebSocket must not replay stale monster waypoints one by one.
// Four 250ms snapshots for one actor become one newest destination while
// retaining the historical span for diagnostics and bounded visual catch-up.
var worldMovePeer = hub.Register(prioritizePlayerEvents: true);
for (var i = 1; i <= 4; i++)
{
    hub.PublishToConnection(worldMovePeer.ConnectionId, new
    {
        Type = "MoveEntity",
        EntityID = 8_130_045L,
        X = i * 25,
        Y = 12,
        Dir = "6",
        SpeedTime = 0.25,
        MoveSpeed = 100.0
    });
}
Check(worldMovePeer.Queue.Count == 1,
    "four unsent snapshots for one monster occupy one ordinary queue slot");
Check(hub.TryDequeueOrdinary(worldMovePeer.ConnectionId, out var latestWorldMove),
    "coalesced world movement dequeues normally");
using (var decoded = ClientProtocol.Decode(
    Encoding.UTF8.GetString(hub.EncodeBroadcastPacket(latestWorldMove))))
{
    var packet = decoded.RootElement;
    Check(packet.GetProperty("EntityID").GetInt64() == 8_130_045L &&
          packet.GetProperty("X").GetDouble() == 100 &&
          packet.GetProperty("Y").GetDouble() == 12 &&
          Math.Abs(packet.GetProperty("SpeedTime").GetDouble() - 0.75) < 0.0001 &&
          Math.Abs(packet.GetProperty("SnapshotSpanSeconds").GetDouble() - 1.0) < 0.0001 &&
          packet.GetProperty("CoalescedMoves").GetInt32() == 4 &&
          Math.Abs(packet.GetProperty("MoveSpeed").GetDouble() - 100.0) < 0.0001,
        "world movement keeps newest destination, true span and entity movement speed");
}
hub.Unregister(worldMovePeer.ConnectionId);

// Combat/state is a hard causal boundary: never combine a later waypoint
// across attack/hit/death or other actor events.
var worldBoundary = hub.Register(prioritizePlayerEvents: true);
var worldBeforeAttack = new
{
    Type = "MoveEntity", EntityID = 8_130_046L,
    X = 10, Y = 12, Dir = "6", SpeedTime = 0.25
};
var worldAttack = new
{
    Type = "SyncState", EntityID = 8_130_046L,
    State = "attack", Dir = "6"
};
var worldAfterAttack = new
{
    Type = "MoveEntity", EntityID = 8_130_046L,
    X = 20, Y = 12, Dir = "6", SpeedTime = 0.25
};
hub.PublishToConnection(worldBoundary.ConnectionId, worldBeforeAttack);
hub.PublishToConnection(worldBoundary.ConnectionId, worldAttack);
hub.PublishToConnection(worldBoundary.ConnectionId, worldAfterAttack);
Check(worldBoundary.Queue.Count == 3,
    "monster state boundary prevents movement coalescing across combat");
Check(hub.TryDequeueOrdinary(worldBoundary.ConnectionId, out var worldBefore) &&
      ReferenceEquals(worldBefore, worldBeforeAttack),
    "monster movement before combat remains first");
Check(hub.TryDequeueOrdinary(worldBoundary.ConnectionId, out var worldMiddle) &&
      ReferenceEquals(worldMiddle, worldAttack),
    "monster combat state remains in causal order");
Check(hub.TryDequeueOrdinary(worldBoundary.ConnectionId, out var worldAfter) &&
      ReferenceEquals(worldAfter, worldAfterAttack),
    "monster movement after combat remains after the boundary");
hub.Unregister(worldBoundary.ConnectionId);

// SpeedTime <= 0.01 is an authoritative correction (for example the final
// attack-position snap). It must remain a separate FIFO packet and must also
// prevent a later normal waypoint from folding back before that correction.
var correctionBoundary = hub.Register(prioritizePlayerEvents: true);
var preCorrectionMove = new
{
    Type = "MoveEntity", EntityID = 8_130_047L,
    X = 30, Y = 12, Dir = "6", SpeedTime = 0.25
};
var authoritativeCorrection = new
{
    Type = "MoveEntity", EntityID = 8_130_047L,
    X = 34, Y = 12, Dir = "6", SpeedTime = 0.0
};
var postCorrectionMove = new
{
    Type = "MoveEntity", EntityID = 8_130_047L,
    X = 50, Y = 12, Dir = "6", SpeedTime = 0.25
};
hub.PublishToConnection(correctionBoundary.ConnectionId, preCorrectionMove);
hub.PublishToConnection(correctionBoundary.ConnectionId, authoritativeCorrection);
hub.PublishToConnection(correctionBoundary.ConnectionId, postCorrectionMove);
Check(correctionBoundary.Queue.Count == 3,
    "authoritative zero-time correction is never merged with visual waypoints");
Check(hub.TryDequeueOrdinary(correctionBoundary.ConnectionId, out var preCorrection) &&
      ReferenceEquals(preCorrection, preCorrectionMove),
    "movement before authoritative correction remains before it");
Check(hub.TryDequeueOrdinary(correctionBoundary.ConnectionId, out var exactCorrection) &&
      ReferenceEquals(exactCorrection, authoritativeCorrection),
    "authoritative correction packet identity and FIFO position are preserved");
Check(hub.TryDequeueOrdinary(correctionBoundary.ConnectionId, out var postCorrection) &&
      ReferenceEquals(postCorrection, postCorrectionMove),
    "movement after authoritative correction gets a fresh queue slot");
hub.Unregister(correctionBoundary.ConnectionId);

// Severe observer backpressure should scale with moving actors, not historical
// waypoints: 1000 snapshots distributed across 20 monsters => 20 queue slots.
var worldBurst = hub.Register(prioritizePlayerEvents: true);
for (var i = 0; i < 1000; i++)
{
    hub.PublishToConnection(worldBurst.ConnectionId, new
    {
        Type = "MoveEntity",
        EntityID = 8_200_000L + i % 20,
        X = i,
        Y = i % 7,
        Dir = "3",
        SpeedTime = 0.25
    });
}
Check(worldBurst.Queue.Count == 20,
    "1000 unsent monster waypoints collapse to 20 newest per-actor slots");
var worldBurstSeen = 0;
while (hub.TryDequeueOrdinary(worldBurst.ConnectionId, out _))
    worldBurstSeen++;
Check(worldBurstSeen == 20,
    "all 20 moving monsters retain one newest waypoint after coalescing");
hub.Unregister(worldBurst.ConnectionId);

// A large stationary-ground and monster burst must not delay a newly queued
// player update, nor may it silently discard or reorder ordinary messages.
for (var i = 0; i < 1000; i++)
{
    hub.PublishToConnection(fast.ConnectionId,
        new { Type = "MoveEntity", EntityID = 8_000_000L + i, X = i, Y = 0 });
    hub.PublishToConnection(fast.ConnectionId,
        new { Type = "SpawnEntity", EntityType = 3, EntityID = 5_000_000L + i });
}
var latePlayerMove = new { Type = "MoveEntity", EntityID = 37L, X = 7, Y = 8 };
hub.PublishToConnection(fast.ConnectionId, latePlayerMove);
Check(hub.TryDequeuePlayerPriority(fast.ConnectionId, out var urgent) &&
      ReferenceEquals(urgent, latePlayerMove),
      "player move is dequeued first even after 2000 ordinary AOI events");
Check(fast.Queue.Count == 2000, "all 2000 monster and drop updates remain queued");
var ordinarySeen = 0;
while (hub.TryDequeueOrdinary(fast.ConnectionId, out _))
    ordinarySeen++;
Check(ordinarySeen == 2000, "every ordinary item survives the priority burst");
// Simulate twenty independent observers receiving mixed player and world
// updates concurrently. This verifies there is no shared global head-of-line
// queue between clients, and no loss of ground/monster events.
var peers = Enumerable.Range(0, 20)
    .Select(_ => hub.Register(prioritizePlayerEvents: true))
    .ToArray();
for (var i = 0; i < peers.Length; i++)
{
    var peer = peers[i];
    for (var step = 0; step < 150; step++)
    {
        hub.PublishToConnection(peer.ConnectionId, new
        {
            Type = "MoveEntity",
            EntityID = 8_000_000L + step,
            X = step,
            Y = i
        });
        hub.PublishToConnection(peer.ConnectionId, new
        {
            Type = "SpawnEntity",
            EntityType = 3,
            EntityID = 5_000_000L + step
        });
        if (step % 15 == 0)
        {
            hub.PublishToConnection(peer.ConnectionId, new
            {
                Type = "MoveEntity",
                EntityID = 100L + i,
                X = step,
                Y = i
            });
        }
    }
}
var everyPeerHasAllEvents = true;
foreach (var peer in peers)
{
    // World/monster traffic keeps one ordered slot per independent actor;
    // ten historical visual positions for the SAME player coalesce into the
    // newest urgent point.
    var valid = peer.Queue.Count == 300
        && hub.PlayerPriorityBacklogCount(peer.ConnectionId) == 1;
    valid &= hub.TryDequeuePlayerPriority(peer.ConnectionId, out var latest)
        && (int)(latest.GetType().GetProperty("X")?.GetValue(latest) ?? -1) == 135
        && !hub.HasPendingPlayerPriority(peer.ConnectionId);
    var groundAndMonsterCount = 0;
    while (hub.TryDequeueOrdinary(peer.ConnectionId, out _))
        groundAndMonsterCount++;
    valid &= groundAndMonsterCount == 300;
    everyPeerHasAllEvents &= valid;
    hub.Unregister(peer.ConnectionId);
}
Check(everyPeerHasAllEvents,
    "twenty observers preserve 6000 world events and the newest per-player visual position");

// A combat/animation boundary MUST break position coalescing: never
// reorder attack state or remove the last movement before combat.
var boundary = hub.Register(prioritizePlayerEvents: true);
var moveBeforeAttack = new { Type = "MoveEntity", EntityID = 123L, X = 10 };
var stateAttack = new { Type = "SyncState", EntityID = 123L, State = "attack_aa" };
var moveAfterAttack = new { Type = "MoveEntity", EntityID = 123L, X = 20 };
hub.PublishToConnection(boundary.ConnectionId, moveBeforeAttack);
hub.PublishToConnection(boundary.ConnectionId, stateAttack);
hub.PublishToConnection(boundary.ConnectionId, moveAfterAttack);
Check(hub.PlayerPriorityBacklogCount(boundary.ConnectionId) == 3,
    "combat animation boundary prevents combining the two player positions");
Check(hub.TryDequeuePlayerPriority(boundary.ConnectionId, out var before) &&
      ReferenceEquals(before, moveBeforeAttack),
    "movement before attack preserved in causal order");
Check(hub.TryDequeuePlayerPriority(boundary.ConnectionId, out var middle) &&
      ReferenceEquals(middle, stateAttack),
    "player combat animation remains in causal order");
Check(hub.TryDequeuePlayerPriority(boundary.ConnectionId, out var after) &&
      ReferenceEquals(after, moveAfterAttack),
    "new movement after attack preserved without merging across event");
hub.Unregister(boundary.ConnectionId);

// Three 100ms movement snapshots can be coalesced while a socket is busy.
// The last coordinate must arrive with 300ms of accumulated server time;
// otherwise the Client sees an impossible three-times-faster remote player.
var timedPeer = hub.Register(prioritizePlayerEvents: true);
for (var i = 1; i <= 3; i++)
{
    hub.PublishToConnection(timedPeer.ConnectionId, new
    {
        Type = "MoveEntity", EntityID = 321L,
        X = i * 20, Y = 5, Dir = "6", SpeedTime = 0.10
    });
}
Check(hub.PlayerPriorityBacklogCount(timedPeer.ConnectionId) == 1,
    "three unsent player movements keep one urgent queue entry");
Check(hub.TryDequeuePlayerPriority(timedPeer.ConnectionId, out var timedMove),
    "coalesced player movement dequeues");
using (var decoded = ClientProtocol.Decode(
    Encoding.UTF8.GetString(hub.EncodeBroadcastPacket(timedMove))))
{
    var packet = decoded.RootElement;
    Check(packet.GetProperty("X").GetDouble() == 60 &&
          packet.GetProperty("Y").GetDouble() == 5 &&
          packet.GetProperty("Dir").GetString() == "6" &&
          Math.Abs(packet.GetProperty("SpeedTime").GetDouble() - 0.30) < 0.0001 &&
          packet.GetProperty("CoalescedMoves").GetInt32() == 3,
        "coalesced movement retains newest position, interval and diagnostic count");
}

// Preserve the existing client nominal-interval bounds under a long backlog.
for (var i = 0; i < 20; i++)
{
    hub.PublishToConnection(timedPeer.ConnectionId, new
    {
        Type = "MoveEntity", EntityID = 321L,
        X = 100 + i * 20, Y = 5, Dir = "6", SpeedTime = 0.10
    });
}
Check(hub.TryDequeuePlayerPriority(timedPeer.ConnectionId, out var boundedMove) &&
      Math.Abs(Convert.ToDouble(
          boundedMove.GetType().GetProperty("SpeedTime")?.GetValue(boundedMove)) - 0.75) < 0.0001,
    "large queued visual movement caps interval to the supported client limit");
Check(Math.Abs(Convert.ToDouble(
          boundedMove.GetType().GetProperty("SnapshotSpanSeconds")?.GetValue(boundedMove)) - 2.0) < 0.0001,
    "coalesced movement separately retains true 2-second server elapsed time");
hub.Unregister(timedPeer.ConnectionId);

// Repeated per-observer AES+JSON work is amortized for immutable scalar
// broadcasts only. Confirm simultaneous observers get the same frame while
// mutable packet data is never mistakenly cached after a state change.
var immutableMove = new
{
    Type = "MoveEntity",
    EntityID = 555L,
    X = 120,
    Y = 180,
    Dir = "2",
    SpeedTime = 0.1
};
var encodedFrames = new ConcurrentBag<byte[]>();
Parallel.For(0, 20, _ =>
{
    encodedFrames.Add(hub.EncodeBroadcastPacket(immutableMove));
});
Check(encodedFrames.Count == 20 &&
      encodedFrames.All(bytes => ReferenceEquals(bytes, encodedFrames.First())),
    "20 concurrent recipients share one immutable encrypted frame");
var firstFrame = encodedFrames.First();
using (var decoded = ClientProtocol.Decode(Encoding.UTF8.GetString(firstFrame)))
{
    Check(decoded.RootElement.GetProperty("Type").GetString() == "MoveEntity" &&
          decoded.RootElement.GetProperty("X").GetInt32() == 120,
        "cached frames remain exactly compatible with original client crypto");
}
var mutablePacket = new Dictionary<string, object>
{
    ["Type"] = "MoveEntity",
    ["EntityID"] = 19L,
    ["X"] = 42
};
var mutableBefore = hub.EncodeBroadcastPacket(mutablePacket);
mutablePacket["X"] = 43;
var mutableAfter = hub.EncodeBroadcastPacket(mutablePacket);
using (var oldPacket = ClientProtocol.Decode(Encoding.UTF8.GetString(mutableBefore)))
using (var newPacket = ClientProtocol.Decode(Encoding.UTF8.GetString(mutableAfter)))
{
    Check(oldPacket.RootElement.GetProperty("X").GetInt32() == 42 &&
          newPacket.RootElement.GetProperty("X").GetInt32() == 43,
        "mutable dictionary payload bypasses cache and always reflects new state");
}
var nestedPacket = new { Type = "SpawnEntity", EntityID = 11L,
    ExtraData = new Dictionary<string, object> { ["HP"] = 50 } };
var nestedFirst = hub.EncodeBroadcastPacket(nestedPacket);
nestedPacket.ExtraData["HP"] = 49;
var nestedSecond = hub.EncodeBroadcastPacket(nestedPacket);
using (var next = ClientProtocol.Decode(Encoding.UTF8.GetString(nestedSecond)))
{
    Check(next.RootElement.GetProperty("ExtraData").GetProperty("HP").GetInt32() == 49 &&
          !ReferenceEquals(nestedFirst, nestedSecond),
        "anonymous packets containing nested mutable state bypass cache");
}

hub.Unregister(fast.ConnectionId);
hub.Unregister(normal.ConnectionId);
Check(!hub.HasPendingPlayerPriority(fast.ConnectionId), "unregister cleans priority queue");
// A normal character read must never acquire SQLite's writer lock, even
// while another connection is mid-transaction. The historical unconditional
// UPDATE in GetCharacter blocked here for busy_timeout=5000 ms.
var databasePath = Path.Combine(Path.GetTempPath(),
    "sgo-multiplayer-sqlite-read-" + Guid.NewGuid().ToString("N") + ".db");
try
{
    var store = new SqliteGameStore(databasePath);
    var userName = "readprobe" + Guid.NewGuid().ToString("N")[..10];
    Check(store.Register(userName, "read-probe-pass", "read@example.invalid").Success,
        "isolated SQLite test account registered");
    var accountId = store.Login(userName, "read-probe-pass");
    Check(accountId.HasValue, "isolated SQLite test account logged in");
    using var create = JsonDocument.Parse("""
        {"Name":"ReadProbe","Password":"0000","Race":0,"Hair":1,
         "Str":5,"Dex":5,"Con":5,"Wis":5}
        """);
    Check(store.Create(accountId!.Value, create.RootElement).Success,
        "isolated SQLite test character created");
    long characterId;
    using (var lookup = new SqliteConnection(
               new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
    {
        lookup.Open();
        using var cmd = lookup.CreateCommand();
        cmd.CommandText = "SELECT id FROM characters WHERE account_id=$a LIMIT 1";
        cmd.Parameters.AddWithValue("$a", accountId.Value);
        characterId = (long)(cmd.ExecuteScalar() ?? 0L);
    }
    Check(characterId > 0, "test character persisted");
    using (var writer = new SqliteConnection(
               new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
    {
        writer.Open();
        using (var begin = writer.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE";
            begin.ExecuteNonQuery();
        }
        var started = Stopwatch.GetTimestamp();
        var character = store.GetCharacter(accountId!.Value, characterId);
        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Check(character is not null && elapsedMs < 1000,
            $"character read remains nonblocking under another WAL writer ({elapsedMs:F1}ms)");
        var hpStarted = Stopwatch.GetTimestamp();
        var hpSnapshot = store.GetCharacterHpSnapshot(
            [(accountId!.Value, characterId)]);
        var hpElapsedMs = Stopwatch.GetElapsedTime(hpStarted).TotalMilliseconds;
        Check(hpSnapshot.TryGetValue(characterId, out var hp) &&
              hp > 0 && hpElapsedMs < 1000,
            $"batched HP snapshot is read-only under another WAL writer ({hpElapsedMs:F1}ms)");
        Check(store.GetCharacterHpSnapshot([(accountId.Value + 1000, characterId)]).Count == 0,
            "batch HP query never leaks another account's character");
        var travelStarted = Stopwatch.GetTimestamp();
        var location = store.GetCharacterLocation(accountId.Value, characterId);
        var travelMs = Stopwatch.GetElapsedTime(travelStarted).TotalMilliseconds;
        Check(location.HasValue && location.Value.MapId > 0 && travelMs < 1000,
            $"narrow live map location remains nonblocking under WAL writer ({travelMs:F1}ms)");
        Check(store.GetCharacterLocation(accountId.Value + 1000, characterId) is null,
            "narrow location read checks account ownership");
        var petsStarted = Stopwatch.GetTimestamp();
        var noPets = store.GetActivePetFollowSnapshot(accountId.Value, characterId);
        var petsMs = Stopwatch.GetElapsedTime(petsStarted).TotalMilliseconds;
        Check(noPets.Count == 0 && petsMs < 1000,
            $"no-active-pet follow snapshot remains nonblocking ({petsMs:F1}ms)");
        var cleanupStarted = Stopwatch.GetTimestamp();
        var expired = store.RemoveExpiredGroundItems(990, DateTimeOffset.UtcNow);
        var cleanupElapsedMs = Stopwatch.GetElapsedTime(cleanupStarted).TotalMilliseconds;
        Check(expired.Count == 0 && cleanupElapsedMs < 1000,
            $"empty-map 1Hz ground cleanup does not wait for WAL writer ({cleanupElapsedMs:F1}ms)");
        var drainStarted = Stopwatch.GetTimestamp();
        var noCompanions = store.DrainCompanionRuntime(accountId.Value, characterId, 1);
        var drainElapsedMs = Stopwatch.GetElapsedTime(drainStarted).TotalMilliseconds;
        Check(noCompanions.Pets.Count == 0 &&
              noCompanions.Horse is null &&
              noCompanions.RecalledPetUids.Count == 0 &&
              drainElapsedMs < 1000,
            $"idle companion tick bypasses WAL writer and full assets ({drainElapsedMs:F1}ms)");
        using var rollback = writer.CreateCommand();
        rollback.CommandText = "ROLLBACK";
        rollback.ExecuteNonQuery();
    }
    // A live pet state change must be reflected immediately by the next
    // 250ms query: no stale cache and no leak to another account.
    using (var seed = new SqliteConnection(
               new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
    {
        seed.Open();
        using var cmd = seed.CreateCommand();
        cmd.CommandText = """
            INSERT INTO pets (character_id,pet_id,name,hp,max_hp,level,attack,evol_id,feed_value,state)
            VALUES ($id,245,'ActiveTester',67,120,4,31,2,2,1);
            INSERT INTO pets (character_id,pet_id,name,state)
            VALUES ($id,246,'StoredTester',0);
            """;
        cmd.Parameters.AddWithValue("$id", characterId);
        cmd.ExecuteNonQuery();
    }
    var livePet = store.GetActivePetFollowSnapshot(accountId!.Value, characterId);
    Check(livePet.Count == 1 &&
          Convert.ToInt32(livePet[0]["HP"]) == 67 &&
          Convert.ToInt32(livePet[0]["Attack"]) == 31 &&
          Convert.ToInt32(livePet[0]["EvolID"]) == 2 &&
          Convert.ToInt32(livePet[0]["Level"]) == 4 &&
          Convert.ToString(livePet[0]["Name"]) == "ActiveTester",
        "narrow pet query returns exact live spawn, skill and attack fields");
    Check(store.GetActivePetFollowSnapshot(accountId.Value + 1000, characterId).Count == 0,
        "active pets cannot leak across accounts");
    var firstDrain = store.DrainCompanionRuntime(accountId.Value, characterId, 1);
    var afterOne = store.GetActivePetFollowSnapshot(accountId.Value, characterId);
    Check(firstDrain.Pets.Count == 1 &&
          firstDrain.RecalledPetUids.Count == 0 &&
          afterOne.Count == 1,
        "active companion still drains food on the original one-second cadence");
    var secondDrain = store.DrainCompanionRuntime(accountId.Value, characterId, 1);
    Check(secondDrain.RecalledPetUids.Count == 1 &&
          store.GetActivePetFollowSnapshot(accountId.Value, characterId).Count == 0,
        "food exhaustion still recalls the pet and updates live server state");
    using (var corrupt = new SqliteConnection(
               new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
    {
        corrupt.Open();
        using var cmd = corrupt.CreateCommand();
        cmd.CommandText = "UPDATE characters SET exp_acc=-1 WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", characterId);
        cmd.ExecuteNonQuery();
    }
    Check(store.GetCharacter(accountId!.Value, characterId) is not null,
        "invalid legacy experience row remains readable");
    using (var verify = new SqliteConnection(
               new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
    {
        verify.Open();
        using var cmd = verify.CreateCommand();
        cmd.CommandText = "SELECT exp_acc FROM characters WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", characterId);
        Check(Convert.ToInt64(cmd.ExecuteScalar()) == 0,
            "invalid experience still repaired by original conditional UPDATE");
    }
}
finally
{
    SqliteConnection.ClearAllPools();
    foreach (var suffix in new[] { "", "-wal", "-shm" })
    {
        var file = databasePath + suffix;
        if (File.Exists(file))
            File.Delete(file);
    }
}

Console.WriteLine("[AOI_PRIORITY_REGRESSION] ALL PASS");
