using System.Text.Json;
using SGO.Server.Application.Messaging;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.World;

/// <summary>
/// Repeated HP/state-only refreshes must not rescan the entire nearby observer
/// grid. The newest authoritative Spawn factory must still reach late
/// observers, and even a same-cell coordinate move must be reconciled.
/// </summary>
internal static class WorldEntityAoiStationaryRefreshSmokeTests
{
    public static void Run()
    {
        const long entityId = 6_500_101;
        const int mapId = 13;
        var aoi = new WorldEntitySpatialAoi();

        var near = new OnlinePlayerPresence(
            Guid.NewGuid(), 1, 1, mapId, 0, 0);
        var far = new OnlinePlayerPresence(
            Guid.NewGuid(), 2, 2, mapId, 9000, 0);

        static WorldAoiEntity Ground(
            long id, int map, double x, double y, int revision)
            => new(id, 3, map, x, y,
                () => new
                {
                    Type = "SpawnEntity",
                    EntityID = id,
                    EntityType = 3,
                    LocX = x,
                    LocY = y,
                    Revision = revision
                });

        Check(aoi.UpsertObserver(near).Count == 0 &&
              aoi.UpsertObserver(far).Count == 0,
            "near and far AOI observers register without ghosts");

        var firstSpawn = aoi.UpsertEntity(
            Ground(entityId, mapId, 2599, 0, 1));
        Check(firstSpawn.Count == 1 &&
              firstSpawn[0].Appeared &&
              firstSpawn[0].ObserverConnectionId == near.ConnectionId,
            "initial ground spawn targets only the observer within enter radius");

        // This is the hot path: unchanged coordinates but current HP, timer
        // or sprite payload refreshed thousands of times by runtime ticks.
        for (var revision = 2; revision <= 1001; revision++)
        {
            Check(aoi.UpsertEntity(
                      Ground(entityId, mapId, 2599, 0, revision)).Count == 0,
                "stationary AOI state refresh never duplicates spawn " + revision);
        }

        Check(aoi.IsVisible(near.ConnectionId, entityId) &&
              !aoi.IsVisible(far.ConnectionId, entityId) &&
              aoi.EntityCount == 1,
            "stationary refresh preserves known-set and entity cardinality");

        // The far observer moves into range and must receive revision 1001,
        // not stale revision 1 from the initial spawn factory.
        var arrived = aoi.UpsertObserver(far with { X = 2600 });
        Check(arrived.Count == 1 &&
              arrived[0].Appeared &&
              arrived[0].Entity.EntityId == entityId &&
              JsonSerializer.Serialize(
                  arrived[0].Entity.SpawnPacketFactory())
                  .Contains("\"Revision\":1001", StringComparison.Ordinal),
            "late observer receives the newest stationary entity snapshot");

        // Coordinate changes *within the same 1024-cell* must not use the
        // state-only short circuit; crossing exit radius still despawns.
        var moved = aoi.UpsertEntity(
            Ground(entityId, mapId, 3001, 0, 1002));
        Check(moved.Count == 1 &&
              !moved[0].Appeared &&
              moved[0].ObserverConnectionId == near.ConnectionId &&
              !aoi.IsVisible(near.ConnectionId, entityId),
            "same-cell coordinate movement crossing exit radius despawns near observer");
        Check(aoi.IsVisible(far.ConnectionId, entityId),
            "moving entity remains visible to other nearby observers");

        // A new entity incarnation using the same ID on another map must
        // reindex map/type and never leak between maps.
        aoi.UpsertEntity(
            Ground(entityId, 15, 0, 0, 1003));
        Check(!aoi.IsVisible(far.ConnectionId, entityId) &&
              !aoi.IsVisible(near.ConnectionId, entityId),
            "entity map transition removes all old-map visibility");
        Check(aoi.UpsertObserver(new OnlinePlayerPresence(
                  Guid.NewGuid(), 3, 3, 15, 0, 0))
                  .Single().Entity.MapId == 15,
            "new-map late observer sees the replacement incarnation");

        Console.WriteLine("World entity stationary-refresh AOI smoke passed.");
    }
}
