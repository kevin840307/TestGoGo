using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SGO.Server.Protocol;

// Nondestructive transport smoke test. Start SGO.Server separately, then run:
// dotnet run --project tests/SGO.Server.WireSmokeTests -- ws://127.0.0.1:5678/
var url = args.Length > 0 ? args[0] : "ws://127.0.0.1:5678/";
using var socket = new ClientWebSocket();
using (var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
    await socket.ConnectAsync(new Uri(url), connectTimeout.Token);

var passed = 0;
await Probe(
    new { Type = "AnnouncementListRequest" },
    "AnnouncementListResponse",
    root => root.TryGetProperty("Announcements", out var list) &&
            list.ValueKind == JsonValueKind.Array &&
            root.TryGetProperty("Success", out var success) &&
            success.ValueKind is JsonValueKind.True or JsonValueKind.False,
    "encrypted announcement request/response");

await Probe(
    new
    {
        Type = "RegisterAccountRequest",
        LoginName = "wire-test",
        Password = "test",
        EMAIL = 123
    },
    "RegisterAccountResponse",
    root => IsFailure(root),
    "malformed registration responds without creating an account");

await Probe(
    new { Type = "LoginRequest", User = "wire-test" },
    "LoginResponse",
    root => IsFailure(root) &&
            root.TryGetProperty("Characters", out var characters) &&
            characters.ValueKind == JsonValueKind.Array,
    "malformed login returns its expected response shape");

await Probe(
    new { Type = "UseSkill", SkillID = 1 },
    "PacketError",
    root => HasCode(root, "AUTH_REQUIRED"),
    "unauthenticated gameplay packet yields AUTH_REQUIRED");

await Probe(
    new[] { "not-an-object" },
    "PacketError",
    root => HasCode(root, "INVALID_PACKET"),
    "non-object encrypted JSON yields INVALID_PACKET");

await Probe(
    new { Type = 42 },
    "PacketError",
    root => HasCode(root, "INVALID_PACKET"),
    "non-string encrypted packet Type yields INVALID_PACKET");

if (socket.State == WebSocketState.Open)
{
    using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await socket.CloseAsync(
        WebSocketCloseStatus.NormalClosure, "Wire smoke complete",
        closeTimeout.Token);
}

Console.WriteLine($"PASS: {passed} encrypted WebSocket wire checks.");

async Task Probe(
    object request,
    string responseType,
    Func<JsonElement, bool> assert,
    string description)
{
    var wire = Encoding.UTF8.GetBytes(ClientProtocol.Encode(request));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await socket.SendAsync(
        wire.AsMemory(), WebSocketMessageType.Text, true, timeout.Token);

    for (var attempts = 0; attempts < 20; attempts++)
    {
        var response = await ReceivePacket(timeout.Token);
        var type = response.GetProperty("Type").GetString();
        if (type == "Ping")
        {
            var pong = Encoding.UTF8.GetBytes(
                ClientProtocol.Encode(new { Type = "Pong" }));
            await socket.SendAsync(
                pong.AsMemory(), WebSocketMessageType.Text, true,
                timeout.Token);
            continue;
        }

        if (type != responseType)
            throw new Exception(
                $"{description}: expected {responseType}, got {type}");

        if (!assert(response))
            throw new Exception(
                $"{description}: wrong response fields: {response.GetRawText()}");

        Console.WriteLine($"PASS: {description}");
        passed++;
        return;
    }

    throw new Exception($"{description}: no expected packet received");
}

async Task<JsonElement> ReceivePacket(CancellationToken token)
{
    var buffer = new byte[8192];
    using var message = new MemoryStream();
    while (true)
    {
        var received = await socket.ReceiveAsync(
            buffer.AsMemory(), token);
        if (received.MessageType != WebSocketMessageType.Text)
            throw new Exception(
                $"Unexpected WebSocket message: {received.MessageType}");

        message.Write(buffer, 0, received.Count);
        if (message.Length > 2_097_152)
            throw new Exception("Received packet exceeds the 2MiB limit");
        if (!received.EndOfMessage)
            continue;

        using var decoded = ClientProtocol.Decode(
            Encoding.UTF8.GetString(message.ToArray()));
        return decoded.RootElement.Clone();
    }
}

static bool HasCode(JsonElement root, string expected) =>
    root.TryGetProperty("Code", out var code) &&
    code.ValueKind == JsonValueKind.String &&
    code.GetString() == expected &&
    root.TryGetProperty("RequestType", out var requestType) &&
    requestType.ValueKind == JsonValueKind.String &&
    root.TryGetProperty("Message", out var message) &&
    message.ValueKind == JsonValueKind.String &&
    !string.IsNullOrWhiteSpace(message.GetString());

static bool IsFailure(JsonElement root) =>
    root.TryGetProperty("Success", out var success) &&
    success.ValueKind == JsonValueKind.False &&
    root.TryGetProperty("Message", out var message) &&
    message.ValueKind == JsonValueKind.String &&
    !string.IsNullOrWhiteSpace(message.GetString());
