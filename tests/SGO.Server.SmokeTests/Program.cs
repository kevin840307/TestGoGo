using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using SGO.Server.Application;
using SGO.Server.Application.Assets;
using SGO.Server.Application.Bank;
using SGO.Server.Application.Packets;
using SGO.Server.Application.Party;
using SGO.Server.Application.Combat;
using SGO.Server.Application.Crafting;
using SGO.Server.Application.Club;
using SGO.Server.Application.Rank;
using SGO.Server.Application.Quest;
using SGO.Server.Application.Character;
using SGO.Server.Application.Gm;
using SGO.Server.Application.Items;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.Mall;
using SGO.Server.Application.Shops;
using SGO.Server.Application.Trade;
using SGO.Server.Application.World;
using SGO.Server.Domain.Sessions;
using SGO.Server.Domain.World;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Quest;
using SGO.Server.Infrastructure.WorldData;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.Protocol;
using System.Text.Json;

static void Check(bool ok, string msg)
{
    if (!ok) throw new Exception("FAILED: " + msg);
    Console.WriteLine("PASS: " + msg);
}

static JsonDocument Json(string value) => JsonDocument.Parse(value);

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

var repoRoot = Directory.GetCurrentDirectory();
var gameDataRoot = Path.Combine(repoRoot, "Old", "服務端", "Dat");
var questRoot = Path.Combine(gameDataRoot, "Quest");

Check(Directory.Exists(gameDataRoot), "server data directory exists");

var world = new WorldDataService(gameDataRoot);

var blockingTestRoot = Path.Combine(
    Path.GetTempPath(),
    "sgo-blocking-" + Guid.NewGuid().ToString("N"));
var blockingMapRoot = Path.Combine(
    blockingTestRoot,
    "Map",
    "Map01");
Directory.CreateDirectory(blockingMapRoot);

var blockingTestPath = Path.Combine(
    blockingMapRoot,
    "Blocking.dat");
using (var writer = new BinaryWriter(
           File.Open(
               blockingTestPath,
               FileMode.Create,
               FileAccess.Write,
               FileShare.None)))
{
    writer.Write(5);
    writer.Write(1);
    writer.Write(new byte[] { 0, 0, 1, 0, 3 });
}

var blocking = new BlockingMapService(blockingTestRoot);
Check(blocking.HasMap(1),
    "Blocking.dat parser loads original 8-byte header grid");
Check(blocking.IsMoveAllowed(
          1, 8, 8, 24, 8, false),
    "Blocking.dat movement accepts clear cells");
Check(!blocking.IsMoveAllowed(
          1, 8, 8, 40, 8, false),
    "Blocking.dat movement rejects a segment crossing blocked cell");

var cornerRoot = Path.Combine(
    Path.GetTempPath(),
    "sgo-blocking-corner-" + Guid.NewGuid().ToString("N"));
var cornerMapRoot = Path.Combine(
    cornerRoot,
    "Map",
    "Map01");
Directory.CreateDirectory(cornerMapRoot);

using (var writer = new BinaryWriter(
           File.Open(
               Path.Combine(cornerMapRoot, "Blocking.dat"),
               FileMode.Create,
               FileAccess.Write,
               FileShare.None)))
{
    writer.Write(3);
    writer.Write(3);
    writer.Write(new byte[]
    {
        0, 1, 0,
        0, 0, 0,
        0, 0, 0
    });
}

var cornerBlocking = new BlockingMapService(cornerRoot);
Check(cornerBlocking.IsMoveAllowed(
          1,
          8,
          8,
          40,
          8,
          false),
    "Blocking.dat server validation allows a short legal route around a corner");

Directory.Delete(
    cornerRoot,
    recursive: true);

// Monster movement must use strict line segments, not ValidateMove's
// permissive local-route rule for received player positions.
var navigator = new MonsterNavigationService(blocking);
Check(!navigator.IsDirectClear(1, 8, 8, 40, 8),
    "monster cannot cross a blocked cell even if a local detour exists");
Check(!navigator.IsDirectClear(1, 56, 8, 72, 8),
    "monster cannot cross special-terrain flag 3");
Check(!navigator.TryNextStep(1, 56, 8, 72, 8, 16, out _, out _),
    "monster cannot route into special terrain without permission");
Check(!navigator.IsDirectClear(99, 8, 8, 24, 8),
    "monster movement fails closed when Blocking.dat is missing");

// A vertical wall with an opening at the bottom forces an A* detour.
// Deliberately test the diagonal edge beside the wall too.
var navMapRoot = Path.Combine(blockingTestRoot, "Map", "Map02");
Directory.CreateDirectory(navMapRoot);
using (var writer = new BinaryWriter(
           File.Create(Path.Combine(navMapRoot, "Blocking.dat"))))
{
    writer.Write(7);
    writer.Write(5);
    writer.Write(new byte[]
    {
        0,0,0,1,0,0,0,
        0,0,0,1,0,0,0,
        0,0,0,1,0,0,0,
        0,0,0,0,0,0,0,
        0,0,0,0,0,0,0
    });
}
Check(!navigator.IsDirectClear(2, 40, 24, 56, 24) &&
      !navigator.IsDirectClear(2, 40, 24, 56, 40),
    "monster cannot step through a wall or cut its diagonal corner");
var navX = 24.0;
var navY = 24.0;
var followedValidRoute = true;
for (var i = 0; i < 40 &&
                 Math.Sqrt(Math.Pow(88 - navX, 2) +
                           Math.Pow(24 - navY, 2)) > 1; i++)
{
    var oldX = navX;
    var oldY = navY;
    if (!navigator.TryNextStep(2, oldX, oldY, 88, 24, 16,
                              out navX, out navY) ||
        !navigator.IsDirectClear(2, oldX, oldY, navX, navY))
    {
        followedValidRoute = false;
        break;
    }
}
Check(followedValidRoute &&
      Math.Abs(navX - 88) <= 1 &&
      Math.Abs(navY - 24) <= 1,
    "monster A-star chase walks around a wall without clipping through it");

Check(!blocking.IsMoveAllowed(
          1, 56, 8, 72, 8, false) &&
      blocking.IsMoveAllowed(
          1, 56, 8, 72, 8, true),
    "Blocking flag 3 requires special-terrain capability");
Check(!blocking.IsMoveAllowed(
          99, 8, 8, 24, 8, false),
    "missing Blocking.dat fails closed");

Directory.Delete(
    blockingTestRoot,
    recursive: true);

var realCurrentCatalog = new CurrentClientDataCatalog(
    Path.Combine(repoRoot, "client"));

Check(realCurrentCatalog.FindSkillLevel(
          31112,
          1) is not null,
    "real client loader keeps tick-only 隨身修理店 skill level");

Check(realCurrentCatalog.FindSkillLevel(
          23108,
          1) is { AtkBonusPercent: 50, DurationSeconds: 60 },
    "real Skill.csv enriches 狂戰士體 with +50% attack");

Check(realCurrentCatalog.FindSkillLevel(
          26110,
          1) is { MaxMpBonusPercent: 100, DurationSeconds: 600 },
    "real Skill.csv enriches 魔法之泉 with +100% MaxMP");

Check(realCurrentCatalog.FindSkillLevel(
          27104,
          1) is { DeathExpReductionPercent: 10 } &&
      realCurrentCatalog.FindSkillLevel(
          27104,
          3) is { DeathExpReductionPercent: 30 } &&
      realCurrentCatalog.FindSkillLevel(
          27111,
          1) is { DeathExpReductionPercent: 100 },
    "real Skill.csv exposes revive death-EXP reduction percentages");

Check(realCurrentCatalog.ResolveSkillBook(3701) is
      { SkillId: 27103, SkillLevel: 1 },
    "general 加速術一級 book resolves to current skill 27103 Lv1");

Check(realCurrentCatalog.ResolveSkillBook(3797) is
      { SkillId: 29106, SkillLevel: 2 },
    "general 雷之閃影二級 book resolves to current skill 29106 Lv2");

Check(realCurrentCatalog.ResolveSkillBook(3239) is null,
    "crafting blueprint is not misclassified as a skill book");

Check(realCurrentCatalog.ResolveExperienceCard(2460) is
      { MultiplierPercent: 200, DurationSeconds: 3600 } &&
      realCurrentCatalog.ResolveExperienceCard(2461) is
      { MultiplierPercent: 200, DurationSeconds: 10800 },
    "double-EXP cards resolve 2x multiplier and original 1h/3h durations");

Check(realCurrentCatalog.FindItem(2459) is
      { ScriptName: "無罪之書" },
    "real innocence book exposes its authoritative client script name");

CurrentItemDefinition CurrentItem(
    int id,
    string name,
    string category,
    int price = 100,
    int canSell = 1,
    int atk = 0,
    int def = 0,
    int matk = 0,
    int mdef = 0,
    int hit = 0,
    int flee = 0,
    int moveSpeed = 0,
    int maxHpBonus = 0,
    int equipSlot = 0,
    int attackRange = 0,
    int recoverHp = 0,
    int recoverMp = 0,
    int recoverSp = 0,
    int maxBattery = 0,
    int battery = 0,
    string horseType = "",
    int reqLevel = 0,
    int durability = 0,
    int canRepair = 1,
    int canTrade = 1,
    string scriptName = "")
    => new(
        id,
        name,
        category,
        price,
        canSell,
        atk,
        def,
        matk,
        mdef,
        hit,
        flee,
        0, // Luk
        0, // Aspd
        moveSpeed,
        0, // CritRate
        0, // CritDamage
        maxHpBonus,
        0, // MaxMpBonus
        0, // MaxSpBonus
        equipSlot,
        attackRange,
        recoverHp,
        recoverMp,
        recoverSp,
        maxBattery,
        battery,
        horseType,
        reqLevel,
        durability,
        canRepair,
        canTrade,
        scriptName);

var currentCatalog = new CurrentClientDataCatalog(
    new[]
    {
        CurrentItem(
            1001,
            "小藥水",
            "recovery",
            price: 20,
            recoverHp: 140),

        CurrentItem(
            2126,
            "幸福娃娃",
            "general",
            price: 1),

        CurrentItem(
            2803,
            "大聲公",
            "general",
            price: 1),

        CurrentItem(
            2460,
            "雙倍經驗值卡(1小時)",
            "general",
            price: 1,
            scriptName: "雙倍經驗值卡(1小時)"),

        CurrentItem(
            2459,
            "無罪之書",
            "general",
            price: 1,
            scriptName: "無罪之書"),

        CurrentItem(
            3701,
            "加速術．一級",
            "general",
            price: 1,
            scriptName: "加速術．一級"),

        CurrentItem(
            2615,
            "精靈之魂",
            "general",
            price: 1),

        CurrentItem(
            2617,
            "精靈再生水晶",
            "general",
            price: 1),

        // Legacy Shop2 item 31078 is 淺藍色初心褲子.
        // Current client moved it to 18538.
        CurrentItem(
            18538,
            "淺藍色初心褲子",
            "equip",
            price: 1,
            def: 5,
            equipSlot: 4,
            durability: 120),

        // Map15 shop_e3 -> Shop7 bullets.
        CurrentItem(
            4103,
            "合金彈",
            "general",
            price: 1,
            canSell: 0),
        CurrentItem(
            4112,
            "魔咒彈",
            "general",
            price: 1,
            canSell: 0),

        // Map15 shop_e3 -> Shop10 weapons.
        CurrentItem(
            31548,
            "小鐵劍",
            "weapon",
            price: 50,
            atk: 10,
            equipSlot: 5,
            attackRange: 140,
            durability: 100),
        CurrentItem(
            31262,
            "雙銃雷霆砲+16",
            "weapon",
            price: 999,
            atk: 99,
            equipSlot: 5,
            attackRange: 180),
        CurrentItem(
            49962,
            "戰鬥蝕日斧",
            "weapon",
            price: 500,
            atk: 42,
            equipSlot: 5,
            attackRange: 130),

        CurrentItem(
            50001,
            "機甲卡",
            "horse",
            maxBattery: 25200,
            horseType: "MS"),
        CurrentItem(
            50292,
            "戰鬥型先驅者",
            "horseequip",
            price: 100,
            def: 9,
            moveSpeed: -100,
            equipSlot: 0),
        CurrentItem(
            50293,
            "戰鬥型鋼臂",
            "horseequip",
            price: 100,
            atk: 18,
            equipSlot: 1,
            attackRange: 50,
            durability: 100),
        CurrentItem(
            50297,
            "戰鬥型二足引擎",
            "horseequip",
            price: 100,
            moveSpeed: 100,
            equipSlot: 2),
        CurrentItem(
            51304,
            "獸靈卡片",
            "horseequip",
            price: 100,
            atk: 100,
            def: 50,
            matk: 100,
            mdef: 50,
            hit: 100,
            flee: 100,
            maxHpBonus: 500,
            equipSlot: 3,
            horseType: "MS",
            reqLevel: 20),
        CurrentItem(
            50102,
            "魔靈電池",
            "horsebattery",
            price: 1200,
            battery: 3600)
    },
    new[]
    {
        new CurrentSkillDefinition(20101, "Current Skill 20101"),
        new CurrentSkillDefinition(20102, "Current Skill 20102"),
        new CurrentSkillDefinition(77701, "烈火爆劍"),
        new CurrentSkillDefinition(27103, "加速術"),
        new CurrentSkillDefinition(27104, "生命之源"),
        new CurrentSkillDefinition(28102, "心靈轉換"),
        new CurrentSkillDefinition(28107, "治療風暴"),
        new CurrentSkillDefinition(
            29101,
            "製彈術",
            "AddItem:4103:100"),
        new CurrentSkillDefinition(
            25101,
            "體力回復",
            "體力回復"),
        new CurrentSkillDefinition(
            25106,
            "大氣旋風",
            "大氣旋風"),
        new CurrentSkillDefinition(
            25108,
            "生命聖印",
            "生命聖印"),
        new CurrentSkillDefinition(
            25104,
            "生命吸收",
            "生命吸收"),
        new CurrentSkillDefinition(
            23108,
            "狂戰士體"),
        new CurrentSkillDefinition(
            33011,
            "冰之觸",
            "冰之觸"),
        new CurrentSkillDefinition(
            33012,
            "黑暗之影"),
        new CurrentSkillDefinition(
            31112,
            "隨身修理店"),
        new CurrentSkillDefinition(
            27110,
            "平安之祝",
            "平安之祝"),
        new CurrentSkillDefinition(
            21103,
            "挑撥",
            "挑撥"),
        new CurrentSkillDefinition(
            20103,
            "偵察",
            "偵察"),
        new CurrentSkillDefinition(26104, "魔影術"),
        new CurrentSkillDefinition(45111, "月神審判")
    },
    new[]
    {
        new CurrentPetDefinition(
            45001,
            "波奇鳥獸",
            new Dictionary<int, CurrentPetEvolution>
            {
                [0] = new CurrentPetEvolution(
                    0,
                    "EN050A",
                    0,
                    80,
                    NextEvolIds: new[] { 1 }),
                [1] = new CurrentPetEvolution(
                    1,
                    "EN010A",
                    2,
                    80,
                    MinLevel: 2,
                    RequiredStr: 1,
                    NextEvolIds: new[] { 2 }),
                [2] = new CurrentPetEvolution(
                    2,
                    "EN011A",
                    0,
                    80,
                    MinLevel: 10,
                    RequiredStr: 10,
                    NextEvolIds: Array.Empty<int>())
            })
    },
    new[]
    {
        new CurrentSkillLevelDefinition(
            29101, 1, 7, 0,
            0, 0,
            0, 20, 0,
            0, 0, 1,
            0, 0,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0,
            0, 0, 0, 0),
        new CurrentSkillLevelDefinition(
            21103, 1, 7, 0,
            0, 150,
            0, 20, 0,
            0, 0, 2,
            0, 1,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0,
            0, 0, 0, 0),
        new CurrentSkillLevelDefinition(
            20103, 1, 7, 0,
            350, 0,
            0, 10, 0,
            0, 0, 0,
            0, 1,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0,
            0, 0, 0, 0),
        new CurrentSkillLevelDefinition(
            27110, 1, 7, 0,
            0, 0,
            0, 0, 0,
            0, 0, 1800,
            0, 1,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0,
            0, 0, 0, 0),
        new CurrentSkillLevelDefinition(
            25101, 1, 7, 0,
            0, 0,
            0, 22, 0,
            0, 10, 1,
            0, 1,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0,
            0, 0, 0, 0),
        new CurrentSkillLevelDefinition(
            25106, 1, 7, 0,
            0, 0,
            0, 0, 26,
            0, 20, 1,
            0, 1,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0,
            0, 0, 0, 0),
        new CurrentSkillLevelDefinition(
            25104, 1, 7, 0,
            200, 0,
            0, 0, 69,
            60, 90, 2,
            0, 1,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0,
            0, 0, 0, 0),
        new CurrentSkillLevelDefinition(
            23108, 1, 4, 0,
            0, 0,
            0, 0, 0,
            0, 0, 1800,
            60, 1,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0)
        {
            AtkBonusPercent = 50
        },
        new CurrentSkillLevelDefinition(
            33011, 1, 7, 0,
            0, 0,
            0, 209, 0,
            0, 0, 1,
            900, 1,
            0, 0, 0,
            0, 80, 0, 80,
            0, 0, 0, 0,
            0, 0, 0),
        new CurrentSkillLevelDefinition(
            33012, 1, 6, 0,
            0, 0,
            0, 213, 17,
            0, 0, 1,
            0, 1,
            0, 0, 0,
            80, 0, 80, 0,
            0, 0, 0, 0,
            0, 0, 0),
        new CurrentSkillLevelDefinition(
            31112, 1, 6, 0,
            0, 0,
            0, 0, 0,
            0, 0, 0,
            0, 1,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0),
        new CurrentSkillLevelDefinition(
            25108, 1, 7, 0,
            0, 0,
            0, 0, 0,
            0, 0, 1800,
            0, 1,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0,
            0, 0, 0, 0),
        new CurrentSkillLevelDefinition(
            28102, 1, 2, 0,
            250, 0,
            0, 38, 0,
            0, 5, 1,
            0, 1,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0,
            0, 0, 0, 0),
        new CurrentSkillLevelDefinition(
            28107, 1, 3, 0,
            0, 0,
            0, 0, 32,
            0, 10, 1,
            0, 1,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0,
            0, 0, 0, 0),
        new CurrentSkillLevelDefinition(
            27103, 1, 4, 0,
            0, 0,
            0, 10, 0,
            0, 0, 1,
            60, 1,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0,
            0, 0, 0, 0),
        new CurrentSkillLevelDefinition(
            27104, 1, 6, 0,
            250, 0,
            0, 50, 0,
            0, 0, 1,
            0, 1,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0,
            0, 0, 0, 0),
        new CurrentSkillLevelDefinition(
            26104, 5, 1, 1,
            280, 280,
            0, 126, 0,
            350, 155, 2,
            0, 0,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0,
            0, 0, 0, 0),
        new CurrentSkillLevelDefinition(
            45111, 1, 7, 0,
            300, 300,
            0, 100, 0,
            0, 0, 2,
            0, 0,
            0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0,
            0, 0, 0, 0)
    },
    new[]
    {
        new CurrentHorsePreset(
            50001, 50292, 50293, 50297)
    });

var legacyClientCatalog = new LegacyClientDataCatalog(
    new (string Category, int Id, string Name)[]
    {
        ("weapon", 31262, "戰鬥蝕日斧")
    },
    new (int Id, string Name)[]
    {
        (1103, "烈火爆劍")
    });

var shopCatalog = new ShopCatalog(
    gameDataRoot,
    currentCatalog,
    legacyClientCatalog);
var enemyDrops = new EnemyDropCatalog(
    gameDataRoot,
    currentCatalog);
var questItemMappings = new EnemyDropCatalog(
    gameDataRoot,
    realCurrentCatalog);
var batteryCatalog = new HorseBatteryCatalog(currentCatalog);
var recoveryCatalog = new RecoveryItemCatalog(currentCatalog);
var horseCardCatalog = new HorseCardCatalog(currentCatalog);
var skillCatalog = new SkillCatalog(currentCatalog);
var quests = new QuestRuntime(
    questRoot,
    shopCatalog,
    questItemMappings,
    legacyClientCatalog,
    currentCatalog);
var map15Npcs = world.GetNpcs(15);

Check(map15Npcs.Count == 52, "Map15 NPC.dat contains 52 NPC records");
Check(map15Npcs[0].Script == "KingCity(P06)_to_KingCity(P08).txt",
    "Map15 first portal script parsed");
Check(map15Npcs[0].X == 4932 && map15Npcs[0].Y == 7783,
    "Map15 first portal coordinates parsed");

Check(DialogFormatter.CleanText("n要進入尼尼亞皇城嗎？") == "要進入尼尼亞皇城嗎？" &&
      DialogFormatter.CleanText("n1你好") == "你好" &&
      DialogFormatter.CleanText("p7取得道具") == "取得道具",
    "dialog formatter strips presentation prefixes");

var bankResult = quests.Execute(
    "Bank.txt", "start", 1, "銀行", null, _ => 0);
Check(bankResult.Packets.Any(x =>
        JsonSerializer.Serialize(x).Contains("\"Type\":\"OpenBankWindow\"")),
    "OpenBank emits client OpenBankWindow");

var repairResult = quests.Execute(
    "Repair.txt", "start", 2, "修理店", null, _ => 0);
Check(repairResult.Packets.Any(x =>
        JsonSerializer.Serialize(x).Contains("\"Type\":\"OpenRepairWindow\"") &&
        JsonSerializer.Serialize(x).Contains("\"RepairType\":10")),
    "OpenRepair emits repair window and type");

var shopStart = quests.Execute(
    "shop_e3.txt", "start", 3, "商店", null, _ => 0);
Check(shopStart.Packets.Any(x =>
        JsonSerializer.Serialize(x).Contains("\"Type\":\"NpcDialogResponse\"")),
    "shop start opens choice dialog");

var shopBuy = quests.Execute(
    "shop_e3.txt", "reply", 3, "商店", 0, _ => 0);
Check(shopBuy.Packets.Any(x =>
        JsonSerializer.Serialize(x).Contains("\"Type\":\"NpcShopViewResponse\"") &&
        JsonSerializer.Serialize(x).Contains("\"ShopID\":7")),
    "OpenShop emits client NPC shop response");

var ranking = quests.Execute(
    "Ranking.txt", "Check", 4, "排行", 0, _ => 0);
Check(ranking.Packets.Any(x =>
        JsonSerializer.Serialize(x).Contains("\"Type\":\"OpenRankWindow\"") &&
        JsonSerializer.Serialize(x).Contains("\"RankType\":1")),
    "RankQuery emits OpenRankWindow");

var reborn = quests.Execute(
    "King_RebornSeting.txt", "set", 5, "復活點", 0, _ => 0);
Check(reborn.RebornMapId == 15 &&
      reborn.RebornX == 9408 &&
      reborn.RebornY == 10336,
    "SetRebornLocation captured");

var addGiftResult = quests.Execute(
    "Add_Gift.txt",
    "CheckGift",
    5,
    "虛寶管理員",
    0,
    _ => 0);

Check(addGiftResult.Packets.Any(x =>
        JsonSerializer.Serialize(x).Contains(
            "你沒有未領取的虛擬道具")),
    "legacy AddGift safely dispatches None callback when no gift provider is configured");

var tsPortal = quests.Execute(
    "TS_KINGTD_KING.txt", "start", 6, "傳送點", null, _ => 0);
Check(tsPortal.Packets.Any(x =>
        JsonSerializer.Serialize(x).Contains("\"Type\":\"NpcDialogResponse\"")),
    "#include TS_Common parses and opens dialog");

var careerNpc = quests.Execute(
    "K27001_A.txt", "start", 7, "皇家侍衛", null,
    key => key == "Level" ? 1 : 0);
Check(careerNpc.Packets.Any(x =>
        JsonSerializer.Serialize(x).Contains("\"Type\":\"NpcDialogResponse\"")),
    "nonstandard smart-quote escapes are normalized");

var legacyCareer = 0;
var legacyJob = 0;
var legacyCareerFlag = 0;

var legacyJobResult = quests.Execute(
    "Job_Test.txt",
    "KnightJob",
    8,
    "職業管理人",
    0,
    key => key == "Race" ? 0 : 0,
    new QuestRuntimeBindings
    {
        SetCareer = (career, job) =>
        {
            legacyCareer = career;
            legacyJob = job;
            return true;
        },
        SetFlag = (flag, value) =>
        {
            if (flag == "職業測試完成")
                legacyCareerFlag = value;
        }
    });

Check(legacyCareer == 2 &&
      legacyJob == 1 &&
      legacyCareerFlag == 1 &&
      !legacyJobResult.Packets.Any(x =>
          JsonSerializer.Serialize(x).Contains(
              "NPC 腳本執行失敗")),
    "original Job_Test KnightJob executes SetCareer and completion flag");

var map15Enemies = world.GetEnemies(15);
Check(map15Enemies.Count == 4,
    "Map15 Enemy.dat binary parser reads four records");
Check(map15Enemies.All(x => x.EnemyId == 245),
    "Map15 enemy records resolve EnemyID 245");
Check(world.BuildMapSpawnPackets(15).Count == 52,
    "Map15 city suppresses combat enemy spawns");

var map15Merchant = map15Npcs.First(x => x.IsMerchant);
Check(map15Merchant.NpcTypeId == 393 &&
      map15Merchant.Sprite == "NPC137.png",
    "Map15 merchant uses low-byte NPC sprite fallback when legacy visual mapping is unavailable");
Check(shopCatalog.GetShopItems(7).Any(x => x.ItemIndex == 4103) &&
      shopCatalog.GetShopItems(7).Any(x => x.ItemIndex == 4112),
    "Map15 Shop7 maps old bullet names to current client IDs");
Check(shopCatalog.GetShopItems(10).Any(x => x.ItemIndex == 31548),
    "Map15 Shop10 maps old weapon name to current client ID");
Check(shopCatalog.GetShopItems(2).Any(x => x.ItemIndex == 18538) &&
      !shopCatalog.GetShopItems(2).Any(x => x.ItemIndex == 31078),
    "legacy Shop2 equip maps by name from old 31078 to current 18538");
Check(shopCatalog.MappingWarnings.Any(x =>
        x.Contains("[OLD_NAME_MISSING]") &&
        x.Contains("Shop=1")),
    "unconfirmed legacy recovery IDs are skipped instead of reused");

Check(shopCatalog.GetShopItems(9).Any(x =>
          x.ItemIndex == 49962 &&
          x.Name == "戰鬥蝕日斧") &&
      !shopCatalog.GetShopItems(9).Any(x =>
          x.ItemIndex == 31262),
    "Shop9 remaps legacy 31262 戰鬥蝕日斧 to current item ID by confirmed name");

Check(shopCatalog.MappingInfo.Any(x =>
        x.Contains("[NAME_REMAP]") &&
        x.Contains("LegacyItemID=31262") &&
        x.Contains("CurrentItemID=49962") &&
        x.Contains("戰鬥蝕日斧")),
    "Shop9 collision logs exact legacy-to-current name remap");
Check(batteryCatalog.GetEnergy(50102) == 3600,
    "current mecha battery 50102 resolves 3600 seconds");
Check(recoveryCatalog.Find(1001) is { RecoverHp: 140 },
    "current recovery item 1001 resolves +140 HP");
Check(horseCardCatalog.Find(50001) is { MaxBattery: 25200 },
    "current mecha card 50001 resolves max battery");
Check(currentCatalog.FindSkillLevel(27104, 1) is
      { SkillType: 6, CostMp: 50, CastRange: 250 },
    "current 生命之源 resolves revive-skill cast data");
Check(currentCatalog.FindSkillLevel(28102, 1) is
      { SkillType: 2, CostMp: 38, DamageMultiplier: 5 },
    "current 心靈轉換 resolves single-target SP recovery data");
Check(currentCatalog.FindSkillLevel(28107, 1) is
      { SkillType: 3, CostSp: 32, DamageMultiplier: 10 },
    "current 治療風暴 resolves party recovery data");
Check(currentCatalog.FindSkill(29101) is
      { SpecialSkill: "AddItem:4103:100" },
    "current 製彈術 exposes data-driven AddItem special metadata");
Check(currentCatalog.FindSkillLevel(25108, 1) is
      { SkillType: 7, CooldownSeconds: 1800 },
    "current 生命聖印 resolves full-HP special skill data");
Check(currentCatalog.FindSkillLevel(21103, 1) is
      { SkillType: 7, AoeRadius: 150, CostMp: 20 },
    "current 挑撥 resolves monster-aggro radius data");
Check(currentCatalog.FindSkillLevel(20103, 1) is
      { SkillType: 7, CastRange: 350, CostMp: 10 },
    "current 偵察 resolves player-target range data");
Check(currentCatalog.FindSkillLevel(26104, 5) is
      { CostMp: 126, CastRange: 280, AoeRadius: 280 },
    "current 魔影術 Lv5 resolves server-side cast data");
Check(currentCatalog.FindSkillLevel(45111, 1) is
      { SkillType: 7, CostMp: 100, CastRange: 300, AoeRadius: 300 },
    "current 月神審判 resolves special-skill cast data");
Check(currentCatalog.FindHorsePreset(50001) is
      { BodyId: 50292, WeaponId: 50293, EngineId: 50297 },
    "current mecha 50001 resolves default body weapon engine preset");

var payload = new { Type = "LoginRequest", User = "offline", Pass = "test" };
var ciphertext = ClientProtocol.Encode(payload);
using (var packet = ClientProtocol.Decode(ciphertext))
{
    Check(packet.RootElement.Str("Type") == "LoginRequest",
        "AES-CBC/PKCS7 packet round-trip");
    Check(packet.RootElement.Str("User") == "offline",
        "JSON decoded fields");
}

var questApiRoot = Path.Combine(
    Path.GetTempPath(),
    "sgo-quest-api-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(questApiRoot);
try
{
    File.WriteAllText(
        Path.Combine(questApiRoot, "QuestApi.txt"),
        """
        function start()
            SetFlag("TEST_FLAG", GetFlag("TEST_FLAG") + 1)
            if CheckItem(31262) == 0 then
                AddItem(31262, 2)
            end
            if CheckItem(31262) == 1 then
                RemoveItem(31262, 1)
            end
        end

        function team()
            caller = GetCallerPtr()
            hasmember, member = GetTeamList_First()
            if hasmember == 1 then
                SetTargetFlag(member, "TEAM_FLAG", 9)
                member_map = GetTargetData(member, "MapID")
                member_x, member_y = GetTargetXY(member)
                member_emotion = GetTargetEmotion(member)
            end
            hasnext, nextmember = GetTeamList_Next()
        end

        function title()
            if HasTitle(21) == 0 then
                AddTitle(21)
            end
            AddTitle(21)
            SetData("Title", 21)
        end

        function career_skill()
            SetCareer(2, 1)
            if HasSkill(1103) == 0 then
                AddSkill(1103)
            end
            learned = HasSkill(1103)
            item_count = GetCountItemCount(31262)
        end

        function legacy_random()
            SetFlag("RANDOM_ONE", random(10))
            SetFlag("RANDOM_RANGE", random(5, 7))
        end
        """);

    var runtimeFlag = 0;
    var runtimeItemCount = 0;
    var runtimeMappedItemId = 0;
    var runtimeMappedTab = -1;
    long runtimeTeamTarget = 0;
    var runtimeTeamFlag = 0;

    var questApiRuntime = new QuestRuntime(
        questApiRoot,
        shopCatalog,
        enemyDrops,
        legacyClientCatalog,
        currentCatalog);

    questApiRuntime.Execute(
        "QuestApi.txt",
        "start",
        999,
        "Quest API Test",
        null,
        _ => 0,
        new QuestRuntimeBindings
        {
            GetFlag = _ => runtimeFlag,
            SetFlag = (_, value) => runtimeFlag = value,
            GetItemCount = (itemId, tabType) =>
            {
                runtimeMappedItemId = itemId;
                runtimeMappedTab = tabType;
                return runtimeItemCount;
            },
            ItemListCountLeft = _ => 100,
            AddItem = (itemId, tabType, count) =>
            {
                runtimeMappedItemId = itemId;
                runtimeMappedTab = tabType;
                runtimeItemCount += count;
                return true;
            },
            RemoveItem = (itemId, tabType, count) =>
            {
                runtimeMappedItemId = itemId;
                runtimeMappedTab = tabType;
                runtimeItemCount -= count;
                return true;
            }
        });

    Check(runtimeFlag == 1,
        "legacy quest GetFlag/SetFlag are routed through runtime bindings");
    Check(runtimeMappedItemId == 49962 &&
          runtimeMappedTab == 1 &&
          runtimeItemCount == 1,
        "legacy quest CheckItem/AddItem/RemoveItem safely remap old item IDs");

    var teamResult = questApiRuntime.Execute(
        "QuestApi.txt",
        "team",
        999,
        "Quest Team API Test",
        null,
        _ => 0,
        new QuestRuntimeBindings
        {
            CallerDbid = 101,
            GetTeamMemberIds = () => new long[] { 202 },
            SetTargetFlag = (target, _, value) =>
            {
                runtimeTeamTarget = target;
                runtimeTeamFlag = value;
            },
            GetTargetData = (target, key) =>
                target == 202 && key == "MapID"
                    ? 15
                    : 0,
            GetTargetXY = target =>
                target == 202
                    ? (123.0, 456.0)
                    : (0.0, 0.0),
            GetTargetEmotion = target =>
                target == 202 ? 3 : -1
        });

    Check(runtimeTeamTarget == 202 &&
          runtimeTeamFlag == 9,
        "legacy SetTargetFlag targets the current party member");

    Check(!teamResult.Packets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "NPC 腳本執行失敗")),
        "legacy team iterator/data/position/emotion APIs execute without Lua errors");

    var runtimeHasTitle = false;
    var runtimeUnlockCalls = 0;
    var runtimeCurrentTitle = 0;

    var titleResult = questApiRuntime.Execute(
        "QuestApi.txt",
        "title",
        999,
        "Quest Title API Test",
        null,
        _ => 0,
        new QuestRuntimeBindings
        {
            HasTitle = _ => runtimeHasTitle,
            UnlockTitle = _ =>
            {
                runtimeUnlockCalls++;
                runtimeHasTitle = true;
                return true;
            },
            SetData = (key, value) =>
            {
                if (!key.Equals(
                        "Title",
                        StringComparison.OrdinalIgnoreCase) ||
                    !runtimeHasTitle)
                    return false;

                runtimeCurrentTitle = value;
                return true;
            }
        });

    Check(runtimeUnlockCalls == 1 &&
          runtimeCurrentTitle == 21 &&
          titleResult.Packets.Count(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"UnlockTitle\"")) == 1,
        "legacy AddTitle unlocks once and SetData(Title) equips the unlocked title");

    var runtimeCareer = 0;
    var runtimeJob = 0;
    var runtimeLearnedSkill = 0;
    var runtimeSkillSet = new HashSet<int>();

    var careerSkillResult = questApiRuntime.Execute(
        "QuestApi.txt",
        "career_skill",
        999,
        "Quest Career Skill API Test",
        null,
        _ => 0,
        new QuestRuntimeBindings
        {
            SetCareer = (career, job) =>
            {
                runtimeCareer = career;
                runtimeJob = job;
                return true;
            },
            HasSkill = currentSkillId =>
                runtimeSkillSet.Contains(currentSkillId),
            AddSkill = currentSkillId =>
            {
                runtimeLearnedSkill = currentSkillId;
                runtimeSkillSet.Add(currentSkillId);
                return true;
            },
            GetItemCount = (itemId, tabType) =>
                itemId == 49962 && tabType == 1
                    ? 3
                    : 0
        });

    Check(runtimeCareer == 2 &&
          runtimeJob == 1 &&
          runtimeLearnedSkill == 77701 &&
          runtimeSkillSet.Contains(77701),
        "legacy SetCareer/AddSkill maps old skill 1103 by name to current skill 77701");

    Check(!careerSkillResult.Packets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "NPC 腳本執行失敗")),
        "legacy HasSkill and GetCountItemCount APIs execute with safe current mappings");

    var randomOne = 0;
    var randomRange = 0;

    questApiRuntime.Execute(
        "QuestApi.txt",
        "legacy_random",
        999,
        "Quest Random API Test",
        null,
        _ => 0,
        new QuestRuntimeBindings
        {
            SetFlag = (name, value) =>
            {
                if (name == "RANDOM_ONE")
                    randomOne = value;
                else if (name == "RANDOM_RANGE")
                    randomRange = value;
            }
        });

    Check(randomOne is >= 1 and <= 10 &&
          randomRange is >= 5 and <= 7,
        "legacy Lua random(max) and random(min,max) use inclusive ranges");
}
finally
{
    try { Directory.Delete(questApiRoot, true); } catch { }
}

var temp = Path.Combine(
    Path.GetTempPath(),
    "sgo-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);

try
{
    var dbPath = Path.Combine(temp, "game.db");
    var store = new SqliteGameStore(dbPath);
    var growth = new LegacyPlayerGrowthCatalog(gameDataRoot);
    var petGrowth = new LegacyPetGrowthCatalog(gameDataRoot);
    var petSkills = new PetSkillCatalog(
        Path.Combine(
            repoRoot,
            "client",
            "Assets",
            "Pets",
            "Config.json"));

    var petIdleSpeech = new PetIdleSpeechCatalog(
        Path.Combine(
            repoRoot,
            "server",
            "GameData",
            "pet_idle_speech.json"));

    var deterministicPetSpeech =
        petIdleSpeech.Pick(45001, 12345, 7);
    Check(!string.IsNullOrWhiteSpace(
              deterministicPetSpeech) &&
          deterministicPetSpeech ==
              petIdleSpeech.Pick(
                  45001,
                  12345,
                  7),
        "pet idle speech catalog returns deterministic configurable text");

    var questTrackingCatalog = new QuestTrackingCatalog(
        Path.Combine(
            repoRoot,
            "client",
            "Assets",
            "Quest",
            "QuestConfig.json"));
    var questProgress = new QuestProgressService(
        store,
        store,
        questTrackingCatalog);

    Check(questTrackingCatalog.Count >= 50 &&
          questTrackingCatalog.FindStep(
              1004,
              3) is
              {
                  TargetType: 1,
                  TargetId: "噬魂之影",
                  TargetCount: 1
              } &&
          questTrackingCatalog.FindStep(
              1003,
              4) is
              {
                  TargetType: 2,
                  TargetId: "2614",
                  TargetCount: 1
              },
        "QuestConfig loader exposes kill and collection tracker targets");

    Check(petGrowth.AllocationDelta(
              "STR",
              0,
              0).Attack == 2 &&
          petGrowth.AllocationDelta(
              "DEX",
              0,
              0) is { Hit: 1, Dodge: 1 } &&
          petGrowth.AllocationDelta(
              "CON",
              0,
              0).MaxHp == 41 &&
          petGrowth.AllocationDelta(
              "WIS",
              0,
              0) is { MaxMp: 21, MagicAttack: 2 },
        "legacy pet STR DEX CON WIS tables drive allocation stat deltas");

    Check(growth.ExperienceThresholdForLevel(1) == 2 &&
          growth.ExperienceThresholdForLevel(10) == 20000,
        "legacy Player_Up_Lv.csv drives character EXP thresholds");
    Check(growth.AbilityPointsGained(1, 4) == 6,
        "legacy ExpUse.csv grants two ability points per gained level");

    var battle = new LegacyBattleRulesCatalog(gameDataRoot);
    Check(battle.ExpMultiplier == 2 &&
          battle.ExperienceRatio(20, 20) == 100 &&
          battle.ExperienceRatio(24, 20) == 80 &&
          battle.ExperienceRatio(26, 20) == 50 &&
          battle.ExperienceRatio(29, 20) == 10 &&
          battle.ExperienceRatio(30, 20) == 0,
        "legacy battle.cfg drives ExpX and level-difference EXP ratios");

    var mappedEnemyDrops = Enumerable.Range(1, 300)
        .SelectMany(enemyDrops.GetDrops)
        .ToArray();

    Check(mappedEnemyDrops.Length > 0 &&
          mappedEnemyDrops.All(x =>
              currentCatalog.FindItem(x.ItemId) is not null),
        "enemy drop table exposes only safely mapped current-client ItemIDs");

    Check(mappedEnemyDrops.Any(x =>
              x.LegacyItemId == 31262 &&
              x.ItemId == 49962 &&
              x.Name == "戰鬥蝕日斧"),
        "enemy drop table remaps legacy weapon 31262 to current 49962 by name");

    Check(enemyDrops.LuckRollCount(0) == 1 &&
          enemyDrops.LuckRollCount(49) == 1 &&
          enemyDrops.LuckRollCount(50) == 2 &&
          enemyDrops.LuckRollCount(90) == 3 &&
          enemyDrops.LuckRollCount(140) == 4 &&
          enemyDrops.LuckRollCount(200) == 5,
        "enemy.cfg Luck thresholds increase only luck-checked drop rolls");

    Check(enemyDrops.OwnerProtectionSeconds == 15,
        "enemy.cfg OwnedDropItemTimeout drives server loot protection");

    var legacyTrainTicketIds =
        new[] { 10505, 10506, 10507, 10508, 10509, 10510, 10511 };

    Check(legacyTrainTicketIds.All(id =>
              questItemMappings.ResolveItem(id) is
              {
                  TabType: 0
              }),
        "all seven legacy rail tickets map to current-client inventory items");

    var broadcastHub = new ServerBroadcastHub();
    var broadcastRegistration = broadcastHub.Register();
    var accountSessions = new AccountSessionRegistry();

    var registryA = Guid.NewGuid();
    var registryB = Guid.NewGuid();
    Check(accountSessions.Register(999, registryA) is null &&
          accountSessions.Register(999, registryB) == registryA &&
          !accountSessions.IsCurrent(999, registryA) &&
          accountSessions.IsCurrent(999, registryB),
        "account session registry atomically replaces an older connection");
    accountSessions.Unregister(registryA);
    Check(accountSessions.IsCurrent(999, registryB),
        "unregistering superseded connection cannot remove current session");
    accountSessions.Unregister(registryB);

    var heartbeatBase = DateTimeOffset.UtcNow;
    Check(ConnectionHeartbeat.ShouldPing(
              DateTimeOffset.MinValue,
              heartbeatBase) &&
          ConnectionHeartbeat.ShouldPing(
              heartbeatBase,
              heartbeatBase.AddSeconds(20)) &&
          !ConnectionHeartbeat.ShouldPing(
              heartbeatBase,
              heartbeatBase.AddSeconds(19)) &&
          ConnectionHeartbeat.IsTimedOut(
              heartbeatBase,
              heartbeatBase.AddSeconds(60)),
        "heartbeat policy uses 20-second Ping and 60-second timeout");

    var worldRuntime = new WorldRuntimeService(world);
    var party = new PartyService(
        store,
        broadcastHub);
    var petEvolution = new PetEvolutionService(
        store,
        currentCatalog);
    var club = new ClubService(
        store,
        broadcastHub);
    var rank = new RankService(store);
    var mall = new MallService(store);
    var crafting = new CraftingService(
        store,
        new RecipeCatalog(
            Path.Combine(gameDataRoot, "Recipe.json")),
        currentCatalog);

    var trade = new TradeService(
        store,
        currentCatalog,
        broadcastHub);

    var combat = new CombatService(
        store,
        world,
        new BlockingMapService(gameDataRoot),
        currentCatalog,
        growth,
        battle,
        enemyDrops,
        worldRuntime,
        broadcastHub,
        party,
        petEvolution,
        questProgress,
        NullLogger<CombatService>.Instance);
    var shops = new ShopService(
        store,
        store,
        shopCatalog,
        currentCatalog);
    var playerShop = new PlayerShopService(
        store,
        store,
        currentCatalog,
        broadcastHub);
    var config = new ConfigurationManager();
    config["Game:GmCommandsEnabled"] = "true";
    config["Gameplay:ResourceRecovery:Enabled"] = "true";
    config["Gameplay:ResourceRecovery:IntervalSeconds"] = "5";
    config["Gameplay:ResourceRecovery:HpPercent"] = "10";
    config["Gameplay:ResourceRecovery:MpPercent"] = "10";
    config["Gameplay:ResourceRecovery:MinimumHp"] = "1";
    config["Gameplay:ResourceRecovery:MinimumMp"] = "1";
    var gm = new GmCommandService(
        store,
        skillCatalog,
        horseCardCatalog,
        currentCatalog,
        legacyClientCatalog,
        growth,
        combat,
        config);
    var packetHandlers = new IClientPacketHandler[]
    {
        new BankPacketHandler(new BankService(store))
    };
    var recovery = new ResourceRecoveryService(
        store,
        store,
        currentCatalog,
        config);
    config["Gameplay:Dolls:Happiness:AnnouncementRewardIds"] = "50102";
    config["Gameplay:Dolls:Happiness:AnnouncementMinimumPrice"] = "0";
    var dolls = new DollService(
        store,
        store,
        currentCatalog,
        broadcastHub,
        config);
    var router = new PacketRouter(
        store,
        world,
        new BlockingMapService(gameDataRoot),
        quests,
        combat,
        recovery,
        crafting,
        club,
        shops,
        playerShop,
        packetHandlers,
        gm,
        dolls,
        batteryCatalog,
        recoveryCatalog,
        horseCardCatalog,
        currentCatalog,
        growth,
        petGrowth,
        petSkills,
        petIdleSpeech,
        petEvolution,
        mall,
        broadcastHub,
        accountSessions,
        party,
        rank,
        questProgress,
        trade,
        NullLogger<PacketRouter>.Instance);
    var session = new ClientSession
    {
        ConnectionId = broadcastRegistration.ConnectionId
    };

    using (var malformedLogin = Json(
        """{"Type":"LoginRequest","User":"offline"}"""))
    {
        var packets = router.Handle(malformedLogin.RootElement, session);
        var json = JsonSerializer.Serialize(packets);
        Check(json.Contains("LoginResponse") &&
              json.Contains("false") &&
              !session.AccountId.HasValue,
            "malformed login gets LoginResponse failure without session mutation");
    }
    using (var malformedRegister = Json(
        """{"Type":"RegisterAccountRequest","LoginName":"n","Password":"p","EMAIL":17}"""))
    {
        var packets = router.Handle(malformedRegister.RootElement, session);
        var json = JsonSerializer.Serialize(packets);
        Check(json.Contains("RegisterAccountResponse") &&
              json.Contains("false"),
            "invalid registration email type returns RegisterAccountResponse failure");
    }
    using (var nonObject = Json("""["not-a-packet"]"""))
    {
        var packets = router.Handle(nonObject.RootElement, session);
        Check(JsonSerializer.Serialize(packets).Contains("INVALID_PACKET"),
            "non-object JSON payload yields explicit error, not an exception");
    }

    using (var invalidPacketType = Json("""{"Type":42}"""))
    {
        var packets = router.Handle(invalidPacketType.RootElement, session);
        Check(JsonSerializer.Serialize(packets).Contains("INVALID_PACKET"),
            "non-string Type is rejected before packet routing");
    }

    var pongBefore = session.LastPongAt;
    using (var pongRequest = Json(
        """{"Type":"Pong"}"""))
    {
        var responses = router.Handle(
            pongRequest.RootElement,
            session);
        Check(responses.Count == 0 &&
              session.LastPongAt >= pongBefore,
            "Pong is accepted before authentication for login-screen heartbeat");
    }

    using (var announcementRequest = Json(
        """{"Type":"AnnouncementListRequest"}"""))
    {
        var responses = router.Handle(
            announcementRequest.RootElement,
            session);

        var json = JsonSerializer.Serialize(responses);
        Check(json.Contains(
                  "\"Type\":\"AnnouncementListResponse\"") &&
              json.Contains("\"Success\":true") &&
              json.Contains("\"Announcements\":[]"),
            "announcement list returns a successful empty response when no announcements are configured");
    }

    var (created, _) = store.Register("offline", "test-password", "");
    Check(created, "create account");
    Check(store.Login("offline", "bad") == null,
        "reject incorrect password");

    var account = store.Login("offline", "test-password");
    Check(account.HasValue, "login with correct password");

    var slots = store.Characters(account!.Value);
    Check(slots.Count == 3 &&
          slots.All(x => Convert.ToInt64(x["DBID"]) == 0),
        "stable three empty character slots");

    using var create = Json("""
        {"Name":"TestCharacter","Password":"1234","Race":1,"Hair":2,"HairColor":3,"Str":4,"Dex":5,"Con":6,"Wis":7}
        """);
    Check(store.Create(account.Value, create.RootElement).Success,
        "create first character");

    using var create2 = Json("""
        {"Name":"SecondCharacter","Password":"5678","Race":0,"Hair":1,"HairColor":2,"Str":7,"Dex":6,"Con":5,"Wis":4}
        """);
    Check(store.Create(account.Value, create2.RootElement).Success,
        "create second character");

    slots = store.Characters(account.Value);
    Check(slots.Count == 3 &&
          slots.Count(x => Convert.ToInt64(x["DBID"]) > 0) == 2,
        "two characters remain in stable three-slot response");
    Check(
        slots.Any(x => x.TryGetValue("NAME", out var n) &&
                       n?.ToString() == "TestCharacter") &&
        slots.Any(x => x.TryGetValue("NAME", out var n) &&
                       n?.ToString() == "SecondCharacter"),
        "creating second character does not replace first");

    var id = Convert.ToInt64(
        slots.First(x =>
            x.TryGetValue("NAME", out var n) &&
            n?.ToString() == "TestCharacter")["DBID"]);

    var secondCharacterId = Convert.ToInt64(
        slots.First(x =>
            x.TryGetValue("NAME", out var n) &&
            n?.ToString() == "SecondCharacter")["DBID"]);

    var accelerationBook =
        realCurrentCatalog.ResolveSkillBook(3701)!;
    var accelerationBookUid =
        store.AddInventoryItem(
            account.Value,
            secondCharacterId,
            3701,
            2);

    Check(accelerationBookUid > 0,
        "prepare two 加速術一級 skill books");

    var learnedAcceleration =
        store.LearnSkillFromBook(
            account.Value,
            secondCharacterId,
            accelerationBookUid,
            3701,
            accelerationBook.SkillId,
            accelerationBook.SkillLevel);

    var accelerationAfterLearn =
        store.GetCharacterAssets(
            account.Value,
            secondCharacterId)
        .Inventory
        .First(x =>
            Convert.ToInt64(x["ItemUID"]) ==
            accelerationBookUid);

    Check(learnedAcceleration.Success &&
          Convert.ToInt32(
              accelerationAfterLearn["Count"]) == 1 &&
          store.GetSkills(
              account.Value,
              secondCharacterId)
            .Any(x =>
                Convert.ToInt32(x["SkillID"]) == 27103 &&
                Convert.ToInt32(x["SkillLevel"]) == 1),
        "skill book atomically learns skill and consumes exactly one book");

    var duplicateAcceleration =
        store.LearnSkillFromBook(
            account.Value,
            secondCharacterId,
            accelerationBookUid,
            3701,
            accelerationBook.SkillId,
            accelerationBook.SkillLevel);

    var accelerationAfterDuplicate =
        store.GetCharacterAssets(
            account.Value,
            secondCharacterId)
        .Inventory
        .First(x =>
            Convert.ToInt64(x["ItemUID"]) ==
            accelerationBookUid);

    Check(!duplicateAcceleration.Success &&
          Convert.ToInt32(
              accelerationAfterDuplicate["Count"]) == 1,
        "reusing same-level skill book is rejected without consuming another book");

    Check(!store.SetCurrentTitle(
              account.Value,
              id,
              21),
        "locked title cannot be equipped");

    Check(store.UnlockTitle(
              account.Value,
              id,
              21) &&
          store.HasTitle(
              account.Value,
              id,
              21) &&
          store.SetCurrentTitle(
              account.Value,
              id,
              21),
        "unlocked title can be equipped persistently");

    var titledCharacter = store.GetCharacter(
        account.Value,
        id)!;

    Check(Convert.ToInt32(titledCharacter["Title"]) == 21 &&
          ((IEnumerable<int>)titledCharacter["UnlockedTitles"])
              .Contains(21),
        "character bootstrap reloads current and unlocked titles");

    Check(store.SetLevel(
              account.Value,
              secondCharacterId,
              9) is not null,
        "prepare ranked character level");

    var rankLevelPackets = rank.GetRankData(
        account.Value,
        secondCharacterId,
        1);
    var rankLevelJson = JsonSerializer.Serialize(
        rankLevelPackets);

    Check(rankLevelJson.Contains(
              $"\"DBID\":{secondCharacterId}") &&
          rankLevelJson.Contains("\"Value\":9"),
        "level ranking returns persisted character level");

    var rankMessage = rank.UpdateMessage(
        account.Value,
        secondCharacterId,
        1,
        "我是第一名");

    Check(JsonSerializer.Serialize(rankMessage)
              .Contains("\"Success\":true") &&
          JsonSerializer.Serialize(
              rank.GetRankData(
                  account.Value,
                  secondCharacterId,
                  1))
              .Contains("我是第一名"),
        "rank #1 can persist and reload top message");

    Check(JsonSerializer.Serialize(
              rank.UpdateMessage(
                  account.Value,
                  id,
                  1,
                  "不應成功"))
              .Contains("\"Success\":false"),
        "non-top character cannot update rank message");

    var clubLeaderRegistration = broadcastHub.Register();
    var clubMemberRegistration = broadcastHub.Register();

    broadcastHub.UpdatePresence(
        clubLeaderRegistration.ConnectionId,
        15,
        secondCharacterId,
        account.Value);
    broadcastHub.UpdatePresence(
        clubMemberRegistration.ConnectionId,
        15,
        id,
        account.Value);

    var openCreateClubPackets = club.Handle(
        account.Value,
        secondCharacterId,
        "OpenCreate",
        0,
        "",
        "",
        0,
        0);

    Check(JsonSerializer.Serialize(
              openCreateClubPackets)
              .Contains("\"Type\":\"OpenCreateClubWindow\""),
        "player without a club can request the create-club UI");

    var rejectedClubCreate = club.Handle(
        account.Value,
        secondCharacterId,
        "Create",
        0,
        "測試團",
        "",
        0,
        0);

    Check(
        store.GetClubIdForCharacter(
            secondCharacterId) == 0 &&
        JsonSerializer.Serialize(rejectedClubCreate)
            .Contains("30 級"),
        "club creation rejects characters below the original level requirement");

    Check(store.SetLevel(
              account.Value,
              secondCharacterId,
              30) is not null &&
          store.AddRewards(
              account.Value,
              secondCharacterId,
              0,
              500_000) is not null &&
          store.AddInventoryItem(
              account.Value,
              secondCharacterId,
              2560,
              1) > 0 &&
          store.AddInventoryItem(
              account.Value,
              secondCharacterId,
              2561,
              1) > 0 &&
          store.AddInventoryItem(
              account.Value,
              secondCharacterId,
              2562,
              1) > 0,
        "prepare original club creation level, fee, and signed documents");

    var beforeClubCreateCharacter = store.GetCharacter(
        account.Value,
        secondCharacterId)!;
    var beforeClubCreateAssets = store.GetCharacterAssets(
        account.Value,
        secondCharacterId);

    int ClubDocumentCount(
        SGO.Server.Domain.Assets.CharacterAssets assets,
        int itemId)
        => assets.Inventory
            .Where(x =>
                Convert.ToInt32(x["ItemIndex"]) == itemId &&
                Convert.ToInt32(x["InBank"]) == 0 &&
                Convert.ToInt32(x["InShop"]) == 0)
            .Sum(x => Convert.ToInt32(x["Count"]));

    var createClubPackets = club.Handle(
        account.Value,
        secondCharacterId,
        "Create",
        0,
        "測試團",
        "",
        0,
        0);

    var afterClubCreateCharacter = store.GetCharacter(
        account.Value,
        secondCharacterId)!;
    var afterClubCreateAssets = store.GetCharacterAssets(
        account.Value,
        secondCharacterId);

    Check(store.GetClubIdForCharacter(
              secondCharacterId) > 0 &&
          JsonSerializer.Serialize(createClubPackets)
              .Contains("\"ClubID\":") &&
          Convert.ToInt64(afterClubCreateCharacter["Money"]) ==
              Convert.ToInt64(beforeClubCreateCharacter["Money"]) -
              500_000 &&
          ClubDocumentCount(
              beforeClubCreateAssets,
              2560) ==
              ClubDocumentCount(
                  afterClubCreateAssets,
                  2560) + 1 &&
          ClubDocumentCount(
              beforeClubCreateAssets,
              2561) ==
              ClubDocumentCount(
                  afterClubCreateAssets,
                  2561) + 1 &&
          ClubDocumentCount(
              beforeClubCreateAssets,
              2562) ==
              ClubDocumentCount(
                  afterClubCreateAssets,
                  2562) + 1,
        "club creation atomically consumes 500k and all three signed documents");

    Check(JsonSerializer.Serialize(
              club.Handle(
                  account.Value,
                  secondCharacterId,
                  "OpenCreate",
                  0,
                  "",
                  "",
                  0,
                  0))
              .Contains("你已經加入戰團"),
        "existing club member cannot reopen create-club flow");

    var invitePackets = club.Handle(
        account.Value,
        secondCharacterId,
        "Invite",
        id,
        "",
        "",
        0,
        0);

    Check(JsonSerializer.Serialize(invitePackets)
              .Contains("戰團邀請已送出") &&
          clubMemberRegistration.Queue.ToArray().Any(x =>
              JsonSerializer.Serialize(x)
                  .Contains("\"Type\":\"ClubInvite\"")),
        "club leader can invite an online character");

    var acceptPackets = club.Handle(
        account.Value,
        id,
        "Accept",
        secondCharacterId,
        "",
        "",
        0,
        0);

    var clubId = store.GetClubIdForCharacter(
        secondCharacterId);

    Check(clubId > 0 &&
          store.GetClubIdForCharacter(id) == clubId &&
          JsonSerializer.Serialize(acceptPackets)
              .Contains("\"ClubID\":"),
        "club invitation acceptance persists shared membership");

    Check(!store.UpdateClubMessage(
              account.Value,
              id,
              "非法公告").Success &&
          store.UpdateClubMessage(
              account.Value,
              secondCharacterId,
              "團長公告").Success,
        "only club leader can update announcement");

    Check(store.AddRewards(
              account.Value,
              secondCharacterId,
              1_100_000,
              600_000) is not null,
        "fund club leader for donation tests");

    var beforeClubDonation = store.GetCharacter(
        account.Value,
        secondCharacterId)!;

    Check(store.DonateClubMoney(
              account.Value,
              secondCharacterId,
              500_000).Success &&
          store.DonateClubExp(
              account.Value,
              secondCharacterId,
              1_000_000).Success,
        "club money and EXP donations commit successfully");

    var afterClubDonation = store.GetCharacter(
        account.Value,
        secondCharacterId)!;
    var clubSnapshot = store.GetClubSnapshot(
        account.Value,
        secondCharacterId,
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())!;

    Check(
        Convert.ToInt64(afterClubDonation["Money"]) ==
            Convert.ToInt64(beforeClubDonation["Money"]) - 500_000 &&
        Convert.ToInt32(afterClubDonation["ExpAcc"]) ==
            Convert.ToInt32(beforeClubDonation["ExpAcc"]) - 1_000_000 &&
        clubSnapshot.ClubSaving >= 500_000 &&
        clubSnapshot.ClubExp >= 1_000_000,
        "club donations atomically deduct character resources and fund club");

    var researchStart = store.StartClubResearch(
        account.Value,
        secondCharacterId,
        2,
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    Check(researchStart.Success,
        "club leader can start affordable technology research");

    var completedResearch = store.GetClubSnapshot(
        account.Value,
        secondCharacterId,
        DateTimeOffset.UtcNow
            .AddHours(7)
            .ToUnixTimeMilliseconds())!;

    Check(completedResearch.ActiveResearchId == 0 &&
          completedResearch.Researches.Any(x =>
              x.ResearchId == 2 &&
              x.Level == 1),
        "completed club research is finalized persistently on snapshot");

    var researchedLeader = store.GetCharacter(
        account.Value,
        secondCharacterId)!;
    var researchedStats = CharacterStatCalculator.Calculate(
        researchedLeader,
        store.GetCharacterAssets(
            account.Value,
            secondCharacterId),
        currentCatalog);

    Check(Convert.ToInt32(
              researchedLeader.GetValueOrDefault(
                  "ClubTechAttack",
                  0)) == 10 &&
          researchedStats.Attack >=
              Convert.ToInt32(researchedLeader["Attack"]) + 10,
        "completed club attack research contributes to effective combat stats");

    var clubRankJson = JsonSerializer.Serialize(
        rank.GetRankData(
            account.Value,
            secondCharacterId,
            3));

    Check(clubRankJson.Contains(
              $"\"DBID\":{clubId}") &&
          clubRankJson.Contains(
              "\"Name\":\"測試團\""),
        "club ranking exposes ClubID while using leader ownership internally");

    var clubLeaderSession = new ClientSession
    {
        ConnectionId =
            clubLeaderRegistration.ConnectionId,
        AccountId = account.Value,
        CharacterId = secondCharacterId,
        CurrentMapId = 15
    };
    var clubMemberSession = new ClientSession
    {
        ConnectionId =
            clubMemberRegistration.ConnectionId,
        AccountId = account.Value,
        CharacterId = id,
        CurrentMapId = 15
    };

    using (var leaderQuit = Json(
        """{"Type":"ChatMessage","Message":"/quit","Channel":0,"BubbleType":0}"""))
    {
        var responses = router.Handle(
            leaderQuit.RootElement,
            clubLeaderSession);

        Check(
            JsonSerializer.Serialize(responses)
                .Contains("團長無法直接退出戰團") &&
            store.GetClubIdForCharacter(
                secondCharacterId) == clubId,
            "club leader cannot use /quit");
    }

    using (var promote = Json(
        """{"Type":"ChatMessage","Message":"/poweron 2 TestCharacter","Channel":0,"BubbleType":0}"""))
    {
        var responses = router.Handle(
            promote.RootElement,
            clubLeaderSession);
        var snapshot = store.GetClubSnapshot(
            account.Value,
            secondCharacterId,
            DateTimeOffset.UtcNow
                .ToUnixTimeMilliseconds())!;

        Check(
            snapshot.Members.Any(x =>
                x.DbId == id &&
                x.Position == 2) &&
            !responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"ChatBroadcast\"")),
            "/poweron changes a lower member position and is intercepted from normal chat");
    }

    using (var memberQuit = Json(
        """{"Type":"ChatMessage","Message":"/quit","Channel":0,"BubbleType":0}"""))
    {
        var responses = router.Handle(
            memberQuit.RootElement,
            clubMemberSession);

        Check(
            JsonSerializer.Serialize(responses)
                .Contains("已退出戰團") &&
            store.GetClubIdForCharacter(id) == 0,
            "non-leader /quit removes membership persistently");
    }

    club.Handle(
        account.Value,
        secondCharacterId,
        "Invite",
        id,
        "",
        "",
        0,
        0);
    club.Handle(
        account.Value,
        id,
        "Accept",
        secondCharacterId,
        "",
        "",
        0,
        0);

    Check(store.GetClubIdForCharacter(id) == clubId,
        "club member can rejoin after /quit for kick-command coverage");

    using (var fire = Json(
        """{"Type":"ChatMessage","Message":"/fire TestCharacter","Channel":0,"BubbleType":0}"""))
    {
        var responses = router.Handle(
            fire.RootElement,
            clubLeaderSession);

        Check(
            JsonSerializer.Serialize(responses)
                .Contains("移出戰團") &&
            store.GetClubIdForCharacter(id) == 0,
            "/fire removes only a lower-position club member");
    }

    using (var dismiss = Json(
        """{"Type":"ChatMessage","Message":"/dismiss","Channel":0,"BubbleType":0}"""))
    {
        var responses = router.Handle(
            dismiss.RootElement,
            clubLeaderSession);

        Check(
            JsonSerializer.Serialize(responses)
                .Contains("戰團已解散") &&
            store.GetClubIdForCharacter(
                secondCharacterId) == 0 &&
            store.GetClubIdForCharacter(id) == 0,
            "/dismiss removes the leader club and all remaining memberships");
    }

    broadcastHub.Unregister(
        clubLeaderRegistration.ConnectionId);
    broadcastHub.Unregister(
        clubMemberRegistration.ConnectionId);

    Check(store.GetQuestFlag(
              account.Value,
              id,
              "SMOKE_FLAG") == 0,
        "missing quest flags default to zero");

    Check(store.SetQuestFlag(
              account.Value,
              id,
              "SMOKE_FLAG",
              7) &&
          store.GetQuestFlag(
              account.Value,
              id,
              "SMOKE_FLAG") == 7 &&
          store.GetQuestFlag(
              account.Value,
              secondCharacterId,
              "SMOKE_FLAG") == 0,
        "quest flags persist per character and do not leak across characters");

    Check(store.UpsertQuest(
              account.Value,
              id,
              1002,
              1,
              1,
              12) &&
          store.GetQuests(
              account.Value,
              id).Any(x =>
              Convert.ToInt32(x["QuestID"]) == 1002 &&
              Convert.ToInt32(x["CurrentStep"]) == 1 &&
              Convert.ToInt32(x["StepProgress"]) == 12),
        "tracked quest state persists current step and progress");

    Check(store.UpsertQuest(
              account.Value,
              id,
              1004,
              1,
              3,
              0),
        "prepare active kill quest tracker step");

    var killQuestPackets = questProgress.OnMonsterKilled(
        account.Value,
        id,
        "噬魂之影");
    var killQuest = store.GetQuests(
            account.Value,
            id)
        .First(x =>
            Convert.ToInt32(x["QuestID"]) == 1004);

    Check(
        Convert.ToInt32(killQuest["Status"]) == 2 &&
        Convert.ToInt32(killQuest["CurrentStep"]) == 4 &&
        Convert.ToInt32(killQuest["StepProgress"]) == 0 &&
        killQuestPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"QuestUpdate\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"CurrentStep\":4") &&
            JsonSerializer.Serialize(x).Contains(
                "\"Status\":2")),
        "monster kill advances to a final NPC step as ready-to-report");

    Check(store.UpsertQuest(
              account.Value,
              id,
              1003,
              1,
              4,
              0),
        "prepare final collection quest tracker step");

    var questItemUid = store.AddInventoryItem(
        account.Value,
        id,
        2614,
        1);
    var collectionPackets = questProgress.RefreshCollectedItems(
        account.Value,
        id,
        2614);
    var collectionQuest = store.GetQuests(
            account.Value,
            id)
        .First(x =>
            Convert.ToInt32(x["QuestID"]) == 1003);

    Check(
        questItemUid > 0 &&
        Convert.ToInt32(collectionQuest["Status"]) == 2 &&
        Convert.ToInt32(collectionQuest["CurrentStep"]) == 4 &&
        Convert.ToInt32(collectionQuest["StepProgress"]) == 1 &&
        collectionPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"QuestUpdate\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"Status\":2")),
        "inventory collection marks a final active tracker step ready to report");

    Check(store.AbandonQuest(
              account.Value,
              id,
              1004) &&
          store.AbandonQuest(
              account.Value,
              id,
              1003),
        "quest progress test state can be cleaned through normal abandon flow");

    var legacyQuestStartPackets =
        questProgress.OnLegacyFlagChanged(
            account.Value,
            id,
            "K27001_Task",
            1);
    var legacyQuest1001 = store.GetQuests(
            account.Value,
            id)
        .First(x =>
            Convert.ToInt32(x["QuestID"]) == 1001);

    Check(
        Convert.ToInt32(legacyQuest1001["Status"]) == 1 &&
        Convert.ToInt32(legacyQuest1001["CurrentStep"]) == 1 &&
        legacyQuestStartPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"QuestID\":1001")),
        "legacy K27001_Task=1 starts quest 1001 tracker at the doctor-search step");

    questProgress.OnLegacyFlagChanged(
        account.Value,
        id,
        "K27001_Task",
        2);
    legacyQuest1001 = store.GetQuests(
            account.Value,
            id)
        .First(x =>
            Convert.ToInt32(x["QuestID"]) == 1001);

    Check(
        Convert.ToInt32(legacyQuest1001["Status"]) == 1 &&
        Convert.ToInt32(legacyQuest1001["CurrentStep"]) == 2,
        "legacy K27001_Task=2 advances quest 1001 tracker to feather collection");

    questProgress.OnLegacyFlagChanged(
        account.Value,
        id,
        "K27001_Task",
        3);
    legacyQuest1001 = store.GetQuests(
            account.Value,
            id)
        .First(x =>
            Convert.ToInt32(x["QuestID"]) == 1001);

    Check(
        Convert.ToInt32(legacyQuest1001["Status"]) == 2 &&
        Convert.ToInt32(legacyQuest1001["CurrentStep"]) == 3,
        "legacy K27001_Task=3 marks quest 1001 ready to report to the Royal Guard");

    questProgress.OnLegacyFlagChanged(
        account.Value,
        id,
        "K27001_END",
        1);
    legacyQuest1001 = store.GetQuests(
            account.Value,
            id)
        .First(x =>
            Convert.ToInt32(x["QuestID"]) == 1001);

    Check(
        Convert.ToInt32(legacyQuest1001["Status"]) == 3,
        "legacy K27001_END completes and hides quest 1001 tracker");

    if (store.GetQuests(
            account.Value,
            id).Any(x =>
            Convert.ToInt32(x["QuestID"]) == 1002))
    {
        store.AbandonQuest(
            account.Value,
            id,
            1002);
    }

    var mimiStartPackets =
        questProgress.OnLegacyFlagChanged(
            account.Value,
            id,
            "K27002A_Task",
            1);
    var mimiQuest = store.GetQuests(
            account.Value,
            id)
        .First(x =>
            Convert.ToInt32(x["QuestID"]) == 1002);

    Check(
        Convert.ToInt32(mimiQuest["Status"]) == 1 &&
        Convert.ToInt32(mimiQuest["CurrentStep"]) == 1 &&
        mimiStartPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"QuestID\":1002")),
        "legacy K27002A_Task=1 starts Mimi collection tracker");

    var mimiShellUid = store.AddInventoryItem(
        account.Value,
        id,
        4075,
        300);

    var mimiCollectionPackets =
        questProgress.RefreshCollectedItems(
            account.Value,
            id,
            4075);
    mimiQuest = store.GetQuests(
            account.Value,
            id)
        .First(x =>
            Convert.ToInt32(x["QuestID"]) == 1002);

    Check(
        mimiShellUid > 0 &&
        Convert.ToInt32(mimiQuest["Status"]) == 2 &&
        Convert.ToInt32(mimiQuest["CurrentStep"]) == 2 &&
        mimiCollectionPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Status\":2")),
        "collecting 300 shells marks Mimi quest ready to report");

    var removeMimiShells = store.DiscardAsset(
        account.Value,
        id,
        mimiShellUid,
        0,
        300);
    Check(removeMimiShells.Success,
        "prepare successful Mimi turn-in by consuming the 300 shells");

    var mimiRepeatPackets =
        questProgress.OnLegacyInteractionCompleted(
            account.Value,
            id,
            "K27002_A.txt",
            "response1",
            0,
            true);

    mimiQuest = store.GetQuests(
            account.Value,
            id)
        .First(x =>
            Convert.ToInt32(x["QuestID"]) == 1002);

    Check(
        Convert.ToInt32(mimiQuest["Status"]) == 1 &&
        Convert.ToInt32(mimiQuest["CurrentStep"]) == 1 &&
        Convert.ToInt32(mimiQuest["StepProgress"]) == 0 &&
        mimiRepeatPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"QuestID\":1002")),
        "successful Mimi turn-in resets repeatable quest tracker to shell collection");

    Check(store.AbandonQuest(
              account.Value,
              id,
              1002) &&
          !store.GetQuests(
              account.Value,
              id).Any(x =>
              Convert.ToInt32(x["QuestID"]) == 1002),
        "active quest can be abandoned persistently");

    Check(store.GetMallPoints(account.Value) == 0 &&
          store.AdjustMallPoints(
              account.Value,
              250,
              out var mallPoints) &&
          mallPoints == 250 &&
          JsonSerializer.Serialize(
              mall.Handle(
                  account.Value,
                  id,
                  "OpenMall",
                  0,
                  1,
                  0,
                  0))
              .Contains("\"Points\":250"),
        "mall point balance persists and OpenMall returns safe current balance");

    Check(JsonSerializer.Serialize(
              mall.Handle(
                  account.Value,
                  id,
                  "CreatePayPalDonation",
                  0,
                  1,
                  500,
                  0))
              .Contains("\"Success\":false"),
        "unconfigured PayPal flow fails safely without creating an order");

    var protectedGround = store.CreateGroundItem(
        id,
        0,
        1001,
        1,
        15,
        10000,
        10000,
        protectSeconds: 15,
        lifetimeSeconds: 60)!;

    var protectedAttempt = store.PickUpGroundItem(
        account.Value,
        secondCharacterId,
        protectedGround.GroundItemId);

    Check(!protectedAttempt.Success &&
          protectedAttempt.Message.Contains("保護"),
        "ground-item owner protection is enforced server-side");

    var ownerPickup = store.PickUpGroundItem(
        account.Value,
        id,
        protectedGround.GroundItemId);
    Check(ownerPickup.Success,
        "ground-item owner can pick up during protection");

    var expiringGround = store.CreateGroundItem(
        id,
        0,
        1001,
        1,
        15,
        10000,
        10000,
        lifetimeSeconds: 1)!;

    var expiredEntities = store.RemoveExpiredGroundItems(
        15,
        DateTimeOffset.UtcNow.AddSeconds(2));

    Check(expiredEntities.Contains(expiringGround.EntityId) &&
          !store.GetGroundItems(15).Any(x =>
              x.EntityId == expiringGround.EntityId),
        "expired ground items are deleted by the server lifecycle");

    var moneyBeforeFunding = Convert.ToInt64(
        store.GetCharacter(account.Value, id)!["Money"]);
    var funded = store.AddRewards(account.Value, id, 0, 10_000);
    Check(funded is not null &&
          Convert.ToInt64(funded["Money"]) == moneyBeforeFunding + 10_000,
        "fund character for NPC shop transaction tests");

    var generalShopItem = shopCatalog.GetShopItems(7)
        .First(x => x.ItemIndex == 4103);
    var weaponShopItem = shopCatalog.GetShopItems(10)
        .First(x => x.ItemIndex == 31548);

    var itemUid = store.AddInventoryItem(
        account.Value, id, generalShopItem.ItemIndex, 5);
    var equipUid = store.AddEquipment(
        account.Value, id, weaponShopItem.ItemIndex, 255, 0, 2);
    var petUid = store.AddPet(
        account.Value, id, 45001, "TestPet");
    var horseUid = store.AddHorse(
        account.Value, id, 50001, "TestMecha", "MS", 3600);
    var horseEquipUid = store.AddHorseEquipment(
        account.Value, id, 50293, -1, 1, 0, 1);
    var beastCardUid1 = store.AddHorseEquipment(
        account.Value, id, 51304, -1, 3);
    var beastCardUid2 = store.AddHorseEquipment(
        account.Value, id, 51304, -1, 3);
    var beastCardUid3 = store.AddHorseEquipment(
        account.Value, id, 51304, -1, 3);
    var batteryUid = store.AddInventoryItem(
        account.Value, id, 50102, 1);
    var recoveryUid = store.AddInventoryItem(
        account.Value, id, 1001, 1);
    var petFeedUid = store.AddInventoryItem(
        account.Value, id, 1001, 1);
    var petLifeUid = store.AddInventoryItem(
        account.Value, id, 2615, 1);
    var petResetUid = store.AddInventoryItem(
        account.Value, id, 2617, 1);
    var horseCardUid = store.AddInventoryItem(
        account.Value, id, 50001, 1);
    var happinessDollUid = store.AddInventoryItem(
        account.Value, id, 2126, 3);

    Check(itemUid > 0 &&
          equipUid > 0 &&
          petUid > 0 &&
          horseUid > 0 &&
          horseEquipUid > 0 &&
          beastCardUid1 > 0 &&
          beastCardUid2 > 0 &&
          beastCardUid3 > 0 &&
          batteryUid > 0 &&
          recoveryUid > 0 &&
          petFeedUid > 0 &&
          petLifeUid > 0 &&
          petResetUid > 0 &&
          horseCardUid > 0 &&
          happinessDollUid > 0,
        "seed persistent inventory equipment pet mecha and doll assets");

    var seededAssets = store.GetCharacterAssets(account.Value, id);
    Check(seededAssets.Inventory.Count >= 4 &&
          seededAssets.Inventory.Any(x =>
              Convert.ToInt64(x["ItemUID"]) == itemUid) &&
          seededAssets.Inventory.Any(x =>
              Convert.ToInt64(x["ItemUID"]) == batteryUid) &&
          seededAssets.Inventory.Any(x =>
              Convert.ToInt64(x["ItemUID"]) == recoveryUid) &&
          seededAssets.Inventory.Any(x =>
              Convert.ToInt64(x["ItemUID"]) == horseCardUid) &&
          seededAssets.Equips.Count == 1 &&
          seededAssets.Pets.Count == 1 &&
          seededAssets.Horses.Count == 1 &&
          seededAssets.HorseEquips.Count == 4,
        "character assets round-trip from SQLite");

    var seededPet = seededAssets.Pets.Single();
    Check(Convert.ToInt32(seededPet["LifeCount"]) > 0 &&
          Convert.ToInt32(seededPet["FeedValue"]) > 0 &&
          Convert.ToInt32(seededPet["FeedExpLimit"]) > 0,
        "new pets start alive and fed so recovered client enables Summon");

    using (var login = Json(
        """{"Type":"LoginRequest","User":"offline","Pass":"test-password"}"""))
    {
        var responses = router.Handle(login.RootElement, session);
        Check(responses.Count == 1 && session.AccountId == account,
            "router login establishes authenticated session");
    }

    using (var select = Json(JsonSerializer.Serialize(new
    {
        Type = "SelectCharacterRequest",
        DBID = id
    })))
    {
        var responses = router.Handle(select.RootElement, session);
        Check(session.CharacterId == id,
            "router selects character");
        Check(session.CurrentMapId == 15 && session.PendingMapSpawn,
            "selected character waits for server world tick spawn");
        Check(responses.Count == 9,
            "enter world emits bootstrap sync packets only");

        Check(JsonSerializer.Serialize(responses[1]).Contains(
                $"\"ItemIndex\":{generalShopItem.ItemIndex}"),
            "InventorySync loads persisted item");
        Check(JsonSerializer.Serialize(responses[2]).Contains(
                $"\"ItemIdx\":{weaponShopItem.ItemIndex}"),
            "EquipSync loads persisted equipment");
        Check(JsonSerializer.Serialize(responses[3]).Contains("\"PETID\":45001"),
            "PetSync loads persisted current-client pet");
        Check(JsonSerializer.Serialize(responses[4]).Contains("\"HorseIdx\":50001") &&
              JsonSerializer.Serialize(responses[4]).Contains("\"EquipIdx\":50293"),
            "HorseSync loads persisted mecha card and mecha equipment");

        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"FestivalThemeSync\"") &&
                JsonSerializer.Serialize(x).Contains(
                    "\"Theme\":\"normal\"")),
            "enter world sends the default festival theme baseline");

        using var first = Json(JsonSerializer.Serialize(responses[0]));
        Check(first.RootElement.Str("Type") == "EnterWorldSuccess",
            "first select response enters world");
        Check(first.RootElement.Int("MapID") == 15,
            "enter world includes Map15");
    }

    var petBeforeLevel = store.GetCharacterAssets(
            account.Value,
            id)
        .Pets.Single(x =>
            Convert.ToInt64(x["UID"]) == petUid);

    var leveledPet = store.AddPetExperience(
        account.Value,
        id,
        petUid,
        100)!;

    Check(Convert.ToInt32(leveledPet["Level"]) == 2 &&
          Convert.ToInt32(leveledPet["UseExp"]) ==
              Convert.ToInt32(petBeforeLevel["UseExp"]) + 2,
        "pet level-up grants two legacy ExpUse ability points");

    var attackBeforePetStat =
        Convert.ToInt32(leveledPet["Attack"]);
    var useExpBeforePetStat =
        Convert.ToInt32(leveledPet["UseExp"]);

    using (var allocatePetStat = Json(
        JsonSerializer.Serialize(new
        {
            Type = "PetAction",
            Action = "AllocateStat",
            PetUID = petUid,
            StatType = "STR"
        })))
    {
        var responses = router.Handle(
            allocatePetStat.RootElement,
            session);
        var updatedPet = store.GetCharacterAssets(
                account.Value,
                id)
            .Pets.Single(x =>
                Convert.ToInt64(x["UID"]) == petUid);

        Check(
            Convert.ToInt32(updatedPet["ExtraSTR"]) == 1 &&
            Convert.ToInt32(updatedPet["UseExp"]) ==
                useExpBeforePetStat - 1 &&
            Convert.ToInt32(updatedPet["Attack"]) ==
                attackBeforePetStat + 2 &&
            Convert.ToInt32(updatedPet["EvolID"]) == 1 &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"PetSync\"")) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "寵物進化")),
            "PetAction AllocateStat consumes UseExp, applies STR growth, and evolves when requirements are met");
    }

    using (var db = new SqliteConnection(
        $"Data Source={dbPath}"))
    {
        db.Open();
        using var setupPet = db.CreateCommand();
        setupPet.CommandText = """
            UPDATE pets
               SET feed_value=100,
                   life_count=9,
                   evol_id=2
             WHERE uid=$pet
            """;
        setupPet.Parameters.AddWithValue(
            "$pet",
            petUid);
        setupPet.ExecuteNonQuery();
    }

    using (var setMainSkill = Json(
        JsonSerializer.Serialize(new
        {
            Type = "PetAction",
            Action = "SetMainSkill",
            PetUID = petUid,
            SkillID = 20102
        })))
    {
        router.Handle(
            setMainSkill.RootElement,
            session);
        var pet = store.GetCharacterAssets(
                account.Value,
                id)
            .Pets.Single(x =>
                Convert.ToInt64(x["UID"]) == petUid);

        Check(Convert.ToInt32(
                  pet["MainSkillID"]) == 20102,
            "PetAction SetMainSkill accepts a skill from the current evolution config");
    }

    using (var db = new SqliteConnection(
        $"Data Source={dbPath}"))
    {
        db.Open();
        using var restoreEvolution = db.CreateCommand();
        restoreEvolution.CommandText = """
            UPDATE pets
               SET evol_id=0
             WHERE uid=$pet
            """;
        restoreEvolution.Parameters.AddWithValue(
            "$pet",
            petUid);
        restoreEvolution.ExecuteNonQuery();
    }

    using (var feedPet = Json(
        JsonSerializer.Serialize(new
        {
            Type = "PetAction",
            Action = "Feed",
            PetUID = petUid,
            ItemUID = petFeedUid
        })))
    {
        var responses = router.Handle(
            feedPet.RootElement,
            session);
        var pet = store.GetCharacterAssets(
                account.Value,
                id)
            .Pets.Single(x =>
                Convert.ToInt64(x["UID"]) == petUid);

        Check(
            Convert.ToInt32(pet["FeedValue"]) == 240 &&
            !store.GetCharacterAssets(
                account.Value,
                id).Inventory.Any(x =>
                Convert.ToInt64(x["ItemUID"]) ==
                    petFeedUid) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"InventorySync\"")),
            "PetAction Feed atomically consumes a recovery item and restores feed value");
    }

    using (var addLife = Json(
        JsonSerializer.Serialize(new
        {
            Type = "PetAction",
            Action = "AddLife",
            PetUID = petUid
        })))
    {
        router.Handle(addLife.RootElement, session);
        var pet = store.GetCharacterAssets(
                account.Value,
                id)
            .Pets.Single(x =>
                Convert.ToInt64(x["UID"]) == petUid);

        Check(
            Convert.ToInt32(pet["LifeCount"]) == 10 &&
            !store.GetCharacterAssets(
                account.Value,
                id).Inventory.Any(x =>
                Convert.ToInt64(x["ItemUID"]) ==
                    petLifeUid),
            "PetAction AddLife consumes 精靈之魂 and restores one life");
    }

    using (var resetPet = Json(
        JsonSerializer.Serialize(new
        {
            Type = "PetAction",
            Action = "ResetStat",
            PetUID = petUid
        })))
    {
        router.Handle(resetPet.RootElement, session);
        var pet = store.GetCharacterAssets(
                account.Value,
                id)
            .Pets.Single(x =>
                Convert.ToInt64(x["UID"]) == petUid);

        Check(
            Convert.ToInt32(pet["ExtraSTR"]) == 0 &&
            Convert.ToInt32(pet["UseExp"]) ==
                useExpBeforePetStat &&
            Convert.ToInt32(pet["Attack"]) ==
                attackBeforePetStat &&
            !store.GetCharacterAssets(
                account.Value,
                id).Inventory.Any(x =>
                Convert.ToInt64(x["ItemUID"]) ==
                    petResetUid),
            "PetAction ResetStat consumes 精靈再生水晶 and refunds allocated points");
    }

    var disposablePetUid = store.AddPet(
        account.Value,
        id,
        45001,
        "DiscardPet");
    var disposableHorseUid = store.AddHorse(
        account.Value,
        id,
        50001,
        "DiscardMecha",
        "MS",
        100);

    using (var dropPet = Json(
        JsonSerializer.Serialize(new
        {
            Type = "DropItemRequest",
            TabType = 2,
            ItemUID = disposablePetUid,
            Count = 1
        })))
    {
        var responses = router.Handle(
            dropPet.RootElement,
            session);

        Check(
            !store.GetCharacterAssets(
                account.Value,
                id).Pets.Any(x =>
                Convert.ToInt64(x["UID"]) ==
                    disposablePetUid) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"PetSync\"")),
            "DropItemRequest permanently removes an inactive unequipped pet");
    }

    using (var dropMecha = Json(
        JsonSerializer.Serialize(new
        {
            Type = "DropItemRequest",
            TabType = 4,
            ItemUID = disposableHorseUid,
            Count = 1
        })))
    {
        var responses = router.Handle(
            dropMecha.RootElement,
            session);

        Check(
            !store.GetCharacterAssets(
                account.Value,
                id).Horses.Any(x =>
                Convert.ToInt64(x["HorseUID"]) ==
                    disposableHorseUid) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"HorseSync\"")),
            "DropItemRequest permanently removes an unused unequipped mecha");
    }

    var megaphoneUid = store.AddInventoryItem(
        account.Value,
        id,
        2803,
        2);

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }

    using (var openMegaphone = Json(
        JsonSerializer.Serialize(new
        {
            Type = "UseItemRequest",
            ItemUID = megaphoneUid
        })))
    {
        var beforeCount = store.GetCharacterAssets(
                account.Value,
                id)
            .Inventory
            .Where(x =>
                Convert.ToInt64(x["ItemUID"]) == megaphoneUid)
            .Sum(x => Convert.ToInt32(x["Count"]));

        var responses = router.Handle(
            openMegaphone.RootElement,
            session);

        var afterCount = store.GetCharacterAssets(
                account.Value,
                id)
            .Inventory
            .Where(x =>
                Convert.ToInt64(x["ItemUID"]) == megaphoneUid)
            .Sum(x => Convert.ToInt32(x["Count"]));

        Check(
            beforeCount == 2 &&
            afterCount == 2 &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"OpenMegaphoneWindow\"")),
            "UseItemRequest opens megaphone input without consuming the item");
    }

    using (var megaphoneMessage = Json(
        JsonSerializer.Serialize(new
        {
            Type = "MegaphoneMessage",
            ItemUID = megaphoneUid,
            Message = "全服測試訊息"
        })))
    {
        var responses = router.Handle(
            megaphoneMessage.RootElement,
            session);

        var remaining = store.GetCharacterAssets(
                account.Value,
                id)
            .Inventory
            .Where(x =>
                Convert.ToInt64(x["ItemUID"]) == megaphoneUid)
            .Sum(x => Convert.ToInt32(x["Count"]));

        Check(
            remaining == 1 &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"InventorySync\"")) &&
            broadcastRegistration.Queue.ToArray().Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SystemMessageBroadcast\"") &&
                JsonSerializer.Serialize(x).Contains(
                    "TestCharacter") &&
                JsonSerializer.Serialize(x).Contains(
                    "全服測試訊息")),
            "MegaphoneMessage consumes one item and publishes global announcement");
    }

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }

    var skillBookUid = store.AddInventoryItem(
        account.Value,
        id,
        3701,
        1);

    using (var useSkillBook = Json(
        JsonSerializer.Serialize(new
        {
            Type = "UseItemRequest",
            ItemUID = skillBookUid
        })))
    {
        var responses = router.Handle(
            useSkillBook.RootElement,
            session);

        Check(
            !store.GetCharacterAssets(
                account.Value,
                id).Inventory.Any(x =>
                Convert.ToInt64(x["ItemUID"]) ==
                    skillBookUid) &&
            store.GetSkills(
                account.Value,
                id).Any(x =>
                Convert.ToInt32(x["SkillID"]) == 27103 &&
                Convert.ToInt32(x["SkillLevel"]) == 1) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"InventorySync\"")) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SkillSync\"")) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "已學會 加速術 Lv.1")),
            "UseItemRequest consumes a resolved skill book and syncs learned skill");
    }

    var autoSaintStartX = session.LastX;
    var autoSaintStartY = session.LastY;

    using (var autoSaintOn = Json(
        """
        {"Type":"ToggleAutoSaint","IsOn":true,"MpThreshold":25,"SpThreshold":35}
        """))
    {
        var responses = router.Handle(
            autoSaintOn.RootElement,
            session);

        Check(
            state: session.AutoSaintEnabled &&
            session.AutoSaintMpThreshold == 25 &&
            session.AutoSaintSpThreshold == 35 &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"ToggleAutoSaintResponse\"") &&
                JsonSerializer.Serialize(x).Contains(
                    "\"IsOn\":true")),
            msg: "ToggleAutoSaint persists server-authoritative state and thresholds");
    }

    using (var blockedMove = Json(
        JsonSerializer.Serialize(new
        {
            Type = "SyncPosition",
            X = (int)autoSaintStartX + 999,
            Y = (int)autoSaintStartY + 999
        })))
    {
        router.Handle(
            blockedMove.RootElement,
            session);

        Check(
            Math.Abs(session.LastX - autoSaintStartX) < 0.001 &&
            Math.Abs(session.LastY - autoSaintStartY) < 0.001,
            "AutoSaint blocks authoritative player movement while enabled");
    }

    using (var blockedAttack = Json(
        """
        {"Type":"AttackEntity","TargetEntityID":1,"TargetType":1}
        """))
    {
        var responses = router.Handle(
            blockedAttack.RootElement,
            session);

        Check(
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "自動聖靈啟動中")),
            "AutoSaint blocks manual combat on the server");
    }

    using (var autoSaintOff = Json(
        """
        {"Type":"ToggleAutoSaint","IsOn":false,"MpThreshold":30,"SpThreshold":30}
        """))
    {
        router.Handle(
            autoSaintOff.RootElement,
            session);

        Check(
            !session.AutoSaintEnabled,
            "AutoSaint can be disabled without reconnecting");
    }

    var moneyBeforeRepair = Convert.ToInt64(
        store.GetCharacter(account.Value, id)!["Money"]);

    using (var repair = Json(JsonSerializer.Serialize(new
    {
        Type = "RepairItemRequest",
        ItemType = "Equip",
        UID = equipUid
    })))
    {
        var responses = router.Handle(
            repair.RootElement,
            session);
        var repairedEquip = store.GetCharacterAssets(
                account.Value,
                id)
            .Equips
            .First(x =>
                Convert.ToInt64(x["ItemID"]) == equipUid);

        Check(
            Convert.ToInt32(repairedEquip["Damage"]) == 100 &&
            Convert.ToInt64(
                store.GetCharacter(
                    account.Value,
                    id)!["Money"]) < moneyBeforeRepair &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SyncMoney\"")) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"EquipSync\"")),
            "RepairItemRequest restores current durability and deducts money");
    }

    var beforeDollAssets = store.GetCharacterAssets(account.Value, id);
    var beforeDollRewardCount = beforeDollAssets.Inventory
        .Where(x =>
            Convert.ToInt32(x["ItemIndex"]) is 1001 or 50102)
        .Sum(x => Convert.ToInt32(x["Count"]));

    using (var openDoll = Json(JsonSerializer.Serialize(new
    {
        Type = "UseItemRequest",
        ItemUID = happinessDollUid
    })))
    {
        var responses = router.Handle(openDoll.RootElement, session);
        var after = store.GetCharacterAssets(account.Value, id);

        var doll = after.Inventory.FirstOrDefault(x =>
            Convert.ToInt64(x["ItemUID"]) == happinessDollUid);
        var afterRewardCount = after.Inventory
            .Where(x =>
                Convert.ToInt32(x["ItemIndex"]) is 1001 or 50102)
            .Sum(x => Convert.ToInt32(x["Count"]));

        Check(doll is not null &&
              Convert.ToInt32(doll["Count"]) == 2 &&
              afterRewardCount == beforeDollRewardCount + 1 &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"Type\":\"InventorySync\"")) &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "幸福娃娃送給你")),
            "UseItemRequest opens Happiness Doll, consumes one and grants one reward");

        Check(
            broadcastRegistration.Queue.TryDequeue(out var announcement) &&
            JsonSerializer.Serialize(announcement).Contains(
                "\"Type\":\"SystemMessageBroadcast\"") &&
            JsonSerializer.Serialize(announcement).Contains(
                "TestCharacter") &&
            JsonSerializer.Serialize(announcement).Contains(
                "魔靈電池"),
            "rare Happiness Doll reward publishes global announcement");
    }

    var equipmentDollUid = store.AddInventoryItem(
        account.Value, id, 2126, 1);
    var equipmentOnlyCatalog = new CurrentClientDataCatalog(
        new[]
        {
            CurrentItem(2126, "幸福娃娃", "general", price: 1),
            CurrentItem(
                19059,
                "黃金戰神頭盔",
                "equip",
                price: 100000,
                def: 177,
                equipSlot: 0)
        },
        Array.Empty<CurrentSkillDefinition>());
    var equipmentBroadcasts = new ServerBroadcastHub();
    var equipmentDolls = new DollService(
        store,
        store,
        equipmentOnlyCatalog,
        equipmentBroadcasts,
        config);

    var equipmentDollResponses = equipmentDolls.Open(
        account.Value,
        id,
        equipmentDollUid,
        2126);
    var equipmentReward = store.GetCharacterAssets(account.Value, id)
        .Equips
        .Last(x => Convert.ToInt32(x["ItemIdx"]) == 19059);

    Check(
        Convert.ToInt32(equipmentReward["Part"]) == 255 &&
        equipmentDollResponses.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"EquipSync\"")),
        "Happiness Doll equipment reward stays in equipment bag instead of auto-equipping");

    // RepairItemRequest: character equipment uses the same price formula
    // as SharedInventoryPanel and restores remaining durability to max.
    var repairEquipUid = store.AddEquipment(
        account.Value,
        id,
        18538,
        255,
        60);
    var moneyBeforeEquipRepair = Convert.ToInt64(
        store.GetCharacter(account.Value, id)!["Money"]);

    using (var repairEquip = Json(JsonSerializer.Serialize(new
    {
        Type = "RepairItemRequest",
        ItemType = "Equip",
        UID = repairEquipUid
    })))
    {
        var responses = router.Handle(
            repairEquip.RootElement,
            session);
        var repairedEquip = store.GetCharacterAssets(
                account.Value,
                id)
            .Equips
            .First(x =>
                Convert.ToInt64(x["ItemID"]) == repairEquipUid);

        Check(
            Convert.ToInt32(repairedEquip["Damage"]) == 120 &&
            Convert.ToInt64(
                store.GetCharacter(account.Value, id)!["Money"]) ==
                moneyBeforeEquipRepair - 1 &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"EquipSync\"")) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SyncMoney\"")),
            "RepairItemRequest repairs character equipment and syncs money/equipment");
    }

    var repairHorseEquipUid = store.AddHorseEquipment(
        account.Value,
        id,
        50293,
        -1,
        255,
        25);
    var moneyBeforeHorseRepair = Convert.ToInt64(
        store.GetCharacter(account.Value, id)!["Money"]);

    using (var repairHorseEquip = Json(JsonSerializer.Serialize(new
    {
        Type = "RepairItemRequest",
        ItemType = "HorseEquip",
        UID = repairHorseEquipUid
    })))
    {
        var responses = router.Handle(
            repairHorseEquip.RootElement,
            session);
        var repairedHorseEquip = store.GetCharacterAssets(
                account.Value,
                id)
            .HorseEquips
            .First(x =>
                Convert.ToInt64(x["EquipUID"]) == repairHorseEquipUid);

        Check(
            Convert.ToInt32(repairedHorseEquip["Damage"]) == 100 &&
            Convert.ToInt64(
                store.GetCharacter(account.Value, id)!["Money"]) ==
                moneyBeforeHorseRepair - 75 &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"HorseSync\"")) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SyncMoney\"")),
            "RepairItemRequest repairs mecha equipment and syncs money/mecha data");
    }

    // Verify malformed requests do not enter module handlers or mutate assets.
    using (var malformedBank = Json(
        """{"Type":"BankTransfer","UID":"1","TabType":0,"Amount":1,"ToBank":true}"""))
    {
        var response = router.Handle(malformedBank.RootElement, session);
        Check(response.Count == 1 &&
              JsonSerializer.Serialize(response[0]).Contains("INVALID_PACKET"),
            "malformed bank transfer returns typed INVALID_PACKET instead of modifying assets");
    }

    // BankTransfer: general items support partial stack deposit/withdraw.
    using (var bankDeposit = Json(JsonSerializer.Serialize(new
    {
        Type = "BankTransfer",
        UID = itemUid,
        ToBank = true,
        Amount = 1,
        TabType = 0
    })))
    {
        var responses = router.Handle(bankDeposit.RootElement, session);
        var after = store.GetCharacterAssets(account.Value, id);

        Check(after.Inventory.Any(x =>
                  Convert.ToInt64(x["ItemUID"]) == itemUid &&
                  Convert.ToInt32(x["InBank"]) == 0 &&
                  Convert.ToInt32(x["Count"]) == 4) &&
              after.Inventory.Any(x =>
                  Convert.ToInt32(x["ItemIndex"]) ==
                      generalShopItem.ItemIndex &&
                  Convert.ToInt32(x["InBank"]) == 1 &&
                  Convert.ToInt32(x["Count"]) == 1) &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"Type\":\"InventorySync\"")),
            "BankTransfer deposits one item from a general-item stack");
    }

    var bankGeneralUid = Convert.ToInt64(
        store.GetCharacterAssets(account.Value, id).Inventory
            .First(x =>
                Convert.ToInt32(x["ItemIndex"]) ==
                    generalShopItem.ItemIndex &&
                Convert.ToInt32(x["InBank"]) == 1)["ItemUID"]);

    using (var bankWithdraw = Json(JsonSerializer.Serialize(new
    {
        Type = "BankTransfer",
        UID = bankGeneralUid,
        ToBank = false,
        Amount = 1,
        TabType = 0
    })))
    {
        var responses = router.Handle(bankWithdraw.RootElement, session);
        var after = store.GetCharacterAssets(account.Value, id);

        Check(after.Inventory.Any(x =>
                  Convert.ToInt64(x["ItemUID"]) == itemUid &&
                  Convert.ToInt32(x["InBank"]) == 0 &&
                  Convert.ToInt32(x["Count"]) == 5) &&
              !after.Inventory.Any(x =>
                  Convert.ToInt32(x["ItemIndex"]) ==
                      generalShopItem.ItemIndex &&
                  Convert.ToInt32(x["InBank"]) == 1) &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"Type\":\"InventorySync\"")),
            "BankTransfer withdraws and merges a general-item stack");
    }

    using (var bankEquip = Json(JsonSerializer.Serialize(new
    {
        Type = "BankTransfer",
        UID = equipUid,
        ToBank = true,
        Amount = 1,
        TabType = 1
    })))
    {
        var responses = router.Handle(bankEquip.RootElement, session);
        Check(store.GetCharacterAssets(account.Value, id).Equips.Any(x =>
                  Convert.ToInt64(x["ItemID"]) == equipUid &&
                  Convert.ToInt32(x["InBank"]) == 1) &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"Type\":\"EquipSync\"")),
            "BankTransfer deposits unequipped character equipment");
    }

    using (var bankEquipBack = Json(JsonSerializer.Serialize(new
    {
        Type = "BankTransfer",
        UID = equipUid,
        ToBank = false,
        Amount = 1,
        TabType = 1
    })))
    {
        router.Handle(bankEquipBack.RootElement, session);
        Check(store.GetCharacterAssets(account.Value, id).Equips.Any(x =>
                Convert.ToInt64(x["ItemID"]) == equipUid &&
                Convert.ToInt32(x["InBank"]) == 0),
            "BankTransfer withdraws character equipment");
    }

    using (var bankMechaEquip = Json(JsonSerializer.Serialize(new
    {
        Type = "BankTransfer",
        UID = horseEquipUid,
        ToBank = true,
        Amount = 1,
        TabType = 3
    })))
    {
        var responses = router.Handle(bankMechaEquip.RootElement, session);
        Check(store.GetCharacterAssets(account.Value, id).HorseEquips.Any(x =>
                  Convert.ToInt64(x["EquipUID"]) == horseEquipUid &&
                  Convert.ToInt32(x["InBank"]) == 1) &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"Type\":\"HorseSync\"")),
            "BankTransfer deposits unbound mecha equipment");
    }

    using (var bankMechaEquipBack = Json(JsonSerializer.Serialize(new
    {
        Type = "BankTransfer",
        UID = horseEquipUid,
        ToBank = false,
        Amount = 1,
        TabType = 3
    })))
    {
        router.Handle(bankMechaEquipBack.RootElement, session);
        Check(store.GetCharacterAssets(account.Value, id).HorseEquips.Any(x =>
                Convert.ToInt64(x["EquipUID"]) == horseEquipUid &&
                Convert.ToInt32(x["InBank"]) == 0),
            "BankTransfer withdraws mecha equipment");
    }


    // Timed HP/MP recovery is server-driven and interval-gated.
    store.ApplyDamage(account.Value, id, 50);
    var beforeRecovery = store.GetCharacter(account.Value, id)!;
    var beforeRecoveryHp = Convert.ToInt32(beforeRecovery["HP"]);
    var beforeRecoveryMp = Convert.ToInt32(beforeRecovery["MP"]);

    // Spend some MP without requiring a learned skill for this persistence test.
    store.SpendSkillResources(account.Value, id, 0, Math.Min(10, beforeRecoveryMp), 0);
    beforeRecovery = store.GetCharacter(account.Value, id)!;
    beforeRecoveryMp = Convert.ToInt32(beforeRecovery["MP"]);

    session.LastResourceRecoveryAt = DateTimeOffset.UtcNow;
    var earlyRecoveryPackets = recovery.Tick(
        session,
        session.LastResourceRecoveryAt.AddSeconds(4));

    Check(earlyRecoveryPackets.Count == 0 &&
          Convert.ToInt32(store.GetCharacter(account.Value, id)!["HP"]) ==
              beforeRecoveryHp,
        "resource recovery waits for configured interval");

    var recoveryPackets = recovery.Tick(
        session,
        session.LastResourceRecoveryAt.AddSeconds(5));

    var afterRecovery = store.GetCharacter(account.Value, id)!;
    var effectiveRecoveryStats =
        SGO.Server.Application.Assets.CharacterStatCalculator.Calculate(
            afterRecovery,
            store.GetCharacterAssets(account.Value, id),
            currentCatalog);
    Check(Convert.ToInt32(afterRecovery["HP"]) > beforeRecoveryHp &&
          Convert.ToInt32(afterRecovery["MP"]) >= beforeRecoveryMp &&
          recoveryPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"PlayerStatusSync\"") &&
              JsonSerializer.Serialize(x).Contains(
                  $"\"MaxHP\":{effectiveRecoveryStats.MaxHP}")),
        "resource recovery preserves effective mecha MaxHP instead of reverting to DB base MaxHP");

    using (var restore = Json(
        """{"Type":"ChatMessage","Channel":0,"Message":"/gm restore","BubbleType":0}"""))
    {
        store.ApplyDamage(account.Value, id, 50);
        var beforeRestore = store.GetCharacter(account.Value, id)!;
        store.SpendSkillResources(
            account.Value,
            id,
            0,
            Math.Min(10, Convert.ToInt32(beforeRestore["MP"])),
            0);

        var responses = router.Handle(restore.RootElement, session);
        var restored = store.GetCharacter(account.Value, id)!;

        Check(Convert.ToInt32(restored["HP"]) ==
                  Convert.ToInt32(restored["MaxHP"]) &&
              Convert.ToInt32(restored["MP"]) ==
                  Convert.ToInt32(restored["MaxMP"]) &&
              Convert.ToInt32(restored["SP"]) ==
                  Convert.ToInt32(restored["MaxSP"]) &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"Type\":\"PlayerStatusSync\"")),
            "GM restore fully restores HP MP SP");
    }

    var damaged = store.ApplyDamage(account.Value, id, 50);
    Check(damaged is not null && Convert.ToInt32(damaged["HP"]) == 50,
        "prepare recovery item test with player damage");

    using (var useRecovery = Json(JsonSerializer.Serialize(new
    {
        Type = "UseItemRequest",
        ItemUID = recoveryUid
    })))
    {
        var responses = router.Handle(useRecovery.RootElement, session);
        var recovered = store.GetCharacter(account.Value, id)!;

        Check(Convert.ToInt32(recovered["HP"]) == 100,
            "UseItemRequest applies current recovery .tres and caps at MaxHP");
        Check(!store.GetCharacterAssets(account.Value, id).Inventory.Any(x =>
                Convert.ToInt64(x["ItemUID"]) == recoveryUid),
            "recovery item is consumed");
        var recoveryInventorySync = responses
            .FirstOrDefault(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"InventorySync\""));

        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"PlayerStatusSync\"")) &&
              recoveryInventorySync is not null &&
              !JsonSerializer.Serialize(recoveryInventorySync)
                  .Contains($"\"ItemUID\":{recoveryUid}") &&
              responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"PlayItemEffect\"")),
            "recovery item refreshes post-consumption inventory, status and item effect");
    }

    var horseCountBeforeCard =
        store.GetCharacterAssets(account.Value, id).Horses.Count;

    using (var useMechaCard = Json(JsonSerializer.Serialize(new
    {
        Type = "UseItemRequest",
        ItemUID = horseCardUid
    })))
    {
        var responses = router.Handle(useMechaCard.RootElement, session);
        var assetsAfterCard = store.GetCharacterAssets(account.Value, id);

        Check(assetsAfterCard.Horses.Count == horseCountBeforeCard + 1 &&
              assetsAfterCard.Horses.Any(x =>
                  Convert.ToInt32(x["HorseIdx"]) == 50001 &&
                  Convert.ToInt32(x["EnergyLeft"]) == 25200),
            "UseItemRequest converts source mecha card into rideable mecha");
        Check(!assetsAfterCard.Inventory.Any(x =>
                Convert.ToInt64(x["ItemUID"]) == horseCardUid),
            "mecha card is consumed after activation");
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains("\"Type\":\"HorseSync\"")),
            "mecha-card activation refreshes HorseSync");
    }

    var buyItem = shopCatalog.GetShopItems(7)
        .First(x => x.ItemIndex == 4103);
    var moneyBeforeShop = Convert.ToInt64(
        store.GetCharacter(account.Value, id)!["Money"]);

    using (var buy = Json(JsonSerializer.Serialize(new
    {
        Type = "ShopAction",
        Action = "BuyNpcItem",
        ShopID = 7,
        ItemIndex = buyItem.ItemIndex,
        Count = 2
    })))
    {
        var responses = router.Handle(buy.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains("\"Type\":\"SyncMoney\"")),
            "NPC shop buy emits money sync");

        var afterBuy = store.GetCharacterAssets(account.Value, id);
        Check(afterBuy.Inventory.Any(x =>
                Convert.ToInt32(x["ItemIndex"]) == buyItem.ItemIndex),
            "NPC shop buy persists source item");
        Check(Convert.ToInt64(
                  store.GetCharacter(account.Value, id)!["Money"]) < moneyBeforeShop,
            "NPC shop buy deducts money");
    }

    var mappedEquipShopItem = shopCatalog.GetShopItems(2)
        .First(x => x.ItemIndex == 18538);
    var equipCountBeforePurchase =
        store.GetCharacterAssets(account.Value, id).Equips.Count;

    using (var buyEquip = Json(JsonSerializer.Serialize(new
    {
        Type = "ShopAction",
        Action = "BuyNpcItem",
        ShopID = 2,
        ItemIndex = mappedEquipShopItem.ItemIndex,
        Count = 1
    })))
    {
        var responses = router.Handle(buyEquip.RootElement, session);
        var assetsAfterEquipBuy =
            store.GetCharacterAssets(account.Value, id);

        Check(assetsAfterEquipBuy.Equips.Count ==
                  equipCountBeforePurchase + 1 &&
              assetsAfterEquipBuy.Equips.Any(x =>
                  Convert.ToInt32(x["ItemIdx"]) == 18538 &&
                  Convert.ToInt32(x["Damage"]) == 120),
            "NPC shop buys mapped current equipment at full durability");
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"EquipSync\"")),
            "equipment purchase refreshes EquipSync");
    }

    var purchasedEquip = store.GetCharacterAssets(account.Value, id)
        .Equips.First(x =>
            Convert.ToInt32(x["ItemIdx"]) == 18538);
    var purchasedEquipUid =
        Convert.ToInt64(purchasedEquip["ItemID"]);
    var moneyBeforeSell = Convert.ToInt64(
        store.GetCharacter(account.Value, id)!["Money"]);

    using (var sell = Json(JsonSerializer.Serialize(new
    {
        Type = "ShopAction",
        Action = "SellNpcItem",
        UID = purchasedEquipUid,
        TabType = 1,
        Count = 1
    })))
    {
        var responses = router.Handle(sell.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains("\"Type\":\"SyncMoney\"")),
            "NPC shop sell emits money sync for sellable current equipment");
    }

    Check(!store.GetCharacterAssets(account.Value, id).Equips.Any(x =>
              Convert.ToInt64(x["ItemID"]) == purchasedEquipUid) &&
          Convert.ToInt64(
              store.GetCharacter(account.Value, id)!["Money"]) > moneyBeforeSell,
        "NPC shop sell removes equipment and adds money");

    var baseAttackBeforeEquip = Convert.ToInt32(
        store.GetCharacter(account.Value, id)!["Attack"]);

    using (var equip = Json(JsonSerializer.Serialize(new { Type = "EquipRequest", ItemUID = equipUid, TargetSlot = 5 })))
    {
        var responses = router.Handle(equip.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"EquipResponse\"") &&
                JsonSerializer.Serialize(x).Contains(
                    "\"Success\":true")),
            "EquipRequest succeeds");
        Check(store.GetCharacterAssets(account.Value, id).Equips
                .Any(x => Convert.ToInt64(x["ItemID"]) == equipUid &&
                          Convert.ToInt32(x["Part"]) == 5),
            "equipped slot persists");
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains("\"Type\":\"PlayerStatusSync\"") &&
                JsonSerializer.Serialize(x).Contains($"\"Attack\":{baseAttackBeforeEquip + 10}")),
            "equipping weapon increases effective attack");
    }

    using (var unequip = Json(
        """{"Type":"UnequipRequest","TargetSlot":5}"""))
    {
        var responses = router.Handle(unequip.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"EquipResponse\"") &&
                JsonSerializer.Serialize(x).Contains(
                    "\"Success\":true")),
            "UnequipRequest succeeds");
        Check(store.GetCharacterAssets(account.Value, id).Equips
                .Any(x => Convert.ToInt64(x["ItemID"]) == equipUid &&
                          Convert.ToInt32(x["Part"]) == 255),
            "unequipped slot persists");
    }

    using (var drive = Json(JsonSerializer.Serialize(new { Type = "DriveMechaRequest", HorseUID = horseUid })))
    {
        var responses = router.Handle(drive.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"DriveMechaResponse\"") &&
                JsonSerializer.Serialize(x).Contains(
                    "\"Success\":true")),
            "DriveMechaRequest succeeds");
        Check(Convert.ToInt64(
                  store.GetCharacter(account.Value, id)!["SummonedHorseUID"]) == horseUid,
            "summoned mecha persists on character");
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains("\"Type\":\"PlayerStatusSync\"")),
            "driving mecha refreshes effective combat stats");
    }

    var energyBeforeCharge = Convert.ToInt32(
        store.GetCharacterAssets(account.Value, id).Horses
            .First(x => Convert.ToInt64(x["HorseUID"]) == horseUid)["EnergyLeft"]);

    using (var charge = Json(JsonSerializer.Serialize(new
    {
        Type = "HorseChargeRequest",
        HorseUID = horseUid,
        ItemUID = batteryUid
    })))
    {
        var responses = router.Handle(charge.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains("\"Type\":\"SyncMechaEnergy\"")),
            "HorseChargeRequest emits mecha energy sync");

        var assetsAfterCharge = store.GetCharacterAssets(account.Value, id);
        var horseAfterCharge = assetsAfterCharge.Horses
            .First(x => Convert.ToInt64(x["HorseUID"]) == horseUid);

        Check(Convert.ToInt32(horseAfterCharge["EnergyLeft"]) ==
                  energyBeforeCharge + 3600,
            "source battery adds expected mecha energy");
        Check(!assetsAfterCharge.Inventory.Any(x =>
                Convert.ToInt64(x["ItemUID"]) == batteryUid),
            "mecha battery is consumed");
    }

    var directBatteryUid = store.AddInventoryItem(
        account.Value,
        id,
        50102,
        1);
    var directEnergyBefore = Convert.ToInt32(
        store.GetCharacterAssets(
                account.Value,
                id)
            .Horses
            .First(x =>
                Convert.ToInt64(x["HorseUID"]) == horseUid)["EnergyLeft"]);

    using (var useBattery = Json(
        JsonSerializer.Serialize(new
        {
            Type = "UseItemRequest",
            ItemUID = directBatteryUid
        })))
    {
        var responses = router.Handle(
            useBattery.RootElement,
            session);
        var after = store.GetCharacterAssets(
            account.Value,
            id);
        var chargedHorse = after.Horses
            .First(x =>
                Convert.ToInt64(x["HorseUID"]) == horseUid);

        Check(
            Convert.ToInt32(chargedHorse["EnergyLeft"]) ==
                Math.Min(25_200, directEnergyBefore + 3_600) &&
            !after.Inventory.Any(x =>
                Convert.ToInt64(x["ItemUID"]) ==
                    directBatteryUid) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SyncMechaEnergy\"")) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"PlayItemEffect\"")),
            "UseItemRequest directly charges the currently driven mecha and consumes one battery");
    }

    var fillBatteryUid = store.AddInventoryItem(
        account.Value,
        id,
        50102,
        1);

    Check(store.ChargeHorse(
              account.Value,
              id,
              horseUid,
              fillBatteryUid,
              99_999,
              25_200) is not null,
        "direct store charge clamps test mecha to max battery");

    var fullBatteryUid = store.AddInventoryItem(
        account.Value,
        id,
        50102,
        1);

    using (var useFullBattery = Json(
        JsonSerializer.Serialize(new
        {
            Type = "UseItemRequest",
            ItemUID = fullBatteryUid
        })))
    {
        var responses = router.Handle(
            useFullBattery.RootElement,
            session);
        var after = store.GetCharacterAssets(
            account.Value,
            id);
        var fullHorse = after.Horses
            .First(x =>
                Convert.ToInt64(x["HorseUID"]) == horseUid);

        Check(
            Convert.ToInt32(fullHorse["EnergyLeft"]) == 25_200 &&
            after.Inventory.Any(x =>
                Convert.ToInt64(x["ItemUID"]) ==
                    fullBatteryUid) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "機甲能源已滿")),
            "full mecha rejects battery use without consuming the battery");
    }

    using (var horseEquip = Json(JsonSerializer.Serialize(new { Type = "HorseEquipRequest", EquipUID = horseEquipUid, HorseUID = horseUid })))
    {
        var responses = router.Handle(horseEquip.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"HorseSync\"")),
            "HorseEquipRequest refreshes HorseSync");
        Check(store.GetCharacterAssets(account.Value, id).HorseEquips
                .Any(x => Convert.ToInt64(x["EquipUID"]) == horseEquipUid &&
                          Convert.ToInt64(x["HorseUID"]) == horseUid &&
                          Convert.ToInt32(x["Part"]) == 1),
            "mecha equipment assignment and part persist");
    }

    // Beast Spirit is a real mecha reinforcement card (slot 3).
    using (var tooLow = Json(JsonSerializer.Serialize(new
    {
        Type = "HorseEquipRequest",
        EquipUID = beastCardUid1,
        HorseUID = horseUid
    })))
    {
        var responses = router.Handle(tooLow.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains("機甲等級不足")),
            "Lv1 mecha cannot equip Lv20 Beast Spirit card");
    }

    using (var mechaLevel = Json(JsonSerializer.Serialize(new
    {
        Type = "ChatMessage",
        Channel = 0,
        Message = $"/gm mechalevel {horseUid} 20",
        BubbleType = 0
    })))
    {
        var responses = router.Handle(mechaLevel.RootElement, session);
        var leveled = store.GetCharacterAssets(account.Value, id).Horses
            .First(x => Convert.ToInt64(x["HorseUID"]) == horseUid);

        Check(Convert.ToInt32(leveled["Level"]) == 20 &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"Type\":\"SyncMechaEnergy\"")),
            "GM mechalevel changes level and syncs mecha level/EXP");
    }

    foreach (var cardUid in new[]
             {
                 beastCardUid1,
                 beastCardUid2,
                 beastCardUid3
             })
    {
        using var equipCard = Json(JsonSerializer.Serialize(new
        {
            Type = "HorseEquipRequest",
            EquipUID = cardUid,
            HorseUID = horseUid
        }));
        router.Handle(equipCard.RootElement, session);
    }

    var cardParts = store.GetCharacterAssets(account.Value, id).HorseEquips
        .Where(x =>
            Convert.ToInt32(x["EquipIdx"]) == 51304 &&
            Convert.ToInt64(x["HorseUID"]) == horseUid)
        .Select(x => Convert.ToInt32(x["Part"]))
        .OrderBy(x => x)
        .ToArray();

    Check(cardParts.SequenceEqual(new[] { 3, 4, 5 }),
        "three reinforcement cards occupy independent card1/card2/card3 slots");

    var cardAssets = store.GetCharacterAssets(account.Value, id);
    var cardCharacter = store.GetCharacter(account.Value, id)!;
    var baseMaxHp = Convert.ToInt32(cardCharacter["MaxHP"]);
    var cardStatusJson = JsonSerializer.Serialize(
        SGO.Server.Application.Assets.CharacterStatCalculator
            .ToPlayerStatusPacket(
                cardCharacter,
                cardAssets,
                currentCatalog));

    Check(cardStatusJson.Contains($"\"MaxHP\":{baseMaxHp + 1500}"),
        "three Beast Spirit cards stack their +500 MaxHP bonuses");

    var expHorseBefore = store.GetCharacterAssets(account.Value, id).Horses
        .First(x => Convert.ToInt64(x["HorseUID"]) == horseUid);
    var expBefore = Convert.ToInt32(expHorseBefore["Exp"]);
    var expHorseAfter = store.AddHorseExperience(
        account.Value,
        id,
        horseUid,
        2500)!;

    Check(Convert.ToInt32(expHorseAfter["Exp"]) > expBefore ||
          Convert.ToInt32(expHorseAfter["Level"]) >
              Convert.ToInt32(expHorseBefore["Level"]),
        "mecha EXP persists and can advance mecha level");

    using (var petSummon = Json(JsonSerializer.Serialize(new { Type = "PetAction", Action = "Summon", PetUID = petUid })))
    {
        var responses = router.Handle(petSummon.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"PetSync\"")),
            "PetAction Summon refreshes PetSync");
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SpawnEntity\"") &&
                JsonSerializer.Serialize(x).Contains(
                    "\"EntityType\":4") &&
                JsonSerializer.Serialize(x).Contains(
                    "\"Sprite\":\"EN050A\"")),
            "PetAction Summon spawns current-client pet appearance");
        Check(store.GetCharacterAssets(account.Value, id).Pets
                .Any(x => Convert.ToInt64(x["UID"]) == petUid &&
                          Convert.ToInt32(x["State"]) == 1),
            "pet summon state persists");
    }

    using (var petRename = Json(JsonSerializer.Serialize(new { Type = "PetAction", Action = "Rename", PetUID = petUid, NewName = "Momo" })))
    {
        router.Handle(petRename.RootElement, session);
        Check(store.GetCharacterAssets(account.Value, id).Pets
                .Any(x => Convert.ToInt64(x["UID"]) == petUid &&
                          x["Name"].ToString() == "Momo"),
            "pet rename persists");
    }

    using (var petEquip = Json(JsonSerializer.Serialize(new { Type = "PetAction", Action = "Equip", PetUID = petUid, ItemUID = equipUid, TargetSlot = 0 })))
    {
        router.Handle(petEquip.RootElement, session);
        Check(store.GetCharacterAssets(account.Value, id).Pets
                .Any(x => Convert.ToInt64(x["UID"]) == petUid &&
                          Convert.ToInt64(x["Equip0"]) == equipUid),
            "pet equipment assignment persists");
    }

    using (var petUnequip = Json(JsonSerializer.Serialize(new { Type = "PetAction", Action = "Unequip", PetUID = petUid, TargetSlot = 0 })))
    {
        router.Handle(petUnequip.RootElement, session);
        Check(store.GetCharacterAssets(account.Value, id).Pets
                .Any(x => Convert.ToInt64(x["UID"]) == petUid &&
                          Convert.ToInt64(x["Equip0"]) == 0),
            "pet equipment removal persists");
    }

    using (var petRecall = Json(JsonSerializer.Serialize(new { Type = "PetAction", Action = "Recall", PetUID = petUid })))
    {
        var responses = router.Handle(petRecall.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"DespawnEntity\"")),
            "PetAction Recall despawns pet entity");
        Check(store.GetCharacterAssets(account.Value, id).Pets
                .Any(x => Convert.ToInt64(x["UID"]) == petUid &&
                          Convert.ToInt32(x["State"]) == 0),
            "pet recall state persists");
    }

    // World entities are now server-driven. No player movement is needed
    // for the first NPC spawn.
    var initialWorldPackets = router.Tick(
        session,
        DateTimeOffset.UtcNow);

    Check(initialWorldPackets.Count == 52,
        "server world tick spawns Map15 NPCs without player movement");
    Check(initialWorldPackets.All(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"SpawnEntity\"")),
        "Map15 NPCs use SpawnEntity packets");
    Check(!session.PendingMapSpawn,
        "Map15 spawn gate clears after server world tick");

    using (var move = Json(
        """{"Type":"SyncPosition","X":4932,"Y":7783}"""))
    {
        var responses = router.Handle(move.RootElement, session);
        Check(responses.Count == 0,
            "SyncPosition only updates position after server-driven world tick");
    }

    using (var summonForFollow = Json(JsonSerializer.Serialize(new
    {
        Type = "PetAction",
        Action = "Summon",
        PetUID = petUid
    })))
    {
        router.Handle(summonForFollow.RootElement, session);
    }

    session.LastPetFollowTick = DateTimeOffset.MinValue;
    using (var followMove = Json(JsonSerializer.Serialize(new
    {
        Type = "SyncPosition",
        X = 5000,
        Y = 7800
    })))
    {
        var responses = router.Handle(followMove.RootElement, session);
        Check(responses.Count == 0,
            "player movement no longer directly drives pet AI");
    }

    var followPackets = router.Tick(
        session,
        DateTimeOffset.UtcNow.AddSeconds(1));

    Check(followPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains("\"Type\":\"MoveEntity\"") &&
            JsonSerializer.Serialize(x).Contains(
                $"\"EntityID\":{4_000_000L + petUid}")),
        "summoned pet follows player from server world tick");

    var speechPeer = broadcastHub.Register();
    broadcastHub.UpdatePresence(
        speechPeer.ConnectionId,
        session.CurrentMapId,
        secondCharacterId,
        account.Value,
        session.LastX + 50,
        session.LastY);

    while (speechPeer.Queue.TryDequeue(out _)) { }

    session.PetPositions[petUid] =
        (session.LastX + 42, session.LastY + 24);
    session.PetTargets.Remove(petUid);
    session.PetNextIdleSpeechAt[petUid] =
        DateTimeOffset.UtcNow.AddSeconds(-1);

    var idleSpeechPackets = router.Tick(
        session,
        DateTimeOffset.UtcNow.AddSeconds(2));

    Check(
        idleSpeechPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"PetIdleSpeech\"") &&
            JsonSerializer.Serialize(x).Contains(
                $"\"EntityID\":{4_000_000L + petUid}")) &&
        speechPeer.Queue.ToArray().Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"PetIdleSpeech\"")),
        "idle summoned pet emits configurable speech to owner and same-map peers");

    broadcastHub.Unregister(
        speechPeer.ConnectionId);

    var selected = store.GetCharacter(account.Value, id);
    Check(Convert.ToInt32(Convert.ToDouble(selected!["LocX"])) == 5000 &&
          Convert.ToInt32(Convert.ToDouble(selected["LocY"])) == 7800,
        "movement and pet-follow position persist to SQLite");

    var merchant = map15Npcs.First(x =>
        x.Script.Equals("shop_e3.txt", StringComparison.OrdinalIgnoreCase));

    using (var moveToMerchant = Json(JsonSerializer.Serialize(new
    {
        Type = "SyncPosition",
        X = merchant.X,
        Y = merchant.Y
    })))
    {
        router.Handle(moveToMerchant.RootElement, session);
    }

    using (var interactMerchant = Json(JsonSerializer.Serialize(new
    {
        Type = "InteractRequest",
        EntityID = merchant.EntityId
    })))
    {
        var responses = router.Handle(interactMerchant.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains("\"Type\":\"NpcDialogResponse\"") &&
                JsonSerializer.Serialize(x).Contains("reply#0")),
            "Map15 merchant is interactable through world entity");
    }

    using (var openMerchantShop = Json(JsonSerializer.Serialize(new
    {
        Type = "NpcOptionRequest",
        EntityID = merchant.EntityId,
        ScriptName = merchant.Script,
        TargetNode = "reply#0"
    })))
    {
        var responses = router.Handle(openMerchantShop.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains("\"Type\":\"NpcShopViewResponse\"") &&
                JsonSerializer.Serialize(x).Contains("\"ShopID\":7") &&
                JsonSerializer.Serialize(x).Contains("\"Items\":[")),
            "Map15 merchant option opens populated NPC shop");
    }

    var portal = map15Npcs[0];

    using (var moveBackToPortal = Json(JsonSerializer.Serialize(new
    {
        Type = "SyncPosition",
        X = portal.X,
        Y = portal.Y
    })))
    {
        router.Handle(moveBackToPortal.RootElement, session);
    }

    using (var interact = Json(JsonSerializer.Serialize(new
    {
        Type = "InteractRequest",
        EntityID = portal.EntityId
    })))
    {
        var responses = router.Handle(interact.RootElement, session);
        Check(responses.Count >= 1 &&
              JsonSerializer.Serialize(responses[0]).Contains(
                  "\"Type\":\"NpcDialogResponse\""),
            "Map15 portal executes Lua start()");
        Check(JsonSerializer.Serialize(responses[0]).Contains("Check#0"),
            "Ask options encode GetChoice target");
    }

    using (var option = Json(JsonSerializer.Serialize(new
    {
        Type = "NpcOptionRequest",
        EntityID = portal.EntityId,
        ScriptName = portal.Script,
        TargetNode = "Check#0"
    })))
    {
        var responses = router.Handle(option.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"ChangeMap\"")),
            "portal choice emits ChangeMap");
        Check(session.CurrentMapId == 16 &&
              session.PendingMapSpawn,
            "portal changes session from Map15 to Map16");
    }

    selected = store.GetCharacter(account.Value, id);
    Check(Convert.ToInt32(selected!["MapID"]) == 16 &&
          Convert.ToInt32(Convert.ToDouble(selected["LocX"])) == 10816 &&
          Convert.ToInt32(Convert.ToDouble(selected["LocY"])) == 3104,
        "portal destination persists Map16 coordinates in SQLite");

    using (var map16Ready = Json(
        """{"Type":"SyncPosition","X":10816,"Y":3104}"""))
    {
        var responses = router.Handle(map16Ready.RootElement, session);
        Check(responses.Count == 0,
            "Map16 position update does not drive world spawn");
    }

    var map16Packets = router.Tick(
        session,
        DateTimeOffset.UtcNow.AddSeconds(2));

    Check(!session.PendingMapSpawn,
        "Map16 spawn gate clears after server world tick");
    Check(map16Packets.Count >= world.GetNpcs(16).Count &&
          map16Packets.Count(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"EntityType\":2")) == world.GetNpcs(16).Count,
        "Map16 NPCs spawn from server world tick");
    Check(map16Packets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"EntityType\":4")),
        "summoned pet respawns after map transition");

    // Combat runtime: use a monster map directly so this test is independent
    // from the exact portal route used by the recovered client.
    var combatMapId = 7;
    var combatEnemy = world.GetEnemies(combatMapId)
        .First(x => x.Level < 65535);

    store.UpdateLocation(
        account.Value,
        id,
        combatMapId,
        combatEnemy.X,
        combatEnemy.Y);

    session.CurrentMapId = combatMapId;
    session.PendingMapSpawn = false;
    router.SyncPresence(session);

    store.SetSkill(
        account.Value,
        id,
        29101,
        1);
    store.Revive(account.Value, id);

    var bulletsBefore = store.GetCharacterAssets(
            account.Value,
            id)
        .Inventory
        .Where(x =>
            Convert.ToInt32(x["ItemIndex"]) == 4103)
        .Sum(x => Convert.ToInt32(x["Count"]));

    var craftSkillPackets = combat.UseSkill(
        session,
        account.Value,
        29101,
        0,
        0,
        combatEnemy.X,
        combatEnemy.Y,
        DateTimeOffset.UtcNow);

    var bulletsAfter = store.GetCharacterAssets(
            account.Value,
            id)
        .Inventory
        .Where(x =>
            Convert.ToInt32(x["ItemIndex"]) == 4103)
        .Sum(x => Convert.ToInt32(x["Count"]));

    Check(
        bulletsAfter == bulletsBefore + 100 &&
        craftSkillPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"InventorySync\"")),
        "製彈術 parses AddItem special metadata and creates 100 current bullets");

    store.SetSkill(
        account.Value,
        id,
        25108,
        1);
    store.ApplyDamage(
        account.Value,
        id,
        50);

    var sealBeforeHp = Convert.ToInt32(
        store.GetCharacter(account.Value, id)!["HP"]);

    combat.UseSkill(
        session,
        account.Value,
        25108,
        0,
        0,
        combatEnemy.X,
        combatEnemy.Y,
        DateTimeOffset.UtcNow.AddSeconds(2));

    var sealAfter = store.GetCharacter(
        account.Value,
        id)!;

    Check(
        Convert.ToInt32(sealAfter["HP"]) >
            sealBeforeHp &&
        Convert.ToInt32(sealAfter["HP"]) ==
            Convert.ToInt32(sealAfter["MaxHP"]),
        "生命聖印 restores the caster to full HP");

    var combatSpawns = combat.EnterMap(session, combatMapId);

    Check(session.Monsters.Count > 0 &&
          combatSpawns.Any(x =>
              JsonSerializer.Serialize(x).Contains("\"EntityType\":1")),
        "monster map initializes server-shared combat entities");

    var monster = session.Monsters.Values.First();

    store.SetSkill(
        account.Value,
        id,
        23108,
        1);
    store.Revive(account.Value, id);

    var berserkerBefore = store.GetCharacter(
        account.Value,
        id)!;
    var berserkerBase = CharacterStatCalculator.Calculate(
        berserkerBefore,
        store.GetCharacterAssets(
            account.Value,
            id),
        currentCatalog);
    var berserkerAt =
        DateTimeOffset.UtcNow.AddSeconds(1);

    var berserkerPackets = combat.UseSkill(
        session,
        account.Value,
        23108,
        0,
        0,
        combatEnemy.X,
        combatEnemy.Y,
        berserkerAt);

    var expectedBerserkerAttack =
        berserkerBase.Attack +
        berserkerBase.Attack * 50 / 100;
    var berserkerJson =
        JsonSerializer.Serialize(
            berserkerPackets);

    Check(
        berserkerJson.Contains("狂戰士體") &&
        berserkerJson.Contains(
            $"\"Attack\":{expectedBerserkerAttack}"),
        "狂戰士體 +50% attack is applied to effective combat stats");

    var berserkerExpired = JsonSerializer.Serialize(
        combat.TickBuffs(
            session,
            berserkerAt.AddSeconds(61)));

    Check(
        berserkerExpired.Contains(
            "\"Buffs\":[]") &&
        berserkerExpired.Contains(
            $"\"Attack\":{berserkerBase.Attack}"),
        "percentage attack buff expiry restores original effective attack");

    store.SetSkill(
        account.Value,
        id,
        25104,
        1);
    store.Revive(account.Value, id);
    store.ApplyDamage(
        account.Value,
        id,
        99);
    store.UpdateLocation(
        account.Value,
        id,
        combatMapId,
        Convert.ToInt32(monster.X),
        Convert.ToInt32(monster.Y));
    session.LastX = monster.X;
    session.LastY = monster.Y;

    var lifeDrainHpBefore = Convert.ToInt32(
        store.GetCharacter(account.Value, id)!["HP"]);
    var lifeDrainMonsterHpBefore = monster.CurrentHp;

    var lifeDrainPackets = combat.UseSkill(
        session,
        account.Value,
        25104,
        monster.EntityId,
        1,
        Convert.ToInt32(monster.X),
        Convert.ToInt32(monster.Y),
        DateTimeOffset.UtcNow.AddSeconds(3));

    var lifeDrainAfter = store.GetCharacter(
        account.Value,
        id)!;

    var lifeDrainDamageDealt = Math.Max(
        0,
        lifeDrainMonsterHpBefore - monster.CurrentHp);
    var lifeDrainRecovered = Math.Max(
        0,
        Convert.ToInt32(lifeDrainAfter["HP"]) -
        lifeDrainHpBefore);

    Check(
        lifeDrainDamageDealt > 0 &&
        lifeDrainRecovered ==
            Math.Min(lifeDrainDamageDealt, 94) &&
        Convert.ToInt32(lifeDrainAfter["HP"]) <=
            Convert.ToInt32(lifeDrainAfter["MaxHP"]) &&
        lifeDrainPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"EntityDamage\"")) &&
        lifeDrainPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"EntityHeal\"")),
        "生命吸收 Lv1 restores min(actual damage, original 94 HP cap)");

    store.SetSkill(
        account.Value,
        id,
        25101,
        1);
    store.Revive(account.Value, id);
    store.ApplyDamage(
        account.Value,
        id,
        60);

    var selfCureBefore = Convert.ToInt32(
        store.GetCharacter(account.Value, id)!["HP"]);

    var selfCurePackets = combat.UseSkill(
        session,
        account.Value,
        25101,
        0,
        0,
        Convert.ToInt32(monster.X),
        Convert.ToInt32(monster.Y),
        DateTimeOffset.UtcNow.AddSeconds(6));

    var selfCureAfter = Convert.ToInt32(
        store.GetCharacter(account.Value, id)!["HP"]);
    var selfCureAmount =
        selfCureAfter - selfCureBefore;

    Check(
        selfCureAmount is >= 12 and <= 14 &&
        selfCurePackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"EntityHeal\"")),
        "體力回復 Lv1 uses original fixed 13 HP cure with ±10% variance");

    var peerRegistration = broadcastHub.Register();
    var peerSession = new ClientSession
    {
        ConnectionId = peerRegistration.ConnectionId,
        AccountId = account.Value,
        CharacterId = secondCharacterId,
        CurrentMapId = combatMapId,
        LastX = combatEnemy.X,
        LastY = combatEnemy.Y,
        PendingMapSpawn = true
    };

    store.UpdateLocation(
        account.Value,
        secondCharacterId,
        combatMapId,
        combatEnemy.X,
        combatEnemy.Y);

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }
    while (peerRegistration.Queue.TryDequeue(out _)) { }

    router.SyncPresence(peerSession);

    Check(
        broadcastRegistration.Queue.TryDequeue(
            out var peerPlayerSpawn) &&
        JsonSerializer.Serialize(peerPlayerSpawn).Contains(
            "\"EntityType\":0") &&
        JsonSerializer.Serialize(peerPlayerSpawn).Contains(
            $"\"EntityID\":{secondCharacterId}") &&
        peerRegistration.Queue.IsEmpty,
        "joining player broadcasts SpawnEntity to existing map peers only");

    while (peerRegistration.Queue.TryDequeue(out _)) { }

    using (var publicStatusEquip = Json(
        JsonSerializer.Serialize(new
        {
            Type = "EquipRequest",
            ItemUID = equipUid,
            TargetSlot = 5
        })))
    {
        var responses = router.Handle(
            publicStatusEquip.RootElement,
            session);

        var peerStatusPackets =
            peerRegistration.Queue.ToArray();
        var publicStatusJson =
            JsonSerializer.Serialize(peerStatusPackets);

        Check(
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"PlayerStatusSync\"")) &&
            publicStatusJson.Contains(
                "\"Type\":\"SyncStatus\"") &&
            publicStatusJson.Contains(
                $"\"EntityID\":{id}") &&
            !publicStatusJson.Contains("\"Money\"") &&
            !publicStatusJson.Contains("\"ExpAcc\""),
            "effective status changes return private PlayerStatusSync and broadcast public SyncStatus only");
    }

    using (var publicStatusUnequip = Json(
        """{"Type":"UnequipRequest","TargetSlot":5}"""))
    {
        router.Handle(
            publicStatusUnequip.RootElement,
            session);
    }

    while (peerRegistration.Queue.TryDequeue(out _)) { }

    store.SetSkill(
        account.Value,
        id,
        27104,
        1);
    store.Revive(account.Value, id);
    store.ApplyDamage(
        account.Value,
        secondCharacterId,
        999_999);

    store.UpdateLocation(
        account.Value,
        id,
        combatMapId,
        combatEnemy.X,
        combatEnemy.Y);
    store.UpdateLocation(
        account.Value,
        secondCharacterId,
        combatMapId,
        combatEnemy.X + 40,
        combatEnemy.Y);

    session.LastX = combatEnemy.X;
    session.LastY = combatEnemy.Y;
    peerSession.LastX = combatEnemy.X + 40;
    peerSession.LastY = combatEnemy.Y;

    while (peerRegistration.Queue.TryDequeue(out _)) { }

    var reviveCast = combat.UseSkill(
        session,
        account.Value,
        27104,
        checked((int)secondCharacterId),
        0,
        combatEnemy.X + 40,
        combatEnemy.Y,
        DateTimeOffset.UtcNow);

    var revivePromptJson = JsonSerializer.Serialize(
        peerRegistration.Queue.ToArray());

    Check(
        reviveCast.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"SkillCast\"")) &&
        revivePromptJson.Contains(
            "\"Type\":\"RevivePrompt\"") &&
        revivePromptJson.Contains("\"HPPct\":30") &&
        revivePromptJson.Contains("\"MPPct\":30"),
        "生命之源 Lv1 sends original 30% HP/MP revive prompt");

    var reviveAccepted = combat.AcceptRevive(
        peerSession,
        account.Value,
        27104,
        1,
        DateTimeOffset.UtcNow.AddSeconds(1));

    var revivedCharacter = store.GetCharacter(
        account.Value,
        secondCharacterId)!;

    Check(
        Convert.ToInt32(revivedCharacter["HP"]) ==
            Math.Max(
                1,
                Convert.ToInt32(
                    revivedCharacter["MaxHP"]) * 30 / 100) &&
        Convert.ToInt32(revivedCharacter["MP"]) ==
            Convert.ToInt32(
                revivedCharacter["MaxMP"]) * 30 / 100 &&
        reviveAccepted.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"EntityHeal\"")),
        "accepted 生命之源 Lv1 restores original 30% HP and MP");

    store.SetSkill(
        account.Value,
        id,
        28102,
        1);
    store.Revive(account.Value, id);
    store.SpendSkillResources(
        account.Value,
        secondCharacterId,
        0,
        0,
        50);

    var targetSpBefore = Convert.ToInt32(
        store.GetCharacter(
            account.Value,
            secondCharacterId)!["SP"]);

    while (peerRegistration.Queue.TryDequeue(out _)) { }

    var spHealPackets = combat.UseSkill(
        session,
        account.Value,
        28102,
        checked((int)secondCharacterId),
        0,
        combatEnemy.X + 40,
        combatEnemy.Y,
        DateTimeOffset.UtcNow.AddSeconds(2));

    var targetSpAfter = Convert.ToInt32(
        store.GetCharacter(
            account.Value,
            secondCharacterId)!["SP"]);

    var targetSpRecovered =
        targetSpAfter - targetSpBefore;

    Check(
        targetSpRecovered is >= 35 and <= 41 &&
        spHealPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"SkillCast\"")) &&
        peerRegistration.Queue.ToArray().Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"PlayerStatusSync\"")) &&
        peerRegistration.Queue.ToArray().Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"SyncStatus\"") &&
            JsonSerializer.Serialize(x).Contains(
                $"\"EntityID\":{secondCharacterId}")),
        "心靈轉換 restores another same-map player's SP and broadcasts public SyncStatus");

    store.SetSkill(
        account.Value,
        id,
        20103,
        1);
    store.Revive(account.Value, id);

    var scoutPackets = combat.UseSkill(
        session,
        account.Value,
        20103,
        checked((int)secondCharacterId),
        0,
        combatEnemy.X + 40,
        combatEnemy.Y,
        DateTimeOffset.UtcNow.AddSeconds(4));

    var scoutJson = JsonSerializer.Serialize(
        scoutPackets);

    Check(
        scoutJson.Contains("[偵察]") &&
        scoutJson.Contains("SecondCharacter") &&
        !scoutJson.Contains("CharacterPwd") &&
        !scoutJson.Contains("\"ACCID\""),
        "偵察 returns public target status without account/password fields");

    store.SetSkill(
        account.Value,
        id,
        21103,
        1);
    store.Revive(account.Value, id);

    monster.X = combatEnemy.X + 30;
    monster.Y = combatEnemy.Y;
    monster.HasTarget = false;
    monster.TargetCharacterId = 0;

    var tauntPackets = combat.UseSkill(
        session,
        account.Value,
        21103,
        0,
        0,
        combatEnemy.X,
        combatEnemy.Y,
        DateTimeOffset.UtcNow.AddSeconds(5));

    Check(
        monster.HasTarget &&
        monster.TargetCharacterId == id &&
        tauntPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"MonsterTarget\"") &&
            JsonSerializer.Serialize(x).Contains(
                $"\"TargetID\":{id}")),
        "挑撥 redirects nearby monster aggro to the caster");

    monster.HasTarget = false;
    monster.TargetCharacterId = 0;

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }
    while (peerRegistration.Queue.TryDequeue(out _)) { }

    var peerWorldPackets = router.Tick(
        peerSession,
        DateTimeOffset.UtcNow.AddSeconds(1));

    Check(peerWorldPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"EntityType\":0") &&
            JsonSerializer.Serialize(x).Contains(
                $"\"EntityID\":{id}")),
        "joining player receives existing same-map players in world snapshot");

    var senderMoneyBeforeTransfer = Convert.ToInt64(
        store.GetCharacter(account.Value, id)!["Money"]);
    var targetMoneyBeforeTransfer = Convert.ToInt64(
        store.GetCharacter(
            account.Value,
            secondCharacterId)!["Money"]);

    while (peerRegistration.Queue.TryDequeue(out _)) { }

    using (var directTransfer = Json(
        JsonSerializer.Serialize(new
        {
            Type = "DirectTransfer",
            TargetDBID = secondCharacterId,
            Amount = 321L
        })))
    {
        var responses = router.Handle(
            directTransfer.RootElement,
            session);

        Check(
            Convert.ToInt64(
                store.GetCharacter(
                    account.Value,
                    id)!["Money"]) ==
                senderMoneyBeforeTransfer - 321 &&
            Convert.ToInt64(
                store.GetCharacter(
                    account.Value,
                    secondCharacterId)!["Money"]) ==
                targetMoneyBeforeTransfer + 321 &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SyncMoney\"")) &&
            peerRegistration.Queue.ToArray().Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SyncMoney\"")),
            "DirectTransfer atomically moves money and syncs both online players");
    }

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }
    while (peerRegistration.Queue.TryDequeue(out _)) { }

    using (var partyInvite = Json(
        JsonSerializer.Serialize(new
        {
            Type = "PartyAction",
            Action = "Invite",
            TargetDBID = secondCharacterId
        })))
    {
        var responses = router.Handle(
            partyInvite.RootElement,
            session);

        Check(
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "隊伍邀請已送出")) &&
            peerRegistration.Queue.ToArray().Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"PartyInvite\"") &&
                JsonSerializer.Serialize(x).Contains(
                    "TestCharacter")),
            "Party Invite targets the selected online player");
    }

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }

    using (var partyAccept = Json(
        JsonSerializer.Serialize(new
        {
            Type = "PartyAction",
            Action = "Accept",
            TargetDBID = id
        })))
    {
        var responses = router.Handle(
            partyAccept.RootElement,
            peerSession);

        var firstPartyId = party.GetPartyId(id);
        Check(
            !string.IsNullOrWhiteSpace(firstPartyId) &&
            firstPartyId ==
                party.GetPartyId(secondCharacterId) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"PartySync\"") &&
                JsonSerializer.Serialize(x).Contains(
                    firstPartyId)) &&
            broadcastRegistration.Queue.ToArray().Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"PartySync\"") &&
                JsonSerializer.Serialize(x).Contains(
                    firstPartyId)) &&
            broadcastRegistration.Queue.ToArray().Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"UpdateNameColor\"") &&
                JsonSerializer.Serialize(x).Contains(
                    $"\"PartyID\":\"{firstPartyId}\"")),
            "Party Invite/Accept synchronizes party state and name-color PartyID to map peers");
    }

    var casterBeforeIceTouch = store.GetCharacter(
        account.Value,
        id)!;

    store.ApplyGrowthStats(
        account.Value,
        id,
        Convert.ToInt32(casterBeforeIceTouch["MaxHP"]),
        Math.Max(
            500,
            Convert.ToInt32(casterBeforeIceTouch["MaxMP"])),
        Math.Max(
            100,
            Convert.ToInt32(casterBeforeIceTouch["MaxSP"])),
        Convert.ToInt32(casterBeforeIceTouch["Attack"]),
        Convert.ToInt32(casterBeforeIceTouch["Defense"]),
        Convert.ToInt32(casterBeforeIceTouch["MagicAttack"]),
        Convert.ToInt32(casterBeforeIceTouch["MagicDefense"]),
        Convert.ToInt32(casterBeforeIceTouch["Hit"]),
        Convert.ToInt32(casterBeforeIceTouch["Dodge"]),
        Convert.ToInt32(casterBeforeIceTouch["MoveSpeed"]));

    store.SetSkill(
        account.Value,
        id,
        33011,
        1);

    var peerBeforeIceTouch = store.GetCharacter(
        account.Value,
        secondCharacterId)!;
    var peerBaseStats = CharacterStatCalculator.Calculate(
        peerBeforeIceTouch,
        store.GetCharacterAssets(
            account.Value,
            secondCharacterId),
        currentCatalog);

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }
    while (peerRegistration.Queue.TryDequeue(out _)) { }

    var iceTouchAt =
        DateTimeOffset.UtcNow.AddSeconds(2);

    var iceTouchPackets = combat.UseSkill(
        session,
        account.Value,
        33011,
        0,
        0,
        combatEnemy.X,
        combatEnemy.Y,
        iceTouchAt);

    var peerIceTouchJson = JsonSerializer.Serialize(
        peerRegistration.Queue.ToArray());

    Check(
        iceTouchPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                $"\"ID\":{secondCharacterId}")) &&
        peerIceTouchJson.Contains(
            "\"Type\":\"SyncBuffs\"") &&
        peerIceTouchJson.Contains("冰之觸") &&
        peerIceTouchJson.Contains(
            $"\"Defense\":{peerBaseStats.Defense + 80}") &&
        peerIceTouchJson.Contains(
            $"\"MagicDefense\":{peerBaseStats.MagicDefense + 80}"),
        "冰之觸 applies real +80 DEF/MDEF to online same-map party members");

    var expiredPeerBuffPackets = combat.TickBuffs(
        peerSession,
        iceTouchAt.AddSeconds(901));

    var expiredPeerBuffJson =
        JsonSerializer.Serialize(
            expiredPeerBuffPackets);

    Check(
        expiredPeerBuffJson.Contains(
            "\"Type\":\"SyncBuffs\"") &&
        expiredPeerBuffJson.Contains(
            "\"Buffs\":[]") &&
        expiredPeerBuffJson.Contains(
            $"\"Defense\":{peerBaseStats.Defense}") &&
        expiredPeerBuffJson.Contains(
            $"\"MagicDefense\":{peerBaseStats.MagicDefense}"),
        "expired party buff removes icon and restores peer effective stats");

    combat.TickBuffs(
        session,
        iceTouchAt.AddSeconds(901));

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }
    while (peerRegistration.Queue.TryDequeue(out _)) { }

    store.SetSkill(
        account.Value,
        id,
        33012,
        1);

    var darkShadowAt =
        iceTouchAt.AddSeconds(902);

    var darkShadowPackets = combat.UseSkill(
        session,
        account.Value,
        33012,
        0,
        0,
        combatEnemy.X,
        combatEnemy.Y,
        darkShadowAt);

    var peerDarkShadowJson =
        JsonSerializer.Serialize(
            peerRegistration.Queue.ToArray());

    Check(
        darkShadowPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                $"\"ID\":{secondCharacterId}")) &&
        peerDarkShadowJson.Contains("黑暗之影") &&
        peerDarkShadowJson.Contains(
            $"\"Attack\":{peerBaseStats.Attack + 80}") &&
        peerDarkShadowJson.Contains(
            $"\"MagicAttack\":{peerBaseStats.MagicAttack + 80}") &&
        !JsonSerializer.Serialize(darkShadowPackets)
            .Contains("死亡角色"),
        "黑暗之影 type-6 data is treated as a party ATK/MATK buff, not revive");

    combat.TickBuffs(
        peerSession,
        darkShadowAt.AddSeconds(2));
    combat.TickBuffs(
        session,
        darkShadowAt.AddSeconds(2));

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }
    while (peerRegistration.Queue.TryDequeue(out _)) { }

    store.SetSkill(
        account.Value,
        id,
        31112,
        1);

    var portableRepairPackets = combat.UseSkill(
        session,
        account.Value,
        31112,
        0,
        0,
        combatEnemy.X,
        combatEnemy.Y,
        darkShadowAt.AddSeconds(3));

    Check(
        portableRepairPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"OpenRepairWindow\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"RepairType\":10")) &&
        !JsonSerializer.Serialize(portableRepairPackets)
            .Contains("死亡角色"),
        "隨身修理店 type-6 skill opens the repair UI instead of revive targeting");

    store.SetSkill(
        account.Value,
        id,
        28107,
        1);
    store.Revive(account.Value, id);
    store.Revive(
        account.Value,
        secondCharacterId);
    store.ApplyDamage(
        account.Value,
        id,
        20);
    store.ApplyDamage(
        account.Value,
        secondCharacterId,
        20);
    store.SpendSkillResources(
        account.Value,
        id,
        0,
        10,
        0);
    store.SpendSkillResources(
        account.Value,
        secondCharacterId,
        0,
        10,
        0);

    var groupBeforeSelf = store.GetCharacter(
        account.Value,
        id)!;
    var groupBeforePeer = store.GetCharacter(
        account.Value,
        secondCharacterId)!;

    var groupHealPackets = combat.UseSkill(
        session,
        account.Value,
        28107,
        0,
        0,
        combatEnemy.X,
        combatEnemy.Y,
        DateTimeOffset.UtcNow.AddSeconds(3));

    var groupAfterSelf = store.GetCharacter(
        account.Value,
        id)!;
    var groupAfterPeer = store.GetCharacter(
        account.Value,
        secondCharacterId)!;

    Check(
        Convert.ToInt32(groupAfterSelf["HP"]) >
            Convert.ToInt32(groupBeforeSelf["HP"]) &&
        Convert.ToInt32(groupAfterPeer["HP"]) >
            Convert.ToInt32(groupBeforePeer["HP"]) &&
        Convert.ToInt32(groupAfterSelf["MP"]) >
            Convert.ToInt32(groupBeforeSelf["MP"]) &&
        Convert.ToInt32(groupAfterPeer["MP"]) >
            Convert.ToInt32(groupBeforePeer["MP"]) &&
        groupHealPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"SkillCast\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"AoeTargets\"")),
        "治療風暴 restores HP and MP for online same-map party members");

    store.UpdateLocation(
        account.Value,
        secondCharacterId,
        combatMapId,
        combatEnemy.X + 550,
        combatEnemy.Y);
    peerSession.LastX = combatEnemy.X + 550;
    peerSession.LastY = combatEnemy.Y;

    store.ApplyDamage(
        account.Value,
        secondCharacterId,
        20);
    store.SpendSkillResources(
        account.Value,
        secondCharacterId,
        0,
        10,
        0);

    var farPeerBefore = store.GetCharacter(
        account.Value,
        secondCharacterId)!;

    combat.UseSkill(
        session,
        account.Value,
        28107,
        0,
        0,
        combatEnemy.X,
        combatEnemy.Y,
        DateTimeOffset.UtcNow.AddSeconds(5));

    var farPeerAfter = store.GetCharacter(
        account.Value,
        secondCharacterId)!;

    Check(
        Convert.ToInt32(farPeerAfter["HP"]) ==
            Convert.ToInt32(farPeerBefore["HP"]) &&
        Convert.ToInt32(farPeerAfter["MP"]) ==
            Convert.ToInt32(farPeerBefore["MP"]),
        "治療風暴 does not heal same-map party members outside original 400 range");

    store.UpdateLocation(
        account.Value,
        secondCharacterId,
        combatMapId,
        combatEnemy.X,
        combatEnemy.Y);
    peerSession.LastX = combatEnemy.X;
    peerSession.LastY = combatEnemy.Y;

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }
    while (peerRegistration.Queue.TryDequeue(out _)) { }

    using (var partyLeave = Json(
        """
        {"Type":"PartyAction","Action":"Leave","TargetDBID":-1}
        """))
    {
        var responses = router.Handle(
            partyLeave.RootElement,
            peerSession);

        Check(
            string.IsNullOrWhiteSpace(
                party.GetPartyId(id)) &&
            string.IsNullOrWhiteSpace(
                party.GetPartyId(secondCharacterId)) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"PartySync\"") &&
                JsonSerializer.Serialize(x).Contains(
                    "\"PartyID\":\"\"")) &&
            broadcastRegistration.Queue.ToArray().Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"PartyID\":\"\"")),
            "leaving a two-player party dissolves it for both players");
    }

    var firstTradeItemCountBefore = store.GetCharacterAssets(
            account.Value,
            id)
        .Inventory
        .Where(x =>
            Convert.ToInt32(x["ItemIndex"]) ==
                generalShopItem.ItemIndex &&
            Convert.ToInt32(x["InBank"]) == 0 &&
            Convert.ToInt32(x["InShop"]) == 0)
        .Sum(x => Convert.ToInt32(x["Count"]));

    var secondTradeItemCountBefore = store.GetCharacterAssets(
            account.Value,
            secondCharacterId)
        .Inventory
        .Where(x =>
            Convert.ToInt32(x["ItemIndex"]) ==
                generalShopItem.ItemIndex &&
            Convert.ToInt32(x["InBank"]) == 0 &&
            Convert.ToInt32(x["InShop"]) == 0)
        .Sum(x => Convert.ToInt32(x["Count"]));

    var firstTradeMoneyBefore = Convert.ToInt64(
        store.GetCharacter(account.Value, id)!["Money"]);
    var secondTradeMoneyBefore = Convert.ToInt64(
        store.GetCharacter(
            account.Value,
            secondCharacterId)!["Money"]);

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }
    while (peerRegistration.Queue.TryDequeue(out _)) { }

    using (var tradeRequest = Json(
        JsonSerializer.Serialize(new
        {
            Type = "TradeRequest",
            TargetEntityID = secondCharacterId
        })))
    {
        router.Handle(
            tradeRequest.RootElement,
            session);

        Check(
            peerRegistration.Queue.ToArray().Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"TradeInvite\"")),
            "TradeRequest sends invite to target player");
    }

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }

    using (var tradeReply = Json(
        JsonSerializer.Serialize(new
        {
            Type = "TradeReply",
            RequesterDBID = id,
            Accept = true
        })))
    {
        router.Handle(
            tradeReply.RootElement,
            peerSession);

        Check(
            broadcastRegistration.Queue.ToArray().Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"TradeStart\"")) &&
            peerRegistration.Queue.ToArray().Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"TradeStart\"")),
            "TradeReply starts one shared trade session for both players");
    }

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }
    while (peerRegistration.Queue.TryDequeue(out _)) { }

    using (var addTradeItem = Json(
        JsonSerializer.Serialize(new
        {
            Type = "TradeAddItem",
            TabType = 0,
            UID = itemUid,
            ItemIndex = generalShopItem.ItemIndex,
            Count = 2
        })))
    {
        router.Handle(
            addTradeItem.RootElement,
            session);
    }

    using (var setFirstMoney = Json(
        """
        {"Type":"TradeSetMoney","Money":100}
        """))
    {
        router.Handle(
            setFirstMoney.RootElement,
            session);
    }

    using (var setSecondMoney = Json(
        """
        {"Type":"TradeSetMoney","Money":50}
        """))
    {
        router.Handle(
            setSecondMoney.RootElement,
            peerSession);
    }

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }
    while (peerRegistration.Queue.TryDequeue(out _)) { }

    using (var firstLock = Json(
        """{"Type":"TradeLock"}"""))
    {
        router.Handle(
            firstLock.RootElement,
            session);
    }

    using (var secondLock = Json(
        """{"Type":"TradeLock"}"""))
    {
        router.Handle(
            secondLock.RootElement,
            peerSession);
    }

    Check(
        broadcastRegistration.Queue.ToArray().Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"TradeFinalConfirmReq\"")) &&
        peerRegistration.Queue.ToArray().Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"TradeFinalConfirmReq\"")),
        "both locked traders receive final confirmation request");

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }
    while (peerRegistration.Queue.TryDequeue(out _)) { }

    using (var firstConfirm = Json(
        """{"Type":"TradeFinalConfirm"}"""))
    {
        router.Handle(
            firstConfirm.RootElement,
            session);
    }

    using (var secondConfirm = Json(
        """{"Type":"TradeFinalConfirm"}"""))
    {
        router.Handle(
            secondConfirm.RootElement,
            peerSession);
    }

    var firstTradeItemCountAfter = store.GetCharacterAssets(
            account.Value,
            id)
        .Inventory
        .Where(x =>
            Convert.ToInt32(x["ItemIndex"]) ==
                generalShopItem.ItemIndex &&
            Convert.ToInt32(x["InBank"]) == 0 &&
            Convert.ToInt32(x["InShop"]) == 0)
        .Sum(x => Convert.ToInt32(x["Count"]));

    var secondTradeItemCountAfter = store.GetCharacterAssets(
            account.Value,
            secondCharacterId)
        .Inventory
        .Where(x =>
            Convert.ToInt32(x["ItemIndex"]) ==
                generalShopItem.ItemIndex &&
            Convert.ToInt32(x["InBank"]) == 0 &&
            Convert.ToInt32(x["InShop"]) == 0)
        .Sum(x => Convert.ToInt32(x["Count"]));

    Check(
        firstTradeItemCountAfter ==
            firstTradeItemCountBefore - 2 &&
        secondTradeItemCountAfter ==
            secondTradeItemCountBefore + 2 &&
        Convert.ToInt64(
            store.GetCharacter(
                account.Value,
                id)!["Money"]) ==
            firstTradeMoneyBefore - 50 &&
        Convert.ToInt64(
            store.GetCharacter(
                account.Value,
                secondCharacterId)!["Money"]) ==
            secondTradeMoneyBefore + 50 &&
        broadcastRegistration.Queue.ToArray().Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"TradeSuccess\"")) &&
        peerRegistration.Queue.ToArray().Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"TradeSuccess\"")) &&
        broadcastRegistration.Queue.ToArray().Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"InventorySync\"")) &&
        peerRegistration.Queue.ToArray().Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"InventorySync\"")),
        "two-phase trade atomically exchanges money and item stacks");

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }
    while (peerRegistration.Queue.TryDequeue(out _)) { }

    var shopItemUid = store.AddInventoryItem(
        account.Value,
        id,
        generalShopItem.ItemIndex,
        5);

    var sellerShopItemBefore = store.GetCharacterAssets(
            account.Value,
            id)
        .Inventory
        .Where(x =>
            Convert.ToInt32(x["ItemIndex"]) ==
                generalShopItem.ItemIndex)
        .Sum(x => Convert.ToInt32(x["Count"]));

    var buyerShopItemBefore = store.GetCharacterAssets(
            account.Value,
            secondCharacterId)
        .Inventory
        .Where(x =>
            Convert.ToInt32(x["ItemIndex"]) ==
                generalShopItem.ItemIndex)
        .Sum(x => Convert.ToInt32(x["Count"]));

    long reservedShopUid;

    using (var addShopItem = Json(
        JsonSerializer.Serialize(new
        {
            Type = "ShopAction",
            Action = "AddItem",
            UID = shopItemUid,
            Count = 4,
            Price = 10,
            TabType = 0
        })))
    {
        var responses = router.Handle(
            addShopItem.RootElement,
            session);

        var sync = responses.First(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"ShopSync\""));

        using var syncJson = Json(
            JsonSerializer.Serialize(sync));

        reservedShopUid = syncJson.RootElement
            .GetProperty("Items")[0]
            .GetProperty("UID")
            .GetInt64();

        var listed = store.GetCharacterAssets(
                account.Value,
                id)
            .Inventory
            .FirstOrDefault(x =>
                Convert.ToInt64(x["ItemUID"]) ==
                    reservedShopUid &&
                Convert.ToInt32(x["InShop"]) == 1);

        Check(
            reservedShopUid > 0 &&
            reservedShopUid != shopItemUid &&
            listed is not null &&
            Convert.ToInt32(listed["Count"]) == 4,
            "player shop partial listing reserves a server-side stack");
    }

    while (peerRegistration.Queue.TryDequeue(out _)) { }

    using (var openShop = Json(
        """
        {"Type":"ShopAction","Action":"OpenShop","ShopName":"測試商店"}
        """))
    {
        var responses = router.Handle(
            openShop.RootElement,
            session);

        Check(
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SyncShopBoard\"") &&
                JsonSerializer.Serialize(x).Contains(
                    "\"IsOpen\":true")) &&
            peerRegistration.Queue.ToArray().Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SyncShopBoard\"") &&
                JsonSerializer.Serialize(x).Contains(
                    "測試商店")),
            "opening player shop broadcasts the shop board to map peers");
    }

    using (var viewShop = Json(
        JsonSerializer.Serialize(new
        {
            Type = "ShopAction",
            Action = "ViewShop",
            TargetDBID = id
        })))
    {
        var responses = router.Handle(
            viewShop.RootElement,
            peerSession);

        Check(
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"ShopViewResponse\"") &&
                JsonSerializer.Serialize(x).Contains(
                    $"\"OwnerDBID\":{id}") &&
                JsonSerializer.Serialize(x).Contains(
                    $"\"UID\":{reservedShopUid}") &&
                JsonSerializer.Serialize(x).Contains(
                    "\"Count\":4")),
            "ViewShop returns shared server listings to another player");
    }

    var sellerShopMoneyBefore = Convert.ToInt64(
        store.GetCharacter(account.Value, id)!["Money"]);
    var buyerShopMoneyBefore = Convert.ToInt64(
        store.GetCharacter(
            account.Value,
            secondCharacterId)!["Money"]);

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }

    using (var buyShopItem = Json(
        JsonSerializer.Serialize(new
        {
            Type = "ShopAction",
            Action = "BuyItem",
            TargetDBID = id,
            UID = reservedShopUid,
            Count = 2
        })))
    {
        var responses = router.Handle(
            buyShopItem.RootElement,
            peerSession);

        var sellerAfter = store.GetCharacterAssets(
                account.Value,
                id)
            .Inventory
            .Where(x =>
                Convert.ToInt32(x["ItemIndex"]) ==
                    generalShopItem.ItemIndex)
            .Sum(x => Convert.ToInt32(x["Count"]));

        var buyerAfter = store.GetCharacterAssets(
                account.Value,
                secondCharacterId)
            .Inventory
            .Where(x =>
                Convert.ToInt32(x["ItemIndex"]) ==
                    generalShopItem.ItemIndex)
            .Sum(x => Convert.ToInt32(x["Count"]));

        var remainingListed = store.GetCharacterAssets(
                account.Value,
                id)
            .Inventory
            .FirstOrDefault(x =>
                Convert.ToInt64(x["ItemUID"]) ==
                    reservedShopUid);

        Check(
            sellerAfter == sellerShopItemBefore - 2 &&
            buyerAfter == buyerShopItemBefore + 2 &&
            Convert.ToInt64(
                store.GetCharacter(
                    account.Value,
                    id)!["Money"]) ==
                sellerShopMoneyBefore + 20 &&
            Convert.ToInt64(
                store.GetCharacter(
                    account.Value,
                    secondCharacterId)!["Money"]) ==
                buyerShopMoneyBefore - 20 &&
            remainingListed is not null &&
            Convert.ToInt32(
                remainingListed["Count"]) == 2 &&
            Convert.ToInt32(
                remainingListed["InShop"]) == 1 &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"InventorySync\"")) &&
            broadcastRegistration.Queue.ToArray().Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SyncMoney\"")) &&
            broadcastRegistration.Queue.ToArray().Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"ShopSync\"")),
            "BuyItem atomically transfers player-shop item and money");
    }

    using (var closeShop = Json(
        """{"Type":"ShopAction","Action":"CloseShop"}"""))
    {
        router.Handle(
            closeShop.RootElement,
            session);

        Check(
            store.GetCharacterAssets(
                    account.Value,
                    id)
                .Inventory
                .Where(x =>
                    Convert.ToInt32(x["ItemIndex"]) ==
                        generalShopItem.ItemIndex)
                .All(x =>
                    Convert.ToInt32(x["InShop"]) == 0) &&
            !playerShop.GetPublicState(id).IsOpen,
            "closing player shop releases all reserved assets");
    }

    Check(peerSession.Monsters.TryGetValue(
              monster.EntityId,
              out var peerMonster) &&
          ReferenceEquals(monster, peerMonster),
        "two sessions on one map reference the same MonsterRuntimeState");

    var sharedPositionBefore = monster.X;
    monster.X += 3;
    Check(Math.Abs(peerMonster!.X - monster.X) < 0.001 &&
          Math.Abs(monster.X - sharedPositionBefore - 3) < 0.001,
        "shared monster position changes are immediately visible to both sessions");

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }
    while (peerRegistration.Queue.TryDequeue(out _)) { }

    var sharedSpawnPackets = combat.SpawnLegacyEnemy(
        session,
        combatEnemy.EnemyId,
        monster.X + 100,
        monster.Y);

    Check(sharedSpawnPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"SpawnEntity\"")) &&
          peerRegistration.Queue.TryDequeue(out var peerSpawn) &&
          JsonSerializer.Serialize(peerSpawn).Contains(
              "\"Type\":\"SpawnEntity\"") &&
          broadcastRegistration.Queue.IsEmpty,
        "map world events broadcast to peers without echoing to source connection");

    combat.BuildCurrentMapSnapshot(peerSession);
    var dynamicMonster = session.Monsters.Values
        .First(x => x.EntityId >= 9_000_000);
    var peerDynamicMonster =
        peerSession.Monsters[dynamicMonster.EntityId];
    var dynamicHpBefore = dynamicMonster.CurrentHp;

    while (peerRegistration.Queue.TryDequeue(out _)) { }

    var sharedAttackPackets = combat.Attack(
        session,
        account.Value,
        dynamicMonster.EntityId,
        1);

    Check(
        dynamicMonster.CurrentHp < dynamicHpBefore &&
        peerDynamicMonster.CurrentHp ==
            dynamicMonster.CurrentHp &&
        sharedAttackPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"EntityDamage\"")) &&
        peerRegistration.Queue.ToArray().Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"EntityDamage\"")),
        "shared monster damage and HP are synchronized across map sessions");

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }
    router.DisconnectPresence(peerSession);

    Check(
        broadcastRegistration.Queue.TryDequeue(
            out var peerDespawn) &&
        JsonSerializer.Serialize(peerDespawn).Contains(
            "\"Type\":\"DespawnEntity\"") &&
        JsonSerializer.Serialize(peerDespawn).Contains(
            $"\"EntityID\":{secondCharacterId}"),
        "disconnecting player immediately despawns from map peers");

    broadcastHub.Unregister(peerRegistration.ConnectionId);

    var idleMonsters = session.Monsters.Values
        .Take(2)
        .ToArray();
    var wanderStartX = monster.X;
    var wanderStartY = monster.Y;
    var wanderScheduleAt = DateTimeOffset.UtcNow.AddSeconds(1);

    var initialWanderPackets = combat.TickAi(
        session,
        account.Value,
        monster.X + 2000,
        monster.Y + 2000,
        wanderScheduleAt);

    Check(!initialWanderPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"MoveEntity\"")) &&
          idleMonsters.All(x =>
              x.NextWanderAt > wanderScheduleAt),
        "idle monsters schedule their own first wander instead of moving in sync");

    if (idleMonsters.Length >= 2)
    {
        Check(idleMonsters[0].NextWanderAt !=
              idleMonsters[1].NextWanderAt,
            "different monsters receive staggered independent wander timers");
    }

    var wanderPackets = combat.TickAi(
        session,
        account.Value,
        monster.X + 2000,
        monster.Y + 2000,
        wanderScheduleAt.AddSeconds(4));

    var wanderDistanceFromCenter = Math.Sqrt(
        Math.Pow(monster.X - monster.WanderCenterX, 2) +
        Math.Pow(monster.Y - monster.WanderCenterY, 2));

    Check(wanderPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"MoveEntity\"")) &&
          (Math.Abs(monster.X - wanderStartX) > 0.001 ||
           Math.Abs(monster.Y - wanderStartY) > 0.001) &&
          wanderDistanceFromCenter <= 180.001,
        "idle monster wanders near its own center without client input");

    store.SetPetState(account.Value, id, petUid, 1);
    session.PetPositions[petUid] = (monster.X - 80, monster.Y);
    var hpBeforePetAttack = monster.CurrentHp;

    using (var petAttack = Json(JsonSerializer.Serialize(new
    {
        Type = "PetAction",
        Action = "CommandAttack",
        TargetEntityID = monster.EntityId
    })))
    {
        var responses = router.Handle(petAttack.RootElement, session);
        Check(monster.CurrentHp < hpBeforePetAttack &&
              monster.HasTarget &&
              session.PetTargets.TryGetValue(
                  petUid,
                  out var commandedTargetId) &&
              commandedTargetId == monster.EntityId &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"Type\":\"SyncState\"") &&
                  JsonSerializer.Serialize(x).Contains(
                      "\"State\":\"attack\"")) &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"Type\":\"EntityDamage\"")) &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"Type\":\"MonsterTarget\"") &&
                  JsonSerializer.Serialize(x).Contains(
                      "\"HasTarget\":true")),
            "CommandAttack pins the pet target and pulls monster aggro onto the owner");
    }

    // Pet AI should independently acquire and attack a nearby monster.
    monster.CurrentHp = monster.MaxHp;
    monster.RespawnAt = null;
    monster.HasTarget = false;
    monster.HoldingAttackRange = false;
    session.PetTargets.Clear();
    session.PetPositions[petUid] = (monster.X - 80, monster.Y);
    session.PetLastAttackAt[petUid] = DateTimeOffset.MinValue;
    session.LastPetFollowTick = DateTimeOffset.MinValue;
    var hpBeforeAutoPetAttack = monster.CurrentHp;
    var autoPetPackets = router.Tick(
        session,
        DateTimeOffset.UtcNow.AddSeconds(5));

    Check(monster.CurrentHp < hpBeforeAutoPetAttack &&
          monster.HasTarget &&
          session.PetTargets.TryGetValue(
              petUid,
              out var autoTargetId) &&
          autoTargetId == monster.EntityId &&
          autoPetPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"SyncState\"") &&
              JsonSerializer.Serialize(x).Contains(
                  $"\"EntityID\":{4_000_000L + petUid}") &&
              JsonSerializer.Serialize(x).Contains(
                  "\"State\":\"attack\"")) &&
          autoPetPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"EntityDamage\"")),
        "summoned pet automatically acquires, sticks to and attacks nearby monster");

    // Monster aggro must consider the summoned pet even when the pet has not
    // attacked first. Put the pet closer than the player and verify the
    // monster chooses and damages the pet directly.
    monster.CurrentHp = monster.MaxHp;
    monster.RespawnAt = null;
    monster.HasTarget = false;
    monster.TargetCharacterId = 0;
    monster.TargetPetUid = 0;
    monster.AiState = MonsterAiState.Idle;
    monster.LastAttackAt = DateTimeOffset.MinValue;

    session.PetTargets.Clear();
    session.PetLastAttackAt[petUid] = DateTimeOffset.UtcNow;
    session.PetPositions[petUid] =
        (monster.X + 60, monster.Y);

    var petBeforeMonsterAggro = store.GetCharacterAssets(
            account.Value,
            id)
        .Pets
        .First(x => Convert.ToInt64(x["UID"]) == petUid);
    var petHpBeforeMonsterAggro =
        Convert.ToInt32(petBeforeMonsterAggro["HP"]);

    var monsterPetAggroPackets = combat.TickAi(
        session,
        account.Value,
        monster.X + 300,
        monster.Y,
        session.LastMonsterAiTick.AddSeconds(1));

    var petAfterMonsterAggro = store.GetCharacterAssets(
            account.Value,
            id)
        .Pets
        .First(x => Convert.ToInt64(x["UID"]) == petUid);

    Check(
        monster.HasTarget &&
        monster.TargetCharacterId == id &&
        monster.TargetPetUid == petUid &&
        Convert.ToInt32(petAfterMonsterAggro["HP"]) <
            petHpBeforeMonsterAggro &&
        monsterPetAggroPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"EntityDamage\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"TargetType\":4") &&
            JsonSerializer.Serialize(x).Contains(
                $"\"TargetID\":{4_000_000L + petUid}")),
        "monster proactively aggros and attacks a closer summoned pet without waiting for pet attack");

    monster.HasTarget = false;
    monster.TargetCharacterId = 0;
    monster.TargetPetUid = 0;

    // Owner leash uses a grace period. Falling far behind should not
    // immediately teleport the pet; only sustained off-screen distance does.
    session.LastX = monster.X - 1200;
    session.LastY = monster.Y;
    session.PetPositions[petUid] =
        (monster.X - 20, monster.Y);
    session.PetLastAttackAt[petUid] =
        DateTimeOffset.MinValue;
    session.LastPetFollowTick =
        DateTimeOffset.MinValue;

    var leashStart = DateTimeOffset.UtcNow.AddSeconds(6);
    var gracePackets = router.Tick(
        session,
        leashStart);

    Check(
        session.PetOutOfRangeSince.ContainsKey(petUid) &&
        !gracePackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"SpeedTime\":0.01")),
        "pet beyond 900px starts owner-leash grace instead of teleporting immediately");

    session.LastPetFollowTick =
        DateTimeOffset.MinValue;

    var leashPackets = router.Tick(
        session,
        leashStart.AddSeconds(3.1));

    var expectedPetX = session.LastX + 42;
    var expectedPetY = session.LastY + 24;
    var leashedPetPosition =
        session.PetPositions[petUid];

    Check(
        Math.Abs(leashedPetPosition.X - expectedPetX) < 0.001 &&
        Math.Abs(leashedPetPosition.Y - expectedPetY) < 0.001 &&
        leashPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"MoveEntity\"") &&
            JsonSerializer.Serialize(x).Contains(
                $"\"EntityID\":{4_000_000L + petUid}") &&
            JsonSerializer.Serialize(x).Contains(
                "\"SpeedTime\":0.01")) &&
        !session.PetOutOfRangeSince.ContainsKey(petUid) &&
        !leashPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"EntityDamage\"") &&
            JsonSerializer.Serialize(x).Contains(
                $"\"AttackerID\":{4_000_000L + petUid}")),
        "pet teleports back only after remaining beyond 900px for three seconds");

    Check(!session.PetTargets.ContainsKey(petUid),
        "hard owner return clears the pet combat target");

    // A monster may retain a target whose websocket presence is still on the
    // map even though that character is already far outside aggro range. It
    // must release that stale owner and acquire the nearby player in the same
    // tick instead of standing beside the nearby player indefinitely.
    store.Revive(account.Value, secondCharacterId);
    store.UpdateLocation(
        account.Value,
        secondCharacterId,
        combatMapId,
        Convert.ToInt32(monster.X + 2000),
        Convert.ToInt32(monster.Y));

    monster.HasTarget = true;
    monster.TargetCharacterId = secondCharacterId;
    monster.AiState = MonsterAiState.Attack;
    monster.LastAttackAt = DateTimeOffset.MinValue;

    var staleTargetPackets = combat.TickAi(
        session,
        account.Value,
        monster.X + 80,
        monster.Y,
        session.LastMonsterAiTick.AddSeconds(1));

    Check(
        monster.HasTarget &&
        monster.TargetCharacterId == id &&
        monster.AiState == MonsterAiState.Attack &&
        staleTargetPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"HasTarget\":false")) &&
        staleTargetPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                $"\"TargetID\":{id}")) &&
        staleTargetPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"EntityDamage\"") &&
            JsonSerializer.Serialize(x).Contains(
                $"\"AttackerID\":{monster.EntityId}")),
        "monster releases stale foreign target and attacks nearby player in same tick");

    monster.HasTarget = false;
    monster.TargetCharacterId = 0;
    monster.HoldingAttackRange = false;

    monster.HasTarget = false;
    monster.HoldingAttackRange = false;

    var aiPackets = combat.TickAi(
        session,
        account.Value,
        monster.X + 200,
        monster.Y,
        session.LastMonsterAiTick.AddSeconds(1));

    Check(monster.HasTarget &&
          monster.AiState == MonsterAiState.Chase &&
          aiPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"MonsterTarget\"")) &&
          aiPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"MoveEntity\"") &&
              JsonSerializer.Serialize(x).Contains(
                  "\"SpeedTime\":0.25")),
        "monster enters aggro and emits a chase waypoint aligned with the 250ms world tick");

    var chaseStartX = monster.X;
    var chaseStartY = monster.Y;
    var chaseTick2 = combat.TickAi(
        session,
        account.Value,
        monster.X + 260,
        monster.Y + 20,
        session.LastMonsterAiTick.AddMilliseconds(249));

    var chaseMoved = Math.Sqrt(
        Math.Pow(monster.X - chaseStartX, 2) +
        Math.Pow(monster.Y - chaseStartY, 2));

    Check(chaseMoved > 0.001 &&
          chaseTick2.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"MoveEntity\"")),
        "monster AI does not double-throttle a near-250ms world tick and keeps advancing");

    monster.IsMoving = true;
    monster.AiState = MonsterAiState.Chase;
    monster.LastAttackAt = session.LastMonsterAiTick.AddMilliseconds(900);
    var closeRangePackets = combat.TickAi(
        session,
        account.Value,
        monster.X + 90,
        monster.Y,
        session.LastMonsterAiTick.AddSeconds(1));

    Check(!monster.IsMoving &&
          monster.AiState == MonsterAiState.Attack &&
          closeRangePackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"SyncState\"") &&
              JsonSerializer.Serialize(x).Contains(
                  "\"State\":\"attack\"")) &&
          closeRangePackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"EntityDamage\"")) &&
          !closeRangePackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"MoveEntity\"") &&
              JsonSerializer.Serialize(x).Contains(
                  $"\"EntityID\":{monster.EntityId}")),
        "monster entering melee range attacks immediately instead of standing through an old cooldown");

    // Attack facing must use the exact target direction rather than movement
    // hysteresis. At about 26 degrees, movement may intentionally keep dir 6,
    // while an attack must face the adjacent sector (dir 7).
    monster.HasTarget = true;
    monster.TargetCharacterId = id;
    monster.AiState = MonsterAiState.Attack;
    monster.CurrentDir = "6";
    monster.LastAttackAt = DateTimeOffset.MinValue;

    var attackAngleRadians = 26.0 * Math.PI / 180.0;
    var attackDirectionPackets = combat.TickAi(
        session,
        account.Value,
        monster.X + Math.Cos(attackAngleRadians) * 80.0,
        monster.Y + Math.Sin(attackAngleRadians) * 80.0,
        session.LastMonsterAiTick.AddMilliseconds(125));

    Check(
        monster.CurrentDir == "7" &&
        attackDirectionPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"SyncState\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"State\":\"attack\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"Dir\":\"7\"")),
        "monster attack direction faces the current target instead of retaining movement hysteresis");

    // Regression: vector math can produce 90.0000000000001 for a target that
    // is logically exactly at the 90px engage boundary. That must not enter
    // Chase with an effectively zero movement step.
    monster.HasTarget = true;
    monster.TargetCharacterId = id;
    monster.IsMoving = true;
    monster.AiState = MonsterAiState.Chase;
    monster.LastAttackAt = session.LastMonsterAiTick;
    var floatingBoundaryPackets = combat.TickAi(
        session,
        account.Value,
        monster.X + 90.0000000000001,
        monster.Y,
        session.LastMonsterAiTick.AddMilliseconds(250));

    Check(
        monster.AiState == MonsterAiState.Attack &&
        !monster.IsMoving &&
        !floatingBoundaryPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"MoveEntity\"") &&
            JsonSerializer.Serialize(x).Contains(
                $"\"EntityID\":{monster.EntityId}")),
        "monster treats floating-point noise at the 90px engage boundary as in range instead of zero-step chasing");

    monster.HasTarget = true;
    monster.AiState = MonsterAiState.Chase;
    var boundaryStartX = monster.X;
    var boundaryPackets = combat.TickAi(
        session,
        account.Value,
        monster.X + 120,
        monster.Y,
        session.LastMonsterAiTick.AddMilliseconds(500));

    Check(
        monster.AiState == MonsterAiState.Chase &&
        monster.X > boundaryStartX &&
        boundaryPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"MoveEntity\"")) &&
        !boundaryPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"EntityDamage\"")),
        "monster at 120px keeps moving closer instead of standing at the attack boundary");

    monster.HasTarget = true;
    monster.AiState = MonsterAiState.Attack;
    monster.LastAttackAt = DateTimeOffset.MinValue;
    var hpBeforeFarAttack =
        Convert.ToInt32(store.GetCharacter(account.Value, id)!["HP"]);

    var farAttackPackets = combat.TickAi(
        session,
        account.Value,
        monster.X + 155,
        monster.Y,
        session.LastMonsterAiTick.AddSeconds(2));

    var hpAfterFarAttack =
        Convert.ToInt32(store.GetCharacter(account.Value, id)!["HP"]);

    Check(
        hpAfterFarAttack == hpBeforeFarAttack &&
        monster.AiState == MonsterAiState.Chase &&
        farAttackPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"MoveEntity\"") &&
            JsonSerializer.Serialize(x).Contains(
                $"\"EntityID\":{monster.EntityId}")) &&
        !farAttackPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"EntityDamage\"") &&
            JsonSerializer.Serialize(x).Contains(
                $"\"AttackerID\":{monster.EntityId}")),
        "monster beyond legal melee range keeps chasing and cannot deal melee damage");

    var invalidAttackPackets = combat.Attack(
        session,
        account.Value,
        99_999_999,
        1);
    Check(invalidAttackPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "攻擊目標不存在")),
        "invalid AttackEntity receives explicit response instead of NO_RESPONSE");

    var sawDeath = false;
    IReadOnlyList<object> deathPackets = Array.Empty<object>();

    var rewardBeforeKill = store.GetCharacter(
        account.Value,
        id)!;
    var rewardLevelBeforeKill =
        Convert.ToInt32(rewardBeforeKill["Level"]);
    var nextLevelThreshold =
        growth.ExperienceThresholdForLevel(
            rewardLevelBeforeKill);
    var accumulatedBeforeKill =
        Convert.ToInt32(rewardBeforeKill["ExpAcc"]);

    if (accumulatedBeforeKill < nextLevelThreshold)
    {
        store.AddRewards(
            account.Value,
            id,
            Math.Max(
                0,
                nextLevelThreshold -
                accumulatedBeforeKill -
                1),
            0);

        rewardBeforeKill = store.GetCharacter(
            account.Value,
            id)!;
    }

    var activePetBeforePlayerKill = store.GetCharacterAssets(
            account.Value,
            id)
        .Pets
        .First(x => Convert.ToInt64(x["UID"]) == petUid);
    var petExpBeforePlayerKill =
        Convert.ToInt32(activePetBeforePlayerKill["Exp"]);
    var petLevelBeforePlayerKill =
        Convert.ToInt32(activePetBeforePlayerKill["Level"]);

    for (var i = 0; i < 100 && !sawDeath; i++)
    {
        var attackPackets = combat.Attack(
            session,
            account.Value,
            monster.EntityId,
            1);

        Check(attackPackets.Count >= 1,
            "AttackEntity returns combat packet");

        sawDeath = attackPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"EntityDeath\""));

        if (sawDeath)
            deathPackets = attackPackets;
    }

    var rewardAfterKill = store.GetCharacter(account.Value, id)!;

    Check(sawDeath &&
          monster.CurrentHp == 0 &&
          monster.RespawnAt.HasValue,
        "monster reaches death state and schedules respawn");

    Check(
        Convert.ToInt32(rewardAfterKill["ExpAcc"]) >
            Convert.ToInt32(rewardBeforeKill["ExpAcc"]) &&
        Convert.ToInt64(rewardAfterKill["Money"]) >
            Convert.ToInt64(rewardBeforeKill["Money"]),
        "monster kill persists character EXP and money rewards");

    var activePetAfterPlayerKill = store.GetCharacterAssets(
            account.Value,
            id)
        .Pets
        .First(x => Convert.ToInt64(x["UID"]) == petUid);

    Check(
        Convert.ToInt32(activePetAfterPlayerKill["Exp"]) >
            petExpBeforePlayerKill ||
        Convert.ToInt32(activePetAfterPlayerKill["Level"]) >
            petLevelBeforePlayerKill,
        "player monster kill also grants EXP to the currently summoned pet");

    Check(
        deathPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"PetSync\"")),
        "player monster kill syncs summoned pet EXP to the client");

    Check(
        Convert.ToInt32(rewardAfterKill["MaxExp"]) ==
            growth.ExperienceThresholdForLevel(
                Convert.ToInt32(rewardAfterKill["Level"])) &&
        deathPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"PlayerStatusSync\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"ExpAcc\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"MaxExp\"")) &&
        deathPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"SyncMoney\"")),
        "monster kill syncs EXP threshold and money to the existing client protocol");

    Check(
        Convert.ToInt32(rewardAfterKill["Level"]) >
            rewardLevelBeforeKill &&
        deathPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"PlayEffect\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"EffectName\":\"LevelUp\"")),
        "character level-up emits the existing client PlayEffect animation packet");

    monster.RespawnAt = DateTimeOffset.UtcNow.AddMilliseconds(-1);
    var respawnPackets = combat.TickRespawns(
        session,
        DateTimeOffset.UtcNow);

    Check(monster.CurrentHp == monster.MaxHp &&
          monster.RespawnAt is null &&
          monster.AiState == MonsterAiState.Idle &&
          respawnPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"SpawnEntity\"")),
        "dead monster respawns with full HP");

    // Monster -> player damage/death.
    var playerDeathSeen = false;
    var aiNow = session.LastMonsterAiTick.AddSeconds(2);

    var monsterAttackAnimationSeen = false;

    for (var i = 0; i < 100 && !playerDeathSeen; i++)
    {
        aiNow = aiNow.AddSeconds(2);
        var monsterAttackPackets = combat.TickAi(
            session,
            account.Value,
            monster.X + 20,
            monster.Y,
            aiNow);

        monsterAttackAnimationSeen |= monsterAttackPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"SyncState\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"State\":\"attack\"") &&
            JsonSerializer.Serialize(x).Contains(
                $"\"EntityID\":{monster.EntityId}"));

        var attackMovePackets = monsterAttackPackets
            .Where(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"MoveEntity\"") &&
                JsonSerializer.Serialize(x).Contains(
                    $"\"EntityID\":{monster.EntityId}"))
            .ToArray();

        Check(attackMovePackets.All(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"SpeedTime\":0")),
            "monster attack transition only emits authoritative snap movement");

        playerDeathSeen = monsterAttackPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"EntityDeath\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"EntityType\":0"));
    }

    selected = store.GetCharacter(account.Value, id);
    Check(monsterAttackAnimationSeen,
        "monster attack emits SyncState attack for existing client animation");

    Check(playerDeathSeen &&
          Convert.ToInt32(selected!["HP"]) == 0 &&
          selected["State"].ToString() == "death",
        "monster attacks can kill player and persist death state");

    monster.HasTarget = true;
    monster.NextWanderAt = DateTimeOffset.MinValue;
    var deathPositionX = monster.X;
    var deathPositionY = monster.Y;
    var deadPlayerTickAt = session.LastMonsterAiTick.AddSeconds(2);

    var deadPlayerAiPackets = combat.TickAi(
        session,
        account.Value,
        monster.X + 20,
        monster.Y,
        deadPlayerTickAt);

    Check(!monster.HasTarget &&
          deadPlayerAiPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"MonsterTarget\"") &&
              JsonSerializer.Serialize(x).Contains(
                  "\"HasTarget\":false")) &&
          !deadPlayerAiPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"EntityDamage\"")),
        "dead player immediately drops monster aggro and cannot be attacked");

    Check(Math.Abs(monster.X - deathPositionX) < 0.001 &&
          Math.Abs(monster.Y - deathPositionY) < 0.001 &&
          Math.Abs(monster.WanderCenterX - deathPositionX) < 0.001 &&
          Math.Abs(monster.WanderCenterY - deathPositionY) < 0.001 &&
          !deadPlayerAiPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"MoveEntity\"") &&
              JsonSerializer.Serialize(x).Contains(
                  $"\"EntityID\":{monster.EntityId}")),
        "player death preserves target monster position and re-anchors idle wandering there");

    var deadPlayerWanderPackets = combat.TickAi(
        session,
        account.Value,
        monster.X + 20,
        monster.Y,
        deadPlayerTickAt.AddSeconds(4));

    var deadWanderDistance = Math.Sqrt(
        Math.Pow(monster.X - deathPositionX, 2) +
        Math.Pow(monster.Y - deathPositionY, 2));

    Check(deadPlayerWanderPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"MoveEntity\"") &&
              JsonSerializer.Serialize(x).Contains(
                  $"\"EntityID\":{monster.EntityId}")) &&
          deadWanderDistance > 60.0 &&
          deadWanderDistance <= 180.001 &&
          monster.AiState == MonsterAiState.Wander,
        "monster resumes longer local random wandering after player death without returning to spawn");

    using (var revive = Json(
        """{"Type":"ReviveRequest"}"""))
    {
        var responses = router.Handle(revive.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"PlayerStatusSync\"")) &&
              responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"ChangeMap\"")),
            "ReviveRequest restores status and changes to reborn map");
        Check(session.CurrentMapId == 15 &&
              session.PendingMapSpawn,
            "revive returns session to persisted Map15 reborn point");
    }

    selected = store.GetCharacter(account.Value, id);
    Check(Convert.ToInt32(selected!["HP"]) ==
              Convert.ToInt32(selected["MaxHP"]) &&
          Convert.ToInt32(selected["MapID"]) == 15 &&
          selected["State"].ToString() == "idle",
        "revive persists full HP idle state and reborn map");

    var levelBeforeGm = Convert.ToInt32(
        store.GetCharacter(account.Value, id)!["Level"]);
    var pointsBeforeGm = Convert.ToInt32(
        store.GetCharacter(account.Value, id)!["ExpUse"]);

    using (var gmLevelPoints = Json(
        """{"Type":"ChatMessage","Channel":0,"Message":"/gm level 80","BubbleType":0}"""))
    {
        var responses = router.Handle(gmLevelPoints.RootElement, session);
        var afterLevel = store.GetCharacter(account.Value, id)!;
        Check(Convert.ToInt32(afterLevel["Level"]) == 80 &&
              Convert.ToInt32(afterLevel["ExpUse"]) ==
                  pointsBeforeGm + Math.Max(0, 80 - levelBeforeGm) * 2 &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"ExpUse\"")),
            "GM level grants and syncs two ability points per gained level from legacy ExpUse.csv");
    }

    using (var allocate = Json(
        """{"Type":"AllocateStatRequest","StatType":"STR"}"""))
    {
        var before = store.GetCharacter(account.Value, id)!;
        var beforeStr = Convert.ToInt32(before["Str"]);
        var beforePoints = Convert.ToInt32(before["ExpUse"]);
        var responses = router.Handle(allocate.RootElement, session);
        var after = store.GetCharacter(account.Value, id)!;

        Check(Convert.ToInt32(after["Str"]) == beforeStr + 1 &&
              Convert.ToInt32(after["ExpUse"]) == beforePoints - 1 &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"Type\":\"AllocateStatResponse\"") &&
                  JsonSerializer.Serialize(x).Contains(
                      "\"Success\":true")),
            "AllocateStatRequest spends an ability point server-side");
    }


    var beforeMaxStats = store.GetCharacter(account.Value, id)!;
    using (var maxStats = Json(
        """{"Type":"ChatMessage","Channel":0,"Message":"/gm maxstats","BubbleType":0}"""))
    {
        var responses = router.Handle(maxStats.RootElement, session);
        var after = store.GetCharacter(account.Value, id)!;

        Check(Convert.ToInt32(after["Str"]) == 100 &&
              Convert.ToInt32(after["Dex"]) == 100 &&
              Convert.ToInt32(after["Con"]) == 100 &&
              Convert.ToInt32(after["Wis"]) == 100 &&
              Convert.ToInt32(after["ExpUse"]) == 0,
            "GM maxstats sets all four primary stats to client maximum");

        Check(Convert.ToInt32(after["MaxHP"]) >
                  Convert.ToInt32(beforeMaxStats["MaxHP"]) &&
              Convert.ToInt32(after["MaxMP"]) >
                  Convert.ToInt32(beforeMaxStats["MaxMP"]) &&
              Convert.ToInt32(after["Attack"]) >
                  Convert.ToInt32(beforeMaxStats["Attack"]) &&
              Convert.ToInt32(after["MagicAttack"]) >
                  Convert.ToInt32(beforeMaxStats["MagicAttack"]) &&
              Convert.ToInt32(after["Defense"]) >
                  Convert.ToInt32(beforeMaxStats["Defense"]) &&
              Convert.ToInt32(after["Dodge"]) >
                  Convert.ToInt32(beforeMaxStats["Dodge"]) &&
              Convert.ToInt32(after["MoveSpeed"]) >
                  Convert.ToInt32(beforeMaxStats["MoveSpeed"]),
            "legacy primary-stat tables increase HP MP ATK MATK DEF dodge and move speed");

        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"PlayerStatusSync\"")),
            "GM maxstats refreshes effective status");
    }

    using (var attackSpeed = Json(
        """{"Type":"ChatMessage","Channel":0,"Message":"/gm attackspeed 300","BubbleType":0}"""))
    {
        var responses = router.Handle(attackSpeed.RootElement, session);
        Check(Convert.ToInt32(
                  store.GetCharacter(account.Value, id)!["AttackSpeed"]) == 300,
            "GM attackspeed persists base attack speed");
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"AttackSpeed\":300")),
            "GM attackspeed immediately syncs effective attack speed");
    }

    store.SetSkill(account.Value, id, 45111, 1);
    store.Revive(account.Value, id);
    session.CurrentMapId = combatMapId;
    session.PendingMapSpawn = false;
    combat.EnterMap(session, combatMapId);
    var moonTarget = session.Monsters.Values.First(x => x.IsAlive);
    store.UpdateLocation(
        account.Value,
        id,
        combatMapId,
        Convert.ToInt32(moonTarget.X),
        Convert.ToInt32(moonTarget.Y));
    session.LastX = moonTarget.X;
    session.LastY = moonTarget.Y;

    using (var moonJudgment = Json(JsonSerializer.Serialize(new
    {
        Type = "UseSkill",
        SkillID = 45111,
        TargetID = moonTarget.EntityId,
        TargetType = 1,
        TargetX = (int)moonTarget.X,
        TargetY = (int)moonTarget.Y
    })))
    {
        var before = moonTarget.CurrentHp;
        var responses = router.Handle(moonJudgment.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SkillCast\"") &&
                JsonSerializer.Serialize(x).Contains(
                    "\"SkillID\":45111")) &&
              responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"EntityDamage\"")) &&
              moonTarget.CurrentHp < before &&
              !responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "此技能類型尚未支援")),
            "月神審判 skill_type=7 casts and deals composite damage");
    }

        // Reproduce real map-transition timing: CurrentMapId changes first,
    // while combat runtime is still loaded for the previous map. UseSkill
    // must load the destination monster runtime before validating TargetID.
    var transitionMapId = world.GetEnemies(8).Any(x => x.Level < 65535)
        ? 8
        : combatMapId;
    var transitionEnemy = world.GetEnemies(transitionMapId)
        .First(x => x.Level < 65535);

    store.UpdateLocation(
        account.Value,
        id,
        transitionMapId,
        transitionEnemy.X,
        transitionEnemy.Y);
    session.CurrentMapId = transitionMapId;
    session.LastX = transitionEnemy.X;
    session.LastY = transitionEnemy.Y;
    session.PendingMapSpawn = true;

    // Derive the expected entity ID from the destination world data exactly
    // as EnterMap will populate it, but do not call Tick/EnterMap first.
    var expectedTransitionTarget = transitionEnemy.EntityId;

    store.SetSkill(account.Value, id, 26104, 5);
    store.Revive(account.Value, id);

    using (var transitionSkill = Json(JsonSerializer.Serialize(new
    {
        Type = "UseSkill",
        SkillID = 26104,
        TargetID = expectedTransitionTarget,
        TargetType = 1,
        TargetX = transitionEnemy.X,
        TargetY = transitionEnemy.Y
    })))
    {
        var responses = router.Handle(
            transitionSkill.RootElement,
            session);

        Check(!session.PendingMapSpawn &&
              session.LoadedMonsterMapId == transitionMapId &&
              session.Monsters.ContainsKey(expectedTransitionTarget),
            "UseSkill loads destination monster runtime immediately after map change");

        Check(!responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "技能目標無效")),
            "魔影術 does not report invalid target after map transition");
    }

    store.SetSkill(account.Value, id, 26104, 5);
    store.Revive(account.Value, id);
    session.CurrentMapId = combatMapId;
    session.PendingMapSpawn = false;
    combat.EnterMap(session, combatMapId);
    var skillTarget = session.Monsters.Values.First(x => x.IsAlive);
    store.UpdateLocation(
        account.Value,
        id,
        combatMapId,
        Convert.ToInt32(skillTarget.X),
        Convert.ToInt32(skillTarget.Y));
    session.LastX = skillTarget.X;
    session.LastY = skillTarget.Y;
    var mpBeforeSkill = Convert.ToInt32(
        store.GetCharacter(account.Value, id)!["MP"]);
    if (mpBeforeSkill < 126)
    {
        // Revive fills only up to MaxMP in the lightweight test schema.
        // Raising MaxMP is outside this test, so validate insufficient-MP
        // behavior first, then use a direct DB-backed character with enough
        // MP through the normal recovery path is covered by production data.
        using var skillNoMp = Json(JsonSerializer.Serialize(new
        {
            Type = "UseSkill",
            SkillID = 26104,
            TargetID = skillTarget.EntityId,
            TargetType = 1,
            TargetX = (int)skillTarget.X,
            TargetY = (int)skillTarget.Y
        }));
        var noMpResponses = router.Handle(skillNoMp.RootElement, session);
        Check(noMpResponses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "HP / MP / SP 不足")),
            "UseSkill returns explicit resource error instead of NO_RESPONSE");
    }

    using (var gmLevel = Json(
        """{"Type":"ChatMessage","Channel":0,"Message":"/gm level 25","BubbleType":0}"""))
    {
        var responses = router.Handle(gmLevel.RootElement, session);
        Check(Convert.ToInt32(
                  store.GetCharacter(account.Value, id)!["Level"]) == 25,
            "GM level command persists current character level");
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"PlayerStatusSync\"")),
            "GM level command refreshes player status");
    }

    session.CurrentMapId = 16;
    session.LastX = 10000;
    session.LastY = 10000;

    var spawnableEnemy = world.GetEnemies(16)
        .FirstOrDefault(x => x.Level < 65535);

    if (spawnableEnemy is not null)
    {
        using var gmSpawn = Json(JsonSerializer.Serialize(new
        {
            Type = "ChatMessage",
            Channel = 0,
            Message = $"/gm spawn {spawnableEnemy.EnemyId} 10120 10000",
            BubbleType = 0
        }));

        var beforeSpawnCount = session.Monsters.Count;
        var responses = router.Handle(gmSpawn.RootElement, session);

        Check(session.Monsters.Count == beforeSpawnCount + 1 &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"Type\":\"SpawnEntity\"")),
            "GM spawn command creates a real map monster runtime entity");
    }

    using (var gmSkill = Json(
        """{"Type":"ChatMessage","Channel":0,"Message":"/gm skill 20101 5","BubbleType":0}"""))
    {
        var responses = router.Handle(gmSkill.RootElement, session);
        Check(store.GetSkills(account.Value, id).Any(x =>
                Convert.ToInt32(x["skill_id"]) == 20101 &&
                Convert.ToInt32(x["level"]) == 5),
            "GM skill command persists learned skill");
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SkillSync\"")),
            "GM skill command refreshes SkillSync");
    }

    using (var gmItem = Json(
        """{"Type":"ChatMessage","Channel":0,"Message":"/gm item 1001 3","BubbleType":0}"""))
    {
        var before = store.GetCharacterAssets(account.Value, id).Inventory.Count;
        var responses = router.Handle(gmItem.RootElement, session);
        var after = store.GetCharacterAssets(account.Value, id).Inventory;
        Check(after.Count >= before + 1 &&
              after.Any(x =>
                  Convert.ToInt32(x["ItemIndex"]) == 1001 &&
                  Convert.ToInt32(x["Count"]) == 3),
            "GM item command creates inventory item");
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"InventorySync\"")),
            "GM item command refreshes inventory");
    }

    using (var gmMap = Json(
        """{"Type":"ChatMessage","Channel":0,"Message":"/gm map 15 9408 10336","BubbleType":0}"""))
    {
        var responses = router.Handle(gmMap.RootElement, session);
        Check(session.CurrentMapId == 15 &&
              session.PendingMapSpawn &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"Type\":\"ChangeMap\"")),
            "GM map command teleports and schedules world respawn");
    }

    using (var chat = Json(
        """{"Type":"ChatMessage","Channel":0,"Message":"hello","BubbleType":0}"""))
    {
        var responses = router.Handle(chat.RootElement, session);
        Check(responses.Count == 1 &&
              JsonSerializer.Serialize(responses[0]).Contains("ChatBroadcast"),
            "local chat echoes as ChatBroadcast");
    }

    // NPC selling must use current client item metadata even when the
    // legacy shop catalog does not contain the player's item.
    var sellUid = store.AddInventoryItem(
        account.Value,
        id,
        1001,
        1);

    using (var sellCurrentItem = Json(JsonSerializer.Serialize(new
    {
        Type = "ShopAction",
        Action = "SellNpcItem",
        UID = sellUid,
        TabType = 0,
        Count = 1
    })))
    {
        var responses = router.Handle(
            sellCurrentItem.RootElement,
            session);

        Check(
            !responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "此物品無法出售")) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "出售成功")),
            "NPC selling uses current client can_sell metadata instead of legacy catalog");
    }

    // Drop/pickup round-trip for an unequipped item.
    var dropEquipUid = store.AddEquipment(
        account.Value,
        id,
        18538,
        255,
        7,
        2);

    session.LastX = 8000;
    session.LastY = 11200;
    session.CurrentMapId = 15;

    using (var dropItem = Json(JsonSerializer.Serialize(new
    {
        Type = "DropItemRequest",
        TabType = 1,
        ItemUID = dropEquipUid,
        Count = 1,
        DropX = 8020,
        DropY = 11220
    })))
    {
        var responses = router.Handle(
            dropItem.RootElement,
            session);

        Check(
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"EntityType\":3")) &&
            !store.GetCharacterAssets(account.Value, id).Equips.Any(x =>
                Convert.ToInt64(x["ItemID"]) == dropEquipUid) &&
            session.GroundItems.Count > 0,
            "DropItemRequest removes equipment and spawns a ground item");
    }

    var droppedEntityId = session.GroundItems.Keys.Last();

    // Simulate a completely new login/session. Ground items must be restored
    // from SQLite rather than relying on the old ClientSession dictionary.
    var reloggedSession = new ClientSession
    {
        AccountId = account.Value,
        CharacterId = id,
        CurrentMapId = 15,
        LastX = 8000,
        LastY = 11200,
        PendingMapSpawn = true
    };

    var relogPackets = router.Tick(
        reloggedSession,
        DateTimeOffset.UtcNow.AddSeconds(30));

    Check(
        reloggedSession.GroundItems.ContainsKey(droppedEntityId) &&
        relogPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"EntityType\":3") &&
            JsonSerializer.Serialize(x).Contains(
                $"\"EntityID\":{droppedEntityId}")),
        "dropped ground item is restored from DB after relog");

    using (var pickupItem = Json(JsonSerializer.Serialize(new
    {
        Type = "PickUpItemRequest",
        EntityID = droppedEntityId
    })))
    {
        var responses = router.Handle(
            pickupItem.RootElement,
            reloggedSession);

        Check(
            !reloggedSession.GroundItems.ContainsKey(droppedEntityId) &&
            !store.GetGroundItems(15).Any(x =>
                x.EntityId == droppedEntityId) &&
            store.GetCharacterAssets(account.Value, id).Equips.Any(x =>
                Convert.ToInt32(x["ItemIdx"]) == 18538 &&
                Convert.ToInt32(x["Damage"]) == 7 &&
                Convert.ToInt32(x["Color"]) == 2) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"DespawnEntity\"")) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"ItemPickupFeedback\"") &&
                JsonSerializer.Serialize(x).Contains(
                    "淺藍色初心褲子")),
            "PickUpItemRequest restores persisted equipment, removes ground row and emits pickup feedback");
    }

    var returnPeerRegistration = broadcastHub.Register();
    var returnPeerSession = new ClientSession
    {
        ConnectionId = returnPeerRegistration.ConnectionId,
        AccountId = account.Value,
        CharacterId = secondCharacterId,
        CurrentMapId = 15,
        LastX = 9408,
        LastY = 10336
    };

    store.UpdateLocation(
        account.Value,
        id,
        15,
        9408,
        10336);
    store.UpdateLocation(
        account.Value,
        secondCharacterId,
        15,
        9448,
        10336);
    store.SetRebornLocation(
        account.Value,
        id,
        16,
        9000,
        9000);
    store.SetRebornLocation(
        account.Value,
        secondCharacterId,
        16,
        9040,
        9000);

    session.CurrentMapId = 15;
    session.LastX = 9408;
    session.LastY = 10336;
    router.SyncPresence(session);
    router.SyncPresence(returnPeerSession);

    party.Handle(
        session,
        account.Value,
        "Invite",
        secondCharacterId,
        DateTimeOffset.UtcNow);
    party.Handle(
        returnPeerSession,
        account.Value,
        "Accept",
        id,
        DateTimeOffset.UtcNow.AddSeconds(1));

    store.SetSkill(
        account.Value,
        id,
        27110,
        1);
    store.Revive(account.Value, id);

    while (returnPeerRegistration.Queue.TryDequeue(out _)) { }

    var returnPackets = combat.UseSkill(
        session,
        account.Value,
        27110,
        0,
        0,
        9408,
        10336,
        DateTimeOffset.UtcNow.AddSeconds(2));

    Check(
        session.CurrentMapId == 16 &&
        Convert.ToInt32(
            store.GetCharacter(
                account.Value,
                id)!["MapID"]) == 16 &&
        Convert.ToInt32(
            store.GetCharacter(
                account.Value,
                secondCharacterId)!["MapID"]) == 16 &&
        returnPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"ChangeMap\"")) &&
        returnPeerRegistration.Queue.ToArray().Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"ChangeMap\"")),
        "平安之祝 sends online same-map party members to their reborn locations");

    router.Tick(
        returnPeerSession,
        DateTimeOffset.UtcNow.AddSeconds(3));

    Check(
        returnPeerSession.CurrentMapId == 16 &&
        returnPeerSession.PendingMapSpawn == false,
        "peer world tick reconciles server-initiated party map travel");

    party.Handle(
        returnPeerSession,
        account.Value,
        "Leave",
        -1,
        DateTimeOffset.UtcNow.AddSeconds(4));
    router.DisconnectPresence(returnPeerSession);
    broadcastHub.Unregister(
        returnPeerRegistration.ConnectionId);

    var trainTicketMapping =
        questItemMappings.ResolveItem(10505);
    Check(trainTicketMapping is
          {
              TabType: 0
          },
        "legacy magic-line train ticket maps to a current inventory item");

    var ticketNpc = world.GetNpcs(47)
        .First(x =>
            x.Script.Equals(
                "NQ024_A.txt",
                StringComparison.OrdinalIgnoreCase));
    var conductorNpc = world.GetNpcs(47)
        .First(x =>
            x.Script.Equals(
                "NQ024_B.txt",
                StringComparison.OrdinalIgnoreCase));

    store.UpdateLocation(
        account.Value,
        id,
        47,
        ticketNpc.X,
        ticketNpc.Y);
    session.CurrentMapId = 47;
    session.LastX = ticketNpc.X;
    session.LastY = ticketNpc.Y;
    session.PendingMapSpawn = false;
    router.SyncPresence(session);

    store.AddRewards(
        account.Value,
        id,
        0,
        1_000);

    var trainMoneyBefore = Convert.ToInt64(
        store.GetCharacter(
            account.Value,
            id)!["Money"]);
    var trainTicketCountBefore =
        trainTicketMapping is null
            ? 0
            : store.GetCharacterAssets(
                    account.Value,
                    id)
                .Inventory
                .Where(x =>
                    Convert.ToInt32(
                        x["ItemIndex"]) ==
                    trainTicketMapping.ItemId)
                .Sum(x =>
                    Convert.ToInt32(x["Count"]));

    using (var buyTrainTicket = Json(
        JsonSerializer.Serialize(new
        {
            Type = "NpcOptionRequest",
            EntityID = ticketNpc.EntityId,
            ScriptName = ticketNpc.Script,
            TargetNode = "BuySingleReply#0"
        })))
    {
        var responses = router.Handle(
            buyTrainTicket.RootElement,
            session);

        var trainMoneyAfter = Convert.ToInt64(
            store.GetCharacter(
                account.Value,
                id)!["Money"]);
        var trainTicketCountAfter =
            store.GetCharacterAssets(
                    account.Value,
                    id)
                .Inventory
                .Where(x =>
                    Convert.ToInt32(
                        x["ItemIndex"]) ==
                    trainTicketMapping!.ItemId)
                .Sum(x =>
                    Convert.ToInt32(x["Count"]));

        Check(
            trainMoneyAfter ==
                trainMoneyBefore - 300 &&
            trainTicketCountAfter ==
                trainTicketCountBefore + 1 &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SyncMoney\"")) &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"InventorySync\"")),
            "legacy train ticket seller uses ModifyData(Money) and grants the mapped ticket");
    }

    store.SetQuestFlag(
        account.Value,
        id,
        "NQ024_DEBUG",
        1);

    using (var conductorStart = Json(
        JsonSerializer.Serialize(new
        {
            Type = "InteractRequest",
            EntityID = conductorNpc.EntityId
        })))
    {
        store.UpdateLocation(
            account.Value,
            id,
            47,
            conductorNpc.X,
            conductorNpc.Y);
        session.LastX = conductorNpc.X;
        session.LastY = conductorNpc.Y;

        var responses = router.Handle(
            conductorStart.RootElement,
            session);

        Check(
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"NpcDialogResponse\"")) &&
            !responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "NPC 腳本執行失敗")),
            "legacy train conductor schedule executes floor() and opens boarding choices");
    }

    store.SetQuestFlag(
        account.Value,
        id,
        "NQ024_Type",
        0);

    var ticketCountBeforeVideo =
        store.GetCharacterAssets(
                account.Value,
                id)
            .Inventory
            .Where(x =>
                Convert.ToInt32(
                    x["ItemIndex"]) ==
                trainTicketMapping!.ItemId)
            .Sum(x =>
                Convert.ToInt32(x["Count"]));

    IReadOnlyList<object> trainStartPackets;
    using (var trainDeparture = Json(
        JsonSerializer.Serialize(new
        {
            Type = "NpcOptionRequest",
            EntityID = conductorNpc.EntityId,
            ScriptName = conductorNpc.Script,
            TargetNode = "dia_04a1"
        })))
    {
        trainStartPackets = router.Handle(
            trainDeparture.RootElement,
            session);
    }

    var travelId =
        session.PendingTrainTravelId;

    Check(
        !string.IsNullOrWhiteSpace(travelId) &&
        trainStartPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"TrainTravelStart\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"SourceVideo\":\"train_depart_king.ogv\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"DestinationVideo\":\"train_depart_magic.ogv\"")) &&
        store.GetCharacterAssets(
                account.Value,
                id)
            .Inventory
            .Where(x =>
                Convert.ToInt32(
                    x["ItemIndex"]) ==
                trainTicketMapping!.ItemId)
            .Sum(x =>
                Convert.ToInt32(x["Count"])) ==
            ticketCountBeforeVideo,
        "train departure starts client video without consuming the ticket before Ready");

    IReadOnlyList<object> trainReadyPackets;
    using (var trainReady = Json(
        JsonSerializer.Serialize(new
        {
            Type = "TrainTravelReady",
            TravelID = travelId
        })))
    {
        trainReadyPackets = router.Handle(
            trainReady.RootElement,
            session);
    }

    var afterTrainCharacter = store.GetCharacter(
        account.Value,
        id)!;
    var ticketCountAfterTrain =
        store.GetCharacterAssets(
                account.Value,
                id)
            .Inventory
            .Where(x =>
                Convert.ToInt32(
                    x["ItemIndex"]) ==
                trainTicketMapping!.ItemId)
            .Sum(x =>
                Convert.ToInt32(x["Count"]));

    Check(
        string.IsNullOrEmpty(
            session.PendingTrainTravelId) &&
        Convert.ToInt32(
            afterTrainCharacter["MapID"]) == 47 &&
        Math.Abs(
            Convert.ToDouble(
                afterTrainCharacter["LocX"]) -
            5079.0) < 0.1 &&
        Math.Abs(
            Convert.ToDouble(
                afterTrainCharacter["LocY"]) -
            2887.0) < 0.1 &&
        ticketCountAfterTrain ==
            ticketCountBeforeVideo - 1 &&
        trainReadyPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"ChangeMap\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"X\":5079") &&
            JsonSerializer.Serialize(x).Contains(
                "\"Y\":2887")),
        "TrainTravelReady consumes the legacy ticket and resumes Lua movement to the magic-city station");

    using (var duplicateTrainReady = Json(
        JsonSerializer.Serialize(new
        {
            Type = "TrainTravelReady",
            TravelID = travelId
        })))
    {
        var responses = router.Handle(
            duplicateTrainReady.RootElement,
            session);

        Check(
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "列車行程已失效")) &&
            store.GetCharacterAssets(
                    account.Value,
                    id)
                .Inventory
                .Where(x =>
                    Convert.ToInt32(
                        x["ItemIndex"]) ==
                    trainTicketMapping!.ItemId)
                .Sum(x =>
                    Convert.ToInt32(x["Count"])) ==
                ticketCountAfterTrain,
            "replayed TrainTravelReady is rejected without consuming another ticket");
    }

    var runtimePetUid = store.AddPet(
        account.Value,
        id,
        45001,
        "RuntimePet");
    var runtimeHorseUid = store.AddHorse(
        account.Value,
        id,
        50001,
        "RuntimeMecha",
        "MS",
        1);

    using (var db = new SqliteConnection(
        $"Data Source={dbPath}"))
    {
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE pets
               SET feed_value=1,
                   state=0
             WHERE uid=$pet
            """;
        cmd.Parameters.AddWithValue(
            "$pet",
            runtimePetUid);
        cmd.ExecuteNonQuery();
    }

    Check(
        store.SetPetState(
            account.Value,
            id,
            runtimePetUid,
            1) &&
        store.SetSummonedHorse(
            account.Value,
            id,
            runtimeHorseUid),
        "prepare one-second pet hunger and mecha energy runtime");

    session.PetPositions[runtimePetUid] = (
        session.LastX + 40,
        session.LastY + 20);

    var runtimeTickNow =
        DateTimeOffset.UtcNow;
    session.LastCompanionRuntimeAt =
        runtimeTickNow.AddSeconds(-2);
    session.LastCompanionClientSyncAt =
        runtimeTickNow;

    var runtimeDrainPackets = router.Tick(
        session,
        runtimeTickNow);

    var runtimeAssets = store.GetCharacterAssets(
        account.Value,
        id);
    var runtimePet = runtimeAssets.Pets.Single(x =>
        Convert.ToInt64(x["UID"]) ==
            runtimePetUid);
    var runtimeHorse = runtimeAssets.Horses.Single(x =>
        Convert.ToInt64(x["HorseUID"]) ==
            runtimeHorseUid);
    var runtimeCharacter = store.GetCharacter(
        account.Value,
        id)!;
    var runtimeJson = JsonSerializer.Serialize(
        runtimeDrainPackets);

    Check(
        Convert.ToInt32(runtimePet["FeedValue"]) == 0 &&
        Convert.ToInt32(runtimePet["State"]) == 0 &&
        Convert.ToInt32(runtimeHorse["EnergyLeft"]) == 0 &&
        Convert.ToInt64(
            runtimeCharacter["SummonedHorseUID"]) == -1 &&
        !session.PetPositions.ContainsKey(runtimePetUid) &&
        runtimeJson.Contains(
            "\"Type\":\"DriveMechaResponse\"") &&
        runtimeJson.Contains(
            "\"HorseUID\":-1") &&
        runtimeJson.Contains(
            "\"Type\":\"SyncMechaEnergy\"") &&
        runtimeJson.Contains(
            "\"Type\":\"PetSync\"") &&
        runtimeJson.Contains(
            $"\"EntityID\":{4_000_000L + runtimePetUid}"),
        "server runtime drains one unit per second and immediately dismounts/recalls depleted companions");

    using (var db = new SqliteConnection(
        $"Data Source={dbPath}"))
    {
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET exp_death=1000
             WHERE id=$id
               AND account_id=$account
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$account", account.Value);
        Check(cmd.ExecuteNonQuery() == 1,
            "prepare 1000 death EXP for innocence-book test");
    }

    var innocenceBookUid = store.AddInventoryItem(
        account.Value,
        id,
        2459,
        1);

    using (var useInnocenceBook = Json(
        JsonSerializer.Serialize(new
        {
            Type = "UseItemRequest",
            ItemUID = innocenceBookUid
        })))
    {
        var responses = router.Handle(
            useInnocenceBook.RootElement,
            session);
        var characterAfterBook = store.GetCharacter(
            account.Value,
            id)!;
        var responseJson = JsonSerializer.Serialize(
            responses);

        Check(
            Convert.ToInt32(
                characterAfterBook["ExpDeath"]) == 100 &&
            !store.GetCharacterAssets(
                account.Value,
                id).Inventory.Any(x =>
                Convert.ToInt64(x["ItemUID"]) ==
                    innocenceBookUid) &&
            responseJson.Contains(
                "\"Type\":\"PlayerStatusSync\"") &&
            responseJson.Contains("\"ExpDeath\":100") &&
            responseJson.Contains(
                "死亡經驗值已減少 90%"),
            "無罪之書 atomically leaves 10% death EXP, consumes one book and syncs ExpDeath");
    }

    var expCardUid = store.AddInventoryItem(
        account.Value,
        id,
        2460,
        2);

    using (var useExpCard = Json(
        JsonSerializer.Serialize(new
        {
            Type = "UseItemRequest",
            ItemUID = expCardUid
        })))
    {
        var responses = router.Handle(
            useExpCard.RootElement,
            session);

        var remainingCards =
            store.GetCharacterAssets(
                    account.Value,
                    id)
                .Inventory
                .Where(x =>
                    Convert.ToInt64(x["ItemUID"]) ==
                        expCardUid)
                .Sum(x =>
                    Convert.ToInt32(x["Count"]));

        Check(
            remainingCards == 1 &&
            store.GetTimedEffectValue(
                account.Value,
                id,
                "exp_multiplier_percent",
                DateTimeOffset.UtcNow
                    .ToUnixTimeMilliseconds()) == 200 &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "雙倍經驗值已啟動 1 小時")),
            "UseItemRequest consumes one 1h EXP card and persists a 200% timed effect");
    }

    using (var reuseExpCard = Json(
        JsonSerializer.Serialize(new
        {
            Type = "UseItemRequest",
            ItemUID = expCardUid
        })))
    {
        var responses = router.Handle(
            reuseExpCard.RootElement,
            session);

        var remainingCards =
            store.GetCharacterAssets(
                    account.Value,
                    id)
                .Inventory
                .Where(x =>
                    Convert.ToInt64(x["ItemUID"]) ==
                        expCardUid)
                .Sum(x =>
                    Convert.ToInt32(x["Count"]));

        Check(
            remainingCards == 1 &&
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "相同效果仍在持續中")),
            "active double-EXP effect rejects reuse without consuming another card");
    }

    Check(
        store.GetTimedEffectValue(
            account.Value,
            id,
            "exp_multiplier_percent",
            DateTimeOffset.UtcNow
                .AddHours(2)
                .ToUnixTimeMilliseconds()) == 0,
        "expired double-EXP timed effect is removed from persistence");

    while (broadcastRegistration.Queue.TryDequeue(out _)) { }

    var duplicateRegistration = broadcastHub.Register();
    var duplicateSession = new ClientSession
    {
        ConnectionId = duplicateRegistration.ConnectionId
    };

    using (var duplicateLogin = Json(
        """{"Type":"LoginRequest","User":"offline","Pass":"test-password"}"""))
    {
        var responses = router.Handle(
            duplicateLogin.RootElement,
            duplicateSession);

        Check(
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"LoginResponse\"") &&
                JsonSerializer.Serialize(x).Contains(
                    "\"Success\":true")) &&
            broadcastRegistration.Queue.ToArray().Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"ForceLogout\"")),
            "new login supersedes old account session and queues ForceLogout");
    }

    while (duplicateRegistration.Queue.TryDequeue(out _)) { }

    using (var reclaimLogin = Json(
        """{"Type":"LoginRequest","User":"offline","Pass":"test-password"}"""))
    {
        var responses = router.Handle(
            reclaimLogin.RootElement,
            session);

        Check(
            responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Success\":true")) &&
            duplicateRegistration.Queue.ToArray().Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"ForceLogout\"")),
            "original connection can re-login and force out the superseding session");
    }

    broadcastHub.Unregister(
        duplicateRegistration.ConnectionId);

    // Map ticks are driven by one scheduler, not the per-connection router.
    // Test the production session lifecycle without running a background loop.
    var mapAi = new SharedMapAiTicker(
        combat,
        worldRuntime,
        store,
        broadcastHub,
        accountSessions,
        NullLogger<SharedMapAiTicker>.Instance);
    var originalCharacter = session.CharacterId;
    var originalMap = session.CurrentMapId;
    var originalReady = session.WorldReady;
    session.CharacterId = id;
    session.CurrentMapId = combatMapId; // active map with real monsters
    session.WorldReady = false;
    mapAi.Register(session);
    Check(session.SharedAiManaged &&
          mapAi.TickOnce(DateTimeOffset.UtcNow) == 0,
        "shared map scheduler ignores maps without world-ready players");
    session.WorldReady = true;
    var sharedTickTime = DateTimeOffset.UtcNow.AddSeconds(1);
    Check(mapAi.TickOnce(sharedTickTime) == 1 &&
          session.LastMonsterAiTick == sharedTickTime,
        "shared map scheduler ticks an active map once");
    var socketTickTime = sharedTickTime.AddMilliseconds(250);
    router.Tick(session, socketTickTime);
    Check(session.LastMonsterAiTick == sharedTickTime,
        "per-socket tick does not run managed monster AI again");
    mapAi.Unregister(session);
    Check(!session.SharedAiManaged &&
          mapAi.TickOnce(sharedTickTime.AddSeconds(1)) == 0,
        "shared map scheduler stops maps after last player disconnects");
    session.CharacterId = originalCharacter;
    session.CurrentMapId = originalMap;
    session.WorldReady = originalReady;

    using (var unsupportedPacket = Json(
        """{"Type":"PacketContractUnknownOperation"}"""))
    {
        var errorResponses = router.Handle(
            unsupportedPacket.RootElement,
            session);
        var responseJson = JsonSerializer.Serialize(errorResponses);
        Check(
            responseJson.Contains("\"Type\":\"PacketError\"") &&
            responseJson.Contains("\"RequestType\":\"PacketContractUnknownOperation\"") &&
            responseJson.Contains("\"Code\":\"UNHANDLED_PACKET\""),
            "unsupported client packet receives a typed PacketError instead of silence");
    }

    using (var logout = Json(
        """{"Type":"LogoutRequest","IsBackToLogin":true}"""))
    {
        var responses = router.Handle(logout.RootElement, session);
        Check(responses.Count == 1 && session.CloseRequested,
            "logout requests websocket close after response");
        Check(session.AccountId is null && session.CharacterId is null,
            "logout clears authenticated session");
    }

    using (var unauthorizedPacket = Json(
        """{"Type":"UseSkill","SkillID":123}"""))
    {
        var unauthorizedResponses = router.Handle(
            unauthorizedPacket.RootElement,
            session);
        var unauthorizedJson = JsonSerializer.Serialize(
            unauthorizedResponses);
        Check(
            unauthorizedJson.Contains("\"Type\":\"PacketError\"") &&
            unauthorizedJson.Contains("\"Code\":\"AUTH_REQUIRED\"") &&
            unauthorizedJson.Contains("\"RequestType\":\"UseSkill\""),
            "unauthenticated packet receives an explicit error");
    }

    Check(!store.Delete(account.Value, id, "wrong"),
        "reject wrong character password");
    Check(store.Delete(account.Value, id, "1234"),
        "delete first character");

    slots = store.Characters(account.Value);
    Check(slots.Count == 3 &&
          slots.Count(x => Convert.ToInt64(x["DBID"]) > 0) == 1 &&
          slots.Any(x => x.TryGetValue("NAME", out var n) &&
                         n?.ToString() == "SecondCharacter"),
        "second character remains after deleting first");

    var secondId = Convert.ToInt64(
        slots.First(x =>
            x.TryGetValue("NAME", out var n) &&
            n?.ToString() == "SecondCharacter")["DBID"]);

    Check(store.Delete(account.Value, secondId, "5678"),
        "delete second character");

    slots = store.Characters(account.Value);
    Check(slots.Count == 3 &&
          slots.All(x => Convert.ToInt64(x["DBID"]) == 0),
        "three empty slots after deleting all characters");
}
finally
{
    try { Directory.Delete(temp, true); } catch { }
}

Console.WriteLine("All smoke tests passed.");
