using SGO.Server.Application.Abstractions;
using SGO.Server.Domain.Sessions;
using SGO.Server.Domain.World;
using SGO.Server.Infrastructure.Legacy.World;

namespace SGO.Server.Application.Combat;

public sealed class CombatService
{
    private static readonly TimeSpan RespawnDelay = TimeSpan.FromSeconds(10);

    private readonly IGameStore _store;
    private readonly LegacyWorldService _world;

    public CombatService(
        IGameStore store,
        LegacyWorldService world)
    {
        _store = store;
        _world = world;
    }

    public IReadOnlyList<object> EnterMap(
        ClientSession session,
        int mapId)
    {
        session.Monsters.Clear();

        var packets = new List<object>();
        packets.AddRange(_world.BuildNpcSpawnPackets(mapId));

        if (_world.IsCityMap(mapId))
            return packets;

        foreach (var enemy in _world.GetEnemies(mapId))
        {
            if (enemy.Level >= 65535)
                continue;

            var state = new MonsterRuntimeState
            {
                EntityId = enemy.EntityId,
                LegacyEnemyId = enemy.EnemyId,
                MapId = enemy.MapId,
                Name = enemy.Name,
                Sprite = enemy.Sprite,
                Level = Math.Max(1, enemy.Level),
                MaxHp = Math.Max(1, enemy.MaxHp),
                CurrentHp = Math.Max(1, enemy.MaxHp),
                X = enemy.X,
                Y = enemy.Y
            };

            session.Monsters[state.EntityId] = state;
            packets.Add(BuildMonsterSpawnPacket(state));
        }

        return packets;
    }

    public IReadOnlyList<object> TickRespawns(
        ClientSession session,
        DateTimeOffset now)
    {
        if (session.Monsters.Count == 0)
            return [];

        var packets = new List<object>();
        foreach (var monster in session.Monsters.Values)
        {
            if (monster.RespawnAt is null ||
                monster.RespawnAt > now)
                continue;

            monster.CurrentHp = monster.MaxHp;
            monster.RespawnAt = null;
            packets.Add(BuildMonsterSpawnPacket(monster));
        }

        return packets;
    }

    public IReadOnlyList<object> Attack(
        ClientSession session,
        long accountId,
        int targetEntityId,
        int targetType)
    {
        if (targetType != 1 ||
            !session.CharacterId.HasValue ||
            !session.Monsters.TryGetValue(targetEntityId, out var monster) ||
            monster.MapId != session.CurrentMapId ||
            !monster.IsAlive)
            return [];

        var character = _store.GetCharacter(
            accountId,
            session.CharacterId.Value);

        if (character is null)
            return [];

        var playerX = Convert.ToDouble(character["LocX"]);
        var playerY = Convert.ToDouble(character["LocY"]);
        var attackRange = Math.Max(
            80,
            Convert.ToInt32(character["AttackRange"]));

        var dx = playerX - monster.X;
        var dy = playerY - monster.Y;
        var allowedRange = attackRange + 120;

        if ((dx * dx) + (dy * dy) >
            allowedRange * allowedRange)
        {
            return [new
            {
                Type = "SystemMessageFlash",
                Message = "目標距離太遠"
            }];
        }

        var damage = Math.Max(
            1,
            Convert.ToInt32(character["Attack"]));

        monster.CurrentHp = Math.Max(
            0,
            monster.CurrentHp - damage);

        var packets = new List<object>
        {
            new
            {
                Type = "EntityDamage",
                TargetID = monster.EntityId,
                TargetType = 1,
                Damage = damage,
                CurrentHP = monster.CurrentHp,
                MaxHP = monster.MaxHp,
                IsCrit = false,
                SkillID = 0,
                SkillLevel = 1,
                AttackerID = session.CharacterId.Value
            }
        };

        if (monster.CurrentHp == 0)
        {
            monster.RespawnAt =
                DateTimeOffset.UtcNow + RespawnDelay;

            packets.Add(new
            {
                Type = "EntityDeath",
                EntityID = monster.EntityId,
                EntityType = 1,
                KillerID = session.CharacterId.Value
            });
        }

        return packets;
    }

    private static object BuildMonsterSpawnPacket(
        MonsterRuntimeState monster)
        => new
        {
            Type = "SpawnEntity",
            EntityID = monster.EntityId,
            EntityType = 1,
            Name = monster.Name,
            LocX = monster.X,
            LocY = monster.Y,
            ExtraData = new
            {
                Sprite = monster.Sprite,
                Level = monster.Level,
                MaxHP = monster.MaxHp,
                CurrentHP = monster.CurrentHp,
                Scale = 1.0,
                RequireCtrlToAttack = false,
                IsWorldBoss = false,
                LegacyEnemyID = monster.LegacyEnemyId
            }
        };
}
