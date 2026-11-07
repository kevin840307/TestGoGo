using System.Collections.Concurrent;

namespace SGO.Server.Application.Messaging;

public sealed record OnlinePlayerPresence(
    Guid ConnectionId,
    long AccountId,
    long CharacterId,
    int MapId);

public sealed class ServerBroadcastHub
{
    private sealed record Presence(
        int MapId,
        long AccountId,
        long CharacterId);

    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<object>>
        _queues = new();

    private readonly ConcurrentDictionary<Guid, Presence>
        _presence = new();

    public (Guid ConnectionId, ConcurrentQueue<object> Queue) Register()
    {
        var id = Guid.NewGuid();
        var queue = new ConcurrentQueue<object>();
        _queues[id] = queue;
        _presence[id] = new Presence(0, 0, 0);
        return (id, queue);
    }

    public void UpdatePresence(
        Guid connectionId,
        int mapId,
        long? characterId,
        long? accountId = null)
    {
        if (connectionId == Guid.Empty ||
            !_queues.ContainsKey(connectionId))
            return;

        _presence[connectionId] = new Presence(
            Math.Max(0, mapId),
            Math.Max(0, accountId ?? 0),
            Math.Max(0, characterId ?? 0));
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
                x.Value.MapId))
            .ToArray();

    public void Unregister(Guid connectionId)
    {
        _queues.TryRemove(connectionId, out _);
        _presence.TryRemove(connectionId, out _);
    }

    public void Publish(object packet)
    {
        foreach (var queue in _queues.Values)
            queue.Enqueue(packet);
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
