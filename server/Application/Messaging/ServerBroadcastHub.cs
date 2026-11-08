using System.Collections.Concurrent;

namespace SGO.Server.Application.Messaging;

public sealed record OnlinePlayerPresence(
    Guid ConnectionId,
    long AccountId,
    long CharacterId,
    int MapId,
    double X,
    double Y,
    string CharacterName = "");

public sealed class ServerBroadcastHub
{
    private sealed record Presence(
        int MapId,
        long AccountId,
        long CharacterId,
        double X,
        double Y,
        string CharacterName);

    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<object>>
        _queues = new();

    private readonly ConcurrentDictionary<Guid, Presence>
        _presence = new();

    // Only WorldReady clients enter the grid. Character selection does not
    // expose a half-loaded player to distant peers.
    private readonly PlayerSpatialAoi _playerAoi = new();

    public int AoiReadyPlayers => _playerAoi.ReadyPlayerCount;

    public (Guid ConnectionId, ConcurrentQueue<object> Queue) Register()
    {
        var id = Guid.NewGuid();
        var queue = new ConcurrentQueue<object>();
        _queues[id] = queue;
        _presence[id] = new Presence(0, 0, 0, 0, 0, "");
        return (id, queue);
    }

    public void UpdatePresence(
        Guid connectionId,
        int mapId,
        long? characterId,
        long? accountId = null,
        double x = 0,
        double y = 0,
        string characterName = "")
    {
        if (connectionId == Guid.Empty ||
            !_queues.ContainsKey(connectionId))
            return;

        _presence[connectionId] = new Presence(
            Math.Max(0, mapId),
            Math.Max(0, accountId ?? 0),
            Math.Max(0, characterId ?? 0),
            x,
            y,
            characterName ?? "");
    }

    public void UpdatePosition(
        Guid connectionId,
        double x,
        double y)
    {
        if (!_presence.TryGetValue(
                connectionId,
                out var current))
            return;

        _presence[connectionId] = current with
        {
            X = x,
            Y = y
        };
    }

    public OnlinePlayerPresence? FindCharacter(long characterId)
    {
        if (characterId <= 0)
            return null;

        foreach (var pair in _presence)
        {
            if (pair.Value.CharacterId != characterId ||
                pair.Value.AccountId <= 0)
                continue;

            return new OnlinePlayerPresence(
                pair.Key,
                pair.Value.AccountId,
                pair.Value.CharacterId,
                pair.Value.MapId,
                pair.Value.X,
                pair.Value.Y,
                pair.Value.CharacterName);
        }

        return null;
    }

    public bool IsCharacterInMap(
        long characterId,
        int mapId)
        => characterId > 0 &&
           _presence.Values.Any(x =>
               x.CharacterId == characterId &&
               x.MapId == mapId);

    public IReadOnlyList<OnlinePlayerPresence> PlayersInMap(
        int mapId,
        Guid exceptConnectionId = default)
        => _presence
            .Where(x =>
                x.Key != exceptConnectionId &&
                x.Value.MapId == mapId &&
                x.Value.AccountId > 0 &&
                x.Value.CharacterId > 0)
            .Select(x => new OnlinePlayerPresence(
                x.Key,
                x.Value.AccountId,
                x.Value.CharacterId,
                x.Value.MapId,
                x.Value.X,
                x.Value.Y,
                x.Value.CharacterName))
            .ToArray();

    public IReadOnlyList<OnlinePlayerPresence> OnlinePlayers(
        Guid exceptConnectionId = default)
        => _presence
            .Where(x =>
                x.Key != exceptConnectionId &&
                x.Value.AccountId > 0 &&
                x.Value.CharacterId > 0)
            .Select(x => new OnlinePlayerPresence(
                x.Key,
                x.Value.AccountId,
                x.Value.CharacterId,
                x.Value.MapId,
                x.Value.X,
                x.Value.Y,
                x.Value.CharacterName))
            .ToArray();

    public void Unregister(Guid connectionId)
    {
        // WebSocketEndpoint calls PacketRouter.DisconnectPresence first;
        // remove here defensively for paths that terminate early.
        _playerAoi.Remove(connectionId);
        _queues.TryRemove(connectionId, out _);
        _presence.TryRemove(connectionId, out _);
    }

    /// <summary>Update one *ready* player's AOI relations after position,
    /// map and identity are in the presence registry.</summary>
    public IReadOnlyList<PlayerSpatialAoi.Change> ReconcilePlayerAoi(
        Guid connectionId)
    {
        if (!_presence.TryGetValue(connectionId, out var presence) ||
            presence.AccountId <= 0 || presence.CharacterId <= 0 ||
            presence.MapId <= 0)
            return _playerAoi.Remove(connectionId);

        return _playerAoi.Upsert(new OnlinePlayerPresence(
            connectionId, presence.AccountId, presence.CharacterId,
            presence.MapId, presence.X, presence.Y, presence.CharacterName));
    }

    public IReadOnlyList<PlayerSpatialAoi.Change> RemovePlayerAoi(
        Guid connectionId)
        => _playerAoi.Remove(connectionId);

    public IReadOnlyList<OnlinePlayerPresence> VisiblePlayerPeers(
        Guid connectionId)
        => _playerAoi.VisiblePeers(connectionId);

    public bool IsPlayerAoiVisible(Guid viewer, Guid subject)
        => _playerAoi.IsVisible(viewer, subject);

    public bool IsPlayerAoiVisibleAs(
        Guid viewer, Guid subject, long subjectCharacterId)
        => _playerAoi.IsVisibleAs(viewer, subject, subjectCharacterId);

    /// <summary>Only peer connections that already received SpawnEntity.
    /// Keeps world chat, party, private responses and commerce independent
    /// from proximity filtering.</summary>
    public void PublishToPlayerObservers(
        Guid sourceConnectionId, object packet)
    {
        foreach (var observer in _playerAoi.ObserversOf(sourceConnectionId))
            // A simultaneous AOI exit may invalidate a copied recipient list.
            if (_playerAoi.IsVisible(observer, sourceConnectionId))
                PublishToConnection(observer, packet);
    }

    public void Publish(object packet)
    {
        foreach (var queue in _queues.Values)
            queue.Enqueue(packet);
    }

    public void PublishToAllExcept(
        Guid exceptConnectionId,
        object packet)
    {
        foreach (var pair in _queues)
        {
            if (pair.Key == exceptConnectionId)
                continue;

            if (!_presence.TryGetValue(
                    pair.Key,
                    out var presence) ||
                presence.AccountId <= 0 ||
                presence.CharacterId <= 0)
            {
                continue;
            }

            pair.Value.Enqueue(packet);
        }
    }

    public bool PublishToConnection(
        Guid connectionId,
        object packet)
    {
        if (connectionId == Guid.Empty ||
            !_queues.TryGetValue(
                connectionId,
                out var queue))
            return false;

        queue.Enqueue(packet);
        return true;
    }

    public bool PublishToCharacter(
        long characterId,
        object packet)
    {
        if (characterId <= 0)
            return false;

        foreach (var pair in _presence)
        {
            if (pair.Value.CharacterId != characterId)
                continue;

            if (!_queues.TryGetValue(
                    pair.Key,
                    out var queue))
                continue;

            queue.Enqueue(packet);
            return true;
        }

        return false;
    }

    public void PublishToMap(
        int mapId,
        object packet,
        Guid exceptConnectionId = default)
    {
        foreach (var pair in _queues)
        {
            if (pair.Key == exceptConnectionId)
                continue;

            if (!_presence.TryGetValue(
                    pair.Key,
                    out var presence) ||
                presence.MapId != mapId)
                continue;

            pair.Value.Enqueue(packet);
        }
    }
}
