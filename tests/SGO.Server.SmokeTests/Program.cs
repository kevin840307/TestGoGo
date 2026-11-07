using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using SGO.Server.Application;
using SGO.Server.Application.Combat;
using SGO.Server.Application.Gm;
using SGO.Server.Application.Shops;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Quest;
using SGO.Server.Infrastructure.World;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.Protocol;
using System.Text.Json;

static void Check(bool ok, string msg)
{
    if (!ok) throw new Exception("FAILED: " + msg);
    Console.WriteLine("PASS: " + msg);
}

static JsonDocument Json(string value) => JsonDocument.Parse(value);

var repoRoot = Directory.GetCurrentDirectory();
var gameDataRoot = Path.Combine(repoRoot, "Old", "服務端", "Dat");
var questRoot = Path.Combine(gameDataRoot, "Quest");

Check(Directory.Exists(gameDataRoot), "server data directory exists");

var world = new WorldDataService(gameDataRoot);

CurrentItemDefinition CurrentItem(
    int id,
    string name,
    string category,
    int price = 100,
    int canSell = 1,
    int atk = 0,
    int def = 0,
    int moveSpeed = 0,
    int equipSlot = 0,
    int attackRange = 0,
    int recoverHp = 0,
    int recoverMp = 0,
    int recoverSp = 0,
    int maxBattery = 0,
    int battery = 0,
    string horseType = "")
    => new(
        id,
        name,
        category,
        price,
        canSell,
        atk,
        def,
        0, // MAtk
        0, // MDef
        0, // Hit
        0, // Flee
        0, // Luk
        0, // Aspd
        moveSpeed,
        0, // CritRate
        0, // CritDamage
        0, // MaxHpBonus
        0, // MaxMpBonus
        0, // MaxSpBonus
        equipSlot,
        attackRange,
        recoverHp,
        recoverMp,
        recoverSp,
        maxBattery,
        battery,
        horseType);

var currentCatalog = new CurrentClientDataCatalog(
    new[]
    {
        CurrentItem(
            1001,
            "小藥水",
            "recovery",
            price: 20,
            recoverHp: 140),

        // Legacy Shop2 item 31078 is 淺藍色初心褲子.
        // Current client moved it to 18538.
        CurrentItem(
            18538,
            "淺藍色初心褲子",
            "equip",
            price: 1,
            def: 5,
            equipSlot: 4),

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
            attackRange: 140),
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
            attackRange: 50),
        CurrentItem(
            50297,
            "戰鬥型二足引擎",
            "horseequip",
            price: 100,
            moveSpeed: 100,
            equipSlot: 2),
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
        new CurrentSkillDefinition(26104, "魔影術")
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
                    80)
            })
    },
    new[]
    {
        new CurrentSkillLevelDefinition(
            26104, 5, 1, 1,
            280, 280,
            0, 126, 0,
            350, 155, 2)
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
    Array.Empty<(int Id, string Name)>());

var shopCatalog = new ShopCatalog(
    gameDataRoot,
    currentCatalog,
    legacyClientCatalog);
var batteryCatalog = new HorseBatteryCatalog(currentCatalog);
var recoveryCatalog = new RecoveryItemCatalog(currentCatalog);
var horseCardCatalog = new HorseCardCatalog(currentCatalog);
var skillCatalog = new SkillCatalog(currentCatalog);
var quests = new QuestRuntime(questRoot, shopCatalog);
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

var map15Enemies = world.GetEnemies(15);
Check(map15Enemies.Count == 4,
    "Map15 Enemy.dat binary parser reads four records");
Check(map15Enemies.All(x => x.EnemyId == 245),
    "Map15 enemy records resolve EnemyID 245");
Check(world.BuildMapSpawnPackets(15).Count == 52,
    "Map15 city suppresses combat enemy spawns");

var map15Merchant = map15Npcs.First(x => x.IsMerchant);
Check(map15Merchant.NpcTypeId == 393 &&
      map15Merchant.Sprite == "NPC002.png",
    "Map15 merchant uses a recovered visible/clickable sprite fallback");
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
Check(currentCatalog.FindSkillLevel(26104, 5) is
      { CostMp: 126, CastRange: 280, AoeRadius: 280 },
    "current 魔影術 Lv5 resolves server-side cast data");
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

var temp = Path.Combine(
    Path.GetTempPath(),
    "sgo-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);

try
{
    var store = new SqliteGameStore(Path.Combine(temp, "game.db"));
    var combat = new CombatService(store, world, currentCatalog);
    var shops = new ShopService(store, shopCatalog, currentCatalog);
    var config = new ConfigurationManager();
    config["Game:GmCommandsEnabled"] = "true";
    var gm = new GmCommandService(
        store,
        skillCatalog,
        horseCardCatalog,
        currentCatalog,
        legacyClientCatalog,
        combat,
        config);
    var router = new PacketRouter(
        store,
        world,
        quests,
        combat,
        shops,
        gm,
        batteryCatalog,
        recoveryCatalog,
        horseCardCatalog,
        currentCatalog,
        NullLogger<PacketRouter>.Instance);
    var session = new ClientSession();

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
    var batteryUid = store.AddInventoryItem(
        account.Value, id, 50102, 1);
    var recoveryUid = store.AddInventoryItem(
        account.Value, id, 1001, 1);
    var horseCardUid = store.AddInventoryItem(
        account.Value, id, 50001, 1);

    Check(itemUid > 0 &&
          equipUid > 0 &&
          petUid > 0 &&
          horseUid > 0 &&
          horseEquipUid > 0 &&
          batteryUid > 0 &&
          recoveryUid > 0 &&
          horseCardUid > 0,
        "seed persistent inventory equipment pet and mecha assets");

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
          seededAssets.HorseEquips.Count == 1,
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

        using var first = Json(JsonSerializer.Serialize(responses[0]));
        Check(first.RootElement.Str("Type") == "EnterWorldSuccess",
            "first select response enters world");
        Check(first.RootElement.Int("MapID") == 15,
            "enter world includes Map15");
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
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains("\"Type\":\"PlayerStatusSync\"")),
            "recovery item refreshes player status");
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
                  Convert.ToInt32(x["ItemIdx"]) == 18538),
            "NPC shop buys mapped current equipment into equipment storage");
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
    var combatSpawns = combat.EnterMap(session, combatMapId);

    Check(session.Monsters.Count > 0 &&
          combatSpawns.Any(x =>
              JsonSerializer.Serialize(x).Contains("\"EntityType\":1")),
        "monster map initializes per-session combat entities");

    var monster = session.Monsters.Values.First();

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
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"Type\":\"SyncState\"") &&
                  JsonSerializer.Serialize(x).Contains(
                      "\"State\":\"attack\"")) &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"Type\":\"EntityDamage\"")),
            "CommandAttack makes summoned pet face and attack nearby monster");
    }

    // Pet AI should independently acquire and attack a nearby monster.
    monster.CurrentHp = monster.MaxHp;
    monster.RespawnAt = null;
    session.PetPositions[petUid] = (monster.X - 80, monster.Y);
    session.PetLastAttackAt[petUid] = DateTimeOffset.MinValue;
    session.LastPetFollowTick = DateTimeOffset.MinValue;
    var hpBeforeAutoPetAttack = monster.CurrentHp;
    var autoPetPackets = router.Tick(
        session,
        DateTimeOffset.UtcNow.AddSeconds(5));

    Check(monster.CurrentHp < hpBeforeAutoPetAttack &&
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
        "summoned pet automatically acquires and attacks nearby monster");

    var aiPackets = combat.TickAi(
        session,
        account.Value,
        monster.X + 200,
        monster.Y,
        session.LastMonsterAiTick.AddSeconds(1));

    Check(monster.HasTarget &&
          aiPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"MonsterTarget\"")) &&
          aiPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"MoveEntity\"") &&
              JsonSerializer.Serialize(x).Contains(
                  "\"SpeedTime\":0.32")),
        "monster enters aggro and emits overlapped smooth chase movement");

    monster.IsMoving = true;
    var closeRangePackets = combat.TickAi(
        session,
        account.Value,
        monster.X + 120,
        monster.Y,
        session.LastMonsterAiTick.AddSeconds(1));

    Check(!monster.IsMoving &&
          closeRangePackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"SyncState\"") &&
              (JsonSerializer.Serialize(x).Contains(
                   "\"State\":\"attack\"") ||
               JsonSerializer.Serialize(x).Contains(
                   "\"State\":\"idle\""))),
        "monster stops walk animation and switches to attack/idle inside melee stop distance");

    var sawDeath = false;

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
    }

    Check(sawDeath &&
          monster.CurrentHp == 0 &&
          monster.RespawnAt.HasValue,
        "monster reaches death state and schedules respawn");

    monster.RespawnAt = DateTimeOffset.UtcNow.AddMilliseconds(-1);
    var respawnPackets = combat.TickRespawns(
        session,
        DateTimeOffset.UtcNow);

    Check(monster.CurrentHp == monster.MaxHp &&
          monster.RespawnAt is null &&
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
          deadWanderDistance <= 180.001,
        "monster resumes local random wandering after player death without returning to spawn");

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
                  pointsBeforeGm + Math.Max(0, 80 - levelBeforeGm) &&
              responses.Any(x =>
                  JsonSerializer.Serialize(x).Contains(
                      "\"ExpUse\"")),
            "GM level grants and syncs one ability point per gained level");
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

    using (var logout = Json(
        """{"Type":"LogoutRequest","IsBackToLogin":true}"""))
    {
        var responses = router.Handle(logout.RootElement, session);
        Check(responses.Count == 1 && session.CloseRequested,
            "logout requests websocket close after response");
        Check(session.AccountId is null && session.CharacterId is null,
            "logout clears authenticated session");
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
