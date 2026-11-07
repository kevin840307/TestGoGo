using System.Collections.Concurrent;

namespace SGO.Server.Application.Messaging;

public sealed class ServerBroadcastHub
{
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<object>>
        _queues = new();

    public (Guid ConnectionId, ConcurrentQueue<object> Queue) Register()
    {
        var id = Guid.NewGuid();
        var queue = new ConcurrentQueue<object>();
        _queues[id] = queue;
        return (id, queue);
    }

    public void Unregister(Guid connectionId)
        => _queues.TryRemove(connectionId, out _);

    public void Publish(object packet)
    {
        foreach (var queue in _queues.Values)
            queue.Enqueue(packet);
    }
}
