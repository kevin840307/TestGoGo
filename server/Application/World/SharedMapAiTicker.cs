using System.Collections.Concurrent;
using System.Diagnostics;
using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Combat;
using SGO.Server.Application.Messaging;
using SGO.Server.Domain.Sessions;

namespace SGO.Server.Application.World;

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

    public SharedMapAiTicker(
        CombatService combat,
        WorldRuntimeService world,
        ICharacterStore characters,
        ServerBroadcastHub broadcasts,
        AccountSessionRegistry accounts,
        ILogger<SharedMapAiTicker> log)
    {
        _combat = combat;
        _world = world;
        _characters = characters;
        _broadcasts = broadcasts;
        _accounts = accounts;
        _log = log;
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
        var activeByMap = _sessions.Values
            .Where(s => s.WorldReady &&
                        s.CurrentMapId > 0 &&
                        s.AccountId.HasValue &&
                        s.CharacterId.HasValue)
            .GroupBy(s => s.CurrentMapId)
            .ToArray();

        var tickedMaps = 0;
        foreach (var group in activeByMap)
        {
            // Empty city/maps need no player HP queries and no monster AI.
            // GM-spawned monsters still cause normal processing.
            var map = _world.GetMap(group.Key);
            lock (map.SyncRoot)
            {
                if (map.Monsters.Count == 0)
                {
                    tickedMaps++;
                    continue;
                }
            }

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

            if (players.Count == 0)
                continue;

            players.Sort((a, b) => a.CharacterId.CompareTo(b.CharacterId));

            // Respawn once per map, before distributing newly alive monsters.
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
                            _broadcasts.PublishToConnection(
                                respawnOwner.ConnectionId, packet);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex,
                            "[MAP_AI_RESPAWN_FAILED] Map={MapID}", group.Key);
                    }
                }
            }

            var assignments = players.ToDictionary(
                p => p.Session.ConnectionId,
                _ => new List<int>());
            lock (map.SyncRoot)
            {
                foreach (var monster in map.Monsters.Values)
                {
                    var owner = monster.HasTarget
                        ? players.FirstOrDefault(p =>
                            p.CharacterId == monster.TargetCharacterId)
                        : null;

                    if (owner is null && monster.HasTarget)
                    {
                        // Stale or disconnected target: let an active player
                        // clear the target so wandering can resume.
                        owner = players[0];
                    }

                    if (owner is null)
                    {
                        // Assign an idle monster once, using the nearest
                        // player or their currently tracked pet position.
                        // This prevents N players from each scanning/updating
                        // all idle monsters on one map.
                        var bestDistance = double.MaxValue;
                        foreach (var player in players)
                        {
                            if (!player.IsAlive && player.PetPositions.Length == 0)
                                continue;
                            var dx = player.X - monster.X;
                            var dy = player.Y - monster.Y;
                            var distance = player.IsAlive
                                ? dx * dx + dy * dy
                                : double.MaxValue;
                            foreach (var pet in player.PetPositions)
                            {
                                var petDx = pet.X - monster.X;
                                var petDy = pet.Y - monster.Y;
                                distance = Math.Min(
                                    distance, petDx * petDx + petDy * petDy);
                            }

                            if (distance < bestDistance)
                            {
                                bestDistance = distance;
                                owner = player;
                            }
                        }
                    }

                    // When everyone is dead, keep one owner for idle
                    // wandering and stale-target cleanup only.
                    owner ??= players[0];
                    assignments[owner.Session.ConnectionId].Add(
                        monster.EntityId);
                }
            }

            foreach (var player in players)
            {
                var assigned = assignments[player.Session.ConnectionId];
                if (assigned.Count == 0)
                    continue;

                var session = player.Session;
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

                    try
                    {
                        var packets = _combat.TickAi(
                            session,
                            session.AccountId.Value,
                            session.LastX,
                            session.LastY,
                            now,
                            assigned);

                        // The combat service broadcasts to map peers; enqueue
                        // the return packets only to the actor's own socket.
                        foreach (var packet in packets)
                            _broadcasts.PublishToConnection(
                                session.ConnectionId, packet);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex,
                            "[MAP_AI_TICK_FAILED] Map={MapID} Character={CharacterID}",
                            group.Key, session.CharacterId);
                    }
                }
            }

            tickedMaps++;
        }

        return tickedMaps;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var start = Stopwatch.GetTimestamp();
                TickOnce(DateTimeOffset.UtcNow);
                var elapsed = Stopwatch.GetElapsedTime(start);
                if (elapsed > Interval)
                    _log.LogWarning(
                        "[MAP_AI_SLOW_TICK] ElapsedMs={ElapsedMs:F1}",
                        elapsed.TotalMilliseconds);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
