using System.Text.Json;
using SGO.Server.Application.Combat;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.Infrastructure.WorldData;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class MonsterConcurrencySmokeTests
{
    public static void Run(
        CombatService combat,
        SqliteGameStore store,
        WorldDataService world)
    {
        const int mapId = 7;
        var enemy = world.GetEnemies(mapId)
            .First(x => x.Level < 65535);

        var players = new List<(
            long AccountId,
            long CharacterId,
            ClientSession Session)>();

        for (var i = 0; i < 10; i++)
        {
            // Registration enforces 3–40 characters. Preserve the full
            // 32-digit GUID for test isolation with a short prefix (35 total).
            var username =
                $"mr-{Guid.NewGuid():N}";
            var registered =
                store.Register(
                    username,
                    "test-password",
                    "");

            Check(
                registered.Success,
                $"register monster race player {i}: {registered.Message}");

            var accountId =
                store.Login(
                    username,
                    "test-password");

            Check(
                accountId.HasValue,
                $"login monster race player {i}");

            using var create = Json(
                JsonSerializer.Serialize(new
                {
                    Name = $"KillRace{i:D2}",
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
                $"create monster race character {i}");

            var characterId = store.Characters(
                    accountId.Value)
                .Select(x =>
                    Convert.ToInt64(x["DBID"]))
                .First(x => x > 0);

            store.UpdateLocation(
                accountId.Value,
                characterId,
                mapId,
                enemy.X,
                enemy.Y);

            var session = new ClientSession
            {
                ConnectionId = Guid.NewGuid(),
                AccountId = accountId.Value,
                CharacterId = characterId,
                CurrentMapId = mapId,
                LastX = enemy.X,
                LastY = enemy.Y,
                WorldReady = true
            };

            combat.EnterMap(
                session,
                mapId);

            players.Add((
                accountId.Value,
                characterId,
                session));
        }

        var target = players[0].Session.Monsters
            .Values
            .First(x => x.IsAlive);

        Check(
            players.All(x =>
                x.Session.Monsters.TryGetValue(
                    target.EntityId,
                    out var shared) &&
                ReferenceEquals(
                    shared,
                    target)),
            "ten sessions reference one shared monster runtime");

        target.CurrentHp = 1;
        target.RespawnAt = null;

        foreach (var player in players)
        {
            player.Session.LastX = target.X;
            player.Session.LastY = target.Y;
        }

        var attacks = players
            .Select(player =>
                Task.Run(() =>
                    combat.Attack(
                        player.Session,
                        player.AccountId,
                        target.EntityId,
                        1)))
            .ToArray();

        Task.WaitAll(attacks);

        var serialized = attacks
            .Select(task =>
                JsonSerializer.Serialize(
                    task.Result))
            .ToArray();

        Check(
            serialized.Count(json =>
                json.Contains(
                    "\"Type\":\"EntityDeath\"")) == 1,
            "exactly one of ten concurrent attacks settles monster death");

        Check(
            serialized.Count(json =>
                json.Contains(
                    "目標已死亡")) == 9,
            "nine losing concurrent attacks observe the already-dead shared monster");

        Check(
            target.CurrentHp == 0 &&
            !target.IsAlive &&
            target.RespawnAt.HasValue,
            "shared monster remains in one authoritative dead state after race");

        var respawnAt =
            target.RespawnAt!.Value
                .AddMilliseconds(1);

        combat.TickRespawns(
            players[0].Session,
            respawnAt);

        Check(
            target.IsAlive &&
            target.CurrentHp == target.MaxHp &&
            target.RespawnAt is null,
            "monster concurrency test restores target through normal respawn path");
    }
}
