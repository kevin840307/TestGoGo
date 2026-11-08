using System.Text.Json;
using SGO.Server.Protocol;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Protocol;

/// <summary>Independent, deterministic smoke cases; invoke Run from the suite runner.</summary>
internal static class IdentitySchemaSmokeTests
{
    public static void Run()
    {
        // Database-backed item and mecha identifiers are Int64, unlike map entity IDs.
        using (var largeHorseUid = Json(
            """{"Type":"DriveMechaRequest","HorseUID":4294967296}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      largeHorseUid.RootElement, "DriveMechaRequest", out _),
                "mecha drive accepts Int64 persistent horse UID");
        }
        using (var largeHorseEquip = Json(
            """{"Type":"HorseEquipRequest","EquipUID":4294967296,"HorseUID":4294967297}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      largeHorseEquip.RootElement, "HorseEquipRequest", out _),
                "mecha equipment accepts Int64 equipment and horse UIDs");
        }
        using (var malformedHorseCharge = Json(
            """{"Type":"HorseChargeRequest","HorseUID":"4294967296","ItemUID":10}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      malformedHorseCharge.RootElement, "HorseChargeRequest",
                      out var reason) && reason.Contains("HorseUID"),
                "mecha charging rejects string UID instead of targeting default horse");
        }
        using (var largeHorseBattery = Json(
            """{"Type":"HorseChargeRequest","HorseUID":4294967296,"ItemUID":4294967297}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      largeHorseBattery.RootElement, "HorseChargeRequest", out _),
                "mecha charging accepts Int64 battery item UID");
        }
        using (var missingGroundDropUid = Json(
            """{"Type":"DropItemRequest","TabType":0,"Count":1,"DropX":100,"DropY":100}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      missingGroundDropUid.RootElement, "DropItemRequest",
                      out var reason) && reason.Contains("ItemUID"),
                "ground drop requires an inventory UID");
        }
        using (var validGroundDrop = Json(
            """{"Type":"DropItemRequest","TabType":0,"ItemUID":4294967296,"Count":1,"DropX":100,"DropY":100}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      validGroundDrop.RootElement, "DropItemRequest", out _),
                "ground drop accepts Int64 inventory UID and numeric position");
        }
        using (var invalidDropCoordinate = Json(
            """{"Type":"DropItemRequest","TabType":0,"ItemUID":7,"DropX":"100"}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      invalidDropCoordinate.RootElement, "DropItemRequest",
                      out var reason) && reason.Contains("DropX"),
                "present optional ground-drop position must be numeric");
        }
        using (var validMegaphoneUid = Json(
            """{"Type":"MegaphoneMessage","ItemUID":4294967296,"Message":"Hello"}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      validMegaphoneUid.RootElement, "MegaphoneMessage", out _),
                "megaphone inventory UID preserves Int64 ID range");
        }

        using (var longCharacterSelect = Json(
            """{"Type":"SelectCharacterRequest","DBID":4294967296}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      longCharacterSelect.RootElement,
                      "SelectCharacterRequest", out _),
                "character selection preserves Int64 persistent DBID");
        }
        using (var longCharacterDelete = Json(
            """{"Type":"DeleteCharacterRequest","DBID":4294967296,"Password":"1234"}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      longCharacterDelete.RootElement,
                      "DeleteCharacterRequest", out _),
                "character deletion supports Int64 DBID");
        }
        using (var invalidModifyPasswordDbId = Json(
            """{"Type":"ModifyPasswordRequest","DBID":"4294967296","NewPwd":"abc"}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      invalidModifyPasswordDbId.RootElement,
                      "ModifyPasswordRequest", out var reason) &&
                  reason.Contains("DBID"),
                "password update rejects string DBID before database mutation");
        }
    }
}
