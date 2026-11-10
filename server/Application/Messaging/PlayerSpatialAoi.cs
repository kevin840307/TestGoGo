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
    // A client may request enough delivery headroom for a larger viewport,
    // but never an unbounded AOI. 4200 covers 4K/ultrawide plus zoom margin
    // without turning one client into a full-map observer.
    public const double MaxEnterRadius = 4200;
    public const double MaxExitRadius = 4600;

    public static double NormalizeEnterRadius(double requested)
    {
        if (double.IsNaN(requested) || double.IsInfinity(requested))
            return EnterRadius;
        return Math.Clamp(
            Math.Max(EnterRadius, requested),
            EnterRadius,
            MaxEnterRadius);
    }

    public static double ExitRadiusFor(double enterRadius)
        => Math.Clamp(
            NormalizeEnterRadius(enterRadius) + (ExitRadius - EnterRadius),
            ExitRadius,
            MaxExitRadius);

    public sealed record Change(
        Guid ObserverConnectionId,
        OnlinePlayerPresence Subject,
        bool Appeared);

    private sealed class Entry(OnlinePlayerPresence player)
    {
        public OnlinePlayerPresence Player = player;
        public double EnterRadius = NormalizeEnterRadius(
            player.ViewInterestRadius);
        public double ExitRadius = ExitRadiusFor(
            player.ViewInterestRadius);
        public readonly HashSet<Guid> Visible = new();
        public (int MapId, int CellX, int CellY) Cell = CellOf(player);
    }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _players = new();
    private readonly Dictionary<(int MapId, int CellX, int CellY),
        HashSet<Guid>> _cells = new();
    // Keep the common 1080p movement path at the legacy search radius.
    // This expands only while at least one connected viewer truly needs it.
    private double _maxActiveExitRadius = ExitRadius;

    public int ReadyPlayerCount
    {
        get { lock (_gate) return _players.Count; }
    }

    public IReadOnlyList<Change> Upsert(OnlinePlayerPresence player)
    {
        lock (_gate)
        {
            var changes = new List<Change>();
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
            if (entry is not null && entry.Player == player)
                return changes;

            if (entry is null)
            {
                entry = new Entry(player);
                _players.Add(player.ConnectionId, entry);
                AddCell(entry);
                if (entry.ExitRadius > _maxActiveExitRadius)
                    _maxActiveExitRadius = entry.ExitRadius;
            }
            else
            {
                var previousExitRadius = entry.ExitRadius;
                var cell = CellOf(player);
                if (cell != entry.Cell)
                {
                    RemoveCell(entry);
                    entry.Cell = cell;
                    AddCell(entry);
                }
                entry.Player = player;
                entry.EnterRadius = NormalizeEnterRadius(
                    player.ViewInterestRadius);
                entry.ExitRadius = ExitRadiusFor(entry.EnterRadius);
                RefreshMaxActiveExitRadius(
                    previousExitRadius,
                    entry.ExitRadius);
            }

            // Include previous peers to process exits even after moving far
            // away in one tick (teleport, dismount or position correction).
            var candidates = new HashSet<Guid>(entry.Visible);
            var cellKey = entry.Cell;
            // A peer may have a larger viewport than the moving player, so
            // search only the bounded maximum nearby cells, then apply the
            // exact pair radius below. This remains spatial, never full-map.
            var cellReach = (int)Math.Ceiling(
                _maxActiveExitRadius / CellSize);
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
                // Keep the established symmetric player edge contract: if
                // either viewport can see the pair, both sides retain the
                // relation. This avoids a second reverse-observer index while
                // still expanding traffic only for nearby large-view clients.
                var pairRadius = wasVisible
                    ? Math.Max(entry.ExitRadius, peer.ExitRadius)
                    : Math.Max(entry.EnterRadius, peer.EnterRadius);
                var dx = player.X - peer.Player.X;
                var dy = player.Y - peer.Player.Y;
                var nowVisible = player.MapId == peer.Player.MapId &&
                    dx * dx + dy * dy <= pairRadius * pairRadius;

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
        if (entry.ExitRadius >= _maxActiveExitRadius)
            RecalculateMaxActiveExitRadius();
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

    private void RefreshMaxActiveExitRadius(
        double previousExitRadius,
        double nextExitRadius)
    {
        if (nextExitRadius > _maxActiveExitRadius)
        {
            _maxActiveExitRadius = nextExitRadius;
            return;
        }

        if (previousExitRadius >= _maxActiveExitRadius &&
            nextExitRadius < previousExitRadius)
        {
            RecalculateMaxActiveExitRadius();
        }
    }

    private void RecalculateMaxActiveExitRadius()
    {
        var maxRadius = ExitRadius;
        foreach (var player in _players.Values)
            maxRadius = Math.Max(maxRadius, player.ExitRadius);
        _maxActiveExitRadius = maxRadius;
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
