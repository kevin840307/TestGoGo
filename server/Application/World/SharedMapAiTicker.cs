using System.Collections.Concurrent;
using System.Diagnostics;
using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Combat;
using SGO.Server.Application.Diagnostics;
using SGO.Server.Application.Messaging;
using SGO.Server.Domain.Sessions;

namespace SGO.Server.Application.World;

public sealed record SharedMapAiTickDiagnostics(
    DateTimeOffset TickAt,
    double ElapsedMs,
    int ActiveSessions,
    int ActiveMaps,
    int TickedMaps,
    int MonsterCount,
    int AssignedMonsterCount);

public sealed record SharedMapAiTickProfile(
    double TotalMs,
    double SessionSnapshotMs,
    double PlayerSnapshotMs,
    double RespawnMs,
    double AssignmentMs,
    double SessionLockWaitMs,
    double TickAiMs,
    double DirectPublishMs,
    double OtherMs);

/// <summary>
/// A single 250ms scheduler for all active maps. Monster AI and respawns are
/// executed once for each map tick, not once by every websocket's timer.
/// Per-player HP/MP, companion and UI ticks remain on their connection.
/// </summary>
public sealed class SharedMapAiTicker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);
    private readonly ConcurrentDictionary<Guid, ClientSession> _sessions = new();
    private readonly CombatService _combat;
    private readonly WorldRuntimeService _world;
    private readonly ICharacterStore _characters;
    private readonly ServerBroadcastHub _broadcasts;
    private readonly AccountSessionRegistry _accounts;
    private readonly ILogger<SharedMapAiTicker> _log;
    private readonly ServerDiagnostics? _diagnostics;
    private readonly ConcurrentDictionary<int, DateTimeOffset> _lastMapPressureWarning = new();
    private SharedMapAiTickDiagnostics _lastDiagnostics =
        new(
            DateTimeOffset.MinValue,
            0,
            0,
            0,
            0,
            0,
            0);

    private SharedMapAiTickProfile _lastProfile =
        new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    public SharedMapAiTickDiagnostics LastDiagnostics
        => _lastDiagnostics;

    public SharedMapAiTickProfile LastProfile
        => _lastProfile;

    public SharedMapAiTicker(
        CombatService combat,
        WorldRuntimeService world,
        ICharacterStore characters,
        ServerBroadcastHub broadcasts,
        AccountSessionRegistry accounts,
        ILogger<SharedMapAiTicker> log,
        ServerDiagnostics? diagnostics = null)
    {
        _combat = combat;
        _world = world;
        _characters = characters;
        _broadcasts = broadcasts;
        _accounts = accounts;
        _log = log;
        _diagnostics = diagnostics;
    }

    public void Register(ClientSession session)
    {
        if (session.ConnectionId == Guid.Empty)
            throw new ArgumentException("ConnectionId is required.", nameof(session));

        lock (session)
        {
            session.SharedAiManaged = true;
            _sessions[session.ConnectionId] = session;
        }
    }

    public void Unregister(ClientSession session)
    {
        lock (session)
        {
            _sessions.TryRemove(session.ConnectionId, out _);
            session.SharedAiManaged = false;
        }
    }

    /// <summary>
    /// Runs exactly one pass over each active map. A monster is still updated
    /// by its target owner's session, preserving pet and buff behavior.
    /// The map's pass is serialized here rather than driven by N sockets.
    /// </summary>
    private sealed record PlayerContext(
        ClientSession Session,
        long CharacterId,
        bool IsAlive,
        double X,
        double Y,
        (double X, double Y)[] PetPositions);

    public int TickOnce(DateTimeOffset now)
    {
        var started = Stopwatch.GetTimestamp();
        var sessionSnapshotStarted = Stopwatch.GetTimestamp();
        var activeByMap = _sessions.Values
            .Where(s => s.WorldReady &&
                        s.CurrentMapId > 0 &&
                        s.AccountId.HasValue &&
                        s.CharacterId.HasValue)
            .GroupBy(s => s.CurrentMapId)
            .ToArray();
        var sessionSnapshotMs =
            Stopwatch.GetElapsedTime(sessionSnapshotStarted)
                .TotalMilliseconds;

        var playerSnapshotMs = 0.0;
        var respawnMs = 0.0;
        var assignmentMs = 0.0;
        var sessionLockWaitMs = 0.0;
        var tickAiMs = 0.0;
        var directPublishMs = 0.0;

        var activeSessionCount =
            activeByMap.Sum(group => group.Count());
        var tickedMaps = 0;
        var monsterCount = 0;
        var assignedMonsterCount = 0;

        foreach (var group in activeByMap)
        {
            // Empty city/maps need no player HP queries and no monster AI.
            // GM-spawned monsters still cause normal processing.
            var map = _world.GetMap(group.Key);
            var mapMonsterCount = 0;
            lock (map.SyncRoot)
            {
                mapMonsterCount = map.Monsters.Count;
                monsterCount += mapMonsterCount;
                if (map.Monsters.Count == 0)
                {
                    tickedMaps++;
                    continue;
                }
            }

            var playerSnapshotStarted = Stopwatch.GetTimestamp();
            var players = new List<PlayerContext>();
            foreach (var session in group)
            {
                lock (session)
                {
                    if (!session.SharedAiManaged ||
                        !session.WorldReady ||
                        session.CurrentMapId != group.Key ||
                        !session.AccountId.HasValue ||
                        !session.CharacterId.HasValue ||
                        !_accounts.IsCurrent(
                            session.AccountId.Value, session.ConnectionId))
                        continue;

                    var character = _characters.GetCharacter(
                        session.AccountId.Value,
                        session.CharacterId.Value);
                    var isAlive = character is not null &&
                        Convert.ToInt32(character["HP"]) > 0;
                    players.Add(new PlayerContext(
                        session, session.CharacterId.Value,
                        isAlive, session.LastX, session.LastY,
                        session.PetPositions.Values.ToArray()));
                }
            }
            playerSnapshotMs +=
                Stopwatch.GetElapsedTime(playerSnapshotStarted)
                    .TotalMilliseconds;

            if (players.Count == 0)
                continue;

            if ((players.Count >= 50 || mapMonsterCount >= 500) &&
                (!_lastMapPressureWarning.TryGetValue(group.Key, out var lastPressure) ||
                 now - lastPressure >= TimeSpan.FromSeconds(30)))
            {
                _lastMapPressureWarning[group.Key] = now;
                _diagnostics?.MapPressureWarning();
                _diagnostics?.Record(
                    "Warning",
                    "MapPressure",
                    $"Players={players.Count} Monsters={mapMonsterCount}",
                    mapId: group.Key);
                _log.LogWarning(
                    "[MAP_PRESSURE] Map={MapID} Players={Players} Monsters={Monsters}",
                    group.Key,
                    players.Count,
                    mapMonsterCount);
            }

            players.Sort((a, b) => a.CharacterId.CompareTo(b.CharacterId));

            // Respawn once per map, before distributing newly alive monsters.
            var respawnStarted = Stopwatch.GetTimestamp();
            var respawnOwner = players[0].Session;
            lock (respawnOwner)
            {
                if (respawnOwner.WorldReady &&
                    respawnOwner.CurrentMapId == group.Key)
                {
                    try
                    {
                        foreach (var packet in _combat.TickRespawns(
                                     respawnOwner, now))
                        {
                            if (!ShouldDeliverActorPacket(
                                    respawnOwner,
                                    packet))
                                continue;

                            _broadcasts.PublishToConnection(
                                respawnOwner.ConnectionId,
                                packet);
                        }
                    }
                    catch (Exception ex)
                    {
                        _diagnostics?.WorldTickFailure();
                        _diagnostics?.Record(
                            "Error",
                            "MapAiRespawn",
                            ex.GetType().Name,
                            mapId: group.Key);
                        _log.LogError(ex,
                            "[MAP_AI_RESPAWN_FAILED] Map={MapID}", group.Key);
                    }
                }
            }
            respawnMs +=
                Stopwatch.GetElapsedTime(respawnStarted)
                    .TotalMilliseconds;

            var assignmentStarted = Stopwatch.GetTimestamp();
            var assignments = players.ToDictionary(
                p => p.Session.ConnectionId,
                _ => new List<int>());

            // Snapshot once per map tick. Previously every target lookup and
            // every idle monster performed a full scan of all map players:
            // O(monsters * (players + pets)). The direct target map is O(1)
            // and the exact nearest-owner tree is typically O(log(players +
            // pets)) per monster. Preserve the CharacterId sort for ties.
            var targetOwners = new Dictionary<long, PlayerContext>(
                players.Count);
            var ownerPoints = new List<MapAiOwnerPoint>(
                players.Count);
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                targetOwners.TryAdd(player.CharacterId, player);
                if (player.IsAlive)
                    ownerPoints.Add(
                        new MapAiOwnerPoint(i, player.X, player.Y));
                foreach (var pet in player.PetPositions)
                    ownerPoints.Add(
                        new MapAiOwnerPoint(i, pet.X, pet.Y));
            }

            var nearestOwner = new MapAiOwnerSpatialIndex(
                ownerPoints);

            lock (map.SyncRoot)
            {
                foreach (var monster in map.Monsters.Values)
                {
                    PlayerContext? owner = null;
                    if (monster.HasTarget)
                    {
                        if (!targetOwners.TryGetValue(
                                monster.TargetCharacterId, out owner))
                        {
                            // Stale/disconnected target: preserve the old
                            // deterministic fallback so combat can clear it.
                            owner = players[0];
                        }
                    }
                    else
                    {
                        var ownerIndex = nearestOwner.FindNearestOwnerIndex(
                            monster.X, monster.Y);
                        if (ownerIndex >= 0)
                            owner = players[ownerIndex];
                    }

                    // When everyone is dead and has no pets, keep one driver
                    // for idle wandering and stale-target cleanup only.
                    owner ??= players[0];
                    assignments[owner.Session.ConnectionId].Add(
                        monster.EntityId);
                    assignedMonsterCount++;
                }
            }
            assignmentMs +=
                Stopwatch.GetElapsedTime(assignmentStarted)
                    .TotalMilliseconds;

            foreach (var player in players)
            {
                var assigned = assignments[player.Session.ConnectionId];
                if (assigned.Count == 0)
                    continue;

                var session = player.Session;
                var sessionLockStarted = Stopwatch.GetTimestamp();
                lock (session)
                {
                    sessionLockWaitMs +=
                        Stopwatch.GetElapsedTime(sessionLockStarted)
                            .TotalMilliseconds;

                    if (!session.SharedAiManaged ||
                        !session.WorldReady ||
                        session.CurrentMapId != group.Key ||
                        !session.AccountId.HasValue ||
                        !session.CharacterId.HasValue ||
                        !_accounts.IsCurrent(
                            session.AccountId.Value, session.ConnectionId))
                        continue;

                    try
                    {
                        var tickAiStarted = Stopwatch.GetTimestamp();
                        var packets = _combat.TickAi(
                            session,
                            session.AccountId.Value,
                            session.LastX,
                            session.LastY,
                            now,
                            assigned);
                        tickAiMs +=
                            Stopwatch.GetElapsedTime(tickAiStarted)
                                .TotalMilliseconds;

                        var directPublishStarted = Stopwatch.GetTimestamp();
                        // The combat service broadcasts to AOI peers. The
                        // driver socket must obey the same monster known-set;
                        // otherwise a far-away scheduler owner still receives
                        // every idle monster Move/State packet.
                        foreach (var packet in packets)
                        {
                            if (!ShouldDeliverActorPacket(
                                    session,
                                    packet))
                                continue;

                            _broadcasts.PublishToConnection(
                                session.ConnectionId,
                                packet);
                        }
                        directPublishMs +=
                            Stopwatch.GetElapsedTime(directPublishStarted)
                                .TotalMilliseconds;
                    }
                    catch (Exception ex)
                    {
                        _diagnostics?.WorldTickFailure();
                        _diagnostics?.Record(
                            "Error",
                            "MapAiTick",
                            ex.GetType().Name,
                            session.ConnectionId,
                            session.AccountId,
                            session.CharacterId,
                            group.Key);
                        _log.LogError(ex,
                            "[MAP_AI_TICK_FAILED] Map={MapID} Character={CharacterID}",
                            group.Key, session.CharacterId);
                    }
                }
            }

            tickedMaps++;
        }

        var totalMs =
            Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var accountedMs =
            sessionSnapshotMs +
            playerSnapshotMs +
            respawnMs +
            assignmentMs +
            sessionLockWaitMs +
            tickAiMs +
            directPublishMs;
        _lastProfile = new SharedMapAiTickProfile(
            totalMs,
            sessionSnapshotMs,
            playerSnapshotMs,
            respawnMs,
            assignmentMs,
            sessionLockWaitMs,
            tickAiMs,
            directPublishMs,
            Math.Max(0.0, totalMs - accountedMs));

        _lastDiagnostics = new SharedMapAiTickDiagnostics(
            now,
            totalMs,
            activeSessionCount,
            activeByMap.Length,
            tickedMaps,
            monsterCount,
            assignedMonsterCount);

        return tickedMaps;
    }

    private bool ShouldDeliverActorPacket(
        ClientSession session,
        object packet)
    {
        var type = packet.GetType()
            .GetProperty("Type")
            ?.GetValue(packet)
            ?.ToString();

        // Death is terminal and the assigned combat owner is normally the
        // target/attacker. BroadcastWorldPackets removes the entity from the
        // AOI immediately after publishing death, so visibility can no longer
        // be queried here. One terminal packet to the owner is still bounded.
        if (type == "EntityDeath")
            return true;

        if (!CombatService.TryResolveMonsterAoiEntityId(
                session,
                packet,
                type,
                out var monsterEntityId))
            return true;

        return _broadcasts.IsWorldAoiVisible(
            session.ConnectionId,
            monsterEntityId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
#if DEBUG
        DateTimeOffset? lastTickStarted = null;
        var tickGapLogCount = 0;
#endif
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var tickStartedAt = DateTimeOffset.UtcNow;
#if DEBUG
                // Distinguish a true scheduler stall from a normal idle/
                // wandering interval between a monster's MoveEntity packets.
                if (lastTickStarted.HasValue)
                {
                    var gap = tickStartedAt - lastTickStarted.Value;
                    if (gap > TimeSpan.FromMilliseconds(500) &&
                        tickGapLogCount++ < 25)
                    {
                        _log.LogInformation(
                            "[MAP_AI_TICK_GAP] GapMs={GapMs:F1} ActiveSessions={ActiveSessions}",
                            gap.TotalMilliseconds, _sessions.Count);
                    }
                }
                lastTickStarted = tickStartedAt;
#endif
                var start = Stopwatch.GetTimestamp();
                TickOnce(tickStartedAt);
                var elapsed = Stopwatch.GetElapsedTime(start);
                if (elapsed > Interval)
                {
                    _diagnostics?.MapAiSlowTick();
                    _diagnostics?.Record(
                        "Warning",
                        "MapAiSlowTick",
                        $"ActiveSessions={_lastDiagnostics.ActiveSessions} ActiveMaps={_lastDiagnostics.ActiveMaps} Monsters={_lastDiagnostics.MonsterCount}",
                        elapsedMilliseconds: elapsed.TotalMilliseconds);
                    var profile = _lastProfile;
                    var profileMessage =
                        $"Total={profile.TotalMs:F1}ms " +
                        $"SessionSnapshot={profile.SessionSnapshotMs:F1}ms " +
                        $"PlayerSnapshot={profile.PlayerSnapshotMs:F1}ms " +
                        $"Respawn={profile.RespawnMs:F1}ms " +
                        $"Assignment={profile.AssignmentMs:F1}ms " +
                        $"SessionLockWait={profile.SessionLockWaitMs:F1}ms " +
                        $"TickAi={profile.TickAiMs:F1}ms " +
                        $"DirectPublish={profile.DirectPublishMs:F1}ms " +
                        $"Other={profile.OtherMs:F1}ms " +
                        $"ActiveSessions={_lastDiagnostics.ActiveSessions} " +
                        $"ActiveMaps={_lastDiagnostics.ActiveMaps} " +
                        $"Monsters={_lastDiagnostics.MonsterCount} " +
                        $"Assigned={_lastDiagnostics.AssignedMonsterCount}";
                    _diagnostics?.Record(
                        "Warning",
                        "MapAiProfile",
                        profileMessage,
                        elapsedMilliseconds: elapsed.TotalMilliseconds);
                    _log.LogWarning(
                        "[MAP_AI_SLOW_TICK] ElapsedMs={ElapsedMs:F1}",
                        elapsed.TotalMilliseconds);
                    _log.LogWarning(
                        "[MAP_AI_PROFILE] TotalMs={TotalMs:F1} SessionSnapshotMs={SessionSnapshotMs:F1} PlayerSnapshotMs={PlayerSnapshotMs:F1} RespawnMs={RespawnMs:F1} AssignmentMs={AssignmentMs:F1} SessionLockWaitMs={SessionLockWaitMs:F1} TickAiMs={TickAiMs:F1} DirectPublishMs={DirectPublishMs:F1} OtherMs={OtherMs:F1} ActiveSessions={ActiveSessions} ActiveMaps={ActiveMaps} Monsters={Monsters} Assigned={Assigned}",
                        profile.TotalMs,
                        profile.SessionSnapshotMs,
                        profile.PlayerSnapshotMs,
                        profile.RespawnMs,
                        profile.AssignmentMs,
                        profile.SessionLockWaitMs,
                        profile.TickAiMs,
                        profile.DirectPublishMs,
                        profile.OtherMs,
                        _lastDiagnostics.ActiveSessions,
                        _lastDiagnostics.ActiveMaps,
                        _lastDiagnostics.MonsterCount,
                        _lastDiagnostics.AssignedMonsterCount);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
