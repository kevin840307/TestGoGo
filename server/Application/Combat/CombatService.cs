using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Assets;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.World;
using SGO.Server.Domain.Sessions;
using SGO.Server.Domain.World;
using SGO.Server.Infrastructure.WorldData;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Combat;

public sealed class CombatService
{
    private static readonly TimeSpan RespawnDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MonsterAttackCooldown = TimeSpan.FromMilliseconds(1400);
    private static readonly TimeSpan WanderMinInterval = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan WanderMaxInterval = TimeSpan.FromMilliseconds(2800);

    private const double AggroRadius = 420.0;
    private const double LoseTargetRadius = 700.0;
    private const double AttackRange = 130.0;
    private const double EngageDistance = 95.0;
    private const double MoveStep = 42.0;
    // Keep the movement packet duration aligned with the 250 ms world cadence.
    // The client now interpolates continuously toward the latest authoritative
    // waypoint, so this value represents movement speed rather than tween timing.
    private const double MoveTweenSeconds = 0.25;
    private const double WanderRadius = 180.0;
    private const double WanderStep = 60.0;

    private readonly IGameStore _store;
    private readonly WorldDataService _world;
    private readonly CurrentClientDataCatalog _current;
    private readonly LegacyPlayerGrowthCatalog _growth;
    private readonly LegacyBattleRulesCatalog _battle;
    private readonly EnemyDropCatalog _drops;
    private readonly WorldRuntimeService _runtime;
    private readonly ServerBroadcastHub _broadcasts;

    public CombatService(
        IGameStore store,
        WorldDataService world,
        CurrentClientDataCatalog current,
        LegacyPlayerGrowthCatalog growth,
        LegacyBattleRulesCatalog battle,
        EnemyDropCatalog drops,
        WorldRuntimeService runtime,
        ServerBroadcastHub broadcasts)
    {
        _store = store;
        _world = world;
        _current = current;
        _growth = growth;
        _battle = battle;
        _drops = drops;
        _runtime = runtime;
        _broadcasts = broadcasts;
    }

    public IReadOnlyList<object> EnterMap(
        ClientSession session,
        int mapId)
    {
        session.Monsters.Clear();
        session.MonstersByMap.Clear();
        session.LoadedMonsterMapId = mapId;
        session.LastMonsterAiTick = DateTimeOffset.MinValue;

        var packets = new List<object>();
        packets.AddRange(_world.BuildNpcSpawnPackets(mapId));

        var runtime = _runtime.GetMap(mapId);
        lock (runtime.SyncRoot)
        {
            foreach (var pair in runtime.Monsters)
            {
                // Every connection points at the same MonsterRuntimeState
                // instances. Position, HP, aggro and respawn state therefore
                // belong to the server map rather than an individual socket.
                session.Monsters[pair.Key] = pair.Value;

                if (pair.Value.IsAlive)
                    packets.Add(BuildMonsterSpawnPacket(pair.Value));
            }
        }

        return packets;
    }

    public IReadOnlyList<object> BuildCurrentMapSnapshot(
        ClientSession session)
    {
        var packets = new List<object>();
        packets.AddRange(
            _world.BuildNpcSpawnPackets(
                session.CurrentMapId));

        var runtime = SyncSessionMonsters(session);
        lock (runtime.SyncRoot)
        {
            foreach (var monster in runtime.Monsters.Values)
            {
                if (monster.IsAlive)
                    packets.Add(BuildMonsterSpawnPacket(monster));
            }
        }

        return packets;
    }

    public IReadOnlyList<object> TickRespawns(
        ClientSession session,
        DateTimeOffset now)
    {
        if (session.CurrentMapId <= 0)
            return [];

        var runtime = SyncSessionMonsters(session);
        var packets = new List<object>();

        lock (runtime.SyncRoot)
        {
            foreach (var monster in runtime.Monsters.Values)
            {
                if (monster.RespawnAt is null ||
                    monster.RespawnAt > now)
                    continue;

                monster.CurrentHp = monster.MaxHp;
                monster.RespawnAt = null;
                monster.HasTarget = false;
                monster.TargetCharacterId = 0;
                monster.LastAiAt = DateTimeOffset.MinValue;
                SetAiState(monster, MonsterAiState.Idle);
                monster.X = monster.SpawnX;
                monster.Y = monster.SpawnY;
                monster.WanderCenterX = monster.SpawnX;
                monster.WanderCenterY = monster.SpawnY;
                monster.NextWanderAt = DateTimeOffset.MinValue;
                packets.Add(BuildMonsterSpawnPacket(monster));
            }
        }

        BroadcastWorldPackets(session, packets);
        return packets;
    }

    public IReadOnlyList<object> TickAi(
        ClientSession session,
        long accountId,
        double playerX,
        double playerY,
        DateTimeOffset now)
    {
        session.LastMonsterAiTick = now;

        if (!session.CharacterId.HasValue ||
            session.CurrentMapId <= 0)
            return [];

        var currentCharacterId = session.CharacterId.Value;
        var character = _store.GetCharacter(
            accountId,
            currentCharacterId);
        var playerAlive =
            character is not null &&
            Convert.ToInt32(character["HP"]) > 0;

        var runtime = SyncSessionMonsters(session);
        var packets = new List<object>();

        lock (runtime.SyncRoot)
        {
            foreach (var monster in runtime.Monsters.Values)
            {
                if (!monster.IsAlive)
                {
                    SetAiState(monster, MonsterAiState.Dead);
                    continue;
                }

                // A targeted monster is driven only by the target player's
                // connection. Other sockets share the same object but do not
                // advance its AI a second time.
                if (monster.HasTarget &&
                    monster.TargetCharacterId > 0 &&
                    monster.TargetCharacterId != currentCharacterId)
                {
                    if (_broadcasts.IsCharacterInMap(
                            monster.TargetCharacterId,
                            session.CurrentMapId))
                    {
                        continue;
                    }

                    DropMonsterTarget(monster, now);
                    packets.Add(new
                    {
                        Type = "MonsterTarget",
                        EntityID = monster.EntityId,
                        HasTarget = false
                    });
                }

                // Compatibility for runtime state created before target-owner
                // tracking and for focused unit/smoke scenarios.
                if (monster.HasTarget &&
                    monster.TargetCharacterId == 0)
                {
                    monster.TargetCharacterId =
                        currentCharacterId;
                }

                if (!playerAlive)
                {
                    if (monster.HasTarget &&
                        monster.TargetCharacterId == currentCharacterId)
                    {
                        DropMonsterTarget(monster, now);
                        packets.Add(new
                        {
                            Type = "MonsterTarget",
                            EntityID = monster.EntityId,
                            HasTarget = false
                        });
                        continue;
                    }

                    if (!monster.HasTarget &&
                        (monster.LastAiAt == DateTimeOffset.MinValue ||
                         now - monster.LastAiAt >=
                         TimeSpan.FromMilliseconds(200)))
                    {
                        monster.LastAiAt = now;
                        TryWander(monster, now, packets);
                    }

                    continue;
                }

                var dx = playerX - monster.X;
                var dy = playerY - monster.Y;
                var distance = Math.Sqrt(dx * dx + dy * dy);

                // Every nearby player may compete to acquire an idle monster.
                // The map lock makes this decision atomic; after acquisition
                // only the winning TargetCharacterId drives the monster.
                if (!monster.HasTarget &&
                    distance <= AggroRadius)
                {
                    monster.HasTarget = true;
                    monster.TargetCharacterId = currentCharacterId;
                    monster.LastAiAt = now;
                    SetAiState(monster, MonsterAiState.Chase);
                    packets.Add(new
                    {
                        Type = "MonsterTarget",
                        EntityID = monster.EntityId,
                        HasTarget = true,
                        TargetID = currentCharacterId
                    });
                }

                if (!monster.HasTarget)
                {
                    if (monster.LastAiAt == DateTimeOffset.MinValue ||
                        now - monster.LastAiAt >=
                        TimeSpan.FromMilliseconds(200))
                    {
                        monster.LastAiAt = now;
                        TryWander(monster, now, packets);
                    }

                    continue;
                }

                if (monster.TargetCharacterId != currentCharacterId)
                    continue;

                monster.LastAiAt = now;

                if (distance > LoseTargetRadius)
                {
                    DropMonsterTarget(monster, now);
                    packets.Add(new
                    {
                        Type = "MonsterTarget",
                        EntityID = monster.EntityId,
                        HasTarget = false
                    });
                    continue;
                }

                var shouldAttack =
                    distance <= EngageDistance;

                if (shouldAttack)
                {
                    var enteredAttackRange =
                        monster.AiState != MonsterAiState.Attack;

                    var wasMoving = monster.AiState is
                        MonsterAiState.Chase or
                        MonsterAiState.Wander;

                    var stableDir = StableDirectionFromDelta(
                        dx,
                        dy,
                        monster.CurrentDir);

                    var directionChanged =
                        !string.Equals(
                            stableDir,
                            monster.CurrentDir,
                            StringComparison.Ordinal);

                    monster.CurrentDir = stableDir;
                    SetAiState(monster, MonsterAiState.Attack);

                    var withinDamageRange =
                        distance <= AttackRange;

                    var cooldownElapsed =
                        monster.LastAttackAt > now ||
                        now - monster.LastAttackAt >=
                        MonsterAttackCooldown;

                    var attackReady =
                        withinDamageRange &&
                        (enteredAttackRange || cooldownElapsed);

                    if (attackReady &&
                        character is not null &&
                        Convert.ToInt32(character["HP"]) > 0)
                    {
                        var assets = _store.GetCharacterAssets(
                            accountId,
                            currentCharacterId);

                        var effective = ApplyActiveSkillBuffs(
                            CharacterStatCalculator.Calculate(
                                character,
                                assets,
                                _current),
                            session,
                            now);

                        var defense = Math.Max(0, effective.Defense);
                        var rawDamage = Math.Max(1, monster.Level * 2);
                        var damage = Math.Max(
                            1,
                            rawDamage - defense / 5);

                        var updated = _store.ApplyDamage(
                            accountId,
                            currentCharacterId,
                            damage);

                        if (updated is not null)
                        {
                            monster.LastAttackAt = now;
                            character = updated;

                            var currentHp =
                                Convert.ToInt32(updated["HP"]);
                            var maxHp =
                                Convert.ToInt32(updated["MaxHP"]);

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
                                TargetID = currentCharacterId,
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
                                    EntityID = currentCharacterId,
                                    EntityType = 0,
                                    KillerID = monster.EntityId
                                });

                                DropMonsterTarget(
                                    monster,
                                    now,
                                    scheduleWander: false);
                            }
                        }
                    }
                    else if (wasMoving || directionChanged)
                    {
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

                SetAiState(monster, MonsterAiState.Chase);

                var step = Math.Min(
                    MoveStep,
                    Math.Max(0.0, distance - EngageDistance));

                if (step <= 0.0)
                {
                    SetAiState(monster, MonsterAiState.Attack);
                    continue;
                }

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
                    MoveTweenSeconds,
                    moveDx,
                    moveDy));
            }
        }

        BroadcastWorldPackets(session, packets);
        return packets;
    }

    private SharedMapRuntime SyncSessionMonsters(
        ClientSession session)
    {
        var runtime = _runtime.GetMap(session.CurrentMapId);

        lock (runtime.SyncRoot)
        {
            session.Monsters.Clear();
            foreach (var pair in runtime.Monsters)
                session.Monsters[pair.Key] = pair.Value;
        }

        session.LoadedMonsterMapId = session.CurrentMapId;
        return runtime;
    }

    private void BroadcastWorldPackets(
        ClientSession session,
        IEnumerable<object> packets)
    {
        if (session.CurrentMapId <= 0)
            return;

        foreach (var packet in packets)
        {
            var type = packet.GetType()
                .GetProperty("Type")
                ?.GetValue(packet)
                ?.ToString();

            if (type is not (
                    "SpawnEntity" or
                    "DespawnEntity" or
                    "MoveEntity" or
                    "SyncState" or
                    "EntityDamage" or
                    "EntityDeath" or
                    "EntityHeal" or
                    "MonsterTarget" or
                    "SkillCast"))
                continue;

            _broadcasts.PublishToMap(
                session.CurrentMapId,
                packet,
                session.ConnectionId);
        }
    }

    private static void SetAiState(
        MonsterRuntimeState monster,
        MonsterAiState state)
    {
        monster.AiState = state;
        monster.IsMoving =
            state is MonsterAiState.Wander or MonsterAiState.Chase;
        monster.HoldingAttackRange =
            state == MonsterAiState.Attack;
    }

    private static void DropMonsterTarget(
        MonsterRuntimeState monster,
        DateTimeOffset now,
        bool scheduleWander = true)
    {
        monster.HasTarget = false;
        monster.TargetCharacterId = 0;
        SetAiState(monster, MonsterAiState.Idle);
        monster.WanderCenterX = monster.X;
        monster.WanderCenterY = monster.Y;
        monster.NextWanderAt = scheduleWander
            ? now + NextWanderDelay(
                monster.EntityId,
                monster.WanderSequence)
            : DateTimeOffset.MinValue;
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

        var runtime = _runtime.GetMap(session.CurrentMapId);
        MonsterRuntimeState state;

        lock (runtime.SyncRoot)
        {
            var entityId = 9_000_000;
            while (runtime.Monsters.ContainsKey(entityId))
                entityId++;

            state = new MonsterRuntimeState
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

            runtime.Monsters[state.EntityId] = state;
            session.Monsters[state.EntityId] = state;
        }

        var packets = new List<object>
        {
            BuildMonsterSpawnPacket(state)
        };
        BroadcastWorldPackets(session, packets);
        return packets;
    }

    public IReadOnlyList<object> PetAttack(
        ClientSession session,
        long accountId,
        long petUid,
        int petAttack,
        int targetEntityId,
        double petX,
        double petY)
    {
        if (!session.CharacterId.HasValue ||
            session.CurrentMapId <= 0)
            return [];

        var runtime = SyncSessionMonsters(session);
        if (!session.Monsters.TryGetValue(
                targetEntityId,
                out var monster) ||
            monster.MapId != session.CurrentMapId)
            return [];

        var packets = new List<object>();

        lock (runtime.SyncRoot)
        {
            if (!monster.IsAlive)
                return [];

            var dx = monster.X - petX;
            var dy = monster.Y - petY;
            var distanceSquared = dx * dx + dy * dy;
            const double petAttackRange = 130.0;

            if (distanceSquared >
                petAttackRange * petAttackRange)
                return [];

            var petEntityId = 4_000_000L + petUid;
            var damage = Math.Max(1, petAttack);

            monster.CurrentHp = Math.Max(
                0,
                monster.CurrentHp - damage);

            packets.Add(new
            {
                Type = "SyncState",
                EntityID = petEntityId,
                State = "attack",
                Dir = DirectionFromDelta(dx, dy)
            });

            packets.Add(new
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
            });

            if (monster.CurrentHp > 0 &&
                !monster.HasTarget)
            {
                monster.HasTarget = true;
                monster.TargetCharacterId =
                    session.CharacterId.Value;
                SetAiState(monster, MonsterAiState.Chase);

                packets.Add(new
                {
                    Type = "MonsterTarget",
                    EntityID = monster.EntityId,
                    HasTarget = true,
                    TargetID = session.CharacterId.Value
                });
            }

            if (monster.CurrentHp == 0)
            {
                monster.RespawnAt =
                    DateTimeOffset.UtcNow + RespawnDelay;
                monster.HasTarget = false;
                monster.TargetCharacterId = 0;
                SetAiState(monster, MonsterAiState.Dead);

                packets.Add(new
                {
                    Type = "EntityDeath",
                    EntityID = monster.EntityId,
                    EntityType = 1,
                    KillerID = petEntityId
                });

                AddCharacterKillRewards(
                    session,
                    accountId,
                    monster,
                    packets);

                AddMonsterDrops(
                    session,
                    accountId,
                    monster,
                    packets);

                AddMechaKillExperience(
                    session,
                    accountId,
                    monster,
                    packets);
            }
        }

        BroadcastWorldPackets(session, packets);
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

        PurgeExpiredBuffs(session, now);

        var learned = _store.GetSkills(
            accountId,
            session.CharacterId.Value)
            .FirstOrDefault(x =>
                Convert.ToInt32(x["SkillID"]) == skillId);

        if (learned is null)
            return [Flash("尚未學會此技能")];

        var skillLevel = Math.Max(
            1,
            Convert.ToInt32(
                learned.TryGetValue("Level", out var upperLevel)
                    ? upperLevel
                    : learned.TryGetValue("level", out var lowerLevel)
                        ? lowerLevel
                        : learned.TryGetValue("SkillLevel", out var clientLevel)
                            ? clientLevel
                            : 1));

        var levelData = _current.FindSkillLevel(
            skillId,
            skillLevel);

        if (levelData is null)
            return [Flash(
                $"技能資料不存在 SkillID={skillId} Lv.{skillLevel}")];

        if (session.SkillReadyAt.TryGetValue(
                skillId,
                out var readyAt) &&
            now < readyAt)
        {
            return [Flash("技能冷卻中")];
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
            return [Flash("HP / MP / SP 不足")];
        }

        var assets = _store.GetCharacterAssets(
            accountId,
            session.CharacterId.Value);
        var effective = ApplyActiveSkillBuffs(
            CharacterStatCalculator.Calculate(
                character,
                assets,
                _current),
            session,
            now);

        var hasBonus = HasGenericStatBonus(levelData);
        var isHeal = levelData.SkillType == 2;
        var isBuff = levelData.SkillType is 4 or 5 || hasBonus;
        var isAttack =
            levelData.SkillType is 0 or 1 ||
            (levelData.SkillType == 7 &&
             (targetType == 1 ||
              levelData.BaseDamage != 0 ||
              levelData.DamageMultiplier != 0));

        if (!isAttack && !isHeal && !isBuff)
        {
            return [Flash(
                "此技能屬於特殊/製造/復活類型，效果尚未還原")];
        }

        MonsterRuntimeState? primaryMonster = null;
        double centerX = targetX;
        double centerY = targetY;

        if (isAttack)
            SyncSessionMonsters(session);

        if (isAttack)
        {
            if (targetType != 1 ||
                !session.Monsters.TryGetValue(
                    targetEntityId,
                    out primaryMonster) ||
                !primaryMonster.IsAlive ||
                primaryMonster.MapId != session.CurrentMapId)
            {
                return [Flash("技能目標無效")];
            }

            centerX = primaryMonster.X;
            centerY = primaryMonster.Y;

            var playerX = Convert.ToDouble(character["LocX"]);
            var playerY = Convert.ToDouble(character["LocY"]);
            var castDx = centerX - playerX;
            var castDy = centerY - playerY;
            var castRange = Math.Max(0, levelData.CastRange) + 120;

            if (castDx * castDx + castDy * castDy >
                castRange * castRange)
            {
                return [Flash("技能目標距離太遠")];
            }
        }
        else
        {
            // The current local server only owns the selected player's state.
            // Self-heal/self-buff therefore accepts target 0 or this DBID.
            if (targetType == 0 &&
                targetEntityId != 0 &&
                targetEntityId != session.CharacterId.Value)
            {
                return [Flash("目前僅支援自身治療/增益")];
            }

            targetEntityId = checked((int)session.CharacterId.Value);
            centerX = Convert.ToDouble(character["LocX"]);
            centerY = Convert.ToDouble(character["LocY"]);
        }

        var updated = _store.SpendSkillResources(
            accountId,
            session.CharacterId.Value,
            levelData.CostHp,
            levelData.CostMp,
            levelData.CostSp);

        if (updated is null)
            return [Flash("HP / MP / SP 不足")];

        var packets = new List<object>
        {
            new
            {
                Type = "SkillCast",
                CasterID = session.CharacterId.Value,
                CasterType = 0,
                SkillID = skillId,
                SkillLevel = skillLevel,
                TargetID = targetEntityId,
                TargetX = Convert.ToInt32(centerX),
                TargetY = Convert.ToInt32(centerY),
                AoeTargets = Array.Empty<object>()
            }
        };

        if (isAttack && primaryMonster is not null)
        {
            var damage = Math.Max(
                1,
                levelData.BaseDamage +
                (levelData.DamageType == 1
                    ? effective.MagicAttack
                    : effective.Attack) *
                Math.Max(0, levelData.DamageMultiplier) / 100);

            if (levelData.SkillType == 7 &&
                levelData.BaseDamage == 0 &&
                levelData.DamageMultiplier == 0)
            {
                damage = Math.Max(
                    1,
                    effective.Attack + effective.MagicAttack);
            }

            var runtime = _runtime.GetMap(
                session.CurrentMapId);

            lock (runtime.SyncRoot)
            {
                var radius = Math.Max(
                    0,
                    levelData.AoeRadius);

                var targets = runtime.Monsters.Values
                    .Where(x =>
                        x.IsAlive &&
                        x.MapId == session.CurrentMapId &&
                        (radius <= 0
                            ? x.EntityId ==
                              primaryMonster.EntityId
                            : Math.Pow(x.X - centerX, 2) +
                              Math.Pow(x.Y - centerY, 2) <=
                              radius * radius))
                    .ToArray();

                packets[0] = new
                {
                    Type = "SkillCast",
                    CasterID = session.CharacterId.Value,
                    CasterType = 0,
                    SkillID = skillId,
                    SkillLevel = skillLevel,
                    TargetID = primaryMonster.EntityId,
                    TargetX = Convert.ToInt32(centerX),
                    TargetY = Convert.ToInt32(centerY),
                    AoeTargets = targets
                        .Select(x => new { ID = x.EntityId })
                        .ToArray()
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

                    if (monster.CurrentHp > 0 &&
                        !monster.HasTarget)
                    {
                        monster.HasTarget = true;
                        monster.TargetCharacterId =
                            session.CharacterId.Value;
                        SetAiState(
                            monster,
                            MonsterAiState.Chase);

                        packets.Add(new
                        {
                            Type = "MonsterTarget",
                            EntityID = monster.EntityId,
                            HasTarget = true,
                            TargetID = session.CharacterId.Value
                        });
                    }

                    if (monster.CurrentHp != 0)
                        continue;

                    monster.RespawnAt =
                        now + RespawnDelay;
                    monster.HasTarget = false;
                    monster.TargetCharacterId = 0;
                    SetAiState(
                        monster,
                        MonsterAiState.Dead);

                    packets.Add(new
                    {
                        Type = "EntityDeath",
                        EntityID = monster.EntityId,
                        EntityType = 1,
                        KillerID = session.CharacterId.Value
                    });

                    AddCharacterKillRewards(
                        session,
                        accountId,
                        monster,
                        packets);

                    AddMonsterDrops(
                        session,
                        accountId,
                        monster,
                        packets);

                    AddMechaKillExperience(
                        session,
                        accountId,
                        monster,
                        packets);
                }
            }
        }

        if (isHeal)
        {
            var healAmount = Math.Max(
                1,
                levelData.BaseDamage +
                effective.MagicAttack *
                Math.Max(1, levelData.DamageMultiplier) / 100);

            var healed = _store.ApplyHeal(
                accountId,
                session.CharacterId.Value,
                healAmount);

            if (healed is not null)
            {
                packets.Add(new
                {
                    Type = "EntityHeal",
                    TargetID = session.CharacterId.Value,
                    TargetType = 0,
                    Amount = healAmount,
                    CurrentHP = healed["HP"],
                    MaxHP = healed["MaxHP"],
                    SkillID = skillId,
                    SkillLevel = skillLevel,
                    HealerID = session.CharacterId.Value
                });
                updated = healed;
            }
        }

        if (isBuff && HasGenericStatBonus(levelData))
        {
            var definition = _current.FindSkill(skillId);
            var duration = Math.Max(
                1,
                levelData.DurationSeconds);

            session.ActiveSkillBuffs[skillId] =
                new SkillBuffRuntime
                {
                    SkillId = skillId,
                    Name = definition?.Name ?? $"Skill {skillId}",
                    Level = skillLevel,
                    DurationSeconds = duration,
                    ExpiresAt = now + TimeSpan.FromSeconds(duration),
                    AtkBonus = levelData.AtkBonus,
                    DefBonus = levelData.DefBonus,
                    MAtkBonus = levelData.MAtkBonus,
                    MDefBonus = levelData.MDefBonus,
                    HitBonus = levelData.HitBonus,
                    FleeBonus = levelData.FleeBonus,
                    LukBonus = levelData.LukBonus,
                    AspdBonus = levelData.AspdBonus,
                    MoveSpeedBonus = levelData.MoveSpeedBonus,
                    CritRateBonus = levelData.CritRateBonus,
                    CritDamageBonus = levelData.CritDamageBonus
                };

            packets.Add(BuildSyncBuffsPacket(
                session,
                now));
        }

        var refreshedAssets = _store.GetCharacterAssets(
            accountId,
            session.CharacterId.Value);
        var refreshedStats = ApplyActiveSkillBuffs(
            CharacterStatCalculator.Calculate(
                updated,
                refreshedAssets,
                _current),
            session,
            now);

        packets.Add(BuildPlayerStatusPacket(
            updated,
            refreshedStats));

        session.SkillReadyAt[skillId] =
            now + TimeSpan.FromSeconds(
                Math.Max(0, levelData.CooldownSeconds));

        BroadcastWorldPackets(session, packets);
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
            session.CurrentMapId <= 0)
            return [];

        var runtime = SyncSessionMonsters(session);
        if (!session.Monsters.TryGetValue(
                targetEntityId,
                out var monster) ||
            monster.MapId != session.CurrentMapId)
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
        var effective = ApplyActiveSkillBuffs(
            CharacterStatCalculator.Calculate(
                character,
                assets,
                _current),
            session,
            DateTimeOffset.UtcNow);

        var attackRange = Math.Max(
            80,
            effective.AttackRange);

        var packets = new List<object>();

        lock (runtime.SyncRoot)
        {
            if (!monster.IsAlive)
                return [];

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

            packets.Add(new
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
            });

            if (monster.CurrentHp > 0 &&
                !monster.HasTarget)
            {
                monster.HasTarget = true;
                monster.TargetCharacterId =
                    session.CharacterId.Value;
                SetAiState(monster, MonsterAiState.Chase);
                packets.Add(new
                {
                    Type = "MonsterTarget",
                    EntityID = monster.EntityId,
                    HasTarget = true,
                    TargetID = session.CharacterId.Value
                });
            }

            if (monster.CurrentHp == 0)
            {
                monster.RespawnAt =
                    DateTimeOffset.UtcNow + RespawnDelay;
                monster.HasTarget = false;
                monster.TargetCharacterId = 0;
                SetAiState(monster, MonsterAiState.Dead);

                packets.Add(new
                {
                    Type = "EntityDeath",
                    EntityID = monster.EntityId,
                    EntityType = 1,
                    KillerID = session.CharacterId.Value
                });

                AddCharacterKillRewards(
                    session,
                    accountId,
                    monster,
                    packets);

                AddMonsterDrops(
                    session,
                    accountId,
                    monster,
                    packets);

                AddMechaKillExperience(
                    session,
                    accountId,
                    monster,
                    packets);
            }
        }

        BroadcastWorldPackets(session, packets);
        return packets;
    }

    private void AddMonsterDrops(
        ClientSession session,
        long accountId,
        MonsterRuntimeState monster,
        List<object> packets)
    {
        if (!session.CharacterId.HasValue)
            return;

        var dbid = session.CharacterId.Value;
        var character = _store.GetCharacter(
            accountId,
            dbid);

        if (character is null)
            return;

        var assets = _store.GetCharacterAssets(
            accountId,
            dbid);
        var lucky = CharacterStatCalculator.Calculate(
                character,
                assets,
                _current)
            .Lucky;

        monster.KillSequence++;

        var drops = _drops.RollDrops(
            monster.EnemyId,
            monster.EntityId,
            monster.KillSequence,
            lucky);

        var offsetIndex = 0;
        foreach (var drop in drops)
        {
            var offsetX = ((offsetIndex % 5) - 2) * 12.0;
            var offsetY = (((offsetIndex / 5) % 5) - 2) * 10.0;
            offsetIndex++;

            var ground = _store.CreateGroundItem(
                dbid,
                drop.TabType,
                drop.ItemId,
                1,
                monster.MapId,
                monster.X + offsetX,
                monster.Y + offsetY,
                protectSeconds: _drops.OwnerProtectionSeconds,
                lifetimeSeconds: 60);

            if (ground is null)
                continue;

            session.GroundItems[ground.EntityId] = ground;
            var now = DateTimeOffset.UtcNow;

            packets.Add(new
            {
                Type = "SpawnEntity",
                EntityID = ground.EntityId,
                EntityType = 3,
                Name = drop.Name,
                LocX = ground.X,
                LocY = ground.Y,
                ExtraData = new
                {
                    ItemIndex = ground.ItemIndex,
                    Count = ground.Count,
                    TabType = ground.TabType,
                    Damage = ground.Damage,
                    Color = ground.Color,
                    Part = ground.Part,
                    RemainingTime = ground.RemainingSeconds(now),
                    OwnerDBID = ground.OwnerDbid,
                    ProtectTimeSeconds = ground.ProtectSeconds(now)
                }
            });

            // The recovered drop table contains explicit probabilities but no
            // separate announcement flag. Treat ultra-rare entries (<= 0.5%)
            // as treasure announcements while preserving the original rate.
            if (drop.Chance <= 0.005)
            {
                var characterName =
                    character.TryGetValue(
                        "NAME",
                        out var name)
                        ? name?.ToString() ??
                          $"角色{dbid}"
                        : $"角色{dbid}";

                _broadcasts.Publish(new
                {
                    Type = "SystemMessageBroadcast",
                    Message =
                        $"[掉寶] 恭喜 {characterName} 擊敗 {monster.Name}，獲得稀有寶物「{drop.Name}」！"
                });
            }
        }
    }

    private void AddCharacterKillRewards(
        ClientSession session,
        long accountId,
        MonsterRuntimeState monster,
        List<object> packets)
    {
        if (!session.CharacterId.HasValue)
            return;

        var dbid = session.CharacterId.Value;
        var before = _store.GetCharacter(accountId, dbid);
        if (before is null)
            return;

        var oldLevel = Math.Clamp(
            Convert.ToInt32(before["Level"]),
            1,
            255);

        // battle.cfg provides ExpX=2 and the original player-vs-monster
        // level-difference ratios. Money still has no recovered source column,
        // so keep only that small fallback isolated here.
        var gainedExp = _battle.ExperienceForKill(
            oldLevel,
            monster.Level);
        var gainedMoney = Math.Max(1, monster.Level);

        var rewarded = _store.AddRewards(
            accountId,
            dbid,
            gainedExp,
            gainedMoney);

        if (rewarded is null)
            return;

        var accumulatedExp = Math.Max(
            0,
            Convert.ToInt32(rewarded["ExpAcc"]));
        var newLevel = _growth.ResolveLevel(
            oldLevel,
            accumulatedExp);
        var abilityPoints = _growth.AbilityPointsGained(
            oldLevel,
            newLevel);
        var maxExp = _growth.ExperienceThresholdForLevel(
            newLevel);

        rewarded = _store.SetExperienceProgress(
                       accountId,
                       dbid,
                       newLevel,
                       maxExp,
                       abilityPoints)
                   ?? rewarded;

        if (newLevel > oldLevel)
        {
            var growth = _growth.Calculate(rewarded);
            rewarded = _store.ApplyGrowthStats(
                           accountId,
                           dbid,
                           growth.MaxHP,
                           growth.MaxMP,
                           growth.MaxSP,
                           growth.Attack,
                           growth.Defense,
                           growth.MagicAttack,
                           growth.MagicDefense,
                           growth.Hit,
                           growth.Dodge,
                           growth.MoveSpeed)
                       ?? rewarded;

            packets.Add(new
            {
                Type = "SystemMessageFlash",
                Message = $"升級！Lv.{newLevel}"
            });
        }

        var assets = _store.GetCharacterAssets(
            accountId,
            dbid);

        packets.Add(
            CharacterStatCalculator.ToPlayerStatusPacket(
                rewarded,
                assets,
                _current));

        packets.Add(new
        {
            Type = "SyncMoney",
            Money = rewarded["Money"]
        });
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

    public IReadOnlyList<object> TickBuffs(
        ClientSession session,
        DateTimeOffset now)
    {
        var removed = PurgeExpiredBuffs(session, now);
        return removed
            ? [BuildSyncBuffsPacket(session, now)]
            : [];
    }

    private static bool PurgeExpiredBuffs(
        ClientSession session,
        DateTimeOffset now)
    {
        var expired = session.ActiveSkillBuffs
            .Where(x => x.Value.ExpiresAt <= now)
            .Select(x => x.Key)
            .ToArray();

        foreach (var skillId in expired)
            session.ActiveSkillBuffs.Remove(skillId);

        return expired.Length > 0;
    }

    private static bool HasGenericStatBonus(
        CurrentSkillLevelDefinition level)
        => level.AtkBonus != 0 ||
           level.DefBonus != 0 ||
           level.MAtkBonus != 0 ||
           level.MDefBonus != 0 ||
           level.HitBonus != 0 ||
           level.FleeBonus != 0 ||
           level.LukBonus != 0 ||
           level.AspdBonus != 0 ||
           level.MoveSpeedBonus != 0 ||
           level.CritRateBonus != 0 ||
           level.CritDamageBonus != 0;

    private static EffectiveCharacterStats ApplyActiveSkillBuffs(
        EffectiveCharacterStats stats,
        ClientSession session,
        DateTimeOffset now)
    {
        PurgeExpiredBuffs(session, now);

        return new EffectiveCharacterStats(
            stats.MaxHP,
            stats.MaxMP,
            stats.MaxSP,
            stats.Attack + session.ActiveSkillBuffs.Values.Sum(x => x.AtkBonus),
            stats.Defense + session.ActiveSkillBuffs.Values.Sum(x => x.DefBonus),
            stats.MagicAttack + session.ActiveSkillBuffs.Values.Sum(x => x.MAtkBonus),
            stats.MagicDefense + session.ActiveSkillBuffs.Values.Sum(x => x.MDefBonus),
            stats.Hit + session.ActiveSkillBuffs.Values.Sum(x => x.HitBonus),
            stats.Dodge + session.ActiveSkillBuffs.Values.Sum(x => x.FleeBonus),
            stats.Lucky + session.ActiveSkillBuffs.Values.Sum(x => x.LukBonus),
            stats.AttackSpeed + session.ActiveSkillBuffs.Values.Sum(x => x.AspdBonus),
            stats.AttackRange,
            stats.MoveSpeed + session.ActiveSkillBuffs.Values.Sum(x => x.MoveSpeedBonus),
            stats.CritRate + session.ActiveSkillBuffs.Values.Sum(x => x.CritRateBonus),
            stats.CritDamage + session.ActiveSkillBuffs.Values.Sum(x => x.CritDamageBonus));
    }

    private static object BuildSyncBuffsPacket(
        ClientSession session,
        DateTimeOffset now)
        => new
        {
            Type = "SyncBuffs",
            Buffs = session.ActiveSkillBuffs.Values
                .Where(x => x.ExpiresAt > now)
                .Select(x => new
                {
                    BuffName = x.Name,
                    SourceType = 2,
                    Value = x.SkillId,
                    RemainingTime = Math.Max(
                        0.0,
                        (x.ExpiresAt - now).TotalSeconds),
                    TotalTime = x.DurationSeconds
                })
                .ToArray()
        };

    private static object BuildPlayerStatusPacket(
        Dictionary<string, object> character,
        EffectiveCharacterStats s)
        => new
        {
            Type = "PlayerStatusSync",
            Level = character["Level"],
            ExpUse = character["ExpUse"],
            Str = character["Str"],
            Dex = character["Dex"],
            Con = character["Con"],
            Wis = character["Wis"],
            HP = Math.Min(Convert.ToInt32(character["HP"]), s.MaxHP),
            MP = Math.Min(Convert.ToInt32(character["MP"]), s.MaxMP),
            SP = Math.Min(Convert.ToInt32(character["SP"]), s.MaxSP),
            MaxHP = s.MaxHP,
            MaxMP = s.MaxMP,
            MaxSP = s.MaxSP,
            Attack = s.Attack,
            Defense = s.Defense,
            MagicAttack = s.MagicAttack,
            MagicDefense = s.MagicDefense,
            Hit = s.Hit,
            Dodge = s.Dodge,
            Lucky = s.Lucky,
            AttackSpeed = s.AttackSpeed,
            AttackRange = s.AttackRange,
            MoveSpeed = s.MoveSpeed,
            CritRate = s.CritRate,
            CritDamage = s.CritDamage
        };

    private static object Flash(string message)
        => new
        {
            Type = "SystemMessageFlash",
            Message = message
        };

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
        SetAiState(monster, MonsterAiState.Wander);
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
