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

// Same-map movement and chat must traverse the real encrypted socket and the
// map broadcast hub rather than only direct service calls.
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

await SendPacket(
    socket,
    new
    {
        Type = "SyncPosition",
        X = firstX,
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
            firstX &&
        root.TryGetProperty(
            "Y",
            out var y) &&
        y.GetInt32() ==
            firstY,
    "peer receives first player's authoritative same-map movement");

var nearbyText =
    "wire-nearby-" + suffix;
await SendPacket(
    socket,
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

await SendPacket(
    socket,
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

await SendPacket(
    socket,
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

await SendPacket(
    socket,
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

await SendPacket(
    socket,
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

await SendPacket(
    socket,
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

await SendPacket(
    socket,
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
await SendPacket(
    socket,
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
await SendPacket(
    socket,
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
    await SendPacket(
        socket,
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

async Task<JsonElement> SendGm(
    string command,
    string responseType,
    Func<JsonElement, bool> assert,
    string description,
    bool skipNonMatchingSameType = false)
{
    await SendPacket(
        socket,
        new
        {
            Type = "ChatMessage",
            Channel = 0,
            Message = "/gm " + command,
            BubbleType = 0
        });

    return await AwaitType(
        socket,
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
