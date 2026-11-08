using System.Text.Json;
using SGO.Server.Protocol;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Protocol;

/// <summary>Independent, deterministic smoke cases; invoke Run from the suite runner.</summary>
internal static class RequestSchemaSmokeTests
{
    public static void Run()
    {
        // Request payload validation rejects incompatible JSON shapes without
        // invoking any persistence or mutation handler.
        using (var invalidNumber = Json(
            """{"Type":"BankTransfer","UID":"123","TabType":0,"Amount":1,"ToBank":true}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      invalidNumber.RootElement,
                      "BankTransfer",
                      out var invalidNumberReason) &&
                  invalidNumberReason.Contains("UID"),
                "packet schema rejects numeric fields sent as strings");
        }
        using (var invalidBoolean = Json(
            """{"Type":"TradeReply","RequesterDBID":123,"Accept":"true"}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      invalidBoolean.RootElement,
                      "TradeReply",
                      out var invalidBoolReason) &&
                  invalidBoolReason.Contains("Accept"),
                "packet schema rejects boolean fields sent as strings");
        }
        using (var missingRequired = Json(
            """{"Type":"UseSkill","TargetID":42}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      missingRequired.RootElement,
                      "UseSkill",
                      out var missingReason) &&
                  missingReason.Contains("SkillID"),
                "packet schema rejects missing required skill field");
        }
        using (var validBankPacket = Json(
            """{"Type":"BankTransfer","UID":9,"TabType":0,"Amount":1,"ToBank":false}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      validBankPacket.RootElement,
                      "BankTransfer", out _),
                "packet schema accepts recovered client bank payload");
        }
        using (var optionalSkillTarget = Json(
            """{"Type":"UseSkill","SkillID":26104}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      optionalSkillTarget.RootElement,
                      "UseSkill", out _),
                "packet schema permits optional no-target skill fields");
        }

        using (var badDismantle = Json(
            """{"Type":"DismantleItemRequest","ItemUIDs":{"uid":1},"ItemType":"Equip"}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      badDismantle.RootElement,
                      "DismantleItemRequest", out var badDismantleReason) &&
                  badDismantleReason.Contains("ItemUIDs"),
                "packet schema rejects dismantle IDs that are not a JSON array");
        }
        using (var goodDismantle = Json(
            """{"Type":"DismantleItemRequest","ItemUIDs":[10,11],"ItemType":"Equip"}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      goodDismantle.RootElement,
                      "DismantleItemRequest", out _),
                "packet schema accepts client dismantle array payload");
        }
        using (var missingCraftBatch = Json(
            """{"Type":"CraftItemRequest","RecipeID":5}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      missingCraftBatch.RootElement,
                      "CraftItemRequest", out var craftReason) &&
                  craftReason.Contains("BatchCount"),
                "packet schema rejects incomplete craft requests");
        }
        using (var invalidAction = Json(
            """{"Type":"ShopAction","Action":"   "}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      invalidAction.RootElement, "ShopAction", out _),
                "packet schema rejects blank dynamic shop actions");
        }

        using (var oversizedInt = Json(
            """{"Type":"SyncPosition","X":2147483648,"Y":100}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      oversizedInt.RootElement,
                      "SyncPosition", out var rangeError) &&
                  rangeError.Contains("X"),
                "packet schema rejects Int32 overflow before req.Int fallback");
        }
        using (var validLongTransfer = Json(
            """{"Type":"DirectTransfer","TargetDBID":2147483648,"Amount":4294967296}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      validLongTransfer.RootElement,
                      "DirectTransfer", out _),
                "packet schema preserves Int64 transfer IDs and amounts");
        }
        using (var invalidMixedUidArray = Json(
            """{"Type":"DismantleItemRequest","ItemUIDs":[11,"12",13],"ItemType":"Equip"}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      invalidMixedUidArray.RootElement,
                      "DismantleItemRequest", out var arrayError) &&
                  arrayError.Contains("ItemUIDs"),
                "packet schema rejects mixed-type dismantle UID arrays");
        }
        using (var oversizedUidArray = Json(JsonSerializer.Serialize(new
        {
            Type = "DismantleItemRequest",
            ItemUIDs = Enumerable.Range(1, 513).ToArray(),
            ItemType = "Equip"
        })))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      oversizedUidArray.RootElement,
                      "DismantleItemRequest", out _),
                "packet schema rejects oversized dismantle arrays");
        }

        using (var missingTradeMoney = Json(
            """{"Type":"TradeSetMoney"}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      missingTradeMoney.RootElement, "TradeSetMoney", out _) &&
                  !PacketSchemaValidator.TryValidate(
                      Json("""{"Type":"TradeAddItem","UID":1,"TabType":0}""").RootElement,
                      "TradeAddItem", out _),
                "trade money and offered item count are mandatory");
        }
        using (var missingNpcBuyCount = Json(
            """{"Type":"ShopAction","Action":"BuyNpcItem","ShopID":7,"ItemIndex":100}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      missingNpcBuyCount.RootElement, "ShopAction", out var shopReason) &&
                  shopReason.Contains("Count"),
                "NPC purchase action requires valid quantity");
        }
        using (var brokenSellerPrice = Json(
            """{"Type":"ShopAction","Action":"AddItem","UID":9,"Count":2,"Price":"100","TabType":0}"""))
        {
            Check(!PacketSchemaValidator.TryValidate(
                      brokenSellerPrice.RootElement, "ShopAction", out var sellerReason) &&
                  sellerReason.Contains("Price"),
                "player shop rejects string price without mutating inventory");
        }
        using (var closePlayerShop = Json(
            """{"Type":"ShopAction","Action":"CloseShop"}"""))
        {
            Check(PacketSchemaValidator.TryValidate(
                      closePlayerShop.RootElement, "ShopAction", out _),
                "shop close remains valid without buy/sell-only fields");
        }

        // Optional fields are deliberately not all in PacketSchemaValidator.
        // Their conversion helpers must never throw for malformed JSON values.
        using (var badOptionalNumbers = Json(
            """{"Count":"2","UID":true,"Unknown":null}"""))
        {
            Check(badOptionalNumbers.RootElement.Int("Count", 7) == 7 &&
                  badOptionalNumbers.RootElement.Long("UID", 9) == 9 &&
                  badOptionalNumbers.RootElement.Long("Unknown", 11) == 11 &&
                  badOptionalNumbers.RootElement.Int("Missing", 13) == 13,
                "optional numeric fields safely return fallback for wrong types");
        }
        using (var jsonNonObject = Json("""[1,2,3]"""))
        {
            Check(jsonNonObject.RootElement.Int("Count", 7) == 7 &&
                  jsonNonObject.RootElement.Long("UID", 9) == 9 &&
                  jsonNonObject.RootElement.Str("Name", "fallback") == "fallback",
                "JSON numeric and text helpers safely reject non-object input");
        }
    }
}
