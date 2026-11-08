using System.Collections.Concurrent;

namespace SGO.Server.Application.Messaging;

public sealed class AccountSessionRegistry
{
    private readonly ConcurrentDictionary<long, Guid>
        _accountConnections = new();

    private readonly ConcurrentDictionary<Guid, long>
        _connectionAccounts = new();

    public Guid? Register(
        long accountId,
        Guid connectionId)
    {
        if (accountId <= 0 ||
            connectionId == Guid.Empty)
            return null;

        Unregister(connectionId);

        while (true)
        {
            if (_accountConnections.TryGetValue(
                    accountId,
                    out var existing))
            {
                if (existing == connectionId)
                {
                    _connectionAccounts[connectionId] = accountId;
                    return null;
                }

                if (!_accountConnections.TryUpdate(
                        accountId,
                        connectionId,
                        existing))
                    continue;

                _connectionAccounts.TryRemove(
                    existing,
                    out _);
                _connectionAccounts[connectionId] = accountId;
                return existing;
            }

            if (!_accountConnections.TryAdd(
                    accountId,
                    connectionId))
                continue;

            _connectionAccounts[connectionId] = accountId;
            return null;
        }
    }

    public bool IsCurrent(
        long accountId,
        Guid connectionId)
        => accountId > 0 &&
           connectionId != Guid.Empty &&
           _accountConnections.TryGetValue(
               accountId,
               out var current) &&
           current == connectionId;

    public void Unregister(Guid connectionId)
    {
        if (connectionId == Guid.Empty ||
            !_connectionAccounts.TryRemove(
                connectionId,
                out var accountId))
            return;

        ((ICollection<KeyValuePair<long, Guid>>)
            _accountConnections)
            .Remove(new KeyValuePair<long, Guid>(
                accountId,
                connectionId));
    }
}

public static class ConnectionHeartbeat
{
    public static readonly TimeSpan PingInterval =
        TimeSpan.FromSeconds(20);

    public static readonly TimeSpan Timeout =
        TimeSpan.FromSeconds(60);

    public static bool ShouldPing(
        DateTimeOffset lastPingAt,
        DateTimeOffset now)
        => lastPingAt == DateTimeOffset.MinValue ||
           now - lastPingAt >= PingInterval;

    public static bool IsTimedOut(
        DateTimeOffset lastPongAt,
        DateTimeOffset now)
        => lastPongAt != DateTimeOffset.MinValue &&
           now - lastPongAt >= Timeout;
}
