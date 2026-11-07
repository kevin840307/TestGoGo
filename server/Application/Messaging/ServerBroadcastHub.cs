using System.Collections.Concurrent;

namespace SGO.Server.Application.Messaging;

public sealed class ServerBroadcastHub
{
    private sealed record Presence(
        int MapId,
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
        _presence[id] = new Presence(0, 0);
        return (id, queue);
    }

    public void UpdatePresence(
        Guid connectionId,
        int mapId,
        long? characterId)
    {
        if (connectionId == Guid.Empty ||
            !_queues.ContainsKey(connectionId))
            return;

        _presence[connectionId] = new Presence(
            Math.Max(0, mapId),
            Math.Max(0, characterId ?? 0));
    }

    public bool IsCharacterInMap(
        long characterId,
        int mapId)
        => characterId > 0 &&
           _presence.Values.Any(x =>
               x.CharacterId == characterId &&
               x.MapId == mapId);

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
