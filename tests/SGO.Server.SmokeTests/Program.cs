using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using SGO.Server.Application;
using SGO.Server.Application.Bank;
using SGO.Server.Application.Packets;
using SGO.Server.Application.Combat;
using SGO.Server.Application.Character;
using SGO.Server.Application.Gm;
using SGO.Server.Application.Items;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.Shops;
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
    int reqLevel = 0)
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
        reqLevel);

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
                    80)
            })
    },
    new[]
    {
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
    Array.Empty<(int Id, string Name)>());

var shopCatalog = new ShopCatalog(
    gameDataRoot,
    currentCatalog,
    legacyClientCatalog);
var enemyDrops = new EnemyDropCatalog(
    gameDataRoot,
    currentCatalog);
var batteryCatalog = new HorseBatteryCatalog(currentCatalog);
var recoveryCatalog = new RecoveryItemCatalog(currentCatalog);
var horseCardCatalog = new HorseCardCatalog(currentCatalog);
var skillCatalog = new SkillCatalog(currentCatalog);
var quests = new QuestRuntime(
    questRoot,
    shopCatalog,
    enemyDrops);
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
        """);

    var runtimeFlag = 0;
    var runtimeItemCount = 0;
    var runtimeMappedItemId = 0;
    var runtimeMappedTab = -1;

    var questApiRuntime = new QuestRuntime(
        questApiRoot,
        shopCatalog,
        enemyDrops);

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
    var store = new SqliteGameStore(Path.Combine(temp, "game.db"));
    var growth = new LegacyPlayerGrowthCatalog(gameDataRoot);
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

    var combat = new CombatService(
        store,
        world,
        currentCatalog,
        growth,
        battle,
        enemyDrops);
    var shops = new ShopService(
        store,
        store,
        shopCatalog,
        currentCatalog);
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
    var recovery = new ResourceRecoveryService(store, config);
    config["Gameplay:Dolls:Happiness:AnnouncementRewardIds"] = "50102";
    config["Gameplay:Dolls:Happiness:AnnouncementMinimumPrice"] = "0";
    var broadcastHub = new ServerBroadcastHub();
    var broadcastRegistration = broadcastHub.Register();
    var dolls = new DollService(
        store,
        store,
        currentCatalog,
        broadcastHub,
        config);
    var router = new PacketRouter(
        store,
        world,
        quests,
        combat,
        recovery,
        shops,
        packetHandlers,
        gm,
        dolls,
        batteryCatalog,
        recoveryCatalog,
        horseCardCatalog,
        currentCatalog,
        growth,
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

    var secondCharacterId = Convert.ToInt64(
        slots.First(x =>
            x.TryGetValue("NAME", out var n) &&
            n?.ToString() == "SecondCharacter")["DBID"]);

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

        using var first = Json(JsonSerializer.Serialize(responses[0]));
        Check(first.RootElement.Str("Type") == "EnterWorldSuccess",
            "first select response enters world");
        Check(first.RootElement.Int("MapID") == 15,
            "enter world includes Map15");
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
    Check(Convert.ToInt32(afterRecovery["HP"]) > beforeRecoveryHp &&
          Convert.ToInt32(afterRecovery["MP"]) >= beforeRecoveryMp &&
          recoveryPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"PlayerStatusSync\"")),
        "resource recovery restores HP/MP and syncs status after interval");

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

    var sawDeath = false;
    IReadOnlyList<object> deathPackets = Array.Empty<object>();
    var rewardBeforeKill = store.GetCharacter(account.Value, id)!;

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

        Check(!monsterAttackPackets.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"MoveEntity\"") &&
                JsonSerializer.Serialize(x).Contains(
                    $"\"EntityID\":{monster.EntityId}")),
            "monster attack tick never emits movement for the attacking monster");

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
          deadWanderDistance > 45.0 &&
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
                    "\"Type\":\"DespawnEntity\"")),
            "PickUpItemRequest restores persisted equipment and removes ground row");
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
