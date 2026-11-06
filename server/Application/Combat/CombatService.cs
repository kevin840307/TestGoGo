using SGO.Server.Application.Abstractions;
using SGO.Server.Domain.Sessions;
using SGO.Server.Domain.World;
using SGO.Server.Infrastructure.Legacy.World;

namespace SGO.Server.Application.Combat;

public sealed class CombatService
{
    private static readonly TimeSpan RespawnDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AiTickInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MonsterAttackCooldown = TimeSpan.FromMilliseconds(1400);

    private const double AggroRadius = 420.0;
    private const double LoseTargetRadius = 700.0;
    private const double StopDistance = 90.0;
    private const double MoveStep = 70.0;

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
                SpawnX = enemy.X,
                SpawnY = enemy.Y,
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
            monster.HasTarget = false;
            monster.X = monster.SpawnX;
            monster.Y = monster.SpawnY;
            packets.Add(BuildMonsterSpawnPacket(monster));
        }

        return packets;
    }

    public IReadOnlyList<object> TickAi(
        ClientSession session,
        long accountId,
        double playerX,
        double playerY,
        DateTimeOffset now)
    {
        if (now - session.LastMonsterAiTick < AiTickInterval)
            return [];

        session.LastMonsterAiTick = now;

        var packets = new List<object>();

        foreach (var monster in session.Monsters.Values)
        {
            if (!monster.IsAlive)
                continue;

            var dx = playerX - monster.X;
            var dy = playerY - monster.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);

            if (!monster.HasTarget && distance <= AggroRadius)
            {
                monster.HasTarget = true;
                packets.Add(new
                {
                    Type = "MonsterTarget",
                    EntityID = monster.EntityId,
                    HasTarget = true
                });
            }

            if (monster.HasTarget && distance > LoseTargetRadius)
            {
                monster.HasTarget = false;
                packets.Add(new
                {
                    Type = "MonsterTarget",
                    EntityID = monster.EntityId,
                    HasTarget = false
                });

                var homeDx = monster.SpawnX - monster.X;
                var homeDy = monster.SpawnY - monster.Y;
                var homeDistance = Math.Sqrt(
                    homeDx * homeDx + homeDy * homeDy);

                if (homeDistance > 1.0)
                {
                    var returnMoveDx = monster.SpawnX - monster.X;
                    var returnMoveDy = monster.SpawnY - monster.Y;
                    monster.X = monster.SpawnX;
                    monster.Y = monster.SpawnY;
                    packets.Add(BuildMovePacket(
                        monster,
                        monster.X,
                        monster.Y,
                        0.6,
                        returnMoveDx,
                        returnMoveDy));
                }

                continue;
            }

            if (monster.HasTarget &&
                distance <= StopDistance &&
                session.CharacterId.HasValue &&
                now - monster.LastAttackAt >= MonsterAttackCooldown)
            {
                var character = _store.GetCharacter(
                    accountId,
                    session.CharacterId.Value);

                if (character is not null &&
                    Convert.ToInt32(character["HP"]) > 0)
                {
                    var defense = Math.Max(
                        0,
                        Convert.ToInt32(character["Defense"]));

                    var rawDamage = Math.Max(
                        1,
                        monster.Level * 2);

                    var damage = Math.Max(
                        1,
                        rawDamage - defense / 5);

                    var updated = _store.ApplyDamage(
                        accountId,
                        session.CharacterId.Value,
                        damage);

                    if (updated is not null)
                    {
                        monster.LastAttackAt = now;
                        var currentHp = Convert.ToInt32(updated["HP"]);
                        var maxHp = Convert.ToInt32(updated["MaxHP"]);

                        packets.Add(new
                        {
                            Type = "EntityDamage",
                            TargetID = session.CharacterId.Value,
                            TargetType = 0,
                            Damage = damage,
                            CurrentHP = currentHp,
                            MaxHP = maxHp,
                            IsCrit = false,
                            SkillID = 0,
                            SkillLevel = 1,
                            AttackerID = monster.EntityId
                        });

                        packets.Add(new
                        {
                            Type = "PlayerStatusSync",
                            HP = currentHp,
                            MP = updated["MP"],
                            SP = updated["SP"],
                            MaxHP = maxHp,
                            MaxMP = updated["MaxMP"],
                            MaxSP = updated["MaxSP"]
                        });

                        if (currentHp <= 0)
                        {
                            packets.Add(new
                            {
                                Type = "EntityDeath",
                                EntityID = session.CharacterId.Value,
                                EntityType = 0,
                                KillerID = monster.EntityId
                            });

                            monster.HasTarget = false;
                        }
                    }
                }

                continue;
            }

            if (!monster.HasTarget || distance <= StopDistance)
                continue;

            var step = Math.Min(
                MoveStep,
                Math.Max(0.0, distance - StopDistance));

            if (step <= 0.0)
                continue;

            var nx = monster.X + dx / distance * step;
            var ny = monster.Y + dy / distance * step;
            var moveDx = nx - monster.X;
            var moveDy = ny - monster.Y;

            monster.X = nx;
            monster.Y = ny;

            packets.Add(BuildMovePacket(
                monster,
                nx,
                ny,
                0.5,
                moveDx,
                moveDy));
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
            monster.HasTarget = false;

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

    private static object BuildMovePacket(
        MonsterRuntimeState monster,
        double x,
        double y,
        double speedTime,
        double moveDx,
        double moveDy)
        => new
        {
            Type = "MoveEntity",
            EntityID = monster.EntityId,
            X = x,
            Y = y,
            Dir = DirectionFromDelta(moveDx, moveDy),
            SpeedTime = speedTime
        };

    private static string DirectionFromDelta(
        double dx,
        double dy)
    {
        if (Math.Abs(dx) < 0.001 &&
            Math.Abs(dy) < 0.001)
            return "1";

        var degrees = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (degrees < 0)
            degrees += 360.0;

        var dirs = new[] { "6", "7", "8", "1", "2", "3", "4", "5" };
        var index = (int)Math.Round(degrees / 45.0) % 8;
        return dirs[index];
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
