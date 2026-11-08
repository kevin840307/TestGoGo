using System.Collections.Concurrent;

namespace SGO.Server.Application.Diagnostics;

public sealed record ServerDiagnosticEvent(
    long Sequence,
    DateTimeOffset Timestamp,
    string Level,
    string Category,
    string Message,
    Guid? ConnectionId = null,
    long? AccountId = null,
    long? CharacterId = null,
    int? MapId = null,
    double? ElapsedMilliseconds = null);

public sealed class ServerDiagnostics
{
    private const int MaxEvents = 500;
    private readonly ConcurrentQueue<ServerDiagnosticEvent> _events = new();
    private long _sequence;
    private long _packetCount;
    private long _packetErrorCount;
    private long _slowPacketCount;
    private long _webSocketConnectCount;
    private long _webSocketDisconnectCount;
    private long _mapAiSlowTickCount;
    private long _mapPressureWarningCount;
    private long _broadcastBacklogWarningCount;
    private long _tradeConflictCount;
    private long _pickupConflictCount;
    private long _databaseFailureCount;
    private long _worldTickFailureCount;
    private long _resourceWarningCount;

    public void PacketHandled() => Interlocked.Increment(ref _packetCount);
    public void PacketError() => Interlocked.Increment(ref _packetErrorCount);
    public void SlowPacket() => Interlocked.Increment(ref _slowPacketCount);
    public void WebSocketConnected() => Interlocked.Increment(ref _webSocketConnectCount);
    public void WebSocketDisconnected() => Interlocked.Increment(ref _webSocketDisconnectCount);
    public void MapAiSlowTick() => Interlocked.Increment(ref _mapAiSlowTickCount);
    public void MapPressureWarning() => Interlocked.Increment(ref _mapPressureWarningCount);
    public void BroadcastBacklogWarning() => Interlocked.Increment(ref _broadcastBacklogWarningCount);
    public void TradeConflict() => Interlocked.Increment(ref _tradeConflictCount);
    public void PickupConflict() => Interlocked.Increment(ref _pickupConflictCount);
    public void DatabaseFailure() => Interlocked.Increment(ref _databaseFailureCount);
    public void WorldTickFailure() => Interlocked.Increment(ref _worldTickFailureCount);
    public void ResourceWarning() => Interlocked.Increment(ref _resourceWarningCount);

    public void Record(
        string level,
        string category,
        string message,
        Guid? connectionId = null,
        long? accountId = null,
        long? characterId = null,
        int? mapId = null,
        double? elapsedMilliseconds = null)
    {
        var item = new ServerDiagnosticEvent(
            Interlocked.Increment(ref _sequence),
            DateTimeOffset.UtcNow,
            level,
            category,
            message,
            connectionId,
            accountId,
            characterId,
            mapId,
            elapsedMilliseconds);

        _events.Enqueue(item);
        while (_events.Count > MaxEvents &&
               _events.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<ServerDiagnosticEvent> Recent(int count = 100)
    {
        var take = Math.Clamp(count, 1, MaxEvents);
        return _events
            .Reverse()
            .Take(take)
            .ToArray();
    }

    public object SnapshotCounters() => new
    {
        packets = Interlocked.Read(ref _packetCount),
        packetErrors = Interlocked.Read(ref _packetErrorCount),
        slowPackets = Interlocked.Read(ref _slowPacketCount),
        webSocketConnects = Interlocked.Read(ref _webSocketConnectCount),
        webSocketDisconnects = Interlocked.Read(ref _webSocketDisconnectCount),
        mapAiSlowTicks = Interlocked.Read(ref _mapAiSlowTickCount),
        mapPressureWarnings = Interlocked.Read(ref _mapPressureWarningCount),
        broadcastBacklogWarnings = Interlocked.Read(ref _broadcastBacklogWarningCount),
        tradeConflicts = Interlocked.Read(ref _tradeConflictCount),
        pickupConflicts = Interlocked.Read(ref _pickupConflictCount),
        databaseFailures = Interlocked.Read(ref _databaseFailureCount),
        worldTickFailures = Interlocked.Read(ref _worldTickFailureCount),
        resourceWarnings = Interlocked.Read(ref _resourceWarningCount)
    };
}
