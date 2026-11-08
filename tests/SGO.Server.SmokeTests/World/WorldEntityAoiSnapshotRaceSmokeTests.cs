using SGO.Server.Application.Messaging;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.World;

/// <summary>
/// Deterministic snapshot/insert interleavings reproduce a bug where a
/// WorldReady SELECT taken before another player's DropItem committed could
/// purge the new drop from AOI even though SQLite still contained the row.
/// </summary>
internal static class WorldEntityAoiSnapshotRaceSmokeTests
{
    private const int MapId = 13;

    private static WorldAoiEntity Ground(long entityId)
        => new(entityId, 3, MapId, 100, 100,
            () => new
            {
                Type = "SpawnEntity",
                EntityID = entityId,
                EntityType = 3,
                LocX = 100,
                LocY = 100
            });

    public static void Run()
    {
        StaleSnapshotCannotPurgeLaterGroundDrop();
        BroadcastHubPreservesLateDropDuringWorldReadyPrune();
        Console.WriteLine("World entity snapshot-race AOI smoke passed.");
    }

    private static void StaleSnapshotCannotPurgeLaterGroundDrop()
    {
        var aoi = new WorldEntitySpatialAoi();
        var observer = new OnlinePlayerPresence(
            Guid.NewGuid(), 1, 1, MapId, 0, 0);
        aoi.UpsertObserver(observer);

        const long keepId = 6_000_501;
        const long staleId = 6_000_502;
        const long concurrentId = 6_000_503;
        aoi.UpsertEntity(Ground(keepId));
        aoi.UpsertEntity(Ground(staleId));
        Check(aoi.VisibleEntities(observer.ConnectionId).Count == 2,
            "observer initially sees two persisted ground entities");

        // Player A begins SELECT ground_items. Player B concurrently commits
        // another drop and indexes it before A runs the stale AOI prune.
        var snapshotGeneration = aoi.CaptureEntityGeneration();
        var authoritativeIds = new HashSet<long> { keepId };
        var added = aoi.UpsertEntity(Ground(concurrentId));
        Check(added.Count == 1 && added[0].Appeared,
            "new concurrent ground drop becomes AOI-visible");

        var removed = aoi.RemoveMissingEntities(
            MapId, 3, authoritativeIds, snapshotGeneration);
        Check(removed.Count == 1 &&
              removed[0].Entity.EntityId == staleId &&
              !removed[0].Appeared,
            "old snapshot removes only rows indexed before SELECT");
        Check(aoi.EntityCount == 2 &&
              aoi.IsVisible(observer.ConnectionId, keepId) &&
              aoi.IsVisible(observer.ConnectionId, concurrentId) &&
              !aoi.IsVisible(observer.ConnectionId, staleId),
            "old DB snapshot cannot hide a newly committed drop");

        // A subsequent fresh DB SELECT can legitimately expire/remove the
        // previously-new row. Version boundary must not block real cleanup.
        var newerGeneration = aoi.CaptureEntityGeneration();
        var expired = aoi.RemoveMissingEntities(
            MapId, 3, authoritativeIds, newerGeneration);
        Check(expired.Count == 1 &&
              expired[0].Entity.EntityId == concurrentId &&
              !aoi.IsVisible(observer.ConnectionId, concurrentId) &&
              aoi.EntityCount == 1,
            "later authoritative snapshot can remove a genuinely missing drop");

        // The existing three-arg API remains supported for callers without
        // a snapshot boundary, preserving prior test helper behavior.
        Check(aoi.RemoveMissingEntities(
                MapId, 3, new HashSet<long>()).Count == 1 &&
              aoi.EntityCount == 0,
            "legacy unconditional reconciliation semantics remain unchanged");
    }

    private static void BroadcastHubPreservesLateDropDuringWorldReadyPrune()
    {
        var hub = new ServerBroadcastHub();
        var session = hub.Register();
        try
        {
            hub.UpdatePresence(
                session.ConnectionId, MapId, 1, 1, 0, 0);
            hub.ReconcileWorldAoi(session.ConnectionId);

            const long earlyId = 6_001_100;
            const long lateId = 6_001_101;
            hub.UpsertWorldEntity(Ground(earlyId));

            // Equivalent to a slow WorldReady DB query followed by a
            // concurrent DropItemRequest from another socket.
            var snapshotGeneration =
                hub.CaptureWorldEntityGeneration();
            hub.UpsertWorldEntity(Ground(lateId));

            var changes = hub.RemoveMissingWorldEntities(
                MapId,
                3,
                new HashSet<long> { earlyId },
                snapshotGeneration);
            Check(changes.Count == 0 &&
                  hub.IsWorldAoiVisible(session.ConnectionId, lateId) &&
                  hub.VisibleWorldEntities(session.ConnectionId).Count == 2,
                "hub preserves a newly indexed ground entity against a stale WorldReady result");
        }
        finally
        {
            hub.Unregister(session.ConnectionId);
        }
    }
}
