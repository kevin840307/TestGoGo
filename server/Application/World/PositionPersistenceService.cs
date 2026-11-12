using SGO.Server.Application.Abstractions;

namespace SGO.Server.Application.World;

/// <summary>
/// Coalesces periodic, already-authorized player positions into short DB
/// batches. It never changes movement, combat, AOI, or outgoing packet order.
/// A critical map/identity transition uses FenceAndDiscard to wait for any
/// in-flight writer and prevent stale coordinates from being committed later.
/// </summary>
public sealed class PositionPersistenceService : BackgroundService
{
    private static readonly TimeSpan FlushInterval =
        TimeSpan.FromMilliseconds(100);

    private readonly ICharacterStore _store;
    private readonly ILogger<PositionPersistenceService> _log;
    private readonly object _pendingGate = new();
    private readonly SemaphoreSlim _writerGate = new(1, 1);
    private readonly Dictionary<(long Account, long Character),
        CharacterPositionWrite> _pending = new();
    // SyncState(walk/idle) used to issue one synchronous SQLite UPDATE
    // *inside* the live session lock; under 20-player combat it blocked
    // packet handling for 250-450ms. Coalesce visual state writes here,
    // using the same background 100ms worker/final logout fence as position.
    private readonly Dictionary<(long Account, long Character), string>
        _pendingStates = new();
    private DateTimeOffset _lastFailureLogAt = DateTimeOffset.MinValue;

    public PositionPersistenceService(
        ICharacterStore store,
        ILogger<PositionPersistenceService> log)
    {
        _store = store;
        _log = log;
    }

    // O(1), no DB access, safe to invoke inside the per-session packet lock.
    // Intermediate snapshots are replaceable; logout/map transitions are not.
    public void Stage(
        long accountId, long characterId, int mapId, int x, int y)
    {
        if (accountId <= 0 || characterId <= 0 || mapId <= 0)
            return;

        lock (_pendingGate)
        {
            _pending[(accountId, characterId)] =
                new CharacterPositionWrite(
                    accountId, characterId, mapId, x, y);
        }
    }

    // O(1), no SQLite writer acquisition while processing SyncState.
    // The same player's last state wins; death/HP remain authoritative in
    // their own database transactions, not in this visual state queue.
    public void StageState(long accountId, long characterId, string state)
    {
        if (accountId <= 0 || characterId <= 0 || state.Length > 32)
            return;
        lock (_pendingGate)
            _pendingStates[(accountId, characterId)] = state;
    }

    private void RestoreStateIfNotNewer(
        (long Account, long Character) key, string state)
    {
        lock (_pendingGate)
            _pendingStates.TryAdd(key, state);
    }

    /// <summary>
    /// Wait for all earlier batched writes; drop uncommitted old snapshots.
    /// Call before authoritative teleport/respawn/identity replacement.
    /// The current packet holds its session lock, so the same player cannot
    /// stage another periodic snapshot during this transition.
    /// </summary>
    public void FenceAndDiscard(long accountId, long characterId)
    {
        _writerGate.Wait();
        try
        {
            lock (_pendingGate)
                _pending.Remove((accountId, characterId));
        }
        finally
        {
            _writerGate.Release();
        }
    }

    // Character selection may follow a superseded session's most recent
    // periodic snapshot. Persist that snapshot before loading its saved data.
    public void FlushQueued(long accountId, long characterId)
    {
        _writerGate.Wait();
        try
        {
            var key = (accountId, characterId);
            CharacterPositionWrite position;
            bool havePosition;
            string? pendingState;
            lock (_pendingGate)
            {
                havePosition = _pending.Remove(key, out position);
                _pendingStates.Remove(key, out pendingState);
            }
            if (havePosition)
            {
                try { _store.UpdatePositionsBatch([position]); }
                catch
                {
                    RestoreIfNotNewer([position]);
                    if (pendingState is not null)
                        RestoreStateIfNotNewer(key, pendingState);
                    throw;
                }
            }
            if (pendingState is not null)
            {
                try { _store.UpdateState(accountId, characterId, pendingState); }
                catch
                {
                    RestoreStateIfNotNewer(key, pendingState);
                    throw;
                }
            }
        }
        finally
        {
            _writerGate.Release();
        }
    }

    // Logout/disconnect is a durability boundary: wait for any older
    // background write and persist the actual latest session coordinates.
    public void FlushFinal(
        long accountId, long characterId, int x, int y, bool dirty)
    {
        _writerGate.Wait();
        try
        {
            CharacterPositionWrite lastQueued;
            bool hadQueued;
            var key = (accountId, characterId);
            string? pendingState;
            lock (_pendingGate)
            {
                hadQueued = _pending.Remove(key, out lastQueued);
                _pendingStates.Remove(key, out pendingState);
            }

            if (dirty)
            {
                if (!_store.UpdatePosition(accountId, characterId, x, y))
                    _log.LogWarning(
                        "[POSITION_FINAL_NOT_FOUND] Character={Character}",
                        characterId);
            }
            else if (hadQueued)
            {
                // Do not discard an already scheduled final periodic write.
                try
                {
                    _store.UpdatePositionsBatch([lastQueued]);
                }
                catch
                {
                    RestoreIfNotNewer([lastQueued]);
                    throw;
                }
            }
            if (pendingState is not null)
            {
                try { _store.UpdateState(accountId, characterId, pendingState); }
                catch
                {
                    RestoreStateIfNotNewer(key, pendingState);
                    throw;
                }
            }
        }
        finally
        {
            _writerGate.Release();
        }
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await DrainOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown: below, flush whatever the runtime has approved.
        }
        finally
        {
            await DrainOnceAsync(CancellationToken.None);
        }
    }

    private void RestoreIfNotNewer(
        IReadOnlyList<CharacterPositionWrite> positions)
    {
        lock (_pendingGate)
        {
            foreach (var position in positions)
                _pending.TryAdd(
                    (position.AccountId, position.CharacterId), position);
        }
    }

    private async Task DrainOnceAsync(CancellationToken token)
    {
        await _writerGate.WaitAsync(token);
        try
        {
            CharacterPositionWrite[] positions;
            KeyValuePair<(long Account, long Character), string>[] states;
            lock (_pendingGate)
            {
                if (_pending.Count == 0 && _pendingStates.Count == 0)
                    return;
                positions = _pending.Values.ToArray();
                states = _pendingStates.ToArray();
                _pending.Clear();
                _pendingStates.Clear();
            }

            if (positions.Length > 0)
            {
                try
                {
                    if (_store is IOpportunisticPositionBatchStore lowPriority)
                    {
                        if (!lowPriority.TryUpdatePositionsBatch(positions))
                        {
                            // The SQLite hot monitor or its only WAL writer is
                            // busy. Keep the newest staged position and retry
                            // at a later heartbeat; never block gameplay just
                            // to persist a replaceable intermediate waypoint.
                            RestoreIfNotNewer(positions);
                        }
                    }
                    else
                    {
                        // Existing fake/non-SQLite stores retain their original
                        // complete flush behavior and contract.
                        _store.UpdatePositionsBatch(positions);
                    }
                }
                catch (Exception ex)
                {
                    RestoreIfNotNewer(positions);
                    LogBatchFailure(ex, "POSITION_BATCH_FAILED", positions.Length);
                }
            }

            // State writes are serialized *outside* all gameplay session
            // locks. One last state per character per flush, never one SQL
            // transaction per incoming SyncState packet.
            foreach (var entry in states)
            {
                try
                {
                    _store.UpdateState(
                        entry.Key.Account, entry.Key.Character, entry.Value);
                }
                catch (Exception ex)
                {
                    RestoreStateIfNotNewer(entry.Key, entry.Value);
                    LogBatchFailure(ex, "STATE_BATCH_FAILED", states.Length);
                }
            }
        }
        finally
        {
            _writerGate.Release();
        }
    }

    private void LogBatchFailure(Exception ex, string code, int count)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastFailureLogAt < TimeSpan.FromSeconds(5))
            return;
        _lastFailureLogAt = now;
        _log.LogError(ex, "[{Code}] Characters={Count} Retry=true",
            code, count);
    }
}
