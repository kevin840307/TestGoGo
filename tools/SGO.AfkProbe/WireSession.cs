using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SGO.Server.Protocol;

namespace Sgo.AfkProbe;

/// <summary>
/// One WebSocket reader and one writer. Nothing is throttled during AFK.
/// This client is not safe to launch against an account currently in Godot.
/// </summary>
internal sealed class WireSession : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly byte[] _receiveBuffer = new byte[64 * 1024];
    internal AfkState State { get; } = new();
    internal long ReceivedBytes { get; private set; }
    internal long SentBytes { get; private set; }

    internal async Task ConnectAsync(Uri endpoint, CancellationToken ct)
        => await _socket.ConnectAsync(endpoint, ct);

    internal async Task SendAsync(object packet, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(ClientProtocol.Encode(packet));
        await _socket.SendAsync(
            bytes.AsMemory(), WebSocketMessageType.Text, true, ct);
        SentBytes += bytes.Length;
    }

    internal async Task<JsonElement> ReceiveAsync(CancellationToken ct)
    {
        using var message = new MemoryStream();
        while (true)
        {
            var frame = await _socket.ReceiveAsync(_receiveBuffer.AsMemory(), ct);
            if (frame.MessageType == WebSocketMessageType.Close)
                throw new EndOfStreamException("Server closed WebSocket");
            if (frame.MessageType != WebSocketMessageType.Text)
                throw new InvalidDataException("Non-text WebSocket frame");
            message.Write(_receiveBuffer, 0, frame.Count);
            if (message.Length > 2_097_152)
                throw new InvalidDataException("Encrypted message exceeded 2 MiB");
            if (frame.EndOfMessage) break;
        }

        ReceivedBytes += message.Length;
        using var decoded = ClientProtocol.Decode(
            Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
        var packet = decoded.RootElement.Clone();
        State.Apply(packet);
        if (AfkState.ReadString(packet, "Type") == "Ping")
            await SendAsync(new { Type = "Pong" }, ct);
        return packet;
    }

    internal async Task<JsonElement> RequestAsync(
        object request, string expectedType, TimeSpan timeout, CancellationToken ct)
    {
        await SendAsync(request, ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        while (true)
        {
            var packet = await ReceiveAsync(deadline.Token);
            var type = AfkState.ReadString(packet, "Type");
            if (type == "PacketError")
                throw new InvalidOperationException(
                    "Server refused request: " + AfkState.ReadString(packet, "Code", "UNKNOWN"));
            if (type == expectedType)
                return packet;
        }
    }

    internal async Task EnterWorldAsync(
        string login, string password, long? characterId,
        bool disposableTestAccount, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(25);
        if (disposableTestAccount)
        {
            var registration = await RequestAsync(
                new
                {
                    Type = "RegisterAccountRequest",
                    LoginName = login,
                    Password = password,
                    EMAIL = login + "@example.invalid"
                }, "RegisterAccountResponse", timeout, ct);
            RequireSuccess(registration, "RegisterAccount");
        }

        var loggedIn = await RequestAsync(
            new { Type = "LoginRequest", User = login, Pass = password },
            "LoginResponse", timeout, ct);
        RequireSuccess(loggedIn, "Login");

        if (disposableTestAccount)
        {
            var accountId = loggedIn.GetProperty("Characters")[0]
                .GetProperty("ACCID").GetInt64();
            var characterName = "AP" + Guid.NewGuid().ToString("N")[..9];
            var created = await RequestAsync(new
            {
                Type = "CreateCharacterRequest",
                ACCID = accountId,
                Name = characterName,
                Race = 0,
                Hair = 1,
                HairColor = 1,
                Password = "1234",
                Str = 5,
                Dex = 5,
                Con = 5,
                Wis = 5
            }, "CreateCharacterResponse", timeout, ct);
            RequireSuccess(created, "CreateCharacter");

            loggedIn = await RequestAsync(
                new { Type = "LoginRequest", User = login, Pass = password },
                "LoginResponse", timeout, ct);
            RequireSuccess(loggedIn, "Relogin");
            characterId = loggedIn.GetProperty("Characters")
                .EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object)
                .Select(x => AfkState.ReadLong(x, "DBID"))
                .First(id => id > 0);
        }

        if (!characterId.HasValue || characterId.Value <= 0)
            throw new ArgumentException("SelectCharacter needs a valid character ID");
        var entered = await RequestAsync(
            new { Type = "SelectCharacterRequest", DBID = characterId.Value },
            "EnterWorldSuccess", timeout, ct);
        State.Apply(entered);
        if (State.MapId <= 0)
            throw new InvalidDataException("EnterWorldSuccess lacked MapID");
        var ready = await RequestAsync(
            new { Type = "WorldReady", MapID = State.MapId },
            "WorldReadyResponse", timeout, ct);
        RequireSuccess(ready, "WorldReady");
    }

    internal async Task EnterDisposableTestMapAsync(
        int map, CancellationToken ct)
    {
        if (map == State.MapId) return;
        // Allowed only for the auto-generated CI/local throwaway account;
        // no map/position changes are initiated for real user sessions.
        await SendAsync(new
        {
            Type = "ChatMessage",
            Channel = 0,
            BubbleType = 0,
            Message = "/gm map " + map + " 4331 5346"
        }, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        while (true)
        {
            var packet = await ReceiveAsync(timeout.Token);
            var type = AfkState.ReadString(packet, "Type");
            if (type == "ChangeMap")
            {
                if (State.MapId != map)
                    throw new InvalidDataException("Unexpected GM test-map destination");
                break;
            }
            if (type == "PacketError")
                throw new InvalidOperationException(
                    "Server rejected disposable test map switch");
        }
        var ready = await RequestAsync(
            new { Type = "WorldReady", MapID = State.MapId },
            "WorldReadyResponse", TimeSpan.FromSeconds(25), ct);
        RequireSuccess(ready, "Test Map WorldReady");
    }

    internal Task PrepareDisposableAutoSaintAsync(CancellationToken ct)
        // No credentials, user accounts or production DB can reach this path:
        // the caller only enables it for --smoke-account on localhost.
        => SendAsync(new
        {
            Type = "ChatMessage",
            Channel = 0,
            BubbleType = 0,
            Message = "/gm skill all 1"
        }, ct);

    internal async Task<bool> EnableAutoSaintAsync(
        int mpThreshold, int spThreshold, CancellationToken ct)
    {
        var reply = await RequestAsync(new
        {
            Type = "ToggleAutoSaint",
            IsOn = true,
            MpThreshold = mpThreshold,
            SpThreshold = spThreshold
        }, "ToggleAutoSaintResponse", TimeSpan.FromSeconds(25), ct);
        return AfkState.ReadBool(reply, "IsOn");
    }

    internal async Task RunAsync(
        TimeSpan duration, ProcessMeter meter, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(duration);
        var sampleTask = SampleUntilCancelledAsync(meter, timeout.Token);
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                JsonElement packet;
                try
                {
                    packet = await ReceiveAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (
                    timeout.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    break;
                }

                if (AfkState.ReadString(packet, "Type") == "ChangeMap" &&
                    State.MapId > 0)
                    await SendAsync(
                        new { Type = "WorldReady", MapID = State.MapId },
                        timeout.Token);
            }
        }
        finally
        {
            timeout.Cancel();
            try { await sampleTask; }
            catch (OperationCanceledException) { }
            meter.Sample();
        }
    }

    private static async Task SampleUntilCancelledAsync(
        ProcessMeter meter, CancellationToken ct)
    {
        meter.Sample();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct))
            meter.Sample();
    }

    private static void RequireSuccess(JsonElement packet, string operation)
    {
        if (!AfkState.ReadBool(packet, "Success"))
            throw new InvalidOperationException(operation + " rejected by server");
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                using var close = new CancellationTokenSource(
                    TimeSpan.FromSeconds(2));
                await SendAsync(
                    new { Type = "LogoutRequest", IsBackToLogin = true },
                    close.Token);
            }
            catch (Exception) { /* remote disconnect is valid */ }
        }
        _socket.Dispose();
    }
}
