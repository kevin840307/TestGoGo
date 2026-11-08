namespace SGO.Server.Application.Messaging;

/// <summary>
/// Spatial interest management for non-player world entities such as
/// monsters and persisted ground items. Both observers and entities are
/// indexed by map/cell, so movement only checks nearby buckets instead of
/// scanning every player x every entity.
///
/// SpawnPacketFactory is intentionally invoked by the caller after this
/// class releases its lock. Ground-item timers and monster HP therefore use
/// fresh authoritative state when an entity enters a viewer's AOI.
/// </summary>
public sealed record WorldAoiEntity(
    long EntityId,
    int EntityType,
    int MapId,
    double X,
    double Y,
    Func<object> SpawnPacketFactory);

public sealed class WorldEntitySpatialAoi
{
    public const int CellSize = 1024;

    // Larger than the client's current ground visual AOI (2000/2400) and
    // larger than player AOI. This preserves visual headroom while still
    // eliminating full-map dynamic traffic on large maps.
    public const double EnterRadius = 2600;
    public const double ExitRadius = 3000;

    private const double EnterRadiusSquared =
        EnterRadius * EnterRadius;
    private const double ExitRadiusSquared =
        ExitRadius * ExitRadius;

    public sealed record Change(
        Guid ObserverConnectionId,
        WorldAoiEntity Entity,
        bool Appeared);

    private sealed class ObserverEntry(
        OnlinePlayerPresence player)
    {
        public OnlinePlayerPresence Player = player;
        public readonly HashSet<long> Visible = new();
        public (int MapId, int CellX, int CellY) Cell =
            CellOf(player.MapId, player.X, player.Y);
    }

    private sealed class EntityEntry(
        WorldAoiEntity entity,
        long indexedGeneration)
    {
        public WorldAoiEntity Entity = entity;
        public readonly long IndexedGeneration = indexedGeneration;
        public long LastMutationGeneration = indexedGeneration;
        public readonly HashSet<Guid> Observers = new();
        public (int MapId, int CellX, int CellY) Cell =
            CellOf(entity.MapId, entity.X, entity.Y);
    }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, ObserverEntry> _observers = new();
    private readonly Dictionary<long, EntityEntry> _entities = new();
    // Monotonic index generation makes DB snapshot reconciliation safe when
    // another socket drops an item between SELECT and AOI stale-row pruning.
    private long _entityGeneration;
    // Retain removal generations only while at least one map snapshot is
    // reading SQLite. No unbounded per-item tombstone history between loads.
    private int _activeSnapshots;
    private readonly Dictionary<long, long> _removedDuringSnapshots = new();

    public sealed class SnapshotScope : IDisposable
    {
        private WorldEntitySpatialAoi? _owner;
        internal SnapshotScope(WorldEntitySpatialAoi owner, long generation)
        {
            _owner = owner;
            Generation = generation;
        }

        public long Generation { get; }

        internal bool IsActiveFor(WorldEntitySpatialAoi owner)
            => ReferenceEquals(_owner, owner);

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.EndSnapshot();
        }
    }

    public SnapshotScope BeginSnapshot()
    {
        lock (_gate)
        {
            _activeSnapshots++;
            return new SnapshotScope(this, _entityGeneration);
        }
    }

    private void EndSnapshot()
    {
        lock (_gate)
        {
            _activeSnapshots--;
            if (_activeSnapshots == 0)
                _removedDuringSnapshots.Clear();
        }
    }

    /// <summary>
    /// A DB snapshot must never resurrect an entity removed after its SELECT
    /// began, or overwrite a newer live move/state. Caller keeps the scope
    /// alive until the snapshot has been fully reconciled.
    /// </summary>
    public bool TryUpsertEntityFromSnapshot(
        WorldAoiEntity entity,
        SnapshotScope snapshot,
        out IReadOnlyList<Change> changes)
    {
        lock (_gate)
        {
            changes = [];
            if (!snapshot.IsActiveFor(this))
                throw new ObjectDisposedException(nameof(SnapshotScope));
            if (_removedDuringSnapshots.TryGetValue(
                    entity.EntityId, out var removedAt) &&
                removedAt > snapshot.Generation)
                return false;
            if (_entities.TryGetValue(entity.EntityId, out var latest) &&
                latest.LastMutationGeneration > snapshot.Generation)
                return false;
            if (entity.EntityId <= 0 || entity.MapId <= 0)
                return false;

            // The lock is re-entrant: validate and install atomically with
            // respect to other threads' live Upsert/Remove operations.
            changes = UpsertEntity(entity);
            return true;
        }
    }

    private readonly Dictionary<
        (int MapId, int CellX, int CellY),
        HashSet<Guid>> _observerCells = new();

    private readonly Dictionary<
        (int MapId, int CellX, int CellY),
        HashSet<long>> _entityCells = new();

    private readonly Dictionary<
        (int MapId, int EntityType),
        HashSet<long>> _entityGroups = new();

    public int ObserverCount
    {
        get { lock (_gate) return _observers.Count; }
    }

    public int EntityCount
    {
        get { lock (_gate) return _entities.Count; }
    }

    public long CaptureEntityGeneration()
    {
        lock (_gate)
            return _entityGeneration;
    }

    public IReadOnlyList<Change> UpsertObserver(
        OnlinePlayerPresence player)
    {
        lock (_gate)
        {
            if (player.ConnectionId == Guid.Empty ||
                player.AccountId <= 0 ||
                player.CharacterId <= 0 ||
                player.MapId <= 0)
            {
                RemoveObserverLocked(
                    player.ConnectionId);
                return [];
            }

            var transitionChanges = new List<Change>();

            if (_observers.TryGetValue(
                    player.ConnectionId,
                    out var entry) &&
                (entry.Player.MapId != player.MapId ||
                 entry.Player.CharacterId != player.CharacterId ||
                 entry.Player.AccountId != player.AccountId))
            {
                foreach (var entityId in entry.Visible)
                {
                    if (_entities.TryGetValue(
                            entityId,
                            out var oldEntity))
                    {
                        transitionChanges.Add(
                            new Change(
                                player.ConnectionId,
                                oldEntity.Entity,
                                Appeared: false));
                    }
                }

                RemoveObserverLocked(
                    player.ConnectionId);
                entry = null;
            }

            if (entry is not null &&
                SamePosition(entry.Player, player))
                return transitionChanges;

            if (entry is null)
            {
                entry = new ObserverEntry(player);
                _observers.Add(
                    player.ConnectionId,
                    entry);
                AddObserverCell(entry);
            }
            else
            {
                var nextCell = CellOf(
                    player.MapId,
                    player.X,
                    player.Y);
                if (nextCell != entry.Cell)
                {
                    RemoveObserverCell(entry);
                    entry.Cell = nextCell;
                    AddObserverCell(entry);
                }
                entry.Player = player;
            }

            var candidates =
                new HashSet<long>(
                    entry.Visible);

            foreach (var entityId in
                     NearbyEntityIds(
                         entry.Cell,
                         ExitRadius))
            {
                candidates.Add(entityId);
            }

            var changes = transitionChanges;
            foreach (var entityId in candidates)
            {
                if (!_entities.TryGetValue(
                        entityId,
                        out var entity))
                {
                    entry.Visible.Remove(
                        entityId);
                    continue;
                }

                var wasVisible =
                    entry.Visible.Contains(
                        entityId);
                var nowVisible = IsWithin(
                    player.MapId,
                    player.X,
                    player.Y,
                    entity.Entity.MapId,
                    entity.Entity.X,
                    entity.Entity.Y,
                    wasVisible
                        ? ExitRadiusSquared
                        : EnterRadiusSquared);

                if (wasVisible == nowVisible)
                    continue;

                if (nowVisible)
                {
                    entry.Visible.Add(entityId);
                    entity.Observers.Add(
                        player.ConnectionId);
                }
                else
                {
                    entry.Visible.Remove(entityId);
                    entity.Observers.Remove(
                        player.ConnectionId);
                }

                changes.Add(
                    new Change(
                        player.ConnectionId,
                        entity.Entity,
                        nowVisible));
            }

            return changes;
        }
    }

    public IReadOnlyList<Change> UpsertEntity(
        WorldAoiEntity entity)
    {
        lock (_gate)
        {
            if (entity.EntityId <= 0 ||
                entity.MapId <= 0)
                return [];

            if (_entities.TryGetValue(
                    entity.EntityId,
                    out var entry) &&
                (entry.Entity.MapId != entity.MapId ||
                 entry.Entity.EntityType != entity.EntityType))
            {
                // Entity identity reused across maps/types: old observers must
                // drop the old incarnation before the new one can appear.
                var removed = RemoveEntityLocked(
                    entity.EntityId,
                    notifyObservers: true);
                entry = null;

                var added = UpsertEntityLocked(
                    entity);
                return removed
                    .Concat(added)
                    .ToArray();
            }

            if (entry is null)
            {
                entry = new EntityEntry(entity, ++_entityGeneration);
                _entities.Add(
                    entity.EntityId,
                    entry);
                AddEntityCell(entry);
            }
            else
            {
                // A state-only refresh (HP, drops' remaining lifetime, or
                // updated SpawnPacketFactory) does not change visibility.
                // Observers already reconcile their own movement, so
                // re-scanning all nearby observer cells for every stationary
                // monster on each AI tick is unnecessary. Keep the newest
                // factory for late AOI entrants without duplicating Spawn.
                if (entry.Entity.X == entity.X &&
                    entry.Entity.Y == entity.Y)
                {
                    entry.Entity = entity;
                    entry.LastMutationGeneration = ++_entityGeneration;
                    return [];
                }

                var nextCell = CellOf(
                    entity.MapId,
                    entity.X,
                    entity.Y);
                if (nextCell != entry.Cell)
                {
                    RemoveEntityCell(entry);
                    entry.Cell = nextCell;
                    AddEntityCell(entry);
                }

                // Always retain the newest factory/state even if the entity
                // did not move. A later AOI enter must see current HP/timers.
                entry.Entity = entity;
                entry.LastMutationGeneration = ++_entityGeneration;
            }

            return ReconcileEntityLocked(
                entry);
        }
    }

    public IReadOnlyList<Change> RemoveMissingEntities(
        int mapId,
        int entityType,
        IReadOnlySet<long> authoritativeIds,
        long snapshotGeneration = long.MaxValue)
    {
        lock (_gate)
        {
            if (!_entityGroups.TryGetValue(
                    (mapId, entityType),
                    out var group) ||
                group.Count == 0)
                return [];

            var stale =
                group.Where(id =>
                        !authoritativeIds.Contains(id) &&
                        _entities.TryGetValue(id, out var entity) &&
                        entity.LastMutationGeneration <= snapshotGeneration)
                    .ToArray();

            var changes =
                new List<Change>();
            foreach (var entityId in stale)
            {
                changes.AddRange(
                    RemoveEntityLocked(
                        entityId,
                        notifyObservers: true));
            }

            return changes;
        }
    }

    public IReadOnlyList<Change> RemoveEntity(
        long entityId,
        bool notifyObservers = true)
    {
        lock (_gate)
            return RemoveEntityLocked(
                entityId,
                notifyObservers);
    }

    public void RemoveObserver(
        Guid connectionId)
    {
        lock (_gate)
            RemoveObserverLocked(
                connectionId);
    }

    public bool IsVisible(
        Guid observerConnectionId,
        long entityId)
    {
        lock (_gate)
            return _observers.TryGetValue(
                       observerConnectionId,
                       out var observer) &&
                   observer.Visible.Contains(
                       entityId);
    }

    public IReadOnlyList<Guid> ObserversOf(
        long entityId)
    {
        lock (_gate)
        {
            if (!_entities.TryGetValue(
                    entityId,
                    out var entity))
                return [];

            return entity.Observers.ToArray();
        }
    }

    public IReadOnlyList<WorldAoiEntity> VisibleEntities(
        Guid observerConnectionId)
    {
        lock (_gate)
        {
            if (!_observers.TryGetValue(
                    observerConnectionId,
                    out var observer))
                return [];

            var result =
                new List<WorldAoiEntity>(
                    observer.Visible.Count);
            foreach (var entityId in
                     observer.Visible)
            {
                if (_entities.TryGetValue(
                        entityId,
                        out var entity))
                    result.Add(
                        entity.Entity);
            }
            return result;
        }
    }

    private IReadOnlyList<Change> UpsertEntityLocked(
        WorldAoiEntity entity)
    {
        var entry = new EntityEntry(entity, ++_entityGeneration);
        _entities.Add(
            entity.EntityId,
            entry);
        AddEntityCell(entry);
        return ReconcileEntityLocked(
            entry);
    }

    private IReadOnlyList<Change> ReconcileEntityLocked(
        EntityEntry entry)
    {
        var candidates =
            new HashSet<Guid>(
                entry.Observers);

        foreach (var observerId in
                 NearbyObserverIds(
                     entry.Cell,
                     ExitRadius))
        {
            candidates.Add(observerId);
        }

        var changes = new List<Change>();
        foreach (var observerId in candidates)
        {
            if (!_observers.TryGetValue(
                    observerId,
                    out var observer))
            {
                entry.Observers.Remove(
                    observerId);
                continue;
            }

            var wasVisible =
                entry.Observers.Contains(
                    observerId);
            var nowVisible = IsWithin(
                observer.Player.MapId,
                observer.Player.X,
                observer.Player.Y,
                entry.Entity.MapId,
                entry.Entity.X,
                entry.Entity.Y,
                wasVisible
                    ? ExitRadiusSquared
                    : EnterRadiusSquared);

            if (wasVisible == nowVisible)
                continue;

            if (nowVisible)
            {
                entry.Observers.Add(
                    observerId);
                observer.Visible.Add(
                    entry.Entity.EntityId);
            }
            else
            {
                entry.Observers.Remove(
                    observerId);
                observer.Visible.Remove(
                    entry.Entity.EntityId);
            }

            changes.Add(
                new Change(
                    observerId,
                    entry.Entity,
                    nowVisible));
        }

        return changes;
    }

    private IReadOnlyList<Change> RemoveEntityLocked(
        long entityId,
        bool notifyObservers)
    {
        if (!_entities.Remove(
                entityId,
                out var entry))
        {
            // A pickup can commit before the first WorldReady snapshot has
            // indexed its row. Even an absent AOI entry must leave a temporary
            // tombstone, otherwise that older SELECT could resurrect it.
            if (_activeSnapshots > 0)
                _removedDuringSnapshots[entityId] = ++_entityGeneration;
            return [];
        }

        RemoveEntityCell(entry);
        var removedAt = ++_entityGeneration;
        if (_activeSnapshots > 0)
            _removedDuringSnapshots[entityId] = removedAt;

        var changes =
            notifyObservers
                ? new List<Change>(
                    entry.Observers.Count)
                : null;

        foreach (var observerId in
                 entry.Observers)
        {
            if (_observers.TryGetValue(
                    observerId,
                    out var observer))
            {
                observer.Visible.Remove(
                    entityId);
            }

            changes?.Add(
                new Change(
                    observerId,
                    entry.Entity,
                    false));
        }

        entry.Observers.Clear();
        return changes ??
               (IReadOnlyList<Change>)
               Array.Empty<Change>();
    }

    private void RemoveObserverLocked(
        Guid connectionId)
    {
        if (!_observers.Remove(
                connectionId,
                out var entry))
            return;

        RemoveObserverCell(entry);

        foreach (var entityId in
                 entry.Visible)
        {
            if (_entities.TryGetValue(
                    entityId,
                    out var entity))
            {
                entity.Observers.Remove(
                    connectionId);
            }
        }

        entry.Visible.Clear();
    }

    private IEnumerable<long> NearbyEntityIds(
        (int MapId, int CellX, int CellY) cell,
        double radius)
    {
        var reach =
            (int)Math.Ceiling(
                radius / CellSize);

        for (var y = cell.CellY - reach;
             y <= cell.CellY + reach;
             y++)
        {
            for (var x = cell.CellX - reach;
                 x <= cell.CellX + reach;
                 x++)
            {
                if (_entityCells.TryGetValue(
                        (cell.MapId, x, y),
                        out var bucket))
                {
                    foreach (var entityId in bucket)
                        yield return entityId;
                }
            }
        }
    }

    private IEnumerable<Guid> NearbyObserverIds(
        (int MapId, int CellX, int CellY) cell,
        double radius)
    {
        var reach =
            (int)Math.Ceiling(
                radius / CellSize);

        for (var y = cell.CellY - reach;
             y <= cell.CellY + reach;
             y++)
        {
            for (var x = cell.CellX - reach;
                 x <= cell.CellX + reach;
                 x++)
            {
                if (_observerCells.TryGetValue(
                        (cell.MapId, x, y),
                        out var bucket))
                {
                    foreach (var observerId in bucket)
                        yield return observerId;
                }
            }
        }
    }

    private void AddObserverCell(
        ObserverEntry entry)
    {
        if (!_observerCells.TryGetValue(
                entry.Cell,
                out var bucket))
        {
            bucket = new HashSet<Guid>();
            _observerCells.Add(
                entry.Cell,
                bucket);
        }

        bucket.Add(
            entry.Player.ConnectionId);
    }

    private void RemoveObserverCell(
        ObserverEntry entry)
    {
        if (!_observerCells.TryGetValue(
                entry.Cell,
                out var bucket))
            return;

        bucket.Remove(
            entry.Player.ConnectionId);
        if (bucket.Count == 0)
            _observerCells.Remove(
                entry.Cell);
    }

    private void AddEntityCell(
        EntityEntry entry)
    {
        if (!_entityCells.TryGetValue(
                entry.Cell,
                out var bucket))
        {
            bucket = new HashSet<long>();
            _entityCells.Add(
                entry.Cell,
                bucket);
        }

        bucket.Add(
            entry.Entity.EntityId);

        var groupKey =
            (entry.Entity.MapId,
             entry.Entity.EntityType);
        if (!_entityGroups.TryGetValue(
                groupKey,
                out var group))
        {
            group = new HashSet<long>();
            _entityGroups.Add(
                groupKey,
                group);
        }

        group.Add(
            entry.Entity.EntityId);
    }

    private void RemoveEntityCell(
        EntityEntry entry)
    {
        if (!_entityCells.TryGetValue(
                entry.Cell,
                out var bucket))
            return;

        bucket.Remove(
            entry.Entity.EntityId);
        if (bucket.Count == 0)
            _entityCells.Remove(
                entry.Cell);

        var groupKey =
            (entry.Entity.MapId,
             entry.Entity.EntityType);
        if (_entityGroups.TryGetValue(
                groupKey,
                out var group))
        {
            group.Remove(
                entry.Entity.EntityId);
            if (group.Count == 0)
                _entityGroups.Remove(
                    groupKey);
        }
    }

    private static bool IsWithin(
        int firstMap,
        double firstX,
        double firstY,
        int secondMap,
        double secondX,
        double secondY,
        double radiusSquared)
    {
        if (firstMap != secondMap)
            return false;

        var dx = firstX - secondX;
        var dy = firstY - secondY;
        return dx * dx + dy * dy <=
               radiusSquared;
    }

    private static bool SamePosition(
        OnlinePlayerPresence first,
        OnlinePlayerPresence second)
        => first.MapId == second.MapId &&
           first.AccountId == second.AccountId &&
           first.CharacterId == second.CharacterId &&
           first.X == second.X &&
           first.Y == second.Y;

    private static (
        int MapId,
        int CellX,
        int CellY) CellOf(
            int mapId,
            double x,
            double y)
        => (
            mapId,
            (int)Math.Floor(
                x / CellSize),
            (int)Math.Floor(
                y / CellSize));
}
