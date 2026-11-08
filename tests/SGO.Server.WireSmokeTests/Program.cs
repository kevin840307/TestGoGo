using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SGO.Server.Protocol;

// Real encrypted WebSocket contract/E2E smoke. It creates a uniquely named
// throwaway account/character on the isolated test server and never touches an
// existing account.
var url = args.Length > 0
    ? args[0]
    : "ws://127.0.0.1:5678/";

using var socket = new ClientWebSocket();
using (var connectTimeout =
       new CancellationTokenSource(
           TimeSpan.FromSeconds(10)))
{
    await socket.ConnectAsync(
        new Uri(url),
        connectTimeout.Token);
}

var passed = 0;

await Probe(
    new
    {
        Type = "AnnouncementListRequest"
    },
    "AnnouncementListResponse",
    root =>
        root.TryGetProperty(
            "Announcements",
            out var list) &&
        list.ValueKind ==
            JsonValueKind.Array &&
        root.TryGetProperty(
            "Success",
            out var success) &&
        success.ValueKind is
            JsonValueKind.True or
            JsonValueKind.False,
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
    new
    {
        Type = "LoginRequest",
        User = "wire-test"
    },
    "LoginResponse",
    root =>
        IsFailure(root) &&
        root.TryGetProperty(
            "Characters",
            out var characters) &&
        characters.ValueKind ==
            JsonValueKind.Array,
    "malformed login returns its expected response shape");

await Probe(
    new
    {
        Type = "UseSkill",
        SkillID = 1
    },
    "PacketError",
    root =>
        HasCode(
            root,
            "AUTH_REQUIRED"),
    "unauthenticated gameplay packet yields AUTH_REQUIRED");

await Probe(
    new[] { "not-an-object" },
    "PacketError",
    root =>
        HasCode(
            root,
            "INVALID_PACKET"),
    "non-object encrypted JSON yields INVALID_PACKET");

await Probe(
    new
    {
        Type = 42
    },
    "PacketError",
    root =>
        HasCode(
            root,
            "INVALID_PACKET"),
    "non-string encrypted packet Type yields INVALID_PACKET");

// Authenticated end-to-end flow over the exact encrypted WebSocket transport
// used by Godot.
var suffix =
    Guid.NewGuid()
        .ToString("N")[..12];
var loginName =
    "wire-" + suffix;
const string password =
    "WireTest-123";
var characterName =
    "Wire" + suffix[..8];

var registered = await Probe(
    new
    {
        Type = "RegisterAccountRequest",
        LoginName = loginName,
        Password = password,
        EMAIL = $"{loginName}@example.invalid"
    },
    "RegisterAccountResponse",
    root =>
        root.TryGetProperty(
            "Success",
            out var success) &&
        success.ValueKind ==
            JsonValueKind.True,
    "register unique account over encrypted WebSocket");

var login = await Probe(
    new
    {
        Type = "LoginRequest",
        User = loginName,
        Pass = password
    },
    "LoginResponse",
    root =>
        root.TryGetProperty(
            "Success",
            out var success) &&
        success.ValueKind ==
            JsonValueKind.True &&
        root.TryGetProperty(
            "Characters",
            out var characters) &&
        characters.ValueKind ==
            JsonValueKind.Array &&
        characters.GetArrayLength() == 3,
    "login returns authoritative three-slot character list");

var initialCharacters =
    login.GetProperty(
        "Characters");

var accountId =
    initialCharacters[0]
        .GetProperty(
            "ACCID")
        .GetInt64();

await Probe(
    new
    {
        Type = "CreateCharacterRequest",
        ACCID = accountId,
        Name = characterName,
        Race = 1,
        Hair = 1,
        HairColor = 1,
        Password = "1234",
        Str = 5,
        Dex = 5,
        Con = 5,
        Wis = 5
    },
    "CreateCharacterResponse",
    root =>
        root.TryGetProperty(
            "Success",
            out var success) &&
        success.ValueKind ==
            JsonValueKind.True,
    "create character through real packet router");

var relogin = await Probe(
    new
    {
        Type = "LoginRequest",
        User = loginName,
        Pass = password
    },
    "LoginResponse",
    root =>
        root.TryGetProperty(
            "Success",
            out var success) &&
        success.ValueKind ==
            JsonValueKind.True,
    "relogin refreshes created character list");

var createdCharacter =
    relogin.GetProperty(
            "Characters")
        .EnumerateArray()
        .First(character =>
            character.TryGetProperty(
                "NAME",
                out var name) &&
            name.GetString() ==
                characterName);

var dbid =
    createdCharacter.GetProperty(
            "DBID")
        .GetInt64();

var enterWorld = await Probe(
    new
    {
        Type = "SelectCharacterRequest",
        DBID = dbid
    },
    "EnterWorldSuccess",
    root =>
        root.TryGetProperty(
            "DBID",
            out var returnedDbid) &&
        returnedDbid.GetInt64() ==
            dbid &&
        root.TryGetProperty(
            "MapID",
            out var mapId) &&
        mapId.ValueKind ==
            JsonValueKind.Number,
    "select character enters world through real server");

var bootstrapTypes = new HashSet<string>(
    StringComparer.Ordinal)
{
    "InventorySync",
    "EquipSync",
    "PetSync",
    "HorseSync",
    "SkillSync",
    "ClubSync",
    "SyncBuffs",
    "QuestSync",
    "FestivalThemeSync"
};

await CollectTypes(
    bootstrapTypes,
    "character bootstrap sync set");

var currentMapId =
    enterWorld.GetProperty(
            "MapID")
        .GetInt32();

await Probe(
    new
    {
        Type = "WorldReady",
        MapID = currentMapId
    },
    "WorldReadyResponse",
    root =>
        root.TryGetProperty(
            "Success",
            out var success) &&
        success.ValueKind ==
            JsonValueKind.True &&
        root.TryGetProperty(
            "MapID",
            out var mapId) &&
        mapId.GetInt32() ==
            currentMapId,
    "WorldReady completes real map-load handshake");

await Probe(
    new
    {
        Type = "MallAction",
        Action = "OpenMall"
    },
    "MallListResponse",
    root =>
        root.TryGetProperty(
            "Points",
            out _) &&
        root.TryGetProperty(
            "Items",
            out var items) &&
        items.ValueKind ==
            JsonValueKind.Array &&
        root.TryGetProperty(
            "Gifts",
            out var gifts) &&
        gifts.ValueKind ==
            JsonValueKind.Array,
    "authenticated mall list returns points/items/gifts");

await Probe(
    new
    {
        Type = "LogoutRequest",
        IsBackToLogin = true
    },
    "LogoutResponse",
    root =>
        root.TryGetProperty(
            "IsBackToLogin",
            out var back) &&
        back.ValueKind ==
            JsonValueKind.True,
    "safe logout returns acknowledgement before socket close");

if (socket.State ==
    WebSocketState.Open)
{
    using var closeTimeout =
        new CancellationTokenSource(
            TimeSpan.FromSeconds(3));

    await socket.CloseAsync(
        WebSocketCloseStatus.NormalClosure,
        "Wire smoke complete",
        closeTimeout.Token);
}

Console.WriteLine(
    $"PASS: {passed} encrypted WebSocket wire checks.");

async Task<JsonElement> Probe(
    object request,
    string responseType,
    Func<JsonElement, bool> assert,
    string description)
{
    await SendPacket(
        request);

    for (var attempts = 0;
         attempts < 80;
         attempts++)
    {
        using var timeout =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(10));

        var response =
            await ReceivePacket(
                timeout.Token);

        var type =
            response.GetProperty(
                    "Type")
                .GetString();

        if (type == "Ping")
        {
            await SendPacket(
                new
                {
                    Type = "Pong"
                });
            continue;
        }

        if (type != responseType)
        {
            throw new Exception(
                $"{description}: expected {responseType}, got {type}: " +
                response.GetRawText());
        }

        if (!assert(response))
        {
            throw new Exception(
                $"{description}: wrong response fields: " +
                response.GetRawText());
        }

        Console.WriteLine(
            $"PASS: {description}");
        passed++;
        return response;
    }

    throw new Exception(
        $"{description}: no expected packet received");
}

async Task CollectTypes(
    HashSet<string> required,
    string description)
{
    var pending =
        new HashSet<string>(
            required,
            StringComparer.Ordinal);

    for (var attempts = 0;
         attempts < 120 &&
         pending.Count > 0;
         attempts++)
    {
        using var timeout =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(10));

        var response =
            await ReceivePacket(
                timeout.Token);

        var type =
            response.GetProperty(
                    "Type")
                .GetString() ??
            "";

        if (type == "Ping")
        {
            await SendPacket(
                new
                {
                    Type = "Pong"
                });
            continue;
        }

        if (!pending.Remove(type))
        {
            throw new Exception(
                $"{description}: unexpected packet {type}: " +
                response.GetRawText());
        }
    }

    if (pending.Count > 0)
    {
        throw new Exception(
            $"{description}: missing " +
            string.Join(
                ", ",
                pending.OrderBy(x => x)));
    }

    Console.WriteLine(
        $"PASS: {description}");
    passed++;
}

async Task SendPacket(
    object request)
{
    var wire =
        Encoding.UTF8.GetBytes(
            ClientProtocol.Encode(
                request));

    using var timeout =
        new CancellationTokenSource(
            TimeSpan.FromSeconds(10));

    await socket.SendAsync(
        wire.AsMemory(),
        WebSocketMessageType.Text,
        true,
        timeout.Token);
}

async Task<JsonElement> ReceivePacket(
    CancellationToken token)
{
    var buffer =
        new byte[64 * 1024];

    using var message =
        new MemoryStream();

    while (true)
    {
        var received =
            await socket.ReceiveAsync(
                buffer.AsMemory(),
                token);

        if (received.MessageType ==
            WebSocketMessageType.Close)
        {
            throw new Exception(
                "WebSocket closed before expected response: " +
                socket.CloseStatusDescription);
        }

        if (received.MessageType !=
            WebSocketMessageType.Text)
        {
            throw new Exception(
                $"Unexpected WebSocket message: {received.MessageType}");
        }

        message.Write(
            buffer,
            0,
            received.Count);

        if (message.Length >
            2_097_152)
        {
            throw new Exception(
                "Received packet exceeds the 2MiB limit");
        }

        if (!received.EndOfMessage)
            continue;

        using var decoded =
            ClientProtocol.Decode(
                Encoding.UTF8.GetString(
                    message.ToArray()));

        return decoded.RootElement.Clone();
    }
}

static bool HasCode(
    JsonElement root,
    string expected) =>
    root.TryGetProperty(
        "Code",
        out var code) &&
    code.ValueKind ==
        JsonValueKind.String &&
    code.GetString() ==
        expected &&
    root.TryGetProperty(
        "RequestType",
        out var requestType) &&
    requestType.ValueKind ==
        JsonValueKind.String &&
    root.TryGetProperty(
        "Message",
        out var message) &&
    message.ValueKind ==
        JsonValueKind.String &&
    !string.IsNullOrWhiteSpace(
        message.GetString());

static bool IsFailure(
    JsonElement root) =>
    root.TryGetProperty(
        "Success",
        out var success) &&
    success.ValueKind ==
        JsonValueKind.False &&
    root.TryGetProperty(
        "Message",
        out var message) &&
    message.ValueKind ==
        JsonValueKind.String &&
    !string.IsNullOrWhiteSpace(
        message.GetString());
