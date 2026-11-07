using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Assets;
using SGO.Server.Domain.Sessions;
using SGO.Server.Domain.World;
using SGO.Server.Infrastructure.World;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Combat;

public sealed class CombatService
{
    private static readonly TimeSpan RespawnDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AiTickInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MonsterAttackCooldown = TimeSpan.FromMilliseconds(1400);
    private static readonly TimeSpan WanderMinInterval = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan WanderMaxInterval = TimeSpan.FromMilliseconds(2800);

    private const double AggroRadius = 420.0;
    private const double LoseTargetRadius = 700.0;
    private const double StopDistance = 130.0;
    private const double ResumeChaseDistance = 160.0;
    private const double MoveStep = 35.0;
    private const double MoveTweenSeconds = 0.18;
    private const double WanderRadius = 180.0;
    private const double WanderStep = 45.0;

    private readonly IGameStore _store;
    private readonly WorldDataService _world;
    private readonly CurrentClientDataCatalog _current;

    public CombatService(
        IGameStore store,
        WorldDataService world,
        CurrentClientDataCatalog current)
    {
        _store = store;
        _world = world;
        _current = current;
    }

    public IReadOnlyList<object> EnterMap(
        ClientSession session,
        int mapId)
    {
        if (session.Monsters.Count > 0 &&
            session.LoadedMonsterMapId > 0)
        {
            session.MonstersByMap[session.LoadedMonsterMapId] =
                session.Monsters.ToDictionary(
                    x => x.Key,
                    x => x.Value);
        }

        session.Monsters.Clear();
        session.LoadedMonsterMapId = mapId;
        session.LastMonsterAiTick = DateTimeOffset.MinValue;

        var packets = new List<object>();
        packets.AddRange(_world.BuildNpcSpawnPackets(mapId));

        if (_world.IsCityMap(mapId))
            return packets;

        if (session.MonstersByMap.TryGetValue(
                mapId,
                out var preserved))
        {
            foreach (var pair in preserved)
            {
                session.Monsters[pair.Key] = pair.Value;
                if (pair.Value.IsAlive)
                    packets.Add(BuildMonsterSpawnPacket(pair.Value));
            }

            return packets;
        }

        foreach (var enemy in _world.GetEnemies(mapId))
        {
            if (enemy.Level >= 65535)
                continue;

            var state = new MonsterRuntimeState
            {
                EntityId = enemy.EntityId,
                EnemyId = enemy.EnemyId,
                MapId = enemy.MapId,
                Name = enemy.Name,
                Sprite = enemy.Sprite,
                Level = Math.Max(1, enemy.Level),
                MaxHp = Math.Max(1, enemy.MaxHp),
                CurrentHp = Math.Max(1, enemy.MaxHp),
                SpawnX = enemy.X,
                SpawnY = enemy.Y,
                X = enemy.X,
                Y = enemy.Y,
                WanderCenterX = enemy.X,
                WanderCenterY = enemy.Y
            };

            session.Monsters[state.EntityId] = state;
            packets.Add(BuildMonsterSpawnPacket(state));
        }

        session.MonstersByMap[mapId] =
            session.Monsters.ToDictionary(
                x => x.Key,
                x => x.Value);

        return packets;
    }

    public IReadOnlyList<object> BuildCurrentMapSnapshot(
        ClientSession session)
    {
        var packets = new List<object>();
        packets.AddRange(
            _world.BuildNpcSpawnPackets(
                session.CurrentMapId));

        foreach (var monster in session.Monsters.Values)
        {
            if (monster.IsAlive)
                packets.Add(BuildMonsterSpawnPacket(monster));
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
            monster.IsMoving = false;
            monster.HoldingAttackRange = false;
            monster.X = monster.SpawnX;
            monster.Y = monster.SpawnY;
            monster.WanderCenterX = monster.SpawnX;
            monster.WanderCenterY = monster.SpawnY;
            monster.NextWanderAt = DateTimeOffset.MinValue;
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
        var playerAlive = false;

        if (session.CharacterId.HasValue)
        {
            var character = _store.GetCharacter(
                accountId,
                session.CharacterId.Value);
            playerAlive = character is not null &&
                          Convert.ToInt32(character["HP"]) > 0;
        }

        foreach (var monster in session.Monsters.Values)
        {
            if (!monster.IsAlive)
                continue;

            if (!playerAlive)
            {
                if (monster.HasTarget)
                {
                    monster.HasTarget = false;
                    monster.IsMoving = false;
                    monster.WanderCenterX = monster.X;
                    monster.WanderCenterY = monster.Y;
                    monster.NextWanderAt = now + NextWanderDelay(
                        monster.EntityId,
                        monster.WanderSequence);

                    packets.Add(new
                    {
                        Type = "MonsterTarget",
                        EntityID = monster.EntityId,
                        HasTarget = false
                    });

                    // Do not move on the same tick that the player dies.
                    // The monster keeps its current world position and starts
                    // local idle wandering on a later server tick.
                    continue;
                }

                TryWander(monster, now, packets);
                continue;
            }

            var dx = playerX - monster.X;
            var dy = playerY - monster.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);

            if (monster.HoldingAttackRange)
            {
                if (distance > ResumeChaseDistance)
                    monster.HoldingAttackRange = false;
            }
            else if (distance <= StopDistance)
            {
                monster.HoldingAttackRange = true;
            }

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
                monster.IsMoving = false;
                monster.HoldingAttackRange = false;
                monster.WanderCenterX = monster.X;
                monster.WanderCenterY = monster.Y;
                monster.NextWanderAt = now + NextWanderDelay(
                    monster.EntityId,
                    monster.WanderSequence);

                packets.Add(new
                {
                    Type = "MonsterTarget",
                    EntityID = monster.EntityId,
                    HasTarget = false
                });

                continue;
            }

            if (monster.HasTarget &&
                monster.HoldingAttackRange &&
                session.CharacterId.HasValue &&
                now - monster.LastAttackAt >= MonsterAttackCooldown)
            {
                var character = _store.GetCharacter(
                    accountId,
                    session.CharacterId.Value);

                if (character is not null &&
                    Convert.ToInt32(character["HP"]) > 0)
                {
                    var assets = _store.GetCharacterAssets(
                        accountId,
                        session.CharacterId.Value);
                    var effective = CharacterStatCalculator.Calculate(
                        character,
                        assets,
                        _current);
                    var defense = Math.Max(0, effective.Defense);

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

                        monster.IsMoving = false;
                        monster.CurrentDir = StableDirectionFromDelta(
                            dx,
                            dy,
                            monster.CurrentDir);
                        packets.Add(new
                        {
                            Type = "SyncState",
                            EntityID = monster.EntityId,
                            State = "attack",
                            Dir = monster.CurrentDir
                        });

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
                            monster.IsMoving = false;
                            monster.HoldingAttackRange = false;
                            monster.WanderCenterX = monster.X;
                            monster.WanderCenterY = monster.Y;
                            monster.NextWanderAt = DateTimeOffset.MinValue;
                        }
                    }
                }

                continue;
            }

            if (!monster.HasTarget)
            {
                TryWander(monster, now, packets);
                continue;
            }

            if (monster.HoldingAttackRange)
            {
                var stableDir = StableDirectionFromDelta(
                    dx,
                    dy,
                    monster.CurrentDir);
                var directionChanged =
                    !string.Equals(
                        stableDir,
                        monster.CurrentDir,
                        StringComparison.Ordinal);

                if (monster.IsMoving || directionChanged)
                {
                    monster.IsMoving = false;
                    monster.CurrentDir = stableDir;
                    packets.Add(new
                    {
                        Type = "SyncState",
                        EntityID = monster.EntityId,
                        State = "idle",
                        Dir = monster.CurrentDir
                    });
                }

                continue;
            }

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
            monster.IsMoving = true;

            packets.Add(BuildMovePacket(
                monster,
                nx,
                ny,
                MoveTweenSeconds,
                moveDx,
                moveDy));
        }

        return packets;
    }

    public IReadOnlyList<object> SpawnLegacyEnemy(
        ClientSession session,
        int enemyId,
        double x,
        double y)
    {
        var source = _world.GetEnemies(session.CurrentMapId)
            .FirstOrDefault(e => e.EnemyId == enemyId);

        if (source is null)
            return [];

        var entityId = 9_000_000;
        while (session.Monsters.ContainsKey(entityId))
            entityId++;

        var state = new MonsterRuntimeState
        {
            EntityId = entityId,
            EnemyId = source.EnemyId,
            MapId = session.CurrentMapId,
            Name = source.Name,
            Sprite = source.Sprite,
            Level = Math.Max(1, source.Level),
            MaxHp = Math.Max(1, source.MaxHp),
            CurrentHp = Math.Max(1, source.MaxHp),
            SpawnX = x,
            SpawnY = y,
            X = x,
            Y = y,
            WanderCenterX = x,
            WanderCenterY = y
        };

        session.Monsters[state.EntityId] = state;

        return [BuildMonsterSpawnPacket(state)];
    }

    public IReadOnlyList<object> PetAttack(
        ClientSession session,
        long petUid,
        int petAttack,
        int targetEntityId,
        double petX,
        double petY)
    {
        if (!session.Monsters.TryGetValue(
                targetEntityId,
                out var monster) ||
            !monster.IsAlive ||
            monster.MapId != session.CurrentMapId)
            return [];

        var dx = monster.X - petX;
        var dy = monster.Y - petY;
        var distanceSquared = dx * dx + dy * dy;
        const double petAttackRange = 130.0;

        if (distanceSquared > petAttackRange * petAttackRange)
            return [];

        var petEntityId = 4_000_000L + petUid;
        var damage = Math.Max(1, petAttack);

        monster.CurrentHp = Math.Max(
            0,
            monster.CurrentHp - damage);

        var packets = new List<object>
        {
            new
            {
                Type = "SyncState",
                EntityID = petEntityId,
                State = "attack",
                Dir = DirectionFromDelta(dx, dy)
            },
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
                AttackerID = petEntityId
            }
        };

        if (monster.CurrentHp == 0)
        {
            monster.RespawnAt =
                DateTimeOffset.UtcNow + RespawnDelay;
            monster.HasTarget = false;
            monster.IsMoving = false;

            packets.Add(new
            {
                Type = "EntityDeath",
                EntityID = monster.EntityId,
                EntityType = 1,
                KillerID = petEntityId
            });
        }

        return packets;
    }

    public IReadOnlyList<object> UseSkill(
        ClientSession session,
        long accountId,
        int skillId,
        int targetEntityId,
        int targetType,
        int targetX,
        int targetY,
        DateTimeOffset now)
    {
        if (!session.CharacterId.HasValue)
            return [];

        var learned = _store.GetSkills(
            accountId,
            session.CharacterId.Value)
            .FirstOrDefault(x =>
                Convert.ToInt32(x["SkillID"]) == skillId);

        if (learned is null)
            return [new
            {
                Type = "SystemMessageFlash",
                Message = "尚未學會此技能"
            }];

        var skillLevel = Math.Max(
            1,
            Convert.ToInt32(
                learned.TryGetValue("level", out var lowerLevel)
                    ? lowerLevel
                    : learned.TryGetValue("SkillLevel", out var clientLevel)
                        ? clientLevel
                        : 1));

        var levelData = _current.FindSkillLevel(
            skillId,
            skillLevel);

        if (levelData is null)
            return [new
            {
                Type = "SystemMessageFlash",
                Message = $"技能資料不存在 SkillID={skillId} Lv.{skillLevel}"
            }];

        var isMoonJudgment =
            skillId == 45111 &&
            levelData.SkillType == 7;

        if (levelData.SkillType is not (0 or 1) &&
            !isMoonJudgment)
            return [new
            {
                Type = "SystemMessageFlash",
                Message = "此技能類型尚未支援"
            }];

        if (session.SkillReadyAt.TryGetValue(
                skillId,
                out var readyAt) &&
            now < readyAt)
        {
            return [new
            {
                Type = "SystemMessageFlash",
                Message = "技能冷卻中"
            }];
        }

        var character = _store.GetCharacter(
            accountId,
            session.CharacterId.Value);

        if (character is null ||
            Convert.ToInt32(character["HP"]) <= 0)
            return [];

        if (Convert.ToInt32(character["HP"]) < levelData.CostHp ||
            Convert.ToInt32(character["MP"]) < levelData.CostMp ||
            Convert.ToInt32(character["SP"]) < levelData.CostSp)
        {
            return [new
            {
                Type = "SystemMessageFlash",
                Message = "HP / MP / SP 不足"
            }];
        }

        if (targetType != 1 ||
            !session.Monsters.TryGetValue(
                targetEntityId,
                out var target) ||
            !target.IsAlive ||
            target.MapId != session.CurrentMapId)
        {
            return [new
            {
                Type = "SystemMessageFlash",
                Message = "技能目標無效"
            }];
        }

        var playerX = Convert.ToDouble(character["LocX"]);
        var playerY = Convert.ToDouble(character["LocY"]);
        var castDx = target.X - playerX;
        var castDy = target.Y - playerY;
        var castRange = Math.Max(0, levelData.CastRange) + 120;

        if (castDx * castDx + castDy * castDy >
            castRange * castRange)
        {
            return [new
            {
                Type = "SystemMessageFlash",
                Message = "技能目標距離太遠"
            }];
        }

        var updated = _store.SpendSkillResources(
            accountId,
            session.CharacterId.Value,
            levelData.CostHp,
            levelData.CostMp,
            levelData.CostSp);

        if (updated is null)
            return [new
            {
                Type = "SystemMessageFlash",
                Message = "HP / MP / SP 不足"
            }];

        var assets = _store.GetCharacterAssets(
            accountId,
            session.CharacterId.Value);
        var effective = CharacterStatCalculator.Calculate(
            updated,
            assets,
            _current);

        var damage = isMoonJudgment
            // Client marks 月神審判 as a special attack and its current
            // .tres carries no generic base_damage fields. Treat it as a
            // physical+magical composite until the original service formula
            // is recovered.
            ? Math.Max(
                1,
                effective.Attack + effective.MagicAttack)
            : Math.Max(
                1,
                levelData.BaseDamage +
                (levelData.DamageType == 1
                    ? effective.MagicAttack
                    : effective.Attack) *
                Math.Max(0, levelData.DamageMultiplier) / 100);

        var centerX = target.X;
        var centerY = target.Y;
        var radius = Math.Max(0, levelData.AoeRadius);

        var targets = session.Monsters.Values
            .Where(x =>
                x.IsAlive &&
                x.MapId == session.CurrentMapId &&
                (radius <= 0
                    ? x.EntityId == target.EntityId
                    : Math.Pow(x.X - centerX, 2) +
                      Math.Pow(x.Y - centerY, 2) <=
                      radius * radius))
            .ToArray();

        var packets = new List<object>
        {
            new
            {
                Type = "SkillCast",
                CasterID = session.CharacterId.Value,
                CasterType = 0,
                SkillID = skillId,
                SkillLevel = skillLevel,
                TargetID = target.EntityId,
                TargetX = Convert.ToInt32(centerX),
                TargetY = Convert.ToInt32(centerY),
                AoeTargets = targets
                    .Select(x => new { ID = x.EntityId })
                    .ToArray()
            },
            new
            {
                Type = "PlayerStatusSync",
                HP = updated["HP"],
                MP = updated["MP"],
                SP = updated["SP"],
                MaxHP = updated["MaxHP"],
                MaxMP = updated["MaxMP"],
                MaxSP = updated["MaxSP"]
            }
        };

        foreach (var monster in targets)
        {
            monster.CurrentHp = Math.Max(
                0,
                monster.CurrentHp - damage);

            packets.Add(new
            {
                Type = "EntityDamage",
                TargetID = monster.EntityId,
                TargetType = 1,
                Damage = damage,
                CurrentHP = monster.CurrentHp,
                MaxHP = monster.MaxHp,
                IsCrit = false,
                SkillID = skillId,
                SkillLevel = skillLevel,
                AttackerID = session.CharacterId.Value
            });

            if (monster.CurrentHp == 0)
            {
                monster.RespawnAt =
                    now + RespawnDelay;
                monster.HasTarget = false;
                monster.IsMoving = false;

                packets.Add(new
                {
                    Type = "EntityDeath",
                    EntityID = monster.EntityId,
                    EntityType = 1,
                    KillerID = session.CharacterId.Value
                });

                AddMechaKillExperience(
                    session,
                    accountId,
                    monster,
                    packets);
            }
        }

        session.SkillReadyAt[skillId] =
            now + TimeSpan.FromSeconds(
                Math.Max(0, levelData.CooldownSeconds));

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
        var assets = _store.GetCharacterAssets(
            accountId,
            session.CharacterId.Value);
        var effective = CharacterStatCalculator.Calculate(
            character,
            assets,
            _current);

        var attackRange = Math.Max(
            80,
            effective.AttackRange);

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
            effective.Attack);

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

            AddMechaKillExperience(
                session,
                accountId,
                monster,
                packets);
        }

        return packets;
    }

    private void AddMechaKillExperience(
        ClientSession session,
        long accountId,
        MonsterRuntimeState monster,
        List<object> packets)
    {
        if (!session.CharacterId.HasValue)
            return;

        var character = _store.GetCharacter(
            accountId,
            session.CharacterId.Value);

        if (character is null)
            return;

        var horseUid = Convert.ToInt64(
            character["SummonedHorseUID"]);

        if (horseUid <= 0)
            return;

        var gainedExp = Math.Max(1, monster.Level * 100);
        var before = _store.GetCharacterAssets(
                accountId,
                session.CharacterId.Value)
            .Horses
            .FirstOrDefault(x =>
                Convert.ToInt64(x["HorseUID"]) == horseUid);

        var horse = _store.AddHorseExperience(
            accountId,
            session.CharacterId.Value,
            horseUid,
            gainedExp);

        if (horse is null)
            return;

        packets.Add(new
        {
            Type = "SyncMechaEnergy",
            HorseUID = horseUid,
            EnergyLeft = horse["EnergyLeft"],
            Exp = horse["Exp"],
            MaxExp = horse["MaxExp"],
            Level = horse["Level"]
        });

        if (before is not null &&
            Convert.ToInt32(horse["Level"]) >
            Convert.ToInt32(before["Level"]))
        {
            packets.Add(new
            {
                Type = "SystemMessageFlash",
                Message = $"機甲升級！Lv.{horse["Level"]}"
            });
        }
    }

    private static void TryWander(
        MonsterRuntimeState monster,
        DateTimeOffset now,
        List<object> packets)
    {
        // Each monster gets its own deterministic-but-different schedule.
        // MinValue means "not scheduled yet", so the first idle tick only
        // assigns an offset instead of making every monster move together.
        if (monster.NextWanderAt == DateTimeOffset.MinValue)
        {
            monster.NextWanderAt = now + NextWanderDelay(
                monster.EntityId,
                monster.WanderSequence);
            return;
        }

        if (now < monster.NextWanderAt)
            return;

        monster.WanderSequence++;

        var seed = unchecked(
            monster.EntityId * 1103515245 +
            monster.WanderSequence * 12345);
        var angle = ((uint)seed % 360) * Math.PI / 180.0;

        var desiredX = monster.X + Math.Cos(angle) * WanderStep;
        var desiredY = monster.Y + Math.Sin(angle) * WanderStep;

        var fromCenterX = desiredX - monster.WanderCenterX;
        var fromCenterY = desiredY - monster.WanderCenterY;
        var fromCenterDistance = Math.Sqrt(
            fromCenterX * fromCenterX +
            fromCenterY * fromCenterY);

        if (fromCenterDistance > WanderRadius)
        {
            var centerDx = monster.WanderCenterX - monster.X;
            var centerDy = monster.WanderCenterY - monster.Y;
            var centerDistance = Math.Sqrt(
                centerDx * centerDx +
                centerDy * centerDy);

            if (centerDistance > 1.0)
            {
                var step = Math.Min(WanderStep, centerDistance);
                desiredX = monster.X + centerDx / centerDistance * step;
                desiredY = monster.Y + centerDy / centerDistance * step;
            }
            else
            {
                monster.NextWanderAt = now + NextWanderDelay(
                    monster.EntityId,
                    monster.WanderSequence);
                return;
            }
        }

        var moveDx = desiredX - monster.X;
        var moveDy = desiredY - monster.Y;

        if (Math.Abs(moveDx) < 0.001 &&
            Math.Abs(moveDy) < 0.001)
        {
            monster.NextWanderAt = now + NextWanderDelay(
                monster.EntityId,
                monster.WanderSequence);
            return;
        }

        monster.X = desiredX;
        monster.Y = desiredY;
        monster.IsMoving = true;
        monster.NextWanderAt = now + NextWanderDelay(
            monster.EntityId,
            monster.WanderSequence);

        packets.Add(BuildMovePacket(
            monster,
            desiredX,
            desiredY,
            MoveTweenSeconds,
            moveDx,
            moveDy));
    }

    private static TimeSpan NextWanderDelay(
        int entityId,
        int sequence)
    {
        var minMs = (int)WanderMinInterval.TotalMilliseconds;
        var maxMs = (int)WanderMaxInterval.TotalMilliseconds;
        var range = Math.Max(1, maxMs - minMs + 1);

        var seed = unchecked(
            entityId * 214013 +
            sequence * 2531011 +
            1376312589);

        var jitter = (int)((uint)seed % (uint)range);
        return TimeSpan.FromMilliseconds(minMs + jitter);
    }

    private static object BuildMovePacket(
        MonsterRuntimeState monster,
        double x,
        double y,
        double speedTime,
        double moveDx,
        double moveDy)
    {
        monster.CurrentDir = StableDirectionFromDelta(
            moveDx,
            moveDy,
            monster.CurrentDir);

        return new
        {
            Type = "MoveEntity",
            EntityID = monster.EntityId,
            X = x,
            Y = y,
            Dir = monster.CurrentDir,
            SpeedTime = speedTime
        };
    }

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

    private static string StableDirectionFromDelta(
        double dx,
        double dy,
        string currentDir)
    {
        if (Math.Abs(dx) < 0.001 &&
            Math.Abs(dy) < 0.001)
            return string.IsNullOrWhiteSpace(currentDir)
                ? "1"
                : currentDir;

        var degrees = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (degrees < 0)
            degrees += 360.0;

        var dirs = new[] { "6", "7", "8", "1", "2", "3", "4", "5" };
        var currentIndex = Array.IndexOf(dirs, currentDir);

        if (currentIndex >= 0)
        {
            var center = currentIndex * 45.0;
            var diff = Math.Abs(degrees - center);
            diff = Math.Min(diff, 360.0 - diff);

            // Keep the existing facing slightly beyond the normal 22.5°
            // sector boundary to prevent rapid direction flipping.
            if (diff <= 30.0)
                return currentDir;
        }

        return DirectionFromDelta(dx, dy);
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
                EnemyID = monster.EnemyId
            }
        };
}
