using Microsoft.Extensions.Logging.Abstractions;
using SGO.Server.Application;
using SGO.Server.Application.Combat;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.Legacy.Data;
using SGO.Server.Infrastructure.Legacy.Quest;
using SGO.Server.Infrastructure.Legacy.World;
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
var legacyDataRoot = Path.Combine(repoRoot, "Old", "服務端", "Dat");
var questRoot = Path.Combine(legacyDataRoot, "Quest");

Check(Directory.Exists(legacyDataRoot), "legacy server data directory exists");

var world = new LegacyWorldService(legacyDataRoot);
var quests = new LegacyQuestRuntime(questRoot);
var map15Npcs = world.GetNpcs(15);

Check(map15Npcs.Count == 52, "Map15 legacy NPC.dat contains 52 NPC records");
Check(map15Npcs[0].Script == "KingCity(P06)_to_KingCity(P08).txt",
    "Map15 first portal script parsed");
Check(map15Npcs[0].X == 4932 && map15Npcs[0].Y == 7783,
    "Map15 first portal coordinates parsed");

Check(LegacyDialogFormatter.CleanText("n要進入尼尼亞皇城嗎？") == "要進入尼尼亞皇城嗎？" &&
      LegacyDialogFormatter.CleanText("n1你好") == "你好" &&
      LegacyDialogFormatter.CleanText("p7取得道具") == "取得道具",
    "legacy dialog formatter strips presentation prefixes");

var bankResult = quests.Execute(
    "Bank.txt", "start", 1, "銀行", null, _ => 0);
Check(bankResult.Packets.Any(x =>
        JsonSerializer.Serialize(x).Contains("\"Type\":\"OpenBankWindow\"")),
    "legacy OpenBank emits client OpenBankWindow");

var repairResult = quests.Execute(
    "Repair.txt", "start", 2, "修理店", null, _ => 0);
Check(repairResult.Packets.Any(x =>
        JsonSerializer.Serialize(x).Contains("\"Type\":\"OpenRepairWindow\"") &&
        JsonSerializer.Serialize(x).Contains("\"RepairType\":10")),
    "legacy OpenRepair emits repair window and type");

var shopStart = quests.Execute(
    "shop_e3.txt", "start", 3, "商店", null, _ => 0);
Check(shopStart.Packets.Any(x =>
        JsonSerializer.Serialize(x).Contains("\"Type\":\"NpcDialogResponse\"")),
    "legacy shop start opens choice dialog");

var shopBuy = quests.Execute(
    "shop_e3.txt", "reply", 3, "商店", 0, _ => 0);
Check(shopBuy.Packets.Any(x =>
        JsonSerializer.Serialize(x).Contains("\"Type\":\"NpcShopViewResponse\"") &&
        JsonSerializer.Serialize(x).Contains("\"ShopID\":7")),
    "legacy OpenShop emits client NPC shop response");

var ranking = quests.Execute(
    "Ranking.txt", "Check", 4, "排行", 0, _ => 0);
Check(ranking.Packets.Any(x =>
        JsonSerializer.Serialize(x).Contains("\"Type\":\"OpenRankWindow\"") &&
        JsonSerializer.Serialize(x).Contains("\"RankType\":1")),
    "legacy RankQuery emits OpenRankWindow");

var reborn = quests.Execute(
    "King_RebornSeting.txt", "set", 5, "復活點", 0, _ => 0);
Check(reborn.RebornMapId == 15 &&
      reborn.RebornX == 9408 &&
      reborn.RebornY == 10336,
    "legacy SetRebornLocation captured");

var tsPortal = quests.Execute(
    "TS_KINGTD_KING.txt", "start", 6, "傳送點", null, _ => 0);
Check(tsPortal.Packets.Any(x =>
        JsonSerializer.Serialize(x).Contains("\"Type\":\"NpcDialogResponse\"")),
    "legacy #include TS_Common parses and opens dialog");

var careerNpc = quests.Execute(
    "K27001_A.txt", "start", 7, "皇家侍衛", null,
    key => key == "Level" ? 1 : 0);
Check(careerNpc.Packets.Any(x =>
        JsonSerializer.Serialize(x).Contains("\"Type\":\"NpcDialogResponse\"")),
    "legacy nonstandard smart-quote escapes are normalized");

var map15Enemies = world.GetEnemies(15);
Check(map15Enemies.Count == 4,
    "Map15 Enemy.dat binary parser reads four legacy records");
Check(map15Enemies.All(x => x.EnemyId == 245),
    "Map15 legacy enemy records resolve EnemyID 245");
Check(world.BuildMapSpawnPackets(15).Count == 52,
    "Map15 city suppresses combat enemy spawns");

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
    var router = new PacketRouter(
        store,
        world,
        quests,
        combat,
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

    var itemUid = store.AddInventoryItem(
        account.Value, id, 10046, 5);
    var equipUid = store.AddEquipment(
        account.Value, id, 6101, 255, 0, 2);
    var petUid = store.AddPet(
        account.Value, id, 1, "TestPet");
    var horseUid = store.AddHorse(
        account.Value, id, 6001, "TestMecha", "MS", 3600);
    var horseEquipUid = store.AddHorseEquipment(
        account.Value, id, 6201, -1, 3, 0, 1);

    Check(itemUid > 0 &&
          equipUid > 0 &&
          petUid > 0 &&
          horseUid > 0 &&
          horseEquipUid > 0,
        "seed persistent inventory equipment pet and mecha assets");

    var seededAssets = store.GetCharacterAssets(account.Value, id);
    Check(seededAssets.Inventory.Count == 1 &&
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

    using (var select = Json(
        $$"""{"Type":"SelectCharacterRequest","DBID":{{id}}}"""))
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

    using (var equip = Json(
        $"""{"Type":"EquipRequest","ItemUID":{{equipUid}},"TargetSlot":5}"""))
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

    using (var drive = Json(
        $"""{"Type":"DriveMechaRequest","HorseUID":{{horseUid}}}"""))
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
    }

    using (var horseEquip = Json(
        $"""{"Type":"HorseEquipRequest","EquipUID":{{horseEquipUid}},"HorseUID":{{horseUid}}}"""))
    {
        var responses = router.Handle(horseEquip.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"HorseSync\"")),
            "HorseEquipRequest refreshes HorseSync");
        Check(store.GetCharacterAssets(account.Value, id).HorseEquips
                .Any(x => Convert.ToInt64(x["EquipUID"]) == horseEquipUid &&
                          Convert.ToInt64(x["HorseUID"]) == horseUid),
            "mecha equipment assignment persists");
    }

    using (var petSummon = Json(
        $"""{"Type":"PetAction","Action":"Summon","PetUID":{{petUid}}}"""))
    {
        var responses = router.Handle(petSummon.RootElement, session);
        Check(responses.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"PetSync\"")),
            "PetAction Summon refreshes PetSync");
        Check(store.GetCharacterAssets(account.Value, id).Pets
                .Any(x => Convert.ToInt64(x["UID"]) == petUid &&
                          Convert.ToInt32(x["State"]) == 1),
            "pet summon state persists");
    }

    using (var petRecall = Json(
        $"""{"Type":"PetAction","Action":"Recall","PetUID":{{petUid}}}"""))
    {
        router.Handle(petRecall.RootElement, session);
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
            "first Map15 position sync spawns all legacy NPCs");
        Check(responses.All(x =>
                JsonSerializer.Serialize(x).Contains(
                    "\"Type\":\"SpawnEntity\"")),
            "Map15 legacy NPCs use SpawnEntity packets");
        Check(!session.PendingMapSpawn,
            "Map15 spawn gate clears after first position sync");
    }

    var selected = store.GetCharacter(account.Value, id);
    Check(Convert.ToInt32(Convert.ToDouble(selected!["LocX"])) == 4932 &&
          Convert.ToInt32(Convert.ToDouble(selected["LocY"])) == 7783,
        "movement persists to SQLite");

    var portal = map15Npcs[0];

    using (var interact = Json(
        $$"""{"Type":"InteractRequest","EntityID":{{portal.EntityId}}}"""))
    {
        var responses = router.Handle(interact.RootElement, session);
        Check(responses.Count >= 1 &&
              JsonSerializer.Serialize(responses[0]).Contains(
                  "\"Type\":\"NpcDialogResponse\""),
            "Map15 portal executes legacy Lua start()");
        Check(JsonSerializer.Serialize(responses[0]).Contains("Check#0"),
            "legacy Ask options encode GetChoice target");
    }

    using (var option = Json(
        $$"""{"Type":"NpcOptionRequest","EntityID":{{portal.EntityId}},"ScriptName":"{{portal.Script}}","TargetNode":"Check#0"}"""))
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
            "Map16 legacy NPCs spawn after map load");
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
