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
            CharacterPositionWrite position;
            lock (_pendingGate)
            {
                if (!_pending.Remove((accountId, characterId), out position))
                    return;
            }
            try
            {
                _store.UpdatePositionsBatch([position]);
            }
            catch
            {
                // Preserve data for the worker if a transient SQLite writer
                // busy error prevents the synchronous selection flush.
                RestoreIfNotNewer([position]);
                throw;
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
            lock (_pendingGate)
                hadQueued = _pending.Remove(
                    (accountId, characterId), out lastQueued);

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
            CharacterPositionWrite[] batch;
            lock (_pendingGate)
            {
                if (_pending.Count == 0)
                    return;
                batch = _pending.Values.ToArray();
                _pending.Clear();
            }

            try
            {
                _store.UpdatePositionsBatch(batch);
            }
            catch (Exception ex)
            {
                RestoreIfNotNewer(batch);
                var now = DateTimeOffset.UtcNow;
                if (now - _lastFailureLogAt >= TimeSpan.FromSeconds(5))
                {
                    _lastFailureLogAt = now;
                    _log.LogError(ex,
                        "[POSITION_BATCH_FAILED] Characters={Count} Retry=true",
                        batch.Length);
                }
            }
        }
        finally
        {
            _writerGate.Release();
        }
    }
}
