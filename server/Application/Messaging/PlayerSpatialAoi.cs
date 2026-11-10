namespace SGO.Server.Application.Messaging;

/// <summary>
/// Server-owned, symmetric player visibility. A map is split into spatial
/// cells so moving one player does not scan every logged-in character.
/// This is *delivery* visibility only: combat, movement validation, party,
/// trade, chat and persistent world state remain authoritative elsewhere.
/// </summary>
public sealed class PlayerSpatialAoi
{
    public const int CellSize = 1024;
    public const double EnterRadius = 1800;
    public const double ExitRadius = 2200;
    // Symmetric peer visibility uses the larger of both camera envelopes.
    // Legacy connections continue to use 1800/2200.
    public const double MaxViewRadius = 5000;
    public const double MaxViewExitRadius = MaxViewRadius + (ExitRadius - EnterRadius);

    public sealed record Change(
        Guid ObserverConnectionId,
        OnlinePlayerPresence Subject,
        bool Appeared);

    private sealed class Entry(OnlinePlayerPresence player, double viewRadius)
    {
        public OnlinePlayerPresence Player = player;
        public double ViewRadius = viewRadius;
        public readonly HashSet<Guid> Visible = new();
        public (int MapId, int CellX, int CellY) Cell = CellOf(player);
    }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _players = new();
    private readonly Dictionary<(int MapId, int CellX, int CellY),
        HashSet<Guid>> _cells = new();
    // Movement is a hot path. Do not scan the 5000px camera envelope on
    // every map just because the feature exists: track the largest active
    // viewport exit radius separately for each map.
    private readonly Dictionary<int, double>
        _maxViewExitRadiusByMap = new();

    public int ReadyPlayerCount
    {
        get { lock (_gate) return _players.Count; }
    }

    public IReadOnlyList<Change> Upsert(
        OnlinePlayerPresence player, double requestedViewRadius = EnterRadius)
    {
        lock (_gate)
        {
            var changes = new List<Change>();
            var viewRadius = Math.Clamp(
                double.IsFinite(requestedViewRadius) ? requestedViewRadius : EnterRadius,
                EnterRadius, MaxViewRadius);
            if (player.ConnectionId == Guid.Empty ||
                player.AccountId <= 0 || player.CharacterId <= 0 ||
                player.MapId <= 0)
                return changes;

            if (_players.TryGetValue(player.ConnectionId, out var entry) &&
                (entry.Player.MapId != player.MapId ||
                 entry.Player.CharacterId != player.CharacterId ||
                 entry.Player.AccountId != player.AccountId))
            {
                RemoveLocked(player.ConnectionId, changes);
                entry = null;
            }

            // SyncPresence runs every 250ms per socket even without
            // movement; unchanged records must be O(1), not scan cells.
            if (entry is not null && entry.Player == player &&
                entry.ViewRadius == viewRadius)
                return changes;

            if (entry is null)
            {
                entry = new Entry(player, viewRadius);
                _players.Add(player.ConnectionId, entry);
                AddCell(entry);
                TrackViewRadiusAdded(
                    player.MapId,
                    viewRadius);
            }
            else
            {
                var previousViewRadius = entry.ViewRadius;
                var cell = CellOf(player);
                if (cell != entry.Cell)
                {
                    RemoveCell(entry);
                    entry.Cell = cell;
                    AddCell(entry);
                }
                entry.Player = player;
                entry.ViewRadius = viewRadius;
                TrackViewRadiusChanged(
                    player.MapId,
                    previousViewRadius,
                    viewRadius);
            }

            // Include previous peers to process exits even after moving far
            // away in one tick (teleport, dismount or position correction).
            var candidates = new HashSet<Guid>(entry.Visible);
            var cellKey = entry.Cell;
            var cellReach = (int)Math.Ceiling(
                ActiveViewExitRadius(player.MapId) / CellSize);
            for (var cy = cellKey.CellY - cellReach;
                 cy <= cellKey.CellY + cellReach; cy++)
            {
                for (var cx = cellKey.CellX - cellReach;
                     cx <= cellKey.CellX + cellReach; cx++)
                {
                    if (_cells.TryGetValue(
                        (player.MapId, cx, cy), out var bucket))
                        candidates.UnionWith(bucket);
                }
            }

            candidates.Remove(player.ConnectionId);
            foreach (var peerId in candidates)
            {
                if (!_players.TryGetValue(peerId, out var peer))
                {
                    entry.Visible.Remove(peerId);
                    continue;
                }

                var wasVisible = entry.Visible.Contains(peerId);
                var pairEnterRadius = Math.Max(entry.ViewRadius, peer.ViewRadius);
                var pairRadius = pairEnterRadius +
                    (wasVisible ? ExitRadius - EnterRadius : 0);
                var maxDistanceSquared = pairRadius * pairRadius;
                var dx = player.X - peer.Player.X;
                var dy = player.Y - peer.Player.Y;
                var nowVisible = player.MapId == peer.Player.MapId &&
                    dx * dx + dy * dy <= maxDistanceSquared;

                if (wasVisible == nowVisible)
                    continue;

                if (nowVisible)
                {
                    entry.Visible.Add(peerId);
                    peer.Visible.Add(player.ConnectionId);
                }
                else
                {
                    entry.Visible.Remove(peerId);
                    peer.Visible.Remove(player.ConnectionId);
                }

                changes.Add(new Change(
                    player.ConnectionId, peer.Player, nowVisible));
                changes.Add(new Change(
                    peerId, entry.Player, nowVisible));
            }
            return changes;
        }
    }

    public IReadOnlyList<Change> Remove(Guid connectionId)
    {
        lock (_gate)
        {
            var changes = new List<Change>();
            RemoveLocked(connectionId, changes);
            return changes;
        }
    }

    public IReadOnlyList<OnlinePlayerPresence> VisiblePeers(Guid connectionId)
    {
        lock (_gate)
        {
            if (!_players.TryGetValue(connectionId, out var entry))
                return [];
            var peers = new List<OnlinePlayerPresence>(entry.Visible.Count);
            foreach (var peerId in entry.Visible)
                if (_players.TryGetValue(peerId, out var peer))
                    peers.Add(peer.Player);
            return peers;
        }
    }

    public IReadOnlyList<Guid> ObserversOf(Guid sourceConnectionId)
    {
        lock (_gate)
        {
            if (!_players.TryGetValue(sourceConnectionId, out var entry))
                return [];
            return entry.Visible.ToArray();
        }
    }

    public bool IsVisible(Guid observerConnectionId, Guid sourceConnectionId)
    {
        lock (_gate)
            return _players.TryGetValue(observerConnectionId, out var observer) &&
                observer.Visible.Contains(sourceConnectionId);
    }

    /// <summary>Identity-aware visibility: a connection may switch
    /// characters while a pending old-character DespawnEntity is delivered.
    /// Do not suppress the old despawn just because the new character is
    /// already visible under the same socket identifier.</summary>
    public bool IsVisibleAs(
        Guid observerConnectionId,
        Guid sourceConnectionId,
        long subjectCharacterId)
    {
        lock (_gate)
            return _players.TryGetValue(observerConnectionId, out var observer) &&
                observer.Visible.Contains(sourceConnectionId) &&
                _players.TryGetValue(sourceConnectionId, out var subject) &&
                subject.Player.CharacterId == subjectCharacterId;
    }

    private void RemoveLocked(Guid connectionId, List<Change> changes)
    {
        if (!_players.Remove(connectionId, out var entry))
            return;
        RemoveCell(entry);
        TrackViewRadiusRemoved(
            entry.Player.MapId,
            entry.ViewRadius);
        foreach (var peerId in entry.Visible)
        {
            if (!_players.TryGetValue(peerId, out var peer))
                continue;
            peer.Visible.Remove(connectionId);
            changes.Add(new Change(peerId, entry.Player, false));
            // The departing client is loading a different map, or closing.
            // It will receive a fresh WorldReady snapshot for its new map.
            // Never enqueue stale-map despawns to its new scene.
        }
        entry.Visible.Clear();
    }

    private static double ExitRadiusFor(double viewRadius)
        => viewRadius + (ExitRadius - EnterRadius);

    private double ActiveViewExitRadius(int mapId)
        => _maxViewExitRadiusByMap.TryGetValue(
                mapId,
                out var radius)
            ? radius
            : ExitRadius;

    private void TrackViewRadiusAdded(
        int mapId,
        double viewRadius)
    {
        var exitRadius = ExitRadiusFor(viewRadius);
        if (!_maxViewExitRadiusByMap.TryGetValue(
                mapId,
                out var current) ||
            exitRadius > current)
        {
            _maxViewExitRadiusByMap[mapId] = exitRadius;
        }
    }

    private void TrackViewRadiusChanged(
        int mapId,
        double previousViewRadius,
        double nextViewRadius)
    {
        if (previousViewRadius == nextViewRadius)
            return;

        var previousExit =
            ExitRadiusFor(previousViewRadius);
        var nextExit =
            ExitRadiusFor(nextViewRadius);

        if (!_maxViewExitRadiusByMap.TryGetValue(
                mapId,
                out var current) ||
            nextExit > current)
        {
            _maxViewExitRadiusByMap[mapId] = nextExit;
            return;
        }

        if (previousExit >= current &&
            nextExit < previousExit)
        {
            RecalculateMaxViewExitRadius(mapId);
        }
    }

    private void TrackViewRadiusRemoved(
        int mapId,
        double viewRadius)
    {
        if (_maxViewExitRadiusByMap.TryGetValue(
                mapId,
                out var current) &&
            ExitRadiusFor(viewRadius) >= current)
        {
            RecalculateMaxViewExitRadius(mapId);
        }
    }

    private void RecalculateMaxViewExitRadius(
        int mapId)
    {
        var maxRadius = ExitRadius;
        foreach (var entry in _players.Values)
        {
            if (entry.Player.MapId != mapId)
                continue;

            maxRadius = Math.Max(
                maxRadius,
                ExitRadiusFor(entry.ViewRadius));
        }

        if (maxRadius <= ExitRadius)
            _maxViewExitRadiusByMap.Remove(mapId);
        else
            _maxViewExitRadiusByMap[mapId] = maxRadius;
    }

    private static (int MapId, int CellX, int CellY) CellOf(
        OnlinePlayerPresence player)
        => (player.MapId,
            (int)Math.Floor(player.X / CellSize),
            (int)Math.Floor(player.Y / CellSize));

    private void AddCell(Entry entry)
    {
        if (!_cells.TryGetValue(entry.Cell, out var bucket))
        {
            bucket = new HashSet<Guid>();
            _cells.Add(entry.Cell, bucket);
        }
        bucket.Add(entry.Player.ConnectionId);
    }

    private void RemoveCell(Entry entry)
    {
        if (_cells.TryGetValue(entry.Cell, out var bucket))
        {
            bucket.Remove(entry.Player.ConnectionId);
            if (bucket.Count == 0)
                _cells.Remove(entry.Cell);
        }
    }
}
