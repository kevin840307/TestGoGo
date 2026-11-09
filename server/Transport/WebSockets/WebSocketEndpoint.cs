using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SGO.Server.Application;
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
            var broadcastRegistration = broadcasts.Register();
            var broadcastConnectionId = broadcastRegistration.ConnectionId;
            var broadcastQueue = broadcastRegistration.Queue;
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

            async Task SendPacketsAsync(
                IReadOnlyList<object> packets,
                CancellationToken cancellationToken)
            {
                if (packets.Count == 0 ||
                    socket.State != WebSocketState.Open)
                    return;

                await sendGate.WaitAsync(cancellationToken);
                try
                {
                    foreach (var packet in packets)
                    {
                        var output = Encoding.UTF8.GetBytes(
                            ClientProtocol.Encode(packet));

                        await socket.SendAsync(
                            output.AsMemory(),
                            WebSocketMessageType.Text,
                            true,
                            cancellationToken);
                    }
                }
                finally
                {
                    sendGate.Release();
                }
            }

            // Keep per-client AOI broadcasts independent of the 250 ms world
            // simulation. High-priority player updates cannot be held behind
            // a burst of ground-item scene creation/monster snapshots.
            // The underlying ConcurrentQueue remains FIFO; only unrelated
            // player events are promoted within a bounded send batch.
            static int BroadcastPriority(object packet)
            {
                var type = packet.GetType();
                var kind = type.GetProperty("Type")?.GetValue(packet) as string;
                if (kind is not (
                    "MoveEntity" or "SpawnEntity" or "DespawnEntity"
                    or "SyncState" or "EntityDamage" or "EntityHeal"))
                    return 1;

                if (kind == "SpawnEntity")
                {
                    var entityType = type.GetProperty("EntityType")?.GetValue(packet);
                    return Convert.ToInt32(entityType ?? -1) == 0 ? 0 : 1;
                }

                if (kind is "MoveEntity" or "DespawnEntity" or "SyncState")
                {
                    var entity = type.GetProperty("EntityID")?.GetValue(packet);
                    var entityId = Convert.ToInt64(entity ?? -1L);
                    return entityId > 0 && entityId < 1_000_000 ? 0 : 1;
                }

                var targetType = type.GetProperty("TargetType")?.GetValue(packet);
                return Convert.ToInt32(targetType ?? -1) == 0 ? 0 : 1;
            }

            var broadcastSendTask = Task.Run(async () =>
            {
                try
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
                    while (!worldTickToken.IsCancellationRequested &&
                           socket.State == WebSocketState.Open &&
                           await timer.WaitForNextTickAsync(worldTickToken))
                    {
                        if (broadcastQueue.IsEmpty)
                            continue;

                        const int maxBatch = 256;
                        var playerUpdates = new List<object>(32);
                        var otherUpdates = new List<object>(maxBatch);
                        for (var i = 0; i < maxBatch &&
                             broadcastQueue.TryDequeue(out var broadcastPacket); i++)
                        {
                            if (BroadcastPriority(broadcastPacket) == 0)
                                playerUpdates.Add(broadcastPacket);
                            else
                                otherUpdates.Add(broadcastPacket);
                        }

                        // Send all queued updates, never discard gameplay or
                        // ground-item data. The send gate preserves WS thread
                        // safety across request replies, world ticks and AOI.
                        if (playerUpdates.Count > 0)
                            await SendPacketsAsync(playerUpdates, worldTickToken);
                        if (otherUpdates.Count > 0)
                            await SendPacketsAsync(otherUpdates, worldTickToken);

                        var now = DateTimeOffset.UtcNow;
                        if (!broadcastQueue.IsEmpty &&
                            now - lastBroadcastBacklogCheck >= TimeSpan.FromSeconds(2))
                        {
                            lastBroadcastBacklogCheck = now;
                            var backlogCount = broadcastQueue.Count;
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

                        lock (state)
                        {
                            superseded =
                                state.AccountId.HasValue &&
                                !sessions.IsCurrent(
                                    state.AccountId.Value,
                                    state.ConnectionId);

                            if (!superseded)
                            {
                                router.SyncPresence(state);
                                packets = router.Tick(
                                    state,
                                    now);

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
