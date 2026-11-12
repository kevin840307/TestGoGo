using System.Text.Json;
using SGO.Server.Application.Combat;
using SGO.Server.Application.World;
using SGO.Server.Domain.Sessions;
using SGO.Server.Domain.World;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.Infrastructure.WorldData;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

// Real CombatService + real SQLite + real Map13 Blocking.dat integration,
// not just a synthetic ApplyDamageBatch contract probe.
internal static class AtomicMonsterHitSmokeTests
{
    public static void Run(
        CombatService combat,
        WorldRuntimeService worldRuntime,
        WorldDataService world,
        BlockingMapService blocking,
        SqliteGameStore store)
    {
        const int mapId = 13;
        const int firstEntityId = 9_990_091;
        const int secondEntityId = 9_990_092;
        var navigation = new MonsterNavigationService(blocking);
        (double X, double Y, double TargetX, double TargetY)? spot = null;
        foreach (var enemy in world.GetEnemies(mapId))
        {
            foreach (var (dx, dy) in new (double X, double Y)[]
            {
                (60, 0), (-60, 0), (0, 60), (0, -60)
            })
            {
                if (!navigation.IsDirectClear(
                        mapId, enemy.X, enemy.Y,
                        enemy.X + dx, enemy.Y + dy) ||
                    !navigation.IsDirectClear(
                        mapId, enemy.X + 4, enemy.Y,
                        enemy.X + dx, enemy.Y + dy))
                    continue;
                spot = (enemy.X, enemy.Y,
                    enemy.X + dx, enemy.Y + dy);
                break;
            }
            if (spot.HasValue)
                break;
        }

        Check(spot.HasValue,
            "atomic melee fixture finds two clear Blocking.dat lines in Map13");
        var position = spot!.Value;
        var username = "aihit-" + Guid.NewGuid().ToString("N")[..12];
        Check(store.Register(username, "test-password", "").Success,
            "atomic AI fixture account registered");
        var accountId = store.Login(username, "test-password");
        Check(accountId.HasValue, "atomic AI fixture account logged in");
        using var create = JsonDocument.Parse(
            """{"Name":"AtomicHitter","Password":"1234","Race":0,"Hair":1,"Str":5,"Dex":5,"Con":5,"Wis":5}""");
        Check(store.Create(accountId!.Value, create.RootElement).Success,
            "atomic AI fixture character created");
        var characterId = store.Characters(accountId.Value)
            .Select(ch => Convert.ToInt64(ch["DBID"]))
            .First(id => id > 0);
        Check(store.UpdateLocation(accountId.Value, characterId,
                mapId, (int)position.TargetX, (int)position.TargetY),
            "atomic AI character starts in Map13");
        Check(store.ApplyGrowthStats(accountId.Value, characterId,
                20_000, 1_000, 1_000,
                50, 1_000, 50, 1_000, 1, 20, 100) is not null,
            "atomic AI player receives high HP for nonlethal multi-hit path");

        var session = new ClientSession
        {
            ConnectionId = Guid.NewGuid(),
            AccountId = accountId.Value,
            CharacterId = characterId,
            CurrentMapId = mapId,
            LastX = position.TargetX,
            LastY = position.TargetY,
            WorldReady = true
        };
        var map = worldRuntime.GetMap(mapId);
        MonsterRuntimeState Spawn(int entityId, double x) => new()
        {
            EntityId = entityId,
            EnemyId = entityId,
            MapId = mapId,
            Name = "AtomicSmokeMonster",
            Sprite = "",
            Level = 1,
            MaxHp = 100,
            CurrentHp = 100,
            SpawnX = x,
            SpawnY = position.Y,
            X = x,
            Y = position.Y,
            WanderCenterX = x,
            WanderCenterY = position.Y,
            HasTarget = true,
            TargetCharacterId = characterId,
            AiState = MonsterAiState.Attack,
            HoldingAttackRange = true
        };
        var first = Spawn(firstEntityId, position.X);
        var second = Spawn(secondEntityId, position.X + 4);
        lock (map.SyncRoot)
        {
            Check(!map.Monsters.ContainsKey(firstEntityId) &&
                  !map.Monsters.ContainsKey(secondEntityId),
                "synthetic entity ids do not collide with Map13 runtime");
            map.Monsters.Add(firstEntityId, first);
            map.Monsters.Add(secondEntityId, second);
        }

        try
        {
            var hpBefore = Convert.ToInt32(
                store.GetCharacter(accountId.Value, characterId)!["HP"]);
            var batchesBefore = combat.MonsterAiDamageBatches;
            var now = DateTimeOffset.UtcNow;
            var packets = combat.TickAi(
                session, accountId.Value,
                position.TargetX, position.TargetY,
                now, [firstEntityId, secondEntityId]);
            var serialized = packets
                .Select(packet => JsonSerializer.SerializeToElement(packet)).ToArray();
            var damage = serialized.Where(x =>
                x.TryGetProperty("Type", out var type) &&
                type.GetString() == "EntityDamage" &&
                x.GetProperty("TargetType").GetInt32() == 0).ToArray();
            var hpAfter = Convert.ToInt32(
                store.GetCharacter(accountId.Value, characterId)!["HP"]);
            var batchesAfter = combat.MonsterAiDamageBatches;

            Check(batchesAfter.Enabled &&
                  batchesAfter.BatchTransactions ==
                      batchesBefore.BatchTransactions + 1 &&
                  batchesAfter.HitsInBatches ==
                      batchesBefore.HitsInBatches + 2,
                "real TickAi settles two owner-local monster hits in one durable SQLite batch");
            Check(damage.Length == 2 &&
                  damage[0].GetProperty("AttackerID").GetInt32() ==
                      firstEntityId &&
                  damage[1].GetProperty("AttackerID").GetInt32() ==
                      secondEntityId,
                "original per-monster attacker and packet ordering survive batch commit");
            Check(damage.Length == 2 &&
                  damage[0].GetProperty("CurrentHP").GetInt32() ==
                      hpBefore - damage[0].GetProperty("Damage").GetInt32() &&
                  damage[1].GetProperty("CurrentHP").GetInt32() == hpAfter &&
                  hpAfter == hpBefore -
                      damage.Sum(x =>
                          x.GetProperty("Damage").GetInt32()),
                "each EntityDamage contains the committed intermediate HP and final DB HP");
            var causality = serialized
                .Where(x => x.TryGetProperty("Type", out var type) &&
                    (type.GetString() is "EntityDamage" or "PlayerStatusSync"))
                .Select(x => x.GetProperty("Type").GetString()).ToArray();
            Check(causality.SequenceEqual(new[]
                {
                    "EntityDamage", "PlayerStatusSync",
                    "EntityDamage", "PlayerStatusSync"
                }) &&
                  !serialized.Any(x =>
                      x.TryGetProperty("Type", out var type) &&
                      type.GetString() == "EntityDeath"),
                "damage/status packets retain old FIFO pairing and nonlethal hits never emit death");

            // Force an already-low-HP player: the AI must use the original
            // immediate SQL path, nominate exactly one killer, and stop hits.
            Check(store.ApplyDamage(accountId.Value, characterId,
                hpAfter - 1) is not null &&
                  Convert.ToInt32(store.GetCharacter(
                      accountId.Value, characterId)!["HP"]) == 1,
                "prepare a living 1 HP player for death-causality regression");
            first.HasTarget = true;
            second.HasTarget = true;
            first.TargetCharacterId = characterId;
            second.TargetCharacterId = characterId;
            first.LastAttackAt = DateTimeOffset.MinValue;
            second.LastAttackAt = DateTimeOffset.MinValue;
            var lethal = combat.TickAi(
                session, accountId.Value,
                position.TargetX, position.TargetY,
                now.AddSeconds(2), [firstEntityId, secondEntityId])
                .Select(packet => JsonSerializer.SerializeToElement(packet)).ToArray();
            var deaths = lethal.Where(x =>
                x.TryGetProperty("Type", out var type) &&
                type.GetString() == "EntityDeath" &&
                x.GetProperty("EntityType").GetInt32() == 0).ToArray();
            var lethalHits = lethal.Where(x =>
                x.TryGetProperty("Type", out var type) &&
                type.GetString() == "EntityDamage" &&
                x.GetProperty("TargetType").GetInt32() == 0).ToArray();

            Check(deaths.Length == 1 &&
                  deaths[0].GetProperty("KillerID").GetInt32() ==
                      firstEntityId &&
                  lethalHits.Length == 1 &&
                  Convert.ToInt32(store.GetCharacter(
                      accountId.Value, characterId)!["HP"]) == 0 &&
                  combat.MonsterAiDamageBatches.BatchTransactions ==
                      batchesAfter.BatchTransactions,
                "at 1 HP the legacy immediate path emits one exact killer, one hit and zero phantom deaths");
        }
        finally
        {
            lock (map.SyncRoot)
            {
                map.Monsters.Remove(firstEntityId);
                map.Monsters.Remove(secondEntityId);
            }
        }
    }
}
