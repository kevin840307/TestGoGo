using SGO.Server.Application.Messaging;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.World;

/// <summary>Server-only AOI behavior; no SQLite, packet transport or game assets.</summary>
internal static class PlayerSpatialAoiSmokeTests
{
    private static OnlinePlayerPresence Player(
        long id, int mapId, double x, double y)
        => new(Guid.NewGuid(), id, id, mapId, x, y);

    private static OnlinePlayerPresence Move(
        OnlinePlayerPresence player, double x, double y)
        => player with { X = x, Y = y };

    public static void Run()
    {
        var aoi = new PlayerSpatialAoi();
        var a = Player(1, 13, 0, 0);
        var b = Player(2, 13, 9_000, 0);
        var otherMap = Player(3, 15, 0, 0);
        Check(aoi.Upsert(a).Count == 0, "AOI first ready player sees no peers");
        Check(aoi.Upsert(b).Count == 0, "AOI players at far ends of map have no edges");
        Check(aoi.Upsert(otherMap).Count == 0, "AOI different maps never discover each other");
        Check(aoi.VisiblePeers(a.ConnectionId).Count == 0, "AOI A never receives far B");

        b = Move(b, 1799, 0);
        var appeared = aoi.Upsert(b);
        Check(appeared.Count == 2 && appeared.All(change => change.Appeared),
            "AOI crossing enter radius generates exactly two directional spawn edges");
        Check(aoi.IsVisible(a.ConnectionId, b.ConnectionId) &&
              aoi.IsVisible(b.ConnectionId, a.ConnectionId),
            "AOI visibility is symmetric");
        Check(aoi.Upsert(b).Count == 0, "AOI unchanged coordinates never duplicate spawn");

        b = Move(b, 2150, 0);
        Check(aoi.Upsert(b).Count == 0 &&
              aoi.IsVisible(a.ConnectionId, b.ConnectionId),
            "AOI exit hysteresis keeps existing peer at 2150 distance");
        b = Move(b, 2201, 0);
        var vanished = aoi.Upsert(b);
        Check(vanished.Count == 2 && vanished.All(change => !change.Appeared),
            "AOI crossing exit radius delivers reciprocal despawn");
        Check(aoi.VisiblePeers(a.ConnectionId).Count == 0,
            "AOI removed B from A known-entity set");

        b = Move(b, -1799, -1);
        Check(aoi.Upsert(b).Count == 2,
            "AOI negative grid cells and cell boundaries still find nearby peers");
        b = Move(b, 50_000, 50_000);
        Check(aoi.Upsert(b).Count == 2 &&
              aoi.VisiblePeers(a.ConnectionId).Count == 0,
            "AOI teleport crosses many cells and cleans up old visibility");

        b = Move(b, 10, 10);
        Check(aoi.Upsert(b).Count == 2, "AOI returning from teleport resends spawn");
        b = b with { MapId = 15 };
        var mapLeft = aoi.Upsert(b);
        Check(mapLeft.Any(change => !change.Appeared &&
                change.ObserverConnectionId == a.ConnectionId),
            "AOI map transition despawns from old-map viewer");
        Check(!aoi.IsVisible(a.ConnectionId, b.ConnectionId),
            "AOI map transition cannot leak same-map visibility");

        // A wide viewport must discover remote players even when the
        // local character is stationary; legacy connections stay at 1800.
        var adaptive = new PlayerSpatialAoi();
        var wideViewer = Player(9001, 13, 0, 0);
        var outerPeer = Player(9002, 13, 2900, 0);
        adaptive.Upsert(wideViewer);
        Check(adaptive.Upsert(outerPeer).Count == 0,
            "legacy viewport does not unnecessarily subscribe to outer peers");
        var expanded = adaptive.Upsert(wideViewer, 3200);
        Check(expanded.Count == 2 && expanded.All(x => x.Appeared) &&
              adaptive.IsVisible(outerPeer.ConnectionId, wideViewer.ConnectionId),
            "wide viewport creates reciprocal outer player AOI edges");
        Check(adaptive.Upsert(wideViewer, 3200).Count == 0,
            "identical view radius cannot create duplicate player spawns");
        var contracted = adaptive.Upsert(wideViewer, 1800);
        Check(contracted.Count == 2 && contracted.All(x => !x.Appeared),
            "shrink viewport sends reciprocal remote player despawns");

        // Actual 4K zoomed-out views can cover >5000 world units.
        var ultra = new PlayerSpatialAoi();
        var uv = Player(9201, 13, 0, 0);
        var up = Player(9202, 13, 6200, 0);
        ultra.Upsert(uv);
        Check(ultra.Upsert(up).Count == 0,
            "normal viewport keeps ultrawide outer players hidden");
        var ultraSpawn = ultra.Upsert(uv, 6500);
        Check(ultraSpawn.Count == 2 && ultraSpawn.All(x => x.Appeared) &&
              ultra.IsVisible(uv.ConnectionId, up.ConnectionId) &&
              ultra.IsVisible(up.ConnectionId, uv.ConnectionId),
            "extended Canvas radius delivers reciprocal player spawn beyond 5000");
        Check(ultra.Upsert(uv, 6500).Count == 0,
            "unchanged ultrawide radius has no duplicate spawn");
        var ultraStop = ultra.Upsert(uv, 1800);
        Check(ultraStop.Count == 2 && ultraStop.All(x => !x.Appeared),
            "shrinking camera removes extra outer peers in both directions");

        // Separate index: no O(N^2) scanning or accidental pairs in a
        // 1000-player, evenly spread out map.
        var dense = new PlayerSpatialAoi();
        var farPlayers = new List<OnlinePlayerPresence>();
        for (var i = 0; i < 1_000; i++)
        {
            var player = Player(i + 100, 13, i * 8_192, 0);
            farPlayers.Add(player);
            Check(dense.Upsert(player).Count == 0,
                "AOI widely separated player " + i + " has no spurious spawn");
        }
        Check(dense.ReadyPlayerCount == 1000, "AOI indexes all 1000 players");
        Check(dense.VisiblePeers(farPlayers[500].ConnectionId).Count == 0,
            "AOI middle player does not see distant 999 others");
        var crowded = farPlayers[501];
        crowded = Move(crowded, farPlayers[500].X + 10, 0);
        Check(dense.Upsert(crowded).Count == 2,
            "AOI 1000-player spatial index produces only the nearby pair");

        // Stress state transitions, same-map races, and removals: no
        // duplicate visibility or asymmetric ghost edges after final settle.
        var concurrent = new PlayerSpatialAoi();
        var origin = Player(3001, 7, 0, 0);
        var racers = Enumerable.Range(0, 32)
            .Select(i => Player(4000 + i, 7, 5000, 0)).ToArray();
        concurrent.Upsert(origin);
        foreach (var player in racers)
            concurrent.Upsert(player);
        Parallel.For(0, 512, i =>
        {
            var idx = i % racers.Length;
            concurrent.Upsert(Move(
                racers[idx], i % 2 == 0 ? 100 : 5_000, idx * 5));
        });
        // Serial final settle establishes definitive state after the races.
        for (var i = 0; i < racers.Length; i++)
            concurrent.Upsert(Move(racers[i], 100, i * 5));
        Check(concurrent.VisiblePeers(origin.ConnectionId).Count == racers.Length,
            "AOI concurrent movement reconciles every nearby peer exactly once");
        foreach (var racer in racers)
            Check(concurrent.IsVisible(racer.ConnectionId, origin.ConnectionId),
                "AOI concurrent movement retains reciprocal edge");
        foreach (var racer in racers)
            concurrent.Remove(racer.ConnectionId);
        Check(concurrent.VisiblePeers(origin.ConnectionId).Count == 0 &&
              concurrent.ReadyPlayerCount == 1,
            "AOI removes all departed client edges and spatial cells");

        // Hub route check: movement should reach the near player queue only.
        var hub = new ServerBroadcastHub();
        var aConn = hub.Register();
        var bConn = hub.Register();
        var farConn = hub.Register();
        hub.UpdatePresence(aConn.ConnectionId, 13, 1, 1, 0, 0);
        hub.UpdatePresence(bConn.ConnectionId, 13, 2, 2, 100, 100);
        hub.UpdatePresence(farConn.ConnectionId, 13, 3, 3, 8000, 8000);
        hub.ReconcilePlayerAoi(aConn.ConnectionId);
        hub.ReconcilePlayerAoi(bConn.ConnectionId);
        hub.ReconcilePlayerAoi(farConn.ConnectionId);
        hub.PublishToPlayerObservers(aConn.ConnectionId, new { Type = "MoveEntity", EntityID = 1 });
        Check(bConn.Queue.TryDequeue(out _),
            "AOI ServerBroadcastHub delivers nearby player's move");
        Check(!farConn.Queue.TryDequeue(out _),
            "AOI ServerBroadcastHub suppresses distant player's move");
        Check(!aConn.Queue.TryDequeue(out _),
            "AOI source client does not receive its own move echo");
        hub.RemovePlayerAoi(aConn.ConnectionId);
        Check(hub.VisiblePlayerPeers(bConn.ConnectionId).Count == 0,
            "AOI disconnect clears peers in broadcast hub");
        hub.Unregister(aConn.ConnectionId);
        hub.Unregister(bConn.ConnectionId);
        hub.Unregister(farConn.ConnectionId);

        // Live-WebSocket-only fast lane: intermediate unsent coordinates
        // for the same player are replaceable. Lifecycle, combat and state
        // packets must NEVER be coalesced or reordered behind those moves.
        var motionHub = new ServerBroadcastHub();
        var observer = motionHub.Register(prioritizePlayerEvents: true);
        const long actor = 123;
        motionHub.PublishToConnection(observer.ConnectionId,
            new { Type = "MoveEntity", EntityID = actor, X = 10, Y = 20 });
        motionHub.PublishToConnection(observer.ConnectionId,
            new { Type = "MoveEntity", EntityID = actor, X = 30, Y = 40 });
        Check(
            motionHub.PlayerPriorityBacklogCount(observer.ConnectionId) == 1 &&
            motionHub.TryDequeuePlayerPriority(observer.ConnectionId, out var newest) &&
            Convert.ToInt32(newest.GetType().GetProperty("X")?.GetValue(newest)) == 30,
            "queued player positions collapse to newest authoritative snapshot");

        motionHub.PublishToConnection(observer.ConnectionId,
            new { Type = "MoveEntity", EntityID = actor, X = 40 });
        motionHub.PublishToConnection(observer.ConnectionId,
            new { Type = "SyncState", EntityID = actor, State = "attack_aa" });
        motionHub.PublishToConnection(observer.ConnectionId,
            new { Type = "MoveEntity", EntityID = actor, X = 50 });
        Check(motionHub.PlayerPriorityBacklogCount(observer.ConnectionId) == 3,
            "player attack animation separates two motion windows");
        Check(
            motionHub.TryDequeuePlayerPriority(observer.ConnectionId, out var preAttack) &&
            Convert.ToInt32(preAttack.GetType().GetProperty("X")?.GetValue(preAttack)) == 40 &&
            motionHub.TryDequeuePlayerPriority(observer.ConnectionId, out var attackState) &&
            Convert.ToString(attackState.GetType().GetProperty("Type")?.GetValue(attackState)) == "SyncState" &&
            motionHub.TryDequeuePlayerPriority(observer.ConnectionId, out var postAttack) &&
            Convert.ToInt32(postAttack.GetType().GetProperty("X")?.GetValue(postAttack)) == 50,
            "motion / combat state / next motion retain original FIFO order");

        motionHub.PublishToConnection(observer.ConnectionId,
            new { Type = "MoveEntity", EntityID = actor, X = 60 });
        motionHub.PublishToConnection(observer.ConnectionId,
            new { Type = "DespawnEntity", EntityID = actor });
        motionHub.PublishToConnection(observer.ConnectionId,
            new { Type = "MoveEntity", EntityID = actor, X = 70 });
        Check(motionHub.PlayerPriorityBacklogCount(observer.ConnectionId) == 3,
            "despawn lifecycle boundary cannot merge stale and future positions");
        motionHub.Unregister(observer.ConnectionId);

        Console.WriteLine("Player spatial AOI smoke passed.");
    }
}
