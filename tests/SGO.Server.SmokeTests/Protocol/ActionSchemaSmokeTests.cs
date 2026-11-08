using System.Text.Json;
using SGO.Server.Protocol;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Protocol;

/// <summary>Independent, deterministic smoke cases; invoke Run from the suite runner.</summary>
internal static class ActionSchemaSmokeTests
{
    public static void Run()
    {
        // Pet helper always carries PetUID, but combat-mode/attack packets
        // use a different shape. Validate both before entering any pet mutation.
        using (var wrongPetEquipUid = Json(
            """{"Type":"PetAction","Action":"Equip","PetUID":2147483648,"ItemUID":"9","TargetSlot":1}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      wrongPetEquipUid.RootElement, "PetAction", out var petError) &&
                  petError.Contains("ItemUID"),
                "pet equip rejects nonnumeric equipment UID");
        }
        using (var validLongPetUid = Json(
            """{"Type":"PetAction","Action":"Summon","PetUID":2147483648}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      validLongPetUid.RootElement, "PetAction", out _),
                "pet actions support full Int64 pet UIDs");
        }
        using (var petModeWithoutUid = Json(
            """{"Type":"PetAction","Action":"SetCombatMode","CombatMode":2}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      petModeWithoutUid.RootElement, "PetAction", out _),
                "pet combat mode does not incorrectly require PetUID");
        }
        using (var missingPetAttackTarget = Json(
            """{"Type":"PetAction","Action":"CommandAttack"}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      missingPetAttackTarget.RootElement, "PetAction", out var commandError) &&
                  commandError.Contains("TargetEntityID"),
                "pet attack command requires target ID");
        }

        using (var invalidClubDonation = Json(
            """{"Type":"ClubAction","Action":"DonateMoney","Amount":"500"}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      invalidClubDonation.RootElement, "ClubAction", out var error) &&
                  error.Contains("Amount"),
                "club donation rejects string amount before any asset mutation");
        }
        using (var largeClubDonation = Json(
            """{"Type":"ClubAction","Action":"DonateExp","Amount":4294967296}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      largeClubDonation.RootElement, "ClubAction", out _),
                "club donation wire preserves valid Int64 amounts");
        }
        using (var clearClubMessage = Json(
            """{"Type":"ClubAction","Action":"UpdateMessage","ClubMsg":""}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      clearClubMessage.RootElement, "ClubAction", out _),
                "club announcement clearing remains compatible with empty text");
        }
        using (var incompleteMallBuy = Json(
            """{"Type":"MallAction","Action":"BuyItem","MallID":17}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      incompleteMallBuy.RootElement, "MallAction", out var error) &&
                  error.Contains("Count"),
                "mall purchase requires quantity even while no catalog is configured");
        }
        using (var validMallDonate = Json(
            """{"Type":"MallAction","Action":"CreatePayPalDonation","Amount":500}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      validMallDonate.RootElement, "MallAction", out _),
                "PayPal request wire schema remains valid without enabling payments");
        }
        using (var wrongLegacyPassword = Json(
            """{"Type":"MallAction","Action":"QueryLegacyPoints","LoginName":"old","Password":42}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      wrongLegacyPassword.RootElement, "MallAction", out var error) &&
                  error.Contains("Password"),
                "legacy points request requires text credentials without enabling transfer");
        }
        using (var openMall = Json(
            """{"Type":"MallAction","Action":"OpenMall"}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      openMall.RootElement, "MallAction", out _),
                "mall opening remains valid without purchase-only fields");
        }

        using (var badOptionalSkillTarget = Json(
            """{"Type":"UseSkill","SkillID":26104,"TargetID":"55"}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      badOptionalSkillTarget.RootElement, "UseSkill", out var reason) &&
                  reason.Contains("TargetID"),
                "optional combat target must be numeric when present");
        }
        using (var validNoTargetSkill = Json(
            """{"Type":"UseSkill","SkillID":26104}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      validNoTargetSkill.RootElement, "UseSkill", out _),
                "legacy no-target skill request remains valid");
        }
        using (var badAttackTargetType = Json(
            """{"Type":"AttackEntity","TargetEntityID":100,"TargetType":false}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      badAttackTargetType.RootElement, "AttackEntity", out var reason) &&
                  reason.Contains("TargetType"),
                "attack target type must be an integer when present");
        }
        using (var invalidPartyTarget = Json(
            """{"Type":"PartyAction","Action":"Invite","TargetDBID":"1234"}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      invalidPartyTarget.RootElement, "PartyAction", out var reason) &&
                  reason.Contains("TargetDBID"),
                "party target ID must be numeric when present");
        }
        using (var validPartyCreate = Json(
            """{"Type":"PartyAction","Action":"Create"}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      validPartyCreate.RootElement, "PartyAction", out _),
                "party create remains compatible without a target");
        }

        using (var missingPartyInviteTarget = Json(
            """{"Type":"PartyAction","Action":"Invite"}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      missingPartyInviteTarget.RootElement, "PartyAction", out var reason) &&
                  reason.Contains("TargetDBID"),
                "party invite requires target DBID");
        }
        using (var partyDisband = Json(
            """{"Type":"PartyAction","Action":"Disband"}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      partyDisband.RootElement, "PartyAction", out _),
                "party disband remains valid without target DBID");
        }
        using (var invalidClubName = Json(
            """{"Type":"ClubAction","Action":"Create","ClubName":"  "}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      invalidClubName.RootElement, "ClubAction", out var reason) &&
                  reason.Contains("ClubName"),
                "club creation rejects blank club names before database mutation");
        }
        using (var validClubName = Json(
            """{"Type":"ClubAction","Action":"Create","ClubName":"TestClub"}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      validClubName.RootElement, "ClubAction", out _),
                "club creation accepts recovered client name field");
        }
    }
}
