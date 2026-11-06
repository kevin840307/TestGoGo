using SGO.Server;
using System.Text.Json;

static void Check(bool ok, string msg)
{
    if (!ok) throw new Exception("FAILED: " + msg);
    Console.WriteLine("PASS: " + msg);
}
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
    var (created, _) = store.Register("offline", "test-password", "");
    Check(created, "create account");
    Check(store.Login("offline", "bad") == null, "reject incorrect password");
    var account = store.Login("offline", "test-password");
    Check(account.HasValue, "login with correct password");
    var slots = store.Characters(account!.Value);
    Check(slots.Count == 1 && Convert.ToInt64(slots[0]["DBID"]) == 0, "empty character placeholder");
    using var character = JsonDocument.Parse("""
        {"Name":"TestCharacter","Password":"1234","Race":1,"Hair":2,"HairColor":3,"Str":4,"Dex":5,"Con":6,"Wis":7}
        """);
    Check(store.Create(account.Value, character.RootElement).Success, "create character");
    slots = store.Characters(account.Value);
    Check(slots.Count == 1 && slots[0]["NAME"].ToString() == "TestCharacter", "list character");
    var id = Convert.ToInt64(slots[0]["DBID"]);
    Check(store.Select(account.Value, id) is not null, "select owned character");
    Check(!store.Delete(account.Value, id, "wrong"), "reject wrong character password");
    Check(store.Delete(account.Value, id, "1234"), "delete character");
    Check(Convert.ToInt64(store.Characters(account.Value)[0]["DBID"]) == 0, "empty slots after delete");
}
finally
{
    try { Directory.Delete(temp, true); } catch { }
}
Console.WriteLine("All smoke tests passed.");
