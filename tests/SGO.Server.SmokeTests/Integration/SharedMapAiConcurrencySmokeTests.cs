using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using SGO.Server.Application.Combat;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.World;
using SGO.Server.Domain.Sessions;
using SGO.Server.Domain.World;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class SharedMapAiConcurrencySmokeTests
{
    public static void Run(
        CombatService combat,
        WorldRuntimeService worldRuntime,
        SqliteGameStore store,
        ServerBroadcastHub broadcasts,
        AccountSessionRegistry accounts)
    {
        const int mapId = 990;
        var map = worldRuntime.GetMap(mapId);
        var sessions = new List<(
            long AccountId,
            long CharacterId,
            ClientSession Session,
            Guid ConnectionId)>();

        lock (map.SyncRoot)
        {
            map.Monsters.Clear();
            for (var i = 0; i < 10; i++)
            {
                var x = 10_000.0 + i * 2_000.0;
                var y = 10_000.0;
                var entityId = 9_900_000 + i;
                map.Monsters[entityId] = new MonsterRuntimeState
                {
                    EntityId = entityId,
                    EnemyId = 990_000 + i,
                    MapId = mapId,
                    Name = $"Synthetic-{i:D2}",
                    Sprite = "",
                    Level = 1,
                    MaxHp = 100,
                    CurrentHp = 100,
                    SpawnX = x,
                    SpawnY = y,
                    X = x,
                    Y = y,
                    WanderCenterX = x,
                    WanderCenterY = y
                };
            }
        }

        var ticker = new SharedMapAiTicker(
            combat,
            worldRuntime,
            store,
            broadcasts,
            accounts,
            NullLogger<SharedMapAiTicker>.Instance);

        try
        {
            for (var i = 0; i < 10; i++)
            {
                var unique =
                    Guid.NewGuid()
                        .ToString("N")[..8];
                var username =
                    $"ai{i:D2}-{unique}";
                var registered = store.Register(
                    username,
                    "test-password",
                    "");
                Check(
                    registered.Success,
                    $"register shared AI player {i} " +
                    $"(username={username}, message={registered.Message})");

                var accountId = store.Login(
                    username,
                    "test-password");
                Check(
                    accountId.HasValue,
                    $"login shared AI player {i}");

                using var create = Json(
                    JsonSerializer.Serialize(new
                    {
                        Name = $"SharedAi{i:D2}",
                        Password = "1234",
                        Race = i % 2,
                        Hair = 1,
                        HairColor = 1,
                        Str = 5,
                        Dex = 5,
                        Con = 5,
                        Wis = 5
                    }));
                Check(
                    store.Create(
                        accountId!.Value,
                        create.RootElement).Success,
                    $"create shared AI character {i}");

                var characterId = store.Characters(
                        accountId.Value)
                    .Select(x =>
                        Convert.ToInt64(x["DBID"]))
                    .First(x => x > 0);

                var x = 10_000.0 + i * 2_000.0;
                var y = 10_000.0;
                store.UpdateLocation(
                    accountId.Value,
                    characterId,
                    mapId,
                    (int)x,
                    (int)y);

                var registration = broadcasts.Register();
                accounts.Register(
                    accountId.Value,
                    registration.ConnectionId);

                var session = new ClientSession
                {
                    ConnectionId = registration.ConnectionId,
                    AccountId = accountId.Value,
                    CharacterId = characterId,
                    CurrentMapId = mapId,
                    LastX = x,
                    LastY = y,
                    WorldReady = true
                };

                broadcasts.UpdatePresence(
                    registration.ConnectionId,
                    mapId,
                    characterId,
                    accountId.Value,
                    x,
                    y);

                ticker.Register(session);
                sessions.Add((
                    accountId.Value,
                    characterId,
                    session,
                    registration.ConnectionId));
            }

            var firstVitals = sessions[0];
            var firstHp = store.GetCharacterHp(
                firstVitals.AccountId,
                firstVitals.CharacterId);
            Check(
                firstHp.HasValue && firstHp.Value > 0,
                "shared AI lightweight HP snapshot reads the live character without full bootstrap");

            var tickAt =
                DateTimeOffset.UtcNow.AddSeconds(1);
            var tickedMaps =
                ticker.TickOnce(tickAt);

            Check(
                tickedMaps == 1,
                "ten players on one map still produce exactly one shared map tick");

            var firstDiagnostics =
                ticker.LastDiagnostics;
            Check(
                firstDiagnostics.TickAt == tickAt &&
                firstDiagnostics.ActiveSessions == 10 &&
                firstDiagnostics.ActiveMaps == 1 &&
                firstDiagnostics.TickedMaps == 1 &&
                firstDiagnostics.MonsterCount == 10 &&
                firstDiagnostics.AssignedMonsterCount == 10 &&
                firstDiagnostics.ElapsedMs >= 0,
                "shared AI diagnostics report sessions maps monsters assignments and elapsed time");

            var firstProfile = ticker.LastProfile;
            Check(
                firstProfile.TotalMs >= 0 &&
                firstProfile.SessionSnapshotMs >= 0 &&
                firstProfile.PlayerSnapshotMs >= 0 &&
                firstProfile.RespawnMs >= 0 &&
                firstProfile.AssignmentMs >= 0 &&
                firstProfile.SessionLockWaitMs >= 0 &&
                firstProfile.TickAiMs >= 0 &&
                firstProfile.DirectPublishMs >= 0 &&
                firstProfile.OtherMs >= 0 &&
                firstProfile.TotalMs + 0.001 >=
                    firstProfile.SessionSnapshotMs +
                    firstProfile.PlayerSnapshotMs +
                    firstProfile.AssignmentMs +
                    firstProfile.SessionLockWaitMs +
                    firstProfile.TickAiMs +
                    firstProfile.DirectPublishMs,
                "shared AI profile exposes non-negative phase timings without changing the tick contract");

            lock (map.SyncRoot)
            {
                Check(
                    map.Monsters.Count == 10 &&
                    map.Monsters.Values.All(x =>
                        x.LastAiAt == tickAt),
                    "one shared tick advances every synthetic monster exactly in the same scheduler pass");

                for (var i = 0; i < 10; i++)
                {
                    var monster =
                        map.Monsters[9_900_000 + i];
                    Check(
                        monster.HasTarget &&
                        monster.TargetCharacterId ==
                            sessions[i].CharacterId,
                        $"nearest-player arbitration assigns synthetic monster {i} to exactly one expected owner");
                }
            }

            Check(
                sessions.All(x =>
                    x.Session.LastMonsterAiTick ==
                        tickAt),
                "all ten owner sessions participate without per-socket duplicate map ticks");

            var secondAt =
                tickAt.AddMilliseconds(250);
            Check(
                ticker.TickOnce(secondAt) == 1,
                "next 250ms scheduler pass still ticks the map once");

            lock (map.SyncRoot)
            {
                Check(
                    map.Monsters.Values.All(x =>
                        x.LastAiAt == secondAt),
                    "second shared pass advances each monster once at the new scheduler timestamp");
            }
        }
        finally
        {
            foreach (var entry in sessions)
            {
                ticker.Unregister(entry.Session);
                accounts.Unregister(entry.ConnectionId);
                broadcasts.Unregister(entry.ConnectionId);
            }

            lock (map.SyncRoot)
                map.Monsters.Clear();
        }
    }
}
