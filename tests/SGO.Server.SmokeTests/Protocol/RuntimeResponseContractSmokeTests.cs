using System.Text.Json;
using SGO.Server.Application;
using SGO.Server.Domain.Sessions;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Protocol;

/// <summary>
/// Runtime JSON-contract checks for packets whose payloads are built from
/// Dictionary&lt;string, object&gt; or other computed data. Source-level audits
/// can prove field names exist, but they cannot prove the actual serialized
/// value types that the Godot client receives.
/// </summary>
internal static class RuntimeResponseContractSmokeTests
{
    public static void AssertLoginResponse(
        IReadOnlyList<object> responses)
    {
        var packet = FindPacket(
            responses,
            "LoginResponse");

        RequireBoolean(packet, "Success");
        RequireString(packet, "Message");
        var characters = RequireArray(
            packet,
            "Characters");

        Check(
            characters.GetArrayLength() == 3,
            "LoginResponse runtime contract keeps exactly three character slots");

        foreach (var character in
                 characters.EnumerateArray())
        {
            Check(
                character.ValueKind ==
                    JsonValueKind.Object,
                "LoginResponse character slot is a JSON object");

            RequireNumber(character, "ACCID");
            var dbid = RequireNumber(
                character,
                "DBID").GetInt64();

            if (dbid <= 0)
                continue;

            RequireString(character, "NAME");
            RequireString(character, "CharacterPwd");
            RequireNumber(character, "Race");
            RequireNumber(character, "Level");
            RequireNumber(character, "MapID");
            RequireNumber(character, "LocX");
            RequireNumber(character, "LocY");
            RequireNumber(character, "HP");
            RequireNumber(character, "MaxHP");
            RequireNumber(character, "MP");
            RequireNumber(character, "MaxMP");
            RequireNumber(character, "SP");
            RequireNumber(character, "MaxSP");
            RequireNumber(character, "Money");
            RequireString(character, "State");
            RequireArray(character, "UnlockedTitles");
            RequireArray(character, "PartyMembers");
        }

        Console.WriteLine(
            "Runtime LoginResponse dictionary contract passed.");
    }

    public static void AssertBootstrap(
        IReadOnlyList<object> responses)
    {
        Check(
            responses.Count == 10,
            "SelectCharacter runtime bootstrap emits the ten authoritative sync packets");

        var inventory = FindPacket(
            responses,
            "InventorySync");
        var inventoryItems = RequireArray(
            inventory,
            "Items");
        RequireNumber(inventory, "MaxSize");
        Check(
            inventoryItems.GetArrayLength() > 0,
            "InventorySync runtime contract exercises at least one persisted dictionary item");
        ValidateArrayObjects(
            inventoryItems,
            item =>
            {
                RequireNumber(item, "ItemUID");
                RequireNumber(item, "ItemIndex");
                RequireNumber(item, "Count");
                RequireNumber(item, "InBank");
                RequireNumber(item, "InShop");
            },
            "InventorySync.Items");

        var equips = FindPacket(
            responses,
            "EquipSync");
        var equipItems = RequireArray(
            equips,
            "Equips");
        RequireNumber(equips, "MaxSize");
        Check(
            equipItems.GetArrayLength() > 0,
            "EquipSync runtime contract exercises at least one persisted dictionary item");
        ValidateArrayObjects(
            equipItems,
            item =>
            {
                RequireNumber(item, "ItemID");
                RequireNumber(item, "ItemIdx");
                RequireNumber(item, "Damage");
                RequireNumber(item, "Color");
                RequireNumber(item, "Part");
                RequireNumber(item, "InBank");
                RequireNumber(item, "InShop");
            },
            "EquipSync.Equips");

        var pets = FindPacket(
            responses,
            "PetSync");
        var petItems = RequireArray(
            pets,
            "Pets");
        RequireNumber(pets, "MaxSize");
        RequireNumber(pets, "MaxSummon");
        Check(
            petItems.GetArrayLength() > 0,
            "PetSync runtime contract exercises at least one persisted dictionary pet");
        ValidateArrayObjects(
            petItems,
            item =>
            {
                RequireNumber(item, "UID");
                RequireNumber(item, "PETID");
                RequireString(item, "Name");
                RequireNumber(item, "HP");
                RequireNumber(item, "MaxHP");
                RequireNumber(item, "Level");
                RequireNumber(item, "State");
                RequireNumber(item, "Equip0");
                RequireNumber(item, "Equip1");
                RequireNumber(item, "Equip2");
                RequireNumber(item, "InShop");
            },
            "PetSync.Pets");

        var horses = FindPacket(
            responses,
            "HorseSync");
        var horseItems = RequireArray(
            horses,
            "Horses");
        var horseEquips = RequireArray(
            horses,
            "HorseEquips");
        RequireNumber(
            horses,
            "MaxHorseEquipSize");
        Check(
            horseItems.GetArrayLength() > 0 &&
            horseEquips.GetArrayLength() > 0,
            "HorseSync runtime contract exercises horse and horse-equipment dictionaries");

        ValidateArrayObjects(
            horseItems,
            item =>
            {
                RequireNumber(item, "HorseUID");
                RequireNumber(item, "HorseIdx");
                RequireNumber(item, "EnergyLeft");
                RequireNumber(item, "Level");
                RequireString(item, "Name");
                RequireString(item, "HorseType");
            },
            "HorseSync.Horses");

        ValidateArrayObjects(
            horseEquips,
            item =>
            {
                RequireNumber(item, "EquipUID");
                RequireNumber(item, "EquipIdx");
                RequireNumber(item, "HorseUID");
                RequireNumber(item, "Part");
                RequireNumber(item, "InBank");
                RequireNumber(item, "InShop");
            },
            "HorseSync.HorseEquips");

        var skills = FindPacket(
            responses,
            "SkillSync");
        var skillItems = RequireArray(
            skills,
            "Skills");
        ValidateArrayObjects(
            skillItems,
            item =>
            {
                RequireNumber(item, "skill_id");
                RequireNumber(item, "level");
                RequireNumber(item, "exp");
                RequireNumber(item, "SkillID");
                RequireNumber(item, "SkillLevel");
            },
            "SkillSync.Skills");

        var club = FindPacket(
            responses,
            "ClubSync");
        RequireNumber(club, "ClubID");
        RequireString(club, "ClubName");
        RequireArray(club, "Members");
        RequireArray(club, "Researches");

        var buffs = FindPacket(
            responses,
            "SyncBuffs");
        RequireArray(buffs, "Buffs");

        var quests = FindPacket(
            responses,
            "QuestSync");
        RequireArray(quests, "Quests");

        var festival = FindPacket(
            responses,
            "FestivalThemeSync");
        RequireString(festival, "Theme");
        RequireString(festival, "Festival");

        Console.WriteLine(
            "Runtime bootstrap dynamic-response contracts passed.");
    }

    public static void AssertAuthenticatedFailureResponses(
        PacketRouter router,
        ClientSession session)
    {
        using (var unsupported = Json(
            """{"Type":"PacketContractUnknownOperation"}"""))
        {
            AssertPacketError(
                router.Handle(
                    unsupported.RootElement,
                    session),
                "PacketContractUnknownOperation",
                "UNHANDLED_PACKET",
                "unsupported authenticated packet has a typed runtime error");
        }

        using (var invalid = Json(
            """
            {"Type":"BankTransfer","UID":"not-a-number","TabType":0,"Amount":1,"ToBank":true}
            """))
        {
            AssertPacketError(
                router.Handle(
                    invalid.RootElement,
                    session),
                "BankTransfer",
                "INVALID_PACKET",
                "invalid known packet has a typed runtime schema error");
        }

        var characterId = session.CharacterId;
        var mapId = session.CurrentMapId;
        var worldReady = session.WorldReady;
        try
        {
            session.CharacterId = null;
            session.CurrentMapId = 0;
            session.WorldReady = false;

            using var noWorld = Json(
                """{"Type":"SyncPosition","X":1,"Y":1}""");

            AssertPacketError(
                router.Handle(
                    noWorld.RootElement,
                    session),
                "SyncPosition",
                "WORLD_REQUIRED",
                "valid authenticated world packet without a selected character is rejected explicitly");
        }
        finally
        {
            session.CharacterId = characterId;
            session.CurrentMapId = mapId;
            session.WorldReady = worldReady;
        }
    }

    public static void AssertUnauthenticatedFailureResponse(
        PacketRouter router,
        ClientSession session)
    {
        using var request = Json(
            """{"Type":"UseSkill","SkillID":123}""");

        AssertPacketError(
            router.Handle(
                request.RootElement,
                session),
            "UseSkill",
            "AUTH_REQUIRED",
            "unauthenticated packet has a typed runtime auth error");
    }

    private static void AssertPacketError(
        IReadOnlyList<object> responses,
        string requestType,
        string code,
        string description)
    {
        Check(
            responses.Count == 1,
            description + " (single response)");

        var packet = FindPacket(
            responses,
            "PacketError");

        var actualRequestType = RequireString(
            packet,
            "RequestType").GetString();
        var actualCode = RequireString(
            packet,
            "Code").GetString();
        var message = RequireString(
            packet,
            "Message").GetString();

        Check(
            actualRequestType == requestType &&
            actualCode == code &&
            !string.IsNullOrWhiteSpace(message),
            description +
            (actualRequestType == requestType &&
             actualCode == code &&
             !string.IsNullOrWhiteSpace(message)
                ? ""
                : $" (expectedType={requestType}, actualType={actualRequestType}, " +
                  $"expectedCode={code}, actualCode={actualCode}, message={message})"));
    }

    private static JsonElement FindPacket(
        IReadOnlyList<object> responses,
        string type)
    {
        foreach (var response in responses)
        {
            using var document = JsonDocument.Parse(
                JsonSerializer.Serialize(response));
            var root = document.RootElement;

            if (root.ValueKind ==
                    JsonValueKind.Object &&
                root.TryGetProperty(
                    "Type",
                    out var typeElement) &&
                typeElement.ValueKind ==
                    JsonValueKind.String &&
                string.Equals(
                    typeElement.GetString(),
                    type,
                    StringComparison.Ordinal))
            {
                return root.Clone();
            }
        }

        throw new InvalidOperationException(
            $"FAILED: runtime response packet {type} was not emitted");
    }

    private static void ValidateArrayObjects(
        JsonElement array,
        Action<JsonElement> validate,
        string path)
    {
        foreach (var item in array.EnumerateArray())
        {
            Check(
                item.ValueKind ==
                    JsonValueKind.Object,
                $"{path} entry is a JSON object");
            validate(item);
        }
    }

    private static JsonElement RequireArray(
        JsonElement packet,
        string name)
        => RequireKind(
            packet,
            name,
            JsonValueKind.Array);

    private static JsonElement RequireNumber(
        JsonElement packet,
        string name)
        => RequireKind(
            packet,
            name,
            JsonValueKind.Number);

    private static JsonElement RequireString(
        JsonElement packet,
        string name)
        => RequireKind(
            packet,
            name,
            JsonValueKind.String);

    private static JsonElement RequireBoolean(
        JsonElement packet,
        string name)
    {
        Check(
            packet.TryGetProperty(
                name,
                out var value),
            $"runtime response contains {name}");
        Check(
            value.ValueKind is
                JsonValueKind.True or
                JsonValueKind.False,
            $"runtime response {name} is boolean");
        return value;
    }

    private static JsonElement RequireKind(
        JsonElement packet,
        string name,
        JsonValueKind kind)
    {
        Check(
            packet.TryGetProperty(
                name,
                out var value),
            $"runtime response contains {name}");
        Check(
            value.ValueKind == kind,
            $"runtime response {name} is {kind}");
        return value;
    }
}
