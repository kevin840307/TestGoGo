using SGO.Server.Application.Messaging;

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

Check(fast.Queue.TryDequeue(out var low1) && ReferenceEquals(low1, monsterMove),
    "monster movement is preserved");
Check(fast.Queue.TryDequeue(out var low2) && ReferenceEquals(low2, itemSpawn),
    "ground item spawn is preserved");
Check(fast.Queue.TryDequeue(out var low3) && ReferenceEquals(low3, monsterDamage),
    "monster damage is preserved");
Check(fast.Queue.IsEmpty, "no duplicate packets or lost ordinary packets");

foreach (var packet in packets)
{
    Check(normal.Queue.TryDequeue(out var received) && ReferenceEquals(received, packet),
        "legacy queue preserves exact FIFO ordering");
}
Check(!hub.HasPendingPlayerPriority(normal.ConnectionId), "legacy mode has no hidden second queue");

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
while (fast.Queue.TryDequeue(out _))
    ordinarySeen++;
Check(ordinarySeen == 2000, "every ordinary item survives the priority burst");
hub.Unregister(fast.ConnectionId);
hub.Unregister(normal.ConnectionId);
Check(!hub.HasPendingPlayerPriority(fast.ConnectionId), "unregister cleans priority queue");
Console.WriteLine("[AOI_PRIORITY_REGRESSION] ALL PASS");
