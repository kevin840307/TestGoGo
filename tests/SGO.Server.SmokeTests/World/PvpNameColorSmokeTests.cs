using Microsoft.Data.Sqlite;
using SGO.Server.Application.Combat;
using SGO.Server.Infrastructure.Persistence.Sqlite;

namespace SGO.Server.SmokeTests.World;

// Exercises the actual SQLite transaction, not just source-text assertions.
internal static class PvpNameColorSmokeTests
{
    public static void Run()
    {
        foreach (var mapId in new[] { 7, 9, 15, 17, 22, 25, 38, 40 })
            Check(!PvpRules.IsPvpMap(mapId),
                $"original NonPKMap Map{mapId} cannot attack");
        Check(PvpRules.IsPvpMap(13), "Map13 allows player PvP");

        const long clock = 1_800_000_000_000L;
        var dbPath = Path.Combine(
            Path.GetTempPath(), "sgo-pvp-" + Guid.NewGuid() + ".db");
        try
        {
            var store = new SqliteGameStore(dbPath);
            using (var db = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()))
            {
                db.Open();
                using var seed = db.CreateCommand();
                seed.CommandText = """
                    INSERT INTO accounts (id,username,password_hash,email)
                    VALUES (1,'pvp_test_a','hash',''),(2,'pvp_test_b','hash','');
                    INSERT INTO characters
                        (id,account_id,name,password,race,hair,hair_color,
                         str,dex,con,wis,map_id,hp,max_hp,pk_status)
                    VALUES
                        (101,1,'pvp_smoke_a','pw',0,0,0,1,1,1,1,13,100,100,1),
                        (202,2,'pvp_smoke_b','pw',0,0,0,1,1,1,1,13,100,100,1);
                    """;
                seed.ExecuteNonQuery();
            }

            var hit = store.ApplyPvpHit(
                1, 101, 2, 202, 13, 20, clock,
                PvpRules.PinkDurationMilliseconds);
            Check(hit is not null && hit.CurrentHp == 80 &&
                  hit.TargetWasWhite && !hit.Killed,
                  "nonlethal white-name hit commits damage and white-target PK flag");
            Check(hit is not null &&
                  PvpRules.NameColorState(hit.AttackerState, clock) == 2,
                  "attacking white makes attacker pink");
            Check(hit is not null &&
                  PvpRules.NameColorState(
                      hit.AttackerState, clock + 58_000) == 3,
                  "last three seconds blink before white");
            Check(hit is not null &&
                  PvpRules.NameColorState(
                      hit.AttackerState, clock + 61_000) == 0,
                  "pink expires to PKStatus-governed white");
            Check(store.TrySetPvpMode(1, 101, 0, clock) is null,
                  "pink player cannot turn blue to evade PvP");

            var fatal = store.ApplyPvpHit(
                1, 101, 2, 202, 13, 80, clock,
                PvpRules.PinkDurationMilliseconds);
            Check(fatal is not null && fatal.Killed &&
                  fatal.CurrentHp == 0 &&
                  PvpRules.NameColorState(fatal.AttackerState, clock) == 1,
                  "killing white player promotes attacker to red");
            Check(store.ApplyPvpHit(
                  1, 101, 2, 202, 13, 1, clock,
                  PvpRules.PinkDurationMilliseconds) is null,
                  "second lethal hit is atomically denied");
            Check(store.TrySetPvpMode(1, 101, 0, clock) is null,
                  "red cannot switch into protected blue status");

            var reset = store.SetPvpState(1, 101, 0, 0, 0);
            Check(reset is not null && reset.PkStatus == 0 &&
                  reset.RedMan == 0 &&
                  PvpRules.NameColorState(reset, clock) == 0,
                  "GM can restore protected blue state");
            var persisted = store.GetCharacter(1, 101);
            Check(persisted is not null &&
                  Convert.ToInt32(persisted["PKStatus"]) == 0 &&
                  Convert.ToInt32(persisted["RedMan"]) == 0,
                  "GM color state survives reloading from SQLite");
            Check(store.SetPvpState(1, 101, 1, 0, 0) is not null &&
                  store.TrySetPvpMode(1, 101, 0, clock) is not null,
                  "white player can voluntarily switch to blue");
        }
        finally
        {
            foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            {
                try { File.Delete(path); } catch (IOException) { }
            }
        }
    }

    private static void Check(bool ok, string message)
        => SGO.Server.SmokeTests.SmokeAssert.Check(ok, message);
}
