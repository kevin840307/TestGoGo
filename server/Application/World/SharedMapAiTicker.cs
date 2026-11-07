using System.Collections.Concurrent;
using System.Diagnostics;
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
    private readonly ServerBroadcastHub _broadcasts;
    private readonly AccountSessionRegistry _accounts;
    private readonly ILogger<SharedMapAiTicker> _log;

    public SharedMapAiTicker(
        CombatService combat,
        ServerBroadcastHub broadcasts,
        AccountSessionRegistry accounts,
        ILogger<SharedMapAiTicker> log)
    {
        _combat = combat;
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
            _sessions.TryRemove(
                new KeyValuePair<Guid, ClientSession>(
                    session.ConnectionId, session));
            session.SharedAiManaged = false;
        }
    }

    /// <summary>
    /// Runs exactly one pass over each active map. A monster is still updated
    /// by its target owner's session, preserving pet and buff behavior.
    /// The map's pass is serialized here rather than driven by N sockets.
    /// </summary>
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
            var didRespawn = false;
            var mapProcessed = false;
            foreach (var session in group.OrderBy(s => s.CharacterId!.Value))
            {
                lock (session)
                {
                    if (!session.WorldReady ||
                        session.CurrentMapId != group.Key ||
                        !session.AccountId.HasValue ||
                        !session.CharacterId.HasValue ||
                        !_accounts.IsCurrent(
                            session.AccountId.Value, session.ConnectionId))
                        continue;

                    try
                    {
                        // Only the first active session handles this map's
                        // respawns. CombatService broadcasts the map events.
                        var packets = new List<object>();
                        if (!didRespawn)
                        {
                            packets.AddRange(_combat.TickRespawns(session, now));
                            didRespawn = true;
                        }

                        packets.AddRange(_combat.TickAi(
                            session,
                            session.AccountId.Value,
                            session.LastX,
                            session.LastY,
                            now));

                        // TickAi already broadcasts events to other sessions
                        // on this map; return the same events to the owner.
                        foreach (var packet in packets)
                            _broadcasts.PublishToConnection(
                                session.ConnectionId, packet);

                        mapProcessed = true;
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex,
                            "[MAP_AI_TICK_FAILED] Map={MapID} Character={CharacterID}",
                            group.Key, session.CharacterId);
                    }
                }
            }

            if (mapProcessed)
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
