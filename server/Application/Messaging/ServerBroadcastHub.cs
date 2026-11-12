using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using SGO.Server.Protocol;

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

    // One immutable player/world event is normally delivered to many nearby
    // sockets. Serialize/encrypt scalar-only anonymous packets once by object
    // identity instead of repeating this CPU work for every observer. Mutable
    // collections and structured payloads never enter the cache.
    private static readonly ConcurrentDictionary<Type, bool>
        CacheableBroadcastTypes = new();
    private readonly ConditionalWeakTable<object, Lazy<byte[]>>
        _encodedBroadcastPackets = new();

    private static bool IsCacheableBroadcastType(Type type)
        => CacheableBroadcastTypes.GetOrAdd(type, static t =>
        {
            if (!t.IsDefined(typeof(CompilerGeneratedAttribute), false) ||
                !t.Name.Contains("AnonymousType", StringComparison.Ordinal))
                return false;

            foreach (var property in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var propertyType = Nullable.GetUnderlyingType(property.PropertyType)
                                   ?? property.PropertyType;
                if (!propertyType.IsPrimitive && !propertyType.IsEnum &&
                    propertyType != typeof(string) &&
                    propertyType != typeof(decimal))
                    return false;
            }

            return true;
        });

    public byte[] EncodeBroadcastPacket(object packet)
    {
        if (!IsCacheableBroadcastType(packet.GetType()))
            return Encoding.UTF8.GetBytes(ClientProtocol.Encode(packet));

        // Weak keys keep memory bounded by packet lifetime. Lazy guarantees
        // that observers concurrently encoding the same packet share one
        // complete, immutable encrypted frame.
        return _encodedBroadcastPackets.GetValue(
            packet,
            static key => new Lazy<byte[]>(
                () => Encoding.UTF8.GetBytes(ClientProtocol.Encode(key)),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }
    // Opt-in WebSocket fast lane. Legacy in-process registrations keep the
    // original single queue, preserving their ordering and test contracts.
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<object>>
        _playerPriorityQueues = new();

    // For active WebSockets only. A MoveEntity is a visual snapshot, not a
    // gameplay action. While the socket is backpressured, keep only the
    // newest unsent position per player, without coalescing across a
    // Spawn/Despawn/SyncState/attack/death boundary for that same player.
    private sealed class PendingPlayerMove(long entityId, object packet)
    {
        public long EnqueuedAt { get; } = Stopwatch.GetTimestamp();
        public long EntityId { get; } = entityId;
        public object LatestPacket { get; set; } = packet;
        // A queued visual move may replace several 100ms snapshots. Retain
        // their combined server elapsed time so the client does not mistake
        // hundreds of milliseconds of walking for a single short step.
        // Zero means this packet did not provide a trustworthy interval.
        public double TotalServerSeconds { get; set; } =
            ReadMovementServerSpan(packet);
        public int MoveCount { get; set; } = 1;
    }

    // A movement packet can already represent a delayed/coalesced server
    // interval before it reaches this observer queue. Prefer that full span
    // over the compatibility-capped SpeedTime.
    private static double ReadMovementServerSpan(object packet)
    {
        var speedTime = ReadPacketDouble(packet, "SpeedTime");
        // Long monster wander packets may validly represent 1.25 seconds.
        if (speedTime is <= 0.01 or > 1.5)
            return 0;
        var extended = ReadPacketDouble(packet, "SnapshotSpanSeconds");
        return extended is > 0.75 and <= 8.0
            ? extended
            : speedTime;
    }

    private sealed class PlayerPriorityMovementState
    {
        public readonly object Gate = new();
        public readonly Dictionary<long, PendingPlayerMove> Pending = new();
    }

    private readonly ConcurrentDictionary<Guid, PlayerPriorityMovementState>
        _playerPriorityMovementStates = new();

    // Active WebSockets also collapse stale, unsent movement snapshots for
    // world actors (monsters/pets). These are visual waypoints, not gameplay
    // actions. Spawn/Despawn/state/combat packets remain FIFO boundaries.
    private sealed class PendingWorldMove(long entityId, object packet)
    {
        public long EnqueuedAt { get; } = Stopwatch.GetTimestamp();
        public long EntityId { get; } = entityId;
        public object LatestPacket { get; set; } = packet;
        public double TotalServerSeconds { get; set; } =
            ReadMovementServerSpan(packet);
        // Weighted path length lets the client preserve original-style
        // entity movement speed even when several unsent waypoints collapse
        // into one latest destination.
        public double TotalPathDistance { get; set; } =
            ReadPacketDouble(packet, "MoveSpeed") > 0 &&
            ReadMovementServerSpan(packet) > 0
                ? ReadPacketDouble(packet, "MoveSpeed") *
                  ReadMovementServerSpan(packet)
                : 0;
        public int MoveCount { get; set; } = 1;
    }

    private sealed class WorldMovementState
    {
        public readonly object Gate = new();
        public readonly Dictionary<long, PendingWorldMove> Pending = new();
    }

    private readonly ConcurrentDictionary<Guid, WorldMovementState>
        _worldMovementStates = new();

    // Only actual WebSocket queues use these envelopes. In-process legacy
    // queues still expose the original packet object and FIFO contract.
    private sealed record StampedBroadcast(object Packet, long EnqueuedAt);
    private readonly ConcurrentDictionary<Guid, BroadcastQueueAgeRecorder>
        _queueAgeRecorders = new();

    // Wake only active senders. A single bounded notification coalesces
    // bursts; there is no 25ms polling timer per connected WebSocket.
    private readonly ConcurrentDictionary<Guid, Channel<byte>>
        _queueSignals = new();

    public ChannelReader<byte> GetBroadcastSignal(Guid connectionId)
        => _queueSignals[connectionId].Reader;

    private void EnqueueBroadcast(
        Guid connectionId,
        ConcurrentQueue<object> queue,
        object packet)
    {
        // Prioritize only independent player-origin packets. Gameplay and
        // ground-item updates are NEVER dropped or coalesced.
        if (_playerPriorityQueues.TryGetValue(connectionId, out var playerQueue) &&
            IsPlayerPriorityPacket(packet))
        {
            if (_playerPriorityMovementStates.TryGetValue(
                    connectionId, out var movement))
            {
                lock (movement.Gate)
                {
                    var type = ReadPacketString(packet, "Type");
                    var entityId = ReadPacketLong(packet, "EntityID");
                    // Unlike replaceable destination updates, a
                    // SpeedTime=0 packet is an authoritative stop/position
                    // correction (legacy A10D-like event). It separates
                    // walking windows even when the socket is backpressured.
                    // Old packets without SpeedTime stay coalescible.
                    var speedRaw = ReadPacketValue(packet, "SpeedTime");
                    var speedSeconds = ReadPacketDouble(packet, "SpeedTime");
                    var correction = speedRaw is not null &&
                        (!double.IsFinite(speedSeconds) || speedSeconds <= 0.01);
                    if (type == "MoveEntity" &&
                        entityId is > 0 and < 1_000_000 &&
                        !correction)
                    {
                        if (movement.Pending.TryGetValue(entityId, out var slot))
                        {
                            // The old MoveEntity has not left this queue yet.
                            // No need to transmit a historical intermediate
                            // point before the latest authoritative position.
                            var nextSeconds = ReadMovementServerSpan(packet);
                            slot.TotalServerSeconds =
                                slot.TotalServerSeconds > 0 && nextSeconds > 0
                                    ? Math.Min(
                                        8.0,
                                        slot.TotalServerSeconds + nextSeconds)
                                    : 0;
                            slot.MoveCount++;
                            slot.LatestPacket = packet;
                        }
                        else
                        {
                            slot = new PendingPlayerMove(entityId, packet);
                            movement.Pending.Add(entityId, slot);
                            playerQueue.Enqueue(slot);
                        }
                    }
                    else
                    {
                        // Never merge movement across gameplay or identity
                        // boundaries: the next position needs a new slot after
                        // the original packet, in the original FIFO order.
                        if (entityId is > 0 and < 1_000_000)
                            movement.Pending.Remove(entityId);
                        if (type == "SkillCast")
                            movement.Pending.Remove(ReadPacketLong(packet, "CasterID"));
                        if (type == "EntityDamage")
                        {
                            movement.Pending.Remove(ReadPacketLong(packet, "AttackerID"));
                            if (ReadPacketLong(packet, "TargetType") == 0)
                                movement.Pending.Remove(ReadPacketLong(packet, "TargetID"));
                        }
                        if (type == "EntityHeal")
                            movement.Pending.Remove(ReadPacketLong(packet, "TargetID"));
                        playerQueue.Enqueue(new StampedBroadcast(packet, Stopwatch.GetTimestamp()));
                    }
                }
            }
            else
            {
                playerQueue.Enqueue(new StampedBroadcast(packet, Stopwatch.GetTimestamp()));
            }
        }
        else
        {
            EnqueueOrdinaryBroadcast(connectionId, queue, packet);
        }
        if (_queueSignals.TryGetValue(connectionId, out var signal))
            signal.Writer.TryWrite(1);
    }

    private void EnqueueOrdinaryBroadcast(
        Guid connectionId,
        ConcurrentQueue<object> queue,
        object packet)
    {
        if (!_worldMovementStates.TryGetValue(connectionId, out var movement))
        {
            queue.Enqueue(packet);
            return;
        }

        lock (movement.Gate)
        {
            var type = ReadPacketString(packet, "Type");
            var entityId = ReadPacketLong(packet, "EntityID");
            var speedTime = ReadPacketDouble(packet, "SpeedTime");

            // Player movement is already handled by the urgent lane. World
            // actor IDs use the ordinary lane and may replace only another
            // still-unsent movement waypoint for the exact same actor.
            // SpeedTime <= 0.01 is an authoritative correction used around
            // combat and must remain an exact FIFO boundary, never a visual
            // waypoint that can be replaced by a later movement.
            if (type == "MoveEntity" && entityId >= 1_000_000 &&
                speedTime is > 0.01 and <= 1.5)
            {
                if (movement.Pending.TryGetValue(entityId, out var slot))
                {
                    var nextSeconds = ReadMovementServerSpan(packet);
                    var previousSeconds = slot.TotalServerSeconds;
                    slot.TotalServerSeconds =
                        previousSeconds > 0 && nextSeconds > 0
                            ? Math.Min(8.0, previousSeconds + nextSeconds)
                            : 0;
                    var retainedSeconds = Math.Max(
                        0.0, slot.TotalServerSeconds - previousSeconds);
                    var nextMoveSpeed = ReadPacketDouble(packet, "MoveSpeed");
                    if (slot.TotalPathDistance > 0 &&
                        nextMoveSpeed > 0 &&
                        retainedSeconds > 0)
                    {
                        slot.TotalPathDistance +=
                            nextMoveSpeed * retainedSeconds;
                    }
                    else if (slot.TotalServerSeconds < 8.0)
                    {
                        slot.TotalPathDistance = 0;
                    }
                    slot.MoveCount++;
                    slot.LatestPacket = packet;
                }
                else
                {
                    slot = new PendingWorldMove(entityId, packet);
                    movement.Pending.Add(entityId, slot);
                    queue.Enqueue(slot);
                }
                return;
            }

            // Any event touching a world actor is a causal boundary. Leave
            // the old queued movement before this event, but force later
            // movement to allocate a fresh queue slot after the event.
            RemovePendingWorldActor(movement, entityId);
            RemovePendingWorldActor(movement, ReadPacketLong(packet, "TargetID"));
            RemovePendingWorldActor(movement, ReadPacketLong(packet, "AttackerID"));
            RemovePendingWorldActor(movement, ReadPacketLong(packet, "CasterID"));
            queue.Enqueue(new StampedBroadcast(packet, Stopwatch.GetTimestamp()));
        }
    }

    private static void RemovePendingWorldActor(
        WorldMovementState movement,
        long entityId)
    {
        if (entityId >= 1_000_000)
            movement.Pending.Remove(entityId);
    }

    public bool TryDequeueOrdinary(Guid connectionId, out object packet)
    {
        packet = null!;
        if (!_queues.TryGetValue(connectionId, out var queue))
            return false;

        if (!_worldMovementStates.TryGetValue(connectionId, out var movement))
            return queue.TryDequeue(out packet!);

        lock (movement.Gate)
        {
            if (!queue.TryDequeue(out var queued))
                return false;

            if (queued is PendingWorldMove slot)
            {
                if (_queueAgeRecorders.TryGetValue(connectionId, out var recorder))
                    recorder.RecordWorld(slot.EnqueuedAt);
                if (movement.Pending.TryGetValue(slot.EntityId, out var current) &&
                    ReferenceEquals(current, slot))
                    movement.Pending.Remove(slot.EntityId);

                if (slot.MoveCount > 1 && slot.TotalServerSeconds > 0)
                {
                    var newest = slot.LatestPacket;
                    packet = new
                    {
                        Type = "MoveEntity",
                        EntityID = slot.EntityId,
                        X = ReadPacketDouble(newest, "X"),
                        Y = ReadPacketDouble(newest, "Y"),
                        Dir = ReadPacketString(newest, "Dir") ?? "1",
                        // Keep visual catch-up bounded: replaying every old
                        // 250ms waypoint is worse than moving smoothly to the
                        // latest authoritative point. The full span remains
                        // available for diagnostics/future presentation logic.
                        SpeedTime = Math.Clamp(slot.TotalServerSeconds, 0.05, 0.75),
                        SnapshotSpanSeconds = Math.Clamp(slot.TotalServerSeconds, 0.05, 8.0),
                        CoalescedMoves = slot.MoveCount,
                        MoveSpeed = slot.TotalPathDistance > 0 &&
                                    slot.TotalServerSeconds > 0
                            ? slot.TotalPathDistance / slot.TotalServerSeconds
                            : ReadPacketDouble(newest, "MoveSpeed")
                    };
                }
                else
                {
                    packet = slot.LatestPacket;
                }
            }
            else if (queued is StampedBroadcast stamped)
            {
                if (_queueAgeRecorders.TryGetValue(connectionId, out var recorder))
                    recorder.RecordWorld(stamped.EnqueuedAt);
                packet = stamped.Packet;
            }
            else
            {
                packet = queued;
            }

            return true;
        }
    }

    private static bool IsPlayerPriorityPacket(object packet)
    {
        var type = ReadPacketString(packet, "Type");
        return type switch
        {
            "SpawnEntity" or "EntityDeath" =>
                ReadPacketLong(packet, "EntityType") == 0,
            "MoveEntity" or "SyncState" or "DespawnEntity" =>
                ReadPacketLong(packet, "EntityID") is > 0 and < 1_000_000,
            // Player attack results must not wait behind monster/loot AOI
            // traffic even when the victim is a monster (TargetType=1).
            "EntityDamage" =>
                ReadPacketLong(packet, "TargetType") == 0 ||
                ReadPacketLong(packet, "AttackerID") is > 0 and < 1_000_000,
            "EntityHeal" =>
                ReadPacketLong(packet, "TargetType") == 0,
            "SkillCast" =>
                ReadPacketLong(packet, "CasterType") == 0,
            _ => false,
        };
    }

    public bool TryDequeuePlayerPriority(Guid connectionId, out object packet)
    {
        packet = null!;
        if (!_playerPriorityQueues.TryGetValue(connectionId, out var queue))
            return false;

        if (!_playerPriorityMovementStates.TryGetValue(
                connectionId, out var movement))
            return queue.TryDequeue(out packet!);

        lock (movement.Gate)
        {
            if (!queue.TryDequeue(out var queued))
                return false;

            if (queued is PendingPlayerMove slot)
            {
                if (_queueAgeRecorders.TryGetValue(connectionId, out var recorder))
                    recorder.RecordPlayer(slot.EnqueuedAt);
                if (movement.Pending.TryGetValue(slot.EntityId, out var current) &&
                    ReferenceEquals(current, slot))
                    movement.Pending.Remove(slot.EntityId);
                // For coalesced movements preserve the newest destination
                // AND elapsed server time. Otherwise the client computes an
                // exaggerated velocity and an otherwise smooth remote actor
                // looks as if it teleports forward after socket backpressure.
                // Non-coalesced packets retain identity and encryption cache.
                if (slot.MoveCount > 1 && slot.TotalServerSeconds > 0)
                {
                    var newest = slot.LatestPacket;
                    packet = new
                    {
                        Type = "MoveEntity",
                        EntityID = slot.EntityId,
                        X = ReadPacketDouble(newest, "X"),
                        Y = ReadPacketDouble(newest, "Y"),
                        Dir = ReadPacketString(newest, "Dir") ?? "1",
                        // Existing clients retain the original <=750ms field.
                        // New clients can use the full elapsed *server* span
                        // to avoid false speed-up after queued snapshots.
                        SpeedTime = Math.Clamp(slot.TotalServerSeconds, 0.05, 0.75),
                        SnapshotSpanSeconds = Math.Clamp(slot.TotalServerSeconds, 0.05, 8.0),
                        CoalescedMoves = slot.MoveCount
                    };
                }
                else
                {
                    packet = slot.LatestPacket;
                }
            }
            else if (queued is StampedBroadcast stamped)
            {
                if (_queueAgeRecorders.TryGetValue(connectionId, out var recorder))
                    recorder.RecordPlayer(stamped.EnqueuedAt);
                packet = stamped.Packet;
            }
            else
            {
                packet = queued;
            }
            return true;
        }
    }

    public bool HasPendingPlayerPriority(Guid connectionId)
        => _playerPriorityQueues.TryGetValue(connectionId, out var queue) &&
           !queue.IsEmpty;

    public int PlayerPriorityBacklogCount(Guid connectionId)
        => _playerPriorityQueues.TryGetValue(connectionId, out var queue)
            ? queue.Count : 0;

    // Drain per-connection interval metrics at a slow diagnostic cadence.
    // This measures enqueue-to-dequeue, not the transport's SendAsync time.
    public BroadcastQueueAgeReport? TakeQueueAgeReport(Guid connectionId)
        => _queueAgeRecorders.TryGetValue(connectionId, out var recorder)
            ? recorder.Drain()
            : null;

    private readonly ConcurrentDictionary<Guid, Presence>
        _presence = new();
    private readonly ConcurrentDictionary<Guid, double>
        _requestedViewRadii = new();

    // Viewport-only changes are stored without any database/position mutation.
    // Reconciliation is explicitly requested only when the quantized value changes.
    public bool SetViewRadius(Guid connectionId, double radius)
    {
        if (!_presence.ContainsKey(connectionId))
            return false;
        var normalized = Math.Clamp(
            double.IsFinite(radius) ? radius : PlayerSpatialAoi.EnterRadius,
            PlayerSpatialAoi.EnterRadius, PlayerSpatialAoi.MaxViewRadius);
        if (_requestedViewRadii.TryGetValue(connectionId, out var previous) &&
            previous == normalized)
            return false;
        _requestedViewRadii[connectionId] = normalized;
        return true;
    }

    private double GetViewRadius(Guid connectionId)
        => _requestedViewRadii.TryGetValue(connectionId, out var radius)
            ? radius : PlayerSpatialAoi.EnterRadius;

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

    public (Guid ConnectionId, ConcurrentQueue<object> Queue) Register(
        bool prioritizePlayerEvents = false)
    {
        var id = Guid.NewGuid();
        var queue = new ConcurrentQueue<object>();
        if (prioritizePlayerEvents)
        {
            _playerPriorityMovementStates[id] = new PlayerPriorityMovementState();
            _playerPriorityQueues[id] = new ConcurrentQueue<object>();
            _worldMovementStates[id] = new WorldMovementState();
            _queueAgeRecorders[id] = new BroadcastQueueAgeRecorder();
        }
        _queueSignals[id] = Channel.CreateBounded<byte>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });
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
        _playerPriorityQueues.TryRemove(connectionId, out _);
        _playerPriorityMovementStates.TryRemove(connectionId, out _);
        _worldMovementStates.TryRemove(connectionId, out _);
        _queueAgeRecorders.TryRemove(connectionId, out _);
        if (_queueSignals.TryRemove(connectionId, out var signal))
            signal.Writer.TryComplete();
        _presence.TryRemove(connectionId, out _);
        _requestedViewRadii.TryRemove(connectionId, out _);
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
            presence.MapId, presence.X, presence.Y, presence.CharacterName),
            GetViewRadius(connectionId));
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
                presence.CharacterName),
            GetViewRadius(connectionId));
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

    // A scope keeps only in-flight removal history. WorldReady's SELECT
    // and re-index pass must use the same scope so a concurrent pickup
    // cannot be undone by an older SQLite row.
    public WorldEntitySpatialAoi.SnapshotScope BeginWorldEntitySnapshot()
        => _worldAoi.BeginSnapshot();

    public bool TryUpsertWorldEntityFromSnapshot(
        WorldAoiEntity entity,
        WorldEntitySpatialAoi.SnapshotScope snapshot,
        out IReadOnlyList<WorldEntitySpatialAoi.Change> changes)
        => _worldAoi.TryUpsertEntityFromSnapshot(
            entity, snapshot, out changes);

    // Retained for existing non-leased generation checks and probes.
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
        foreach (var pair in _queues)
            EnqueueBroadcast(pair.Key, pair.Value, packet);
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

            EnqueueBroadcast(pair.Key, pair.Value, packet);
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

        EnqueueBroadcast(connectionId, queue, packet);
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

            EnqueueBroadcast(pair.Key, queue, packet);
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

            EnqueueBroadcast(pair.Key, pair.Value, packet);
        }
    }
}
