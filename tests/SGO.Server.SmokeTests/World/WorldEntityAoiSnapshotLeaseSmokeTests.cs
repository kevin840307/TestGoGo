using System.Text.Json;
using SGO.Server.Application.Messaging;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.World;

/// <summary>
/// A stale WorldReady SELECT must not resurrect a ground row already picked up,
/// or replace a newer authoritative location/timer. This tests the exact
/// interleavings across independent sessions rather than relying on sleep.
/// </summary>
internal static class WorldEntityAoiSnapshotLeaseSmokeTests
{
    private const int MapId = 13;
    private static WorldAoiEntity Ground(long id, double x = 100, int revision = 0)
        => new(id, 3, MapId, x, 100,
            () => new
            {
                Type = "SpawnEntity",
                EntityID = id,
                LocX = x,
                Revision = revision
            });

    public static void Run()
    {
        PickupDuringSnapshotCannotResurrectItem();
        PickupBeforeFirstAoiInsertStillBlocksOldSnapshot();
        NewerLiveStateWinsAgainstOlderSnapshot();
        OverlappingSnapshotsKeepTombstonesUntilLastReader();
        HubConditionalIndexingPreservesWorldReadyConsistency();
        Console.WriteLine("World entity snapshot lease race smoke passed.");
    }

    private static void PickupDuringSnapshotCannotResurrectItem()
    {
        var aoi = new WorldEntitySpatialAoi();
        var viewer = new OnlinePlayerPresence(
            Guid.NewGuid(), 1, 1, MapId, 0, 0);
        aoi.UpsertObserver(viewer);
        var ground = Ground(6_008_101);
        aoi.UpsertEntity(ground);

        // A reads the row from SQLite. B picks it up, commits and clears
        // the AOI. A then attempts to index the now-stale result.
        using (var snapshot = aoi.BeginSnapshot())
        {
            Check(aoi.RemoveEntity(ground.EntityId).Count == 1,
                "pickup removes old observer visibility");
            var accepted = aoi.TryUpsertEntityFromSnapshot(
                ground, snapshot, out var changes);
            Check(!accepted && changes.Count == 0 &&
                  aoi.EntityCount == 0 &&
                  !aoi.IsVisible(viewer.ConnectionId, ground.EntityId),
                "stale DB snapshot cannot revive a concurrently picked-up item");
        }

        // Once the old SELECT has finished, later real authoritative
        // operations are allowed again: tombstones are not permanent.
        using (var fresh = aoi.BeginSnapshot())
        {
            Check(aoi.TryUpsertEntityFromSnapshot(
                ground, fresh, out var changes) &&
                changes.Count == 1 && changes[0].Appeared,
                "new snapshot can legitimately index a current ground row");
        }
    }

    private static void PickupBeforeFirstAoiInsertStillBlocksOldSnapshot()
    {
        var aoi = new WorldEntitySpatialAoi();
        var item = Ground(6_008_105);
        using var snapshot = aoi.BeginSnapshot();

        // A's SELECT read the row while no observer had ever visited this
        // map. B picks up that row, but the AOI has not indexed it yet.
        Check(aoi.RemoveEntity(item.EntityId).Count == 0,
            "pickup on an as-yet unindexed map has no observer edge");
        Check(!aoi.TryUpsertEntityFromSnapshot(
                  item, snapshot, out var changes) &&
              changes.Count == 0 && aoi.EntityCount == 0,
            "unindexed pickup must still tombstone the stale DB row");
    }

    private static void NewerLiveStateWinsAgainstOlderSnapshot()
    {
        var aoi = new WorldEntitySpatialAoi();
        var viewer = new OnlinePlayerPresence(
            Guid.NewGuid(), 1, 1, MapId, 0, 0);
        aoi.UpsertObserver(viewer);
        const long id = 6_008_102;
        aoi.UpsertEntity(Ground(id, 120, 1));
        using var snapshot = aoi.BeginSnapshot();

        // World map updates can change a drop's real position after the
        // SELECT; an older factory must not put it back onto a blocked tile.
        aoi.UpsertEntity(Ground(id, 250, 2));
        Check(!aoi.TryUpsertEntityFromSnapshot(
                  Ground(id, 120, 1),
                  snapshot,
                  out var changes) &&
              changes.Count == 0 &&
              aoi.VisibleEntities(viewer.ConnectionId)
                  .Single().X == 250,
            "old snapshot cannot overwrite a newer moving entity");

        aoi.UpsertEntity(Ground(id, 250, 3));
        Check(!aoi.TryUpsertEntityFromSnapshot(
                  Ground(id, 120, 1),
                  snapshot,
                  out _) &&
              JsonSerializer.Serialize(
                  aoi.VisibleEntities(viewer.ConnectionId)
                      .Single().SpawnPacketFactory())
                  .Contains("\"Revision\":3", StringComparison.Ordinal),
            "stationary state updates also advance snapshot generation");
    }

    private static void OverlappingSnapshotsKeepTombstonesUntilLastReader()
    {
        var aoi = new WorldEntitySpatialAoi();
        var item = Ground(6_008_103);
        aoi.UpsertEntity(item);

        var older = aoi.BeginSnapshot();
        var newer = aoi.BeginSnapshot();
        try
        {
            aoi.RemoveEntity(item.EntityId);
            newer.Dispose();
            Check(!aoi.TryUpsertEntityFromSnapshot(
                      item, older, out var changes) &&
                  changes.Count == 0,
                "ending one of two in-flight snapshots must retain deletion history");
        }
        finally
        {
            newer.Dispose();
            older.Dispose();
        }

        using var next = aoi.BeginSnapshot();
        Check(aoi.TryUpsertEntityFromSnapshot(
                  item, next, out _) && aoi.EntityCount == 1,
            "tombstones are released after last snapshot ends");
    }

    private static void HubConditionalIndexingPreservesWorldReadyConsistency()
    {
        var hub = new ServerBroadcastHub();
        var viewer = hub.Register();
        try
        {
            hub.UpdatePresence(viewer.ConnectionId, MapId, 1, 1, 0, 0);
            hub.ReconcileWorldAoi(viewer.ConnectionId);
            var ground = Ground(6_008_104);
            hub.UpsertWorldEntity(ground);

            using var snapshot = hub.BeginWorldEntitySnapshot();
            hub.RemoveWorldEntity(ground.EntityId, notifyObservers: true);
            Check(!hub.TryUpsertWorldEntityFromSnapshot(
                      ground, snapshot, out var changes) &&
                  changes.Count == 0 &&
                  !hub.IsWorldAoiVisible(
                      viewer.ConnectionId, ground.EntityId),
                "broadcast hub rejects stale WorldReady item after pickup");
        }
        finally
        {
            hub.Unregister(viewer.ConnectionId);
        }
    }
}
