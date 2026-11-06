using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SGO.Server.Application;
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
                        var responses = router.Handle(packet.RootElement, state);

                        foreach (var response in responses)
                        {
                            var output = Encoding.UTF8.GetBytes(
                                ClientProtocol.Encode(response));

                            await socket.SendAsync(
                                output.AsMemory(),
                                WebSocketMessageType.Text,
                                true,
                                ctx.RequestAborted);
                        }

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
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Bye",
                        CancellationToken.None);
                }
            }
        });

        return app;
    }
}
