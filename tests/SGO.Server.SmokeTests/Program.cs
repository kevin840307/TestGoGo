using Microsoft.Extensions.Logging.Abstractions;
using SGO.Server.Application;
using SGO.Server.Application.Combat;
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
var shopCatalog = new ShopCatalog(gameDataRoot);
var batteryCatalog = new HorseBatteryCatalog(
    Path.Combine(gameDataRoot, "Horse", "HorseBattery.csv"));
var recoveryCatalog = new RecoveryItemCatalog(
    Path.Combine(gameDataRoot, "Item", "Recovery.itd"));
var horseCardCatalog = new HorseCardCatalog(
    Path.Combine(gameDataRoot, "Horse", "HorseMA.csv"));
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
Check(shopCatalog.GetShopItems(1).Count > 0 &&
      shopCatalog.GetShopItems(2).Count > 0 &&
      shopCatalog.GetShopItems(5).Count > 0 &&
      shopCatalog.GetShopItems(6).Count > 0 &&
      shopCatalog.GetShopItems(7).Count > 0,
    "Map15 item weapon armor and shield shops load source item data");
Check(batteryCatalog.GetEnergy(6905) == 3600,
    "source mecha battery 6905 resolves 3600 seconds");
Check(recoveryCatalog.Find(31201) is { RecoverHp: 20 },
    "source recovery item 31201 resolves +20 HP");
Check(horseCardCatalog.Find(6008) is { MaxBattery: 25200 },
    "source mecha card 6008 resolves max battery");

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
    var combat = new CombatService(store, world);
    var shops = new ShopService(store, shopCatalog);
    var router = new PacketRouter(
        store,
        world,
        quests,
        combat,
        shops,
        batteryCatalog,
        recoveryCatalog,
        horseCardCatalog,
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

    var generalShopItem = shopCatalog.GetShopItems(1).First();
    var weaponShopItem = shopCatalog.GetShopItems(2).First();

    var itemUid = store.AddInventoryItem(
        account.Value, id, generalShopItem.ItemIndex, 5);
    var equipUid = store.AddEquipment(
        account.Value, id, weaponShopItem.ItemIndex, 255, 0, 2);
    var petUid = store.AddPet(
        account.Value, id, 1, "TestPet");
    var horseUid = store.AddHorse(
        account.Value, id, 6001, "TestMecha", "MS", 3600);
    var horseEquipUid = store.AddHorseEquipment(
        account.Value, id, 6201, -1, 255, 0, 1);
    var batteryUid = store.AddInventoryItem(
        account.Value, id, 6905, 1);
    var recoveryUid = store.AddInventoryItem(
        account.Value, id, 31201, 1);
    var horseCardUid = store.AddInventoryItem(
        account.Value, id, 6008, 1);

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
            "selected character waits for Map15 first-position spawn");
        Check(responses.Count == 9,
            "enter world emits bootstrap sync packets only");

        Check(JsonSerializer.Serialize(responses[1]).Contains("\"ItemIndex\":10046"),
            "InventorySync loads persisted item");
        Check(JsonSerializer.Serialize(responses[2]).Contains("\"ItemIdx\":6101"),
            "EquipSync loads persisted equipment");
        Check(JsonSerializer.Serialize(responses[3]).Contains("\"PETID\":1"),
            "PetSync loads persisted pet");
        Check(JsonSerializer.Serialize(responses[4]).Contains("\"HorseIdx\":6001") &&
              JsonSerializer.Serialize(responses[4]).Contains("\"EquipIdx\":6201"),
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

        Check(Convert.ToInt32(recovered["HP"]) == 70,
            "UseItemRequest applies Recovery.itd HP effect");
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
                  Convert.ToInt32(x["HorseIdx"]) == 6008 &&
                  Convert.ToInt32(x["EnergyLeft"]) == 25200),
            "UseItemRequest converts source mecha card into rideable mecha");
        Check(!assetsAfterCard.Inventory.Any(x =>
                Convert.ToInt64(x["ItemUID"]) == horseCardUid),
            "mecha card is consumed after activation");
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains("\"Type\":\"HorseSync\"")),
            "mecha-card activation refreshes HorseSync");
    }

    var buyItem = shopCatalog.GetShopItems(1).First();
    var moneyBeforeShop = Convert.ToInt64(
        store.GetCharacter(account.Value, id)!["Money"]);

    using (var buy = Json(JsonSerializer.Serialize(new
    {
        Type = "ShopAction",
        Action = "BuyNpcItem",
        ShopID = 1,
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

    var purchased = store.GetCharacterAssets(account.Value, id)
        .Inventory.First(x =>
            Convert.ToInt32(x["ItemIndex"]) == buyItem.ItemIndex);
    var purchasedUid = Convert.ToInt64(purchased["ItemUID"]);
    var moneyBeforeSell = Convert.ToInt64(
        store.GetCharacter(account.Value, id)!["Money"]);

    using (var sell = Json(JsonSerializer.Serialize(new
    {
        Type = "ShopAction",
        Action = "SellNpcItem",
        UID = purchasedUid,
        TabType = 0,
        Count = 1
    })))
    {
        var responses = router.Handle(sell.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains("\"Type\":\"SyncMoney\"")),
            "NPC shop sell emits money sync");
    }

    Check(Convert.ToInt64(
              store.GetCharacter(account.Value, id)!["Money"]) > moneyBeforeSell,
        "NPC shop sell adds money");

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
                JsonSerializer.Serialize(x).Contains($"\"Attack\":{baseAttackBeforeEquip + 20}")),
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
                    "\"EntityType\":4")),
            "PetAction Summon spawns pet entity");
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

    // The first position packet arrives after the client map scene is ready.
    using (var move = Json(
        """{"Type":"SyncPosition","X":4932,"Y":7783}"""))
    {
        var responses = router.Handle(move.RootElement, session);
        Check(responses.Count == 52,
            "first Map15 position sync spawns all NPCs");
        Check(responses.All(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SpawnEntity\"")),
            "Map15 NPCs use SpawnEntity packets");
        Check(!session.PendingMapSpawn,
            "Map15 spawn gate clears after first position sync");
    }

    var selected = store.GetCharacter(account.Value, id);
    Check(Convert.ToInt32(Convert.ToDouble(selected!["LocX"])) == 4932 &&
          Convert.ToInt32(Convert.ToDouble(selected["LocY"])) == 7783,
        "movement persists to SQLite");

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
        Check(!session.PendingMapSpawn,
            "Map16 spawn gate clears after new map is ready");
        Check(responses.Count == world.GetNpcs(16).Count,
            "Map16 NPCs spawn after map load");
    }

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

    var aiPackets = combat.TickAi(
        session,
        account.Value,
        monster.X + 200,
        monster.Y,
        DateTimeOffset.UtcNow.AddSeconds(1));

    Check(monster.HasTarget &&
          aiPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"MonsterTarget\"")) &&
          aiPackets.Any(x =>
              JsonSerializer.Serialize(x).Contains(
                  "\"Type\":\"MoveEntity\"")),
        "monster enters aggro and emits chase movement");

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

    for (var i = 0; i < 100 && !playerDeathSeen; i++)
    {
        aiNow = aiNow.AddSeconds(2);
        var monsterAttackPackets = combat.TickAi(
            session,
            account.Value,
            monster.X + 20,
            monster.Y,
            aiNow);

        playerDeathSeen = monsterAttackPackets.Any(x =>
            JsonSerializer.Serialize(x).Contains(
                "\"Type\":\"EntityDeath\"") &&
            JsonSerializer.Serialize(x).Contains(
                "\"EntityType\":0"));
    }

    selected = store.GetCharacter(account.Value, id);
    Check(playerDeathSeen &&
          Convert.ToInt32(selected!["HP"]) == 0 &&
          selected["State"].ToString() == "death",
        "monster attacks can kill player and persist death state");

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
