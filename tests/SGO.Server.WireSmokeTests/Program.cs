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

// Separate short E2E for the user's missing spectator damage numbers.
// It verifies two real encrypted clients without relying on unrelated
// shop, crafting, buff or mall test contracts.
var peerDamageOnly = args.Any(x => x == "--peer-damage-only");

var websocketUri =
    new Uri(url);
var webhookUri =
    new UriBuilder(
        websocketUri)
    {
        Scheme =
            websocketUri.Scheme ==
            "wss"
                ? "https"
                : "http",
        Path =
            "/integrations/paypal/webhook"
    }.Uri;

using (var http = new HttpClient())
using (var webhookBody =
       new StringContent(
           "{}",
           Encoding.UTF8,
           "application/json"))
{
    var webhookResponse =
        await http.PostAsync(
            webhookUri,
            webhookBody);

    if (webhookResponse.StatusCode !=
        System.Net.HttpStatusCode.ServiceUnavailable)
    {
        throw new Exception(
            "PayPal webhook without configured provider must fail closed with 503; got " +
            (int)webhookResponse.StatusCode);
    }

    Console.WriteLine(
        "PASS: unconfigured PayPal webhook fails closed");
}

using var socket = new ClientWebSocket();
using (var connectTimeout =
       new CancellationTokenSource(
           TimeSpan.FromSeconds(10)))
{
    await socket.ConnectAsync(
        new Uri(url),
        connectTimeout.Token);
}

var passed = 1;

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
    "authenticated mall list returns points/items/gifts",
    allowInterleaved: true);

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

// Same-map movement and chat must traverse the real encrypted socket and the
// map broadcast hub rather than only direct service calls.
// An unchanged SyncPosition is not a move; use a small real delta.
var firstX =
    (int)Math.Round(
        enterWorld.GetProperty(
                "LocX")
            .GetDouble());
var firstY =
    (int)Math.Round(
        enterWorld.GetProperty(
                "LocY")
            .GetDouble());

var firstMovementX = firstX + 4;
await SendPacketOn(socket,
    new
    {
        Type = "SyncPosition",
        X = firstMovementX,
        Y = firstY,
        Dir = "1"
    });

await AwaitType(
    peerSocket,
    "MoveEntity",
    root =>
        root.TryGetProperty(
            "EntityID",
            out var entity) &&
        entity.GetInt64() ==
            dbid &&
        root.TryGetProperty(
            "X",
            out var x) &&
        x.GetInt32() ==
            firstMovementX &&
        root.TryGetProperty(
            "Y",
            out var y) &&
        y.GetInt32() ==
            firstY,
    "peer receives first player's authoritative same-map movement");

var nearbyText =
    "wire-nearby-" + suffix;
await SendPacketOn(socket,
    new
    {
        Type = "ChatMessage",
        Channel = 0,
        Message = nearbyText,
        BubbleType = 0
    });

await AwaitType(
    peerSocket,
    "ChatBroadcast",
    root =>
        root.TryGetProperty(
            "EntityID",
            out var entity) &&
        entity.GetInt64() ==
            dbid &&
        root.TryGetProperty(
            "Channel",
            out var channel) &&
        channel.GetInt32() == 0 &&
        root.TryGetProperty(
            "Message",
            out var message) &&
        message.GetString() ==
            nearbyText,
    "peer receives nearby chat from first player over map broadcast");

if (peerDamageOnly)
{
    // Establish the same monster as visible to BOTH real WebSocket peers.
    // Do not estimate damage on the observer: compare the actual Server
    // result with the observer's encrypted EntityDamage packet.
    var witnessSpawn = await SendGm(
        $"spawn 245 {firstX + 20} {firstY}",
        "SpawnEntity",
        root =>
            root.TryGetProperty("EntityType", out var kind) &&
            kind.GetInt32() == 1,
        "seed nearby monster for spectator normal-hit wire test",
        skipNonMatchingSameType: true);
    var witnessMonsterId = witnessSpawn.GetProperty("EntityID").GetInt32();
    await AwaitType(
        peerSocket,
        "SpawnEntity",
        root =>
            root.TryGetProperty("EntityID", out var id) &&
            id.GetInt32() == witnessMonsterId &&
            root.TryGetProperty("EntityType", out var kind) &&
            kind.GetInt32() == 1,
        "peer receives monster spawn before damage",
        skipNonMatchingSameType: true);

    await SendPacketOn(socket, new {
        Type = "AttackEntity",
        TargetEntityID = witnessMonsterId,
        TargetType = 1
    });
    var ownerHit = await AwaitType(
        socket,
        "EntityDamage",
        root =>
            root.TryGetProperty("TargetID", out var target) &&
            target.GetInt32() == witnessMonsterId &&
            root.TryGetProperty("TargetType", out var kind) &&
            kind.GetInt32() == 1 &&
            root.TryGetProperty("Damage", out var value) &&
            value.GetInt32() > 0,
        "attacker receives real monster normal attack damage",
        skipNonMatchingSameType: true);
    var observerHit = await AwaitType(
        peerSocket,
        "EntityDamage",
        root =>
            root.TryGetProperty("TargetID", out var target) &&
            target.GetInt32() == witnessMonsterId &&
            root.TryGetProperty("TargetType", out var kind) &&
            kind.GetInt32() == 1 &&
            root.TryGetProperty("AttackerID", out var source) &&
            source.GetInt64() == dbid &&
            root.TryGetProperty("Damage", out var value) &&
            value.GetInt32() > 0,
        "peer receives normal-attack monster damage",
        skipNonMatchingSameType: true);
    // The source coordinates must be valid even before the observer's
    // ordinary SpawnEntity reaches the Client scene construction queue.
    if (!observerHit.TryGetProperty("TargetX", out var impactX) ||
        !observerHit.TryGetProperty("TargetY", out var impactY) ||
        impactX.ValueKind != JsonValueKind.Number ||
        impactY.ValueKind != JsonValueKind.Number)
    {
        throw new Exception(
            "Observer EntityDamage lacks authoritative impact coordinates");
    }
    foreach (var name in new[] {"Damage", "CurrentHP", "MaxHP", "TargetID", "AttackerID"})
    {
        if (ownerHit.GetProperty(name).GetInt64() !=
            observerHit.GetProperty(name).GetInt64())
        {
            throw new Exception(
                $"Peer EntityDamage differs from attacker for {name}");
        }
    }
    foreach (var name in new[] {"TargetX", "TargetY"})
    {
        if (Math.Abs(ownerHit.GetProperty(name).GetDouble() -
                     observerHit.GetProperty(name).GetDouble()) > 0.001)
        {
            throw new Exception(
                $"Peer impact coordinate differs from attacker for {name}");
        }
    }
    Console.WriteLine("[PEER_DAMAGE_WIRE] ALL PASS");
    return;
}

// Economy packets also run over the real encrypted two-client path. Use
// isolated item IDs so these checks cannot pollute the later drop/pickup test.
var bankSeed = await SendGm(
    "item 19 2",
    "InventorySync",
    root =>
        root.TryGetProperty("Items", out var items) &&
        items.ValueKind == JsonValueKind.Array &&
        items.EnumerateArray().Any(item =>
            item.TryGetProperty("ItemIndex", out var itemIndex) &&
            itemIndex.GetInt32() == 19 &&
            item.TryGetProperty("Count", out var count) &&
            count.GetInt32() == 2 &&
            item.TryGetProperty("InBank", out var inBank) &&
            inBank.GetInt32() == 0),
    "GM seeds isolated bank-transfer stack");

var bankItemUid =
    bankSeed.GetProperty("Items")
        .EnumerateArray()
        .Where(item =>
            item.GetProperty("ItemIndex").GetInt32() == 19 &&
            item.GetProperty("Count").GetInt32() == 2 &&
            item.GetProperty("InBank").GetInt32() == 0)
        .Select(item => item.GetProperty("ItemUID").GetInt64())
        .Max();

await SendPacketOn(socket,
    new
    {
        Type = "BankTransfer",
        UID = bankItemUid,
        ToBank = true,
        Amount = 1,
        TabType = 0
    });

await AwaitType(
    socket,
    "InventorySync",
    root =>
    {
        if (!root.TryGetProperty("Items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
            return false;

        var matching = items
            .EnumerateArray()
            .Where(item =>
                item.TryGetProperty("ItemIndex", out var itemIndex) &&
                itemIndex.GetInt32() == 19)
            .ToArray();

        return matching
                   .Where(item => item.GetProperty("InBank").GetInt32() == 0)
                   .Sum(item => item.GetProperty("Count").GetInt32()) == 1 &&
               matching
                   .Where(item => item.GetProperty("InBank").GetInt32() == 1)
                   .Sum(item => item.GetProperty("Count").GetInt32()) == 1;
    },
    "BankTransfer moves exactly one item over encrypted WebSocket");

var senderMoneySeed = await SendGm(
    "money 100",
    "SyncMoney",
    root =>
        root.TryGetProperty("Money", out var money) &&
        money.ValueKind == JsonValueKind.Number,
    "GM seeds sender money for direct-transfer wire test");

var peerMoneySeed = await SendGmOn(
    peerSocket,
    "money 0",
    "SyncMoney",
    root =>
        root.TryGetProperty("Money", out var money) &&
        money.ValueKind == JsonValueKind.Number,
    "GM reads peer money before direct-transfer wire test");

var senderMoneyBeforeTransfer =
    senderMoneySeed.GetProperty("Money").GetInt64();
var peerMoneyBeforeTransfer =
    peerMoneySeed.GetProperty("Money").GetInt64();

await SendPacketOn(socket,
    new
    {
        Type = "DirectTransfer",
        TargetDBID = peerDbid,
        Amount = 25
    });

var senderMoneyAfterTransferPacket = await AwaitType(
    socket,
    "SyncMoney",
    root =>
        root.TryGetProperty("Money", out var money) &&
        money.GetInt64() == senderMoneyBeforeTransfer - 25,
    "DirectTransfer debits sender exactly once");

var peerMoneyAfterTransferPacket = await AwaitType(
    peerSocket,
    "SyncMoney",
    root =>
        root.TryGetProperty("Money", out var money) &&
        money.GetInt64() == peerMoneyBeforeTransfer + 25,
    "DirectTransfer credits nearby peer exactly once");

var senderMoneyAfterTransfer =
    senderMoneyAfterTransferPacket.GetProperty("Money").GetInt64();
var peerMoneyAfterTransfer =
    peerMoneyAfterTransferPacket.GetProperty("Money").GetInt64();

var playerShopSeed = await SendGm(
    "item 1 1",
    "InventorySync",
    root =>
        root.TryGetProperty("Items", out var items) &&
        items.ValueKind == JsonValueKind.Array &&
        items.EnumerateArray().Any(item =>
            item.TryGetProperty("ItemIndex", out var itemIndex) &&
            itemIndex.GetInt32() == 1 &&
            item.TryGetProperty("InBank", out var inBank) &&
            inBank.GetInt32() == 0 &&
            item.TryGetProperty("InShop", out var inShop) &&
            inShop.GetInt32() == 0),
    "GM seeds tradeable player-shop item");

var playerShopItemUid =
    playerShopSeed.GetProperty("Items")
        .EnumerateArray()
        .Where(item =>
            item.GetProperty("ItemIndex").GetInt32() == 1 &&
            item.GetProperty("InBank").GetInt32() == 0 &&
            item.GetProperty("InShop").GetInt32() == 0)
        .Select(item => item.GetProperty("ItemUID").GetInt64())
        .Max();

await SendPacketOn(socket,
    new
    {
        Type = "ShopAction",
        Action = "AddItem",
        UID = playerShopItemUid,
        TabType = 0,
        Count = 1,
        Price = 7
    });

await AwaitType(
    socket,
    "ShopSync",
    root =>
        root.TryGetProperty("Items", out var items) &&
        items.ValueKind == JsonValueKind.Array &&
        items.EnumerateArray().Any(item =>
            item.TryGetProperty("UID", out var uid) &&
            uid.GetInt64() == playerShopItemUid &&
            item.TryGetProperty("ItemIndex", out var itemIndex) &&
            itemIndex.GetInt32() == 1 &&
            item.TryGetProperty("Price", out var price) &&
            price.GetInt64() == 7),
    "ShopAction AddItem reserves seller item over real wire");

await SendPacketOn(socket,
    new
    {
        Type = "ShopAction",
        Action = "OpenShop",
        ShopName = "WireShop"
    });

await AwaitType(
    socket,
    "SyncShopBoard",
    root =>
        root.TryGetProperty("EntityID", out var entity) &&
        entity.GetInt64() == dbid &&
        root.TryGetProperty("IsOpen", out var isOpen) &&
        isOpen.ValueKind == JsonValueKind.True,
    "seller opens player shop over real wire",
    skipNonMatchingSameType: true);

await AwaitType(
    peerSocket,
    "SyncShopBoard",
    root =>
        root.TryGetProperty("EntityID", out var entity) &&
        entity.GetInt64() == dbid &&
        root.TryGetProperty("IsOpen", out var isOpen) &&
        isOpen.ValueKind == JsonValueKind.True,
    "nearby peer receives player-shop board broadcast",
    skipNonMatchingSameType: true);

await SendPacketOn(peerSocket,
    new
    {
        Type = "ShopAction",
        Action = "ViewShop",
        TargetDBID = dbid
    });

await AwaitType(
    peerSocket,
    "ShopViewResponse",
    root =>
        root.TryGetProperty("OwnerDBID", out var owner) &&
        owner.GetInt64() == dbid &&
        root.TryGetProperty("Items", out var items) &&
        items.ValueKind == JsonValueKind.Array &&
        items.EnumerateArray().Any(item =>
            item.TryGetProperty("UID", out var uid) &&
            uid.GetInt64() == playerShopItemUid),
    "peer views seller player shop over real wire");

await SendPacketOn(peerSocket,
    new
    {
        Type = "ShopAction",
        Action = "BuyItem",
        TargetDBID = dbid,
        UID = playerShopItemUid,
        Count = 1
    });

await AwaitType(
    peerSocket,
    "SyncMoney",
    root =>
        root.TryGetProperty("Money", out var money) &&
        money.GetInt64() == peerMoneyAfterTransfer - 7,
    "player-shop purchase debits buyer over real wire");

await AwaitType(
    peerSocket,
    "InventorySync",
    root =>
        root.TryGetProperty("Items", out var items) &&
        items.ValueKind == JsonValueKind.Array &&
        items.EnumerateArray().Any(item =>
            item.TryGetProperty("ItemIndex", out var itemIndex) &&
            itemIndex.GetInt32() == 1 &&
            item.TryGetProperty("InShop", out var inShop) &&
            inShop.GetInt32() == 0),
    "player-shop purchase transfers item to buyer");

await AwaitType(
    socket,
    "SyncMoney",
    root =>
        root.TryGetProperty("Money", out var money) &&
        money.GetInt64() == senderMoneyAfterTransfer + 7,
    "player-shop purchase credits seller exactly once");

await AwaitType(
    socket,
    "ShopSync",
    root =>
        root.TryGetProperty("Items", out var items) &&
        items.ValueKind == JsonValueKind.Array &&
        items.GetArrayLength() == 0,
    "sold-out player shop publishes empty authoritative listing");

// NPC shop and deterministic crafting/dismantling also exercise the exact
// client packet names instead of only calling application services in-process.
await SendPacketOn(socket,
    new
    {
        Type = "ShopAction",
        Action = "BuyNpcItem",
        ShopID = 7,
        ItemIndex = 4103,
        Count = 2
    });

var npcShopMoney = await AwaitType(
    socket,
    "SyncMoney",
    root =>
        root.TryGetProperty("Money", out var money) &&
        money.GetInt64() <
            senderMoneyAfterTransfer + 7,
    "NPC ShopAction BuyNpcItem debits authoritative money over real wire");

await AwaitType(
    socket,
    "InventorySync",
    root =>
        root.TryGetProperty("Items", out var items) &&
        items.ValueKind == JsonValueKind.Array &&
        items.EnumerateArray()
            .Where(item =>
                item.TryGetProperty("ItemIndex", out var itemIndex) &&
                itemIndex.GetInt32() == 4103 &&
                item.TryGetProperty("InBank", out var inBank) &&
                inBank.GetInt32() == 0)
            .Sum(item =>
                item.GetProperty("Count").GetInt32()) >= 2,
    "NPC shop purchase persists mapped current-client item");

var craftMoneySeed = await SendGm(
    "money 250",
    "SyncMoney",
    root =>
        root.TryGetProperty("Money", out var money) &&
        money.GetInt64() >
            npcShopMoney.GetProperty("Money").GetInt64(),
    "GM seeds deterministic craft/dismantle costs");

var craftMoneyBefore =
    craftMoneySeed.GetProperty("Money").GetInt64();

await SendGm(
    "item 4051 20",
    "InventorySync",
    root =>
        root.TryGetProperty("Items", out var items) &&
        items.ValueKind == JsonValueKind.Array &&
        items.EnumerateArray().Any(item =>
            item.TryGetProperty("ItemIndex", out var itemIndex) &&
            itemIndex.GetInt32() == 4051 &&
            item.TryGetProperty("Count", out var count) &&
            count.GetInt32() >= 20 &&
            item.TryGetProperty("InBank", out var inBank) &&
            inBank.GetInt32() == 0),
    "GM seeds 20 iron ore for deterministic Recipe 1291");

await SendPacketOn(socket,
    new
    {
        Type = "CraftItemRequest",
        RecipeID = 1291,
        BatchCount = 1
    });

await AwaitType(
    socket,
    "CraftItemResponse",
    root =>
        root.TryGetProperty("Success", out var success) &&
        success.ValueKind == JsonValueKind.True &&
        root.TryGetProperty("ResultItems", out var resultItems) &&
        resultItems.ValueKind == JsonValueKind.Array &&
        resultItems.EnumerateArray().Any(item =>
            item.TryGetProperty("ItemID", out var itemId) &&
            itemId.GetInt32() == 4052 &&
            item.TryGetProperty("Count", out var count) &&
            count.GetInt32() == 1),
    "CraftItemRequest consumes deterministic Recipe 1291 over real wire");

await AwaitType(
    socket,
    "SyncMoney",
    root =>
        root.TryGetProperty("Money", out var money) &&
        money.GetInt64() == craftMoneyBefore - 100,
    "deterministic craft charges exact recipe money cost");

var craftInventory = await AwaitType(
    socket,
    "InventorySync",
    root =>
        root.TryGetProperty("Items", out var items) &&
        items.ValueKind == JsonValueKind.Array &&
        items.EnumerateArray().Any(item =>
            item.TryGetProperty("ItemIndex", out var itemIndex) &&
            itemIndex.GetInt32() == 4052 &&
            item.TryGetProperty("Count", out var count) &&
            count.GetInt32() >= 1 &&
            item.TryGetProperty("InBank", out var inBank) &&
            inBank.GetInt32() == 0),
    "deterministic craft replaces input material with iron block");

var ironBlockUid =
    craftInventory.GetProperty("Items")
        .EnumerateArray()
        .Where(item =>
            item.GetProperty("ItemIndex").GetInt32() == 4052 &&
            item.GetProperty("InBank").GetInt32() == 0)
        .Select(item =>
            item.GetProperty("ItemUID").GetInt64())
        .Max();

await SendPacketOn(socket,
    new
    {
        Type = "DismantleItemRequest",
        ItemUIDs = new[] { ironBlockUid },
        ItemType = "Material"
    });

await AwaitType(
    socket,
    "DismantleItemResponse",
    root =>
        root.TryGetProperty("Success", out var success) &&
        success.ValueKind == JsonValueKind.True &&
        root.TryGetProperty("ResultItems", out var resultItems) &&
        resultItems.ValueKind == JsonValueKind.Array &&
        resultItems.EnumerateArray().Any(item =>
            item.TryGetProperty("ItemID", out var itemId) &&
            itemId.GetInt32() == 4051 &&
            item.TryGetProperty("Count", out var count) &&
            count.GetInt32() == 20),
    "DismantleItemRequest executes deterministic Recipe 1292 over real wire");

await AwaitType(
    socket,
    "SyncMoney",
    root =>
        root.TryGetProperty("Money", out var money) &&
        money.GetInt64() == craftMoneyBefore - 150,
    "deterministic dismantle charges exact recipe money cost");

await AwaitType(
    socket,
    "InventorySync",
    root =>
    {
        if (!root.TryGetProperty("Items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
            return false;

        var array = items.EnumerateArray().ToArray();
        var ironBlocks = array
            .Where(item =>
                item.TryGetProperty("ItemIndex", out var itemIndex) &&
                itemIndex.GetInt32() == 4052 &&
                item.GetProperty("InBank").GetInt32() == 0)
            .Sum(item => item.GetProperty("Count").GetInt32());
        var ironOre = array
            .Where(item =>
                item.TryGetProperty("ItemIndex", out var itemIndex) &&
                itemIndex.GetInt32() == 4051 &&
                item.GetProperty("InBank").GetInt32() == 0)
            .Sum(item => item.GetProperty("Count").GetInt32());

        return ironBlocks == 0 &&
               ironOre >= 20;
    },
    "dismantle removes iron block and restores 20 iron ore");

// Seed through the normal privileged ChatMessage command path, then exercise
// the actual gameplay packets. The isolated test server explicitly enables GM;
// production defaults GM commands off.
await SendGm(
    "level 255",
    "PlayerStatusSync",
    root =>
        root.TryGetProperty(
            "MaxHP",
            out var maxHp) &&
        maxHp.GetInt32() > 0,
    "GM seed raises first player level for deterministic combat");

await SendGm(
    "maxstats",
    "AllocateStatResponse",
    root =>
        root.TryGetProperty(
            "Success",
            out var success) &&
        success.ValueKind ==
            JsonValueKind.True,
    "GM seed prepares deterministic combat stats");

await SendGm(
    "heal",
    "PlayerStatusSync",
    root =>
        root.TryGetProperty(
            "HP",
            out var hp) &&
        root.TryGetProperty(
            "MaxHP",
            out var maxHp) &&
        hp.GetInt32() ==
            maxHp.GetInt32(),
    "GM seed restores resources before skill wire test");

await SendGm(
    "skill 23108 1",
    "SkillSync",
    root =>
        root.TryGetProperty(
            "Skills",
            out var skills) &&
        skills.ValueKind ==
            JsonValueKind.Array &&
        skills.EnumerateArray().Any(skill =>
            skill.TryGetProperty(
                "SkillID",
                out var skillId) &&
            skillId.GetInt32() == 23108),
    "GM seed learns real buff skill 23108");

await SendPacketOn(socket,
    new
    {
        Type = "UseSkill",
        SkillID = 23108,
        TargetID = dbid,
        TargetType = 0,
        TargetX = firstX,
        TargetY = firstY
    });

await AwaitType(
    socket,
    "SkillCast",
    root =>
        root.TryGetProperty(
            "CasterID",
            out var caster) &&
        caster.GetInt64() ==
            dbid &&
        root.TryGetProperty(
            "SkillID",
            out var skillId) &&
        skillId.GetInt32() == 23108 &&
        root.TryGetProperty(
            "SkillLevel",
            out var level) &&
        level.GetInt32() == 1,
    "UseSkill broadcasts authoritative SkillCast for learned buff");

await AwaitType(
    socket,
    "SyncBuffs",
    root =>
        root.TryGetProperty(
            "Buffs",
            out var buffs) &&
        buffs.ValueKind ==
            JsonValueKind.Array &&
        buffs.EnumerateArray().Any(buff =>
            buff.TryGetProperty(
                "SkillID",
                out var skillId) &&
            skillId.GetInt32() == 23108),
    "UseSkill publishes active buff state");

await AwaitType(
    socket,
    "PlayerStatusSync",
    root =>
        root.TryGetProperty(
            "Attack",
            out var attack) &&
        attack.GetInt32() > 0,
    "UseSkill refreshes effective player stats after buff");

// Inventory drop/pickup through real gameplay packets.
var seededInventory = await SendGm(
    "item 1001 1",
    "InventorySync",
    root =>
        root.TryGetProperty(
            "Items",
            out var items) &&
        items.ValueKind ==
            JsonValueKind.Array &&
        items.EnumerateArray().Any(item =>
            item.TryGetProperty(
                "ItemIndex",
                out var itemIndex) &&
            itemIndex.GetInt32() == 1001),
    "GM seed adds real recovery item 1001");

var wireItem =
    seededInventory.GetProperty(
            "Items")
        .EnumerateArray()
        .First(item =>
            item.GetProperty(
                    "ItemIndex")
                .GetInt32() == 1001);
var wireItemUid =
    wireItem.GetProperty(
            "ItemUID")
        .GetInt64();

await SendPacketOn(socket,
    new
    {
        Type = "DropItemRequest",
        TabType = 0,
        ItemUID = wireItemUid,
        Count = 1,
        DropX = firstX,
        DropY = firstY
    });

var groundSpawn = await AwaitType(
    socket,
    "SpawnEntity",
    root =>
        root.TryGetProperty(
            "EntityType",
            out var entityType) &&
        entityType.GetInt32() == 3 &&
        root.TryGetProperty(
            "ExtraData",
            out var extra) &&
        extra.TryGetProperty(
            "ItemIndex",
            out var itemIndex) &&
        itemIndex.GetInt32() == 1001,
    "DropItemRequest creates authoritative ground item",
    skipNonMatchingSameType: true);

var groundEntityId =
    groundSpawn.GetProperty(
            "EntityID")
        .GetInt32();

await AwaitType(
    peerSocket,
    "SpawnEntity",
    root =>
        root.TryGetProperty(
            "EntityID",
            out var entity) &&
        entity.GetInt32() ==
            groundEntityId &&
        root.TryGetProperty(
            "EntityType",
            out var entityType) &&
        entityType.GetInt32() == 3,
    "peer receives dropped ground item",
    skipNonMatchingSameType: true);

await SendPacketOn(socket,
    new
    {
        Type = "PickUpItemRequest",
        EntityID = groundEntityId
    });

await AwaitType(
    socket,
    "ItemPickupFeedback",
    root =>
        root.TryGetProperty(
            "ItemIndex",
            out var itemIndex) &&
        itemIndex.GetInt32() == 1001 &&
        root.TryGetProperty(
            "Count",
            out var count) &&
        count.GetInt32() == 1,
    "owner picks up persisted ground item through normal request");

await AwaitType(
    peerSocket,
    "DespawnEntity",
    root =>
        root.TryGetProperty(
            "EntityID",
            out var entity) &&
        entity.GetInt32() ==
            groundEntityId,
    "peer receives ground-item despawn after pickup",
    skipNonMatchingSameType: true);

// A second pickup request may arrive after another socket or a map scene
// already removed the entity. SQLite must remain authoritative, and the
// requester needs a local Despawn even if its GroundItems cache was empty.
await SendPacketOn(socket,
    new
    {
        Type = "PickUpItemRequest",
        EntityID = groundEntityId
    });

await AwaitType(
    socket,
    "DespawnEntity",
    root =>
        root.TryGetProperty("EntityID", out var entity) &&
        entity.GetInt32() == groundEntityId,
    "duplicate pickup returns local DespawnEntity for missing SQLite row",
    skipNonMatchingSameType: true);

await AwaitType(
    socket,
    "SystemMessageFlash",
    root =>
        root.TryGetProperty("Message", out var message) &&
        message.GetString() == "物品已不存在",
    "duplicate pickup reports row absent without re-granting item",
    skipNonMatchingSameType: true);

// Explicit choice reward boxes go through the actual encrypted wire path:
// the initial use displays options without consuming; the server rejects a
// forged choice and atomically grants only a whitelisted option once.
var choiceSeed = await SendGm(
    "item 2209 1",
    "InventorySync",
    root =>
        root.TryGetProperty("Items", out var items) &&
        items.ValueKind == JsonValueKind.Array &&
        items.EnumerateArray().Any(item =>
            item.TryGetProperty("ItemIndex", out var itemIndex) &&
            itemIndex.GetInt32() == 2209),
    "GM seeds real 2209 explicit-choice equipment box");

var choiceItemUid = choiceSeed.GetProperty("Items")
    .EnumerateArray()
    .First(item => item.GetProperty("ItemIndex").GetInt32() == 2209)
    .GetProperty("ItemUID")
    .GetInt64();

await SendPacketOn(socket,
    new { Type = "UseItemRequest", ItemUID = choiceItemUid });

await AwaitType(
    socket,
    "ItemChoiceRequired",
    root =>
        root.GetProperty("ItemUID").GetInt64() == choiceItemUid &&
        root.GetProperty("ItemIndex").GetInt32() == 2209 &&
        root.TryGetProperty("Options", out var options) &&
        options.ValueKind == JsonValueKind.Array &&
        options.GetArrayLength() == 3 &&
        options.EnumerateArray()
            .Select(option => option.GetProperty("ItemIndex").GetInt32())
            .OrderBy(id => id)
            .SequenceEqual(new[] { 15112, 15113, 15114 }),
    "UseItemRequest returns three authoritative choices without consuming");

await SendPacketOn(socket,
    new
    {
        Type = "UseItemChoiceRequest",
        ItemUID = choiceItemUid,
        ItemIndex = 999999
    });

await AwaitType(
    socket,
    "SystemMessageFlash",
    root =>
        root.TryGetProperty("Message", out var message) &&
        (message.GetString() ?? "").Contains("獎勵選項已失效"),
    "forged choice is rejected without reward");

await SendPacketOn(socket,
    new
    {
        Type = "UseItemChoiceRequest",
        ItemUID = choiceItemUid,
        ItemIndex = 15113
    });

await AwaitType(
    socket,
    "InventorySync",
    root =>
        root.TryGetProperty("Items", out var items) &&
        !items.EnumerateArray().Any(item =>
            item.GetProperty("ItemUID").GetInt64() == choiceItemUid),
    "selected choice consumes source item exactly once");

await AwaitType(
    socket,
    "EquipSync",
    root =>
        root.TryGetProperty("Equips", out var equips) &&
        equips.ValueKind == JsonValueKind.Array &&
        equips.EnumerateArray().Any(equip =>
            equip.GetProperty("ItemIdx").GetInt32() == 15113),
    "selected armor is granted through equipment sync");

await AwaitType(
    socket,
    "SystemMessageFlash",
    root =>
        root.TryGetProperty("Message", out var message) &&
        (message.GetString() ?? "").Contains("已選擇獎勵"),
    "successful choice acknowledges the one-time selected reward");

// RepairItemRequest is verified on the same real encrypted client path.
// Seed a deliberately damaged copy instead of relying on normal reward grants,
// which correctly create equipment at full durability.
var damagedEquipSync = await SendGm(
    "equip 15113 0",
    "EquipSync",
    root =>
        root.TryGetProperty("Equips", out var equips) &&
        equips.ValueKind == JsonValueKind.Array &&
        equips.EnumerateArray().Any(equip =>
            equip.GetProperty("ItemIdx").GetInt32() == 15113 &&
            equip.GetProperty("Damage").GetInt32() == 0),
    "GM seeds damaged armor for real-wire repair");

var repairItemUid = damagedEquipSync
    .GetProperty("Equips")
    .EnumerateArray()
    .First(equip =>
        equip.GetProperty("ItemIdx").GetInt32() == 15113 &&
        equip.GetProperty("Damage").GetInt32() == 0)
    .GetProperty("ItemID")
    .GetInt64();

var repairMoneySeed = await SendGm(
    "money 10",
    "SyncMoney",
    root =>
        root.TryGetProperty("Money", out var money) &&
        money.ValueKind == JsonValueKind.Number,
    "GM seeds money for real-wire equipment repair");

var repairMoneyBefore =
    repairMoneySeed.GetProperty("Money").GetInt64();

await SendPacketOn(socket,
    new
    {
        Type = "RepairItemRequest",
        ItemType = "Equip",
        UID = repairItemUid
    });

await AwaitType(
    socket,
    "SystemMessageFlash",
    root =>
        root.TryGetProperty("Message", out var message) &&
        (message.GetString() ?? "").Contains("維修完成"),
    "RepairItemRequest reaches authoritative server repair flow");

await AwaitType(
    socket,
    "SyncMoney",
    root =>
        root.TryGetProperty("Money", out var money) &&
        money.GetInt64() < repairMoneyBefore,
    "repair deducts authoritative money over encrypted WebSocket");

await AwaitType(
    socket,
    "EquipSync",
    root =>
        root.TryGetProperty("Equips", out var equips) &&
        equips.ValueKind == JsonValueKind.Array &&
        equips.EnumerateArray().Any(equip =>
            equip.GetProperty("ItemID").GetInt64() == repairItemUid &&
            equip.GetProperty("Damage").GetInt32() == 50),
    "repair persists max durability and returns updated EquipSync");

await SendPacketOn(socket,
    new
    {
        Type = "UseItemChoiceRequest",
        ItemUID = choiceItemUid,
        ItemIndex = 15113
    });

await AwaitType(
    socket,
    "SystemMessageFlash",
    root =>
        root.TryGetProperty("Message", out var message) &&
        (message.GetString() ?? "").Contains("找不到可使用的選擇型道具"),
    "replayed choice UID cannot grant a second reward");

// Pet creation is test seeding; summon and command attack are normal PetAction.
var petSync = await SendGm(
    "pet 45001 WirePet",
    "PetSync",
    root =>
        root.TryGetProperty(
            "Pets",
            out var pets) &&
        pets.ValueKind ==
            JsonValueKind.Array &&
        pets.EnumerateArray().Any(pet =>
            pet.TryGetProperty(
                "PETID",
                out var petId) &&
            petId.GetInt32() == 45001),
    "GM seed adds real PetID 45001");

var wirePet =
    petSync.GetProperty(
            "Pets")
        .EnumerateArray()
        .First(pet =>
            pet.GetProperty(
                    "PETID")
                .GetInt32() == 45001);
var wirePetUid =
    wirePet.GetProperty(
            "UID")
        .GetInt64();

await SendPacketOn(socket,
    new
    {
        Type = "PetAction",
        Action = "Summon",
        PetUID = wirePetUid
    });

await AwaitType(
    socket,
    "PetSync",
    root =>
        root.GetProperty(
                "Pets")
            .EnumerateArray()
            .Any(pet =>
                pet.GetProperty(
                        "UID")
                    .GetInt64() ==
                    wirePetUid &&
                pet.GetProperty(
                        "State")
                    .GetInt32() == 1),
    "PetAction Summon persists active pet state");

await AwaitType(
    socket,
    "SpawnEntity",
    root =>
        root.TryGetProperty(
            "EntityID",
            out var entity) &&
        entity.GetInt64() ==
            4_000_000L +
            wirePetUid &&
        root.TryGetProperty(
            "EntityType",
            out var entityType) &&
        entityType.GetInt32() == 4,
    "summoned pet spawns through normal entity packet",
    skipNonMatchingSameType: true);

// Spawn one real legacy monster near the player and command the pet to attack.
var petTargetSpawn = await SendGm(
    $"spawn 245 {firstX + 60} {firstY}",
    "SpawnEntity",
    root =>
        root.TryGetProperty(
            "EntityType",
            out var entityType) &&
        entityType.GetInt32() == 1,
    "GM seed spawns EnemyID 245 for pet combat",
    skipNonMatchingSameType: true);

var petTargetId =
    petTargetSpawn.GetProperty(
            "EntityID")
        .GetInt32();

await SendPacketOn(socket,
    new
    {
        Type = "PetAction",
        Action = "CommandAttack",
        TargetEntityID = petTargetId
    });

await AwaitType(
    socket,
    "EntityDamage",
    root =>
        root.TryGetProperty(
            "TargetID",
            out var target) &&
        target.GetInt32() ==
            petTargetId &&
        root.TryGetProperty(
            "AttackerID",
            out var attacker) &&
        attacker.GetInt64() ==
            4_000_000L +
            wirePetUid,
    "summoned pet damages target through PetAction CommandAttack",
    skipNonMatchingSameType: true);

// Mecha creation is seeding; DriveMechaRequest is the actual gameplay contract.
var horseSync = await SendGm(
    "mecha 50001",
    "HorseSync",
    root =>
        root.TryGetProperty(
            "Horses",
            out var horses) &&
        horses.ValueKind ==
            JsonValueKind.Array &&
        horses.EnumerateArray().Any(horse =>
            horse.TryGetProperty(
                "HorseIdx",
                out var horseIdx) &&
            horseIdx.GetInt32() == 50001),
    "GM seed adds real mecha 50001");

var wireHorse =
    horseSync.GetProperty(
            "Horses")
        .EnumerateArray()
        .First(horse =>
            horse.GetProperty(
                    "HorseIdx")
                .GetInt32() == 50001);
var wireHorseUid =
    wireHorse.GetProperty(
            "HorseUID")
        .GetInt64();

await SendPacketOn(socket,
    new
    {
        Type = "DriveMechaRequest",
        HorseUID = wireHorseUid
    });

await AwaitType(
    socket,
    "DriveMechaResponse",
    root =>
        root.TryGetProperty(
            "Success",
            out var success) &&
        success.ValueKind ==
            JsonValueKind.True &&
        root.TryGetProperty(
            "HorseUID",
            out var horseUid) &&
        horseUid.GetInt64() ==
            wireHorseUid,
    "DriveMechaRequest mounts seeded mecha through authoritative ACK");

// Dismount must acknowledge the authoritative position. The client reuses
// these fields to invalidate any stale mecha-era movement path/anchor.
await SendPacketOn(socket,
    new
    {
        Type = "DriveMechaRequest",
        HorseUID = -1
    });

await AwaitType(
    socket,
    "DriveMechaResponse",
    root =>
        root.TryGetProperty(
            "Success", out var success) &&
        success.ValueKind == JsonValueKind.True &&
        root.TryGetProperty(
            "HorseUID", out var horseUid) &&
        horseUid.GetInt64() == -1 &&
        root.TryGetProperty(
            "MapID", out var mapId) &&
        mapId.ValueKind == JsonValueKind.Number &&
        mapId.GetInt32() > 0 &&
        root.TryGetProperty(
            "X", out var x) &&
        x.ValueKind == JsonValueKind.Number &&
        root.TryGetProperty(
            "Y", out var y) &&
        y.ValueKind == JsonValueKind.Number,
    "dismount ACK includes map and authoritative position for client rebase");

// Remount so subsequent combat assertions keep their previous setup.
await SendPacketOn(socket,
    new
    {
        Type = "DriveMechaRequest",
        HorseUID = wireHorseUid
    });

await AwaitType(
    socket,
    "DriveMechaResponse",
    root =>
        root.TryGetProperty(
            "Success", out var success) &&
        success.ValueKind == JsonValueKind.True &&
        root.TryGetProperty(
            "HorseUID", out var horseUid) &&
        horseUid.GetInt64() == wireHorseUid,
    "remount preserves the existing mecha combat workflow");

// Player combat uses a separately spawned monster so the pet test cannot
// accidentally consume the same death transition.
var combatSpawn = await SendGm(
    $"spawn 245 {firstX + 20} {firstY}",
    "SpawnEntity",
    root =>
        root.TryGetProperty(
            "EntityType",
            out var entityType) &&
        entityType.GetInt32() == 1,
    "GM seed spawns EnemyID 245 for player combat",
    skipNonMatchingSameType: true);

var combatTargetId =
    combatSpawn.GetProperty(
            "EntityID")
        .GetInt32();
var combatKilled = false;

for (var attempt = 0;
     attempt < 20 && !combatKilled;
     attempt++)
{
    await SendPacketOn(socket,
        new
        {
            Type = "AttackEntity",
            TargetEntityID =
                combatTargetId,
            TargetType = 1
        });

    var damage = await AwaitType(
        socket,
        "EntityDamage",
        root =>
            root.TryGetProperty(
                "TargetID",
                out var target) &&
            target.GetInt32() ==
                combatTargetId,
        $"AttackEntity damages seeded monster (attempt {attempt + 1})",
        skipNonMatchingSameType: true);

    // A spectator must see EVERY normal hit, not only the final
    // EntityDeath. Previously this end-to-end test checked only death,
    // allowing missing peer damage numbers to go completely unnoticed.
    var peerDamage = await AwaitType(
        peerSocket,
        "EntityDamage",
        root =>
            root.TryGetProperty("TargetID", out var peerTarget) &&
            peerTarget.GetInt32() == combatTargetId &&
            root.TryGetProperty("TargetType", out var targetType) &&
            targetType.GetInt32() == 1 &&
            root.TryGetProperty("AttackerID", out var peerAttacker) &&
            peerAttacker.GetInt64() == dbid &&
            root.TryGetProperty("Damage", out var peerAmount) &&
            peerAmount.GetInt32() > 0,
        $"peer receives normal-attack monster damage (attempt {attempt + 1})",
        skipNonMatchingSameType: true);
    if (peerDamage.GetProperty("Damage").GetInt32() !=
        damage.GetProperty("Damage").GetInt32() ||
        peerDamage.GetProperty("CurrentHP").GetInt32() !=
        damage.GetProperty("CurrentHP").GetInt32())
    {
        throw new Exception(
            "Peer damage packet differs from the attacker's authoritative result");
    }

    combatKilled =
        damage.TryGetProperty(
            "CurrentHP",
            out var currentHp) &&
        currentHp.GetInt32() == 0;
}

if (!combatKilled)
{
    throw new Exception(
        "AttackEntity did not kill seeded low-level monster within 20 hits");
}

await AwaitType(
    socket,
    "EntityDeath",
    root =>
        root.TryGetProperty(
            "EntityID",
            out var entity) &&
        entity.GetInt32() ==
            combatTargetId &&
        root.TryGetProperty(
            "KillerID",
            out var killer) &&
        killer.GetInt64() ==
            dbid,
    "player receives authoritative monster death",
    skipNonMatchingSameType: true);

await AwaitType(
    peerSocket,
    "EntityDeath",
    root =>
        root.TryGetProperty(
            "EntityID",
            out var entity) &&
        entity.GetInt32() ==
            combatTargetId,
    "peer receives same monster death broadcast",
    skipNonMatchingSameType: true);

// Party invite/accept over two live sockets.
await SendPacketOn(socket,
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

await SendPacketOn(peerSocket,
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
await SendPacketOn(socket,
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

await SendPacketOn(peerSocket,
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

await SendPacketOn(socket,
    new
    {
        Type = "TradeLock"
    });
await SendPacketOn(peerSocket,
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

await SendPacketOn(socket,
    new
    {
        Type = "TradeFinalConfirm"
    });
await SendPacketOn(peerSocket,
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

// Persist one ground item across a complete leave/return map lifecycle. The
// old client bug was that returning to a map could lose ground entities even
// though SQLite still owned the authoritative row. WorldReady must rebuild it.
var lifecycleGroundSeed = await SendGm(
    "item 1001 1",
    "InventorySync",
    root =>
        root.TryGetProperty("Items", out var items) &&
        items.ValueKind == JsonValueKind.Array &&
        items.EnumerateArray().Any(item =>
            item.TryGetProperty("ItemIndex", out var itemIndex) &&
            itemIndex.GetInt32() == 1001 &&
            item.TryGetProperty("InBank", out var inBank) &&
            inBank.GetInt32() == 0 &&
            item.TryGetProperty("InShop", out var inShop) &&
            inShop.GetInt32() == 0),
    "GM seeds item for ground lifecycle map-return test");

var lifecycleGroundUid = lifecycleGroundSeed
    .GetProperty("Items")
    .EnumerateArray()
    .Where(item =>
        item.GetProperty("ItemIndex").GetInt32() == 1001 &&
        item.GetProperty("InBank").GetInt32() == 0 &&
        item.GetProperty("InShop").GetInt32() == 0)
    .Select(item => item.GetProperty("ItemUID").GetInt64())
    .Max();

await SendPacketOn(socket,
    new
    {
        Type = "DropItemRequest",
        TabType = 0,
        ItemUID = lifecycleGroundUid,
        Count = 1,
        DropX = firstX,
        DropY = firstY
    });

var lifecycleGroundSpawn = await AwaitType(
    socket,
    "SpawnEntity",
    root =>
        root.TryGetProperty("EntityType", out var entityType) &&
        entityType.GetInt32() == 3 &&
        root.TryGetProperty("ExtraData", out var extra) &&
        extra.TryGetProperty("ItemIndex", out var itemIndex) &&
        itemIndex.GetInt32() == 1001,
    "ground lifecycle item is persisted before leaving map",
    skipNonMatchingSameType: true);

var lifecycleGroundEntityId =
    lifecycleGroundSpawn.GetProperty("EntityID").GetInt32();

await AwaitType(
    peerSocket,
    "SpawnEntity",
    root =>
        root.TryGetProperty("EntityID", out var entity) &&
        entity.GetInt32() == lifecycleGroundEntityId &&
        root.TryGetProperty("EntityType", out var entityType) &&
        entityType.GetInt32() == 3,
    "peer sees lifecycle ground item before owner leaves map",
    skipNonMatchingSameType: true);

// Exercise a complete map-presence transition while both clients remain live.
// GM only selects the deterministic destination; ChangeMap/WorldReady and
// presence despawn/spawn still use the normal transport/router path.
var awayMap = await SendGm(
    $"map 16 {firstX} {firstY}",
    "ChangeMap",
    root =>
        root.TryGetProperty(
            "MapID",
            out var mapId) &&
        mapId.GetInt32() == 16,
    "map transition sends authoritative ChangeMap to destination 16");

await AwaitType(
    peerSocket,
    "DespawnEntity",
    root =>
        root.TryGetProperty(
            "EntityID",
            out var entity) &&
        entity.GetInt64() ==
            dbid,
    "peer despawns player immediately when player leaves map 15",
    skipNonMatchingSameType: true);

await ProbeOn(
    socket,
    new
    {
        Type = "WorldReady",
        MapID = 16
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
        mapId.GetInt32() == 16,
    "destination map WorldReady completes after ChangeMap",
    allowInterleaved: true);

var returnMap = await SendGm(
    $"map {currentMapId} {firstX} {firstY}",
    "ChangeMap",
    root =>
        root.TryGetProperty(
            "MapID",
            out var mapId) &&
        mapId.GetInt32() ==
            currentMapId,
    "map transition sends authoritative ChangeMap back to original map");

await AwaitType(
    peerSocket,
    "SpawnEntity",
    root =>
        root.TryGetProperty(
            "EntityID",
            out var entity) &&
        entity.GetInt64() ==
            dbid &&
        root.TryGetProperty(
            "EntityType",
            out var entityType) &&
        entityType.GetInt32() == 0,
    "peer respawns returning player in original map presence",
    skipNonMatchingSameType: true);

await ProbeOn(
    socket,
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
    "return map WorldReady completes after presence respawn",
    allowInterleaved: true);

await AwaitType(
    socket,
    "SpawnEntity",
    root =>
        root.TryGetProperty("EntityID", out var entity) &&
        entity.GetInt32() == lifecycleGroundEntityId &&
        root.TryGetProperty("EntityType", out var entityType) &&
        entityType.GetInt32() == 3 &&
        root.TryGetProperty("IsSnapshot", out var snapshot) &&
        snapshot.ValueKind == JsonValueKind.True,
    "return-map WorldReady rebuilds persisted ground item snapshot",
    skipNonMatchingSameType: true);

await SendPacketOn(socket,
    new
    {
        Type = "PickUpItemRequest",
        EntityID = lifecycleGroundEntityId
    });

await AwaitType(
    socket,
    "ItemPickupFeedback",
    root =>
        root.TryGetProperty("ItemIndex", out var itemIndex) &&
        itemIndex.GetInt32() == 1001 &&
        root.TryGetProperty("Count", out var count) &&
        count.GetInt32() == 1,
    "owner can pick up lifecycle ground item after returning to map");

await AwaitType(
    peerSocket,
    "DespawnEntity",
    root =>
        root.TryGetProperty("EntityID", out var entity) &&
        entity.GetInt32() == lifecycleGroundEntityId,
    "peer receives lifecycle ground despawn after return-map pickup",
    skipNonMatchingSameType: true);

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

await ProbeOn(
    socket,
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
    "safe logout returns acknowledgement before socket close",
    allowInterleaved: true);

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
    string description,
    bool allowInterleaved = false)
    => await ProbeOn(
        socket,
        request,
        responseType,
        assert,
        description,
        allowInterleaved);

async Task<JsonElement> ProbeOn(
    ClientWebSocket targetSocket,
    object request,
    string responseType,
    Func<JsonElement, bool> assert,
    string description,
    bool allowInterleaved = false)
{
    await SendPacketOn(targetSocket,
        request);

    for (var attempts = 0;
         attempts < 120;
         attempts++)
    {
        using var timeout =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(10));

        var response =
            await ReceivePacketOn(targetSocket,
                timeout.Token);

        var type =
            response.GetProperty(
                    "Type")
                .GetString();

        if (type == "Ping")
        {
            await SendPacketOn(targetSocket,
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

async Task<JsonElement> SendGm(
    string command,
    string responseType,
    Func<JsonElement, bool> assert,
    string description,
    bool skipNonMatchingSameType = false)
    => await SendGmOn(
        socket,
        command,
        responseType,
        assert,
        description,
        skipNonMatchingSameType);

async Task<JsonElement> SendGmOn(
    ClientWebSocket targetSocket,
    string command,
    string responseType,
    Func<JsonElement, bool> assert,
    string description,
    bool skipNonMatchingSameType = false)
{
    await SendPacketOn(targetSocket,
        new
        {
            Type = "ChatMessage",
            Channel = 0,
            Message = "/gm " + command,
            BubbleType = 0
        });

    return await AwaitType(
        targetSocket,
        responseType,
        assert,
        description,
        skipNonMatchingSameType);
}

async Task<JsonElement> AwaitType(
    ClientWebSocket targetSocket,
    string responseType,
    Func<JsonElement, bool> assert,
    string description,
    bool skipNonMatchingSameType = false)
{
    var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
    var skippedByType = new Dictionary<string, int>(StringComparer.Ordinal);
    // Large WorldReady snapshots can contain hundreds of unrelated entities.
    // Time-limit this wait instead of falsely stopping after 160 packets.
    for (var attempts = 0;
         attempts < 8192 &&
         System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalSeconds < 20.0;
         attempts++)
    {
        using var timeout =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(10));

        var response =
            await ReceivePacketOn(targetSocket,
                timeout.Token);

        var type =
            response.GetProperty(
                    "Type")
                .GetString();

        if (type == "Ping")
        {
            await SendPacketOn(targetSocket,
                new
                {
                    Type = "Pong"
                });
            continue;
        }

        if (type != responseType)
        {
            if (type is not null)
            {
                skippedByType.TryGetValue(type, out var previous);
                skippedByType[type] = previous + 1;
            }
            continue;
        }

        if (!assert(response))
        {
            if (skipNonMatchingSameType)
                continue;

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
        $"{description}: no expected packet received; skipped=" +
        string.Join(", ", skippedByType.OrderByDescending(x => x.Value).Take(8)
            .Select(x => $"{x.Key}={x.Value}")));
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
            await ReceivePacketOn(targetSocket,
                timeout.Token);

        var type =
            response.GetProperty(
                    "Type")
                .GetString() ??
            "";

        if (type == "Ping")
        {
            await SendPacketOn(targetSocket,
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
    => await SendPacketOn(socket,
        request);

async Task SendPacketOn(
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
    => await ReceivePacketOn(socket,
        token);

async Task<JsonElement> ReceivePacketOn(
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
