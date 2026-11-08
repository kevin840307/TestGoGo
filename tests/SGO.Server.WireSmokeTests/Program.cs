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

// Two real encrypted clients exercise cross-player packet delivery and
// server-side party/trade state transitions on the same map.
using var peerSocket = new ClientWebSocket();
using (var connectTimeout =
       new CancellationTokenSource(
           TimeSpan.FromSeconds(10)))
{
    await peerSocket.ConnectAsync(
        new Uri(url),
        connectTimeout.Token);
}

var peerSuffix =
    Guid.NewGuid()
        .ToString("N")[..12];
var peerLoginName =
    "wire-peer-" + peerSuffix;
var peerCharacterName =
    "Peer" + peerSuffix[..8];

await ProbeOn(
    peerSocket,
    new
    {
        Type = "RegisterAccountRequest",
        LoginName = peerLoginName,
        Password = password,
        EMAIL = $"{peerLoginName}@example.invalid"
    },
    "RegisterAccountResponse",
    root =>
        root.TryGetProperty(
            "Success",
            out var success) &&
        success.ValueKind ==
            JsonValueKind.True,
    "peer registers over encrypted WebSocket");

var peerLogin = await ProbeOn(
    peerSocket,
    new
    {
        Type = "LoginRequest",
        User = peerLoginName,
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
    "peer login returns three-slot character list");

var peerAccountId =
    peerLogin.GetProperty(
            "Characters")[0]
        .GetProperty(
            "ACCID")
        .GetInt64();

await ProbeOn(
    peerSocket,
    new
    {
        Type = "CreateCharacterRequest",
        ACCID = peerAccountId,
        Name = peerCharacterName,
        Race = 0,
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
    "peer creates character through real packet router");

var peerRelogin = await ProbeOn(
    peerSocket,
    new
    {
        Type = "LoginRequest",
        User = peerLoginName,
        Pass = password
    },
    "LoginResponse",
    root =>
        root.TryGetProperty(
            "Success",
            out var success) &&
        success.ValueKind ==
            JsonValueKind.True,
    "peer relogin refreshes character list");

var peerCharacter =
    peerRelogin.GetProperty(
            "Characters")
        .EnumerateArray()
        .First(character =>
            character.TryGetProperty(
                "NAME",
                out var name) &&
            name.GetString() ==
                peerCharacterName);

var peerDbid =
    peerCharacter.GetProperty(
            "DBID")
        .GetInt64();

var peerEnter = await ProbeOn(
    peerSocket,
    new
    {
        Type = "SelectCharacterRequest",
        DBID = peerDbid
    },
    "EnterWorldSuccess",
    root =>
        root.TryGetProperty(
            "DBID",
            out var returnedDbid) &&
        returnedDbid.GetInt64() ==
            peerDbid,
    "peer selects character and enters world");

await CollectTypesOn(
    peerSocket,
    new HashSet<string>(
        bootstrapTypes,
        StringComparer.Ordinal),
    "peer character bootstrap sync set");

var peerMapId =
    peerEnter.GetProperty(
            "MapID")
        .GetInt32();

if (peerMapId != currentMapId)
{
    throw new Exception(
        $"multiplayer wire smoke requires both new characters on the same map; first={currentMapId}, peer={peerMapId}");
}

await ProbeOn(
    peerSocket,
    new
    {
        Type = "WorldReady",
        MapID = peerMapId
    },
    "WorldReadyResponse",
    root =>
        root.TryGetProperty(
            "Success",
            out var success) &&
        success.ValueKind ==
            JsonValueKind.True,
    "peer WorldReady completes map-load handshake",
    allowInterleaved: true);

// Party invite/accept over two live sockets.
await SendPacket(
    socket,
    new
    {
        Type = "PartyAction",
        Action = "Invite",
        TargetDBID = peerDbid
    });

await AwaitType(
    peerSocket,
    "PartyInvite",
    root =>
        root.TryGetProperty(
            "InviterDBID",
            out var inviter) &&
        inviter.GetInt64() ==
            dbid,
    "peer receives party invite from first player");

await SendPacket(
    peerSocket,
    new
    {
        Type = "PartyAction",
        Action = "Accept",
        TargetDBID = dbid
    });

await AwaitType(
    socket,
    "PartySync",
    root =>
        root.TryGetProperty(
            "Members",
            out var members) &&
        members.ValueKind ==
            JsonValueKind.Array &&
        members.GetArrayLength() == 2,
    "first player receives two-member PartySync");

await AwaitType(
    peerSocket,
    "PartySync",
    root =>
        root.TryGetProperty(
            "Members",
            out var members) &&
        members.ValueKind ==
            JsonValueKind.Array &&
        members.GetArrayLength() == 2,
    "peer receives two-member PartySync");

// Empty trade still exercises invite, shared session, locks and atomic final
// confirmation without requiring test-only item seeding.
await SendPacket(
    socket,
    new
    {
        Type = "TradeRequest",
        TargetEntityID = peerDbid
    });

await AwaitType(
    peerSocket,
    "TradeInvite",
    root =>
        root.TryGetProperty(
            "RequesterDBID",
            out var requester) &&
        requester.GetInt64() ==
            dbid,
    "peer receives trade invite from first player");

await SendPacket(
    peerSocket,
    new
    {
        Type = "TradeReply",
        RequesterDBID = dbid,
        Accept = true
    });

await AwaitType(
    socket,
    "TradeStart",
    root =>
        root.TryGetProperty(
            "SessionID",
            out var sessionId) &&
        !string.IsNullOrWhiteSpace(
            sessionId.GetString()),
    "first player enters shared trade session");

await AwaitType(
    peerSocket,
    "TradeStart",
    root =>
        root.TryGetProperty(
            "SessionID",
            out var sessionId) &&
        !string.IsNullOrWhiteSpace(
            sessionId.GetString()),
    "peer enters shared trade session");

await SendPacket(
    socket,
    new
    {
        Type = "TradeLock"
    });
await SendPacket(
    peerSocket,
    new
    {
        Type = "TradeLock"
    });

await AwaitType(
    socket,
    "TradeFinalConfirmReq",
    _ => true,
    "first player receives final trade confirmation request");
await AwaitType(
    peerSocket,
    "TradeFinalConfirmReq",
    _ => true,
    "peer receives final trade confirmation request");

await SendPacket(
    socket,
    new
    {
        Type = "TradeFinalConfirm"
    });
await SendPacket(
    peerSocket,
    new
    {
        Type = "TradeFinalConfirm"
    });

await AwaitType(
    socket,
    "TradeSuccess",
    root =>
        root.TryGetProperty(
            "Message",
            out var message) &&
        !string.IsNullOrWhiteSpace(
            message.GetString()),
    "first player receives one successful trade completion");
await AwaitType(
    peerSocket,
    "TradeSuccess",
    root =>
        root.TryGetProperty(
            "Message",
            out var message) &&
        !string.IsNullOrWhiteSpace(
            message.GetString()),
    "peer receives one successful trade completion");

await ProbeOn(
    peerSocket,
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
    "peer safe logout acknowledges before close",
    allowInterleaved: true);

if (peerSocket.State ==
    WebSocketState.Open)
{
    using var peerCloseTimeout =
        new CancellationTokenSource(
            TimeSpan.FromSeconds(3));

    await peerSocket.CloseAsync(
        WebSocketCloseStatus.NormalClosure,
        "Peer wire smoke complete",
        peerCloseTimeout.Token);
}

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
    => await ProbeOn(
        socket,
        request,
        responseType,
        assert,
        description);

async Task<JsonElement> ProbeOn(
    ClientWebSocket targetSocket,
    object request,
    string responseType,
    Func<JsonElement, bool> assert,
    string description,
    bool allowInterleaved = false)
{
    await SendPacket(
        targetSocket,
        request);

    for (var attempts = 0;
         attempts < 120;
         attempts++)
    {
        using var timeout =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(10));

        var response =
            await ReceivePacket(
                targetSocket,
                timeout.Token);

        var type =
            response.GetProperty(
                    "Type")
                .GetString();

        if (type == "Ping")
        {
            await SendPacket(
                targetSocket,
                new
                {
                    Type = "Pong"
                });
            continue;
        }

        if (type != responseType)
        {
            if (allowInterleaved)
                continue;

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

async Task<JsonElement> AwaitType(
    ClientWebSocket targetSocket,
    string responseType,
    Func<JsonElement, bool> assert,
    string description)
{
    for (var attempts = 0;
         attempts < 160;
         attempts++)
    {
        using var timeout =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(10));

        var response =
            await ReceivePacket(
                targetSocket,
                timeout.Token);

        var type =
            response.GetProperty(
                    "Type")
                .GetString();

        if (type == "Ping")
        {
            await SendPacket(
                targetSocket,
                new
                {
                    Type = "Pong"
                });
            continue;
        }

        if (type != responseType)
            continue;

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
    => await CollectTypesOn(
        socket,
        required,
        description);

async Task CollectTypesOn(
    ClientWebSocket targetSocket,
    HashSet<string> required,
    string description)
{
    var pending =
        new HashSet<string>(
            required,
            StringComparer.Ordinal);

    for (var attempts = 0;
         attempts < 160 &&
         pending.Count > 0;
         attempts++)
    {
        using var timeout =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(10));

        var response =
            await ReceivePacket(
                targetSocket,
                timeout.Token);

        var type =
            response.GetProperty(
                    "Type")
                .GetString() ??
            "";

        if (type == "Ping")
        {
            await SendPacket(
                targetSocket,
                new
                {
                    Type = "Pong"
                });
            continue;
        }

        // Bootstrap is allowed to interleave with server-push packets such as
        // nearby SpawnEntity when another player is already on the map.
        pending.Remove(type);
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
    => await SendPacket(
        socket,
        request);

async Task SendPacket(
    ClientWebSocket targetSocket,
    object request)
{
    var wire =
        Encoding.UTF8.GetBytes(
            ClientProtocol.Encode(
                request));

    using var timeout =
        new CancellationTokenSource(
            TimeSpan.FromSeconds(10));

    await targetSocket.SendAsync(
        wire.AsMemory(),
        WebSocketMessageType.Text,
        true,
        timeout.Token);
}

async Task<JsonElement> ReceivePacket(
    CancellationToken token)
    => await ReceivePacket(
        socket,
        token);

async Task<JsonElement> ReceivePacket(
    ClientWebSocket targetSocket,
    CancellationToken token)
{
    var buffer =
        new byte[64 * 1024];

    using var message =
        new MemoryStream();

    while (true)
    {
        var received =
            await targetSocket.ReceiveAsync(
                buffer.AsMemory(),
                token);

        if (received.MessageType ==
            WebSocketMessageType.Close)
        {
            throw new Exception(
                "WebSocket closed before expected response: " +
                targetSocket.CloseStatusDescription);
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
