using System.Text.Json;
using SGO.Server.Application;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.WorldData;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

/// <summary>
/// Reproduces the missing-monster regression after asynchronously loading a map.
/// The post-load WorldReady packet must reconcile the entire world exactly once.
/// </summary>
internal static class MapReadySnapshotSmokeTests
{
    public static void Run(
        PacketRouter router,
        ClientSession session,
        WorldDataService world,
        SqliteGameStore store)
    {
        var mapId = session.CurrentMapId;
        var priorReady = session.WorldReady;
        var priorRetries = session.WorldSnapshotRetriesRemaining;
        var priorRetryTime = session.NextWorldSnapshotAt;

        try
        {
            var testGround = store.CreateGroundItem(
                session.CharacterId!.Value,
                0,
                1001,
                1,
                mapId,
                session.LastX + 16,
                session.LastY + 16,
                lifetimeSeconds: 60);
            Check(testGround is not null,
                "create an unexpired ground item for map return regression");
            session.WorldSnapshotRetriesRemaining = 8;
            using var ready = Json(JsonSerializer.Serialize(new
            {
                Type = "WorldReady",
                MapID = mapId
            }));

            var packets = router.Handle(ready.RootElement, session);
            var serialized = packets
                .Select(packet => JsonSerializer.Serialize(packet))
                .ToArray();

            Check(serialized.Any(x =>
                    x.Contains("\"Type\":\"WorldReadyResponse\"") &&
                    x.Contains("\"Success\":true")),
                "WorldReady acknowledges a completed map transition");

            var npcCount = serialized.Count(x =>
                x.Contains("\"EntityType\":2"));
            Check(npcCount == world.GetNpcs(mapId).Count,
                "post-map WorldReady resends every NPC");

            var monsterCount = serialized.Count(x =>
                x.Contains("\"EntityType\":1"));
            Check(monsterCount > 0,
                "post-map WorldReady resends living monsters");

            Check(serialized.Any(x =>
                    x.Contains("\"EntityID\":" + testGround!.EntityId) &&
                    x.Contains("\"EntityType\":3") &&
                    x.Contains("\"IsSnapshot\":true")),
                "WorldReady restores the same unexpired ground item as an immediately visible snapshot");

            Check(session.WorldSnapshotRetriesRemaining == 0,
                "full map-ready snapshot cancels duplicate periodic retries");
            Check(session.WorldReady,
                "map-ready restores server AI readiness");
        }
        finally
        {
            session.WorldReady = priorReady;
            session.WorldSnapshotRetriesRemaining = priorRetries;
            session.NextWorldSnapshotAt = priorRetryTime;
        }
    }
}
