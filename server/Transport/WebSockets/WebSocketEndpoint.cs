using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SGO.Server.Application;
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
            state.ConnectionId = broadcastConnectionId;
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

                        if (!broadcastQueue.IsEmpty)
                        {
                            var broadcastPackets = new List<object>();
                            while (broadcastQueue.TryDequeue(out var packet))
                                broadcastPackets.Add(packet);

                            await SendPacketsAsync(
                                broadcastPackets,
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
                    log.LogError(
                        ex,
                        "World tick failed");
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
                        await socket.CloseAsync(
                            WebSocketCloseStatus.InvalidMessageType,
                            "Text only",
                            CancellationToken.None);
                        break;
                    }

                    message.Write(buffer, 0, result.Count);

                    if (message.Length > 2_097_152)
                    {
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
                            packet.RootElement.TryGetProperty(
                                "Type",
                                out var typeElement)
                            ? typeElement.GetString() ?? "<empty>"
                            : "<missing>";
                        var rawPayload =
                            packet.RootElement.GetRawText();

                        try
                        {
                            lock (state)
                            {
                                responses = router.Handle(
                                    packet.RootElement,
                                    state);

                                router.SyncPresence(state);
                            }
                        }
                        catch (Exception ex)
                        {
                            log.LogError(
                                ex,
                                "[PACKET_HANDLER_FAILED] Type={Type} Payload={Payload}",
                                packetType,
                                rawPayload.Length <= 1200
                                    ? rawPayload
                                    : rawPayload[..1200] + "...<truncated>");
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
                                "[NO_RESPONSE] Type={Type} Payload={Payload}",
                                packetType,
                                rawPayload.Length <= 1200
                                    ? rawPayload
                                    : rawPayload[..1200] + "...<truncated>");
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
                        log.LogWarning(
                            "Rejected invalid encrypted packet: {Error}",
                            e.GetType().Name);

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
                log.LogInformation(
                    "WebSocket disconnected: {Message}",
                    ex.Message);
            }
            finally
            {
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
