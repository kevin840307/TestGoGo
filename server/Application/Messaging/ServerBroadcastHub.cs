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

    private sealed record CompanionSnapshot(
        long EntityId,
        object SpawnPacket,
        double X,
        double Y);

    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<object>>
        _queues = new();

    private readonly ConcurrentDictionary<Guid, Presence>
        _presence = new();

    private readonly object _companionGate = new();
    private readonly Dictionary<Guid, Dictionary<long, CompanionSnapshot>>
        _companionsByOwner = new();

    // Only WorldReady clients enter the grid. Character selection does not
    // expose a half-loaded player to distant peers.
    private readonly PlayerSpatialAoi _playerAoi = new();

    // Dynamic world entities have a separate spatial index. Player AOI has a
    // tighter radius; monsters/ground items keep extra visual headroom.
    private readonly WorldEntitySpatialAoi _worldAoi = new();

    public int AoiReadyPlayers => _playerAoi.ReadyPlayerCount;
    public int AoiWorldObservers => _worldAoi.ObserverCount;
    public int AoiWorldEntities => _worldAoi.EntityCount;

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
        _worldAoi.RemoveObserver(connectionId);
        ClearPlayerCompanions(connectionId);
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

    public IReadOnlyList<WorldEntitySpatialAoi.Change> ReconcileWorldAoi(
        Guid connectionId)
    {
        if (!_presence.TryGetValue(
                connectionId,
                out var presence) ||
            presence.AccountId <= 0 ||
            presence.CharacterId <= 0 ||
            presence.MapId <= 0)
        {
            _worldAoi.RemoveObserver(
                connectionId);
            return [];
        }

        return _worldAoi.UpsertObserver(
            new OnlinePlayerPresence(
                connectionId,
                presence.AccountId,
                presence.CharacterId,
                presence.MapId,
                presence.X,
                presence.Y,
                presence.CharacterName));
    }

    public void RemoveWorldAoiObserver(
        Guid connectionId)
        => _worldAoi.RemoveObserver(
            connectionId);

    public IReadOnlyList<WorldEntitySpatialAoi.Change> UpsertWorldEntity(
        WorldAoiEntity entity)
        => _worldAoi.UpsertEntity(
            entity);

    public IReadOnlyList<WorldEntitySpatialAoi.Change> RemoveWorldEntity(
        long entityId,
        bool notifyObservers = true)
        => _worldAoi.RemoveEntity(
            entityId,
            notifyObservers);

    // Capture before reading persisted drops. New AOI entities indexed
    // after this point cannot be removed by an older DB snapshot.
    public long CaptureWorldEntityGeneration()
        => _worldAoi.CaptureEntityGeneration();

    public IReadOnlyList<WorldEntitySpatialAoi.Change> RemoveMissingWorldEntities(
        int mapId,
        int entityType,
        IReadOnlySet<long> authoritativeIds,
        long snapshotGeneration = long.MaxValue)
        => _worldAoi.RemoveMissingEntities(
            mapId,
            entityType,
            authoritativeIds,
            snapshotGeneration);

    public bool IsWorldAoiVisible(
        Guid observerConnectionId,
        long entityId)
        => _worldAoi.IsVisible(
            observerConnectionId,
            entityId);

    public IReadOnlyList<WorldAoiEntity> VisibleWorldEntities(
        Guid observerConnectionId)
        => _worldAoi.VisibleEntities(
            observerConnectionId);

    public bool IsPlayerAoiVisible(Guid viewer, Guid subject)
        => _playerAoi.IsVisible(viewer, subject);

    public bool IsPlayerAoiVisibleAs(
        Guid viewer, Guid subject, long subjectCharacterId)
        => _playerAoi.IsVisibleAs(viewer, subject, subjectCharacterId);

    public bool TrackPlayerCompanionPacket(
        Guid ownerConnectionId,
        object packet)
    {
        if (ownerConnectionId == Guid.Empty)
            return false;

        var type =
            ReadPacketString(
                packet,
                "Type");
        var entityId =
            ReadPacketLong(
                packet,
                "EntityID");

        if (entityId < 4_000_000 ||
            entityId >= 6_000_000)
            return false;

        lock (_companionGate)
        {
            if (type == "DespawnEntity")
            {
                if (_companionsByOwner.TryGetValue(
                        ownerConnectionId,
                        out var existing))
                {
                    existing.Remove(entityId);
                    if (existing.Count == 0)
                    {
                        _companionsByOwner.Remove(
                            ownerConnectionId);
                    }
                }
                return false;
            }

            if (type == "SpawnEntity")
            {
                if (!_companionsByOwner.TryGetValue(
                        ownerConnectionId,
                        out var companions))
                {
                    companions =
                        new Dictionary<long, CompanionSnapshot>();
                    _companionsByOwner.Add(
                        ownerConnectionId,
                        companions);
                }

                var isNew =
                    !companions.ContainsKey(
                        entityId);
                companions[entityId] =
                    new CompanionSnapshot(
                        entityId,
                        packet,
                        ReadPacketDouble(
                            packet,
                            "LocX"),
                        ReadPacketDouble(
                            packet,
                            "LocY"));
                return isNew;
            }

            if (type == "MoveEntity" &&
                _companionsByOwner.TryGetValue(
                    ownerConnectionId,
                    out var tracked) &&
                tracked.TryGetValue(
                    entityId,
                    out var snapshot))
            {
                tracked[entityId] =
                    snapshot with
                    {
                        X = ReadPacketDouble(
                            packet,
                            "X",
                            snapshot.X),
                        Y = ReadPacketDouble(
                            packet,
                            "Y",
                            snapshot.Y)
                    };
            }
        }

        return false;
    }

    public IReadOnlyList<object> PlayerCompanionSnapshots(
        Guid ownerConnectionId)
    {
        lock (_companionGate)
        {
            if (!_companionsByOwner.TryGetValue(
                    ownerConnectionId,
                    out var companions))
                return [];

            return companions.Values
                .OrderBy(x => x.EntityId)
                .Select(BuildCurrentCompanionSpawn)
                .ToArray();
        }
    }

    public IReadOnlyList<long> PlayerCompanionEntityIds(
        Guid ownerConnectionId)
    {
        lock (_companionGate)
        {
            if (!_companionsByOwner.TryGetValue(
                    ownerConnectionId,
                    out var companions))
                return [];

            return companions.Keys
                .OrderBy(x => x)
                .ToArray();
        }
    }

    public void ClearPlayerCompanions(
        Guid ownerConnectionId)
    {
        lock (_companionGate)
        {
            _companionsByOwner.Remove(
                ownerConnectionId);
        }
    }

    /// <summary>Only peer connections that already received SpawnEntity.
    /// Keeps world chat, party, private responses and commerce independent
    /// from proximity filtering.</summary>
    public void PublishToPlayerObservers(
        Guid sourceConnectionId,
        object packet,
        Guid exceptConnectionId = default)
    {
        TrackPlayerCompanionPacket(
            sourceConnectionId,
            packet);

        foreach (var observer in
                 _playerAoi.ObserversOf(
                     sourceConnectionId))
        {
            if (observer == exceptConnectionId)
                continue;

            // A simultaneous AOI exit may invalidate a copied recipient list.
            if (_playerAoi.IsVisible(
                    observer,
                    sourceConnectionId))
            {
                PublishToConnection(
                    observer,
                    packet);
            }
        }
    }

    public void PublishToWorldEntityObservers(
        long entityId,
        object packet,
        Guid exceptConnectionId = default)
    {
        foreach (var observer in
                 _worldAoi.ObserversOf(
                     entityId))
        {
            if (observer == exceptConnectionId)
                continue;

            // The copied list can race an AOI exit; re-check before enqueue.
            if (_worldAoi.IsVisible(
                    observer,
                    entityId))
            {
                PublishToConnection(
                    observer,
                    packet);
            }
        }
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

    private static object BuildCurrentCompanionSpawn(
        CompanionSnapshot snapshot)
    {
        var values =
            new Dictionary<string, object?>(
                StringComparer.Ordinal);

        if (snapshot.SpawnPacket is
            IReadOnlyDictionary<string, object> readOnly)
        {
            foreach (var pair in readOnly)
                values[pair.Key] = pair.Value;
        }
        else if (snapshot.SpawnPacket is
                 IDictionary<string, object> dictionary)
        {
            foreach (var pair in dictionary)
                values[pair.Key] = pair.Value;
        }
        else
        {
            foreach (var property in
                     snapshot.SpawnPacket
                         .GetType()
                         .GetProperties())
            {
                values[property.Name] =
                    property.GetValue(
                        snapshot.SpawnPacket);
            }
        }

        values["Type"] = "SpawnEntity";
        values["EntityID"] = snapshot.EntityId;
        values["LocX"] = snapshot.X;
        values["LocY"] = snapshot.Y;
        values["IsSnapshot"] = true;
        return values;
    }

    private static object? ReadPacketValue(
        object packet,
        string property)
    {
        if (packet is
            IReadOnlyDictionary<string, object> readOnly &&
            readOnly.TryGetValue(
                property,
                out var readOnlyValue))
        {
            return readOnlyValue;
        }

        if (packet is
            IDictionary<string, object> dictionary &&
            dictionary.TryGetValue(
                property,
                out var dictionaryValue))
        {
            return dictionaryValue;
        }

        return packet.GetType()
            .GetProperty(property)
            ?.GetValue(packet);
    }

    private static string? ReadPacketString(
        object packet,
        string property)
        => ReadPacketValue(
                packet,
                property)
            ?.ToString();

    private static long ReadPacketLong(
        object packet,
        string property)
    {
        var raw =
            ReadPacketValue(
                packet,
                property);
        if (raw is null)
            return 0;

        try
        {
            return Convert.ToInt64(raw);
        }
        catch
        {
            return 0;
        }
    }

    private static double ReadPacketDouble(
        object packet,
        string property,
        double fallback = 0)
    {
        var raw =
            ReadPacketValue(
                packet,
                property);
        if (raw is null)
            return fallback;

        try
        {
            return Convert.ToDouble(raw);
        }
        catch
        {
            return fallback;
        }
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
