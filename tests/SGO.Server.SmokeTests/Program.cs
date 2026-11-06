using Microsoft.Extensions.Logging.Abstractions;
using SGO.Server;
using System.Text.Json;

static void Check(bool ok, string msg)
{
    if (!ok) throw new Exception("FAILED: " + msg);
    Console.WriteLine("PASS: " + msg);
}

static JsonDocument Json(string value) => JsonDocument.Parse(value);

var payload = new { Type = "LoginRequest", User = "offline", Pass = "test" };
var ciphertext = Protocol.Encode(payload);
using (var packet = Protocol.Decode(ciphertext))
{
    Check(packet.RootElement.Str("Type") == "LoginRequest", "AES-CBC/PKCS7 packet round-trip");
    Check(packet.RootElement.Str("User") == "offline", "JSON decoded fields");
}

var temp = Path.Combine(Path.GetTempPath(), "sgo-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);

try
{
    var store = new SqliteGameStore(Path.Combine(temp, "game.db"));
    var router = new PacketRouter(store, NullLogger<PacketRouter>.Instance);
    var session = new ClientSession();

    var (created, _) = store.Register("offline", "test-password", "");
    Check(created, "create account");
    Check(store.Login("offline", "bad") == null, "reject incorrect password");

    var account = store.Login("offline", "test-password");
    Check(account.HasValue, "login with correct password");

    var slots = store.Characters(account!.Value);
    Check(slots.Count == 3 &&
          slots.All(x => Convert.ToInt64(x["DBID"]) == 0),
        "stable three empty character slots");

    using var create = Json("""
        {"Name":"TestCharacter","Password":"1234","Race":1,"Hair":2,"HairColor":3,"Str":4,"Dex":5,"Con":6,"Wis":7}
        """);
    Check(store.Create(account.Value, create.RootElement).Success, "create character");

    slots = store.Characters(account.Value);
    Check(slots.Count == 3 &&
          slots.Count(x => Convert.ToInt64(x["DBID"]) > 0) == 1 &&
          slots.Any(x => x.TryGetValue("NAME", out var n) && n?.ToString() == "TestCharacter"),
        "list first character in three-slot response");

    using var character2 = Json("""
        {"Name":"SecondCharacter","Password":"5678","Race":0,"Hair":1,"HairColor":2,"Str":7,"Dex":6,"Con":5,"Wis":4}
        """);
    Check(store.Create(account.Value, character2.RootElement).Success,
        "create second character");
    slots = store.Characters(account.Value);
    Check(slots.Count == 3 &&
          slots.Count(x => Convert.ToInt64(x["DBID"]) > 0) == 2,
        "two characters remain in stable three-slot response");
    Check(slots.Any(x => x.TryGetValue("NAME", out var n) && n?.ToString() == "TestCharacter") &&
          slots.Any(x => x.TryGetValue("NAME", out var n) && n?.ToString() == "SecondCharacter"),
        "creating second character does not replace first");

    var id = Convert.ToInt64(slots.First(x => x.TryGetValue("NAME", out var n) && n?.ToString() == "TestCharacter")["DBID"]);
    var selected = store.GetCharacter(account.Value, id);
    Check(selected is not null, "select owned character");
    Check(Convert.ToInt32(selected!["HP"]) == 100 &&
          Convert.ToInt32(selected["MapID"]) == 15,
        "complete player bootstrap defaults");

    using (var login = Json("""{"Type":"LoginRequest","User":"offline","Pass":"test-password"}"""))
    {
        var responses = router.Handle(login.RootElement, session);
        Check(responses.Count == 1 && session.AccountId == account,
            "router login establishes authenticated session");
    }

    using (var select = Json($$"""{"Type":"SelectCharacterRequest","DBID":{{id}}}"""))
    {
        var responses = router.Handle(select.RootElement, session);
        Check(session.CharacterId == id, "router selects character");
        Check(responses.Count >= 10, "enter world emits initial sync packet set plus Map15 monster");

        var firstJson = JsonSerializer.Serialize(responses[0]);
        using var first = Json(firstJson);
        Check(first.RootElement.Str("Type") == "EnterWorldSuccess",
            "first select response enters world");
        Check(first.RootElement.Int("MapID") == 15,
            "enter world includes map id");
        Check(responses.Any(x => JsonSerializer.Serialize(x).Contains(""Type":"SpawnEntity"")),
            "Map15 spawns local test monster");
    }

    using (var attack = Json("""{"Type":"AttackEntity","TargetEntityID":900001,"TargetType":1}"""))
    {
        var responses = router.Handle(attack.RootElement, session);
        Check(responses.Count >= 1 &&
              JsonSerializer.Serialize(responses[0]).Contains(""Type":"EntityDamage""),
            "basic attack damages Map15 test monster");
    }

    using (var move = Json("""{"Type":"SyncPosition","X":1234,"Y":5678}"""))
        router.Handle(move.RootElement, session);

    selected = store.GetCharacter(account.Value, id);
    Check(Convert.ToInt32(Convert.ToDouble(selected!["LocX"])) == 1234 &&
          Convert.ToInt32(Convert.ToDouble(selected["LocY"])) == 5678,
        "movement persists to SQLite");

    using (var chat = Json("""{"Type":"ChatMessage","Channel":0,"Message":"hello","BubbleType":0}"""))
    {
        var responses = router.Handle(chat.RootElement, session);
        Check(responses.Count == 1 &&
              JsonSerializer.Serialize(responses[0]).Contains("ChatBroadcast"),
            "local chat echoes as ChatBroadcast");
    }

    using (var logout = Json("""{"Type":"LogoutRequest","IsBackToLogin":true}"""))
    {
        var responses = router.Handle(logout.RootElement, session);
        Check(responses.Count == 1 && session.CloseRequested,
            "logout requests websocket close after response");
        Check(session.AccountId is null && session.CharacterId is null,
            "logout clears authenticated session");
    }

    session.AccountId = account;
    session.CharacterId = id;
    session.CloseRequested = false;

    Check(!store.Delete(account.Value, id, "wrong"),
        "reject wrong character password");
    Check(store.Delete(account.Value, id, "1234"),
        "delete first character");
    slots = store.Characters(account.Value);
    Check(slots.Count == 3 &&
          slots.Count(x => Convert.ToInt64(x["DBID"]) > 0) == 1 &&
          slots.Any(x => x.TryGetValue("NAME", out var n) && n?.ToString() == "SecondCharacter"),
        "second character remains after deleting first");

    var secondId = Convert.ToInt64(slots.First(x => x.TryGetValue("NAME", out var n) && n?.ToString() == "SecondCharacter")["DBID"]);
    Check(store.Delete(account.Value, secondId, "5678"),
        "delete second character");
    slots = store.Characters(account.Value);
    Check(slots.Count == 3 && slots.All(x => Convert.ToInt64(x["DBID"]) == 0),
        "three empty slots after deleting all characters");
}
finally
{
    try { Directory.Delete(temp, true); } catch { }
}

Console.WriteLine("All smoke tests passed.");
