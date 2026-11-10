using System.Collections.Concurrent;
using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Assets;
using SGO.Server.Application.Character;
using SGO.Server.Application.Gm;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.Party;
using SGO.Server.Application.Quest;
using SGO.Server.Application.World;
using SGO.Server.Domain.Sessions;
using SGO.Server.Domain.World;
using SGO.Server.Infrastructure.WorldData;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Combat;

public sealed class CombatService
{
    private const long CharacterExperienceMultiplier = 10_000_000L;
    private static readonly TimeSpan RespawnDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NiciaPlateauRespawnDelay = TimeSpan.FromSeconds(2);
    private const int NiciaPlateauMapId = 13;
    private static readonly TimeSpan MonsterAttackCooldown = TimeSpan.FromMilliseconds(1400);
    // Idle/wandering movement has no combat urgency. Use sparse, staggered
    // position changes, but preserve the 250ms chase, attack and AOI ticks.
    private static readonly TimeSpan WanderMinInterval = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan WanderMaxInterval = TimeSpan.FromMilliseconds(4000);

    private const double AggroRadius = 420.0;
    private const double LoseTargetRadius = 700.0;
    private const double AttackRange = 100.0;
    private const double EngageDistance = 90.0;
    // Distance is calculated from normalized double coordinates. Values that
    // should be exactly 90 can become 90.0000000000001, which previously
    // entered Chase with a sub-epsilon step and left the monster stuck.
    private const double EngageDistanceTolerance = 0.01;
    private const double MoveStep = 35.0;
    // Keep the movement packet duration aligned with the 250 ms world cadence.
    // The client now interpolates continuously toward the latest authoritative
    // waypoint, so this value represents movement speed rather than tween timing.
    private const double MoveTweenSeconds = 0.25;
#if DEBUG
    // Small debug-only sample for diagnosing start-of-chase path stalls.
    // Release builds do not emit path diagnostics or pay for formatting.
    private int _debugPathBlockLogCount;
#endif
    private const double WanderRadius = 260.0;
    private const double RespawnRandomRadius = 120.0;
    private const double WanderMinStep = 95.0;
    private const double WanderMaxStep = 175.0;

    private readonly IGameStore _store;
    private readonly WorldDataService _world;
    private readonly CurrentClientDataCatalog _current;
    private readonly LegacyPlayerGrowthCatalog _growth;
    private readonly LegacyBattleRulesCatalog _battle;
    private readonly EnemyDropCatalog _drops;
    private readonly WorldRuntimeService _runtime;
    private readonly BlockingMapService _blocking;
    private readonly MonsterNavigationService _navigation;
    private readonly GroundDropPlacementService _dropPlacement;
    private readonly ServerBroadcastHub _broadcasts;
    private readonly PartyService _party;
    private readonly PetEvolutionService _petEvolution;
    private readonly QuestProgressService _questProgress;
    private readonly GlobalRateService _globalRates;
    private readonly ILogger<CombatService> _log;

    private sealed record PendingRevive(
        long TargetAccountId,
        long TargetDbId,
        long CasterDbId,
        int MapId,
        int SkillId,
        int SkillLevel,
        int HpPercent,
        int MpPercent,
        int DeathExpReductionPercent,
        DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<long, PendingRevive>
        _pendingRevives = new();

    // Buffs are character-scoped rather than connection-scoped so effects
    // applied by another player remain authoritative for combat/stat
    // calculation on the target player's own connection.
    private readonly ConcurrentDictionary<
        long,
        ConcurrentDictionary<int, SkillBuffRuntime>>
        _skillBuffsByCharacter = new();

    public CombatService(
        IGameStore store,
        WorldDataService world,
        BlockingMapService blocking,
        CurrentClientDataCatalog current,
        LegacyPlayerGrowthCatalog growth,
        LegacyBattleRulesCatalog battle,
        EnemyDropCatalog drops,
        WorldRuntimeService runtime,
        ServerBroadcastHub broadcasts,
        PartyService party,
        PetEvolutionService petEvolution,
        QuestProgressService questProgress,
        ILogger<CombatService> log,
        GlobalRateService? globalRates = null)
    {
        _store = store;
        _world = world;
        _blocking = blocking;
        _navigation = new MonsterNavigationService(blocking);
        _dropPlacement = new GroundDropPlacementService(blocking);
        _current = current;
        _growth = growth;
        _battle = battle;
        _drops = drops;
        _runtime = runtime;
        _broadcasts = broadcasts;
        _party = party;
        _petEvolution = petEvolution;
        _questProgress = questProgress;
        _globalRates = globalRates ?? new GlobalRateService();
        _log = log;
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
                    RegisterMonsterAoi(
                        pair.Value,
                        session.ConnectionId);
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
                    RegisterMonsterAoi(
                        monster,
                        session.ConnectionId);
            }
        }

        // NPCs are low-frequency static map content. Dynamic monsters are
        // delivered by WorldEntitySpatialAoi after WorldReady.
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
                monster.TargetPetUid = 0;
                monster.LastAiAt = DateTimeOffset.MinValue;
                monster.LastMovePacketX = null;
                monster.LastMovePacketY = null;
                SetAiState(monster, MonsterAiState.Idle);
                var respawnPosition = ResolveRespawnPosition(monster);
                monster.X = respawnPosition.X;
                monster.Y = respawnPosition.Y;
                monster.WanderCenterX = respawnPosition.X;
                monster.WanderCenterY = respawnPosition.Y;
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
        DateTimeOffset now,
        IReadOnlyList<int>? monsterEntityIds = null)
    {
        session.LastMonsterAiTick = now;

        if (!session.CharacterId.HasValue ||
            session.CurrentMapId <= 0 ||
            !session.WorldReady)
            return [];

        var currentCharacterId = session.CharacterId.Value;
        var character = _store.GetCharacter(
            accountId,
            currentCharacterId);
        var playerAlive =
            character is not null &&
            Convert.ToInt32(character["HP"]) > 0;

        var assets = _store.GetCharacterAssets(
            accountId,
            currentCharacterId);

        var runtime = SyncSessionMonsters(session);
        var packets = new List<object>();

        lock (runtime.SyncRoot)
        {
            // A shared-map tick assigns each monster to one eligible player
            // (its current target's owner, or the closest player when idle).
            // Standalone tests can omit the filter to retain legacy behavior.
            var candidates = monsterEntityIds is null
                ? runtime.Monsters.Values.ToArray()
                : monsterEntityIds
                    .Where(id => runtime.Monsters.ContainsKey(id))
                    .Select(id => runtime.Monsters[id])
                    .ToArray();

            foreach (var monster in candidates)
            {
                if (!monster.IsAlive)
                {
                    SetAiState(monster, MonsterAiState.Dead);
                    continue;
                }

                Dictionary<string, object>? activePet = null;
                var activePetUid = 0L;
                var activePetPosition =
                    (X: 0.0, Y: 0.0);
                var petDistanceSquared =
                    double.MaxValue;

                foreach (var candidatePet in
                         assets.Pets.Where(x =>
                             Convert.ToInt32(
                                 x["State"]) == 1 &&
                             Convert.ToInt32(
                                 x["HP"]) > 0))
                {
                    var candidateUid =
                        Convert.ToInt64(
                            candidatePet["UID"]);

                    if (!session.PetPositions.TryGetValue(
                            candidateUid,
                            out var candidatePosition))
                    {
                        continue;
                    }

                    var candidateDx =
                        candidatePosition.X -
                        monster.X;
                    var candidateDy =
                        candidatePosition.Y -
                        monster.Y;
                    var candidateDistanceSquared =
                        candidateDx * candidateDx +
                        candidateDy * candidateDy;

                    if (candidateDistanceSquared >=
                        petDistanceSquared)
                    {
                        continue;
                    }

                    activePet = candidatePet;
                    activePetUid = candidateUid;
                    activePetPosition =
                        candidatePosition;
                    petDistanceSquared =
                        candidateDistanceSquared;
                }

                var petAlive =
                    activePet is not null;

                // Only the target owner's websocket advances an already-owned
                // monster. For pet targets, the owner connection also owns the
                // authoritative pet runtime position.
                if (monster.HasTarget &&
                    monster.TargetCharacterId > 0 &&
                    monster.TargetCharacterId != currentCharacterId)
                {
                    var targetPresence =
                        _broadcasts.FindCharacter(
                            monster.TargetCharacterId);

                    if (targetPresence is not null &&
                        targetPresence.MapId == session.CurrentMapId)
                    {
                        // A foreign pet target cannot be validated from this
                        // session because pet positions are owner-session
                        // runtime state. Let its owner connection drive it.
                        if (monster.TargetPetUid > 0)
                        {
                            if (monster.LastStallLogAt == DateTimeOffset.MinValue ||
                                now - monster.LastStallLogAt >= TimeSpan.FromSeconds(2))
                            {
                                monster.LastStallLogAt = now;
                                _log.LogDebug(
                                    "[MONSTER_AI_FOREIGN_PET_OWNER] EntityID={EntityID} TargetCharacterID={TargetCharacterID} TargetPetUID={TargetPetUID} MapID={MapID}",
                                    monster.EntityId,
                                    monster.TargetCharacterId,
                                    monster.TargetPetUid,
                                    session.CurrentMapId);
                            }
                            continue;
                        }

                        var targetCharacter = _store.GetCharacter(
                            targetPresence.AccountId,
                            monster.TargetCharacterId);

                        if (targetCharacter is not null &&
                            Convert.ToInt32(targetCharacter["HP"]) > 0)
                        {
                            var foreignX =
                                Convert.ToDouble(targetCharacter["LocX"]);
                            var foreignY =
                                Convert.ToDouble(targetCharacter["LocY"]);
                            var foreignDx = foreignX - monster.X;
                            var foreignDy = foreignY - monster.Y;
                            var foreignDistance = Math.Sqrt(
                                foreignDx * foreignDx +
                                foreignDy * foreignDy);

                            if (foreignDistance <= LoseTargetRadius)
                            {
                                if (monster.LastStallLogAt == DateTimeOffset.MinValue ||
                                    now - monster.LastStallLogAt >= TimeSpan.FromSeconds(2))
                                {
                                    monster.LastStallLogAt = now;
                                    _log.LogDebug(
                                        "[MONSTER_AI_FOREIGN_OWNER] EntityID={EntityID} State={State} TargetCharacterID={TargetCharacterID} TargetPetUID={TargetPetUID} Monster=({MonsterX:F1},{MonsterY:F1}) ForeignTarget=({TargetX:F1},{TargetY:F1}) Distance={Distance:F1} MapID={MapID}",
                                        monster.EntityId,
                                        monster.AiState,
                                        monster.TargetCharacterId,
                                        monster.TargetPetUid,
                                        monster.X,
                                        monster.Y,
                                        foreignX,
                                        foreignY,
                                        foreignDistance,
                                        session.CurrentMapId);
                                }
                                continue;
                            }
                        }
                    }

                    DropMonsterTarget(monster, now);
                    packets.Add(new
                    {
                        Type = "MonsterTarget",
                        EntityID = monster.EntityId,
                        HasTarget = false
                    });
                }

                if (monster.HasTarget &&
                    monster.TargetCharacterId == 0)
                {
                    monster.TargetCharacterId =
                        currentCharacterId;
                }

                // Resolve an existing target owned by this player.
                var targetPetUid = 0L;
                var targetIsPet = false;
                var targetX = playerX;
                var targetY = playerY;
                var targetAlive = playerAlive;
                Dictionary<string, object>? targetPet = null;

                if (monster.HasTarget &&
                    monster.TargetCharacterId == currentCharacterId &&
                    monster.TargetPetUid > 0)
                {
                    targetPetUid = monster.TargetPetUid;
                    targetPet = assets.Pets.FirstOrDefault(x =>
                        Convert.ToInt64(x["UID"]) == targetPetUid &&
                        Convert.ToInt32(x["State"]) == 1 &&
                        Convert.ToInt32(x["HP"]) > 0);

                    if (targetPet is not null &&
                        session.PetPositions.TryGetValue(
                            targetPetUid,
                            out var petPosition))
                    {
                        targetIsPet = true;
                        targetAlive = true;
                        targetX = petPosition.X;
                        targetY = petPosition.Y;
                    }
                    else
                    {
                        DropMonsterTarget(monster, now);
                        packets.Add(new
                        {
                            Type = "MonsterTarget",
                            EntityID = monster.EntityId,
                            HasTarget = false
                        });
                    }
                }

                // An idle monster chooses the closest valid target. A summoned
                // pet is therefore aggroed exactly like a nearby player; it
                // does not need to attack first.
                if (!monster.HasTarget)
                {
                    var playerDx = playerX - monster.X;
                    var playerDy = playerY - monster.Y;
                    var playerDistanceSquared =
                        playerDx * playerDx +
                        playerDy * playerDy;

                    var playerInAggro =
                        playerAlive &&
                        playerDistanceSquared <=
                        AggroRadius * AggroRadius;
                    var petInAggro =
                        petAlive &&
                        petDistanceSquared <=
                        AggroRadius * AggroRadius;

                    if (playerInAggro || petInAggro)
                    {
                        targetIsPet =
                            petInAggro &&
                            (!playerInAggro ||
                             petDistanceSquared <
                             playerDistanceSquared);

                        monster.HasTarget = true;
                        monster.TargetCharacterId =
                            currentCharacterId;
                        monster.TargetPetUid =
                            targetIsPet
                                ? activePetUid
                                : 0;
                        monster.LastAiAt = now;
                        monster.LastActionAt = now;
                        SetAiState(
                            monster,
                            MonsterAiState.Chase);

                        if (targetIsPet)
                        {
                            targetPetUid = activePetUid;
                            targetPet = activePet;
                            targetAlive = true;
                            targetX = activePetPosition.X;
                            targetY = activePetPosition.Y;
                        }
                        else
                        {
                            targetAlive = playerAlive;
                            targetX = playerX;
                            targetY = playerY;
                        }

                        packets.Add(new
                        {
                            Type = "MonsterTarget",
                            EntityID = monster.EntityId,
                            HasTarget = true,
                            TargetID = targetIsPet
                                ? 4_000_000L + targetPetUid
                                : currentCharacterId
                        });
                    }
                }

                if (!monster.HasTarget)
                {
                    var nearPlayerDx = playerX - monster.X;
                    var nearPlayerDy = playerY - monster.Y;
                    var nearPlayerDistance = Math.Sqrt(
                        nearPlayerDx * nearPlayerDx +
                        nearPlayerDy * nearPlayerDy);

                    var nearPetDistance =
                        petAlive
                            ? Math.Sqrt(
                                petDistanceSquared)
                            : double.MaxValue;

                    if ((playerAlive &&
                         nearPlayerDistance <= AggroRadius) ||
                        (petAlive &&
                         nearPetDistance <= AggroRadius))
                    {
                        if (monster.LastStallLogAt == DateTimeOffset.MinValue ||
                            now - monster.LastStallLogAt >=
                            TimeSpan.FromSeconds(2))
                        {
                            monster.LastStallLogAt = now;
                            _log.LogDebug(
                                "[MONSTER_AI_NO_TARGET_NEARBY] EntityID={EntityID} State={State} Monster=({MonsterX:F1},{MonsterY:F1}) PlayerDistance={PlayerDistance:F1} PetDistance={PetDistance:F1} PlayerAlive={PlayerAlive} PetAlive={PetAlive} LastAiAt={LastAiAt:o} LastActionAt={LastActionAt:o}",
                                monster.EntityId,
                                monster.AiState,
                                monster.X,
                                monster.Y,
                                nearPlayerDistance,
                                nearPetDistance,
                                playerAlive,
                                petAlive,
                                monster.LastAiAt,
                                monster.LastActionAt);
                        }
                    }

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

                if (!targetAlive)
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

                // Re-resolve a pet target after acquisition above.
                if (monster.TargetPetUid > 0)
                {
                    targetPetUid = monster.TargetPetUid;
                    targetPet ??= assets.Pets.FirstOrDefault(x =>
                        Convert.ToInt64(x["UID"]) == targetPetUid &&
                        Convert.ToInt32(x["State"]) == 1 &&
                        Convert.ToInt32(x["HP"]) > 0);

                    if (targetPet is null ||
                        !session.PetPositions.TryGetValue(
                            targetPetUid,
                            out var currentPetPosition))
                    {
                        DropMonsterTarget(monster, now);
                        continue;
                    }

                    targetIsPet = true;
                    targetX = currentPetPosition.X;
                    targetY = currentPetPosition.Y;
                }
                else
                {
                    targetIsPet = false;
                    targetX = playerX;
                    targetY = playerY;
                    targetAlive = playerAlive;
                }

                monster.LastAiAt = now;

                var dx = targetX - monster.X;
                var dy = targetY - monster.Y;
                var distance = Math.Sqrt(
                    dx * dx +
                    dy * dy);

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

                // Being geometrically close is not sufficient if a wall
                // separates the target from the monster.
                var hasClearAttackPath = _navigation.IsDirectClear(
                    monster.MapId, monster.X, monster.Y, targetX, targetY);

                if (distance <= EngageDistance + EngageDistanceTolerance &&
                    hasClearAttackPath)
                {
                    var enteredAttackRange =
                        monster.AiState != MonsterAiState.Attack;
                    var wasMoving = monster.AiState is
                        MonsterAiState.Chase or
                        MonsterAiState.Wander;

                    // Movement uses hysteresis to avoid visual direction
                    // jitter, but attack facing must always point at the
                    // current target. Reusing the stable movement direction
                    // could keep the previous sector for up to 30 degrees.
                    var attackDir = DirectionFromDelta(
                        dx,
                        dy);
                    var directionChanged =
                        !string.Equals(
                            attackDir,
                            monster.CurrentDir,
                            StringComparison.Ordinal);

                    monster.CurrentDir = attackDir;
                    SetAiState(
                        monster,
                        MonsterAiState.Attack);

                    var cooldownElapsed =
                        monster.LastAttackAt > now ||
                        now - monster.LastAttackAt >=
                        MonsterAttackCooldown;
                    var attackReady =
                        distance <= AttackRange &&
                        (enteredAttackRange || cooldownElapsed);

                    if (attackReady)
                    {
                        var rawDamage =
                            Math.Max(1, monster.Level * 2);

                        if (targetIsPet &&
                            targetPet is not null)
                        {
                            var defense = Math.Max(
                                0,
                                Convert.ToInt32(
                                    targetPet["Defense"]));
                            var damage = Math.Max(
                                1,
                                rawDamage - defense / 5);
                            var updatedPet =
                                _store.ApplyPetDamage(
                                    accountId,
                                    currentCharacterId,
                                    targetPetUid,
                                    damage);

                            if (updatedPet is not null)
                            {
                                monster.LastAttackAt = now;
                                monster.LastActionAt = now;
                                var currentHp =
                                    Convert.ToInt32(
                                        updatedPet["HP"]);
                                var maxHp =
                                    Convert.ToInt32(
                                        updatedPet["MaxHP"]);
                                var petEntityId =
                                    4_000_000L + targetPetUid;

                                if (enteredAttackRange)
                                {
                                    packets.Add(new
                                    {
                                        Type = "MoveEntity",
                                        EntityID = monster.EntityId,
                                        X = monster.X,
                                        Y = monster.Y,
                                        Dir = monster.CurrentDir,
                                        SpeedTime = 0.0
                                    });
                                }

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
                                    TargetID = petEntityId,
                                    TargetType = 4,
                                    Damage = damage,
                                    CurrentHP = currentHp,
                                    MaxHP = maxHp,
                                    IsCrit = false,
                                    SkillID = 0,
                                    SkillLevel = 1,
                                    AttackerID = monster.EntityId
                                });

                                var refreshedAssets =
                                    _store.GetCharacterAssets(
                                        accountId,
                                        currentCharacterId);
                                packets.Add(new
                                {
                                    Type = "PetSync",
                                    Pets = refreshedAssets.Pets,
                                    MaxSize =
                                        refreshedAssets.PetMaxSize
                                });

                                if (currentHp <= 0)
                                {
                                    session.PetPositions.Remove(
                                        targetPetUid);
                                    session.PetLastAttackAt.Remove(
                                        targetPetUid);
                                    session.PetTargets.Remove(
                                        targetPetUid);
                                    session.PetOutOfRangeSince.Remove(
                                        targetPetUid);

                                    packets.Add(new
                                    {
                                        Type = "EntityDeath",
                                        EntityID = petEntityId,
                                        EntityType = 4,
                                        KillerID = monster.EntityId
                                    });

                                    DropMonsterTarget(
                                        monster,
                                        now,
                                        scheduleWander: false);
                                }
                            }
                        }
                        else if (character is not null &&
                                 Convert.ToInt32(
                                     character["HP"]) > 0)
                        {
                            var effective =
                                ApplyActiveSkillBuffs(
                                    CharacterStatCalculator.Calculate(
                                        character,
                                        assets,
                                        _current),
                                    session,
                                    now);
                            var defense =
                                Math.Max(0, effective.Defense);
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
                                monster.LastActionAt = now;
                                character = updated;
                                var currentHp =
                                    Convert.ToInt32(updated["HP"]);
                                var updatedAssets =
                                    _store.GetCharacterAssets(
                                        accountId,
                                        currentCharacterId);
                                var updatedEffective =
                                    ApplyActiveSkillBuffs(
                                        CharacterStatCalculator.Calculate(
                                            updated,
                                            updatedAssets,
                                            _current),
                                        session,
                                        now);
                                var maxHp =
                                    updatedEffective.MaxHP;

                                if (enteredAttackRange)
                                {
                                    packets.Add(new
                                    {
                                        Type = "MoveEntity",
                                        EntityID = monster.EntityId,
                                        X = monster.X,
                                        Y = monster.Y,
                                        Dir = monster.CurrentDir,
                                        SpeedTime = 0.0
                                    });
                                }

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
                                    TargetID =
                                        currentCharacterId,
                                    TargetType = 0,
                                    Damage = damage,
                                    CurrentHP = currentHp,
                                    MaxHP = maxHp,
                                    IsCrit = false,
                                    SkillID = 0,
                                    SkillLevel = 1,
                                    AttackerID =
                                        monster.EntityId
                                });

                                packets.Add(
                                    BuildPlayerStatusPacket(
                                        updated,
                                        updatedEffective));

                                if (currentHp <= 0)
                                {
                                    packets.Add(new
                                    {
                                        Type = "EntityDeath",
                                        EntityID =
                                            currentCharacterId,
                                        EntityType = 0,
                                        KillerID =
                                            monster.EntityId
                                    });

                                    DropMonsterTarget(
                                        monster,
                                        now,
                                        scheduleWander: false);
                                }
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

                    if (monster.LastActionAt != DateTimeOffset.MinValue &&
                        now - monster.LastActionAt >= TimeSpan.FromSeconds(2) &&
                        (monster.LastStallLogAt == DateTimeOffset.MinValue ||
                         now - monster.LastStallLogAt >= TimeSpan.FromSeconds(2)))
                    {
                        monster.LastStallLogAt = now;
                        _log.LogDebug(
                            "[MONSTER_AI_STALL] EntityID={EntityID} State={State} HasTarget={HasTarget} TargetCharacterID={TargetCharacterID} TargetPetUID={TargetPetUID} Monster=({MonsterX:F1},{MonsterY:F1}) Target=({TargetX:F1},{TargetY:F1}) Distance={Distance:F1} LastAttackAt={LastAttackAt:o} LastActionAt={LastActionAt:o}",
                            monster.EntityId,
                            monster.AiState,
                            monster.HasTarget,
                            monster.TargetCharacterId,
                            monster.TargetPetUid,
                            monster.X,
                            monster.Y,
                            targetX,
                            targetY,
                            distance,
                            monster.LastAttackAt,
                            monster.LastActionAt);
                    }

                    continue;
                }

                // Failed routes are retried at a bounded rate instead of
                // running a costly path search every 250ms against a wall.
                if (now < monster.NextPathRetryAt)
                    continue;

                var wasAlreadyIdle = monster.AiState == MonsterAiState.Idle;
                SetAiState(monster, MonsterAiState.Chase);

                var step = hasClearAttackPath
                    ? Math.Min(MoveStep, Math.Max(0.0, distance - EngageDistance))
                    : MoveStep;

                if (step <= 0.0)
                {
                    SetAiState(
                        monster,
                        MonsterAiState.Attack);
                    continue;
                }

                if (!_navigation.TryNextStep(
                        monster.MapId,
                        monster.X,
                        monster.Y,
                        targetX,
                        targetY,
                        step,
                        out var nx,
                        out var ny))
                {
                    // No collision-free next step. Do not animate running
                    // in place, spam idle packets, or repeatedly pathfind.
#if DEBUG
                    if (System.Threading.Interlocked.Increment(
                            ref _debugPathBlockLogCount) <= 40)
                    {
                        _log.LogInformation(
                            "[MONSTER_AI_PATH_BLOCK] Map={MapID} EntityID={EntityID} WasIdle={WasIdle} From=({X:F1},{Y:F1}) Target=({TargetX:F1},{TargetY:F1}) Distance={Distance:F1} ClearAttackPath={ClearAttackPath} RetryMs=750",
                            monster.MapId, monster.EntityId, wasAlreadyIdle,
                            monster.X, monster.Y, targetX, targetY,
                            distance, hasClearAttackPath);
                    }
#endif
                    monster.NextPathRetryAt =
                        now + TimeSpan.FromMilliseconds(750);
                    SetAiState(monster, MonsterAiState.Idle);
                    if (!wasAlreadyIdle)
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

                monster.NextPathRetryAt = DateTimeOffset.MinValue;
                var moveDx = nx - monster.X;
                var moveDy = ny - monster.Y;

                var oldX = monster.X;
                var oldY = monster.Y;
                monster.X = nx;
                monster.Y = ny;

                TryAddMonsterMovePacket(
                    monster,
                    packets,
                    now,
                    oldX,
                    oldY,
                    nx,
                    ny,
                    MoveTweenSeconds,
                    moveDx,
                    moveDy,
                    targetX,
                    targetY,
                    distance);
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
            // Existing entries reference the authoritative mutable objects.
            // Avoid rebuilding every player's mirror on every AI tick.
            if (session.LoadedMonsterMapId != session.CurrentMapId ||
                session.Monsters.Count != runtime.Monsters.Count)
            {
                session.Monsters.Clear();
                foreach (var pair in runtime.Monsters)
                    session.Monsters[pair.Key] = pair.Value;
            }
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
            var type = PacketString(
                packet,
                "Type");

            if (type is not (
                    "SpawnEntity" or
                    "DespawnEntity" or
                    "MoveEntity" or
                    "SyncState" or
                    "EntityDamage" or
                    "EntityDeath" or
                    "EntityHeal" or
                    "MonsterTarget" or
                    "SkillCast" or
                    "PlayEffect"))
                continue;

            if (TryResolveMonsterAoiEntityId(
                    session,
                    packet,
                    type,
                    out var monsterEntityId) &&
                session.Monsters.TryGetValue(
                    monsterEntityId,
                    out var monster))
            {
                if (type == "EntityDeath")
                {
                    // Observers that know the monster receive the death
                    // animation first. Removing silently afterwards makes a
                    // future respawn a fresh AOI SpawnEntity.
                    _broadcasts.PublishToWorldEntityObservers(
                        monsterEntityId,
                        packet,
                        session.ConnectionId);
                    _broadcasts.RemoveWorldEntity(
                        monsterEntityId,
                        notifyObservers: false);
                    continue;
                }

                if (type == "DespawnEntity")
                {
                    _broadcasts.PublishToWorldEntityObservers(
                        monsterEntityId,
                        packet,
                        session.ConnectionId);
                    _broadcasts.RemoveWorldEntity(
                        monsterEntityId,
                        notifyObservers: false);
                    continue;
                }

                // Keep the AOI position + spawn factory current before any
                // incremental event. If the monster crossed an AOI boundary,
                // Spawn/Despawn is queued before Move/State/Damage.
                RegisterMonsterAoi(
                    monster,
                    type == "SpawnEntity"
                        ? session.ConnectionId
                        : default);

                if (type != "SpawnEntity")
                {
                    _broadcasts.PublishToWorldEntityObservers(
                        monsterEntityId,
                        packet,
                        session.ConnectionId);
                }
                continue;
            }

            // Ground SpawnEntity is registered with world AOI at creation.
            // The killer receives it in the direct response, so broadcasting
            // it here would either duplicate it or leak it across the map.
            if (type == "SpawnEntity" &&
                PacketLong(packet, "EntityType") == 3)
            {
                continue;
            }

            if (type == "EntityHeal" &&
                PacketLong(packet, "TargetType") == 0)
            {
                var targetCharacterId =
                    PacketLong(
                        packet,
                        "TargetID");
                var targetPresence =
                    targetCharacterId > 0
                        ? _broadcasts.FindCharacter(
                            targetCharacterId)
                        : null;

                if (targetPresence is not null &&
                    targetPresence.MapId ==
                        session.CurrentMapId)
                {
                    if (session.CharacterId !=
                        targetCharacterId)
                    {
                        _broadcasts.PublishToCharacter(
                            targetCharacterId,
                            packet);
                    }

                    _broadcasts.PublishToPlayerObservers(
                        targetPresence.ConnectionId,
                        packet,
                        session.ConnectionId);
                    continue;
                }
            }

            // Player-origin visuals follow the tighter player AOI. Party,
            // trade and directed responses never enter this path.
            if (IsPlayerOriginAoiPacket(
                    packet,
                    type,
                    session.CharacterId))
            {
                _broadcasts.PublishToPlayerObservers(
                    session.ConnectionId,
                    packet);
            }
            else
            {
                // Static/legacy world events without tracked entity ownership
                // remain map events until they get their own known-set model.
                _broadcasts.PublishToMap(
                    session.CurrentMapId,
                    packet,
                    session.ConnectionId);
            }
        }
    }

    private void RegisterMonsterAoi(
        MonsterRuntimeState monster,
        Guid skipObserver = default)
    {
        if (!monster.IsAlive)
            return;

        var changes =
            _broadcasts.UpsertWorldEntity(
                new WorldAoiEntity(
                    monster.EntityId,
                    1,
                    monster.MapId,
                    monster.X,
                    monster.Y,
                    () => BuildMonsterSpawnPacket(
                        monster)));

        foreach (var change in changes)
        {
            if (change.ObserverConnectionId ==
                skipObserver)
                continue;

            if (_broadcasts.IsWorldAoiVisible(
                    change.ObserverConnectionId,
                    change.Entity.EntityId) !=
                change.Appeared)
                continue;

            _broadcasts.PublishToConnection(
                change.ObserverConnectionId,
                change.Appeared
                    ? change.Entity.SpawnPacketFactory()
                    : new
                    {
                        Type = "DespawnEntity",
                        EntityID = change.Entity.EntityId
                    });
        }
    }

    internal static bool TryResolveMonsterAoiEntityId(
        ClientSession session,
        object packet,
        string? type,
        out int entityId)
    {
        entityId = 0;

        long candidate = type switch
        {
            "SpawnEntity"
                when PacketLong(packet, "EntityType") == 1
                => PacketLong(packet, "EntityID"),
            "MoveEntity" or
            "SyncState" or
            "MonsterTarget" or
            "DespawnEntity"
                => PacketLong(packet, "EntityID"),
            "EntityDeath"
                when PacketLong(packet, "EntityType") == 1
                => PacketLong(packet, "EntityID"),
            "EntityDamage"
                when PacketLong(packet, "TargetType") == 1
                => PacketLong(packet, "TargetID"),
            "EntityDamage"
                => PacketLong(packet, "AttackerID"),
            "EntityHeal"
                when PacketLong(packet, "TargetType") == 1
                => PacketLong(packet, "TargetID"),
            "SkillCast"
                when PacketLong(packet, "CasterType") == 1
                => PacketLong(packet, "CasterID"),
            _ => 0
        };

        if (candidate <= 0 ||
            candidate > int.MaxValue ||
            !session.Monsters.ContainsKey(
                (int)candidate))
            return false;

        entityId = (int)candidate;
        return true;
    }

    private static long PacketLong(
        object packet,
        string property)
    {
        var raw = packet.GetType()
            .GetProperty(property)
            ?.GetValue(packet);

        if (raw is null)
            return 0;

        try
        {
            return Convert.ToInt64(raw);
        }
        catch
        {
            return 0;
        }
    }

    private static string? PacketString(
        object packet,
        string property)
        => packet.GetType()
            .GetProperty(property)
            ?.GetValue(packet)
            ?.ToString();

    internal static bool IsPlayerOriginAoiPacket(
        object packet, string? type, long? characterId)
    {
        if (!characterId.HasValue || characterId.Value <= 0)
            return false;

        var property = type switch
        {
            "SkillCast" => "CasterID",
            "SpawnEntity" => "EntityID",
            "DespawnEntity" => "EntityID",
            "SyncState" => "EntityID",
            "EntityDeath" => "EntityID",
            "EntityHeal" => "TargetID",
            "EntityDamage" => "AttackerID",
            "PlayEffect" => "EntityID",
            _ => null
        };
        if (property is null)
            return false;

        var raw = packet.GetType().GetProperty(property)?.GetValue(packet);
        if (raw is null)
            return false;

        var entityId =
            Convert.ToInt64(raw);

        if (entityId == characterId.Value)
            return true;

        // Companion world entities use the recovered 4M/5M ranges. Their
        // owner already defines visibility through PlayerSpatialAoi, so
        // spawn/despawn/state/death visuals must never fall back to full-map.
        return (type is
                    "SpawnEntity" or
                    "DespawnEntity" or
                    "SyncState" or
                    "EntityDeath") &&
               entityId >= 4_000_000 &&
               entityId < 6_000_000;
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
        monster.TargetPetUid = 0;
        monster.NextPathRetryAt = DateTimeOffset.MinValue;
        monster.LastMovePacketX = null;
        monster.LastMovePacketY = null;
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
            // WorldEntitySpatialAoi uses EntityID as a process-wide key.
            // Dynamic/GM monsters therefore include mapId in their range,
            // matching the uniqueness rule used by normal/supplemental mobs.
            var entityId = checked(
                9_000_000 +
                session.CurrentMapId * 10_000);
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
            return [Flash("攻擊目標不存在或已離開目前地圖")];

        var packets = new List<object>();

        lock (runtime.SyncRoot)
        {
            if (!monster.IsAlive)
                return [Flash("目標已死亡")];

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
                monster.TargetPetUid = petUid;
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
                    DateTimeOffset.UtcNow + GetRespawnDelay(monster.MapId);
                monster.HasTarget = false;
                monster.TargetCharacterId = 0;
                monster.TargetPetUid = 0;
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

                AddActivePetKillExperience(
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

        PurgeExpiredBuffs(
            session.CharacterId.Value,
            now);

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

        var skillDefinition = _current.FindSkill(skillId);
        var specialSkill =
            skillDefinition?.SpecialSkill?.Trim() ?? "";
        var hasCreateItemSpecial =
            TryParseAddItemSpecial(
                specialSkill,
                out var createItemId,
                out var createItemCount);
        var isSelfRecovery = skillId is
            25101 or 25106 or 25108 or 25109;
        var isPartyReturn =
            specialSkill.Equals(
                "平安之祝",
                StringComparison.Ordinal);
        var isTaunt =
            specialSkill.Equals(
                "挑撥",
                StringComparison.Ordinal);
        var isScout =
            specialSkill.Equals(
                "偵察",
                StringComparison.Ordinal);
        var isPortableRepair =
            skillId == 31112;
        var isScreenAreaAttack =
            specialSkill.Equals(
                "畫面範圍攻擊",
                StringComparison.Ordinal);

        var hasBonus = HasGenericStatBonus(levelData);
        var isHeal = levelData.SkillType == 2;
        var isGroupHeal = levelData.SkillType == 3;
        var isRevive = skillId is
            27104 or // 生命之源
            28109 or // 天使的祝福
            27111;   // 生命之祝
        var isBuff = levelData.SkillType is 4 or 5 || hasBonus;
        var isPartyStatBuff = skillId is
            33011 or // 冰之觸
            33012;   // 黑暗之影
        var isAttack =
            levelData.SkillType is 0 or 1 ||
            (levelData.SkillType == 7 &&
             !hasCreateItemSpecial &&
             !isSelfRecovery &&
             !isPartyReturn &&
             !isTaunt &&
             !isScout &&
             (targetType == 1 ||
              levelData.BaseDamage != 0 ||
              levelData.DamageMultiplier != 0));

        // All damaging AOE skills share one batch settlement pipeline.
        // Skill-specific behavior stays in data/formulas, not a second flow.
        var isAreaAttack =
            isAttack &&
            levelData.AoeRadius > 0;
        var isSelfCenteredAreaAttack =
            isAreaAttack &&
            targetType == 0 &&
            targetEntityId == session.CharacterId.Value;

        if (!isAttack &&
            !isHeal &&
            !isGroupHeal &&
            !isBuff &&
            !isRevive &&
            !hasCreateItemSpecial &&
            !isSelfRecovery &&
            !isPartyReturn &&
            !isTaunt &&
            !isScout &&
            !isPortableRepair)
        {
            return [Flash(
                "此技能屬於特殊/製造類型，效果尚未還原")];
        }

        MonsterRuntimeState? primaryMonster = null;
        OnlinePlayerPresence? reviveTargetPresence = null;
        Dictionary<string, object>? reviveTargetCharacter = null;
        OnlinePlayerPresence? healTargetPresence = null;
        Dictionary<string, object>? healTargetCharacter = null;
        OnlinePlayerPresence? groupHealCenterPresence = null;
        Dictionary<string, object>? groupHealCenterCharacter = null;
        OnlinePlayerPresence? scoutTargetPresence = null;
        Dictionary<string, object>? scoutTargetCharacter = null;
        double centerX = targetX;
        double centerY = targetY;

        if (isAttack)
            SyncSessionMonsters(session);

        if (isAttack)
        {
            if (isScreenAreaAttack ||
                isSelfCenteredAreaAttack)
            {
                targetEntityId =
                    checked((int)session.CharacterId.Value);
                targetType = 0;
                centerX = session.LastX;
                centerY = session.LastY;
            }
            else
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

                var playerX = session.LastX;
                var playerY = session.LastY;
                var castDx = centerX - playerX;
                var castDy = centerY - playerY;
                var castRange = Math.Max(0, levelData.CastRange) + 120;

                if (castDx * castDx + castDy * castDy >
                    castRange * castRange)
                {
                    return [Flash("技能目標距離太遠")];
                }
            }
        }
        else if (isRevive)
        {
            if (targetType != 0 ||
                targetEntityId <= 0 ||
                targetEntityId == session.CharacterId.Value)
            {
                return [Flash("請選擇同地圖的死亡角色")];
            }

            reviveTargetPresence =
                _broadcasts.FindCharacter(targetEntityId);

            if (reviveTargetPresence is null ||
                reviveTargetPresence.MapId != session.CurrentMapId)
            {
                return [Flash("復活目標不在目前地圖")];
            }

            reviveTargetCharacter = _store.GetCharacter(
                reviveTargetPresence.AccountId,
                reviveTargetPresence.CharacterId);

            if (reviveTargetCharacter is null ||
                Convert.ToInt32(reviveTargetCharacter["HP"]) > 0)
            {
                return [Flash("目標目前不需要復活")];
            }

            var castDx =
                reviveTargetPresence.X -
                session.LastX;
            var castDy =
                reviveTargetPresence.Y -
                session.LastY;
            var castRange = Math.Max(0, levelData.CastRange) + 120;

            if (castDx * castDx + castDy * castDy >
                castRange * castRange)
            {
                return [Flash("復活目標距離太遠")];
            }

            centerX = reviveTargetPresence.X;
            centerY = reviveTargetPresence.Y;
        }
        else if (isHeal)
        {
            if (targetType != 0)
                return [Flash("治療目標類型無效")];

            if (targetEntityId <= 0 ||
                targetEntityId == session.CharacterId.Value)
            {
                targetEntityId =
                    checked((int)session.CharacterId.Value);
                healTargetCharacter = character;
            }
            else
            {
                healTargetPresence =
                    _broadcasts.FindCharacter(targetEntityId);

                if (healTargetPresence is null ||
                    healTargetPresence.MapId != session.CurrentMapId)
                {
                    return [Flash("治療目標不在目前地圖")];
                }

                healTargetCharacter = _store.GetCharacter(
                    healTargetPresence.AccountId,
                    healTargetPresence.CharacterId);

                if (healTargetCharacter is null ||
                    Convert.ToInt32(healTargetCharacter["HP"]) <= 0)
                {
                    return [Flash("治療目標目前無法接受治療")];
                }

                var castDx =
                    healTargetPresence.X -
                    session.LastX;
                var castDy =
                    healTargetPresence.Y -
                    session.LastY;
                var castRange =
                    Math.Max(0, levelData.CastRange) + 120;

                if (castDx * castDx + castDy * castDy >
                    castRange * castRange)
                {
                    return [Flash("治療目標距離太遠")];
                }
            }

            if (healTargetPresence is not null)
            {
                centerX = healTargetPresence.X;
                centerY = healTargetPresence.Y;
            }
            else
            {
                centerX = session.LastX;
                centerY = session.LastY;
            }
        }
        else if (isGroupHeal)
        {
            if (targetType != 0)
                return [Flash("群體治療目標類型無效")];

            var partyMembers = _party.GetMembers(
                session.CharacterId.Value);

            if (targetEntityId <= 0 ||
                targetEntityId == session.CharacterId.Value)
            {
                targetEntityId =
                    checked((int)session.CharacterId.Value);
                groupHealCenterCharacter = character;
            }
            else
            {
                if (!partyMembers.Any(x =>
                        x.CharacterId == targetEntityId))
                {
                    return [Flash("群體治療只能指定隊伍成員")];
                }

                groupHealCenterPresence =
                    _broadcasts.FindCharacter(targetEntityId);

                if (groupHealCenterPresence is null ||
                    groupHealCenterPresence.MapId != session.CurrentMapId)
                {
                    return [Flash("群體治療中心不在目前地圖")];
                }

                groupHealCenterCharacter = _store.GetCharacter(
                    groupHealCenterPresence.AccountId,
                    groupHealCenterPresence.CharacterId);

                if (groupHealCenterCharacter is null ||
                    Convert.ToInt32(
                        groupHealCenterCharacter["HP"]) <= 0)
                {
                    return [Flash("群體治療中心目前無法接受治療")];
                }
            }

            if (groupHealCenterPresence is not null)
            {
                centerX = groupHealCenterPresence.X;
                centerY = groupHealCenterPresence.Y;
            }
            else
            {
                centerX = session.LastX;
                centerY = session.LastY;
            }
        }
        else if (isTaunt)
        {
            targetEntityId =
                checked((int)session.CharacterId.Value);
            centerX = session.LastX;
            centerY = session.LastY;
        }
        else if (isScout)
        {
            if (targetType != 0 ||
                targetEntityId <= 0)
            {
                return [Flash("請選擇要偵察的玩家")];
            }

            scoutTargetPresence =
                _broadcasts.FindCharacter(targetEntityId);

            if (scoutTargetPresence is null ||
                scoutTargetPresence.MapId != session.CurrentMapId)
            {
                return [Flash("偵察目標不在目前地圖")];
            }

            scoutTargetCharacter = _store.GetCharacter(
                scoutTargetPresence.AccountId,
                scoutTargetPresence.CharacterId);

            if (scoutTargetCharacter is null)
                return [Flash("找不到偵察目標")];

            var castDx =
                scoutTargetPresence.X -
                session.LastX;
            var castDy =
                scoutTargetPresence.Y -
                session.LastY;
            var castRange =
                Math.Max(0, levelData.CastRange) + 120;

            if (castDx * castDx + castDy * castDy >
                castRange * castRange)
            {
                return [Flash("偵察目標距離太遠")];
            }

            centerX = scoutTargetPresence.X;
            centerY = scoutTargetPresence.Y;
        }
        else
        {
            // Self-buff accepts target 0 or this DBID.
            if (targetType == 0 &&
                targetEntityId != 0 &&
                targetEntityId != session.CharacterId.Value)
            {
                return [Flash("目前僅支援自身增益")];
            }

            targetEntityId = checked((int)session.CharacterId.Value);
            centerX = session.LastX;
            centerY = session.LastY;
        }

        if (hasCreateItemSpecial)
        {
            var item = _current.FindItem(createItemId);
            if (item is null ||
                item.Category is not (
                    "general" or
                    "booster" or
                    "recovery" or
                    "horsebattery"))
            {
                return [Flash(
                    $"技能製造物品不存在 ItemID={createItemId}")];
            }

            targetEntityId =
                checked((int)session.CharacterId.Value);
            centerX = session.LastX;
            centerY = session.LastY;
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

        if (isPortableRepair)
        {
            packets.Add(new
            {
                Type = "OpenRepairWindow",
                RepairType = 10
            });
        }

        if (isTaunt)
        {
            var runtime = SyncSessionMonsters(session);
            var affected = new List<object>();
            var radius = Math.Max(
                0,
                levelData.AoeRadius);

            lock (runtime.SyncRoot)
            {
                foreach (var monster in runtime.Monsters.Values)
                {
                    if (!monster.IsAlive ||
                        monster.MapId != session.CurrentMapId)
                        continue;

                    var dx = monster.X - centerX;
                    var dy = monster.Y - centerY;
                    if (dx * dx + dy * dy >
                        radius * radius)
                        continue;

                    monster.HasTarget = true;
                    monster.TargetCharacterId =
                        session.CharacterId.Value;
                    SetAiState(
                        monster,
                        MonsterAiState.Chase);

                    affected.Add(new
                    {
                        ID = monster.EntityId
                    });

                    packets.Add(new
                    {
                        Type = "MonsterTarget",
                        EntityID = monster.EntityId,
                        HasTarget = true,
                        TargetID = session.CharacterId.Value
                    });
                }
            }

            packets[0] = new
            {
                Type = "SkillCast",
                CasterID = session.CharacterId.Value,
                CasterType = 0,
                SkillID = skillId,
                SkillLevel = skillLevel,
                TargetID = session.CharacterId.Value,
                TargetX = Convert.ToInt32(centerX),
                TargetY = Convert.ToInt32(centerY),
                AoeTargets = affected.ToArray()
            };
        }

        if (isScout &&
            scoutTargetPresence is not null &&
            scoutTargetCharacter is not null)
        {
            packets.Add(new
            {
                Type = "SystemMessageChat",
                Message =
                    $"[偵察] {scoutTargetCharacter["NAME"]} " +
                    $"Lv.{scoutTargetCharacter["Level"]} " +
                    $"HP {scoutTargetCharacter["HP"]}/{scoutTargetCharacter["MaxHP"]} " +
                    $"MP {scoutTargetCharacter["MP"]}/{scoutTargetCharacter["MaxMP"]} " +
                    $"SP {scoutTargetCharacter["SP"]}/{scoutTargetCharacter["MaxSP"]} " +
                    $"攻擊 {scoutTargetCharacter["Attack"]} " +
                    $"防禦 {scoutTargetCharacter["Defense"]}"
            });
        }

        if (isPartyReturn)
        {
            var originMapId = session.CurrentMapId;
            _broadcasts.PublishToPlayerObservers(
                session.ConnectionId,
                packets[0]);

            var members = _party.GetMembers(
                session.CharacterId.Value);

            if (members.Count == 0)
            {
                members =
                [
                    (accountId, session.CharacterId.Value)
                ];
            }

            foreach (var member in members)
            {
                if (member.CharacterId != session.CharacterId.Value)
                {
                    var presence =
                        _broadcasts.FindCharacter(member.CharacterId);

                    if (presence is null ||
                        presence.MapId != originMapId)
                        continue;
                }

                var memberCharacter = _store.GetCharacter(
                    member.AccountId,
                    member.CharacterId);

                if (memberCharacter is null)
                    continue;

                var mapId = Convert.ToInt32(
                    memberCharacter["RebornMapID"]);
                var x = Convert.ToDouble(
                    memberCharacter["RebornX"]);
                var y = Convert.ToDouble(
                    memberCharacter["RebornY"]);

                if (!_store.UpdateLocation(
                        member.AccountId,
                        member.CharacterId,
                        mapId,
                        checked((int)Math.Round(x)),
                        checked((int)Math.Round(y))))
                    continue;

                var changeMap = new
                {
                    Type = "ChangeMap",
                    MapID = mapId,
                    X = x,
                    Y = y
                };

                if (member.CharacterId ==
                    session.CharacterId.Value)
                {
                    session.CurrentMapId = mapId;
                    session.LastX = x;
                    session.LastY = y;
                    session.PendingMapSpawn = true;
                    session.LoadedMonsterMapId = 0;
                    packets.Add(changeMap);
                }
                else
                {
                    _broadcasts.PublishToCharacter(
                        member.CharacterId,
                        changeMap);
                }
            }

            var returnAssets = _store.GetCharacterAssets(
                accountId,
                session.CharacterId.Value);
            var returnStats = ApplyActiveSkillBuffs(
                CharacterStatCalculator.Calculate(
                    updated,
                    returnAssets,
                    _current),
                session,
                now);

            packets.Add(BuildPlayerStatusPacket(
                updated,
                returnStats));
            BroadcastPublicStatus(
                session,
                session.CharacterId.Value,
                updated,
                returnStats);

            session.SkillReadyAt[skillId] =
                now + TimeSpan.FromSeconds(
                    Math.Max(0, levelData.CooldownSeconds));

            return packets;
        }

        if (hasCreateItemSpecial)
        {
            var itemUid = _store.AddInventoryItem(
                accountId,
                session.CharacterId.Value,
                createItemId,
                createItemCount);

            if (itemUid <= 0)
                return [Flash("技能製造物品失敗")];

            var refreshed = _store.GetCharacterAssets(
                accountId,
                session.CharacterId.Value);

            packets.Add(new
            {
                Type = "InventorySync",
                Items = refreshed.Inventory,
                MaxSize = refreshed.InventoryMaxSize
            });
            packets.Add(Flash(
                $"獲得 {_current.FindItem(createItemId)?.Name ?? $"Item {createItemId}"} x{createItemCount}"));
        }

        if (isSelfRecovery)
        {
            var before = updated;
            var hpAmount = 0;
            var mpAmount = 0;

            switch (skillId)
            {
                case 25101: // 體力回復
                case 25106: // 大氣旋風
                    hpAmount = RollLegacySelfCure(
                        skillId,
                        skillLevel);
                    break;

                case 25108: // 生命聖印
                    hpAmount = int.MaxValue;
                    break;

                case 25109: // 魔力聖印
                    mpAmount = int.MaxValue;
                    break;
            }

            var recovered = _store.RecoverResources(
                accountId,
                session.CharacterId.Value,
                hpAmount,
                mpAmount,
                0);

            if (recovered is not null)
            {
                var hpRecovered = Math.Max(
                    0,
                    Convert.ToInt32(recovered["HP"]) -
                    Convert.ToInt32(before["HP"]));

                if (hpRecovered > 0)
                {
                    packets.Add(new
                    {
                        Type = "EntityHeal",
                        TargetID = session.CharacterId.Value,
                        TargetType = 0,
                        Amount = hpRecovered,
                        CurrentHP = recovered["HP"],
                        MaxHP = recovered["MaxHP"],
                        SkillID = skillId,
                        SkillLevel = skillLevel,
                        HealerID = session.CharacterId.Value
                    });
                }

                updated = recovered;
            }
        }

        if (isRevive &&
            reviveTargetPresence is not null &&
            reviveTargetCharacter is not null)
        {
            var hpPercent = skillId switch
            {
                27111 => 100,
                27104 or 28109 => skillLevel switch
                {
                    1 => 30,
                    2 => 50,
                    _ => 70
                },
                _ => 10
            };
            var mpPercent = skillId is 27104 or 28109
                ? hpPercent
                : 0;
            var deathExpReductionPercent = Math.Clamp(
                skillId == 27111
                    ? Math.Max(
                        100,
                        levelData.DeathExpReductionPercent)
                    : levelData.DeathExpReductionPercent,
                0,
                100);

            _pendingRevives[reviveTargetPresence.CharacterId] =
                new PendingRevive(
                    reviveTargetPresence.AccountId,
                    reviveTargetPresence.CharacterId,
                    session.CharacterId.Value,
                    session.CurrentMapId,
                    skillId,
                    skillLevel,
                    hpPercent,
                    mpPercent,
                    deathExpReductionPercent,
                    now.AddSeconds(30));

            var casterName =
                Convert.ToString(character["NAME"]) ??
                $"角色{session.CharacterId.Value}";

            _broadcasts.PublishToCharacter(
                reviveTargetPresence.CharacterId,
                new
                {
                    Type = "RevivePrompt",
                    CasterName = casterName,
                    SkillID = skillId,
                    SkillLevel = skillLevel,
                    HPPct = hpPercent,
                    MPPct = mpPercent
                });
        }

        if (isAttack &&
            (primaryMonster is not null ||
             isScreenAreaAttack ||
             isSelfCenteredAreaAttack))
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

            // Load-test characters already have ATK=1, but a skill's fixed
            // BaseDamage can still instantly kill a monster. Only clamp
            // single-target skills of explicitly marked synthetic bots;
            // real players and all existing damage formulas stay unchanged.
            if (!isAreaAttack &&
                _store.IsLoadTestBot(accountId, session.CharacterId.Value))
                damage = Math.Min(damage, 1);

            var runtime = _runtime.GetMap(
                session.CurrentMapId);
            var totalDamageDealt = 0;
            var areaKills = new List<MonsterRuntimeState>();

            lock (runtime.SyncRoot)
            {
                var radius = Math.Max(
                    0,
                    levelData.AoeRadius);

                var targetQuery = runtime.Monsters.Values
                    .Where(x =>
                        x.IsAlive &&
                        x.MapId == session.CurrentMapId &&
                        (radius <= 0
                            ? primaryMonster is not null &&
                              x.EntityId == primaryMonster.EntityId
                            : Math.Pow(x.X - centerX, 2) +
                              Math.Pow(x.Y - centerY, 2) <=
                              radius * radius));

                // Stellar Shock uses the same uncapped AOE target selection
                // as other area skills: every alive monster inside the radius
                // is included in the shared batch settlement pipeline.
                var targets = targetQuery.ToArray();

                packets[0] = new
                {
                    Type = "SkillCast",
                    CasterID = session.CharacterId.Value,
                    CasterType = 0,
                    SkillID = skillId,
                    SkillLevel = skillLevel,
                    TargetID = (
                        isScreenAreaAttack ||
                        isSelfCenteredAreaAttack)
                        ? session.CharacterId.Value
                        : primaryMonster!.EntityId,
                    TargetX = Convert.ToInt32(centerX),
                    TargetY = Convert.ToInt32(centerY),
                    AoeTargets = targets
                        .Select(x => new { ID = x.EntityId })
                        .ToArray()
                };

                var screenDamageBase =
                    100_000_000L +
                    (long)effective.Attack * 50_000L +
                    (long)effective.MagicAttack * 50_000L;

                foreach (var monster in targets)
                {
                    var appliedDamage = damage;
                    if (isScreenAreaAttack)
                    {
                        // Roll a continuous value inside the same ±10% range
                        // instead of choosing one of 21 whole-percent steps.
                        // This keeps the expected damage unchanged while
                        // avoiding artificial-looking 000/0000 endings.
                        var minDamage = Math.Clamp(
                            screenDamageBase * 90L / 100L,
                            1L,
                            (long)int.MaxValue);
                        var maxDamage = Math.Clamp(
                            screenDamageBase * 110L / 100L,
                            minDamage,
                            (long)int.MaxValue);

                        appliedDamage = (int)Random.Shared.NextInt64(
                            minDamage,
                            maxDamage + 1L);
                    }

                    var hpBeforeDamage = monster.CurrentHp;
                    monster.CurrentHp = Math.Max(
                        0,
                        monster.CurrentHp - appliedDamage);
                    totalDamageDealt += Math.Max(
                        0,
                        hpBeforeDamage - monster.CurrentHp);

                    packets.Add(new
                    {
                        Type = "EntityDamage",
                        TargetID = monster.EntityId,
                        TargetType = 1,
                        Damage = appliedDamage,
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
                        now + GetRespawnDelay(monster.MapId);
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

                    if (isAreaAttack)
                    {
                        areaKills.Add(monster);
                    }
                    else
                    {
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

                        AddActivePetKillExperience(
                            session,
                            accountId,
                            monster,
                            packets);
                    }
                }
            }

            if (isAreaAttack &&
                areaKills.Count > 0)
            {
                AddAreaKillRewards(
                    session,
                    accountId,
                    areaKills,
                    packets);

                AddAreaBatchExtras(
                    session,
                    accountId,
                    areaKills,
                    packets);
            }

            if (skillId == 25104 &&
                totalDamageDealt > 0)
            {
                // Legacy SKC25J104 + skillcommon Attack_Cure:
                // heal HP by min(actual damage dealt, per-level cure cap).
                var cureCap = skillLevel switch
                {
                    1 => 94,
                    2 => 123,
                    3 => 156,
                    4 => 193,
                    _ => 234
                };
                var lifeDrainHeal = Math.Min(
                    totalDamageDealt,
                    cureCap);

                // Kill rewards can level the character before life drain is
                // settled. Refresh the persisted HP after rewards so the heal
                // packet reports only HP restored by 25104, not HP gained by
                // level-up growth during the same attack.
                var beforeHeal =
                    _store.GetCharacter(
                        accountId,
                        session.CharacterId.Value) ??
                    updated;
                var healed = _store.RecoverResources(
                    accountId,
                    session.CharacterId.Value,
                    lifeDrainHeal,
                    0,
                    0);

                if (healed is not null)
                {
                    var recoveredHp = Math.Max(
                        0,
                        Convert.ToInt32(healed["HP"]) -
                        Convert.ToInt32(beforeHeal["HP"]));

                    if (recoveredHp > 0)
                    {
                        packets.Add(new
                        {
                            Type = "EntityHeal",
                            TargetID = session.CharacterId.Value,
                            TargetType = 0,
                            Amount = recoveredHp,
                            CurrentHP = healed["HP"],
                            MaxHP = healed["MaxHP"],
                            SkillID = skillId,
                            SkillLevel = skillLevel,
                            HealerID = session.CharacterId.Value
                        });
                    }

                    updated = healed;
                }
            }
        }

        if (isHeal && healTargetCharacter is not null)
        {
            var targetAccountId =
                healTargetPresence?.AccountId ?? accountId;
            var targetDbId =
                healTargetPresence?.CharacterId ??
                session.CharacterId.Value;

            var healed = ApplyRecoverySkill(
                targetAccountId,
                targetDbId,
                skillId,
                levelData,
                effective,
                healTargetCharacter,
                out var hpRecovered);

            if (healed is not null)
            {
                if (hpRecovered > 0)
                {
                    packets.Add(new
                    {
                        Type = "EntityHeal",
                        TargetID = targetDbId,
                        TargetType = 0,
                        Amount = hpRecovered,
                        CurrentHP = healed["HP"],
                        MaxHP = healed["MaxHP"],
                        SkillID = skillId,
                        SkillLevel = skillLevel,
                        HealerID = session.CharacterId.Value
                    });
                }

                var targetAssets = _store.GetCharacterAssets(
                    targetAccountId,
                    targetDbId);
                var targetStats = ApplyActiveSkillBuffs(
                    CharacterStatCalculator.Calculate(
                        healed,
                        targetAssets,
                        _current),
                    targetDbId,
                    now);

                if (targetDbId == session.CharacterId.Value)
                {
                    updated = healed;
                }
                else
                {
                    _broadcasts.PublishToCharacter(
                        targetDbId,
                        BuildPlayerStatusPacket(
                            healed,
                            targetStats));
                }

                BroadcastPublicStatus(
                    session,
                    targetDbId,
                    healed,
                    targetStats);

                _party.PublishStat(
                    targetAccountId,
                    targetDbId);
            }
        }

        if (isGroupHeal)
        {
            var partyMembers = _party.GetMembers(
                session.CharacterId.Value);

            if (partyMembers.Count == 0)
            {
                partyMembers =
                [
                    (accountId, session.CharacterId.Value)
                ];
            }

            var affected = new List<object>();

            foreach (var member in partyMembers)
            {
                if (member.CharacterId != session.CharacterId.Value)
                {
                    var presence =
                        _broadcasts.FindCharacter(member.CharacterId);

                    if (presence is null ||
                        presence.MapId != session.CurrentMapId)
                        continue;
                }

                var before = _store.GetCharacter(
                    member.AccountId,
                    member.CharacterId);

                if (before is null ||
                    Convert.ToInt32(before["HP"]) <= 0)
                    continue;

                var healRadius = skillId is 28104 or 28107
                    ? 400.0
                    : Math.Max(
                        0.0,
                        levelData.AoeRadius);

                if (healRadius > 0)
                {
                    var dx =
                        Convert.ToDouble(before["LocX"]) -
                        centerX;
                    var dy =
                        Convert.ToDouble(before["LocY"]) -
                        centerY;

                    if (dx * dx + dy * dy >
                        healRadius * healRadius)
                        continue;
                }

                var healed = ApplyRecoverySkill(
                    member.AccountId,
                    member.CharacterId,
                    skillId,
                    levelData,
                    effective,
                    before,
                    out var hpRecovered);

                if (healed is null)
                    continue;

                affected.Add(new
                {
                    ID = member.CharacterId
                });

                if (hpRecovered > 0)
                {
                    packets.Add(new
                    {
                        Type = "EntityHeal",
                        TargetID = member.CharacterId,
                        TargetType = 0,
                        Amount = hpRecovered,
                        CurrentHP = healed["HP"],
                        MaxHP = healed["MaxHP"],
                        SkillID = skillId,
                        SkillLevel = skillLevel,
                        HealerID = session.CharacterId.Value
                    });
                }

                var memberAssets = _store.GetCharacterAssets(
                    member.AccountId,
                    member.CharacterId);
                var memberStats = ApplyActiveSkillBuffs(
                    CharacterStatCalculator.Calculate(
                        healed,
                        memberAssets,
                        _current),
                    member.CharacterId,
                    now);

                if (member.CharacterId ==
                    session.CharacterId.Value)
                {
                    updated = healed;
                }
                else
                {
                    _broadcasts.PublishToCharacter(
                        member.CharacterId,
                        BuildPlayerStatusPacket(
                            healed,
                            memberStats));
                }

                BroadcastPublicStatus(
                    session,
                    member.CharacterId,
                    healed,
                    memberStats);

                _party.PublishStat(
                    member.AccountId,
                    member.CharacterId);
            }

            packets[0] = new
            {
                Type = "SkillCast",
                CasterID = session.CharacterId.Value,
                CasterType = 0,
                SkillID = skillId,
                SkillLevel = skillLevel,
                TargetID = targetEntityId,
                TargetX = Convert.ToInt32(centerX),
                TargetY = Convert.ToInt32(centerY),
                AoeTargets = affected.ToArray()
            };
        }

        if (isBuff && HasGenericStatBonus(levelData))
        {
            var definition = _current.FindSkill(skillId);
            var buff = CreateSkillBuff(
                skillId,
                definition?.Name ?? $"Skill {skillId}",
                skillLevel,
                levelData,
                now);

            SetCharacterBuff(
                session.CharacterId.Value,
                buff);

            if (isPartyStatBuff)
            {
                var affected = new List<object>
                {
                    new { ID = session.CharacterId.Value }
                };

                foreach (var member in _party.GetMembers(
                             session.CharacterId.Value))
                {
                    if (member.CharacterId ==
                        session.CharacterId.Value)
                        continue;

                    var presence =
                        _broadcasts.FindCharacter(
                            member.CharacterId);

                    if (presence is null ||
                        presence.MapId != session.CurrentMapId)
                        continue;

                    SetCharacterBuff(
                        member.CharacterId,
                        CloneSkillBuff(buff));

                    affected.Add(new
                    {
                        ID = member.CharacterId
                    });

                    _broadcasts.PublishToCharacter(
                        member.CharacterId,
                        BuildSyncBuffsPacket(
                            member.CharacterId,
                            now));

                    var targetCharacter = _store.GetCharacter(
                        member.AccountId,
                        member.CharacterId);

                    if (targetCharacter is not null)
                    {
                        var targetAssets =
                            _store.GetCharacterAssets(
                                member.AccountId,
                                member.CharacterId);
                        var targetStats =
                            ApplyActiveSkillBuffs(
                                CharacterStatCalculator.Calculate(
                                    targetCharacter,
                                    targetAssets,
                                    _current),
                                member.CharacterId,
                                now);

                        _broadcasts.PublishToCharacter(
                            member.CharacterId,
                            BuildPlayerStatusPacket(
                                targetCharacter,
                                targetStats));
                    }
                }

                packets[0] = new
                {
                    Type = "SkillCast",
                    CasterID = session.CharacterId.Value,
                    CasterType = 0,
                    SkillID = skillId,
                    SkillLevel = skillLevel,
                    TargetID = session.CharacterId.Value,
                    TargetX = Convert.ToInt32(centerX),
                    TargetY = Convert.ToInt32(centerY),
                    AoeTargets = affected.ToArray()
                };
            }

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
        BroadcastPublicStatus(
            session,
            session.CharacterId.Value,
            updated,
            refreshedStats);

        session.SkillReadyAt[skillId] =
            now + TimeSpan.FromSeconds(
                Math.Max(0, levelData.CooldownSeconds));

        BroadcastWorldPackets(session, packets);
        return packets;
    }

    public IReadOnlyList<object> AcceptRevive(
        ClientSession session,
        long accountId,
        int skillId,
        int skillLevel,
        DateTimeOffset now)
    {
        if (!session.CharacterId.HasValue)
            return [];

        var dbid = session.CharacterId.Value;

        if (!_pendingRevives.TryRemove(
                dbid,
                out var pending) ||
            pending.TargetAccountId != accountId ||
            pending.TargetDbId != dbid ||
            pending.SkillId != skillId ||
            pending.SkillLevel != skillLevel ||
            pending.ExpiresAt < now)
        {
            return [Flash("復活邀請已失效")];
        }

        if (pending.MapId != session.CurrentMapId ||
            !_broadcasts.IsCharacterInMap(
                pending.CasterDbId,
                pending.MapId))
        {
            return [Flash("施法者已離開目前地圖")];
        }

        var revived = _store.ReviveBySkill(
            accountId,
            dbid,
            pending.HpPercent,
            pending.MpPercent,
            pending.DeathExpReductionPercent);

        if (revived is null)
            return [Flash("目前無法接受復活")];

        var assets = _store.GetCharacterAssets(
            accountId,
            dbid);
        var revivedStats = ApplyActiveSkillBuffs(
            CharacterStatCalculator.Calculate(
                revived,
                assets,
                _current),
            dbid,
            now);
        var packets = new List<object>
        {
            new
            {
                Type = "EntityHeal",
                TargetID = dbid,
                TargetType = 0,
                Amount = revived["HP"],
                CurrentHP = revived["HP"],
                MaxHP = revived["MaxHP"],
                SkillID = pending.SkillId,
                SkillLevel = pending.SkillLevel,
                HealerID = pending.CasterDbId
            },
            new
            {
                Type = "SyncState",
                EntityID = dbid,
                State = "idle",
                Dir = "1"
            },
            BuildPlayerStatusPacket(
                revived,
                revivedStats),
            new
            {
                Type = "SystemMessageFlash",
                Message = "復活成功"
            }
        };

        BroadcastPublicStatus(
            session,
            dbid,
            revived,
            revivedStats);
        BroadcastWorldPackets(session, packets);
        return packets;
    }

    public IReadOnlyList<object> Attack(
        ClientSession session,
        long accountId,
        int targetEntityId,
        int targetType)
    {
        if (targetType != 1)
            return [Flash("攻擊目標類型無效")];

        if (!session.CharacterId.HasValue ||
            session.CurrentMapId <= 0)
            return [Flash("目前無法攻擊")];

        var runtime = SyncSessionMonsters(session);
        if (!session.Monsters.TryGetValue(
                targetEntityId,
                out var monster) ||
            monster.MapId != session.CurrentMapId)
            return [Flash("攻擊目標不存在或已離開目前地圖")];

        var character = _store.GetCharacter(
            accountId,
            session.CharacterId.Value);

        if (character is null)
            return [Flash("角色資料不存在")];

        var playerX = session.LastX;
        var playerY = session.LastY;
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
                return [Flash("目標已死亡")];

            var dx = playerX - monster.X;
            var dy = playerY - monster.Y;
            // A 120px blanket tolerance effectively gave melee weapons
            // ranged attacks. Keep only a small latency allowance; genuine
            // ranged weapons still use their own calculated AttackRange.
            const double networkRangeTolerance = 24.0;
            var allowedRange = attackRange + networkRangeTolerance;

            if ((dx * dx) + (dy * dy) >
                allowedRange * allowedRange)
                return [Flash("目標距離太遠")];

            // Distance alone is insufficient: never hit a monster through a
            // blocked tile or the corner of a wall.
            if (!_navigation.IsDirectClear(
                    session.CurrentMapId,
                    playerX, playerY,
                    monster.X, monster.Y))
                return [Flash("攻擊目標被障礙物阻擋")];

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
                monster.TargetPetUid = 0;
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
                    DateTimeOffset.UtcNow + GetRespawnDelay(monster.MapId);
                monster.HasTarget = false;
                monster.TargetCharacterId = 0;
                monster.TargetPetUid = 0;
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

                AddActivePetKillExperience(
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

        var rateSnapshot = _globalRates.Snapshot();
        var dropMultiplier = rateSnapshot.EffectiveDrop(DateTimeOffset.UtcNow);
        var drops = _drops.RollDrops(
            monster.EnemyId,
            monster.EntityId,
            monster.KillSequence,
            lucky,
            dropMultiplier);

        var offsetIndex = 0;
        foreach (var drop in drops)
        {
            var offsetX = ((offsetIndex % 5) - 2) * 12.0;
            var offsetY = (((offsetIndex / 5) % 5) - 2) * 10.0;
            offsetIndex++;

            if (!_dropPlacement.TryResolve(
                    monster.MapId,
                    monster.X, monster.Y,
                    monster.X + offsetX, monster.Y + offsetY,
                    session.LastX, session.LastY,
                    out var placement))
            {
                // An extremely damaged/misconfigured map can have no
                // accessible foot-walkable tile at either anchor. Do not
                // silently lose rolled rewards. The recovered enemy drop
                // catalog produces only tabs 0 (item) and 1 (equipment).
                var rewardUid = drop.TabType switch
                {
                    1 => _store.AddEquipment(
                        accountId, dbid, drop.ItemId,
                        damage: _current.FindItem(drop.ItemId)?.Durability ?? 0),
                    0 => _store.AddInventoryItem(
                        accountId, dbid, drop.ItemId, 1),
                    _ => 0L
                };

                _log.LogWarning(
                    "[GROUND_DROP_NO_SAFE_TILE] Map={MapId} Monster={MonsterId} Item={ItemId} RewardedDirectly={Rewarded}",
                    monster.MapId, monster.EntityId, drop.ItemId,
                    rewardUid > 0);

                if (rewardUid > 0)
                {
                    var recoveryAssets = _store.GetCharacterAssets(
                        accountId, dbid);
                    if (drop.TabType == 1)
                    {
                        packets.Add(new
                        {
                            Type = "EquipSync",
                            Equips = recoveryAssets.Equips,
                            MaxSize = recoveryAssets.EquipMaxSize
                        });
                    }
                    else
                    {
                        packets.Add(new
                        {
                            Type = "InventorySync",
                            Items = recoveryAssets.Inventory,
                            MaxSize = recoveryAssets.InventoryMaxSize
                        });
                    }

                    packets.Add(new
                    {
                        Type = "SystemMessageFlash",
                        Message = $"地面沒有可掉落的位置，已將 {drop.Name} 放入背包"
                    });
                }
                continue;
            }

            if (!placement.CollisionVerified &&
                _dropPlacement.ShouldReportMissingMap(monster.MapId))
            {
                _log.LogWarning(
                    "[GROUND_DROP_MAP_MISSING] Map={MapId} Monster={MonsterId} Blocking.dat unavailable; legacy placement retained",
                    monster.MapId, monster.EntityId);
            }

            var ground = _store.CreateGroundItem(
                dbid,
                drop.TabType,
                drop.ItemId,
                1,
                monster.MapId,
                placement.X,
                placement.Y,
                damage: _current.FindItem(drop.ItemId)?.Durability ?? 0,
                protectSeconds: _drops.OwnerProtectionSeconds,
                lifetimeSeconds: 60);

            if (ground is null)
                continue;

            session.GroundItems[ground.EntityId] = ground;

            object BuildDropSpawn()
                => BuildGroundDropSpawnPacket(
                    ground,
                    drop.Name);

            // The killer receives the SpawnEntity in its direct combat
            // response. Other players receive it only when the persisted
            // ground entity is inside their world AOI. The factory computes
            // protection/lifetime from the current clock for late entrants.
            var changes =
                _broadcasts.UpsertWorldEntity(
                    new WorldAoiEntity(
                        ground.EntityId,
                        3,
                        ground.MapId,
                        ground.X,
                        ground.Y,
                        BuildDropSpawn));

            foreach (var change in changes)
            {
                if (change.ObserverConnectionId ==
                    session.ConnectionId)
                    continue;

                if (_broadcasts.IsWorldAoiVisible(
                        change.ObserverConnectionId,
                        ground.EntityId) !=
                    change.Appeared)
                    continue;

                _broadcasts.PublishToConnection(
                    change.ObserverConnectionId,
                    change.Appeared
                        ? change.Entity.SpawnPacketFactory()
                        : new
                        {
                            Type = "DespawnEntity",
                            EntityID = ground.EntityId
                        });
            }

            packets.Add(
                BuildDropSpawn());

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

    private object BuildGroundDropSpawnPacket(
        GroundItemRuntime ground,
        string? displayName = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new
        {
            Type = "SpawnEntity",
            EntityID = ground.EntityId,
            EntityType = 3,
            Name = string.IsNullOrWhiteSpace(displayName)
                ? _current.FindItem(ground.ItemIndex)?.Name ??
                  $"Item {ground.ItemIndex}"
                : displayName,
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
        };
    }

    private void AddAreaKillRewards(
        ClientSession session,
        long accountId,
        IReadOnlyList<MonsterRuntimeState> monsters,
        List<object> packets)
    {
        if (!session.CharacterId.HasValue ||
            monsters.Count == 0)
            return;

        var dbid = session.CharacterId.Value;
        var before = _store.GetCharacter(accountId, dbid);
        if (before is null)
            return;

        var oldLevel = Math.Clamp(
            Convert.ToInt32(before["Level"]),
            1,
            255);

        long totalExp = 0;
        long totalMoney = 0;

        foreach (var monster in monsters)
        {
            var gainedExp = _battle.ExperienceForKill(
                oldLevel,
                monster.Level);

            var expMultiplierPercent =
                _store.GetTimedEffectValue(
                    accountId,
                    dbid,
                    "exp_multiplier_percent",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            if (expMultiplierPercent > 0)
            {
                gainedExp = (int)Math.Clamp(
                    (long)gainedExp *
                    expMultiplierPercent / 100L,
                    0L,
                    int.MaxValue);
            }

            totalExp += Math.Clamp(
                (long)gainedExp *
                CharacterExperienceMultiplier,
                0L,
                int.MaxValue);
            totalMoney += Math.Max(1, monster.Level);
        }

        var rateSnapshot = _globalRates.Snapshot();
        var now = DateTimeOffset.UtcNow;
        var expToAdd = GlobalRateMath.ApplyReward(
            totalExp,
            rateSnapshot.EffectiveExperience(now));
        var moneyToAdd = GlobalRateMath.ApplyReward(
            totalMoney,
            rateSnapshot.EffectiveMoney(now));

        var rewarded = _store.AddRewards(
            accountId,
            dbid,
            expToAdd,
            moneyToAdd);

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

        packets.Add(new
        {
            Type = "ItemPickupFeedback",
            Message =
                $"獲得經驗值 {expToAdd:N0}（擊殺 {monsters.Count:N0} 隻）",
            PlaySound = false
        });

        var assets = _store.GetCharacterAssets(
            accountId,
            dbid);
        var stats = ApplyActiveSkillBuffs(
            CharacterStatCalculator.Calculate(
                rewarded,
                assets,
                _current),
            dbid,
            DateTimeOffset.UtcNow);

        packets.Add(BuildPlayerStatusPacket(
            rewarded,
            stats));
        BroadcastPublicStatus(
            session,
            dbid,
            rewarded,
            stats);

        packets.Add(new
        {
            Type = "SyncMoney",
            Money = rewarded["Money"]
        });
    }

    private void AddAreaBatchExtras(
        ClientSession session,
        long accountId,
        IReadOnlyList<MonsterRuntimeState> monsters,
        List<object> packets)
    {
        if (!session.CharacterId.HasValue ||
            monsters.Count == 0)
            return;

        var dbid = session.CharacterId.Value;

        // Drops keep their original per-monster probability/kill-sequence
        // semantics, but all resulting spawn packets are returned together
        // with this one skill request instead of causing repeated status syncs.
        foreach (var monster in monsters)
        {
            AddMonsterDrops(
                session,
                accountId,
                monster,
                packets);
        }

        // Quest progression still receives every kill so count-based quests do
        // not lose credit. Only quest-specific packets are emitted here.
        foreach (var monster in monsters)
        {
            packets.AddRange(
                _questProgress.OnMonsterKilled(
                    accountId,
                    dbid,
                    monster.Name));
        }

        // Aggregate all monster EXP into one mecha mutation/sync.
        var character = _store.GetCharacter(
            accountId,
            dbid);

        if (character is not null)
        {
            var horseUid = Convert.ToInt64(
                character["SummonedHorseUID"]);

            if (horseUid > 0)
            {
                var assetsBefore = _store.GetCharacterAssets(
                    accountId,
                    dbid);
                var beforeHorse = assetsBefore.Horses
                    .FirstOrDefault(x =>
                        Convert.ToInt64(x["HorseUID"]) == horseUid);

                var totalHorseExp = (int)Math.Clamp(
                    monsters.Sum(x =>
                        (long)Math.Max(1, x.Level * 100)),
                    0L,
                    int.MaxValue);

                var horse = _store.AddHorseExperience(
                    accountId,
                    dbid,
                    horseUid,
                    totalHorseExp);

                if (horse is not null)
                {
                    packets.Add(new
                    {
                        Type = "SyncMechaEnergy",
                        HorseUID = horseUid,
                        EnergyLeft = horse["EnergyLeft"],
                        Exp = horse["Exp"],
                        MaxExp = horse["MaxExp"],
                        Level = horse["Level"]
                    });

                    if (beforeHorse is not null &&
                        Convert.ToInt32(horse["Level"]) >
                        Convert.ToInt32(beforeHorse["Level"]))
                    {
                        packets.Add(new
                        {
                            Type = "SystemMessageFlash",
                            Message = $"機甲升級！Lv.{horse["Level"]}"
                        });
                    }
                }
            }
        }

        // Aggregate pet EXP across the whole AOE. Each active pet mutates once
        // and the final asset state is synchronized once.
        var activePets = _store.GetCharacterAssets(
                accountId,
                dbid)
            .Pets
            .Where(x => Convert.ToInt32(x["State"]) == 1)
            .ToArray();

        var totalPetExp = (int)Math.Clamp(
            monsters.Sum(x =>
                (long)Math.Max(1, x.Level * 10)),
            0L,
            int.MaxValue);

        var petStateChanged = false;

        foreach (var beforePet in activePets)
        {
            var petUid = Convert.ToInt64(beforePet["UID"]);
            var oldLevel = Convert.ToInt32(beforePet["Level"]);

            var pet = _store.AddPetExperience(
                accountId,
                dbid,
                petUid,
                totalPetExp);

            if (pet is null)
                continue;

            var evolution = _petEvolution.Advance(
                accountId,
                dbid,
                petUid,
                pet);
            pet = evolution.Pet;
            petStateChanged = true;

            if (Convert.ToInt32(pet["Level"]) > oldLevel)
            {
                packets.Add(new
                {
                    Type = "SystemMessageFlash",
                    Message = $"寵物升級！Lv.{pet["Level"]}"
                });
            }

            if (!evolution.Changed)
                continue;

            packets.Add(new
            {
                Type = "SystemMessageFlash",
                Message =
                    $"寵物進化！Evol {evolution.FromEvolId} → {evolution.ToEvolId}"
            });

            if (Convert.ToInt32(pet["State"]) != 1)
                continue;

            var entityId = 4_000_000L + petUid;
            packets.Add(new
            {
                Type = "DespawnEntity",
                EntityID = entityId
            });

            var position = session.PetPositions.TryGetValue(
                    petUid,
                    out var runtimePosition)
                ? runtimePosition
                : (
                    X: session.LastX + 40,
                    Y: session.LastY + 20);

            session.PetPositions[petUid] = position;
            packets.Add(BuildPetSpawnPacket(
                dbid,
                pet,
                position.X,
                position.Y));
        }

        if (petStateChanged)
        {
            var refreshedAssets = _store.GetCharacterAssets(
                accountId,
                dbid);

            packets.Add(new
            {
                Type = "PetSync",
                Pets = refreshedAssets.Pets,
                MaxSize = refreshedAssets.PetMaxSize
            });
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

        var expMultiplierPercent =
            _store.GetTimedEffectValue(
                accountId,
                dbid,
                "exp_multiplier_percent",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        if (expMultiplierPercent > 0)
        {
            gainedExp = (int)Math.Clamp(
                (long)gainedExp *
                expMultiplierPercent / 100L,
                0L,
                int.MaxValue);
        }

        gainedExp = (int)Math.Clamp(
            (long)gainedExp *
            CharacterExperienceMultiplier,
            0L,
            int.MaxValue);

        var rateSnapshot = _globalRates.Snapshot();
        var now = DateTimeOffset.UtcNow;
        gainedExp = GlobalRateMath.ApplyReward(
            gainedExp,
            rateSnapshot.EffectiveExperience(now));

        var gainedMoney = GlobalRateMath.ApplyReward(
            Math.Max(1, monster.Level),
            rateSnapshot.EffectiveMoney(now));

        var rewarded = _store.AddRewards(
            accountId,
            dbid,
            gainedExp,
            gainedMoney);

        if (rewarded is null)
            return;

        packets.Add(new
        {
            Type = "ItemPickupFeedback",
            Message = $"獲得經驗值 {gainedExp:N0}",
            PlaySound = false
        });

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
            packets.Add(new
            {
                Type = "PlayEffect",
                EntityID = dbid,
                EffectName = "LevelUp"
            });
        }

        var assets = _store.GetCharacterAssets(
            accountId,
            dbid);

        var rewardStats = ApplyActiveSkillBuffs(
            CharacterStatCalculator.Calculate(
                rewarded,
                assets,
                _current),
            dbid,
            DateTimeOffset.UtcNow);

        packets.Add(
            BuildPlayerStatusPacket(
                rewarded,
                rewardStats));
        BroadcastPublicStatus(
            session,
            dbid,
            rewarded,
            rewardStats);

        packets.Add(new
        {
            Type = "SyncMoney",
            Money = rewarded["Money"]
        });

        packets.AddRange(
            _questProgress.OnMonsterKilled(
                accountId,
                dbid,
                monster.Name));
    }

    private void AddActivePetKillExperience(
        ClientSession session,
        long accountId,
        MonsterRuntimeState monster,
        List<object> packets)
    {
        if (!session.CharacterId.HasValue)
            return;

        var dbid = session.CharacterId.Value;
        var activePets = _store.GetCharacterAssets(
                accountId,
                dbid)
            .Pets
            .Where(x => Convert.ToInt32(x["State"]) == 1)
            .ToArray();

        foreach (var before in activePets)
        {
            var petUid = Convert.ToInt64(before["UID"]);
            var oldLevel = Convert.ToInt32(before["Level"]);

            // Keep pet progression independent from character EXP tables for
            // now. The existing pet schema starts at MaxExp=100, and this
            // grants useful progress without coupling pet levels to player
            // growth rules.
            var gainedExp = Math.Max(1, monster.Level * 10);
            var pet = _store.AddPetExperience(
                accountId,
                dbid,
                petUid,
                gainedExp);

            if (pet is null)
                continue;

            var evolution = _petEvolution.Advance(
                accountId,
                dbid,
                petUid,
                pet);
            pet = evolution.Pet;

            var refreshedAssets = _store.GetCharacterAssets(
                accountId,
                dbid);

            packets.Add(new
            {
                Type = "PetSync",
                Pets = refreshedAssets.Pets,
                MaxSize = refreshedAssets.PetMaxSize
            });

            if (Convert.ToInt32(pet["Level"]) > oldLevel)
            {
                packets.Add(new
                {
                    Type = "SystemMessageFlash",
                    Message = $"寵物升級！Lv.{pet["Level"]}"
                });
            }

            if (!evolution.Changed)
                continue;

            packets.Add(new
            {
                Type = "SystemMessageFlash",
                Message =
                    $"寵物進化！Evol {evolution.FromEvolId} → {evolution.ToEvolId}"
            });

            if (Convert.ToInt32(pet["State"]) != 1)
                continue;

            var entityId = 4_000_000L + petUid;
            packets.Add(new
            {
                Type = "DespawnEntity",
                EntityID = entityId
            });

            var position = session.PetPositions.TryGetValue(
                    petUid,
                    out var runtimePosition)
                ? runtimePosition
                : (
                    X: session.LastX + 40,
                    Y: session.LastY + 20);

            session.PetPositions[petUid] = position;
            packets.Add(BuildPetSpawnPacket(
                dbid,
                pet,
                position.X,
                position.Y));
        }
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
            packets.Add(new
            {
                Type = "PlayEffect",
                EntityID = session.CharacterId.Value,
                EffectName = "MS_Levelup"
            });
        }
    }

    private static TimeSpan GetRespawnDelay(int mapId)
        => mapId == NiciaPlateauMapId
            ? NiciaPlateauRespawnDelay
            : RespawnDelay;

    public IReadOnlyList<object> TickBuffs(
        ClientSession session,
        DateTimeOffset now)
    {
        if (!session.CharacterId.HasValue)
            return [];

        var dbid = session.CharacterId.Value;
        var removed = PurgeExpiredBuffs(
            dbid,
            now);

        if (!removed)
            return [];

        var packets = new List<object>
        {
            BuildSyncBuffsPacket(
                dbid,
                now)
        };

        if (session.AccountId.HasValue)
        {
            var accountId = session.AccountId.Value;
            var character = _store.GetCharacter(
                accountId,
                dbid);

            if (character is not null)
            {
                var assets = _store.GetCharacterAssets(
                    accountId,
                    dbid);
                var stats = ApplyActiveSkillBuffs(
                    CharacterStatCalculator.Calculate(
                        character,
                        assets,
                        _current),
                    dbid,
                    now);

                packets.Add(
                    BuildPlayerStatusPacket(
                        character,
                        stats));
                BroadcastPublicStatus(
                    session,
                    dbid,
                    character,
                    stats);
            }
        }

        return packets;
    }

    private bool PurgeExpiredBuffs(
        long characterId,
        DateTimeOffset now)
    {
        if (characterId <= 0 ||
            !_skillBuffsByCharacter.TryGetValue(
                characterId,
                out var buffs))
            return false;

        var expired = buffs
            .Where(x => x.Value.ExpiresAt <= now)
            .Select(x => x.Key)
            .ToArray();

        foreach (var skillId in expired)
            buffs.TryRemove(skillId, out _);

        if (buffs.IsEmpty)
            _skillBuffsByCharacter.TryRemove(
                characterId,
                out _);

        return expired.Length > 0;
    }

    private static int RollLegacySelfCure(
        int skillId,
        int skillLevel)
    {
        var levelIndex = Math.Clamp(
            skillLevel,
            1,
            5) - 1;

        var baseValue = skillId switch
        {
            25101 => new[]
            {
                13, 21, 33, 49, 70
            }[levelIndex],
            25106 => new[]
            {
                156, 234, 329, 441, 569
            }[levelIndex],
            _ => 0
        };

        if (baseValue <= 0)
            return 0;

        // Legacy curecommon.txt:
        // effect + effect * (varyDelta - random(varyDelta*2)) / 100
        // with varyDelta=10. Equivalent integer range: -10% .. +9%.
        var deltaPercent = Random.Shared.Next(
            -10,
            10);

        return Math.Max(
            1,
            baseValue +
            baseValue * deltaPercent / 100);
    }

    private static bool TryParseAddItemSpecial(
        string specialSkill,
        out int itemId,
        out int count)
    {
        itemId = 0;
        count = 0;

        if (string.IsNullOrWhiteSpace(specialSkill))
            return false;

        var parts = specialSkill.Split(
            ':',
            StringSplitOptions.TrimEntries |
            StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 3 &&
               parts[0].Equals(
                   "AddItem",
                   StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(parts[1], out itemId) &&
               itemId > 0 &&
               int.TryParse(parts[2], out count) &&
               count > 0;
    }

    private Dictionary<string, object>? ApplyRecoverySkill(
        long accountId,
        long dbid,
        int skillId,
        CurrentSkillLevelDefinition levelData,
        EffectiveCharacterStats casterStats,
        Dictionary<string, object> before,
        out int hpRecovered)
    {
        var genericAmount = Math.Max(
            1,
            levelData.BaseDamage +
            casterStats.MagicAttack *
            Math.Max(1, levelData.DamageMultiplier) / 100);

        var hpAmount = 0;
        var mpAmount = 0;
        var spAmount = 0;

        var legacyPrimary = RollLegacyCureValue(
            skillId,
            levelData.Level,
            secondary: false);
        var legacySecondary = RollLegacyCureValue(
            skillId,
            levelData.Level,
            secondary: true);

        switch (skillId)
        {
            case 28101: // 治療術 -> HP
            case 28104: // 天使之光 -> party HP
                hpAmount = legacyPrimary > 0
                    ? legacyPrimary
                    : genericAmount;
                break;

            case 28102: // 心靈轉換 -> 意志(SP)
                spAmount = legacyPrimary > 0
                    ? legacyPrimary
                    : genericAmount;
                break;

            case 28103: // 靈魂轉換 -> 魔力(MP)
                mpAmount = legacyPrimary > 0
                    ? legacyPrimary
                    : genericAmount;
                break;

            case 28107: // 治療風暴 -> HP + MP
                hpAmount = legacyPrimary > 0
                    ? legacyPrimary
                    : genericAmount;
                mpAmount = legacySecondary > 0
                    ? legacySecondary
                    : genericAmount;
                break;

            case 28108: // 祝福之光 -> 全 HP
            case 28110: // 神聖大十字 -> 全 HP
                hpAmount = int.MaxValue;
                break;

            case 28111: // 心靈大十字 -> 全意志(SP)
                spAmount = int.MaxValue;
                break;

            default:
                hpAmount = genericAmount;
                break;
        }

        var healed = _store.RecoverResources(
            accountId,
            dbid,
            hpAmount,
            mpAmount,
            spAmount);

        hpRecovered = healed is null
            ? 0
            : Math.Max(
                0,
                Convert.ToInt32(healed["HP"]) -
                Convert.ToInt32(before["HP"]));

        return healed;
    }

    private static int RollLegacyCureValue(
        int skillId,
        int skillLevel,
        bool secondary)
    {
        var index = Math.Clamp(
            skillLevel,
            1,
            5) - 1;

        int[]? values = skillId switch
        {
            28101 => [11, 15, 21, 28, 36],
            28102 => [38, 69, 113, 169, 238],
            28103 => [17, 21, 31, 41, 53],
            28104 => [28, 45, 68, 95, 128],
            28107 when secondary =>
                [90, 135, 190, 255, 330],
            28107 =>
                [170, 260, 370, 500, 650],
            _ => null
        };

        if (values is null)
            return 0;

        var baseValue = values[index];

        // Legacy curecommon.txt applies an independent -10%..+9% roll.
        var deltaPercent = Random.Shared.Next(
            -10,
            10);

        return Math.Max(
            1,
            baseValue +
            baseValue * deltaPercent / 100);
    }

    private static bool HasGenericStatBonus(
        CurrentSkillLevelDefinition level)
        => level.MaxHpBonus != 0 ||
           level.MaxMpBonus != 0 ||
           level.MaxSpBonus != 0 ||
           level.AtkBonus != 0 ||
           level.DefBonus != 0 ||
           level.MAtkBonus != 0 ||
           level.MDefBonus != 0 ||
           level.HitBonus != 0 ||
           level.FleeBonus != 0 ||
           level.LukBonus != 0 ||
           level.AspdBonus != 0 ||
           level.MoveSpeedBonus != 0 ||
           level.CritRateBonus != 0 ||
           level.CritDamageBonus != 0 ||
           level.MaxHpBonusPercent != 0 ||
           level.MaxMpBonusPercent != 0 ||
           level.MaxSpBonusPercent != 0 ||
           level.AtkBonusPercent != 0 ||
           level.DefBonusPercent != 0 ||
           level.MAtkBonusPercent != 0 ||
           level.MDefBonusPercent != 0 ||
           level.HitBonusPercent != 0 ||
           level.FleeBonusPercent != 0 ||
           level.LukBonusPercent != 0 ||
           level.AspdBonusPercent != 0 ||
           level.MoveSpeedBonusPercent != 0 ||
           level.CritRateBonusPercent != 0 ||
           level.CritDamageBonusPercent != 0;

    private EffectiveCharacterStats ApplyActiveSkillBuffs(
        EffectiveCharacterStats stats,
        ClientSession session,
        DateTimeOffset now)
        => ApplyActiveSkillBuffs(
            stats,
            session.CharacterId ?? 0,
            now);

    private EffectiveCharacterStats ApplyActiveSkillBuffs(
        EffectiveCharacterStats stats,
        long characterId,
        DateTimeOffset now)
    {
        var buffs = GetActiveBuffs(
            characterId,
            now);

        return new EffectiveCharacterStats(
            ApplyBuffValue(
                stats.MaxHP,
                buffs.Sum(x => x.MaxHpBonus),
                buffs.Sum(x => x.MaxHpBonusPercent),
                1),
            ApplyBuffValue(
                stats.MaxMP,
                buffs.Sum(x => x.MaxMpBonus),
                buffs.Sum(x => x.MaxMpBonusPercent),
                0),
            ApplyBuffValue(
                stats.MaxSP,
                buffs.Sum(x => x.MaxSpBonus),
                buffs.Sum(x => x.MaxSpBonusPercent),
                0),
            ApplyBuffValue(
                stats.Attack,
                buffs.Sum(x => x.AtkBonus),
                buffs.Sum(x => x.AtkBonusPercent),
                0),
            ApplyBuffValue(
                stats.Defense,
                buffs.Sum(x => x.DefBonus),
                buffs.Sum(x => x.DefBonusPercent),
                0),
            ApplyBuffValue(
                stats.MagicAttack,
                buffs.Sum(x => x.MAtkBonus),
                buffs.Sum(x => x.MAtkBonusPercent),
                0),
            ApplyBuffValue(
                stats.MagicDefense,
                buffs.Sum(x => x.MDefBonus),
                buffs.Sum(x => x.MDefBonusPercent),
                0),
            ApplyBuffValue(
                stats.Hit,
                buffs.Sum(x => x.HitBonus),
                buffs.Sum(x => x.HitBonusPercent),
                0),
            ApplyBuffValue(
                stats.Dodge,
                buffs.Sum(x => x.FleeBonus),
                buffs.Sum(x => x.FleeBonusPercent),
                0),
            ApplyBuffValue(
                stats.Lucky,
                buffs.Sum(x => x.LukBonus),
                buffs.Sum(x => x.LukBonusPercent),
                0),
            ApplyBuffValue(
                stats.AttackSpeed,
                buffs.Sum(x => x.AspdBonus),
                buffs.Sum(x => x.AspdBonusPercent),
                0),
            stats.AttackRange,
            ApplyBuffValue(
                stats.MoveSpeed,
                buffs.Sum(x => x.MoveSpeedBonus),
                buffs.Sum(x => x.MoveSpeedBonusPercent),
                1),
            ApplyBuffValue(
                stats.CritRate,
                buffs.Sum(x => x.CritRateBonus),
                buffs.Sum(x => x.CritRateBonusPercent),
                0),
            ApplyBuffValue(
                stats.CritDamage,
                buffs.Sum(x => x.CritDamageBonus),
                buffs.Sum(x => x.CritDamageBonusPercent),
                0));
    }

    private static int ApplyBuffValue(
        int baseValue,
        int flatBonus,
        int percentBonus,
        int minimum)
    {
        var value =
            (long)baseValue +
            flatBonus +
            (long)baseValue * percentBonus / 100L;

        return (int)Math.Clamp(
            value,
            minimum,
            (long)int.MaxValue);
    }

    private SkillBuffRuntime[] GetActiveBuffs(
        long characterId,
        DateTimeOffset now)
    {
        if (characterId <= 0)
            return [];

        PurgeExpiredBuffs(
            characterId,
            now);

        if (!_skillBuffsByCharacter.TryGetValue(
                characterId,
                out var buffs))
            return [];

        return buffs.Values
            .Where(x => x.ExpiresAt > now)
            .OrderBy(x => x.SkillId)
            .ToArray();
    }

    private void SetCharacterBuff(
        long characterId,
        SkillBuffRuntime buff)
    {
        if (characterId <= 0)
            return;

        var buffs = _skillBuffsByCharacter.GetOrAdd(
            characterId,
            _ => new ConcurrentDictionary<int, SkillBuffRuntime>());

        buffs[buff.SkillId] = buff;
    }

    private static SkillBuffRuntime CreateSkillBuff(
        int skillId,
        string name,
        int skillLevel,
        CurrentSkillLevelDefinition levelData,
        DateTimeOffset now)
    {
        var duration = Math.Max(
            1,
            levelData.DurationSeconds);

        return new SkillBuffRuntime
        {
            SkillId = skillId,
            Name = name,
            Level = skillLevel,
            DurationSeconds = duration,
            ExpiresAt =
                now + TimeSpan.FromSeconds(duration),
            MaxHpBonus = levelData.MaxHpBonus,
            MaxMpBonus = levelData.MaxMpBonus,
            MaxSpBonus = levelData.MaxSpBonus,
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
            CritDamageBonus = levelData.CritDamageBonus,
            MaxHpBonusPercent = levelData.MaxHpBonusPercent,
            MaxMpBonusPercent = levelData.MaxMpBonusPercent,
            MaxSpBonusPercent = levelData.MaxSpBonusPercent,
            AtkBonusPercent = levelData.AtkBonusPercent,
            DefBonusPercent = levelData.DefBonusPercent,
            MAtkBonusPercent = levelData.MAtkBonusPercent,
            MDefBonusPercent = levelData.MDefBonusPercent,
            HitBonusPercent = levelData.HitBonusPercent,
            FleeBonusPercent = levelData.FleeBonusPercent,
            LukBonusPercent = levelData.LukBonusPercent,
            AspdBonusPercent = levelData.AspdBonusPercent,
            MoveSpeedBonusPercent = levelData.MoveSpeedBonusPercent,
            CritRateBonusPercent = levelData.CritRateBonusPercent,
            CritDamageBonusPercent = levelData.CritDamageBonusPercent
        };
    }

    private static SkillBuffRuntime CloneSkillBuff(
        SkillBuffRuntime source)
        => new()
        {
            SkillId = source.SkillId,
            Name = source.Name,
            Level = source.Level,
            DurationSeconds = source.DurationSeconds,
            ExpiresAt = source.ExpiresAt,
            MaxHpBonus = source.MaxHpBonus,
            MaxMpBonus = source.MaxMpBonus,
            MaxSpBonus = source.MaxSpBonus,
            AtkBonus = source.AtkBonus,
            DefBonus = source.DefBonus,
            MAtkBonus = source.MAtkBonus,
            MDefBonus = source.MDefBonus,
            HitBonus = source.HitBonus,
            FleeBonus = source.FleeBonus,
            LukBonus = source.LukBonus,
            AspdBonus = source.AspdBonus,
            MoveSpeedBonus = source.MoveSpeedBonus,
            CritRateBonus = source.CritRateBonus,
            CritDamageBonus = source.CritDamageBonus,
            MaxHpBonusPercent = source.MaxHpBonusPercent,
            MaxMpBonusPercent = source.MaxMpBonusPercent,
            MaxSpBonusPercent = source.MaxSpBonusPercent,
            AtkBonusPercent = source.AtkBonusPercent,
            DefBonusPercent = source.DefBonusPercent,
            MAtkBonusPercent = source.MAtkBonusPercent,
            MDefBonusPercent = source.MDefBonusPercent,
            HitBonusPercent = source.HitBonusPercent,
            FleeBonusPercent = source.FleeBonusPercent,
            LukBonusPercent = source.LukBonusPercent,
            AspdBonusPercent = source.AspdBonusPercent,
            MoveSpeedBonusPercent = source.MoveSpeedBonusPercent,
            CritRateBonusPercent = source.CritRateBonusPercent,
            CritDamageBonusPercent = source.CritDamageBonusPercent
        };

    public IReadOnlyList<object> GetCombinedBuffEntries(
        long accountId,
        long characterId,
        DateTimeOffset now)
    {
        var buffs = GetActiveSkillBuffEntries(
                characterId,
                now)
            .ToList();

        if (accountId <= 0 || characterId <= 0)
            return buffs;

        var nowUtcMs = now.ToUnixTimeMilliseconds();
        var timedEffects = _store.GetActiveTimedEffects(
            accountId,
            characterId,
            nowUtcMs);

        foreach (var sourceGroup in timedEffects.GroupBy(effect => new
                 {
                     effect.SourceType,
                     effect.SourceValue,
                     effect.StartedUtcMs,
                     effect.ExpiresUtcMs,
                     effect.DurationSeconds
                 }))
        {
            var remainingSeconds = Math.Max(
                0.0,
                (sourceGroup.Key.ExpiresUtcMs - nowUtcMs) / 1000.0);
            var totalSeconds = sourceGroup.Key.DurationSeconds > 0
                ? sourceGroup.Key.DurationSeconds
                : remainingSeconds;

            // Item-backed bundles (for example one Booster carrying HP, ATK
            // and LUK rows) are one logical HUD buff and should render once.
            if (sourceGroup.Key.SourceType == 1 &&
                sourceGroup.Key.SourceValue > 0)
            {
                var exp = sourceGroup.FirstOrDefault(effect =>
                    effect.EffectKey == "exp_multiplier_percent");

                buffs.Add(new
                {
                    BuffName = exp is not null
                        ? $"ExperienceBoost_{exp.EffectValue}"
                        : $"ItemEffect_{sourceGroup.Key.SourceValue}",
                    SourceType = sourceGroup.Key.SourceType,
                    Value = sourceGroup.Key.SourceValue,
                    RemainingTime = remainingSeconds,
                    TotalTime = totalSeconds
                });
                continue;
            }

            foreach (var effect in sourceGroup)
            {
                var buffName = effect.EffectKey switch
                {
                    "exp_multiplier_percent" =>
                        $"ExperienceBoost_{effect.EffectValue}",
                    _ => $"TimedEffect_{effect.EffectKey}"
                };

                buffs.Add(new
                {
                    BuffName = buffName,
                    SourceType = effect.SourceType,
                    Value = effect.SourceValue > 0
                        ? effect.SourceValue
                        : effect.EffectValue,
                    RemainingTime = remainingSeconds,
                    TotalTime = totalSeconds
                });
            }
        }

        var character = _store.GetCharacter(
            accountId,
            characterId);
        if (character is null ||
            Convert.ToInt32(
                character.GetValueOrDefault(
                    "TransformType",
                    0)) <= 0)
        {
            return buffs;
        }

        var expiresUtcMs = Convert.ToInt64(
            character.GetValueOrDefault(
                "TransformExpiresUtcMs",
                0L));
        var startedUtcMs = Convert.ToInt64(
            character.GetValueOrDefault(
                "TransformStartedUtcMs",
                0L));

        if (expiresUtcMs <= nowUtcMs)
            return buffs;

        var transformId = Convert.ToString(
            character.GetValueOrDefault(
                "TransformID",
                "")) ?? "";
        var sourceItemId = Convert.ToInt32(
            character.GetValueOrDefault(
                "TransformSourceItemID",
                2802));

        buffs.Add(new
        {
            BuffName = $"Transform_{transformId}",
            SourceType = 3,
            Value = sourceItemId > 0
                ? sourceItemId
                : 2802,
            RemainingTime = Math.Max(
                0.0,
                (expiresUtcMs - nowUtcMs) / 1000.0),
            TotalTime = Math.Max(
                1.0,
                (expiresUtcMs - startedUtcMs) / 1000.0)
        });

        return buffs;
    }

    private object BuildSyncBuffsPacket(
        ClientSession session,
        DateTimeOffset now)
        => BuildSyncBuffsPacket(
            session.CharacterId ?? 0,
            now);

    public IReadOnlyList<object> GetActiveSkillBuffEntries(
        long characterId,
        DateTimeOffset now)
        => GetActiveBuffs(
                characterId,
                now)
            .Select(x => (object)new
            {
                BuffName = x.Name,
                SourceType = 2,
                Value = x.SkillId,
                RemainingTime = Math.Max(
                    0.0,
                    (x.ExpiresAt - now).TotalSeconds),
                TotalTime = x.DurationSeconds
            })
            .ToArray();

    private object BuildSyncBuffsPacket(
        long characterId,
        DateTimeOffset now)
    {
        var accountId =
            _broadcasts.FindCharacter(characterId)?.AccountId ?? 0;

        return new
        {
            Type = "SyncBuffs",
            Buffs = accountId > 0
                ? GetCombinedBuffEntries(
                    accountId,
                    characterId,
                    now)
                : GetActiveSkillBuffEntries(
                    characterId,
                    now)
        };
    }

    private static object BuildPlayerStatusPacket(
        Dictionary<string, object> character,
        EffectiveCharacterStats s)
        => new
        {
            Type = "PlayerStatusSync",
            Level = character["Level"],
            Money = character["Money"],
            ExpAcc = character["ExpAcc"],
            ExpDeath = character["ExpDeath"],
            MaxExp = character["MaxExp"],
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

    private object BuildPetSpawnPacket(
        long ownerId,
        Dictionary<string, object> pet,
        double x,
        double y)
    {
        var petId = Convert.ToInt32(pet["PETID"]);
        var evolId = Convert.ToInt32(pet["EvolID"]);
        var evolution = _current.FindPetEvolution(
            petId,
            evolId);

        return new
        {
            Type = "SpawnEntity",
            EntityID =
                4_000_000L + Convert.ToInt64(pet["UID"]),
            EntityType = 4,
            Name = pet["Name"],
            LocX = x,
            LocY = y,
            ExtraData = new
            {
                OwnerID = ownerId,
                Sprite = evolution?.Outlook ?? "EN010A",
                Level = pet["Level"],
                MaxHP = pet["MaxHP"],
                CurrentHP = pet["HP"],
                ColorIndex = evolution?.Color ?? 0,
                AttackRange = evolution?.AttackRange ?? 80
            }
        };
    }

    private void BroadcastPublicStatus(
        ClientSession session,
        long entityId,
        Dictionary<string, object> character,
        EffectiveCharacterStats s)
    {
        if (session.CurrentMapId <= 0 ||
            entityId <= 0)
            return;

        var presence =
            _broadcasts.FindCharacter(
                entityId);
        if (presence is null ||
            presence.MapId !=
                session.CurrentMapId)
            return;

        _broadcasts.PublishToPlayerObservers(
            presence.ConnectionId,
            new
            {
                Type = "SyncStatus",
                EntityID = entityId,
                Level = character["Level"],
                HP = Math.Min(
                    Convert.ToInt32(character["HP"]),
                    s.MaxHP),
                MP = Math.Min(
                    Convert.ToInt32(character["MP"]),
                    s.MaxMP),
                SP = Math.Min(
                    Convert.ToInt32(character["SP"]),
                    s.MaxSP),
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
            });
    }

    private static object Flash(string message)
        => new
        {
            Type = "SystemMessageFlash",
            Message = message
        };

    private void TryWander(
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

        // Give each wander a different travel distance so idle monsters do
        // not all move in equal-sized hops. Keep it deterministic per
        // monster/sequence so tests and replays remain stable.
        var stepSeed = unchecked(
            monster.EntityId * 214013 +
            monster.WanderSequence * 2531011 +
            97);
        var stepRatio =
            ((uint)stepSeed % 10_001) / 10_000.0;
        var wanderStep =
            WanderMinStep +
            (WanderMaxStep - WanderMinStep) * stepRatio;

        var desiredX = monster.X + Math.Cos(angle) * wanderStep;
        var desiredY = monster.Y + Math.Sin(angle) * wanderStep;

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
                var step = Math.Min(wanderStep, centerDistance);
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

        // Choose another deterministic heading when the preferred wander
        // segment crosses blocked terrain. Never teleport through a wall.
        if (!_navigation.IsDirectClear(
                monster.MapId, monster.X, monster.Y, desiredX, desiredY))
        {
            var found = false;
            for (var attempt = 1; attempt <= 8; attempt++)
            {
                var alternateAngle = angle + attempt * Math.PI / 4.0;
                var candidateX = monster.X + Math.Cos(alternateAngle) * wanderStep;
                var candidateY = monster.Y + Math.Sin(alternateAngle) * wanderStep;
                var centerDx = candidateX - monster.WanderCenterX;
                var centerDy = candidateY - monster.WanderCenterY;
                if (centerDx * centerDx + centerDy * centerDy >
                    WanderRadius * WanderRadius)
                    continue;
                if (!_navigation.IsDirectClear(
                        monster.MapId, monster.X, monster.Y, candidateX, candidateY))
                    continue;
                desiredX = candidateX;
                desiredY = candidateY;
                found = true;
                break;
            }
            if (!found)
            {
                // A narrow corridor may not have room for a 95-175px
                // wander segment, but still allows a short safe step.
                for (var attempt = 0; attempt < 8 && !found; attempt++)
                {
                    var alternateAngle = angle + attempt * Math.PI / 4.0;
                    var candidateX = monster.X +
                        Math.Cos(alternateAngle) * MoveStep;
                    var candidateY = monster.Y +
                        Math.Sin(alternateAngle) * MoveStep;
                    var centerDx = candidateX - monster.WanderCenterX;
                    var centerDy = candidateY - monster.WanderCenterY;
                    if (centerDx * centerDx + centerDy * centerDy >
                        WanderRadius * WanderRadius ||
                        !_navigation.IsDirectClear(
                            monster.MapId, monster.X, monster.Y,
                            candidateX, candidateY))
                        continue;

                    desiredX = candidateX;
                    desiredY = candidateY;
                    found = true;
                }
            }
            if (!found)
            {
                monster.NextWanderAt = now + NextWanderDelay(
                    monster.EntityId, monster.WanderSequence);
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

        var oldX = monster.X;
        var oldY = monster.Y;
        monster.X = desiredX;
        monster.Y = desiredY;
        SetAiState(monster, MonsterAiState.Wander);
        monster.NextWanderAt = now + NextWanderDelay(
            monster.EntityId,
            monster.WanderSequence);

        TryAddMonsterMovePacket(
            monster,
            packets,
            now,
            oldX,
            oldY,
            desiredX,
            desiredY,
            MoveTweenSeconds,
            moveDx,
            moveDy);
    }

    private bool TryAddMonsterMovePacket(
        MonsterRuntimeState monster,
        List<object> packets,
        DateTimeOffset now,
        double oldX,
        double oldY,
        double newX,
        double newY,
        double speedTime,
        double moveDx,
        double moveDy,
        double? targetX = null,
        double? targetY = null,
        double? targetDistance = null)
    {
        const double epsilon = 0.01;

        var actualDx = newX - oldX;
        var actualDy = newY - oldY;
        var actualDistanceSquared =
            actualDx * actualDx +
            actualDy * actualDy;

        if (actualDistanceSquared <= epsilon * epsilon)
        {
            if (monster.LastStallLogAt == DateTimeOffset.MinValue ||
                now - monster.LastStallLogAt >= TimeSpan.FromSeconds(2))
            {
                monster.LastStallLogAt = now;
                _log.LogDebug(
                    "[MONSTER_AI_ZERO_MOVE] EntityID={EntityID} State={State} HasTarget={HasTarget} TargetCharacterID={TargetCharacterID} TargetPetUID={TargetPetUID} Old=({OldX:F2},{OldY:F2}) New=({NewX:F2},{NewY:F2}) LastPacket=({LastPacketX},{LastPacketY}) Target=({TargetX},{TargetY}) Distance={Distance}",
                    monster.EntityId,
                    monster.AiState,
                    monster.HasTarget,
                    monster.TargetCharacterId,
                    monster.TargetPetUid,
                    oldX,
                    oldY,
                    newX,
                    newY,
                    monster.LastMovePacketX,
                    monster.LastMovePacketY,
                    targetX,
                    targetY,
                    targetDistance);
            }

            return false;
        }

        monster.LastMovePacketX = newX;
        monster.LastMovePacketY = newY;
        monster.LastActionAt = now;

        packets.Add(BuildMovePacket(
            monster,
            newX,
            newY,
            speedTime,
            moveDx,
            moveDy));

        return true;
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

    private (double X, double Y) ResolveRespawnPosition(
        MonsterRuntimeState monster)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var angle =
                Random.Shared.NextDouble() *
                Math.PI * 2.0;
            var radius =
                24.0 +
                Random.Shared.NextDouble() *
                (RespawnRandomRadius - 24.0);

            var x =
                monster.SpawnX +
                Math.Cos(angle) * radius;
            var y =
                monster.SpawnY +
                Math.Sin(angle) * radius;

            if (_blocking.GetFlags(
                    monster.MapId,
                    x,
                    y) != 0)
            {
                continue;
            }

            if (_navigation.IsDirectClear(
                    monster.MapId,
                    monster.SpawnX,
                    monster.SpawnY,
                    x,
                    y))
            {
                return (x, y);
            }
        }

        return (monster.SpawnX, monster.SpawnY);
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
