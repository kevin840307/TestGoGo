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
    private const double EnterRadiusSquared = EnterRadius * EnterRadius;
    private const double ExitRadiusSquared = ExitRadius * ExitRadius;

    public sealed record Change(
        Guid ObserverConnectionId,
        OnlinePlayerPresence Subject,
        bool Appeared);

    private sealed class Entry(OnlinePlayerPresence player)
    {
        public OnlinePlayerPresence Player = player;
        public readonly HashSet<Guid> Visible = new();
        public (int MapId, int CellX, int CellY) Cell = CellOf(player);
    }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _players = new();
    private readonly Dictionary<(int MapId, int CellX, int CellY),
        HashSet<Guid>> _cells = new();

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

            if (entry is null)
            {
                entry = new Entry(player);
                _players.Add(player.ConnectionId, entry);
                AddCell(entry);
            }
            else
            {
                var cell = CellOf(player);
                if (cell != entry.Cell)
                {
                    RemoveCell(entry);
                    entry.Cell = cell;
                    AddCell(entry);
                }
                entry.Player = player;
            }

            // Include previous peers to process exits even after moving far
            // away in one tick (teleport, dismount or position correction).
            var candidates = new HashSet<Guid>(entry.Visible);
            var cellKey = entry.Cell;
            var cellReach = (int)Math.Ceiling(ExitRadius / CellSize);
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
                var maxDistanceSquared = wasVisible
                    ? ExitRadiusSquared : EnterRadiusSquared;
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

    private void RemoveLocked(Guid connectionId, List<Change> changes)
    {
        if (!_players.Remove(connectionId, out var entry))
            return;
        RemoveCell(entry);
        foreach (var peerId in entry.Visible)
        {
            if (!_players.TryGetValue(peerId, out var peer))
                continue;
            peer.Visible.Remove(connectionId);
            changes.Add(new Change(peerId, entry.Player, false));
            // The departing client may still be connected during a
            // same-map teleport; send its own reciprocal despawn too.
            changes.Add(new Change(connectionId, peer.Player, false));
        }
        entry.Visible.Clear();
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
