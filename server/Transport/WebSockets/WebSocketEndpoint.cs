using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SGO.Server.Application;
using SGO.Server.Application.Character;
using SGO.Server.Application.Diagnostics;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.World;
using SGO.Server.Domain.Sessions;
using SGO.Server.Protocol;

namespace SGO.Server.Transport.WebSockets;

public static class WebSocketEndpoint
{
    public static WebApplication MapSgoWebSocket(this WebApplication app)
    {
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30)
        });

        app.Map("/", async (
            HttpContext ctx,
            PacketRouter router,
            ResourceRecoveryService resourceRecovery,
            ServerBroadcastHub broadcasts,
            AccountSessionRegistry sessions,
            SharedMapAiTicker mapAi,
            ServerDiagnostics diagnostics,
            ILoggerFactory loggerFactory) =>
        {
            var log = loggerFactory.CreateLogger("SGO.WebSocket");

            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
            var state = new ClientSession();
            var buffer = new byte[64 * 1024];
            using var message = new MemoryStream();
            using var sendGate = new SemaphoreSlim(1, 1);
            var broadcastRegistration = broadcasts.Register(prioritizePlayerEvents: true);
            var broadcastConnectionId = broadcastRegistration.ConnectionId;
            var broadcastQueue = broadcastRegistration.Queue;
            var broadcastSignal = broadcasts.GetBroadcastSignal(
                broadcastConnectionId);
            var lastBroadcastBacklogWarning = DateTimeOffset.MinValue;
            var lastBroadcastBacklogCheck = DateTimeOffset.MinValue;
            state.ConnectionId = broadcastConnectionId;
            diagnostics.WebSocketConnected();
            diagnostics.Record(
                "Information",
                "WebSocket",
                "Connected",
                state.ConnectionId);
            log.LogInformation(
                "[WS_CONNECTED] Connection={ConnectionID}",
                state.ConnectionId);
            router.SyncPresence(state);
            mapAi.Register(state);
            using var worldTickCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    ctx.RequestAborted);
            var worldTickToken = worldTickCts.Token;

            long lastSlowSendLoggedUtcTicks = 0;
            async Task SendPacketsAsync(
                IReadOnlyList<object> packets,
                CancellationToken cancellationToken,
                bool broadcastPacket = false)
            {
                if (packets.Count == 0 ||
                    socket.State != WebSocketState.Open)
                    return;

                var sendStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                await sendGate.WaitAsync(cancellationToken);
                var sendGateWaitMs = System.Diagnostics.Stopwatch.GetElapsedTime(
                    sendStarted).TotalMilliseconds;
                var encodeMs = 0.0;
                var socketWriteMs = 0.0;
                try
                {
                    foreach (var packet in packets)
                    {
                        var encodingStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                        var output = broadcastPacket
                            ? broadcasts.EncodeBroadcastPacket(packet)
                            : Encoding.UTF8.GetBytes(ClientProtocol.Encode(packet));
                        encodeMs += System.Diagnostics.Stopwatch.GetElapsedTime(
                            encodingStarted).TotalMilliseconds;

                        var writeStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                        await socket.SendAsync(
                            output.AsMemory(),
                            WebSocketMessageType.Text,
                            true,
                            cancellationToken);
                        socketWriteMs += System.Diagnostics.Stopwatch.GetElapsedTime(
                            writeStarted).TotalMilliseconds;
                    }
                }
                finally
                {
                    sendGate.Release();
                }

                var sendMs = System.Diagnostics.Stopwatch.GetElapsedTime(
                    sendStarted).TotalMilliseconds;
                if (sendMs >= 250)
                {
                    // Per-connection rate limit: don't make a busy sender
                    // slower by flooding logs during a sustained backlog.
                    var loggedAt = System.Threading.Interlocked.Read(
                        ref lastSlowSendLoggedUtcTicks);
                    var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
                    if ((loggedAt == 0 ||
                         nowTicks - loggedAt >= TimeSpan.FromSeconds(5).Ticks) &&
                        System.Threading.Interlocked.CompareExchange(
                            ref lastSlowSendLoggedUtcTicks, nowTicks, loggedAt) == loggedAt)
                    {
                        log.LogWarning(
                            "[SLOW_SEND_BATCH] TotalMs={TotalMs:F1} GateWaitMs={GateWaitMs:F1} EncodeMs={EncodeMs:F1} SocketWriteMs={SocketWriteMs:F1} Count={Count} Broadcast={Broadcast} Connection={ConnectionID}",
                            sendMs, sendGateWaitMs, encodeMs, socketWriteMs,
                            packets.Count, broadcastPacket, state.ConnectionId);
                    }
                }
            }

            // Send AOI broadcasts as soon as the hub signals new traffic,
            // independent of the 250 ms world simulation. This avoids idle
            // per-client timers and still prioritizes player updates.
            // A dedicated player queue preserves intra-lane FIFO ordering;
            // non-player traffic stays reliable in its own FIFO queue.
            var broadcastSendTask = Task.Run(async () =>
            {
                try
                {
                    while (!worldTickToken.IsCancellationRequested &&
                           socket.State == WebSocketState.Open &&
                           await broadcastSignal.WaitToReadAsync(worldTickToken))
                    {
                        // One pending signal represents any number of queued
                        // packets; no wakeups at all when an AOI is idle.
                        while (broadcastSignal.TryRead(out _)) { }
                        do
                        {
                            if (broadcastQueue.IsEmpty &&
                                !broadcasts.HasPendingPlayerPriority(broadcastConnectionId))
                                break;

                        // Serve the newest high-priority lane before ordinary
                        // monster and ground-item traffic. Bound the low batch
                        // so a fresh player MoveEntity never waits behind
                        // hundreds of unrelated item SpawnEntity messages.
                        // Short ordinary bursts bound how long fresh player
                        // events wait behind ground/monster sends. Every
                        // packet remains queued, FIFO within each lane;
                        // no FPS, gameplay or network tick is throttled.
                        const int priorityBatch = 32;
                        const int ordinaryBatch = 8;
                        var playerUpdates = new List<object>(priorityBatch);
                        var otherUpdates = new List<object>(ordinaryBatch);
                        for (var i = 0; i < priorityBatch &&
                             broadcasts.TryDequeuePlayerPriority(
                                 broadcastConnectionId, out var urgentPacket); i++)
                        {
                            playerUpdates.Add(urgentPacket);
                        }
                        for (var i = 0; i < ordinaryBatch &&
                             broadcasts.TryDequeueOrdinary(
                                 broadcastConnectionId, out var broadcastPacket); i++)
                        {
                            // World movement snapshots may collapse to the
                            // newest still-unsent waypoint. Combat/state/loot
                            // boundaries remain exact FIFO and are never lost.
                            otherUpdates.Add(broadcastPacket);
                        }

                        // Send all queued updates, never discard gameplay or
                        // ground-item data. The send gate preserves WS thread
                        // safety across request replies, world ticks and AOI.
                        if (playerUpdates.Count > 0)
                            await SendPacketsAsync(playerUpdates, worldTickToken, broadcastPacket: true);
                        if (otherUpdates.Count > 0)
                            await SendPacketsAsync(otherUpdates, worldTickToken, broadcastPacket: true);

                        var now = DateTimeOffset.UtcNow;
                        if ((!broadcastQueue.IsEmpty ||
                             broadcasts.HasPendingPlayerPriority(broadcastConnectionId)) &&
                            now - lastBroadcastBacklogCheck >= TimeSpan.FromSeconds(2))
                        {
                            lastBroadcastBacklogCheck = now;
                            var backlogCount = broadcastQueue.Count +
                                broadcasts.PlayerPriorityBacklogCount(broadcastConnectionId);
                            if (backlogCount >= 500 &&
                                now - lastBroadcastBacklogWarning >= TimeSpan.FromSeconds(10))
                            {
                                lastBroadcastBacklogWarning = now;
                                diagnostics.BroadcastBacklogWarning();
                                diagnostics.Record(
                                    "Warning",
                                    "BroadcastBacklog",
                                    $"Queued={backlogCount}",
                                    state.ConnectionId,
                                    state.AccountId,
                                    state.CharacterId,
                                    state.CurrentMapId);
                                log.LogWarning(
                                    "[BROADCAST_BACKLOG] Queued={Queued} Connection={ConnectionID} Account={AccountID} Character={CharacterID} Map={MapID}",
                                    backlogCount,
                                    state.ConnectionId,
                                    state.AccountId,
                                    state.CharacterId,
                                    state.CurrentMapId);
                            }
                        }
                        }
                        while ((!broadcastQueue.IsEmpty ||
                                broadcasts.HasPendingPlayerPriority(broadcastConnectionId)) &&
                               !worldTickToken.IsCancellationRequested &&
                               socket.State == WebSocketState.Open);
                    }
                }
                catch (OperationCanceledException) { }
                catch (WebSocketException) { }
                catch (Exception ex)
                {
                    diagnostics.PacketError();
                    log.LogError(ex,
                        "[BROADCAST_SEND_FAILED] Connection={ConnectionID}",
                        state.ConnectionId);
                }
            });

            Task? pendingRecoveryTask = null;
            var worldTickTask = Task.Run(async () =>
            {
                try
                {
                    // Keep world ticks on a wall-clock 250 ms cadence.
                    // Task.Delay(250) after processing/sending made the real
                    // interval 250 ms + work time + socket backpressure,
                    // producing irregular monster movement.
                    using var worldTimer =
                        new PeriodicTimer(TimeSpan.FromMilliseconds(250));
                    var lastSlowWorldTickLogAt = DateTimeOffset.MinValue;

                    while (!worldTickToken.IsCancellationRequested &&
                           socket.State == WebSocketState.Open &&
                           await worldTimer.WaitForNextTickAsync(
                               worldTickToken))
                    {
                        var now = DateTimeOffset.UtcNow;
                        IReadOnlyList<object> packets;
                        var shouldPing = false;
                        var heartbeatTimedOut = false;
                        var superseded = false;
                        var tickStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                        var lockStarted = tickStarted;
                        var lockWaitMs = 0.0;
                        var presenceMs = 0.0;
                        var routerTickMs = 0.0;
                        ClientSession? recoverySnapshot = null;

                        lock (state)
                        {
                            lockWaitMs = System.Diagnostics.Stopwatch.GetElapsedTime(
                                lockStarted).TotalMilliseconds;
                            superseded =
                                state.AccountId.HasValue &&
                                !sessions.IsCurrent(
                                    state.AccountId.Value,
                                    state.ConnectionId);

                            if (!superseded)
                            {
                                var presenceStarted =
                                    System.Diagnostics.Stopwatch.GetTimestamp();
                                router.SyncPresence(state);
                                presenceMs = System.Diagnostics.Stopwatch.GetElapsedTime(
                                    presenceStarted).TotalMilliseconds;
                                var routerTickStarted =
                                    System.Diagnostics.Stopwatch.GetTimestamp();
                                packets = router.Tick(
                                    state, now, includeResourceRecovery: false);
                                // Leave the 250ms logical tick, network poll,
                                // and session state lock entirely independent
                                // of potentially 1-5 second SQLite writer waits.
                                // Reserve the normal regen cadence only when
                                // there is no previous recovery task in flight.
                                if (pendingRecoveryTask is null ||
                                    pendingRecoveryTask.IsCompleted)
                                {
                                    recoverySnapshot = resourceRecovery.CaptureDueTick(
                                        state, now);
                                }
                                routerTickMs = System.Diagnostics.Stopwatch.GetElapsedTime(
                                    routerTickStarted).TotalMilliseconds;

                                shouldPing =
                                    ConnectionHeartbeat.ShouldPing(
                                        state.LastPingAt,
                                        now);

                                if (shouldPing)
                                    state.LastPingAt = now;

                                heartbeatTimedOut =
                                    ConnectionHeartbeat.IsTimedOut(
                                        state.LastPongAt,
                                        now);
                            }
                            else
                            {
                                state.CloseRequested = true;
                                packets = Array.Empty<object>();
                            }
                        }

                        if (recoverySnapshot is not null)
                        {
                            var due = recoverySnapshot;
                            pendingRecoveryTask = Task.Run(async () =>
                            {
                                try
                                {
                                    // No access to the mutable live ClientSession
                                    // while reading/writing SQLite; damage and
                                    // skill rules still use the authoritative DB.
                                    bool stillCurrent;
                                    lock (state)
                                    {
                                        stillCurrent = !state.CloseRequested &&
                                            state.AccountId == due.AccountId &&
                                            state.CharacterId == due.CharacterId &&
                                            state.CurrentMapId == due.CurrentMapId &&
                                            sessions.IsCurrent(
                                                due.AccountId!.Value,
                                                state.ConnectionId);
                                    }
                                    if (!stillCurrent || worldTickToken.IsCancellationRequested)
                                        return;

                                    var recovered = resourceRecovery.Tick(due, now);
                                    if (recovered.Count == 0 ||
                                        worldTickToken.IsCancellationRequested)
                                        return;

                                    lock (state)
                                    {
                                        stillCurrent = !state.CloseRequested &&
                                            state.AccountId == due.AccountId &&
                                            state.CharacterId == due.CharacterId &&
                                            state.CurrentMapId == due.CurrentMapId &&
                                            sessions.IsCurrent(
                                                due.AccountId!.Value,
                                                state.ConnectionId);
                                    }
                                    if (stillCurrent && socket.State == WebSocketState.Open)
                                        await SendPacketsAsync(recovered, worldTickToken);
                                }
                                catch (OperationCanceledException) when (
                                    worldTickToken.IsCancellationRequested)
                                {
                                }
                                catch (Exception ex)
                                {
                                    diagnostics.WorldTickFailure();
                                    log.LogError(ex,
                                        "[RESOURCE_RECOVERY_FAILED] Connection={ConnectionID} Character={CharacterID}",
                                        state.ConnectionId, due.CharacterId);
                                }
                            }, worldTickToken);
                        }

                        // One warning per connection per 5s at most.
                        // This only observes the existing 250ms cadence;
                        // network, gameplay and physics are not throttled.
                        var worldTickMs = System.Diagnostics.Stopwatch.GetElapsedTime(
                            tickStarted).TotalMilliseconds;
                        if (worldTickMs > 250 &&
                            now - lastSlowWorldTickLogAt >= TimeSpan.FromSeconds(5))
                        {
                            lastSlowWorldTickLogAt = now;
                            log.LogWarning(
                                "[SLOW_WORLD_TICK] TotalMs={TotalMs:F1} LockWaitMs={LockWaitMs:F1} PresenceMs={PresenceMs:F1} RouterTickMs={RouterTickMs:F1} Connection={ConnectionID} Character={CharacterID} Map={MapID}",
                                worldTickMs,
                                lockWaitMs,
                                presenceMs,
                                routerTickMs,
                                state.ConnectionId,
                                state.CharacterId,
                                state.CurrentMapId);
                        }

                        await SendPacketsAsync(
                            packets,
                            worldTickToken);

                        if (shouldPing)
                        {
                            await SendPacketsAsync(
                                [new { Type = "Ping" }],
                                worldTickToken);
                        }

                        if (superseded || heartbeatTimedOut)
                        {
                            if (heartbeatTimedOut && !superseded)
                            {
                                log.LogWarning(
                                    "Heartbeat timeout. Connection={ConnectionID} Account={AccountID}",
                                    state.ConnectionId,
                                    state.AccountId);
                            }

                            if (socket.State == WebSocketState.Open)
                            {
                                await socket.CloseOutputAsync(
                                    WebSocketCloseStatus.NormalClosure,
                                    superseded
                                        ? "Superseded login"
                                        : "Heartbeat timeout",
                                    CancellationToken.None);
                            }

                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (WebSocketException)
                {
                }
                catch (Exception ex)
                {
                    diagnostics.WorldTickFailure();
                    diagnostics.PacketError();
                    diagnostics.Record(
                        "Error",
                        "WorldTick",
                        ex.GetType().Name,
                        state.ConnectionId,
                        state.AccountId,
                        state.CharacterId,
                        state.CurrentMapId);
                    log.LogError(
                        ex,
                        "[WORLD_TICK_FAILED] Connection={ConnectionID} Account={AccountID} Character={CharacterID} Map={MapID}",
                        state.ConnectionId,
                        state.AccountId,
                        state.CharacterId,
                        state.CurrentMapId);
                }
            });

            try
            {
                while (socket.State == WebSocketState.Open &&
                       !ctx.RequestAborted.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(
                        buffer.AsMemory(),
                        ctx.RequestAborted);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        diagnostics.PacketError();
                        diagnostics.Record(
                            "Warning",
                            "WebSocket",
                            $"Unsupported message type: {result.MessageType}",
                            state.ConnectionId,
                            state.AccountId,
                            state.CharacterId,
                            state.CurrentMapId);
                        log.LogWarning(
                            "[WS_INVALID_MESSAGE_TYPE] Connection={ConnectionID} Type={MessageType}",
                            state.ConnectionId,
                            result.MessageType);
                        await socket.CloseAsync(
                            WebSocketCloseStatus.InvalidMessageType,
                            "Text only",
                            CancellationToken.None);
                        break;
                    }

                    message.Write(buffer, 0, result.Count);

                    if (message.Length > 2_097_152)
                    {
                        diagnostics.PacketError();
                        diagnostics.Record(
                            "Warning",
                            "WebSocket",
                            $"Packet too large: {message.Length} bytes",
                            state.ConnectionId,
                            state.AccountId,
                            state.CharacterId,
                            state.CurrentMapId);
                        log.LogWarning(
                            "[WS_PACKET_TOO_LARGE] Connection={ConnectionID} Bytes={Bytes}",
                            state.ConnectionId,
                            message.Length);
                        await socket.CloseAsync(
                            WebSocketCloseStatus.MessageTooBig,
                            "Packet too large",
                            CancellationToken.None);
                        break;
                    }

                    if (!result.EndOfMessage)
                        continue;

                    var encoded = Encoding.UTF8.GetString(message.ToArray());
                    message.SetLength(0);

                    try
                    {
                        using var packet = ClientProtocol.Decode(encoded);

                        IReadOnlyList<object> responses;
                        var packetType =
                            packet.RootElement.ValueKind == JsonValueKind.Object &&
                            packet.RootElement.TryGetProperty(
                                "Type",
                                out var typeElement) &&
                            typeElement.ValueKind == JsonValueKind.String
                            ? typeElement.GetString() ?? "<empty>"
                            : "<missing>";
                        // Never log decrypted JSON: it can contain passwords
                        // or other credentials. The encoded length is enough
                        // for transport diagnostics without exposing content.
                        var encodedLength = encoded.Length;

                        var packetStarted =
                            System.Diagnostics.Stopwatch.GetTimestamp();
                        var sessionLockWaitMs = 0.0;
                        var handlerMs = 0.0;
                        var syncPresenceMs = 0.0;

                        try
                        {
                            var sessionLockStarted =
                                System.Diagnostics.Stopwatch.GetTimestamp();
                            lock (state)
                            {
                                sessionLockWaitMs =
                                    System.Diagnostics.Stopwatch.GetElapsedTime(
                                            sessionLockStarted)
                                        .TotalMilliseconds;

                                var handlerStarted =
                                    System.Diagnostics.Stopwatch.GetTimestamp();
                                responses = router.Handle(
                                    packet.RootElement,
                                    state);
                                handlerMs =
                                    System.Diagnostics.Stopwatch.GetElapsedTime(
                                            handlerStarted)
                                        .TotalMilliseconds;

                                var syncPresenceStarted =
                                    System.Diagnostics.Stopwatch.GetTimestamp();
                                router.SyncPresence(state);
                                syncPresenceMs =
                                    System.Diagnostics.Stopwatch.GetElapsedTime(
                                            syncPresenceStarted)
                                        .TotalMilliseconds;
                            }

                            diagnostics.PacketHandled();

                            var packetElapsedMs =
                                System.Diagnostics.Stopwatch.GetElapsedTime(
                                        packetStarted)
                                    .TotalMilliseconds;

                            if (packetElapsedMs >= 250)
                            {
                                var accountedMs =
                                    sessionLockWaitMs +
                                    handlerMs +
                                    syncPresenceMs;
                                var otherMs = Math.Max(
                                    0.0,
                                    packetElapsedMs - accountedMs);
                                var timingDetail =
                                    $"Type={packetType} Total={packetElapsedMs:F1}ms " +
                                    $"SessionLockWait={sessionLockWaitMs:F1}ms " +
                                    $"Handler={handlerMs:F1}ms " +
                                    $"SyncPresence={syncPresenceMs:F1}ms " +
                                    $"Other={otherMs:F1}ms EncodedBytes={encodedLength}";

                                diagnostics.SlowPacket();
                                diagnostics.Record(
                                    "Warning",
                                    "SlowPacket",
                                    packetType,
                                    state.ConnectionId,
                                    state.AccountId,
                                    state.CharacterId,
                                    state.CurrentMapId,
                                    packetElapsedMs);
                                diagnostics.Record(
                                    "Warning",
                                    "SlowPacketDetail",
                                    timingDetail,
                                    state.ConnectionId,
                                    state.AccountId,
                                    state.CharacterId,
                                    state.CurrentMapId,
                                    packetElapsedMs);
                                log.LogWarning(
                                    "[SLOW_PACKET] Type={Type} ElapsedMs={ElapsedMs:F1} Connection={ConnectionID} Account={AccountID} Character={CharacterID} Map={MapID}",
                                    packetType,
                                    packetElapsedMs,
                                    state.ConnectionId,
                                    state.AccountId,
                                    state.CharacterId,
                                    state.CurrentMapId);
                                log.LogWarning(
                                    "[SLOW_PACKET_DETAIL] Type={Type} TotalMs={TotalMs:F1} SessionLockWaitMs={SessionLockWaitMs:F1} HandlerMs={HandlerMs:F1} SyncPresenceMs={SyncPresenceMs:F1} OtherMs={OtherMs:F1} EncodedBytes={EncodedBytes}",
                                    packetType,
                                    packetElapsedMs,
                                    sessionLockWaitMs,
                                    handlerMs,
                                    syncPresenceMs,
                                    otherMs,
                                    encodedLength);
                            }
                        }
                        catch (Exception ex)
                        {
                            diagnostics.PacketError();
                            if (ex is Microsoft.Data.Sqlite.SqliteException)
                            {
                                diagnostics.DatabaseFailure();
                                diagnostics.Record(
                                    "Error",
                                    "Database",
                                    $"{packetType}: {ex.GetType().Name}",
                                    state.ConnectionId,
                                    state.AccountId,
                                    state.CharacterId,
                                    state.CurrentMapId);
                            }
                            diagnostics.Record(
                                "Error",
                                "PacketHandler",
                                $"{packetType}: {ex.GetType().Name}",
                                state.ConnectionId,
                                state.AccountId,
                                state.CharacterId,
                                state.CurrentMapId);
                            log.LogError(
                                ex,
                                "[PACKET_HANDLER_FAILED] Type={Type} EncodedLength={EncodedLength} Connection={ConnectionID} Account={AccountID} Character={CharacterID} Map={MapID}",
                                packetType,
                                encodedLength,
                                state.ConnectionId,
                                state.AccountId,
                                state.CharacterId,
                                state.CurrentMapId);
                            await SendPacketsAsync(
                                [
                                    new
                                    {
                                        Type = "PacketError",
                                        RequestType = packetType,
                                        Code = "HANDLER_FAILED",
                                        Message = "伺服器處理操作時發生錯誤"
                                    }
                                ],
                                ctx.RequestAborted);
                            continue;
                        }

                        if (responses.Count == 0 &&
                            packetType is not (
                                "Pong" or
                                "SyncPosition" or
                                "ViewportAoi" or
                                "SyncState"))
                        {
                            log.LogWarning(
                                "[NO_RESPONSE] Type={Type} EncodedLength={EncodedLength}",
                                packetType,
                                encodedLength);
                        }

                        await SendPacketsAsync(
                            responses,
                            ctx.RequestAborted);

                        if (state.CloseRequested)
                        {
                            await socket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "Logout",
                                CancellationToken.None);
                            break;
                        }
                    }
                    catch (Exception e) when (
                        e is FormatException or
                        CryptographicException or
                        JsonException)
                    {
                        diagnostics.PacketError();
                        diagnostics.Record(
                            "Warning",
                            "PacketDecode",
                            e.GetType().Name,
                            state.ConnectionId,
                            state.AccountId,
                            state.CharacterId,
                            state.CurrentMapId);
                        log.LogWarning(
                            "[INVALID_ENCRYPTED_PACKET] Error={Error} Connection={ConnectionID}",
                            e.GetType().Name,
                            state.ConnectionId);

                        await socket.CloseAsync(
                            WebSocketCloseStatus.InvalidPayloadData,
                            "Bad packet",
                            CancellationToken.None);
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException ex)
            {
                diagnostics.Record(
                    "Information",
                    "WebSocket",
                    $"Transport disconnected: {ex.WebSocketErrorCode}",
                    state.ConnectionId,
                    state.AccountId,
                    state.CharacterId,
                    state.CurrentMapId);
                log.LogInformation(
                    "[WS_TRANSPORT_DISCONNECTED] Connection={ConnectionID} Error={Error} Message={Message}",
                    state.ConnectionId,
                    ex.WebSocketErrorCode,
                    ex.Message);
            }
            finally
            {
                diagnostics.WebSocketDisconnected();
                diagnostics.Record(
                    "Information",
                    "WebSocket",
                    "Disconnected",
                    state.ConnectionId,
                    state.AccountId,
                    state.CharacterId,
                    state.CurrentMapId);
                log.LogInformation(
                    "[WS_DISCONNECTED] Connection={ConnectionID} Account={AccountID} Character={CharacterID} Map={MapID}",
                    state.ConnectionId,
                    state.AccountId,
                    state.CharacterId,
                    state.CurrentMapId);

                lock (state)
                {
                    mapAi.Unregister(state);
                    router.DisconnectPresence(state);
                }

                broadcasts.Unregister(broadcastConnectionId);
                worldTickCts.Cancel();

                try
                {
                    await worldTickTask;
                }
                catch
                {
                    // Connection shutdown owns cancellation/close semantics.
                }

                try
                {
                    await broadcastSendTask;
                }
                catch
                {
                    // Broadcast sender shares the connection lifetime.
                }

                // A pending DB writer may already be finishing a transaction.
                // Drain it before the WebSocket send gate is disposed; no
                // recovery packet is sent after worldTickToken cancellation.
                if (pendingRecoveryTask is not null)
                {
                    try { await pendingRecoveryTask; }
                    catch (OperationCanceledException) { }
                }

                if (socket.State == WebSocketState.Open)
                {
                    try
                    {
                        await socket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Bye",
                            CancellationToken.None);
                    }
                    catch (WebSocketException)
                    {
                        // The peer may have already crashed/reset the TCP
                        // connection without a WebSocket close handshake.
                    }
                    catch (IOException)
                    {
                        // Treat transport reset during shutdown as normal.
                    }
                }
            }
        });

        return app;
    }
}
