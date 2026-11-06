using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;
using SGO.Server.Application.Abstractions;
using SGO.Server.Protocol;

namespace SGO.Server.Infrastructure.Persistence.Sqlite;

public sealed class SqliteGameStore : IGameStore
{
    private readonly string _connectionString;

    public SqliteGameStore(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS accounts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                email TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS characters (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_id INTEGER NOT NULL,
                name TEXT NOT NULL UNIQUE,
                password TEXT NOT NULL,
                race INTEGER NOT NULL,
                hair INTEGER NOT NULL,
                hair_color INTEGER NOT NULL,
                str INTEGER NOT NULL,
                dex INTEGER NOT NULL,
                con INTEGER NOT NULL,
                wis INTEGER NOT NULL,
                level INTEGER NOT NULL DEFAULT 1,
                map_id INTEGER NOT NULL DEFAULT 15,
                x REAL NOT NULL DEFAULT 10000,
                y REAL NOT NULL DEFAULT 10000,
                hp INTEGER NOT NULL DEFAULT 100,
                max_hp INTEGER NOT NULL DEFAULT 100,
                mp INTEGER NOT NULL DEFAULT 50,
                max_mp INTEGER NOT NULL DEFAULT 50,
                sp INTEGER NOT NULL DEFAULT 100,
                max_sp INTEGER NOT NULL DEFAULT 100,
                money INTEGER NOT NULL DEFAULT 0,
                exp_use INTEGER NOT NULL DEFAULT 0,
                exp_acc INTEGER NOT NULL DEFAULT 0,
                exp_death INTEGER NOT NULL DEFAULT 0,
                max_exp INTEGER NOT NULL DEFAULT 1000,
                career INTEGER NOT NULL DEFAULT 0,
                job INTEGER NOT NULL DEFAULT 0,
                attack INTEGER NOT NULL DEFAULT 10,
                defense INTEGER NOT NULL DEFAULT 5,
                magic_attack INTEGER NOT NULL DEFAULT 5,
                magic_defense INTEGER NOT NULL DEFAULT 5,
                hit INTEGER NOT NULL DEFAULT 100,
                dodge INTEGER NOT NULL DEFAULT 0,
                lucky INTEGER NOT NULL DEFAULT 0,
                move_speed INTEGER NOT NULL DEFAULT 100,
                attack_speed INTEGER NOT NULL DEFAULT 100,
                attack_range INTEGER NOT NULL DEFAULT 80,
                crit_rate INTEGER NOT NULL DEFAULT 0,
                crit_damage INTEGER NOT NULL DEFAULT 125,
                state TEXT NOT NULL DEFAULT 'idle',
                reborn_map_id INTEGER NOT NULL DEFAULT 15,
                reborn_x REAL NOT NULL DEFAULT 9408,
                reborn_y REAL NOT NULL DEFAULT 10336,
                FOREIGN KEY(account_id) REFERENCES accounts(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_characters_account ON characters(account_id);
            """;
        cmd.ExecuteNonQuery();
        EnsureCharacterSchema(db);
    }

    private static void EnsureCharacterSchema(SqliteConnection db)
    {
        var required = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hp"] = "INTEGER NOT NULL DEFAULT 100",
            ["max_hp"] = "INTEGER NOT NULL DEFAULT 100",
            ["mp"] = "INTEGER NOT NULL DEFAULT 50",
            ["max_mp"] = "INTEGER NOT NULL DEFAULT 50",
            ["sp"] = "INTEGER NOT NULL DEFAULT 100",
            ["max_sp"] = "INTEGER NOT NULL DEFAULT 100",
            ["money"] = "INTEGER NOT NULL DEFAULT 0",
            ["exp_use"] = "INTEGER NOT NULL DEFAULT 0",
            ["exp_acc"] = "INTEGER NOT NULL DEFAULT 0",
            ["exp_death"] = "INTEGER NOT NULL DEFAULT 0",
            ["max_exp"] = "INTEGER NOT NULL DEFAULT 1000",
            ["career"] = "INTEGER NOT NULL DEFAULT 0",
            ["job"] = "INTEGER NOT NULL DEFAULT 0",
            ["attack"] = "INTEGER NOT NULL DEFAULT 10",
            ["defense"] = "INTEGER NOT NULL DEFAULT 5",
            ["magic_attack"] = "INTEGER NOT NULL DEFAULT 5",
            ["magic_defense"] = "INTEGER NOT NULL DEFAULT 5",
            ["hit"] = "INTEGER NOT NULL DEFAULT 100",
            ["dodge"] = "INTEGER NOT NULL DEFAULT 0",
            ["lucky"] = "INTEGER NOT NULL DEFAULT 0",
            ["move_speed"] = "INTEGER NOT NULL DEFAULT 100",
            ["attack_speed"] = "INTEGER NOT NULL DEFAULT 100",
            ["attack_range"] = "INTEGER NOT NULL DEFAULT 80",
            ["crit_rate"] = "INTEGER NOT NULL DEFAULT 0",
            ["crit_damage"] = "INTEGER NOT NULL DEFAULT 125",
            ["state"] = "TEXT NOT NULL DEFAULT 'idle'",
            ["reborn_map_id"] = "INTEGER NOT NULL DEFAULT 15",
            ["reborn_x"] = "REAL NOT NULL DEFAULT 9408",
            ["reborn_y"] = "REAL NOT NULL DEFAULT 10336"
        };

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var info = db.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(characters)";
            using var reader = info.ExecuteReader();
            while (reader.Read()) existing.Add(reader.GetString(1));
        }

        foreach (var (name, definition) in required)
        {
            if (existing.Contains(name)) continue;
            using var alter = db.CreateCommand();
            alter.CommandText = $"ALTER TABLE characters ADD COLUMN {name} {definition}";
            alter.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        using var pragma = db.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return db;
    }

    private static void Arg(SqliteCommand cmd, string name, object? value)
        => cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, 120_000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(derived);
    }

    private static bool Verify(string password, string hash)
    {
        var parts = hash.Split(':');
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, 120_000, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public (bool Success, string Message) Register(string username, string password, string email)
    {
        username = username.Trim();
        if (username.Length is < 3 or > 40 || password.Length is < 4 or > 200)
            return (false, "帳號或密碼長度不符");

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO accounts(username,password_hash,email) VALUES($u,$p,$e)";
        Arg(cmd, "$u", username);
        Arg(cmd, "$p", Hash(password));
        Arg(cmd, "$e", email.Trim());
        try
        {
            cmd.ExecuteNonQuery();
            return (true, "註冊成功");
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        {
            return (false, "帳號已存在");
        }
    }

    public long? Login(string username, string password)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,password_hash FROM accounts WHERE username=$u";
        Arg(cmd, "$u", username.Trim());
        using var r = cmd.ExecuteReader();
        return r.Read() && Verify(password, r.GetString(1)) ? r.GetInt64(0) : null;
    }

    public List<Dictionary<string, object>> Characters(long accountId)
    {
        var list = new List<Dictionary<string, object>>();
        using var db = Open();
        using var cmd = CharacterQuery(db);
        cmd.CommandText += " WHERE account_id=$a ORDER BY id LIMIT 3";
        Arg(cmd, "$a", accountId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadCharacter(r));

        // The recovered client renders exactly three character slots and also
        // reads character_list[0].ACCID when creating a character. Keep the
        // server response shape stable by always returning three slots.
        while (list.Count < 3)
            list.Add(new Dictionary<string, object>
            {
                ["ACCID"] = accountId,
                ["DBID"] = 0
            });
        return list;
    }

    public Dictionary<string, object>? GetCharacter(long accountId, long dbid)
    {
        using var db = Open();
        using var cmd = CharacterQuery(db);
        cmd.CommandText += " WHERE account_id=$a AND id=$id LIMIT 1";
        Arg(cmd, "$a", accountId);
        Arg(cmd, "$id", dbid);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadCharacter(r) : null;
    }

    private static SqliteCommand CharacterQuery(SqliteConnection db)
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT id,account_id,name,password,race,hair,hair_color,str,dex,con,wis,
                   level,map_id,x,y,hp,max_hp,mp,max_mp,sp,max_sp,money,
                   exp_use,exp_acc,exp_death,max_exp,career,job,attack,defense,
                   magic_attack,magic_defense,hit,dodge,lucky,move_speed,
                   attack_speed,attack_range,crit_rate,crit_damage,state,
                   reborn_map_id,reborn_x,reborn_y
            FROM characters
            """;
        return cmd;
    }

    private static Dictionary<string, object> ReadCharacter(SqliteDataReader r)
    {
        return new Dictionary<string, object>
        {
            ["DBID"] = r.GetInt64(0),
            ["ACCID"] = r.GetInt64(1),
            ["NAME"] = r.GetString(2),
            ["CharacterPwd"] = r.GetString(3),
            ["Race"] = r.GetInt32(4),
            ["Hair"] = r.GetInt32(5),
            ["HairColor"] = r.GetInt32(6),
            ["Str"] = r.GetInt32(7),
            ["Dex"] = r.GetInt32(8),
            ["Con"] = r.GetInt32(9),
            ["Wis"] = r.GetInt32(10),
            ["Level"] = r.GetInt32(11),
            ["MapID"] = r.GetInt32(12),
            ["LastMapID"] = r.GetInt32(12),
            ["LocX"] = r.GetDouble(13),
            ["LocY"] = r.GetDouble(14),
            ["LastLocX"] = r.GetDouble(13),
            ["LastLocY"] = r.GetDouble(14),
            ["HP"] = r.GetInt32(15),
            ["MaxHP"] = r.GetInt32(16),
            ["MP"] = r.GetInt32(17),
            ["MaxMP"] = r.GetInt32(18),
            ["SP"] = r.GetInt32(19),
            ["MaxSP"] = r.GetInt32(20),
            ["Money"] = r.GetInt64(21),
            ["ExpUse"] = r.GetInt32(22),
            ["ExpAcc"] = r.GetInt32(23),
            ["ExpDeath"] = r.GetInt32(24),
            ["MaxExp"] = r.GetInt32(25),
            ["Career"] = r.GetInt32(26),
            ["Job"] = r.GetInt32(27),
            ["Attack"] = r.GetInt32(28),
            ["Defense"] = r.GetInt32(29),
            ["MagicAttack"] = r.GetInt32(30),
            ["MagicDefense"] = r.GetInt32(31),
            ["Hit"] = r.GetInt32(32),
            ["Dodge"] = r.GetInt32(33),
            ["Lucky"] = r.GetInt32(34),
            ["MoveSpeed"] = r.GetInt32(35),
            ["AttackSpeed"] = r.GetInt32(36),
            ["AttackRange"] = r.GetInt32(37),
            ["CritRate"] = r.GetInt32(38),
            ["CritDamage"] = r.GetInt32(39),
            ["State"] = r.GetString(40),
            ["RebornMapID"] = r.GetInt32(41),
            ["RebornX"] = r.GetDouble(42),
            ["RebornY"] = r.GetDouble(43),

            // Safe defaults expected by PlayerData.load_from_json().
            ["Title"] = 0,
            ["UnlockedTitles"] = Array.Empty<int>(),
            ["RedMan"] = 0,
            ["RedManKillLeft"] = 0,
            ["PKStatus"] = 0,
            ["ClubID"] = 0,
            ["ClubPosition"] = 0,
            ["ClubDonation"] = 0,
            ["ClubName"] = "",
            ["Bonnet"] = r.GetInt32(4) == 3 ? -1 : 0,
            ["EquipColor0"] = 0,
            ["Left_hand"] = 0,
            ["Right_hand"] = 0,
            ["Upper_armor"] = 0,
            ["Lower_armor"] = 0,
            ["Weapon"] = 0,
            ["Shield"] = 0,
            ["Back"] = 0,
            ["Amulet"] = 0,
            ["PartyID"] = "",
            ["PartyLeaderDBID"] = 0,
            ["PartyMembers"] = Array.Empty<object>(),
            ["MechaType"] = "MS",
            ["SummonedHorseUID"] = -1,
            ["TransformType"] = 0,
            ["TransformID"] = "",
            ["TransformColorIndex"] = 0,
            ["Inventory"] = Array.Empty<object>(),
            ["InventoryMaxSize"] = 200,
            ["EquipList"] = Array.Empty<object>(),
            ["EquipMaxSize"] = 200,
            ["PetList"] = Array.Empty<object>(),
            ["PetMaxSize"] = 50,
            ["HorseList"] = Array.Empty<object>(),
            ["HorseEquipList"] = Array.Empty<object>(),
            ["HorseEquipMaxSize"] = 20,
            ["SkillList"] = Array.Empty<object>(),
            ["Quests"] = Array.Empty<object>(),
            ["Shortcuts"] = Array.Empty<object>()
        };
    }

    public (bool Success, string Message) Create(long accountId, JsonElement data)
    {
        var name = data.Str("Name").Trim();
        var password = data.Str("Password");
        if (name.Length is < 1 or > 30 || password.Length > 100)
            return (false, "角色資料格式錯誤");

        using var db = Open();
        using var tx = db.BeginTransaction();

        using var count = db.CreateCommand();
        count.Transaction = tx;
        count.CommandText = "SELECT count(*) FROM characters WHERE account_id=$a";
        Arg(count, "$a", accountId);
        if (Convert.ToInt32(count.ExecuteScalar()) >= 3)
            return (false, "最多只能建立三個角色");

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO characters(account_id,name,password,race,hair,hair_color,str,dex,con,wis)
            VALUES($a,$n,$p,$r,$h,$c,$s,$d,$co,$w)
            """;
        Arg(cmd, "$a", accountId);
        Arg(cmd, "$n", name);
        Arg(cmd, "$p", password);
        Arg(cmd, "$r", Math.Clamp(data.Int("Race"), 0, 4));
        Arg(cmd, "$h", Math.Max(0, data.Int("Hair")));
        Arg(cmd, "$c", Math.Max(0, data.Int("HairColor")));
        Arg(cmd, "$s", Math.Max(0, data.Int("Str")));
        Arg(cmd, "$d", Math.Max(0, data.Int("Dex")));
        Arg(cmd, "$co", Math.Max(0, data.Int("Con")));
        Arg(cmd, "$w", Math.Max(0, data.Int("Wis")));

        try
        {
            cmd.ExecuteNonQuery();
            tx.Commit();
            return (true, "角色建立成功");
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        {
            return (false, "角色名稱已使用");
        }
    }

    public bool Delete(long accountId, long dbid, string password)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "DELETE FROM characters WHERE id=$id AND account_id=$a AND password=$p";
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);
        Arg(cmd, "$p", password);
        return cmd.ExecuteNonQuery() == 1;
    }

    public bool ChangeCharacterPassword(long accountId, long dbid, string newPassword)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "UPDATE characters SET password=$p WHERE id=$id AND account_id=$a";
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);
        Arg(cmd, "$p", newPassword);
        return cmd.ExecuteNonQuery() == 1;
    }

    public bool UpdatePosition(long accountId, long dbid, int x, int y)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "UPDATE characters SET x=$x,y=$y WHERE id=$id AND account_id=$a";
        Arg(cmd, "$x", x);
        Arg(cmd, "$y", y);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);
        return cmd.ExecuteNonQuery() == 1;
    }

    public bool UpdateLocation(long accountId, long dbid, int mapId, int x, int y)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "UPDATE characters SET map_id=$m,x=$x,y=$y WHERE id=$id AND account_id=$a";
        Arg(cmd, "$m", mapId);
        Arg(cmd, "$x", x);
        Arg(cmd, "$y", y);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);
        return cmd.ExecuteNonQuery() == 1;
    }

    public bool SetRebornLocation(long accountId, long dbid, int mapId, int x, int y)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET reborn_map_id=$m, reborn_x=$x, reborn_y=$y
             WHERE id=$id AND account_id=$a
            """;
        Arg(cmd, "$m", mapId);
        Arg(cmd, "$x", x);
        Arg(cmd, "$y", y);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);
        return cmd.ExecuteNonQuery() == 1;
    }

    public bool UpdateState(long accountId, long dbid, string state)
    {
        if (state.Length > 32) return false;
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "UPDATE characters SET state=$s WHERE id=$id AND account_id=$a";
        Arg(cmd, "$s", state);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);
        return cmd.ExecuteNonQuery() == 1;
    }

    public Dictionary<string, object>? ApplyDamage(
        long accountId,
        long dbid,
        int damage)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE characters
               SET hp = MAX(0, hp - $damage),
                   state = CASE
                       WHEN hp - $damage <= 0 THEN 'death'
                       ELSE state
                   END
             WHERE id=$id AND account_id=$a
            """;
        Arg(cmd, "$damage", Math.Max(0, damage));
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        tx.Commit();
        return GetCharacter(accountId, dbid);
    }

    public Dictionary<string, object>? AddRewards(
        long accountId,
        long dbid,
        int exp,
        int money)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE characters
               SET exp_acc = exp_acc + $exp,
                   money = money + $money
             WHERE id=$id AND account_id=$a
            """;
        Arg(cmd, "$exp", Math.Max(0, exp));
        Arg(cmd, "$money", Math.Max(0, money));
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        tx.Commit();
        return GetCharacter(accountId, dbid);
    }

    public Dictionary<string, object>? Revive(long accountId, long dbid)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET hp=max_hp, mp=max_mp, sp=max_sp, state='idle'
             WHERE id=$id AND account_id=$a
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);
        if (cmd.ExecuteNonQuery() != 1) return null;
        return GetCharacter(accountId, dbid);
    }
}
