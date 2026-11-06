using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SGO.Server;

public sealed class SqliteGameStore
{
    private readonly string _connectionString;
    public SqliteGameStore(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
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
                FOREIGN KEY(account_id) REFERENCES accounts(id)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, 120_000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(derived);
    }
    private static bool Verify(string password, string hash)
    {
        var parts = hash.Split(':');
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 120_000, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
    private static void Arg(SqliteCommand cmd, string name, object? value) => cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

    public (bool Success, string Message) Register(string username, string password, string email)
    {
        if (username.Length is < 3 or > 40 || password.Length is < 4 or > 200)
            return (false, "帳號或密碼長度不符");
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO accounts(username,password_hash,email) VALUES($u,$p,$e)";
        Arg(cmd, "$u", username); Arg(cmd, "$p", Hash(password)); Arg(cmd, "$e", email);
        try { cmd.ExecuteNonQuery(); return (true, "註冊成功"); }
        catch (SqliteException e) when (e.SqliteErrorCode == 19) { return (false, "帳號已存在"); }
    }

    public long? Login(string username, string password)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,password_hash FROM accounts WHERE username=$u";
        Arg(cmd, "$u", username);
        using var reader = cmd.ExecuteReader();
        return reader.Read() && Verify(password, reader.GetString(1)) ? reader.GetInt64(0) : null;
    }

    public List<Dictionary<string, object>> Characters(long accountId)
    {
        var result = new List<Dictionary<string, object>>();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT id,name,password,race,hair,hair_color,str,dex,con,wis,level,map_id,x,y
            FROM characters WHERE account_id=$a ORDER BY id LIMIT 3
            """;
        Arg(cmd, "$a", accountId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new Dictionary<string, object> {
                ["ACCID"] = accountId, ["DBID"] = r.GetInt64(0), ["NAME"] = r.GetString(1),
                ["CharacterPwd"] = r.GetString(2), ["Race"] = r.GetInt32(3),
                ["Hair"] = r.GetInt32(4), ["HairColor"] = r.GetInt32(5),
                ["STR"] = r.GetInt32(6), ["DEX"] = r.GetInt32(7),
                ["CON"] = r.GetInt32(8), ["WIS"] = r.GetInt32(9),
                ["Level"] = r.GetInt32(10), ["LastMapID"] = r.GetInt32(11),
                ["LastLocX"] = r.GetDouble(12), ["LastLocY"] = r.GetDouble(13),
                ["MapID"] = r.GetInt32(11), ["LocX"] = r.GetDouble(12),
                ["LocY"] = r.GetDouble(13), ["MaxHP"] = 100, ["HP"] = 100,
                ["MaxMP"] = 50, ["MP"] = 50, ["MaxSP"] = 100, ["SP"] = 100
            });
        }
        // The recovered entry_manager reads character_list[0].ACCID even when no character exists.
        // Supply an empty slot to keep first-character creation working.
        if (result.Count == 0)
            result.Add(new Dictionary<string, object> { ["ACCID"] = accountId, ["DBID"] = 0 });
        return result;
    }

    public (bool Success, string Message) Create(long accountId, JsonElement data)
    {
        var name = data.Str("Name").Trim();
        var password = data.Str("Password");
        if (name.Length is < 1 or > 30 || password.Length > 100) return (false, "角色資料格式錯誤");
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var count = db.CreateCommand();
        count.Transaction = tx;
        count.CommandText = "SELECT count(*) FROM characters WHERE account_id=$a";
        Arg(count, "$a", accountId);
        if (Convert.ToInt32(count.ExecuteScalar()) >= 3) return (false, "最多只能建立三個角色");
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO characters(account_id,name,password,race,hair,hair_color,str,dex,con,wis)
            VALUES($a,$n,$p,$r,$h,$c,$s,$d,$co,$w)
            """;
        Arg(cmd, "$a", accountId); Arg(cmd, "$n", name); Arg(cmd, "$p", password);
        Arg(cmd, "$r", data.Int("Race")); Arg(cmd, "$h", data.Int("Hair"));
        Arg(cmd, "$c", data.Int("HairColor")); Arg(cmd, "$s", data.Int("Str"));
        Arg(cmd, "$d", data.Int("Dex")); Arg(cmd, "$co", data.Int("Con")); Arg(cmd, "$w", data.Int("Wis"));
        try { cmd.ExecuteNonQuery(); tx.Commit(); return (true, "角色建立成功"); }
        catch (SqliteException e) when (e.SqliteErrorCode == 19) { return (false, "角色名稱已使用"); }
    }

    public bool Delete(long accountId, long dbid, string password)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM characters WHERE id=$id AND account_id=$a AND password=$p";
        Arg(cmd, "$id", dbid); Arg(cmd, "$a", accountId); Arg(cmd, "$p", password);
        return cmd.ExecuteNonQuery() == 1;
    }

    public bool ChangeCharacterPassword(long accountId, long dbid, string newPassword)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE characters SET password=$p WHERE id=$id AND account_id=$a";
        Arg(cmd, "$id", dbid); Arg(cmd, "$a", accountId); Arg(cmd, "$p", newPassword);
        return cmd.ExecuteNonQuery() == 1;
    }

    public Dictionary<string, object>? Select(long accountId, long dbid)
        => Characters(accountId).FirstOrDefault(x => Convert.ToInt64(x["DBID"]) == dbid);
}
