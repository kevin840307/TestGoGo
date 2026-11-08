using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;
using SGO.Server.Application.Abstractions;
using SGO.Server.Protocol;
using SGO.Server.Domain.Assets;
using SGO.Server.Domain.Shops;
using SGO.Server.Domain.World;

namespace SGO.Server.Infrastructure.Persistence.Sqlite;

public sealed class SqliteGameStore : IGameStore
{
    private readonly string _connectionString;

    public SqliteGameStore(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();

        using var db = Open();

        // WAL keeps readers from blocking the single SQLite writer during
        // multiplayer bursts. Set it once at store startup because journal
        // mode is persistent for the database file; busy_timeout remains
        // connection-local in Open().
        using (var concurrency = db.CreateCommand())
        {
            concurrency.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                """;
            concurrency.ExecuteNonQuery();
        }

        SqliteSchemaMigrator.Apply(db);
    }



    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        using var pragma = db.CreateCommand();
        pragma.CommandText = """
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;
            """;
        pragma.ExecuteNonQuery();
        return db;
    }

    private static void Arg(SqliteCommand cmd, string name, object? value)
        => cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static (
        int Inventory,
        int Equip,
        int Pet,
        int HorseEquip,
        int PetSummon) ReadCharacterCapacities(
        SqliteConnection db,
        long dbid,
        SqliteTransaction? tx = null)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT inventory_capacity,
                   equip_capacity,
                   pet_capacity,
                   horse_equip_capacity,
                   pet_summon_capacity
              FROM characters
             WHERE id=$id
             LIMIT 1
            """;
        Arg(cmd, "$id", dbid);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return (200, 200, 50, 100, 1);

        return (
            Math.Max(1, reader.GetInt32(0)),
            Math.Max(1, reader.GetInt32(1)),
            Math.Max(1, reader.GetInt32(2)),
            Math.Max(1, reader.GetInt32(3)),
            Math.Clamp(reader.GetInt32(4), 1, 3));
    }

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

    private static void RepairOversizedCharacterExperience(SqliteConnection db, long accountId)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET exp_acc = 0
             WHERE account_id = $a
               AND (exp_acc > $maxInt OR exp_acc < 0)
            """;
        Arg(cmd, "$a", accountId);
        Arg(cmd, "$maxInt", int.MaxValue);
        var repaired = cmd.ExecuteNonQuery();
        if (repaired > 0)
            Console.Error.WriteLine($"[CHARACTER_EXP_REPAIRED] AccountID={accountId} Rows={repaired} ExpAccReset=0");
    }

    public List<Dictionary<string, object>> Characters(long accountId)
    {
        var list = new List<Dictionary<string, object>>();
        using var db = Open();
        RepairOversizedCharacterExperience(db, accountId);
        using var cmd = CharacterQuery(db);
        cmd.CommandText += " WHERE account_id=$a ORDER BY id LIMIT 3";
        Arg(cmd, "$a", accountId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadCharacter(r));
        r.Close();

        foreach (var character in list)
        {
            ApplyClubFields(db, character);
            ApplyTitleFields(db, character);
            ApplyTransformFields(db, character);
            ApplyTimedStatFields(db, character);
        }

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
        RepairOversizedCharacterExperience(db, accountId);
        using var cmd = CharacterQuery(db);
        cmd.CommandText += " WHERE account_id=$a AND id=$id LIMIT 1";
        Arg(cmd, "$a", accountId);
        Arg(cmd, "$id", dbid);
        using var r = cmd.ExecuteReader();
        var character = r.Read()
            ? ReadCharacter(r)
            : null;
        r.Close();

        if (character is not null)
        {
            ApplyClubFields(db, character);
            ApplyTitleFields(db, character);
            ApplyTransformFields(db, character);
            ApplyTimedStatFields(db, character);
        }

        return character;
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
                   reborn_map_id,reborn_x,reborn_y,summoned_horse_uid
            FROM characters
            """;
        return cmd;
    }

    private static int GetInt32Safe(SqliteDataReader r, int ordinal, string fieldName)
    {
        var value = r.GetInt64(ordinal);
        if (value > int.MaxValue)
        {
            Console.Error.WriteLine($"[CHARACTER_INT_OVERFLOW] Field={fieldName} Value={value} Clamped={int.MaxValue}");
            return int.MaxValue;
        }

        if (value < int.MinValue)
        {
            Console.Error.WriteLine($"[CHARACTER_INT_OVERFLOW] Field={fieldName} Value={value} Clamped={int.MinValue}");
            return int.MinValue;
        }

        return (int)value;
    }

    private static Dictionary<string, object> ReadCharacter(SqliteDataReader r)
    {
        return new Dictionary<string, object>
        {
            ["DBID"] = r.GetInt64(0),
            ["ACCID"] = r.GetInt64(1),
            ["NAME"] = r.GetString(2),
            ["CharacterPwd"] = r.GetString(3),
            ["Race"] = GetInt32Safe(r, 4, "Race"),
            ["Hair"] = GetInt32Safe(r, 5, "Hair"),
            ["HairColor"] = GetInt32Safe(r, 6, "HairColor"),
            ["Str"] = GetInt32Safe(r, 7, "Str"),
            ["Dex"] = GetInt32Safe(r, 8, "Dex"),
            ["Con"] = GetInt32Safe(r, 9, "Con"),
            ["Wis"] = GetInt32Safe(r, 10, "Wis"),
            ["Level"] = GetInt32Safe(r, 11, "Level"),
            ["MapID"] = GetInt32Safe(r, 12, "MapID"),
            ["LastMapID"] = GetInt32Safe(r, 12, "MapID"),
            ["LocX"] = r.GetDouble(13),
            ["LocY"] = r.GetDouble(14),
            ["LastLocX"] = r.GetDouble(13),
            ["LastLocY"] = r.GetDouble(14),
            ["HP"] = GetInt32Safe(r, 15, "HP"),
            ["MaxHP"] = GetInt32Safe(r, 16, "MaxHP"),
            ["MP"] = GetInt32Safe(r, 17, "MP"),
            ["MaxMP"] = GetInt32Safe(r, 18, "MaxMP"),
            ["SP"] = GetInt32Safe(r, 19, "SP"),
            ["MaxSP"] = GetInt32Safe(r, 20, "MaxSP"),
            ["Money"] = r.GetInt64(21),
            ["ExpUse"] = GetInt32Safe(r, 22, "ExpUse"),
            ["ExpAcc"] = GetInt32Safe(r, 23, "ExpAcc"),
            ["ExpDeath"] = GetInt32Safe(r, 24, "ExpDeath"),
            ["MaxExp"] = GetInt32Safe(r, 25, "MaxExp"),
            ["Career"] = GetInt32Safe(r, 26, "Career"),
            ["Job"] = GetInt32Safe(r, 27, "Job"),
            ["Attack"] = GetInt32Safe(r, 28, "Attack"),
            ["Defense"] = GetInt32Safe(r, 29, "Defense"),
            ["MagicAttack"] = GetInt32Safe(r, 30, "MagicAttack"),
            ["MagicDefense"] = GetInt32Safe(r, 31, "MagicDefense"),
            ["Hit"] = GetInt32Safe(r, 32, "Hit"),
            ["Dodge"] = GetInt32Safe(r, 33, "Dodge"),
            ["Lucky"] = GetInt32Safe(r, 34, "Lucky"),
            ["MoveSpeed"] = GetInt32Safe(r, 35, "MoveSpeed"),
            ["AttackSpeed"] = GetInt32Safe(r, 36, "AttackSpeed"),
            ["AttackRange"] = GetInt32Safe(r, 37, "AttackRange"),
            ["CritRate"] = GetInt32Safe(r, 38, "CritRate"),
            ["CritDamage"] = GetInt32Safe(r, 39, "CritDamage"),
            ["State"] = r.GetString(40),
            ["RebornMapID"] = GetInt32Safe(r, 41, "RebornMapID"),
            ["RebornX"] = r.GetDouble(42),
            ["RebornY"] = r.GetDouble(43),
            ["SummonedHorseUID"] = r.GetInt64(44),

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
            ["HorseEquipMaxSize"] = 100,
            ["SkillList"] = Array.Empty<object>(),
            ["Quests"] = Array.Empty<object>(),
            ["Shortcuts"] = Array.Empty<object>()
        };
    }

    private static void ApplyTransformFields(
        SqliteConnection db,
        Dictionary<string, object> character)
    {
        var dbid = Convert.ToInt64(
            character.GetValueOrDefault("DBID", 0L));

        character["TransformAttackBonus"] = 0;
        character["TransformDefenseBonus"] = 0;
        character["TransformMagicAttackBonus"] = 0;
        character["TransformMagicDefenseBonus"] = 0;
        character["TransformHitBonus"] = 0;
        character["TransformDodgeBonus"] = 0;
        character["TransformSourceItemID"] = 0;
        character["TransformStartedUtcMs"] = 0L;
        character["TransformExpiresUtcMs"] = 0L;

        if (dbid <= 0)
            return;

        var nowUtcMs =
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using (var cleanup = db.CreateCommand())
        {
            cleanup.CommandText = """
                DELETE FROM character_transform_state
                 WHERE character_id=$id
                   AND expires_utc_ms <= $now
                """;
            Arg(cleanup, "$id", dbid);
            Arg(cleanup, "$now", nowUtcMs);
            cleanup.ExecuteNonQuery();
        }

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT transform_type,transform_id,color_index,
                   source_item_id,started_utc_ms,expires_utc_ms,
                   attack_bonus,defense_bonus,
                   magic_attack_bonus,magic_defense_bonus,
                   hit_bonus,dodge_bonus
              FROM character_transform_state
             WHERE character_id=$id
               AND expires_utc_ms > $now
             LIMIT 1
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$now", nowUtcMs);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return;

        character["TransformType"] = reader.GetInt32(0);
        character["TransformID"] = reader.GetString(1);
        character["TransformColorIndex"] = reader.GetInt32(2);
        character["TransformSourceItemID"] = reader.GetInt32(3);
        character["TransformStartedUtcMs"] = reader.GetInt64(4);
        character["TransformExpiresUtcMs"] = reader.GetInt64(5);
        character["TransformAttackBonus"] = reader.GetInt32(6);
        character["TransformDefenseBonus"] = reader.GetInt32(7);
        character["TransformMagicAttackBonus"] = reader.GetInt32(8);
        character["TransformMagicDefenseBonus"] = reader.GetInt32(9);
        character["TransformHitBonus"] = reader.GetInt32(10);
        character["TransformDodgeBonus"] = reader.GetInt32(11);
    }

    private static void ApplyTimedStatFields(
        SqliteConnection db,
        Dictionary<string, object> character)
    {
        var dbid = Convert.ToInt64(
            character.GetValueOrDefault("DBID", 0L));

        var fields = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["stat_max_hp"] = "TimedMaxHpBonus",
            ["stat_max_mp"] = "TimedMaxMpBonus",
            ["stat_max_sp"] = "TimedMaxSpBonus",
            ["stat_attack"] = "TimedAttackBonus",
            ["stat_defense"] = "TimedDefenseBonus",
            ["stat_magic_attack"] = "TimedMagicAttackBonus",
            ["stat_magic_defense"] = "TimedMagicDefenseBonus",
            ["stat_hit"] = "TimedHitBonus",
            ["stat_dodge"] = "TimedDodgeBonus",
            ["stat_lucky"] = "TimedLuckyBonus",
            ["stat_attack_speed"] = "TimedAttackSpeedBonus",
            ["stat_move_speed"] = "TimedMoveSpeedBonus",
            ["stat_crit_rate"] = "TimedCritRateBonus",
            ["stat_crit_damage"] = "TimedCritDamageBonus"
        };

        foreach (var field in fields.Values)
            character[field] = 0;

        if (dbid <= 0)
            return;

        var nowUtcMs =
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using (var cleanup = db.CreateCommand())
        {
            cleanup.CommandText = """
                DELETE FROM character_timed_effects
                 WHERE character_id=$id
                   AND expires_utc_ms <= $now
                """;
            Arg(cleanup, "$id", dbid);
            Arg(cleanup, "$now", nowUtcMs);
            cleanup.ExecuteNonQuery();
        }

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT effect_key,effect_value
              FROM character_timed_effects
             WHERE character_id=$id
               AND expires_utc_ms > $now
               AND effect_key LIKE 'stat_%'
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$now", nowUtcMs);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            if (!fields.TryGetValue(key, out var target))
                continue;

            character[target] =
                Convert.ToInt32(character[target]) +
                reader.GetInt32(1);
        }
    }

    private static void ApplyTitleFields(
        SqliteConnection db,
        Dictionary<string, object> character)
    {
        var dbid = Convert.ToInt64(
            character.GetValueOrDefault("DBID", 0L));

        if (dbid <= 0)
            return;

        using (var state = db.CreateCommand())
        {
            state.CommandText = """
                SELECT current_title
                  FROM character_title_state
                 WHERE character_id=$id
                 LIMIT 1
                """;
            Arg(state, "$id", dbid);

            character["Title"] = Convert.ToInt32(
                state.ExecuteScalar() ?? 0);
        }

        var unlocked = new List<int>();
        using (var titles = db.CreateCommand())
        {
            titles.CommandText = """
                SELECT title_id
                  FROM character_titles
                 WHERE character_id=$id
                 ORDER BY title_id
                """;
            Arg(titles, "$id", dbid);

            using var reader = titles.ExecuteReader();
            while (reader.Read())
                unlocked.Add(reader.GetInt32(0));
        }

        character["UnlockedTitles"] = unlocked.ToArray();
    }

    private static void ApplyClubFields(
        SqliteConnection db,
        Dictionary<string, object> character)
    {
        var dbid = Convert.ToInt64(
            character.GetValueOrDefault("DBID", 0L));

        if (dbid <= 0)
            return;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT c.id,c.name,m.position,m.donation
              FROM club_members m
              JOIN clubs c ON c.id=m.club_id
             WHERE m.character_id=$id
             LIMIT 1
            """;
        Arg(cmd, "$id", dbid);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return;

        var clubId = reader.GetInt64(0);
        character["ClubID"] = clubId;
        character["ClubName"] = reader.GetString(1);
        character["ClubPosition"] = reader.GetInt32(2);
        character["ClubDonation"] = reader.GetInt64(3);
        reader.Close();

        using var tech = db.CreateCommand();
        tech.CommandText = """
            SELECT
                COALESCE(MAX(CASE WHEN research_id=2 THEN level END),0),
                COALESCE(MAX(CASE WHEN research_id=3 THEN level END),0),
                COALESCE(MAX(CASE WHEN research_id=4 THEN level END),0),
                COALESCE(MAX(CASE WHEN research_id=5 THEN level END),0),
                COALESCE(MAX(CASE WHEN research_id=6 THEN level END),0),
                COALESCE(MAX(CASE WHEN research_id=7 THEN level END),0),
                COALESCE(MAX(CASE WHEN research_id=8 THEN level END),0),
                COALESCE(MAX(CASE WHEN research_id=9 THEN level END),0),
                COALESCE(MAX(CASE WHEN research_id=10 THEN level END),0),
                COALESCE(MAX(CASE WHEN research_id=11 THEN level END),0)
              FROM club_research
             WHERE club_id=$club
            """;
        Arg(tech, "$club", clubId);

        using var levels = tech.ExecuteReader();
        if (!levels.Read())
            return;

        character["ClubTechAttack"] = levels.GetInt32(0) * 10;
        character["ClubTechDefense"] = levels.GetInt32(1) * 10;
        character["ClubTechMagicAttack"] = levels.GetInt32(2) * 10;
        character["ClubTechMagicDefense"] = levels.GetInt32(3) * 10;
        character["ClubTechHit"] = levels.GetInt32(4) * 5;
        character["ClubTechDodge"] = levels.GetInt32(5) * 5;
        character["ClubTechMoveSpeed"] = levels.GetInt32(6) * 5;
        character["ClubTechAttackSpeed"] = levels.GetInt32(7) * 5;
        character["ClubTechCritRate"] = levels.GetInt32(8) * 3;
        character["ClubTechCritDamage"] = levels.GetInt32(9) * 5;
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
            INSERT INTO characters(account_id,name,password,race,hair,hair_color,str,dex,con,wis,money)
            VALUES($a,$n,$p,$r,$h,$c,$s,$d,$co,$w,10000)
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

    public CharacterAssets GetCharacterAssets(long accountId, long dbid)
    {
        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid))
            return new CharacterAssets();

        var capacities = ReadCharacterCapacities(
            db,
            dbid);
        var assets = new CharacterAssets
        {
            InventoryMaxSize = capacities.Inventory,
            EquipMaxSize = capacities.Equip,
            PetMaxSize = capacities.Pet,
            PetSummonMax = capacities.PetSummon,
            HorseEquipMaxSize = capacities.HorseEquip
        };

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT uid,item_index,count,in_bank,in_shop
                  FROM inventory_items
                 WHERE character_id=$id
                 ORDER BY uid
                """;
            Arg(cmd, "$id", dbid);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                assets.Inventory.Add(new Dictionary<string, object>
                {
                    ["ItemUID"] = r.GetInt64(0),
                    ["ItemIndex"] = r.GetInt32(1),
                    ["Count"] = r.GetInt32(2),
                    ["InBank"] = r.GetInt32(3),
                    ["InShop"] = r.GetInt32(4)
                });
            }
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT uid,item_index,damage,color,part,in_bank,in_shop
                  FROM equipment_items
                 WHERE character_id=$id
                 ORDER BY uid
                """;
            Arg(cmd, "$id", dbid);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                assets.Equips.Add(new Dictionary<string, object>
                {
                    ["ItemID"] = r.GetInt64(0),
                    ["ItemIdx"] = r.GetInt32(1),
                    ["Damage"] = r.GetInt32(2),
                    ["Color"] = r.GetInt32(3),
                    ["Part"] = r.GetInt32(4),
                    ["InBank"] = r.GetInt32(5),
                    ["InShop"] = r.GetInt32(6)
                });
            }
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT uid,pet_id,name,hp,max_hp,mp,max_mp,attack,defense,
                       magic_attack,magic_defense,hit,dodge,con,str,wis,dex,
                       extra_str,extra_dex,extra_con,extra_wis,life_count,
                       recovery_count,summon_wait_count,exp,max_exp,level,
                       use_exp,feed_exp_limit,evol_id,equip0,equip1,equip2,
                       feed_value,state,main_skill_id,in_shop,shop_price
                  FROM pets
                 WHERE character_id=$id
                 ORDER BY uid
                """;
            Arg(cmd, "$id", dbid);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                assets.Pets.Add(new Dictionary<string, object>
                {
                    ["UID"] = r.GetInt64(0),
                    ["PETID"] = r.GetInt32(1),
                    ["Name"] = r.GetString(2),
                    ["HP"] = r.GetInt32(3),
                    ["MaxHP"] = r.GetInt32(4),
                    ["MP"] = r.GetInt32(5),
                    ["MaxMP"] = r.GetInt32(6),
                    ["Attack"] = r.GetInt32(7),
                    ["Defense"] = r.GetInt32(8),
                    ["MagicAttack"] = r.GetInt32(9),
                    ["MagicDefense"] = r.GetInt32(10),
                    ["Hit"] = r.GetInt32(11),
                    ["Dodge"] = r.GetInt32(12),
                    ["CON"] = r.GetInt32(13),
                    ["STR"] = r.GetInt32(14),
                    ["WIS"] = r.GetInt32(15),
                    ["DEX"] = r.GetInt32(16),
                    ["ExtraSTR"] = r.GetInt32(17),
                    ["ExtraDEX"] = r.GetInt32(18),
                    ["ExtraCON"] = r.GetInt32(19),
                    ["ExtraWIS"] = r.GetInt32(20),
                    ["LifeCount"] = r.GetInt32(21),
                    ["RecoveryCount"] = r.GetInt32(22),
                    ["SummonWaitCount"] = r.GetInt32(23),
                    ["Exp"] = r.GetInt32(24),
                    ["MaxExp"] = r.GetInt32(25),
                    ["Level"] = r.GetInt32(26),
                    ["UseExp"] = r.GetInt32(27),
                    ["FeedExpLimit"] = r.GetInt32(28),
                    ["EvolID"] = r.GetInt32(29),
                    ["Equip0"] = r.GetInt64(30),
                    ["Equip1"] = r.GetInt64(31),
                    ["Equip2"] = r.GetInt64(32),
                    ["FeedValue"] = r.GetInt32(33),
                    ["State"] = r.GetInt32(34),
                    ["MainSkillID"] = r.GetInt32(35),
                    ["InShop"] = r.GetInt32(36),
                    ["ShopPrice"] = r.GetInt32(37)
                });
            }
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT uid,horse_index,exp,max_exp,energy_left,level,name,
                       horse_type,equip_color0,equip_color1,equip_color2,
                       equip_color3,angry
                  FROM horses
                 WHERE character_id=$id
                 ORDER BY uid
                """;
            Arg(cmd, "$id", dbid);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                assets.Horses.Add(new Dictionary<string, object>
                {
                    ["HorseUID"] = r.GetInt64(0),
                    ["HorseIdx"] = r.GetInt32(1),
                    ["Exp"] = r.GetInt32(2),
                    ["MaxExp"] = r.GetInt32(3),
                    ["EnergyLeft"] = r.GetInt32(4),
                    ["Level"] = r.GetInt32(5),
                    ["Name"] = r.GetString(6),
                    ["HorseType"] = r.GetString(7),
                    ["EquipColor0"] = r.GetInt32(8),
                    ["EquipColor1"] = r.GetInt32(9),
                    ["EquipColor2"] = r.GetInt32(10),
                    ["EquipColor3"] = r.GetInt32(11),
                    ["Angry"] = r.GetInt32(12)
                });
            }
        }

        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT uid,equip_index,damage,color,horse_uid,part,in_bank,in_shop
                  FROM horse_equipment
                 WHERE character_id=$id
                 ORDER BY uid
                """;
            Arg(cmd, "$id", dbid);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                assets.HorseEquips.Add(new Dictionary<string, object>
                {
                    ["EquipUID"] = r.GetInt64(0),
                    ["EquipIdx"] = r.GetInt32(1),
                    ["Damage"] = r.GetInt32(2),
                    ["Color"] = r.GetInt32(3),
                    ["HorseUID"] = r.GetInt64(4),
                    ["Part"] = r.GetInt32(5),
                    ["InBank"] = r.GetInt32(6),
                    ["InShop"] = r.GetInt32(7)
                });
            }
        }

        return assets;
    }

    public int GetQuestFlag(
        long accountId,
        long dbid,
        string flagName)
    {
        if (string.IsNullOrWhiteSpace(flagName))
            return 0;

        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid))
            return 0;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT flag_value
              FROM character_quest_flags
             WHERE character_id=$id
               AND flag_name=$name
             LIMIT 1
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$name", flagName.Trim());

        var raw = cmd.ExecuteScalar();
        return raw is null || raw == DBNull.Value
            ? 0
            : Convert.ToInt32(raw);
    }

    public bool SetQuestFlag(
        long accountId,
        long dbid,
        string flagName,
        int value)
    {
        if (string.IsNullOrWhiteSpace(flagName) ||
            flagName.Length > 128)
            return false;

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return false;

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO character_quest_flags(
                character_id,flag_name,flag_value)
            VALUES($id,$name,$value)
            ON CONFLICT(character_id,flag_name)
            DO UPDATE SET flag_value=excluded.flag_value
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$name", flagName.Trim());
        Arg(cmd, "$value", value);

        if (cmd.ExecuteNonQuery() != 1)
            return false;

        tx.Commit();
        return true;
    }

    public IReadOnlyList<Dictionary<string, object>> GetQuests(
        long accountId,
        long dbid)
    {
        var result =
            new List<Dictionary<string, object>>();

        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid))
            return result;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT quest_id,status,current_step,step_progress
              FROM character_quests
             WHERE character_id=$id
             ORDER BY quest_id
            """;
        Arg(cmd, "$id", dbid);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new Dictionary<string, object>
            {
                ["QuestID"] = reader.GetInt32(0),
                ["Status"] = reader.GetInt32(1),
                ["CurrentStep"] = reader.GetInt32(2),
                ["StepProgress"] = reader.GetInt32(3)
            });
        }

        return result;
    }

    public bool UpsertQuest(
        long accountId,
        long dbid,
        int questId,
        int status,
        int currentStep,
        int stepProgress)
    {
        if (questId <= 0)
            return false;

        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid))
            return false;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO character_quests(
                character_id,quest_id,status,current_step,
                step_progress,updated_utc_ms)
            VALUES(
                $id,$quest,$status,$step,$progress,$updated)
            ON CONFLICT(character_id,quest_id)
            DO UPDATE SET
                status=excluded.status,
                current_step=excluded.current_step,
                step_progress=excluded.step_progress,
                updated_utc_ms=excluded.updated_utc_ms
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$quest", questId);
        Arg(cmd, "$status", Math.Clamp(status, 0, 3));
        Arg(cmd, "$step", Math.Max(0, currentStep));
        Arg(cmd, "$progress", Math.Max(0, stepProgress));
        Arg(cmd, "$updated",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        return cmd.ExecuteNonQuery() == 1;
    }

    public bool AbandonQuest(
        long accountId,
        long dbid,
        int questId)
    {
        if (questId <= 0)
            return false;

        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid))
            return false;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            DELETE FROM character_quests
             WHERE character_id=$id
               AND quest_id=$quest
               AND status IN (1,2)
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$quest", questId);
        return cmd.ExecuteNonQuery() == 1;
    }

    public long GetMallPoints(long accountId)
    {
        if (accountId <= 0)
            return 0;

        using var db = Open();
        EnsureMallAccount(db, accountId);

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT points
              FROM account_mall_points
             WHERE account_id=$account
             LIMIT 1
            """;
        Arg(cmd, "$account", accountId);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    public bool AdjustMallPoints(
        long accountId,
        long delta,
        out long newBalance)
    {
        newBalance = GetMallPoints(accountId);

        using var db = Open();
        using var tx = db.BeginTransaction();

        using var update = db.CreateCommand();
        update.Transaction = tx;
        update.CommandText = """
            UPDATE account_mall_points
               SET points=points+$delta
             WHERE account_id=$account
               AND points+$delta >= 0
            """;
        Arg(update, "$delta", delta);
        Arg(update, "$account", accountId);

        if (update.ExecuteNonQuery() != 1)
            return false;

        using var read = db.CreateCommand();
        read.Transaction = tx;
        read.CommandText = """
            SELECT points
              FROM account_mall_points
             WHERE account_id=$account
            """;
        Arg(read, "$account", accountId);
        newBalance = Convert.ToInt64(
            read.ExecuteScalar() ?? 0L);

        tx.Commit();
        return true;
    }

    public long GetLifetimeDonation(long accountId)
    {
        if (accountId <= 0)
            return 0;

        using var db = Open();
        EnsureMallAccount(db, accountId);

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT lifetime_donation
              FROM account_mall_points
             WHERE account_id=$account
             LIMIT 1
            """;
        Arg(cmd, "$account", accountId);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    public bool AdjustLifetimeDonation(
        long accountId,
        long delta,
        out long newTotal)
    {
        newTotal = GetLifetimeDonation(accountId);

        using var db = Open();
        using var tx = db.BeginTransaction();

        using var update = db.CreateCommand();
        update.Transaction = tx;
        update.CommandText = """
            UPDATE account_mall_points
               SET lifetime_donation=lifetime_donation+$delta
             WHERE account_id=$account
               AND lifetime_donation+$delta >= 0
            """;
        Arg(update, "$delta", delta);
        Arg(update, "$account", accountId);

        if (update.ExecuteNonQuery() != 1)
            return false;

        using var read = db.CreateCommand();
        read.Transaction = tx;
        read.CommandText = """
            SELECT lifetime_donation
              FROM account_mall_points
             WHERE account_id=$account
            """;
        Arg(read, "$account", accountId);
        newTotal = Convert.ToInt64(
            read.ExecuteScalar() ?? 0L);

        tx.Commit();
        return true;
    }

    public ExternalMallCreditResult CreditExternalMallPoints(
        long accountId,
        string source,
        string externalTransactionId,
        long points,
        long lifetimeDonation)
    {
        source =
            source.Trim().ToLowerInvariant();
        externalTransactionId =
            externalTransactionId.Trim();

        if (accountId <= 0 ||
            string.IsNullOrWhiteSpace(source) ||
            source.Length > 64 ||
            string.IsNullOrWhiteSpace(
                externalTransactionId) ||
            externalTransactionId.Length > 200 ||
            points < 0 ||
            lifetimeDonation < 0 ||
            points > 9_999_999_999L ||
            lifetimeDonation > 9_999_999_999L ||
            (points == 0 &&
             lifetimeDonation == 0))
        {
            return new(
                false,
                false,
                "外部點數入帳資料無效",
                GetMallPoints(accountId),
                GetLifetimeDonation(accountId));
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        EnsureMallAccount(
            db,
            accountId,
            tx);

        using (var account = db.CreateCommand())
        {
            account.Transaction = tx;
            account.CommandText = """
                SELECT COUNT(*)
                  FROM account_mall_points
                 WHERE account_id=$account
                """;
            Arg(account, "$account", accountId);

            if (Convert.ToInt32(
                    account.ExecuteScalar() ?? 0) != 1)
            {
                return new(
                    false,
                    false,
                    "本服商城帳號不存在",
                    0,
                    0);
            }
        }

        long ReadBalance(string column)
        {
            using var read = db.CreateCommand();
            read.Transaction = tx;
            read.CommandText =
                $"SELECT {column} FROM account_mall_points WHERE account_id=$account";
            Arg(read, "$account", accountId);
            return Convert.ToInt64(
                read.ExecuteScalar() ?? 0L);
        }

        using var insert = db.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT OR IGNORE INTO external_mall_credits(
                source,external_transaction_id,account_id,
                points,lifetime_donation,created_utc_ms)
            VALUES(
                $source,$tx,$account,$points,$donation,$created)
            """;
        Arg(insert, "$source", source);
        Arg(insert, "$tx", externalTransactionId);
        Arg(insert, "$account", accountId);
        Arg(insert, "$points", points);
        Arg(insert, "$donation", lifetimeDonation);
        Arg(
            insert,
            "$created",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var inserted =
            insert.ExecuteNonQuery() == 1;

        if (!inserted)
        {
            long existingAccountId;
            long existingPoints;
            long existingDonation;

            using (var existing = db.CreateCommand())
            {
                existing.Transaction = tx;
                existing.CommandText = """
                    SELECT account_id,points,lifetime_donation
                      FROM external_mall_credits
                     WHERE source=$source
                       AND external_transaction_id=$tx
                    """;
                Arg(existing, "$source", source);
                Arg(existing, "$tx", externalTransactionId);

                using var reader = existing.ExecuteReader();
                if (!reader.Read())
                {
                    return new(
                        false,
                        false,
                        "找不到既有外部交易紀錄",
                        0,
                        0);
                }

                existingAccountId =
                    reader.GetInt64(0);
                existingPoints =
                    reader.GetInt64(1);
                existingDonation =
                    reader.GetInt64(2);
            }

            var currentPoints =
                ReadBalance("points");
            var currentDonation =
                ReadBalance("lifetime_donation");

            if (existingAccountId != accountId ||
                existingPoints != points ||
                existingDonation != lifetimeDonation)
            {
                return new(
                    false,
                    false,
                    "外部交易編號與既有入帳資料衝突",
                    currentPoints,
                    currentDonation);
            }

            tx.Commit();
            return new(
                true,
                true,
                "外部交易已入帳，未重複加點",
                currentPoints,
                currentDonation);
        }

        using (var update = db.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = """
                UPDATE account_mall_points
                   SET points=points+$points,
                       lifetime_donation=
                           lifetime_donation+$donation
                 WHERE account_id=$account
                """;
            Arg(update, "$points", points);
            Arg(update, "$donation", lifetimeDonation);
            Arg(update, "$account", accountId);

            if (update.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    false,
                    "外部點數入帳失敗",
                    0,
                    0);
            }
        }

        var newBalance =
            ReadBalance("points");
        var newDonation =
            ReadBalance("lifetime_donation");

        tx.Commit();

        return new(
            true,
            false,
            "外部交易入帳成功",
            newBalance,
            newDonation);
    }

    public IReadOnlySet<int> GetClaimedMallGiftIds(
        long accountId)
    {
        if (accountId <= 0)
            return new HashSet<int>();

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT reward_id
              FROM account_mall_gift_claims
             WHERE account_id=$account
            """;
        Arg(cmd, "$account", accountId);

        var ids = new HashSet<int>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            ids.Add(reader.GetInt32(0));
        return ids;
    }

    public MallPurchaseResult PurchaseMallAsset(
        long accountId,
        long dbid,
        AssetPurchaseKind kind,
        int itemIndex,
        int assetCount,
        long totalPrice,
        string assetName = "",
        string horseType = "MS",
        int initialEnergy = 0,
        int itemPart = 255,
        int itemDamage = 0)
    {
        if (accountId <= 0 ||
            dbid <= 0 ||
            itemIndex <= 0 ||
            assetCount <= 0 ||
            assetCount > 999 ||
            totalPrice < 0)
        {
            return new(
                false,
                "商城購買資料無效",
                GetMallPoints(accountId));
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在", 0);

        EnsureMallAccount(db, accountId, tx);

        long currentPoints;
        using (var points = db.CreateCommand())
        {
            points.Transaction = tx;
            points.CommandText = """
                SELECT points
                  FROM account_mall_points
                 WHERE account_id=$account
                 LIMIT 1
                """;
            Arg(points, "$account", accountId);
            var raw = points.ExecuteScalar();
            if (raw is null || raw == DBNull.Value)
                return new(false, "商城點數帳戶不存在", 0);
            currentPoints = Convert.ToInt64(raw);
        }

        if (currentPoints < totalPrice)
            return new(false, "商城點數不足", currentPoints);

        if (!TryGrantMallAsset(
                db,
                tx,
                dbid,
                kind,
                itemIndex,
                assetCount,
                assetName,
                horseType,
                initialEnergy,
                itemPart,
                itemDamage,
                out var grantError))
        {
            return new(false, grantError, currentPoints);
        }

        using (var debit = db.CreateCommand())
        {
            debit.Transaction = tx;
            debit.CommandText = """
                UPDATE account_mall_points
                   SET points=points-$price
                 WHERE account_id=$account
                   AND points >= $price
                """;
            Arg(debit, "$price", totalPrice);
            Arg(debit, "$account", accountId);
            if (debit.ExecuteNonQuery() != 1)
                return new(false, "商城點數不足", currentPoints);
        }

        var newBalance = currentPoints - totalPrice;
        tx.Commit();
        return new(true, "商城購買成功", newBalance);
    }

    public MallGiftClaimResult ClaimMallGift(
        long accountId,
        long dbid,
        int rewardId,
        long requiredDonation,
        AssetPurchaseKind kind,
        int itemIndex,
        int assetCount,
        string assetName = "",
        string horseType = "MS",
        int initialEnergy = 0,
        int itemPart = 255,
        int itemDamage = 0)
    {
        if (accountId <= 0 ||
            dbid <= 0 ||
            rewardId <= 0 ||
            requiredDonation <= 0 ||
            itemIndex <= 0 ||
            assetCount <= 0 ||
            assetCount > 999)
        {
            return new(
                false,
                "贊助滿額禮資料無效",
                GetLifetimeDonation(accountId));
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在", 0);

        EnsureMallAccount(db, accountId, tx);

        long lifetimeDonation;
        using (var read = db.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = """
                SELECT lifetime_donation
                  FROM account_mall_points
                 WHERE account_id=$account
                 LIMIT 1
                """;
            Arg(read, "$account", accountId);
            lifetimeDonation = Convert.ToInt64(
                read.ExecuteScalar() ?? 0L);
        }

        if (lifetimeDonation < requiredDonation)
        {
            return new(
                false,
                $"累積贊助尚未達到 {requiredDonation} 點",
                lifetimeDonation);
        }

        using (var claim = db.CreateCommand())
        {
            claim.Transaction = tx;
            claim.CommandText = """
                INSERT OR IGNORE INTO account_mall_gift_claims(
                    account_id,reward_id,character_id,claimed_utc_ms)
                VALUES($account,$reward,$character,$now)
                """;
            Arg(claim, "$account", accountId);
            Arg(claim, "$reward", rewardId);
            Arg(claim, "$character", dbid);
            Arg(
                claim,
                "$now",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            if (claim.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    "此贊助滿額禮已領取",
                    lifetimeDonation);
            }
        }

        if (!TryGrantMallAsset(
                db,
                tx,
                dbid,
                kind,
                itemIndex,
                assetCount,
                assetName,
                horseType,
                initialEnergy,
                itemPart,
                itemDamage,
                out var grantError))
        {
            return new(false, grantError, lifetimeDonation);
        }

        tx.Commit();
        return new(
            true,
            "贊助滿額禮領取成功",
            lifetimeDonation);
    }

    private static void EnsureMallAccount(
        SqliteConnection db,
        long accountId,
        SqliteTransaction? tx = null)
    {
        using var ensure = db.CreateCommand();
        ensure.Transaction = tx;
        ensure.CommandText = """
            INSERT INTO account_mall_points(
                account_id,points,lifetime_donation)
            SELECT id,0,0
              FROM accounts
             WHERE id=$account
            ON CONFLICT(account_id) DO NOTHING
            """;
        Arg(ensure, "$account", accountId);
        ensure.ExecuteNonQuery();
    }

    private static bool TryGrantMallAsset(
        SqliteConnection db,
        SqliteTransaction tx,
        long dbid,
        AssetPurchaseKind kind,
        int itemIndex,
        int assetCount,
        string assetName,
        string horseType,
        int initialEnergy,
        int itemPart,
        int itemDamage,
        out string error)
    {
        error = "";

        int CountRows(string table, string extra = "")
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                $"SELECT COUNT(*) FROM {table} WHERE character_id=$id {extra}";
            Arg(cmd, "$id", dbid);
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }

        switch (kind)
        {
            case AssetPurchaseKind.General:
            case AssetPurchaseKind.HorseBattery:
            {
                long existingUid = 0;
                int existingCount = 0;
                using (var existing = db.CreateCommand())
                {
                    existing.Transaction = tx;
                    existing.CommandText = """
                        SELECT uid,count
                          FROM inventory_items
                         WHERE character_id=$id
                           AND item_index=$item
                           AND in_bank=0
                           AND in_shop=0
                         ORDER BY uid
                         LIMIT 1
                        """;
                    Arg(existing, "$id", dbid);
                    Arg(existing, "$item", itemIndex);
                    using var reader = existing.ExecuteReader();
                    if (reader.Read())
                    {
                        existingUid = reader.GetInt64(0);
                        existingCount = reader.GetInt32(1);
                    }
                }

                if (existingUid > 0)
                {
                    if (existingCount + assetCount > 999)
                    {
                        error = "物品疊加數量將超過 999";
                        return false;
                    }

                    using var add = db.CreateCommand();
                    add.Transaction = tx;
                    add.CommandText = """
                        UPDATE inventory_items
                           SET count=count+$count
                         WHERE uid=$uid
                           AND character_id=$id
                        """;
                    Arg(add, "$count", assetCount);
                    Arg(add, "$uid", existingUid);
                    Arg(add, "$id", dbid);
                    if (add.ExecuteNonQuery() != 1)
                    {
                        error = "物品發放失敗";
                        return false;
                    }
                }
                else
                {
                    if (CountRows(
                            "inventory_items",
                            "AND in_bank=0 AND in_shop=0") >=
                        ReadCharacterCapacities(db, dbid, tx).Inventory)
                    {
                        error = "背包空間不足";
                        return false;
                    }

                    using var add = db.CreateCommand();
                    add.Transaction = tx;
                    add.CommandText = """
                        INSERT INTO inventory_items(
                            character_id,item_index,count,in_bank,in_shop)
                        VALUES($id,$item,$count,0,0)
                        """;
                    Arg(add, "$id", dbid);
                    Arg(add, "$item", itemIndex);
                    Arg(add, "$count", assetCount);
                    if (add.ExecuteNonQuery() != 1)
                    {
                        error = "物品發放失敗";
                        return false;
                    }
                }

                return true;
            }

            case AssetPurchaseKind.Equipment:
            {
                if (CountRows("equipment_items") + assetCount >
                    ReadCharacterCapacities(db, dbid, tx).Equip)
                {
                    error = "裝備欄空間不足";
                    return false;
                }

                for (var i = 0; i < assetCount; i++)
                {
                    using var add = db.CreateCommand();
                    add.Transaction = tx;
                    add.CommandText = """
                        INSERT INTO equipment_items(
                            character_id,item_index,damage,color,part,in_bank,in_shop)
                        VALUES($id,$item,$damage,0,255,0,0)
                        """;
                    Arg(add, "$id", dbid);
                    Arg(add, "$item", itemIndex);
                    Arg(add, "$damage", Math.Max(0, itemDamage));
                    if (add.ExecuteNonQuery() != 1)
                    {
                        error = "裝備發放失敗";
                        return false;
                    }
                }

                return true;
            }

            case AssetPurchaseKind.Pet:
            {
                if (CountRows("pets") + assetCount >
                    ReadCharacterCapacities(
                        db,
                        dbid,
                        tx).Pet)
                {
                    error = "寵物欄空間不足";
                    return false;
                }

                var name = string.IsNullOrWhiteSpace(assetName)
                    ? $"Pet {itemIndex}"
                    : assetName.Trim();

                for (var i = 0; i < assetCount; i++)
                {
                    using var add = db.CreateCommand();
                    add.Transaction = tx;
                    add.CommandText = """
                        INSERT INTO pets(
                            character_id,pet_id,name,
                            life_count,feed_exp_limit,feed_value,summon_wait_count)
                        VALUES(
                            $id,$pet,$name,
                            10,43200,43200,0)
                        """;
                    Arg(add, "$id", dbid);
                    Arg(add, "$pet", itemIndex);
                    Arg(add, "$name", name);
                    if (add.ExecuteNonQuery() != 1)
                    {
                        error = "寵物發放失敗";
                        return false;
                    }
                }

                return true;
            }

            case AssetPurchaseKind.Horse:
            {
                if (CountRows("horses") + assetCount > 50)
                {
                    error = "機甲欄空間不足";
                    return false;
                }

                var name = string.IsNullOrWhiteSpace(assetName)
                    ? $"Mecha {itemIndex}"
                    : assetName.Trim();
                var type = string.IsNullOrWhiteSpace(horseType)
                    ? "MS"
                    : horseType.Trim();

                for (var i = 0; i < assetCount; i++)
                {
                    using var add = db.CreateCommand();
                    add.Transaction = tx;
                    add.CommandText = """
                        INSERT INTO horses(
                            character_id,horse_index,name,horse_type,energy_left)
                        VALUES($id,$item,$name,$type,$energy)
                        """;
                    Arg(add, "$id", dbid);
                    Arg(add, "$item", itemIndex);
                    Arg(add, "$name", name);
                    Arg(add, "$type", type);
                    Arg(add, "$energy", Math.Max(0, initialEnergy));
                    if (add.ExecuteNonQuery() != 1)
                    {
                        error = "機甲發放失敗";
                        return false;
                    }
                }

                return true;
            }

            case AssetPurchaseKind.HorseEquipment:
            {
                if (CountRows("horse_equipment") + assetCount >
                    ReadCharacterCapacities(
                        db,
                        dbid,
                        tx).HorseEquip)
                {
                    error = "機甲裝備欄空間不足";
                    return false;
                }

                var part = itemPart is >= 0 and <= 3
                    ? itemPart
                    : ResolveHorseEquipPart(itemIndex);

                for (var i = 0; i < assetCount; i++)
                {
                    using var add = db.CreateCommand();
                    add.Transaction = tx;
                    add.CommandText = """
                        INSERT INTO horse_equipment(
                            character_id,equip_index,horse_uid,
                            damage,color,part,in_bank,in_shop)
                        VALUES($id,$item,-1,$damage,0,$part,0,0)
                        """;
                    Arg(add, "$id", dbid);
                    Arg(add, "$item", itemIndex);
                    Arg(add, "$damage", Math.Max(0, itemDamage));
                    Arg(add, "$part", part);
                    if (add.ExecuteNonQuery() != 1)
                    {
                        error = "機甲裝備發放失敗";
                        return false;
                    }
                }

                return true;
            }

            default:
                error = "不支援的資產類型";
                return false;
        }
    }

    public CharacterTransformMutationResult ActivateTransformFromItem(
        long accountId,
        long dbid,
        long itemUid,
        int itemIndex,
        CharacterTransformState state)
    {
        if (accountId <= 0 ||
            dbid <= 0 ||
            itemUid <= 0 ||
            itemIndex <= 0 ||
            state.TransformType <= 0 ||
            string.IsNullOrWhiteSpace(state.TransformId) ||
            state.ExpiresUtcMs <= state.StartedUtcMs)
        {
            return new(false, "變身資料無效", null);
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在", null);

        using (var item = db.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = """
                SELECT count
                  FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND item_index=$item
                   AND in_bank=0
                   AND in_shop=0
                 LIMIT 1
                """;
            Arg(item, "$uid", itemUid);
            Arg(item, "$id", dbid);
            Arg(item, "$item", itemIndex);

            var raw = item.ExecuteScalar();
            if (raw is null ||
                raw == DBNull.Value ||
                Convert.ToInt32(raw) <= 0)
            {
                return new(false, "找不到可使用的變身卷軸", null);
            }
        }

        using (var upsert = db.CreateCommand())
        {
            upsert.Transaction = tx;
            upsert.CommandText = """
                INSERT INTO character_transform_state(
                    character_id,transform_type,transform_id,color_index,
                    source_item_id,started_utc_ms,expires_utc_ms,
                    attack_bonus,defense_bonus,
                    magic_attack_bonus,magic_defense_bonus,
                    hit_bonus,dodge_bonus)
                VALUES(
                    $id,$type,$transform,$color,
                    $source,$started,$expires,
                    $attack,$defense,
                    $magicAttack,$magicDefense,
                    $hit,$dodge)
                ON CONFLICT(character_id)
                DO UPDATE SET
                    transform_type=excluded.transform_type,
                    transform_id=excluded.transform_id,
                    color_index=excluded.color_index,
                    source_item_id=excluded.source_item_id,
                    started_utc_ms=excluded.started_utc_ms,
                    expires_utc_ms=excluded.expires_utc_ms,
                    attack_bonus=excluded.attack_bonus,
                    defense_bonus=excluded.defense_bonus,
                    magic_attack_bonus=excluded.magic_attack_bonus,
                    magic_defense_bonus=excluded.magic_defense_bonus,
                    hit_bonus=excluded.hit_bonus,
                    dodge_bonus=excluded.dodge_bonus
                """;
            Arg(upsert, "$id", dbid);
            Arg(upsert, "$type", state.TransformType);
            Arg(upsert, "$transform", state.TransformId.Trim());
            Arg(upsert, "$color", Math.Max(0, state.ColorIndex));
            Arg(upsert, "$source", state.SourceItemId);
            Arg(upsert, "$started", state.StartedUtcMs);
            Arg(upsert, "$expires", state.ExpiresUtcMs);
            Arg(upsert, "$attack", Math.Max(0, state.AttackBonus));
            Arg(upsert, "$defense", Math.Max(0, state.DefenseBonus));
            Arg(upsert, "$magicAttack", Math.Max(0, state.MagicAttackBonus));
            Arg(upsert, "$magicDefense", Math.Max(0, state.MagicDefenseBonus));
            Arg(upsert, "$hit", Math.Max(0, state.HitBonus));
            Arg(upsert, "$dodge", Math.Max(0, state.DodgeBonus));

            if (upsert.ExecuteNonQuery() != 1)
                return new(false, "變身狀態儲存失敗", null);
        }

        ConsumeInventoryItem(
            db,
            tx,
            dbid,
            itemUid);

        tx.Commit();
        return new(true, "變身成功", state);
    }

    public CharacterTransformMutationResult ClearTransformFromItem(
        long accountId,
        long dbid,
        long itemUid,
        int itemIndex)
    {
        if (accountId <= 0 ||
            dbid <= 0 ||
            itemUid <= 0 ||
            itemIndex <= 0)
        {
            return new(false, "還原資料無效", null);
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在", null);

        var nowUtcMs =
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using (var active = db.CreateCommand())
        {
            active.Transaction = tx;
            active.CommandText = """
                SELECT 1
                  FROM character_transform_state
                 WHERE character_id=$id
                   AND expires_utc_ms > $now
                 LIMIT 1
                """;
            Arg(active, "$id", dbid);
            Arg(active, "$now", nowUtcMs);
            if (active.ExecuteScalar() is null)
                return new(false, "目前沒有可解除的變身效果", null);
        }

        using (var item = db.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = """
                SELECT count
                  FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND item_index=$item
                   AND in_bank=0
                   AND in_shop=0
                 LIMIT 1
                """;
            Arg(item, "$uid", itemUid);
            Arg(item, "$id", dbid);
            Arg(item, "$item", itemIndex);

            var raw = item.ExecuteScalar();
            if (raw is null ||
                raw == DBNull.Value ||
                Convert.ToInt32(raw) <= 0)
            {
                return new(false, "找不到可使用的還原卷軸", null);
            }
        }

        using (var delete = db.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = """
                DELETE FROM character_transform_state
                 WHERE character_id=$id
                """;
            Arg(delete, "$id", dbid);
            if (delete.ExecuteNonQuery() != 1)
                return new(false, "解除變身失敗", null);
        }

        ConsumeInventoryItem(
            db,
            tx,
            dbid,
            itemUid);

        tx.Commit();
        return new(true, "已恢復原本外觀", null);
    }

    public IReadOnlyList<CharacterCosmeticOverride> GetCosmeticOverrides(
        long accountId,
        long dbid)
    {
        using var db = Open();

        if (!OwnsCharacter(
                db,
                accountId,
                dbid))
        {
            return Array.Empty<CharacterCosmeticOverride>();
        }

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT part,item_index,color,source_item_id
              FROM character_cosmetic_overrides
             WHERE character_id=$id
             ORDER BY part
            """;
        Arg(cmd, "$id", dbid);

        using var reader = cmd.ExecuteReader();
        var result = new List<CharacterCosmeticOverride>();
        while (reader.Read())
        {
            result.Add(new(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3)));
        }

        return result;
    }

    public CharacterCosmeticMutationResult ApplyCosmeticOverridesFromItem(
        long accountId,
        long dbid,
        long itemUid,
        int itemIndex,
        IReadOnlyList<CharacterCosmeticOverride> overrides,
        bool consumeSource)
    {
        var normalized = overrides
            .Where(x =>
                x.Part is >= 0 and <= 8 &&
                x.ItemIndex > 0)
            .GroupBy(x => x.Part)
            .Select(group => group.Last())
            .OrderBy(x => x.Part)
            .ToArray();

        if (accountId <= 0 ||
            dbid <= 0 ||
            itemUid <= 0 ||
            itemIndex <= 0 ||
            normalized.Length == 0)
        {
            return new(
                false,
                "換裝資料無效",
                Array.Empty<CharacterCosmeticOverride>());
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(
                db,
                accountId,
                dbid,
                tx))
        {
            return new(
                false,
                "角色不存在",
                Array.Empty<CharacterCosmeticOverride>());
        }

        using (var item = db.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = """
                SELECT count
                  FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND item_index=$item
                   AND in_bank=0
                   AND in_shop=0
                 LIMIT 1
                """;
            Arg(item, "$uid", itemUid);
            Arg(item, "$id", dbid);
            Arg(item, "$item", itemIndex);

            var raw = item.ExecuteScalar();
            if (raw is null ||
                raw == DBNull.Value ||
                Convert.ToInt32(raw) <= 0)
            {
                return new(
                    false,
                    "找不到可使用的換裝變身器",
                    Array.Empty<CharacterCosmeticOverride>());
            }
        }

        foreach (var cosmetic in normalized)
        {
            using var upsert = db.CreateCommand();
            upsert.Transaction = tx;
            upsert.CommandText = """
                INSERT INTO character_cosmetic_overrides(
                    character_id,part,item_index,color,source_item_id)
                VALUES($id,$part,$item,$color,$source)
                ON CONFLICT(character_id,part)
                DO UPDATE SET
                    item_index=excluded.item_index,
                    color=excluded.color,
                    source_item_id=excluded.source_item_id
                """;
            Arg(upsert, "$id", dbid);
            Arg(upsert, "$part", cosmetic.Part);
            Arg(upsert, "$item", cosmetic.ItemIndex);
            Arg(upsert, "$color", Math.Max(0, cosmetic.Color));
            Arg(upsert, "$source", itemIndex);

            if (upsert.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    "換裝外觀儲存失敗",
                    Array.Empty<CharacterCosmeticOverride>());
            }
        }

        if (consumeSource)
        {
            ConsumeInventoryItem(
                db,
                tx,
                dbid,
                itemUid);
        }

        tx.Commit();

        return new(
            true,
            consumeSource
                ? "換裝成功"
                : "換裝成功（永久變身器未消耗）",
            normalized);
    }

    public TimedEffectActivationResult ActivateTimedEffectFromItem(
        long accountId,
        long dbid,
        long itemUid,
        int itemIndex,
        string effectKey,
        int effectValue,
        int durationSeconds,
        long nowUtcMs,
        int sourceType = 0,
        int sourceValue = 0)
    {
        if (itemUid <= 0 ||
            itemIndex <= 0 ||
            string.IsNullOrWhiteSpace(effectKey) ||
            effectValue <= 0 ||
            durationSeconds <= 0)
        {
            return new(
                false,
                "效果資料無效",
                0,
                0);
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(
                db,
                accountId,
                dbid,
                tx))
        {
            return new(
                false,
                "角色不存在",
                0,
                0);
        }

        using (var active = db.CreateCommand())
        {
            active.Transaction = tx;
            active.CommandText = """
                SELECT effect_value,expires_utc_ms
                  FROM character_timed_effects
                 WHERE character_id=$id
                   AND effect_key=$key
                 LIMIT 1
                """;
            Arg(active, "$id", dbid);
            Arg(active, "$key", effectKey.Trim());

            using var reader = active.ExecuteReader();
            if (reader.Read())
            {
                var currentValue = reader.GetInt32(0);
                var currentExpiry = reader.GetInt64(1);

                if (currentExpiry > nowUtcMs)
                {
                    return new(
                        false,
                        "相同效果仍在持續中",
                        currentValue,
                        currentExpiry);
                }
            }
        }

        using (var item = db.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = """
                SELECT count
                  FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND item_index=$item
                   AND in_bank=0
                   AND in_shop=0
                 LIMIT 1
                """;
            Arg(item, "$uid", itemUid);
            Arg(item, "$id", dbid);
            Arg(item, "$item", itemIndex);

            var raw = item.ExecuteScalar();
            if (raw is null ||
                raw == DBNull.Value ||
                Convert.ToInt32(raw) <= 0)
            {
                return new(
                    false,
                    "找不到可使用的道具",
                    0,
                    0);
            }
        }

        var expiresUtcMs = checked(
            nowUtcMs +
            (long)durationSeconds * 1000L);

        using (var upsert = db.CreateCommand())
        {
            upsert.Transaction = tx;
            upsert.CommandText = """
                INSERT INTO character_timed_effects(
                    character_id,effect_key,effect_value,expires_utc_ms,
                    source_type,source_value,started_utc_ms,duration_seconds)
                VALUES(
                    $id,$key,$value,$expires,
                    $sourceType,$sourceValue,$started,$duration)
                ON CONFLICT(character_id,effect_key)
                DO UPDATE SET
                    effect_value=excluded.effect_value,
                    expires_utc_ms=excluded.expires_utc_ms,
                    source_type=excluded.source_type,
                    source_value=excluded.source_value,
                    started_utc_ms=excluded.started_utc_ms,
                    duration_seconds=excluded.duration_seconds
                """;
            Arg(upsert, "$id", dbid);
            Arg(upsert, "$key", effectKey.Trim());
            Arg(upsert, "$value", effectValue);
            Arg(upsert, "$expires", expiresUtcMs);
            Arg(upsert, "$sourceType", Math.Max(0, sourceType));
            Arg(upsert, "$sourceValue", Math.Max(0, sourceValue));
            Arg(upsert, "$started", nowUtcMs);
            Arg(upsert, "$duration", durationSeconds);

            if (upsert.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    "效果啟動失敗",
                    0,
                    0);
            }
        }

        ConsumeInventoryItem(
            db,
            tx,
            dbid,
            itemUid);

        tx.Commit();

        return new(
            true,
            "效果已啟動",
            effectValue,
            expiresUtcMs);
    }

    public TimedEffectBundleActivationResult ActivateTimedEffectsFromItem(
        long accountId,
        long dbid,
        long itemUid,
        int itemIndex,
        IReadOnlyDictionary<string, int> effects,
        int durationSeconds,
        long nowUtcMs,
        int sourceType = 0,
        int sourceValue = 0)
    {
        var normalized = effects
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Key) &&
                x.Value > 0)
            .ToDictionary(
                x => x.Key.Trim(),
                x => x.Value,
                StringComparer.Ordinal);

        if (itemUid <= 0 ||
            itemIndex <= 0 ||
            normalized.Count == 0 ||
            durationSeconds <= 0)
        {
            return new(
                false,
                "效果資料無效",
                0);
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(
                db,
                accountId,
                dbid,
                tx))
        {
            return new(
                false,
                "角色不存在",
                0);
        }

        using (var item = db.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = """
                SELECT count
                  FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND item_index=$item
                   AND in_bank=0
                   AND in_shop=0
                 LIMIT 1
                """;
            Arg(item, "$uid", itemUid);
            Arg(item, "$id", dbid);
            Arg(item, "$item", itemIndex);

            var raw = item.ExecuteScalar();
            if (raw is null ||
                raw == DBNull.Value ||
                Convert.ToInt32(raw) <= 0)
            {
                return new(
                    false,
                    "找不到可使用的道具",
                    0);
            }
        }

        using (var cleanup = db.CreateCommand())
        {
            cleanup.Transaction = tx;
            cleanup.CommandText = """
                DELETE FROM character_timed_effects
                 WHERE character_id=$id
                   AND expires_utc_ms <= $now
                """;
            Arg(cleanup, "$id", dbid);
            Arg(cleanup, "$now", nowUtcMs);
            cleanup.ExecuteNonQuery();
        }

        foreach (var effect in normalized)
        {
            using var active = db.CreateCommand();
            active.Transaction = tx;
            active.CommandText = """
                SELECT 1
                  FROM character_timed_effects
                 WHERE character_id=$id
                   AND effect_key=$key
                   AND expires_utc_ms > $now
                 LIMIT 1
                """;
            Arg(active, "$id", dbid);
            Arg(active, "$key", effect.Key);
            Arg(active, "$now", nowUtcMs);

            if (active.ExecuteScalar() is not null)
            {
                return new(
                    false,
                    "相同類型的能力增益仍在持續中",
                    0);
            }
        }

        var expiresUtcMs = checked(
            nowUtcMs +
            (long)durationSeconds * 1000L);

        foreach (var effect in normalized)
        {
            using var upsert = db.CreateCommand();
            upsert.Transaction = tx;
            upsert.CommandText = """
                INSERT INTO character_timed_effects(
                    character_id,effect_key,effect_value,expires_utc_ms,
                    source_type,source_value,started_utc_ms,duration_seconds)
                VALUES(
                    $id,$key,$value,$expires,
                    $sourceType,$sourceValue,$started,$duration)
                ON CONFLICT(character_id,effect_key)
                DO UPDATE SET
                    effect_value=excluded.effect_value,
                    expires_utc_ms=excluded.expires_utc_ms,
                    source_type=excluded.source_type,
                    source_value=excluded.source_value,
                    started_utc_ms=excluded.started_utc_ms,
                    duration_seconds=excluded.duration_seconds
                """;
            Arg(upsert, "$id", dbid);
            Arg(upsert, "$key", effect.Key);
            Arg(upsert, "$value", effect.Value);
            Arg(upsert, "$expires", expiresUtcMs);
            Arg(upsert, "$sourceType", Math.Max(0, sourceType));
            Arg(upsert, "$sourceValue", Math.Max(0, sourceValue));
            Arg(upsert, "$started", nowUtcMs);
            Arg(upsert, "$duration", durationSeconds);

            if (upsert.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    "效果啟動失敗",
                    0);
            }
        }

        ConsumeInventoryItem(
            db,
            tx,
            dbid,
            itemUid);

        tx.Commit();

        return new(
            true,
            "效果已啟動",
            expiresUtcMs);
    }

    public int GetTimedEffectValue(
        long accountId,
        long dbid,
        string effectKey,
        long nowUtcMs)
    {
        if (dbid <= 0 ||
            string.IsNullOrWhiteSpace(effectKey))
            return 0;

        using var db = Open();

        using (var cleanup = db.CreateCommand())
        {
            cleanup.CommandText = """
                DELETE FROM character_timed_effects
                 WHERE character_id=$id
                   AND effect_key=$key
                   AND expires_utc_ms <= $now
                """;
            Arg(cleanup, "$id", dbid);
            Arg(cleanup, "$key", effectKey.Trim());
            Arg(cleanup, "$now", nowUtcMs);
            cleanup.ExecuteNonQuery();
        }

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT effect.effect_value
              FROM character_timed_effects effect
              JOIN characters c
                ON c.id=effect.character_id
             WHERE effect.character_id=$id
               AND c.account_id=$account
               AND effect.effect_key=$key
               AND effect.expires_utc_ms > $now
             LIMIT 1
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$account", accountId);
        Arg(cmd, "$key", effectKey.Trim());
        Arg(cmd, "$now", nowUtcMs);

        return Convert.ToInt32(
            cmd.ExecuteScalar() ?? 0);
    }

    public IReadOnlyList<ActiveTimedEffect> GetActiveTimedEffects(
        long accountId,
        long dbid,
        long nowUtcMs)
    {
        if (accountId <= 0 || dbid <= 0)
            return [];

        using var db = Open();

        if (!OwnsCharacter(db, accountId, dbid))
            return [];

        using (var cleanup = db.CreateCommand())
        {
            cleanup.CommandText = """
                DELETE FROM character_timed_effects
                 WHERE character_id=$id
                   AND expires_utc_ms <= $now
                """;
            Arg(cleanup, "$id", dbid);
            Arg(cleanup, "$now", nowUtcMs);
            cleanup.ExecuteNonQuery();
        }

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT effect_key,effect_value,expires_utc_ms,
                   source_type,source_value,started_utc_ms,duration_seconds
              FROM character_timed_effects
             WHERE character_id=$id
               AND expires_utc_ms > $now
             ORDER BY expires_utc_ms,effect_key
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$now", nowUtcMs);

        var effects = new List<ActiveTimedEffect>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            effects.Add(new ActiveTimedEffect(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt64(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt64(5),
                reader.GetInt32(6)));
        }

        return effects;
    }

    public IReadOnlyList<RankEntry> GetRankEntries(
        int rankType,
        int limit)
    {
        limit = Math.Clamp(limit, 1, 100);

        using var db = Open();
        using var cmd = db.CreateCommand();

        switch (rankType)
        {
            case 1:
                cmd.CommandText = """
                    SELECT id,account_id,name,level
                      FROM characters
                     ORDER BY level DESC,
                              exp_acc DESC,
                              id ASC
                     LIMIT $limit
                    """;
                break;

            case 2:
                cmd.CommandText = """
                    SELECT id,account_id,name,money
                      FROM characters
                     ORDER BY money DESC,
                              level DESC,
                              id ASC
                     LIMIT $limit
                    """;
                break;

            case 3:
                cmd.CommandText = """
                    SELECT club.id,
                           leader.account_id,
                           club.name,
                           club.level,
                           leader.id
                      FROM clubs club
                      JOIN club_members member
                        ON member.club_id=club.id
                       AND member.position=3
                      JOIN characters leader
                        ON leader.id=member.character_id
                     ORDER BY club.level DESC,
                              club.exp DESC,
                              club.id ASC
                     LIMIT $limit
                    """;
                break;

            default:
                return Array.Empty<RankEntry>();
        }

        Arg(cmd, "$limit", limit);

        var result = new List<RankEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (rankType == 3)
            {
                var leaderDbId = reader.GetInt64(4);
                result.Add(new RankEntry(
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    leaderDbId,
                    leaderDbId));
            }
            else
            {
                var dbid = reader.GetInt64(0);
                result.Add(new RankEntry(
                    dbid,
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    dbid,
                    dbid));
            }
        }

        return result;
    }

    public string GetRankMessage(
        int rankType,
        long dbid)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT message
              FROM rank_messages
             WHERE rank_type=$type
               AND character_id=$id
             LIMIT 1
            """;
        Arg(cmd, "$type", rankType);
        Arg(cmd, "$id", dbid);

        return Convert.ToString(cmd.ExecuteScalar())
               ?? "尚無宣言";
    }

    public bool SetRankMessage(
        long accountId,
        long dbid,
        int rankType,
        string message)
    {
        if (rankType is not (1 or 2 or 3 or 4) ||
            message.Length > 20)
            return false;

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return false;

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO rank_messages(
                rank_type,character_id,message,updated_utc_ms)
            VALUES($type,$id,$message,$updated)
            ON CONFLICT(rank_type,character_id)
            DO UPDATE SET
                message=excluded.message,
                updated_utc_ms=excluded.updated_utc_ms
            """;
        Arg(cmd, "$type", rankType);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$message", message);
        Arg(cmd, "$updated",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var ok = cmd.ExecuteNonQuery() == 1;
        if (ok)
            tx.Commit();

        return ok;
    }

    public ClubSnapshot? GetClubSnapshot(
        long accountId,
        long dbid,
        long nowUtcMs)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return null;

        long clubId;
        int myPosition;
        long myDonation;

        using (var membership = db.CreateCommand())
        {
            membership.Transaction = tx;
            membership.CommandText = """
                SELECT club_id, position, donation
                  FROM club_members
                 WHERE character_id=$id
                 LIMIT 1
                """;
            Arg(membership, "$id", dbid);

            using var reader = membership.ExecuteReader();
            if (!reader.Read())
                return null;

            clubId = reader.GetInt64(0);
            myPosition = reader.GetInt32(1);
            myDonation = reader.GetInt64(2);
        }

        string clubName;
        int clubLevel;
        long clubExp;
        long clubSaving;
        string clubMessage;
        int activeResearchId;
        long activeResearchEndUtcMs;

        using (var club = db.CreateCommand())
        {
            club.Transaction = tx;
            club.CommandText = """
                SELECT name,level,exp,saving,message,
                       active_research_id,
                       active_research_end_utc_ms
                  FROM clubs
                 WHERE id=$club
                 LIMIT 1
                """;
            Arg(club, "$club", clubId);

            using var reader = club.ExecuteReader();
            if (!reader.Read())
                return null;

            clubName = reader.GetString(0);
            clubLevel = reader.GetInt32(1);
            clubExp = reader.GetInt64(2);
            clubSaving = reader.GetInt64(3);
            clubMessage = reader.GetString(4);
            activeResearchId = reader.GetInt32(5);
            activeResearchEndUtcMs = reader.GetInt64(6);
        }

        if (activeResearchId > 0 &&
            activeResearchEndUtcMs > 0 &&
            activeResearchEndUtcMs <= nowUtcMs)
        {
            var currentLevel = activeResearchId == 1
                ? clubLevel
                : 0;

            if (activeResearchId != 1)
            {
                using var current = db.CreateCommand();
                current.Transaction = tx;
                current.CommandText = """
                    SELECT level
                      FROM club_research
                     WHERE club_id=$club
                       AND research_id=$research
                     LIMIT 1
                    """;
                Arg(current, "$club", clubId);
                Arg(current, "$research", activeResearchId);
                currentLevel = Convert.ToInt32(
                    current.ExecuteScalar() ?? 0);
            }

            var nextLevel = Math.Min(5, currentLevel + 1);

            using (var upsert = db.CreateCommand())
            {
                upsert.Transaction = tx;
                upsert.CommandText = """
                    INSERT INTO club_research(
                        club_id,research_id,level)
                    VALUES($club,$research,$level)
                    ON CONFLICT(club_id,research_id)
                    DO UPDATE SET level=excluded.level
                    """;
                Arg(upsert, "$club", clubId);
                Arg(upsert, "$research", activeResearchId);
                Arg(upsert, "$level", nextLevel);
                upsert.ExecuteNonQuery();
            }

            using (var finish = db.CreateCommand())
            {
                finish.Transaction = tx;
                finish.CommandText = activeResearchId == 1
                    ? """
                        UPDATE clubs
                           SET level=$level,
                               active_research_id=0,
                               active_research_end_utc_ms=0
                         WHERE id=$club
                        """
                    : """
                        UPDATE clubs
                           SET active_research_id=0,
                               active_research_end_utc_ms=0
                         WHERE id=$club
                        """;
                Arg(finish, "$club", clubId);
                if (activeResearchId == 1)
                    Arg(finish, "$level", nextLevel);
                finish.ExecuteNonQuery();
            }

            if (activeResearchId == 1)
                clubLevel = nextLevel;

            activeResearchId = 0;
            activeResearchEndUtcMs = 0;
        }

        var members = new List<ClubMemberData>();
        using (var memberCmd = db.CreateCommand())
        {
            memberCmd.Transaction = tx;
            memberCmd.CommandText = """
                SELECT c.id,c.name,c.level,c.career,c.job,
                       m.position,m.donation
                  FROM club_members m
                  JOIN characters c ON c.id=m.character_id
                 WHERE m.club_id=$club
                 ORDER BY m.position DESC,
                          c.level DESC,
                          c.id ASC
                """;
            Arg(memberCmd, "$club", clubId);

            using var reader = memberCmd.ExecuteReader();
            while (reader.Read())
            {
                members.Add(new ClubMemberData(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt64(6)));
            }
        }

        var researches = new List<ClubResearchData>();
        using (var researchCmd = db.CreateCommand())
        {
            researchCmd.Transaction = tx;
            researchCmd.CommandText = """
                SELECT research_id,level
                  FROM club_research
                 WHERE club_id=$club
                 ORDER BY research_id
                """;
            Arg(researchCmd, "$club", clubId);

            using var reader = researchCmd.ExecuteReader();
            while (reader.Read())
            {
                researches.Add(new ClubResearchData(
                    reader.GetInt32(0),
                    reader.GetInt32(1)));
            }
        }

        if (!researches.Any(x => x.ResearchId == 1))
            researches.Insert(0, new ClubResearchData(1, clubLevel));

        tx.Commit();

        return new ClubSnapshot(
            clubId,
            clubName,
            clubLevel,
            clubExp,
            clubSaving,
            clubMessage,
            activeResearchId,
            activeResearchEndUtcMs,
            myPosition,
            myDonation,
            members,
            researches);
    }

    public long GetClubIdForCharacter(long dbid)
    {
        if (dbid <= 0)
            return 0;

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT club_id
              FROM club_members
             WHERE character_id=$id
             LIMIT 1
            """;
        Arg(cmd, "$id", dbid);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    public ClubMutationResult CreateClub(
        long accountId,
        long dbid,
        string clubName,
        long nowUtcMs)
    {
        const long creationFee = 500_000;
        int[] requiredDocuments = [2560, 2561, 2562];

        clubName = clubName.Trim();
        if (clubName.Length is < 1 or > 8)
            return new(false, "戰團名稱需為 1 到 8 個字");

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在");

        int level;
        long money;
        using (var character = db.CreateCommand())
        {
            character.Transaction = tx;
            character.CommandText = """
                SELECT level,money
                  FROM characters
                 WHERE id=$id
                   AND account_id=$account
                 LIMIT 1
                """;
            Arg(character, "$id", dbid);
            Arg(character, "$account", accountId);

            using var reader = character.ExecuteReader();
            if (!reader.Read())
                return new(false, "角色不存在");

            level = reader.GetInt32(0);
            money = reader.GetInt64(1);
        }

        if (level < 30)
            return new(false, "建立戰團需要角色等級 30 級以上");

        if (money < creationFee)
            return new(false, "建立戰團需要 50 萬星幣");

        using (var existing = db.CreateCommand())
        {
            existing.Transaction = tx;
            existing.CommandText = """
                SELECT count(*)
                  FROM club_members
                 WHERE character_id=$id
                """;
            Arg(existing, "$id", dbid);
            if (Convert.ToInt32(existing.ExecuteScalar()) > 0)
                return new(false, "你已經加入戰團");
        }

        using (var sameName = db.CreateCommand())
        {
            sameName.Transaction = tx;
            sameName.CommandText = """
                SELECT count(*)
                  FROM clubs
                 WHERE name=$name
                """;
            Arg(sameName, "$name", clubName);
            if (Convert.ToInt32(sameName.ExecuteScalar()) > 0)
                return new(false, "戰團名稱已被使用");
        }

        foreach (var documentId in requiredDocuments)
        {
            using var check = db.CreateCommand();
            check.Transaction = tx;
            check.CommandText = """
                SELECT COALESCE(SUM(count),0)
                  FROM inventory_items
                 WHERE character_id=$id
                   AND item_index=$item
                   AND in_bank=0
                   AND in_shop=0
                """;
            Arg(check, "$id", dbid);
            Arg(check, "$item", documentId);

            if (Convert.ToInt32(check.ExecuteScalar()) < 1)
            {
                return new(
                    false,
                    $"缺少建立戰團所需文件 ItemID={documentId}");
            }
        }

        using (var deductMoney = db.CreateCommand())
        {
            deductMoney.Transaction = tx;
            deductMoney.CommandText = """
                UPDATE characters
                   SET money=money-$fee
                 WHERE id=$id
                   AND account_id=$account
                   AND money >= $fee
                """;
            Arg(deductMoney, "$fee", creationFee);
            Arg(deductMoney, "$id", dbid);
            Arg(deductMoney, "$account", accountId);

            if (deductMoney.ExecuteNonQuery() != 1)
                return new(false, "建立戰團費用扣除失敗");
        }

        foreach (var documentId in requiredDocuments)
        {
            long itemUid;
            using (var find = db.CreateCommand())
            {
                find.Transaction = tx;
                find.CommandText = """
                    SELECT uid
                      FROM inventory_items
                     WHERE character_id=$id
                       AND item_index=$item
                       AND count > 0
                       AND in_bank=0
                       AND in_shop=0
                     ORDER BY uid
                     LIMIT 1
                    """;
                Arg(find, "$id", dbid);
                Arg(find, "$item", documentId);

                var rawUid = find.ExecuteScalar();
                if (rawUid is null)
                    return new(false, "建立戰團文件狀態已變更");

                itemUid = Convert.ToInt64(rawUid);
            }

            using (var consume = db.CreateCommand())
            {
                consume.Transaction = tx;
                consume.CommandText = """
                    UPDATE inventory_items
                       SET count=count-1
                     WHERE uid=$uid
                       AND character_id=$id
                       AND count > 0
                    """;
                Arg(consume, "$uid", itemUid);
                Arg(consume, "$id", dbid);

                if (consume.ExecuteNonQuery() != 1)
                    return new(false, "建立戰團文件扣除失敗");
            }

            using var cleanup = db.CreateCommand();
            cleanup.Transaction = tx;
            cleanup.CommandText = """
                DELETE FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND count <= 0
                """;
            Arg(cleanup, "$uid", itemUid);
            Arg(cleanup, "$id", dbid);
            cleanup.ExecuteNonQuery();
        }

        long clubId;
        using (var insert = db.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO clubs(
                    name,created_utc_ms)
                VALUES($name,$created);
                SELECT last_insert_rowid();
                """;
            Arg(insert, "$name", clubName);
            Arg(insert, "$created", nowUtcMs);
            clubId = Convert.ToInt64(insert.ExecuteScalar());
        }

        using (var member = db.CreateCommand())
        {
            member.Transaction = tx;
            member.CommandText = """
                INSERT INTO club_members(
                    club_id,character_id,position,donation,joined_utc_ms)
                VALUES($club,$id,3,0,$joined)
                """;
            Arg(member, "$club", clubId);
            Arg(member, "$id", dbid);
            Arg(member, "$joined", nowUtcMs);
            member.ExecuteNonQuery();
        }

        using (var research = db.CreateCommand())
        {
            research.Transaction = tx;
            research.CommandText = """
                INSERT INTO club_research(
                    club_id,research_id,level)
                VALUES($club,1,1)
                """;
            Arg(research, "$club", clubId);
            research.ExecuteNonQuery();
        }

        tx.Commit();
        return new(true, "戰團建立成功");
    }

    public ClubMutationResult JoinClub(
        long accountId,
        long dbid,
        long clubId,
        long nowUtcMs)
    {
        if (clubId <= 0)
            return new(false, "戰團不存在");

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在");

        using (var existing = db.CreateCommand())
        {
            existing.Transaction = tx;
            existing.CommandText = """
                SELECT count(*)
                  FROM club_members
                 WHERE character_id=$id
                """;
            Arg(existing, "$id", dbid);
            if (Convert.ToInt32(existing.ExecuteScalar()) > 0)
                return new(false, "你已經加入戰團");
        }

        using (var capacity = db.CreateCommand())
        {
            capacity.Transaction = tx;
            capacity.CommandText = """
                SELECT club.level,
                       (
                           SELECT count(*)
                             FROM club_members
                            WHERE club_id=club.id
                       )
                  FROM clubs club
                 WHERE club.id=$club
                 LIMIT 1
                """;
            Arg(capacity, "$club", clubId);

            using var reader = capacity.ExecuteReader();
            if (!reader.Read())
                return new(false, "戰團不存在");

            var level = Math.Clamp(reader.GetInt32(0), 1, 5);
            var count = reader.GetInt32(1);
            var maxMembers = Math.Min(50, level * 10);

            if (count >= maxMembers)
                return new(false, "戰團人數已滿");
        }

        using var insert = db.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO club_members(
                club_id,character_id,position,donation,joined_utc_ms)
            VALUES($club,$id,0,0,$joined)
            """;
        Arg(insert, "$club", clubId);
        Arg(insert, "$id", dbid);
        Arg(insert, "$joined", nowUtcMs);

        try
        {
            insert.ExecuteNonQuery();
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        {
            return new(false, "無法加入戰團");
        }

        tx.Commit();
        return new(true, "已加入戰團");
    }

    public ClubMutationResult UpdateClubMessage(
        long accountId,
        long dbid,
        string message)
    {
        message = message.Trim();
        if (message.Length > 100)
            return new(false, "戰團公告最多 100 個字");

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在");

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE clubs
               SET message=$message
             WHERE id=(
                 SELECT club_id
                   FROM club_members
                  WHERE character_id=$id
                    AND position=3
                  LIMIT 1)
            """;
        Arg(cmd, "$message", message);
        Arg(cmd, "$id", dbid);

        if (cmd.ExecuteNonQuery() != 1)
            return new(false, "只有團長可以修改戰團公告");

        tx.Commit();
        return new(true, "戰團公告已更新");
    }

    public ClubMutationResult DonateClubMoney(
        long accountId,
        long dbid,
        long amount)
    {
        if (amount <= 0)
            return new(false, "捐獻金額必須大於 0");

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在");

        var clubId = ReadClubId(db, tx, dbid);
        if (clubId <= 0)
            return new(false, "你尚未加入戰團");

        using (var deduct = db.CreateCommand())
        {
            deduct.Transaction = tx;
            deduct.CommandText = """
                UPDATE characters
                   SET money=money-$amount
                 WHERE id=$id
                   AND account_id=$account
                   AND money >= $amount
                """;
            Arg(deduct, "$amount", amount);
            Arg(deduct, "$id", dbid);
            Arg(deduct, "$account", accountId);
            if (deduct.ExecuteNonQuery() != 1)
                return new(false, "星幣不足");
        }

        using (var club = db.CreateCommand())
        {
            club.Transaction = tx;
            club.CommandText = """
                UPDATE clubs
                   SET saving=saving+$amount
                 WHERE id=$club
                """;
            Arg(club, "$amount", amount);
            Arg(club, "$club", clubId);
            club.ExecuteNonQuery();
        }

        using (var member = db.CreateCommand())
        {
            member.Transaction = tx;
            member.CommandText = """
                UPDATE club_members
                   SET donation=donation+$amount
                 WHERE club_id=$club
                   AND character_id=$id
                """;
            Arg(member, "$amount", amount);
            Arg(member, "$club", clubId);
            Arg(member, "$id", dbid);
            member.ExecuteNonQuery();
        }

        tx.Commit();
        return new(true, "戰團星幣捐獻成功");
    }

    public ClubMutationResult DonateClubExp(
        long accountId,
        long dbid,
        long amount)
    {
        if (amount <= 0 || amount > int.MaxValue)
            return new(false, "捐獻經驗值無效");

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在");

        var clubId = ReadClubId(db, tx, dbid);
        if (clubId <= 0)
            return new(false, "你尚未加入戰團");

        using (var deduct = db.CreateCommand())
        {
            deduct.Transaction = tx;
            deduct.CommandText = """
                UPDATE characters
                   SET exp_acc=exp_acc-$amount
                 WHERE id=$id
                   AND account_id=$account
                   AND exp_acc >= $amount
                """;
            Arg(deduct, "$amount", amount);
            Arg(deduct, "$id", dbid);
            Arg(deduct, "$account", accountId);
            if (deduct.ExecuteNonQuery() != 1)
                return new(false, "角色經驗值不足");
        }

        using (var club = db.CreateCommand())
        {
            club.Transaction = tx;
            club.CommandText = """
                UPDATE clubs
                   SET exp=exp+$amount
                 WHERE id=$club
                """;
            Arg(club, "$amount", amount);
            Arg(club, "$club", clubId);
            club.ExecuteNonQuery();
        }

        using (var member = db.CreateCommand())
        {
            member.Transaction = tx;
            member.CommandText = """
                UPDATE club_members
                   SET donation=donation+$amount
                 WHERE club_id=$club
                   AND character_id=$id
                """;
            Arg(member, "$amount", amount);
            Arg(member, "$club", clubId);
            Arg(member, "$id", dbid);
            member.ExecuteNonQuery();
        }

        tx.Commit();
        return new(true, "戰團經驗捐獻成功");
    }

    public ClubMutationResult StartClubResearch(
        long accountId,
        long dbid,
        int researchId,
        long nowUtcMs)
    {
        if (researchId is < 1 or > 11)
            return new(false, "未知戰團科技");

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在");

        long clubId;
        using (var member = db.CreateCommand())
        {
            member.Transaction = tx;
            member.CommandText = """
                SELECT club_id
                  FROM club_members
                 WHERE character_id=$id
                   AND position=3
                 LIMIT 1
                """;
            Arg(member, "$id", dbid);
            clubId = Convert.ToInt64(
                member.ExecuteScalar() ?? 0L);
        }

        if (clubId <= 0)
            return new(false, "只有團長可以研發科技");

        int clubLevel;
        long clubSaving;
        long clubExp;
        int activeResearchId;
        using (var club = db.CreateCommand())
        {
            club.Transaction = tx;
            club.CommandText = """
                SELECT level,saving,exp,active_research_id
                  FROM clubs
                 WHERE id=$club
                 LIMIT 1
                """;
            Arg(club, "$club", clubId);
            using var reader = club.ExecuteReader();
            if (!reader.Read())
                return new(false, "戰團不存在");

            clubLevel = reader.GetInt32(0);
            clubSaving = reader.GetInt64(1);
            clubExp = reader.GetInt64(2);
            activeResearchId = reader.GetInt32(3);
        }

        if (activeResearchId > 0)
            return new(false, "目前已有科技正在研發");

        var currentLevel = researchId == 1
            ? clubLevel
            : 0;

        if (researchId != 1)
        {
            using var current = db.CreateCommand();
            current.Transaction = tx;
            current.CommandText = """
                SELECT level
                  FROM club_research
                 WHERE club_id=$club
                   AND research_id=$research
                 LIMIT 1
                """;
            Arg(current, "$club", clubId);
            Arg(current, "$research", researchId);
            currentLevel = Convert.ToInt32(
                current.ExecuteScalar() ?? 0);
        }

        if (currentLevel >= 5)
            return new(false, "此科技已達最高等級");

        var nextLevel = currentLevel + 1;
        var moneyCost = 500_000L * nextLevel;
        var expCost = 1_000_000L * nextLevel;
        var researchHours = 6L * nextLevel;

        if (clubSaving < moneyCost ||
            clubExp < expCost)
            return new(false, "戰團資金或經驗不足");

        var endUtcMs = nowUtcMs +
                       researchHours * 60L * 60L * 1000L;

        using var update = db.CreateCommand();
        update.Transaction = tx;
        update.CommandText = """
            UPDATE clubs
               SET saving=saving-$money,
                   exp=exp-$exp,
                   active_research_id=$research,
                   active_research_end_utc_ms=$end
             WHERE id=$club
               AND active_research_id=0
            """;
        Arg(update, "$money", moneyCost);
        Arg(update, "$exp", expCost);
        Arg(update, "$research", researchId);
        Arg(update, "$end", endUtcMs);
        Arg(update, "$club", clubId);

        if (update.ExecuteNonQuery() != 1)
            return new(false, "科技研發狀態已變更，請重試");

        tx.Commit();
        return new(
            true,
            $"已開始研發科技 Lv.{nextLevel}");
    }

    public ClubMutationResult SetClubMemberPosition(
        long accountId,
        long callerDbid,
        string targetName,
        int position)
    {
        targetName = targetName.Trim();

        if (targetName.Length == 0)
            return new(false, "請輸入玩家名稱");

        if (position is < 0 or > 2)
            return new(false, "職位代號只能是 0、1、2");

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(
                db,
                accountId,
                callerDbid,
                tx))
            return new(false, "角色不存在");

        long clubId;
        int callerPosition;
        using (var caller = db.CreateCommand())
        {
            caller.Transaction = tx;
            caller.CommandText = """
                SELECT club_id,position
                  FROM club_members
                 WHERE character_id=$id
                 LIMIT 1
                """;
            Arg(caller, "$id", callerDbid);

            using var reader = caller.ExecuteReader();
            if (!reader.Read())
                return new(false, "你尚未加入戰團");

            clubId = reader.GetInt64(0);
            callerPosition = reader.GetInt32(1);
        }

        if (callerPosition < 2)
            return new(false, "你沒有變更職位的權限");

        if (position >= callerPosition)
            return new(false, "不能任命與自己同級或更高的職位");

        long targetDbid;
        int targetPosition;
        using (var target = db.CreateCommand())
        {
            target.Transaction = tx;
            target.CommandText = """
                SELECT member.character_id,member.position
                  FROM club_members member
                  JOIN characters character
                    ON character.id=member.character_id
                 WHERE member.club_id=$club
                   AND character.name=$name COLLATE NOCASE
                 LIMIT 1
                """;
            Arg(target, "$club", clubId);
            Arg(target, "$name", targetName);

            using var reader = target.ExecuteReader();
            if (!reader.Read())
                return new(false, "找不到同戰團的玩家");

            targetDbid = reader.GetInt64(0);
            targetPosition = reader.GetInt32(1);
        }

        if (targetDbid == callerDbid)
            return new(false, "不能變更自己的職位");

        if (targetPosition >= callerPosition)
            return new(false, "不能變更同級或更高職位的成員");

        using var update = db.CreateCommand();
        update.Transaction = tx;
        update.CommandText = """
            UPDATE club_members
               SET position=$position
             WHERE club_id=$club
               AND character_id=$target
            """;
        Arg(update, "$position", position);
        Arg(update, "$club", clubId);
        Arg(update, "$target", targetDbid);

        if (update.ExecuteNonQuery() != 1)
            return new(false, "戰團職位更新失敗");

        tx.Commit();
        return new(
            true,
            $"已將 {targetName} 的職位調整為 {position}");
    }

    public ClubMutationResult KickClubMember(
        long accountId,
        long callerDbid,
        string targetName)
    {
        targetName = targetName.Trim();

        if (targetName.Length == 0)
            return new(false, "請輸入玩家名稱");

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(
                db,
                accountId,
                callerDbid,
                tx))
            return new(false, "角色不存在");

        long clubId;
        int callerPosition;
        using (var caller = db.CreateCommand())
        {
            caller.Transaction = tx;
            caller.CommandText = """
                SELECT club_id,position
                  FROM club_members
                 WHERE character_id=$id
                 LIMIT 1
                """;
            Arg(caller, "$id", callerDbid);

            using var reader = caller.ExecuteReader();
            if (!reader.Read())
                return new(false, "你尚未加入戰團");

            clubId = reader.GetInt64(0);
            callerPosition = reader.GetInt32(1);
        }

        if (callerPosition < 2)
            return new(false, "你沒有剔除成員的權限");

        long targetDbid;
        int targetPosition;
        using (var target = db.CreateCommand())
        {
            target.Transaction = tx;
            target.CommandText = """
                SELECT member.character_id,member.position
                  FROM club_members member
                  JOIN characters character
                    ON character.id=member.character_id
                 WHERE member.club_id=$club
                   AND character.name=$name COLLATE NOCASE
                 LIMIT 1
                """;
            Arg(target, "$club", clubId);
            Arg(target, "$name", targetName);

            using var reader = target.ExecuteReader();
            if (!reader.Read())
                return new(false, "找不到同戰團的玩家");

            targetDbid = reader.GetInt64(0);
            targetPosition = reader.GetInt32(1);
        }

        if (targetDbid == callerDbid)
            return new(false, "不能剔除自己");

        if (targetPosition >= callerPosition)
            return new(false, "只能剔除職位低於自己的成員");

        using var delete = db.CreateCommand();
        delete.Transaction = tx;
        delete.CommandText = """
            DELETE FROM club_members
             WHERE club_id=$club
               AND character_id=$target
            """;
        Arg(delete, "$club", clubId);
        Arg(delete, "$target", targetDbid);

        if (delete.ExecuteNonQuery() != 1)
            return new(false, "剔除戰團成員失敗");

        tx.Commit();
        return new(true, $"已將 {targetName} 移出戰團");
    }

    public ClubMutationResult LeaveClub(
        long accountId,
        long dbid)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在");

        int position;
        using (var membership = db.CreateCommand())
        {
            membership.Transaction = tx;
            membership.CommandText = """
                SELECT position
                  FROM club_members
                 WHERE character_id=$id
                 LIMIT 1
                """;
            Arg(membership, "$id", dbid);

            var raw = membership.ExecuteScalar();
            if (raw is null)
                return new(false, "你尚未加入戰團");

            position = Convert.ToInt32(raw);
        }

        if (position == 3)
            return new(false, "團長無法直接退出戰團，請先解散戰團");

        using var delete = db.CreateCommand();
        delete.Transaction = tx;
        delete.CommandText = """
            DELETE FROM club_members
             WHERE character_id=$id
            """;
        Arg(delete, "$id", dbid);

        if (delete.ExecuteNonQuery() != 1)
            return new(false, "退出戰團失敗");

        tx.Commit();
        return new(true, "已退出戰團");
    }

    public ClubMutationResult DisbandClub(
        long accountId,
        long dbid)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在");

        long clubId;
        using (var membership = db.CreateCommand())
        {
            membership.Transaction = tx;
            membership.CommandText = """
                SELECT club_id
                  FROM club_members
                 WHERE character_id=$id
                   AND position=3
                 LIMIT 1
                """;
            Arg(membership, "$id", dbid);

            clubId = Convert.ToInt64(
                membership.ExecuteScalar() ?? 0L);
        }

        if (clubId <= 0)
            return new(false, "只有團長可以解散戰團");

        using var delete = db.CreateCommand();
        delete.Transaction = tx;
        delete.CommandText = """
            DELETE FROM clubs
             WHERE id=$club
            """;
        Arg(delete, "$club", clubId);

        if (delete.ExecuteNonQuery() != 1)
            return new(false, "解散戰團失敗");

        tx.Commit();
        return new(true, "戰團已解散");
    }

    private static long ReadClubId(
        SqliteConnection db,
        SqliteTransaction tx,
        long dbid)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT club_id
              FROM club_members
             WHERE character_id=$id
             LIMIT 1
            """;
        Arg(cmd, "$id", dbid);
        return Convert.ToInt64(
            cmd.ExecuteScalar() ?? 0L);
    }

    public IReadOnlyList<int> GetUnlockedTitles(
        long accountId,
        long dbid)
    {
        var result = new List<int>();

        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid))
            return result;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT title_id
              FROM character_titles
             WHERE character_id=$id
             ORDER BY title_id
            """;
        Arg(cmd, "$id", dbid);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(reader.GetInt32(0));

        return result;
    }

    public bool HasTitle(
        long accountId,
        long dbid,
        int titleId)
    {
        if (titleId is < 1 or > 26)
            return false;

        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid))
            return false;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT count(*)
              FROM character_titles
             WHERE character_id=$id
               AND title_id=$title
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$title", titleId);

        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public bool UnlockTitle(
        long accountId,
        long dbid,
        int titleId)
    {
        if (titleId is < 1 or > 26)
            return false;

        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid))
            return false;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO character_titles(
                character_id,title_id,unlocked_utc_ms)
            VALUES($id,$title,$time)
            ON CONFLICT(character_id,title_id)
            DO NOTHING
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$title", titleId);
        Arg(cmd, "$time",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();

        return true;
    }

    public bool SetCurrentTitle(
        long accountId,
        long dbid,
        int titleId)
    {
        if (titleId is < 0 or > 26)
            return false;

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return false;

        if (titleId > 0)
        {
            using var check = db.CreateCommand();
            check.Transaction = tx;
            check.CommandText = """
                SELECT count(*)
                  FROM character_titles
                 WHERE character_id=$id
                   AND title_id=$title
                """;
            Arg(check, "$id", dbid);
            Arg(check, "$title", titleId);

            if (Convert.ToInt32(check.ExecuteScalar()) <= 0)
                return false;
        }

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO character_title_state(
                character_id,current_title)
            VALUES($id,$title)
            ON CONFLICT(character_id)
            DO UPDATE SET
                current_title=excluded.current_title
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$title", titleId);

        if (cmd.ExecuteNonQuery() != 1)
            return false;

        tx.Commit();
        return true;
    }

    public List<Dictionary<string, object>> GetShortcuts(
        long accountId,
        long dbid)
    {
        var result = new List<Dictionary<string, object>>();
        using var db = Open();

        if (!OwnsCharacter(db, accountId, dbid))
            return result;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT slot_index,shortcut_type,target_id
              FROM character_shortcuts
             WHERE character_id=$id
             ORDER BY slot_index
            """;
        Arg(cmd, "$id", dbid);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new Dictionary<string, object>
            {
                ["SlotIndex"] = r.GetInt32(0),
                ["ShortcutType"] = r.GetInt32(1),
                ["TargetID"] = r.GetInt32(2)
            });
        }

        return result;
    }

    public bool UpdateShortcut(
        long accountId,
        long dbid,
        int slotIndex,
        int shortcutType,
        int targetId)
    {
        if (slotIndex < 0 || slotIndex > 255)
            return false;

        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid))
            return false;

        if (shortcutType <= 0)
        {
            using var del = db.CreateCommand();
            del.CommandText = """
                DELETE FROM character_shortcuts
                 WHERE character_id=$id AND slot_index=$slot
                """;
            Arg(del, "$id", dbid);
            Arg(del, "$slot", slotIndex);
            del.ExecuteNonQuery();
            return true;
        }

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO character_shortcuts(
                character_id,slot_index,shortcut_type,target_id)
            VALUES($id,$slot,$type,$target)
            ON CONFLICT(character_id,slot_index)
            DO UPDATE SET
                shortcut_type=excluded.shortcut_type,
                target_id=excluded.target_id
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$slot", slotIndex);
        Arg(cmd, "$type", Math.Clamp(shortcutType, 1, 99));
        Arg(cmd, "$target", targetId);
        return cmd.ExecuteNonQuery() == 1;
    }

    public List<Dictionary<string, object>> GetSkills(
        long accountId,
        long dbid)
    {
        var result = new List<Dictionary<string, object>>();
        using var db = Open();

        if (!OwnsCharacter(db, accountId, dbid))
            return result;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT skill_id,level,exp
              FROM character_skills
             WHERE character_id=$id
             ORDER BY skill_id
            """;
        Arg(cmd, "$id", dbid);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var skillId = r.GetInt32(0);
            var level = r.GetInt32(1);
            var exp = r.GetInt32(2);

            result.Add(new Dictionary<string, object>
            {
                ["skill_id"] = skillId,
                ["level"] = level,
                ["exp"] = exp,
                ["SkillID"] = skillId,
                ["SkillLevel"] = level
            });
        }

        return result;
    }

    public bool SetSkill(
        long accountId,
        long dbid,
        int skillId,
        int level)
    {
        if (skillId <= 0 || level <= 0)
            return false;

        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid))
            return false;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO character_skills(
                character_id,skill_id,level,exp)
            VALUES($id,$skill,$level,0)
            ON CONFLICT(character_id,skill_id)
            DO UPDATE SET level=excluded.level
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$skill", skillId);
        Arg(cmd, "$level", Math.Clamp(level, 1, 99));
        return cmd.ExecuteNonQuery() == 1;
    }

    public SkillBookUseResult LearnSkillFromBook(
        long accountId,
        long dbid,
        long itemUid,
        int itemIndex,
        int skillId,
        int targetLevel)
    {
        if (itemUid <= 0 ||
            itemIndex <= 0 ||
            skillId <= 0 ||
            targetLevel <= 0)
        {
            return new(
                false,
                "技能書資料無效",
                skillId,
                Math.Max(0, targetLevel));
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(
                db,
                accountId,
                dbid,
                tx))
        {
            return new(
                false,
                "角色不存在",
                skillId,
                targetLevel);
        }

        int bookCount;
        using (var book = db.CreateCommand())
        {
            book.Transaction = tx;
            book.CommandText = """
                SELECT count
                  FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND item_index=$item
                   AND in_bank=0
                   AND in_shop=0
                 LIMIT 1
                """;
            Arg(book, "$uid", itemUid);
            Arg(book, "$id", dbid);
            Arg(book, "$item", itemIndex);

            var raw = book.ExecuteScalar();
            if (raw is null || raw == DBNull.Value)
            {
                return new(
                    false,
                    "找不到可使用的技能書",
                    skillId,
                    targetLevel);
            }

            bookCount = Convert.ToInt32(raw);
            if (bookCount <= 0)
            {
                return new(
                    false,
                    "技能書數量不足",
                    skillId,
                    targetLevel);
            }
        }

        var currentLevel = 0;
        using (var current = db.CreateCommand())
        {
            current.Transaction = tx;
            current.CommandText = """
                SELECT level
                  FROM character_skills
                 WHERE character_id=$id
                   AND skill_id=$skill
                 LIMIT 1
                """;
            Arg(current, "$id", dbid);
            Arg(current, "$skill", skillId);

            var raw = current.ExecuteScalar();
            if (raw is not null &&
                raw != DBNull.Value)
            {
                currentLevel = Convert.ToInt32(raw);
            }
        }

        if (currentLevel >= targetLevel)
        {
            return new(
                false,
                "已學會同級或更高等級技能",
                skillId,
                currentLevel);
        }

        if (currentLevel != targetLevel - 1)
        {
            return new(
                false,
                targetLevel <= 1
                    ? "技能學習狀態不正確"
                    : $"請先學會前一級技能 Lv.{targetLevel - 1}",
                skillId,
                currentLevel);
        }

        using (var learn = db.CreateCommand())
        {
            learn.Transaction = tx;
            learn.CommandText = """
                INSERT INTO character_skills(
                    character_id,skill_id,level,exp)
                VALUES($id,$skill,$level,0)
                ON CONFLICT(character_id,skill_id)
                DO UPDATE SET
                    level=excluded.level,
                    exp=0
                """;
            Arg(learn, "$id", dbid);
            Arg(learn, "$skill", skillId);
            Arg(learn, "$level", targetLevel);

            if (learn.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    "技能學習失敗",
                    skillId,
                    currentLevel);
            }
        }

        if (bookCount == 1)
        {
            using var delete = db.CreateCommand();
            delete.Transaction = tx;
            delete.CommandText = """
                DELETE FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND item_index=$item
                   AND count=1
                   AND in_bank=0
                   AND in_shop=0
                """;
            Arg(delete, "$uid", itemUid);
            Arg(delete, "$id", dbid);
            Arg(delete, "$item", itemIndex);

            if (delete.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    "技能書扣除失敗",
                    skillId,
                    currentLevel);
            }
        }
        else
        {
            using var reduce = db.CreateCommand();
            reduce.Transaction = tx;
            reduce.CommandText = """
                UPDATE inventory_items
                   SET count=count-1
                 WHERE uid=$uid
                   AND character_id=$id
                   AND item_index=$item
                   AND count > 1
                   AND in_bank=0
                   AND in_shop=0
                """;
            Arg(reduce, "$uid", itemUid);
            Arg(reduce, "$id", dbid);
            Arg(reduce, "$item", itemIndex);

            if (reduce.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    "技能書扣除失敗",
                    skillId,
                    currentLevel);
            }
        }

        tx.Commit();

        return new(
            true,
            $"已學會技能 Lv.{targetLevel}",
            skillId,
            targetLevel);
    }

    public bool ClearSkills(long accountId, long dbid)
    {
        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid))
            return false;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            DELETE FROM character_skills
             WHERE character_id=$id
            """;
        Arg(cmd, "$id", dbid);
        cmd.ExecuteNonQuery();
        return true;
    }

    public Dictionary<string, object>? SpendSkillResources(
        long accountId,
        long dbid,
        int hpCost,
        int mpCost,
        int spCost)
    {
        hpCost = Math.Max(0, hpCost);
        mpCost = Math.Max(0, mpCost);
        spCost = Math.Max(0, spCost);

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET hp = hp - $hp,
                   mp = mp - $mp,
                   sp = sp - $sp
             WHERE id=$id
               AND account_id=$a
               AND hp >= $hp
               AND mp >= $mp
               AND sp >= $sp
            """;
        Arg(cmd, "$hp", hpCost);
        Arg(cmd, "$mp", mpCost);
        Arg(cmd, "$sp", spCost);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        return GetCharacter(accountId, dbid);
    }

    public Dictionary<string, object>? AllocateStat(
        long accountId,
        long dbid,
        string statType)
    {
        var column = statType.Trim().ToUpperInvariant() switch
        {
            "STR" => "str",
            "DEX" => "dex",
            "CON" => "con",
            "WIS" => "wis",
            _ => null
        };

        if (column is null)
            return null;

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"""
            UPDATE characters
               SET [{column}] = [{column}] + 1,
                   exp_use = exp_use - 1
             WHERE id=$id
               AND account_id=$a
               AND exp_use > 0
               AND [{column}] < 100
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        return GetCharacter(accountId, dbid);
    }

    public Dictionary<string, object>? SetPrimaryStats(
        long accountId,
        long dbid,
        int str,
        int dex,
        int con,
        int wis,
        int expUse)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET str=$str,
                   dex=$dex,
                   con=$con,
                   wis=$wis,
                   exp_use=$exp
             WHERE id=$id AND account_id=$a
            """;
        Arg(cmd, "$str", Math.Clamp(str, 0, 100));
        Arg(cmd, "$dex", Math.Clamp(dex, 0, 100));
        Arg(cmd, "$con", Math.Clamp(con, 0, 100));
        Arg(cmd, "$wis", Math.Clamp(wis, 0, 100));
        Arg(cmd, "$exp", Math.Max(0, expUse));
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        return GetCharacter(accountId, dbid);
    }

    public Dictionary<string, object>? ApplyGrowthStats(
        long accountId,
        long dbid,
        int maxHp,
        int maxMp,
        int maxSp,
        int attack,
        int defense,
        int magicAttack,
        int magicDefense,
        int hit,
        int dodge,
        int moveSpeed)
    {
        using var db = Open();

        var before = GetCharacter(accountId, dbid);
        if (before is null)
            return null;

        var oldMaxHp = Convert.ToInt32(before["MaxHP"]);
        var oldMaxMp = Convert.ToInt32(before["MaxMP"]);
        var oldMaxSp = Convert.ToInt32(before["MaxSP"]);
        var oldHp = Convert.ToInt32(before["HP"]);
        var oldMp = Convert.ToInt32(before["MP"]);
        var oldSp = Convert.ToInt32(before["SP"]);

        maxHp = Math.Max(1, maxHp);
        maxMp = Math.Max(0, maxMp);
        maxSp = Math.Max(0, maxSp);

        var newHp = Math.Min(maxHp, Math.Max(0, oldHp + Math.Max(0, maxHp - oldMaxHp)));
        var newMp = Math.Min(maxMp, Math.Max(0, oldMp + Math.Max(0, maxMp - oldMaxMp)));
        var newSp = Math.Min(maxSp, Math.Max(0, oldSp + Math.Max(0, maxSp - oldMaxSp)));

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET hp=$hp,
                   max_hp=$maxHp,
                   mp=$mp,
                   max_mp=$maxMp,
                   sp=$sp,
                   max_sp=$maxSp,
                   attack=$atk,
                   defense=$def,
                   magic_attack=$matk,
                   magic_defense=$mdef,
                   hit=$hit,
                   dodge=$dodge,
                   move_speed=$move
             WHERE id=$id AND account_id=$a
            """;
        Arg(cmd, "$hp", newHp);
        Arg(cmd, "$maxHp", maxHp);
        Arg(cmd, "$mp", newMp);
        Arg(cmd, "$maxMp", maxMp);
        Arg(cmd, "$sp", newSp);
        Arg(cmd, "$maxSp", maxSp);
        Arg(cmd, "$atk", Math.Max(0, attack));
        Arg(cmd, "$def", Math.Max(0, defense));
        Arg(cmd, "$matk", Math.Max(0, magicAttack));
        Arg(cmd, "$mdef", Math.Max(0, magicDefense));
        Arg(cmd, "$hit", Math.Max(0, hit));
        Arg(cmd, "$dodge", Math.Max(0, dodge));
        Arg(cmd, "$move", Math.Max(1, moveSpeed));
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        return GetCharacter(accountId, dbid);
    }

    public long AddInventoryItem(long accountId, long dbid, int itemIndex, int count)
    {
        if (count <= 0) return 0;
        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid)) return 0;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO inventory_items(character_id,item_index,count)
            VALUES($id,$idx,$count);
            SELECT last_insert_rowid();
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$idx", itemIndex);
        Arg(cmd, "$count", count);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public CapacityExpansionResult ExpandCapacityFromItem(
        long accountId,
        long dbid,
        long itemUid,
        int itemIndex,
        int inventoryDelta = 0,
        int equipDelta = 0,
        int petDelta = 0,
        int horseEquipDelta = 0)
    {
        inventoryDelta = Math.Max(0, inventoryDelta);
        equipDelta = Math.Max(0, equipDelta);
        petDelta = Math.Max(0, petDelta);
        horseEquipDelta = Math.Max(0, horseEquipDelta);

        if (itemUid <= 0 ||
            itemIndex <= 0 ||
            inventoryDelta + equipDelta +
                petDelta + horseEquipDelta <= 0)
        {
            return new(
                false,
                "擴充資料無效",
                0,
                0,
                0,
                0);
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
        {
            return new(
                false,
                "角色不存在",
                0,
                0,
                0,
                0);
        }

        int sourceCount;
        using (var source = db.CreateCommand())
        {
            source.Transaction = tx;
            source.CommandText = """
                SELECT count
                  FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND item_index=$item
                   AND in_bank=0
                   AND in_shop=0
                 LIMIT 1
                """;
            Arg(source, "$uid", itemUid);
            Arg(source, "$id", dbid);
            Arg(source, "$item", itemIndex);

            var raw = source.ExecuteScalar();
            if (raw is null)
            {
                return new(
                    false,
                    "找不到可使用的空間擴充卡",
                    0,
                    0,
                    0,
                    0);
            }

            sourceCount = Convert.ToInt32(raw);
            if (sourceCount <= 0)
            {
                return new(
                    false,
                    "空間擴充卡數量不足",
                    0,
                    0,
                    0,
                    0);
            }
        }

        using (var expand = db.CreateCommand())
        {
            expand.Transaction = tx;
            expand.CommandText = """
                UPDATE characters
                   SET inventory_capacity =
                           inventory_capacity + $inventory,
                       equip_capacity =
                           equip_capacity + $equip,
                       pet_capacity =
                           pet_capacity + $pet,
                       horse_equip_capacity =
                           horse_equip_capacity + $horseEquip
                 WHERE id=$id
                   AND account_id=$account
                """;
            Arg(expand, "$inventory", inventoryDelta);
            Arg(expand, "$equip", equipDelta);
            Arg(expand, "$pet", petDelta);
            Arg(expand, "$horseEquip", horseEquipDelta);
            Arg(expand, "$id", dbid);
            Arg(expand, "$account", accountId);

            if (expand.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    "空間擴充失敗",
                    0,
                    0,
                    0,
                    0);
            }
        }

        using (var consume = db.CreateCommand())
        {
            consume.Transaction = tx;
            consume.CommandText = sourceCount == 1
                ? """
                  DELETE FROM inventory_items
                   WHERE uid=$uid
                     AND character_id=$id
                     AND item_index=$item
                     AND in_bank=0
                     AND in_shop=0
                  """
                : """
                  UPDATE inventory_items
                     SET count=count-1
                   WHERE uid=$uid
                     AND character_id=$id
                     AND item_index=$item
                     AND count > 1
                     AND in_bank=0
                     AND in_shop=0
                  """;
            Arg(consume, "$uid", itemUid);
            Arg(consume, "$id", dbid);
            Arg(consume, "$item", itemIndex);

            if (consume.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    "空間擴充卡消耗失敗",
                    0,
                    0,
                    0,
                    0);
            }
        }

        var capacities = ReadCharacterCapacities(
            db,
            dbid,
            tx);

        tx.Commit();

        return new(
            true,
            "空間擴充成功",
            capacities.Inventory,
            capacities.Equip,
            capacities.Pet,
            capacities.HorseEquip);
    }

    public PetSummonExpansionResult ExpandPetSummonCapacityFromItem(
        long accountId,
        long dbid,
        long itemUid,
        int itemIndex)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在", 0);

        var capacities = ReadCharacterCapacities(
            db,
            dbid,
            tx);

        if (capacities.PetSummon >= 3)
        {
            return new(
                false,
                "同時召喚寵物空間已達上限（3）",
                capacities.PetSummon);
        }

        int sourceCount;
        using (var source = db.CreateCommand())
        {
            source.Transaction = tx;
            source.CommandText = """
                SELECT count
                  FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND item_index=$item
                   AND in_bank=0
                   AND in_shop=0
                 LIMIT 1
                """;
            Arg(source, "$uid", itemUid);
            Arg(source, "$id", dbid);
            Arg(source, "$item", itemIndex);

            var raw = source.ExecuteScalar();
            if (raw is null)
            {
                return new(
                    false,
                    "找不到可使用的寵物擴充卡",
                    capacities.PetSummon);
            }

            sourceCount = Convert.ToInt32(raw);
            if (sourceCount <= 0)
            {
                return new(
                    false,
                    "寵物擴充卡數量不足",
                    capacities.PetSummon);
            }
        }

        using (var expand = db.CreateCommand())
        {
            expand.Transaction = tx;
            expand.CommandText = """
                UPDATE characters
                   SET pet_summon_capacity =
                       MIN(3, pet_summon_capacity + 1)
                 WHERE id=$id
                   AND account_id=$account
                   AND pet_summon_capacity < 3
                """;
            Arg(expand, "$id", dbid);
            Arg(expand, "$account", accountId);

            if (expand.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    "寵物召喚空間擴充失敗",
                    capacities.PetSummon);
            }
        }

        using (var consume = db.CreateCommand())
        {
            consume.Transaction = tx;
            consume.CommandText = sourceCount == 1
                ? """
                  DELETE FROM inventory_items
                   WHERE uid=$uid
                     AND character_id=$id
                     AND item_index=$item
                     AND in_bank=0
                     AND in_shop=0
                  """
                : """
                  UPDATE inventory_items
                     SET count=count-1
                   WHERE uid=$uid
                     AND character_id=$id
                     AND item_index=$item
                     AND count > 1
                     AND in_bank=0
                     AND in_shop=0
                  """;
            Arg(consume, "$uid", itemUid);
            Arg(consume, "$id", dbid);
            Arg(consume, "$item", itemIndex);

            if (consume.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    "寵物擴充卡消耗失敗",
                    capacities.PetSummon);
            }
        }

        var updated = ReadCharacterCapacities(
            db,
            dbid,
            tx);

        tx.Commit();

        return new(
            true,
            $"同時召喚寵物空間已增加至 {updated.PetSummon}",
            updated.PetSummon);
    }

    public CharacterStatResetResult ResetCharacterStatsFromItem(
        long accountId,
        long dbid,
        long itemUid,
        int itemIndex,
        int str,
        int dex,
        int con,
        int wis,
        int maxHp,
        int maxMp,
        int maxSp,
        int attack,
        int defense,
        int magicAttack,
        int magicDefense,
        int hit,
        int dodge,
        int moveSpeed)
    {
        str = Math.Clamp(str, 1, 100);
        dex = Math.Clamp(dex, 1, 100);
        con = Math.Clamp(con, 1, 100);
        wis = Math.Clamp(wis, 1, 100);
        maxHp = Math.Max(1, maxHp);
        maxMp = Math.Max(0, maxMp);
        maxSp = Math.Max(0, maxSp);

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在", null);

        int sourceCount;
        using (var source = db.CreateCommand())
        {
            source.Transaction = tx;
            source.CommandText = """
                SELECT count
                  FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND item_index=$item
                   AND in_bank=0
                   AND in_shop=0
                 LIMIT 1
                """;
            Arg(source, "$uid", itemUid);
            Arg(source, "$id", dbid);
            Arg(source, "$item", itemIndex);

            var raw = source.ExecuteScalar();
            if (raw is null)
                return new(false, "找不到可使用的再生水晶", null);

            sourceCount = Convert.ToInt32(raw);
            if (sourceCount <= 0)
                return new(false, "再生水晶數量不足", null);
        }

        int oldStr;
        int oldDex;
        int oldCon;
        int oldWis;
        int oldExpUse;

        using (var current = db.CreateCommand())
        {
            current.Transaction = tx;
            current.CommandText = """
                SELECT str,dex,con,wis,exp_use
                  FROM characters
                 WHERE id=$id
                   AND account_id=$account
                 LIMIT 1
                """;
            Arg(current, "$id", dbid);
            Arg(current, "$account", accountId);

            using var reader = current.ExecuteReader();
            if (!reader.Read())
                return new(false, "角色不存在", null);

            oldStr = reader.GetInt32(0);
            oldDex = reader.GetInt32(1);
            oldCon = reader.GetInt32(2);
            oldWis = reader.GetInt32(3);
            oldExpUse = reader.GetInt32(4);
        }

        var refunded = checked(
            Math.Max(0, oldStr - str) +
            Math.Max(0, oldDex - dex) +
            Math.Max(0, oldCon - con) +
            Math.Max(0, oldWis - wis));
        var newExpUse = checked(
            Math.Max(0, oldExpUse) +
            refunded);

        using (var reset = db.CreateCommand())
        {
            reset.Transaction = tx;
            reset.CommandText = """
                UPDATE characters
                   SET str=$str,
                       dex=$dex,
                       con=$con,
                       wis=$wis,
                       exp_use=$expUse,
                       hp=MIN(
                           $maxHp,
                           MAX(
                               0,
                               hp + MAX(0, $maxHp-max_hp))),
                       max_hp=$maxHp,
                       mp=MIN(
                           $maxMp,
                           MAX(
                               0,
                               mp + MAX(0, $maxMp-max_mp))),
                       max_mp=$maxMp,
                       sp=MIN(
                           $maxSp,
                           MAX(
                               0,
                               sp + MAX(0, $maxSp-max_sp))),
                       max_sp=$maxSp,
                       attack=$attack,
                       defense=$defense,
                       magic_attack=$magicAttack,
                       magic_defense=$magicDefense,
                       hit=$hit,
                       dodge=$dodge,
                       move_speed=$moveSpeed
                 WHERE id=$id
                   AND account_id=$account
                """;
            Arg(reset, "$str", str);
            Arg(reset, "$dex", dex);
            Arg(reset, "$con", con);
            Arg(reset, "$wis", wis);
            Arg(reset, "$expUse", newExpUse);
            Arg(reset, "$maxHp", maxHp);
            Arg(reset, "$maxMp", maxMp);
            Arg(reset, "$maxSp", maxSp);
            Arg(reset, "$attack", Math.Max(0, attack));
            Arg(reset, "$defense", Math.Max(0, defense));
            Arg(reset, "$magicAttack", Math.Max(0, magicAttack));
            Arg(reset, "$magicDefense", Math.Max(0, magicDefense));
            Arg(reset, "$hit", Math.Max(0, hit));
            Arg(reset, "$dodge", Math.Max(0, dodge));
            Arg(reset, "$moveSpeed", Math.Max(1, moveSpeed));
            Arg(reset, "$id", dbid);
            Arg(reset, "$account", accountId);

            if (reset.ExecuteNonQuery() != 1)
                return new(false, "能力點重置失敗", null);
        }

        using (var consume = db.CreateCommand())
        {
            consume.Transaction = tx;
            consume.CommandText = sourceCount == 1
                ? """
                  DELETE FROM inventory_items
                   WHERE uid=$uid
                     AND character_id=$id
                     AND item_index=$item
                     AND in_bank=0
                     AND in_shop=0
                  """
                : """
                  UPDATE inventory_items
                     SET count=count-1
                   WHERE uid=$uid
                     AND character_id=$id
                     AND item_index=$item
                     AND count > 1
                     AND in_bank=0
                     AND in_shop=0
                  """;
            Arg(consume, "$uid", itemUid);
            Arg(consume, "$id", dbid);
            Arg(consume, "$item", itemIndex);

            if (consume.ExecuteNonQuery() != 1)
                return new(false, "再生水晶消耗失敗", null);
        }

        tx.Commit();

        return new(
            true,
            $"能力點已重置，退回 {refunded} 點",
            GetCharacter(accountId, dbid));
    }

    public TeleportItemUseResult UseTeleportItem(
        long accountId,
        long dbid,
        long itemUid,
        int itemIndex,
        int mapId,
        int x,
        int y)
    {
        if (itemUid <= 0 ||
            itemIndex <= 0 ||
            mapId <= 0)
        {
            return new(
                false,
                "傳送資料無效",
                0,
                0,
                0);
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
        {
            return new(
                false,
                "角色不存在",
                0,
                0,
                0);
        }

        int sourceCount;
        using (var source = db.CreateCommand())
        {
            source.Transaction = tx;
            source.CommandText = """
                SELECT count
                  FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND item_index=$item
                   AND in_bank=0
                   AND in_shop=0
                 LIMIT 1
                """;
            Arg(source, "$uid", itemUid);
            Arg(source, "$id", dbid);
            Arg(source, "$item", itemIndex);

            var raw = source.ExecuteScalar();
            if (raw is null)
            {
                return new(
                    false,
                    "找不到可使用的傳送道具",
                    0,
                    0,
                    0);
            }

            sourceCount = Convert.ToInt32(raw);
            if (sourceCount <= 0)
            {
                return new(
                    false,
                    "傳送道具數量不足",
                    0,
                    0,
                    0);
            }
        }

        using (var move = db.CreateCommand())
        {
            move.Transaction = tx;
            move.CommandText = """
                UPDATE characters
                   SET map_id=$map,
                       x=$x,
                       y=$y,
                       state='idle'
                 WHERE id=$id
                   AND account_id=$account
                """;
            Arg(move, "$map", mapId);
            Arg(move, "$x", x);
            Arg(move, "$y", y);
            Arg(move, "$id", dbid);
            Arg(move, "$account", accountId);

            if (move.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    "傳送失敗",
                    0,
                    0,
                    0);
            }
        }

        using (var consume = db.CreateCommand())
        {
            consume.Transaction = tx;
            consume.CommandText = sourceCount == 1
                ? """
                  DELETE FROM inventory_items
                   WHERE uid=$uid
                     AND character_id=$id
                     AND item_index=$item
                     AND in_bank=0
                     AND in_shop=0
                  """
                : """
                  UPDATE inventory_items
                     SET count=count-1
                   WHERE uid=$uid
                     AND character_id=$id
                     AND item_index=$item
                     AND count > 1
                     AND in_bank=0
                     AND in_shop=0
                  """;
            Arg(consume, "$uid", itemUid);
            Arg(consume, "$id", dbid);
            Arg(consume, "$item", itemIndex);

            if (consume.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    "傳送道具消耗失敗",
                    0,
                    0,
                    0);
            }
        }

        tx.Commit();

        return new(
            true,
            "傳送成功",
            mapId,
            x,
            y);
    }

    public (bool Success, string Message, long RewardUid) OpenDoll(
        long accountId,
        long dbid,
        long dollUid,
        int dollItemIndex,
        int rewardItemIndex,
        AssetPurchaseKind rewardKind,
        int rewardCount = 1,
        int rewardPart = 255,
        int rewardDamage = 0)
    {
        rewardCount = Math.Clamp(rewardCount, 1, 999);

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return (false, "角色不存在", 0);

        using (var source = db.CreateCommand())
        {
            source.Transaction = tx;
            source.CommandText = """
                SELECT count,item_index
                  FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND in_bank=0
                   AND in_shop=0
                """;
            Arg(source, "$uid", dollUid);
            Arg(source, "$id", dbid);

            using var r = source.ExecuteReader();
            if (!r.Read() ||
                r.GetInt32(0) <= 0 ||
                r.GetInt32(1) != dollItemIndex)
            {
                return (false, "找不到可使用的娃娃", 0);
            }
        }

        int CountRows(string table, string extra = "")
        {
            using var count = db.CreateCommand();
            count.Transaction = tx;
            count.CommandText =
                $"SELECT COUNT(*) FROM {table} WHERE character_id=$id {extra}";
            Arg(count, "$id", dbid);
            return Convert.ToInt32(count.ExecuteScalar() ?? 0);
        }

        long rewardUid = 0;

        switch (rewardKind)
        {
            case AssetPurchaseKind.General:
            case AssetPurchaseKind.HorseBattery:
            {
                long existingUid = 0;
                int existingCount = 0;
                using (var existing = db.CreateCommand())
                {
                    existing.Transaction = tx;
                    existing.CommandText = """
                        SELECT uid,count
                          FROM inventory_items
                         WHERE character_id=$id
                           AND item_index=$idx
                           AND in_bank=0
                           AND in_shop=0
                         ORDER BY uid
                         LIMIT 1
                        """;
                    Arg(existing, "$id", dbid);
                    Arg(existing, "$idx", rewardItemIndex);

                    using var reader = existing.ExecuteReader();
                    if (reader.Read())
                    {
                        existingUid = reader.GetInt64(0);
                        existingCount = reader.GetInt32(1);
                    }
                }

                if (existingUid > 0)
                {
                    if (existingCount + rewardCount > 999)
                        return (false, "獎勵疊加數量將超過 999", 0);

                    rewardUid = existingUid;

                    using var addCount = db.CreateCommand();
                    addCount.Transaction = tx;
                    addCount.CommandText = """
                        UPDATE inventory_items
                           SET count=count+$count
                         WHERE uid=$uid
                           AND character_id=$id
                        """;
                    Arg(addCount, "$count", rewardCount);
                    Arg(addCount, "$uid", rewardUid);
                    Arg(addCount, "$id", dbid);

                    if (addCount.ExecuteNonQuery() != 1)
                        return (false, "獎勵發放失敗", 0);
                }
                else
                {
                    if (CountRows(
                            "inventory_items",
                            "AND in_bank=0 AND in_shop=0") >=
                        ReadCharacterCapacities(db, dbid, tx).Inventory)
                        return (false, "背包空間不足，未消耗來源道具", 0);

                    using var add = db.CreateCommand();
                    add.Transaction = tx;
                    add.CommandText = """
                        INSERT INTO inventory_items(
                            character_id,item_index,count,in_bank,in_shop)
                        VALUES($id,$idx,$count,0,0);
                        SELECT last_insert_rowid();
                        """;
                    Arg(add, "$id", dbid);
                    Arg(add, "$idx", rewardItemIndex);
                    Arg(add, "$count", rewardCount);
                    rewardUid = Convert.ToInt64(add.ExecuteScalar());
                }

                break;
            }

            case AssetPurchaseKind.Equipment:
            {
                if (CountRows("equipment_items") >=
                    ReadCharacterCapacities(db, dbid, tx).Equip)
                    return (false, "裝備欄空間不足，未消耗來源道具", 0);

                using var add = db.CreateCommand();
                add.Transaction = tx;
                add.CommandText = """
                    INSERT INTO equipment_items(
                        character_id,item_index,damage,color,part,in_bank,in_shop)
                    VALUES($id,$idx,$damage,0,$part,0,0);
                    SELECT last_insert_rowid();
                    """;
                Arg(add, "$id", dbid);
                Arg(add, "$idx", rewardItemIndex);
                Arg(add, "$damage", Math.Max(0, rewardDamage));
                Arg(add, "$part", rewardPart);
                rewardUid = Convert.ToInt64(add.ExecuteScalar());
                break;
            }

            case AssetPurchaseKind.HorseEquipment:
            {
                if (CountRows("horse_equipment") >=
                    ReadCharacterCapacities(db, dbid, tx).HorseEquip)
                    return (false, "機甲裝備欄空間不足，未消耗來源道具", 0);

                using var add = db.CreateCommand();
                add.Transaction = tx;
                add.CommandText = """
                    INSERT INTO horse_equipment(
                        character_id,equip_index,horse_uid,damage,color,part,in_bank,in_shop)
                    VALUES($id,$idx,-1,$damage,0,$part,0,0);
                    SELECT last_insert_rowid();
                    """;
                Arg(add, "$id", dbid);
                Arg(add, "$idx", rewardItemIndex);
                Arg(add, "$damage", Math.Max(0, rewardDamage));
                Arg(add, "$part", rewardPart is >= 0 and <= 6
                    ? rewardPart
                    : 255);
                rewardUid = Convert.ToInt64(add.ExecuteScalar());
                break;
            }

            default:
                return (false, "此娃娃獎勵類型尚未支援", 0);
        }

        ConsumeInventoryItem(db, tx, dbid, dollUid);
        tx.Commit();
        return (true, "抽獎成功", rewardUid);
    }

    public (bool Success, string Message, long RewardUid) OpenFixedRewardBox(
        long accountId,
        long dbid,
        long boxUid,
        int boxItemIndex,
        int rewardItemIndex,
        AssetPurchaseKind rewardKind,
        int rewardCount = 1,
        int rewardPart = 255,
        int rewardDamage = 0)
    {
        var result = OpenDoll(
            accountId,
            dbid,
            boxUid,
            boxItemIndex,
            rewardItemIndex,
            rewardKind,
            rewardCount,
            rewardPart,
            rewardDamage);

        if (result.Success)
            return (true, "開箱成功", result.RewardUid);

        return (
            false,
            result.Message
                .Replace("娃娃", "寶箱", StringComparison.Ordinal)
                .Replace("抽獎", "開箱", StringComparison.Ordinal),
            result.RewardUid);
    }

    public FixedRewardBundleResult OpenFixedRewardBundle(
        long accountId,
        long dbid,
        long boxUid,
        int boxItemIndex,
        IReadOnlyList<FixedRewardGrant> rewards)
    {
        if (boxUid <= 0 ||
            boxItemIndex <= 0 ||
            rewards.Count == 0 ||
            rewards.Any(x =>
                x.ItemIndex <= 0 ||
                x.Count <= 0))
        {
            return new(
                false,
                "寶箱獎勵資料無效",
                Array.Empty<long>());
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(
                db,
                accountId,
                dbid,
                tx))
        {
            return new(
                false,
                "角色不存在",
                Array.Empty<long>());
        }

        using (var source = db.CreateCommand())
        {
            source.Transaction = tx;
            source.CommandText = """
                SELECT count,item_index
                  FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND in_bank=0
                   AND in_shop=0
                 LIMIT 1
                """;
            Arg(source, "$uid", boxUid);
            Arg(source, "$id", dbid);

            using var reader = source.ExecuteReader();
            if (!reader.Read() ||
                reader.GetInt32(0) <= 0 ||
                reader.GetInt32(1) != boxItemIndex)
            {
                return new(
                    false,
                    "找不到可使用的寶箱",
                    Array.Empty<long>());
            }
        }

        int CountRows(
            string table,
            string extra = "")
        {
            using var count = db.CreateCommand();
            count.Transaction = tx;
            count.CommandText =
                $"SELECT COUNT(*) FROM {table} WHERE character_id=$id {extra}";
            Arg(count, "$id", dbid);
            return Convert.ToInt32(
                count.ExecuteScalar() ?? 0);
        }

        var rewardUids = new List<long>(
            rewards.Count);

        foreach (var reward in rewards)
        {
            var count = Math.Clamp(
                reward.Count,
                1,
                999);

            switch (reward.Kind)
            {
                case AssetPurchaseKind.General:
                case AssetPurchaseKind.HorseBattery:
                {
                    long existingUid = 0;
                    int existingCount = 0;

                    using (var existing = db.CreateCommand())
                    {
                        existing.Transaction = tx;
                        existing.CommandText = """
                            SELECT uid,count
                              FROM inventory_items
                             WHERE character_id=$id
                               AND item_index=$idx
                               AND in_bank=0
                               AND in_shop=0
                             ORDER BY uid
                             LIMIT 1
                            """;
                        Arg(existing, "$id", dbid);
                        Arg(existing, "$idx", reward.ItemIndex);

                        using var reader = existing.ExecuteReader();
                        if (reader.Read())
                        {
                            existingUid = reader.GetInt64(0);
                            existingCount = reader.GetInt32(1);
                        }
                    }

                    if (existingUid > 0)
                    {
                        if (existingCount + count > 999)
                        {
                            return new(
                                false,
                                "獎勵疊加數量將超過 999，未消耗寶箱",
                                Array.Empty<long>());
                        }

                        using var update = db.CreateCommand();
                        update.Transaction = tx;
                        update.CommandText = """
                            UPDATE inventory_items
                               SET count=count+$count
                             WHERE uid=$uid
                               AND character_id=$id
                            """;
                        Arg(update, "$count", count);
                        Arg(update, "$uid", existingUid);
                        Arg(update, "$id", dbid);

                        if (update.ExecuteNonQuery() != 1)
                        {
                            return new(
                                false,
                                "獎勵發放失敗，未消耗寶箱",
                                Array.Empty<long>());
                        }

                        rewardUids.Add(existingUid);
                    }
                    else
                    {
                        var capacities =
                            ReadCharacterCapacities(
                                db,
                                dbid,
                                tx);
                        if (CountRows(
                                "inventory_items",
                                "AND in_bank=0 AND in_shop=0") >=
                            capacities.Inventory)
                        {
                            return new(
                                false,
                                "背包空間不足，未消耗寶箱",
                                Array.Empty<long>());
                        }

                        using var add = db.CreateCommand();
                        add.Transaction = tx;
                        add.CommandText = """
                            INSERT INTO inventory_items(
                                character_id,item_index,count,in_bank,in_shop)
                            VALUES($id,$idx,$count,0,0);
                            SELECT last_insert_rowid();
                            """;
                        Arg(add, "$id", dbid);
                        Arg(add, "$idx", reward.ItemIndex);
                        Arg(add, "$count", count);

                        rewardUids.Add(
                            Convert.ToInt64(
                                add.ExecuteScalar()));
                    }

                    break;
                }

                case AssetPurchaseKind.Equipment:
                {
                    var capacities =
                        ReadCharacterCapacities(
                            db,
                            dbid,
                            tx);

                    if (CountRows(
                            "equipment_items",
                            "AND in_bank=0 AND in_shop=0") >=
                        capacities.Equip)
                    {
                        return new(
                            false,
                            "裝備欄空間不足，未消耗寶箱",
                            Array.Empty<long>());
                    }

                    using var add = db.CreateCommand();
                    add.Transaction = tx;
                    add.CommandText = """
                        INSERT INTO equipment_items(
                            character_id,item_index,damage,color,part,in_bank,in_shop)
                        VALUES($id,$idx,$damage,0,$part,0,0);
                        SELECT last_insert_rowid();
                        """;
                    Arg(add, "$id", dbid);
                    Arg(add, "$idx", reward.ItemIndex);
                    Arg(add, "$damage", Math.Max(0, reward.Damage));
                    Arg(add, "$part",
                        reward.Part is >= 0 and <= 6
                            ? reward.Part
                            : 255);

                    rewardUids.Add(
                        Convert.ToInt64(
                            add.ExecuteScalar()));
                    break;
                }

                case AssetPurchaseKind.HorseEquipment:
                {
                    var capacities =
                        ReadCharacterCapacities(
                            db,
                            dbid,
                            tx);

                    if (CountRows(
                            "horse_equipment",
                            "AND in_bank=0 AND in_shop=0") >=
                        capacities.HorseEquip)
                    {
                        return new(
                            false,
                            "機甲裝備欄空間不足，未消耗寶箱",
                            Array.Empty<long>());
                    }

                    using var add = db.CreateCommand();
                    add.Transaction = tx;
                    add.CommandText = """
                        INSERT INTO horse_equipment(
                            character_id,equip_index,horse_uid,damage,color,part,in_bank,in_shop)
                        VALUES($id,$idx,-1,$damage,0,$part,0,0);
                        SELECT last_insert_rowid();
                        """;
                    Arg(add, "$id", dbid);
                    Arg(add, "$idx", reward.ItemIndex);
                    Arg(add, "$damage", Math.Max(0, reward.Damage));
                    Arg(add, "$part",
                        reward.Part is >= 0 and <= 3
                            ? reward.Part
                            : ResolveHorseEquipPart(
                                reward.ItemIndex));

                    rewardUids.Add(
                        Convert.ToInt64(
                            add.ExecuteScalar()));
                    break;
                }

                default:
                    return new(
                        false,
                        "此寶箱包含尚未支援的獎勵類型，未消耗寶箱",
                        Array.Empty<long>());
            }
        }

        ConsumeInventoryItem(
            db,
            tx,
            dbid,
            boxUid);

        tx.Commit();

        return new(
            true,
            "開箱成功",
            rewardUids);
    }

    public long AddEquipment(long accountId, long dbid, int itemIndex, int part = 255, int damage = 0, int color = 0)
    {
        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid)) return 0;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO equipment_items(character_id,item_index,damage,color,part)
            VALUES($id,$idx,$damage,$color,$part);
            SELECT last_insert_rowid();
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$idx", itemIndex);
        Arg(cmd, "$damage", Math.Max(0, damage));
        Arg(cmd, "$color", Math.Max(0, color));
        Arg(cmd, "$part", part);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public long AddPet(long accountId, long dbid, int petId, string name)
    {
        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid)) return 0;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO pets(
                character_id,pet_id,name,
                life_count,feed_exp_limit,feed_value,summon_wait_count)
            VALUES(
                $id,$pet,$name,
                10,43200,43200,0);
            SELECT last_insert_rowid();
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$pet", petId);
        Arg(cmd, "$name", name ?? "");
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public long AddHorse(long accountId, long dbid, int horseIndex, string name, string horseType = "MS", int energyLeft = 0)
    {
        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid)) return 0;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO horses(character_id,horse_index,name,horse_type,energy_left)
            VALUES($id,$idx,$name,$type,$energy);
            SELECT last_insert_rowid();
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$idx", horseIndex);
        Arg(cmd, "$name", name ?? "");
        Arg(cmd, "$type", string.IsNullOrWhiteSpace(horseType) ? "MS" : horseType);
        Arg(cmd, "$energy", Math.Max(0, energyLeft));
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public long AddHorseEquipment(long accountId, long dbid, int equipIndex, long horseUid = -1, int part = 255, int damage = 0, int color = 0)
    {
        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid)) return 0;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO horse_equipment(character_id,equip_index,horse_uid,damage,color,part)
            VALUES($id,$idx,$horse,$damage,$color,$part);
            SELECT last_insert_rowid();
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$idx", equipIndex);
        Arg(cmd, "$horse", horseUid);
        Arg(cmd, "$damage", Math.Max(0, damage));
        Arg(cmd, "$color", Math.Max(0, color));
        Arg(cmd, "$part", part is >= 0 and <= 3
            ? part
            : ResolveHorseEquipPart(equipIndex));
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public ShopMutationResult BuyShopItem(
        long accountId,
        long dbid,
        int itemIndex,
        int tabType,
        int count,
        int totalPrice)
    {
        if (itemIndex <= 0 || count <= 0 || totalPrice < 0)
            return new(false, "購買資料無效", 0);

        if (tabType != 0 && count != 1)
            return new(false, "此類物品一次只能購買一件", 0);

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在", 0);

        long currentMoney;
        using (var money = db.CreateCommand())
        {
            money.Transaction = tx;
            money.CommandText = """
                SELECT money
                  FROM characters
                 WHERE id=$id AND account_id=$a
                """;
            Arg(money, "$id", dbid);
            Arg(money, "$a", accountId);
            var raw = money.ExecuteScalar();
            if (raw is null)
                return new(false, "角色不存在", 0);

            currentMoney = Convert.ToInt64(raw);
        }

        if (currentMoney < totalPrice)
            return new(false, "金錢不足", checked((int)Math.Min(int.MaxValue, currentMoney)));

        switch (tabType)
        {
            case 0:
            {
                using var stack = db.CreateCommand();
                stack.Transaction = tx;
                stack.CommandText = """
                    SELECT uid
                      FROM inventory_items
                     WHERE character_id=$id
                       AND item_index=$idx
                       AND in_bank=0
                       AND in_shop=0
                     ORDER BY uid
                     LIMIT 1
                    """;
                Arg(stack, "$id", dbid);
                Arg(stack, "$idx", itemIndex);
                var existing = stack.ExecuteScalar();

                if (existing is not null)
                {
                    using var update = db.CreateCommand();
                    update.Transaction = tx;
                    update.CommandText = """
                        UPDATE inventory_items
                           SET count = count + $count
                         WHERE uid=$uid AND character_id=$id
                        """;
                    Arg(update, "$count", count);
                    Arg(update, "$uid", Convert.ToInt64(existing));
                    Arg(update, "$id", dbid);
                    update.ExecuteNonQuery();
                }
                else
                {
                    using var insert = db.CreateCommand();
                    insert.Transaction = tx;
                    insert.CommandText = """
                        INSERT INTO inventory_items(character_id,item_index,count)
                        VALUES($id,$idx,$count)
                        """;
                    Arg(insert, "$id", dbid);
                    Arg(insert, "$idx", itemIndex);
                    Arg(insert, "$count", count);
                    insert.ExecuteNonQuery();
                }
                break;
            }

            case 1:
            {
                using var insert = db.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO equipment_items(
                        character_id,item_index,damage,color,part,in_bank,in_shop)
                    VALUES($id,$idx,0,0,255,0,0)
                    """;
                Arg(insert, "$id", dbid);
                Arg(insert, "$idx", itemIndex);
                insert.ExecuteNonQuery();
                break;
            }

            case 3:
            {
                using var insert = db.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO horse_equipment(
                        character_id,equip_index,horse_uid,damage,color,part,in_bank,in_shop)
                    VALUES($id,$idx,-1,0,0,255,0,0)
                    """;
                Arg(insert, "$id", dbid);
                Arg(insert, "$idx", itemIndex);
                insert.ExecuteNonQuery();
                break;
            }

            default:
                return new(false, "尚未支援此物品類型", checked((int)Math.Min(int.MaxValue, currentMoney)));
        }

        var newMoney = currentMoney - totalPrice;
        using (var updateMoney = db.CreateCommand())
        {
            updateMoney.Transaction = tx;
            updateMoney.CommandText = """
                UPDATE characters
                   SET money=$money
                 WHERE id=$id AND account_id=$a
                """;
            Arg(updateMoney, "$money", newMoney);
            Arg(updateMoney, "$id", dbid);
            Arg(updateMoney, "$a", accountId);
            updateMoney.ExecuteNonQuery();
        }

        tx.Commit();
        return new(
            true,
            "購買成功",
            checked((int)Math.Min(int.MaxValue, newMoney)));
    }

    public ShopMutationResult SellShopItem(
        long accountId,
        long dbid,
        long uid,
        int tabType,
        int count,
        int unitPrice)
    {
        if (uid <= 0 || count <= 0 || unitPrice < 0)
            return new(false, "出售資料無效", 0);

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在", 0);

        switch (tabType)
        {
            case 0:
            {
                int ownedCount;
                using (var get = db.CreateCommand())
                {
                    get.Transaction = tx;
                    get.CommandText = """
                        SELECT count
                          FROM inventory_items
                         WHERE uid=$uid AND character_id=$id
                           AND in_bank=0 AND in_shop=0
                        """;
                    Arg(get, "$uid", uid);
                    Arg(get, "$id", dbid);
                    var raw = get.ExecuteScalar();
                    if (raw is null)
                        return new(false, "找不到物品", 0);
                    ownedCount = Convert.ToInt32(raw);
                }

                if (ownedCount < count)
                    return new(false, "物品數量不足", 0);

                using (var update = db.CreateCommand())
                {
                    update.Transaction = tx;
                    update.CommandText = """
                        UPDATE inventory_items
                           SET count=count-$count
                         WHERE uid=$uid AND character_id=$id
                        """;
                    Arg(update, "$count", count);
                    Arg(update, "$uid", uid);
                    Arg(update, "$id", dbid);
                    update.ExecuteNonQuery();
                }

                using (var delete = db.CreateCommand())
                {
                    delete.Transaction = tx;
                    delete.CommandText = """
                        DELETE FROM inventory_items
                         WHERE uid=$uid AND character_id=$id AND count<=0
                        """;
                    Arg(delete, "$uid", uid);
                    Arg(delete, "$id", dbid);
                    delete.ExecuteNonQuery();
                }
                break;
            }

            case 1:
            {
                if (count != 1)
                    return new(false, "裝備一次只能出售一件", 0);

                using var delete = db.CreateCommand();
                delete.Transaction = tx;
                delete.CommandText = """
                    DELETE FROM equipment_items
                     WHERE uid=$uid AND character_id=$id
                       AND part=255 AND in_bank=0 AND in_shop=0
                    """;
                Arg(delete, "$uid", uid);
                Arg(delete, "$id", dbid);
                if (delete.ExecuteNonQuery() != 1)
                    return new(false, "已裝備或找不到此物品", 0);
                break;
            }

            case 3:
            {
                if (count != 1)
                    return new(false, "機甲裝備一次只能出售一件", 0);

                using var delete = db.CreateCommand();
                delete.Transaction = tx;
                delete.CommandText = """
                    DELETE FROM horse_equipment
                     WHERE uid=$uid AND character_id=$id
                       AND horse_uid=-1 AND in_bank=0 AND in_shop=0
                    """;
                Arg(delete, "$uid", uid);
                Arg(delete, "$id", dbid);
                if (delete.ExecuteNonQuery() != 1)
                    return new(false, "已安裝或找不到此機甲裝備", 0);
                break;
            }

            default:
                return new(false, "尚未支援此物品類型", 0);
        }

        var income = checked((long)unitPrice * count);
        long newMoney;

        using (var updateMoney = db.CreateCommand())
        {
            updateMoney.Transaction = tx;
            updateMoney.CommandText = """
                UPDATE characters
                   SET money=money+$income
                 WHERE id=$id AND account_id=$a
                """;
            Arg(updateMoney, "$income", income);
            Arg(updateMoney, "$id", dbid);
            Arg(updateMoney, "$a", accountId);
            updateMoney.ExecuteNonQuery();
        }

        using (var readMoney = db.CreateCommand())
        {
            readMoney.Transaction = tx;
            readMoney.CommandText = """
                SELECT money FROM characters
                 WHERE id=$id AND account_id=$a
                """;
            Arg(readMoney, "$id", dbid);
            Arg(readMoney, "$a", accountId);
            newMoney = Convert.ToInt64(readMoney.ExecuteScalar());
        }

        tx.Commit();
        return new(
            true,
            "出售成功",
            checked((int)Math.Min(int.MaxValue, newMoney)));
    }

    public bool EquipItem(long accountId, long dbid, long itemUid, int targetSlot)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return false;

        using (var clear = db.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = """
                UPDATE equipment_items
                   SET part=255
                 WHERE character_id=$id AND part=$part
                """;
            Arg(clear, "$id", dbid);
            Arg(clear, "$part", targetSlot);
            clear.ExecuteNonQuery();
        }

        using var set = db.CreateCommand();
        set.Transaction = tx;
        set.CommandText = """
            UPDATE equipment_items
               SET part=$part
             WHERE uid=$uid AND character_id=$id
               AND in_bank=0 AND in_shop=0
            """;
        Arg(set, "$part", targetSlot);
        Arg(set, "$uid", itemUid);
        Arg(set, "$id", dbid);

        if (set.ExecuteNonQuery() != 1)
            return false;

        tx.Commit();
        return true;
    }

    public bool UnequipItem(long accountId, long dbid, int targetSlot)
    {
        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid)) return false;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE equipment_items
               SET part=255
             WHERE character_id=$id AND part=$part
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$part", targetSlot);
        cmd.ExecuteNonQuery();
        return true;
    }

    public CompanionRuntimeDrainResult DrainCompanionRuntime(
        long accountId,
        long dbid,
        int elapsedSeconds)
    {
        elapsedSeconds = Math.Max(0, elapsedSeconds);
        if (elapsedSeconds <= 0)
        {
            return new CompanionRuntimeDrainResult(
                null,
                false,
                Array.Empty<Dictionary<string, object>>(),
                Array.Empty<long>());
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
        {
            return new CompanionRuntimeDrainResult(
                null,
                false,
                Array.Empty<Dictionary<string, object>>(),
                Array.Empty<long>());
        }

        long horseUid;
        using (var character = db.CreateCommand())
        {
            character.Transaction = tx;
            character.CommandText = """
                SELECT summoned_horse_uid
                  FROM characters
                 WHERE id=$id
                   AND account_id=$account
                 LIMIT 1
                """;
            Arg(character, "$id", dbid);
            Arg(character, "$account", accountId);
            horseUid = Convert.ToInt64(
                character.ExecuteScalar() ?? -1L);
        }

        var activePetUids = new List<long>();
        using (var pets = db.CreateCommand())
        {
            pets.Transaction = tx;
            pets.CommandText = """
                SELECT uid
                  FROM pets
                 WHERE character_id=$id
                   AND state=1
                """;
            Arg(pets, "$id", dbid);

            using var reader = pets.ExecuteReader();
            while (reader.Read())
                activePetUids.Add(reader.GetInt64(0));
        }

        var mechaDismounted = false;

        if (horseUid > 0)
        {
            using (var drainHorse = db.CreateCommand())
            {
                drainHorse.Transaction = tx;
                drainHorse.CommandText = """
                    UPDATE horses
                       SET energy_left=MAX(
                           0,
                           energy_left-$elapsed)
                     WHERE uid=$horse
                       AND character_id=$id
                    """;
                Arg(drainHorse, "$elapsed", elapsedSeconds);
                Arg(drainHorse, "$horse", horseUid);
                Arg(drainHorse, "$id", dbid);

                if (drainHorse.ExecuteNonQuery() != 1)
                {
                    mechaDismounted = true;
                }
            }

            if (!mechaDismounted)
            {
                using var energy = db.CreateCommand();
                energy.Transaction = tx;
                energy.CommandText = """
                    SELECT energy_left
                      FROM horses
                     WHERE uid=$horse
                       AND character_id=$id
                     LIMIT 1
                    """;
                Arg(energy, "$horse", horseUid);
                Arg(energy, "$id", dbid);

                mechaDismounted =
                    Convert.ToInt32(
                        energy.ExecuteScalar() ?? 0) <= 0;
            }

            if (mechaDismounted)
            {
                using var clear = db.CreateCommand();
                clear.Transaction = tx;
                clear.CommandText = """
                    UPDATE characters
                       SET summoned_horse_uid=-1
                     WHERE id=$id
                       AND account_id=$account
                    """;
                Arg(clear, "$id", dbid);
                Arg(clear, "$account", accountId);
                clear.ExecuteNonQuery();
            }
        }

        if (activePetUids.Count > 0)
        {
            using var drainPets = db.CreateCommand();
            drainPets.Transaction = tx;
            drainPets.CommandText = """
                UPDATE pets
                   SET feed_value=MAX(
                           0,
                           feed_value-$elapsed),
                       state=CASE
                           WHEN feed_value-$elapsed <= 0
                           THEN 0
                           ELSE state
                       END
                 WHERE character_id=$id
                   AND state=1
                """;
            Arg(drainPets, "$elapsed", elapsedSeconds);
            Arg(drainPets, "$id", dbid);
            drainPets.ExecuteNonQuery();
        }

        tx.Commit();

        var assets = GetCharacterAssets(accountId, dbid);

        var horse = horseUid > 0
            ? assets.Horses.FirstOrDefault(x =>
                Convert.ToInt64(x["HorseUID"]) == horseUid)
            : null;

        var petUidSet = activePetUids.ToHashSet();
        var affectedPets = assets.Pets
            .Where(x => petUidSet.Contains(
                Convert.ToInt64(x["UID"])))
            .ToArray();

        var recalledPetUids = affectedPets
            .Where(x =>
                Convert.ToInt32(x["State"]) == 0 &&
                Convert.ToInt32(x["FeedValue"]) <= 0)
            .Select(x => Convert.ToInt64(x["UID"]))
            .ToArray();

        return new CompanionRuntimeDrainResult(
            horse,
            mechaDismounted,
            affectedPets,
            recalledPetUids);
    }

    public bool SetSummonedHorse(long accountId, long dbid, long horseUid)
    {
        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid)) return false;

        if (horseUid != -1)
        {
            using var check = db.CreateCommand();
            check.CommandText = """
                SELECT count(*) FROM horses
                 WHERE uid=$horse AND character_id=$id
                   AND energy_left > 0
                """;
            Arg(check, "$horse", horseUid);
            Arg(check, "$id", dbid);
            if (Convert.ToInt32(check.ExecuteScalar()) != 1)
                return false;
        }

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET summoned_horse_uid=$horse
             WHERE id=$id AND account_id=$a
            """;
        Arg(cmd, "$horse", horseUid);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);
        return cmd.ExecuteNonQuery() == 1;
    }

    public Dictionary<string, object>? ChargeHorse(
        long accountId,
        long dbid,
        long horseUid,
        long itemUid,
        int energyAmount,
        int maxEnergy)
    {
        if (energyAmount <= 0)
            return null;

        maxEnergy = maxEnergy > 0
            ? maxEnergy
            : int.MaxValue;

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return null;

        using (var item = db.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = """
                SELECT count
                  FROM inventory_items
                 WHERE uid=$uid AND character_id=$id
                   AND in_bank=0 AND in_shop=0
                """;
            Arg(item, "$uid", itemUid);
            Arg(item, "$id", dbid);

            var rawCount = item.ExecuteScalar();
            if (rawCount is null || Convert.ToInt32(rawCount) <= 0)
                return null;
        }

        using (var horse = db.CreateCommand())
        {
            horse.Transaction = tx;
            horse.CommandText = """
                UPDATE horses
                   SET energy_left = MIN(
                       $maxEnergy,
                       energy_left + $energy)
                 WHERE uid=$horse
                   AND character_id=$id
                   AND energy_left < $maxEnergy
                """;
            Arg(horse, "$energy", energyAmount);
            Arg(horse, "$maxEnergy", maxEnergy);
            Arg(horse, "$horse", horseUid);
            Arg(horse, "$id", dbid);

            if (horse.ExecuteNonQuery() != 1)
                return null;
        }

        using (var consume = db.CreateCommand())
        {
            consume.Transaction = tx;
            consume.CommandText = """
                UPDATE inventory_items
                   SET count = count - 1
                 WHERE uid=$uid AND character_id=$id
                """;
            Arg(consume, "$uid", itemUid);
            Arg(consume, "$id", dbid);
            consume.ExecuteNonQuery();
        }

        using (var cleanup = db.CreateCommand())
        {
            cleanup.Transaction = tx;
            cleanup.CommandText = """
                DELETE FROM inventory_items
                 WHERE uid=$uid AND character_id=$id AND count <= 0
                """;
            Arg(cleanup, "$uid", itemUid);
            Arg(cleanup, "$id", dbid);
            cleanup.ExecuteNonQuery();
        }

        tx.Commit();

        return GetCharacterAssets(accountId, dbid).Horses
            .FirstOrDefault(x => Convert.ToInt64(x["HorseUID"]) == horseUid);
    }

    public bool EquipHorseItem(long accountId, long dbid, long equipUid, long horseUid)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        if (!OwnsCharacter(db, accountId, dbid, tx)) return false;

        int part;
        using (var get = db.CreateCommand())
        {
            get.Transaction = tx;
            get.CommandText = """
                SELECT part FROM horse_equipment
                 WHERE uid=$uid AND character_id=$id
                """;
            Arg(get, "$uid", equipUid);
            Arg(get, "$id", dbid);
            var raw = get.ExecuteScalar();
            if (raw is null) return false;
            part = Convert.ToInt32(raw);
        }

        if (part is < 0 or > 6)
        {
            using var getIndex = db.CreateCommand();
            getIndex.Transaction = tx;
            getIndex.CommandText = """
                SELECT equip_index FROM horse_equipment
                 WHERE uid=$uid AND character_id=$id
                """;
            Arg(getIndex, "$uid", equipUid);
            Arg(getIndex, "$id", dbid);
            part = ResolveHorseEquipPart(
                Convert.ToInt32(getIndex.ExecuteScalar()));
        }

        // Reinforcement cards are defined as slot 3 in GameData, but the
        // recovered client exposes three independent card slots: 3, 4, 5.
        // Pick the first free card slot instead of replacing card #1.
        if (part == 3)
        {
            var occupied = new HashSet<int>();
            using var slots = db.CreateCommand();
            slots.Transaction = tx;
            slots.CommandText = """
                SELECT part
                  FROM horse_equipment
                 WHERE character_id=$id
                   AND horse_uid=$horse
                   AND part IN (3,4,5)
                """;
            Arg(slots, "$id", dbid);
            Arg(slots, "$horse", horseUid);

            using var r = slots.ExecuteReader();
            while (r.Read())
                occupied.Add(r.GetInt32(0));

            part = new[] { 3, 4, 5 }
                .FirstOrDefault(x => !occupied.Contains(x), -1);

            if (part < 0)
                return false;
        }

        using (var clear = db.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = """
                UPDATE horse_equipment
                   SET horse_uid=-1
                 WHERE character_id=$id AND horse_uid=$horse AND part=$part
                """;
            Arg(clear, "$id", dbid);
            Arg(clear, "$horse", horseUid);
            Arg(clear, "$part", part);
            clear.ExecuteNonQuery();
        }

        using var set = db.CreateCommand();
        set.Transaction = tx;
        set.CommandText = """
            UPDATE horse_equipment
               SET horse_uid=$horse,
                   part=$part
             WHERE uid=$uid AND character_id=$id
            """;
        Arg(set, "$horse", horseUid);
        Arg(set, "$part", part);
        Arg(set, "$uid", equipUid);
        Arg(set, "$id", dbid);
        if (set.ExecuteNonQuery() != 1) return false;

        tx.Commit();
        return true;
    }

    public bool UnequipHorseItem(long accountId, long dbid, long equipUid, long horseUid)
    {
        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid)) return false;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE horse_equipment
               SET horse_uid=-1
             WHERE uid=$uid AND character_id=$id AND horse_uid=$horse
            """;
        Arg(cmd, "$uid", equipUid);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$horse", horseUid);
        return cmd.ExecuteNonQuery() == 1;
    }


    public Dictionary<string, object>? SetHorseLevel(
        long accountId,
        long dbid,
        long horseUid,
        int level)
    {
        level = Math.Clamp(level, 1, 200);

        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid))
            return null;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE horses
               SET level=$level,
                   exp=0,
                   max_exp=$maxExp
             WHERE uid=$horse AND character_id=$id
            """;
        Arg(cmd, "$level", level);
        Arg(cmd, "$maxExp", HorseMaxExpForLevel(level));
        Arg(cmd, "$horse", horseUid);
        Arg(cmd, "$id", dbid);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        return GetCharacterAssets(accountId, dbid).Horses
            .FirstOrDefault(x => Convert.ToInt64(x["HorseUID"]) == horseUid);
    }

    public Dictionary<string, object>? AddHorseExperience(
        long accountId,
        long dbid,
        long horseUid,
        int exp)
    {
        if (exp <= 0)
            return null;

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return null;

        int level;
        int currentExp;
        int maxExp;

        using (var get = db.CreateCommand())
        {
            get.Transaction = tx;
            get.CommandText = """
                SELECT level,exp,max_exp
                  FROM horses
                 WHERE uid=$horse AND character_id=$id
                """;
            Arg(get, "$horse", horseUid);
            Arg(get, "$id", dbid);

            using var r = get.ExecuteReader();
            if (!r.Read())
                return null;

            level = Math.Max(1, r.GetInt32(0));
            currentExp = Math.Max(0, r.GetInt32(1));
            maxExp = Math.Max(1, r.GetInt32(2));
        }

        currentExp += exp;

        while (level < 200 && currentExp >= maxExp)
        {
            currentExp -= maxExp;
            level++;
            maxExp = HorseMaxExpForLevel(level);
        }

        using (var update = db.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = """
                UPDATE horses
                   SET level=$level,
                       exp=$exp,
                       max_exp=$maxExp
                 WHERE uid=$horse AND character_id=$id
                """;
            Arg(update, "$level", level);
            Arg(update, "$exp", currentExp);
            Arg(update, "$maxExp", maxExp);
            Arg(update, "$horse", horseUid);
            Arg(update, "$id", dbid);
            if (update.ExecuteNonQuery() != 1)
                return null;
        }

        tx.Commit();

        return GetCharacterAssets(accountId, dbid).Horses
            .FirstOrDefault(x => Convert.ToInt64(x["HorseUID"]) == horseUid);
    }

    private static int HorseMaxExpForLevel(int level)
        => Math.Max(1000, level * 1000);

    public bool SetPetState(long accountId, long dbid, long petUid, int state)
    {
        state = state == 1 ? 1 : 0;

        using var db = Open();
        using var tx = db.BeginTransaction();
        if (!OwnsCharacter(db, accountId, dbid, tx))
            return false;

        int currentState;
        using (var current = db.CreateCommand())
        {
            current.Transaction = tx;
            current.CommandText = """
                SELECT state
                  FROM pets
                 WHERE uid=$uid
                   AND character_id=$id
                 LIMIT 1
                """;
            Arg(current, "$uid", petUid);
            Arg(current, "$id", dbid);

            var raw = current.ExecuteScalar();
            if (raw is null)
                return false;

            currentState = Convert.ToInt32(raw);
        }

        if (state == 1 &&
            currentState != 1)
        {
            var maxSummon = ReadCharacterCapacities(
                db,
                dbid,
                tx).PetSummon;

            using var active = db.CreateCommand();
            active.Transaction = tx;
            active.CommandText = """
                SELECT COUNT(*)
                  FROM pets
                 WHERE character_id=$id
                   AND state=1
                """;
            Arg(active, "$id", dbid);

            if (Convert.ToInt32(
                    active.ExecuteScalar()) >= maxSummon)
            {
                return false;
            }
        }

        if (currentState == state)
        {
            tx.Commit();
            return true;
        }

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE pets
               SET state=$state
             WHERE uid=$uid
               AND character_id=$id
            """;
        Arg(cmd, "$state", state);
        Arg(cmd, "$uid", petUid);
        Arg(cmd, "$id", dbid);

        if (cmd.ExecuteNonQuery() != 1)
            return false;

        tx.Commit();
        return true;
    }

    public Dictionary<string, object>? ApplyPetDamage(
        long accountId,
        long dbid,
        long petUid,
        int damage)
    {
        damage = Math.Max(0, damage);

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return null;

        using (var update = db.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = """
                UPDATE pets
                   SET hp=MAX(0, hp-$damage),
                       state=CASE
                           WHEN hp-$damage <= 0 THEN 0
                           ELSE state
                       END
                 WHERE uid=$pet
                   AND character_id=$id
                """;
            Arg(update, "$damage", damage);
            Arg(update, "$pet", petUid);
            Arg(update, "$id", dbid);

            if (update.ExecuteNonQuery() != 1)
                return null;
        }

        tx.Commit();

        return GetCharacterAssets(accountId, dbid).Pets
            .FirstOrDefault(x =>
                Convert.ToInt64(x["UID"]) == petUid);
    }

    public Dictionary<string, object>? AddPetExperience(
        long accountId,
        long dbid,
        long petUid,
        int exp)
    {
        if (exp <= 0)
            return null;

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return null;

        int level;
        int currentExp;
        int maxExp;
        var levelsGained = 0;

        using (var get = db.CreateCommand())
        {
            get.Transaction = tx;
            get.CommandText = """
                SELECT level,exp,max_exp
                  FROM pets
                 WHERE uid=$pet
                   AND character_id=$id
                """;
            Arg(get, "$pet", petUid);
            Arg(get, "$id", dbid);

            using var reader = get.ExecuteReader();
            if (!reader.Read())
                return null;

            level = Math.Max(1, reader.GetInt32(0));
            currentExp = Math.Max(0, reader.GetInt32(1));
            maxExp = Math.Max(1, reader.GetInt32(2));
        }

        currentExp += exp;

        while (level < 255 && currentExp >= maxExp)
        {
            currentExp -= maxExp;
            level++;
            levelsGained++;
            maxExp = PetMaxExpForLevel(level);
        }

        using (var update = db.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = """
                UPDATE pets
                   SET level=$level,
                       exp=$exp,
                       max_exp=$maxExp,
                       use_exp=use_exp+$abilityPoints
                 WHERE uid=$pet
                   AND character_id=$id
                """;
            Arg(update, "$level", level);
            Arg(update, "$exp", currentExp);
            Arg(update, "$maxExp", maxExp);
            // Legacy Dat/Player/ExpUse.csv grants two points for every
            // gained level and no separate pet-specific ExpUse table exists.
            Arg(update, "$abilityPoints", levelsGained * 2);
            Arg(update, "$pet", petUid);
            Arg(update, "$id", dbid);

            if (update.ExecuteNonQuery() != 1)
                return null;
        }

        tx.Commit();

        return GetCharacterAssets(accountId, dbid).Pets
            .FirstOrDefault(x =>
                Convert.ToInt64(x["UID"]) == petUid);
    }

    private static int PetMaxExpForLevel(int level)
        => Math.Max(100, level * 100);

    public Dictionary<string, object>? SetPetEvolution(
        long accountId,
        long dbid,
        long petUid,
        int evolId)
    {
        if (petUid <= 0 || evolId < 0)
            return null;

        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid))
            return null;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE pets
               SET evol_id=$evol
             WHERE uid=$pet
               AND character_id=$id
            """;
        Arg(cmd, "$evol", evolId);
        Arg(cmd, "$pet", petUid);
        Arg(cmd, "$id", dbid);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        return GetCharacterAssets(accountId, dbid).Pets
            .FirstOrDefault(x =>
                Convert.ToInt64(x["UID"]) == petUid);
    }

    public PetMutationResult FeedPet(
        long accountId,
        long dbid,
        long petUid,
        long itemUid,
        int itemId,
        int feedAmount)
    {
        if (petUid <= 0 ||
            itemUid <= 0 ||
            itemId <= 0 ||
            feedAmount <= 0)
            return new(false, "餵食資料無效", null);

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在", null);

        int feedValue;
        int feedLimit;
        using (var pet = db.CreateCommand())
        {
            pet.Transaction = tx;
            pet.CommandText = """
                SELECT feed_value,feed_exp_limit
                  FROM pets
                 WHERE uid=$pet
                   AND character_id=$id
                """;
            Arg(pet, "$pet", petUid);
            Arg(pet, "$id", dbid);

            using var reader = pet.ExecuteReader();
            if (!reader.Read())
                return new(false, "找不到寵物", null);

            feedValue = Math.Max(0, reader.GetInt32(0));
            feedLimit = Math.Max(1, reader.GetInt32(1));
        }

        if (feedValue >= feedLimit)
            return new(false, "寵物飽食度已滿", null);

        if (!ConsumeRecipeInventory(
                db,
                tx,
                dbid,
                itemId,
                1,
                itemUid))
        {
            return new(false, "找不到可使用的餵食道具", null);
        }

        using (var update = db.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = """
                UPDATE pets
                   SET feed_value=MIN(
                       feed_exp_limit,
                       feed_value+$feed)
                 WHERE uid=$pet
                   AND character_id=$id
                """;
            Arg(update, "$feed", feedAmount);
            Arg(update, "$pet", petUid);
            Arg(update, "$id", dbid);

            if (update.ExecuteNonQuery() != 1)
                return new(false, "寵物餵食失敗", null);
        }

        tx.Commit();

        return new(
            true,
            "寵物飽食度已恢復",
            GetPetAsset(accountId, dbid, petUid),
            InventoryChanged: true);
    }

    public PetMutationResult SetPetMainSkill(
        long accountId,
        long dbid,
        long petUid,
        int skillId)
    {
        if (petUid <= 0 || skillId < 0)
            return new(false, "主技能資料無效", null);

        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid))
            return new(false, "角色不存在", null);

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE pets
               SET main_skill_id=$skill
             WHERE uid=$pet
               AND character_id=$id
            """;
        Arg(cmd, "$skill", skillId);
        Arg(cmd, "$pet", petUid);
        Arg(cmd, "$id", dbid);

        if (cmd.ExecuteNonQuery() != 1)
            return new(false, "找不到寵物", null);

        return new(
            true,
            skillId == 0
                ? "已取消寵物主技能"
                : "寵物主技能已更新",
            GetPetAsset(accountId, dbid, petUid));
    }

    public PetMutationResult AllocatePetStat(
        long accountId,
        long dbid,
        long petUid,
        string statType,
        PetStatAdjustment adjustment)
    {
        var extraColumn = statType.ToUpperInvariant() switch
        {
            "STR" => "extra_str",
            "DEX" => "extra_dex",
            "CON" => "extra_con",
            "WIS" => "extra_wis",
            _ => ""
        };

        if (petUid <= 0 || extraColumn.Length == 0)
            return new(false, "寵物配點資料無效", null);

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在", null);

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            UPDATE pets
               SET use_exp=use_exp-1,
                   {extraColumn}={extraColumn}+1,
                   max_hp=MAX(1,max_hp+$maxHp),
                   hp=MIN(
                       MAX(1,max_hp+$maxHp),
                       hp+MAX(0,$maxHp)),
                   max_mp=MAX(0,max_mp+$maxMp),
                   mp=MIN(
                       MAX(0,max_mp+$maxMp),
                       mp+MAX(0,$maxMp)),
                   attack=MAX(1,attack+$attack),
                   magic_attack=MAX(0,magic_attack+$magicAttack),
                   hit=MAX(0,hit+$hit),
                   dodge=MAX(0,dodge+$dodge)
             WHERE uid=$pet
               AND character_id=$id
               AND use_exp > 0
            """;
        Arg(cmd, "$maxHp", adjustment.MaxHp);
        Arg(cmd, "$maxMp", adjustment.MaxMp);
        Arg(cmd, "$attack", adjustment.Attack);
        Arg(cmd, "$magicAttack", adjustment.MagicAttack);
        Arg(cmd, "$hit", adjustment.Hit);
        Arg(cmd, "$dodge", adjustment.Dodge);
        Arg(cmd, "$pet", petUid);
        Arg(cmd, "$id", dbid);

        if (cmd.ExecuteNonQuery() != 1)
            return new(false, "沒有可用的寵物配點", null);

        tx.Commit();
        return new(
            true,
            $"寵物 {statType.ToUpperInvariant()} 已提升",
            GetPetAsset(accountId, dbid, petUid));
    }

    public PetMutationResult AddPetLife(
        long accountId,
        long dbid,
        long petUid,
        int itemId,
        int maxLife)
    {
        maxLife = Math.Max(1, maxLife);

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在", null);

        int lifeCount;
        using (var pet = db.CreateCommand())
        {
            pet.Transaction = tx;
            pet.CommandText = """
                SELECT life_count
                  FROM pets
                 WHERE uid=$pet
                   AND character_id=$id
                """;
            Arg(pet, "$pet", petUid);
            Arg(pet, "$id", dbid);

            var raw = pet.ExecuteScalar();
            if (raw is null)
                return new(false, "找不到寵物", null);

            lifeCount = Convert.ToInt32(raw);
        }

        if (lifeCount >= maxLife)
            return new(false, "寵物壽命已滿", null);

        if (!ConsumeRecipeInventory(
                db,
                tx,
                dbid,
                itemId,
                1,
                specificUid: null))
        {
            return new(false, "缺少精靈之魂", null);
        }

        using (var update = db.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = """
                UPDATE pets
                   SET life_count=MIN(
                       $maxLife,
                       life_count+1)
                 WHERE uid=$pet
                   AND character_id=$id
                """;
            Arg(update, "$maxLife", maxLife);
            Arg(update, "$pet", petUid);
            Arg(update, "$id", dbid);

            if (update.ExecuteNonQuery() != 1)
                return new(false, "增加寵物壽命失敗", null);
        }

        tx.Commit();
        return new(
            true,
            "寵物壽命增加 1",
            GetPetAsset(accountId, dbid, petUid),
            InventoryChanged: true);
    }

    public PetMutationResult ResetPetStats(
        long accountId,
        long dbid,
        long petUid,
        int itemId,
        PetStatAdjustment adjustment)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在", null);

        int allocated;
        using (var pet = db.CreateCommand())
        {
            pet.Transaction = tx;
            pet.CommandText = """
                SELECT
                    extra_str+extra_dex+extra_con+extra_wis
                  FROM pets
                 WHERE uid=$pet
                   AND character_id=$id
                """;
            Arg(pet, "$pet", petUid);
            Arg(pet, "$id", dbid);

            var raw = pet.ExecuteScalar();
            if (raw is null)
                return new(false, "找不到寵物", null);

            allocated = Math.Max(0, Convert.ToInt32(raw));
        }

        if (allocated <= 0)
            return new(false, "寵物目前沒有已配置的能力點", null);

        if (!ConsumeRecipeInventory(
                db,
                tx,
                dbid,
                itemId,
                1,
                specificUid: null))
        {
            return new(false, "缺少精靈再生水晶", null);
        }

        using (var update = db.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = """
                UPDATE pets
                   SET use_exp=use_exp+$allocated,
                       extra_str=0,
                       extra_dex=0,
                       extra_con=0,
                       extra_wis=0,
                       max_hp=MAX(1,max_hp+$maxHp),
                       hp=MIN(
                           hp,
                           MAX(1,max_hp+$maxHp)),
                       max_mp=MAX(0,max_mp+$maxMp),
                       mp=MIN(
                           mp,
                           MAX(0,max_mp+$maxMp)),
                       attack=MAX(1,attack+$attack),
                       magic_attack=MAX(0,magic_attack+$magicAttack),
                       hit=MAX(0,hit+$hit),
                       dodge=MAX(0,dodge+$dodge)
                 WHERE uid=$pet
                   AND character_id=$id
                """;
            Arg(update, "$allocated", allocated);
            Arg(update, "$maxHp", adjustment.MaxHp);
            Arg(update, "$maxMp", adjustment.MaxMp);
            Arg(update, "$attack", adjustment.Attack);
            Arg(update, "$magicAttack", adjustment.MagicAttack);
            Arg(update, "$hit", adjustment.Hit);
            Arg(update, "$dodge", adjustment.Dodge);
            Arg(update, "$pet", petUid);
            Arg(update, "$id", dbid);

            if (update.ExecuteNonQuery() != 1)
                return new(false, "寵物配點重置失敗", null);
        }

        tx.Commit();
        return new(
            true,
            "寵物能力配點已重置",
            GetPetAsset(accountId, dbid, petUid),
            InventoryChanged: true);
    }

    private Dictionary<string, object>? GetPetAsset(
        long accountId,
        long dbid,
        long petUid)
        => GetCharacterAssets(accountId, dbid).Pets
            .FirstOrDefault(x =>
                Convert.ToInt64(x["UID"]) == petUid);

    public bool RenamePet(long accountId, long dbid, long petUid, string name)
    {
        var clean = (name ?? "").Trim();
        if (clean.Length is < 1 or > 8)
            return false;

        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid)) return false;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE pets
               SET name=$name
             WHERE uid=$uid AND character_id=$id
            """;
        Arg(cmd, "$name", clean);
        Arg(cmd, "$uid", petUid);
        Arg(cmd, "$id", dbid);
        return cmd.ExecuteNonQuery() == 1;
    }

    public bool EquipPetItem(long accountId, long dbid, long petUid, long itemUid, int targetSlot)
    {
        if (targetSlot is < 0 or > 2) return false;

        using var db = Open();
        using var tx = db.BeginTransaction();
        if (!OwnsCharacter(db, accountId, dbid, tx)) return false;

        using (var item = db.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = """
                SELECT count(*)
                  FROM equipment_items
                 WHERE uid=$uid AND character_id=$id
                   AND in_bank=0 AND in_shop=0 AND part=255
                """;
            Arg(item, "$uid", itemUid);
            Arg(item, "$id", dbid);
            if (Convert.ToInt32(item.ExecuteScalar()) != 1)
                return false;
        }

        var column = targetSlot switch
        {
            0 => "equip0",
            1 => "equip1",
            _ => "equip2"
        };

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            $"UPDATE pets SET {column}=$item WHERE uid=$pet AND character_id=$id";
        Arg(cmd, "$item", itemUid);
        Arg(cmd, "$pet", petUid);
        Arg(cmd, "$id", dbid);

        if (cmd.ExecuteNonQuery() != 1)
            return false;

        tx.Commit();
        return true;
    }

    public bool UnequipPetItem(long accountId, long dbid, long petUid, int targetSlot)
    {
        if (targetSlot is < 0 or > 2) return false;

        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid)) return false;

        var column = targetSlot switch
        {
            0 => "equip0",
            1 => "equip1",
            _ => "equip2"
        };

        using var cmd = db.CreateCommand();
        cmd.CommandText =
            $"UPDATE pets SET {column}=0 WHERE uid=$pet AND character_id=$id";
        Arg(cmd, "$pet", petUid);
        Arg(cmd, "$id", dbid);
        return cmd.ExecuteNonQuery() == 1;
    }

    private static int ResolveHorseEquipPart(int equipIndex)
    {
        if (equipIndex is >= 6100 and < 6200) return 0;
        if (equipIndex is >= 6200 and < 6300) return 1;
        if (equipIndex is >= 6300 and < 6400) return 2;
        return 3;
    }

    private static bool OwnsCharacter(
        SqliteConnection db,
        long accountId,
        long dbid,
        SqliteTransaction? tx = null)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT count(*) FROM characters
             WHERE id=$id AND account_id=$a
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    public CraftCommitResult CommitCraft(
        long accountId,
        long dbid,
        long moneyCost,
        IReadOnlyList<CraftIngredient> ingredients,
        IReadOnlyList<CraftGrant> grants)
    {
        moneyCost = Math.Max(0, moneyCost);

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(
                db,
                accountId,
                dbid,
                tx))
            return new(false, "角色不存在", 0);

        var money = ReadCharacterMoney(
            db,
            tx,
            accountId,
            dbid);

        if (money < 0)
            return new(false, "角色不存在", 0);

        if (money < moneyCost)
            return new(false, "金錢不足", money);

        foreach (var ingredient in ingredients
                     .GroupBy(x => x.ItemId)
                     .Select(x => new CraftIngredient(
                         x.Key,
                         x.Sum(y => y.Count))))
        {
            if (ingredient.ItemId <= 0 ||
                ingredient.Count <= 0 ||
                !ConsumeRecipeInventory(
                    db,
                    tx,
                    dbid,
                    ingredient.ItemId,
                    ingredient.Count,
                    specificUid: null))
            {
                return new(
                    false,
                    "材料不足或狀態已變更",
                    money);
            }
        }

        foreach (var grant in grants)
        {
            if (!AddRecipeGrant(
                    db,
                    tx,
                    dbid,
                    grant))
            {
                return new(
                    false,
                    "產物背包空間不足或數量超過上限",
                    money);
            }
        }

        var newMoney = money - moneyCost;
        if (!DebitCharacterMoney(
                db,
                tx,
                accountId,
                dbid,
                moneyCost))
        {
            return new(
                false,
                "合成扣款失敗",
                money);
        }

        tx.Commit();
        return new(
            true,
            "合成提交成功",
            newMoney);
    }

    public CraftCommitResult CommitDismantle(
        long accountId,
        long dbid,
        long moneyCost,
        IReadOnlyList<DismantleTarget> targets,
        IReadOnlyList<CraftGrant> grants)
    {
        moneyCost = Math.Max(0, moneyCost);

        if (targets.Count == 0)
            return new(false, "未選擇分解物品", 0);

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(
                db,
                accountId,
                dbid,
                tx))
            return new(false, "角色不存在", 0);

        var money = ReadCharacterMoney(
            db,
            tx,
            accountId,
            dbid);

        if (money < 0)
            return new(false, "角色不存在", 0);

        if (money < moneyCost)
            return new(false, "金錢不足", money);

        var seen = new HashSet<(int TabType, long Uid)>();

        foreach (var target in targets)
        {
            if (target.Uid <= 0 ||
                target.ItemId <= 0 ||
                target.Count <= 0 ||
                !seen.Add((
                    target.TabType,
                    target.Uid)))
            {
                return new(
                    false,
                    "分解物品資料無效",
                    money);
            }

            if (target.TabType == 0)
            {
                using var check = db.CreateCommand();
                check.Transaction = tx;
                check.CommandText = """
                    SELECT item_index,count
                      FROM inventory_items
                     WHERE uid=$uid
                       AND character_id=$id
                       AND in_bank=0
                       AND in_shop=0
                    """;
                Arg(check, "$uid", target.Uid);
                Arg(check, "$id", dbid);

                using var reader = check.ExecuteReader();
                if (!reader.Read() ||
                    reader.GetInt32(0) !=
                        target.ItemId ||
                    reader.GetInt32(1) <
                        target.Count)
                {
                    return new(
                        false,
                        "分解材料狀態已變更",
                        money);
                }

                reader.Close();

                if (!ConsumeRecipeInventory(
                        db,
                        tx,
                        dbid,
                        target.ItemId,
                        target.Count,
                        target.Uid))
                {
                    return new(
                        false,
                        "分解材料扣除失敗",
                        money);
                }
            }
            else if (target.TabType == 1)
            {
                if (target.Count != 1)
                    return new(
                        false,
                        "裝備一次只能分解一件",
                        money);

                using var remove = db.CreateCommand();
                remove.Transaction = tx;
                remove.CommandText = """
                    DELETE FROM equipment_items
                     WHERE uid=$uid
                       AND character_id=$id
                       AND item_index=$item
                       AND part=255
                       AND in_bank=0
                       AND in_shop=0
                    """;
                Arg(remove, "$uid", target.Uid);
                Arg(remove, "$id", dbid);
                Arg(remove, "$item", target.ItemId);

                if (remove.ExecuteNonQuery() != 1)
                {
                    return new(
                        false,
                        "分解裝備狀態已變更",
                        money);
                }
            }
            else
            {
                return new(
                    false,
                    "不支援的分解物品類型",
                    money);
            }
        }

        foreach (var grant in grants)
        {
            if (!AddRecipeGrant(
                    db,
                    tx,
                    dbid,
                    grant))
            {
                return new(
                    false,
                    "分解產物背包空間不足或數量超過上限",
                    money);
            }
        }

        var newMoney = money - moneyCost;
        if (!DebitCharacterMoney(
                db,
                tx,
                accountId,
                dbid,
                moneyCost))
        {
            return new(
                false,
                "分解扣款失敗",
                money);
        }

        tx.Commit();
        return new(
            true,
            "分解提交成功",
            newMoney);
    }

    private static long ReadCharacterMoney(
        SqliteConnection db,
        SqliteTransaction tx,
        long accountId,
        long dbid)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT money
              FROM characters
             WHERE id=$id
               AND account_id=$account
            """;
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$account", accountId);
        var raw = cmd.ExecuteScalar();
        return raw is null ||
               raw == DBNull.Value
            ? -1
            : Convert.ToInt64(raw);
    }

    private static bool DebitCharacterMoney(
        SqliteConnection db,
        SqliteTransaction tx,
        long accountId,
        long dbid,
        long amount)
    {
        if (amount < 0)
            return false;

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE characters
               SET money=money-$amount
             WHERE id=$id
               AND account_id=$account
               AND money >= $amount
            """;
        Arg(cmd, "$amount", amount);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$account", accountId);
        return cmd.ExecuteNonQuery() == 1;
    }

    private static bool ConsumeRecipeInventory(
        SqliteConnection db,
        SqliteTransaction tx,
        long dbid,
        int itemId,
        int count,
        long? specificUid)
    {
        var rows = new List<(long Uid, int Count)>();

        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = specificUid.HasValue
                ? """
                  SELECT uid,count
                    FROM inventory_items
                   WHERE uid=$uid
                     AND character_id=$id
                     AND item_index=$item
                     AND in_bank=0
                     AND in_shop=0
                  """
                : """
                  SELECT uid,count
                    FROM inventory_items
                   WHERE character_id=$id
                     AND item_index=$item
                     AND in_bank=0
                     AND in_shop=0
                   ORDER BY uid
                  """;
            Arg(cmd, "$id", dbid);
            Arg(cmd, "$item", itemId);
            if (specificUid.HasValue)
                Arg(cmd, "$uid", specificUid.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((
                    reader.GetInt64(0),
                    reader.GetInt32(1)));
            }
        }

        if (rows.Sum(x => x.Count) < count)
            return false;

        var remaining = count;

        foreach (var row in rows)
        {
            if (remaining <= 0)
                break;

            var take = Math.Min(
                remaining,
                row.Count);

            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;

            if (take == row.Count)
            {
                cmd.CommandText =
                    "DELETE FROM inventory_items WHERE uid=$uid";
                Arg(cmd, "$uid", row.Uid);
            }
            else
            {
                cmd.CommandText = """
                    UPDATE inventory_items
                       SET count=count-$count
                     WHERE uid=$uid
                       AND count >= $count
                    """;
                Arg(cmd, "$count", take);
                Arg(cmd, "$uid", row.Uid);
            }

            if (cmd.ExecuteNonQuery() != 1)
                return false;

            remaining -= take;
        }

        return remaining == 0;
    }

    private static bool AddRecipeGrant(
        SqliteConnection db,
        SqliteTransaction tx,
        long dbid,
        CraftGrant grant)
    {
        if (grant.ItemId <= 0 ||
            grant.Count <= 0)
            return false;

        switch (grant.Kind)
        {
            case AssetPurchaseKind.General:
            case AssetPurchaseKind.HorseBattery:
            {
                int currentTotal;
                long stackUid;

                using (var find = db.CreateCommand())
                {
                    find.Transaction = tx;
                    find.CommandText = """
                        SELECT COALESCE(SUM(count),0),
                               COALESCE(MIN(uid),0)
                          FROM inventory_items
                         WHERE character_id=$id
                           AND item_index=$item
                           AND in_bank=0
                           AND in_shop=0
                        """;
                    Arg(find, "$id", dbid);
                    Arg(find, "$item", grant.ItemId);
                    using var reader = find.ExecuteReader();
                    reader.Read();
                    currentTotal = reader.GetInt32(0);
                    stackUid = reader.GetInt64(1);
                }

                if (currentTotal + grant.Count > 999)
                    return false;

                if (stackUid > 0)
                {
                    using var add = db.CreateCommand();
                    add.Transaction = tx;
                    add.CommandText = """
                        UPDATE inventory_items
                           SET count=count+$count
                         WHERE uid=$uid
                        """;
                    Arg(add, "$count", grant.Count);
                    Arg(add, "$uid", stackUid);
                    return add.ExecuteNonQuery() == 1;
                }

                using (var capacity = db.CreateCommand())
                {
                    capacity.Transaction = tx;
                    capacity.CommandText = """
                        SELECT COUNT(*)
                          FROM inventory_items
                         WHERE character_id=$id
                           AND in_bank=0
                           AND in_shop=0
                        """;
                    Arg(capacity, "$id", dbid);

                    if (Convert.ToInt32(
                            capacity.ExecuteScalar()) >=
                            ReadCharacterCapacities(
                                db,
                                dbid,
                                tx).Inventory)
                        return false;
                }

                using var insert = db.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO inventory_items(
                        character_id,item_index,count,
                        in_bank,in_shop)
                    VALUES($id,$item,$count,0,0)
                    """;
                Arg(insert, "$id", dbid);
                Arg(insert, "$item", grant.ItemId);
                Arg(insert, "$count", grant.Count);
                return insert.ExecuteNonQuery() == 1;
            }

            case AssetPurchaseKind.Equipment:
            {
                using (var capacity = db.CreateCommand())
                {
                    capacity.Transaction = tx;
                    capacity.CommandText = """
                        SELECT COUNT(*)
                          FROM equipment_items
                         WHERE character_id=$id
                           AND in_bank=0
                           AND in_shop=0
                        """;
                    Arg(capacity, "$id", dbid);

                    if (Convert.ToInt32(
                            capacity.ExecuteScalar()) +
                        grant.Count >
                            ReadCharacterCapacities(
                                db,
                                dbid,
                                tx).Equip)
                        return false;
                }

                for (var index = 0;
                     index < grant.Count;
                     index++)
                {
                    using var insert = db.CreateCommand();
                    insert.Transaction = tx;
                    insert.CommandText = """
                        INSERT INTO equipment_items(
                            character_id,item_index,
                            damage,color,part,
                            in_bank,in_shop)
                        VALUES(
                            $id,$item,$damage,0,255,0,0)
                        """;
                    Arg(insert, "$id", dbid);
                    Arg(insert, "$item", grant.ItemId);
                    Arg(insert, "$damage",
                        Math.Max(0, grant.Durability));

                    if (insert.ExecuteNonQuery() != 1)
                        return false;
                }

                return true;
            }

            case AssetPurchaseKind.HorseEquipment:
            {
                using (var capacity = db.CreateCommand())
                {
                    capacity.Transaction = tx;
                    capacity.CommandText = """
                        SELECT COUNT(*)
                          FROM horse_equipment
                         WHERE character_id=$id
                           AND in_bank=0
                           AND in_shop=0
                        """;
                    Arg(capacity, "$id", dbid);

                    if (Convert.ToInt32(
                            capacity.ExecuteScalar()) +
                        grant.Count >
                            ReadCharacterCapacities(
                                db,
                                dbid,
                                tx).HorseEquip)
                        return false;
                }

                for (var index = 0;
                     index < grant.Count;
                     index++)
                {
                    using var insert = db.CreateCommand();
                    insert.Transaction = tx;
                    insert.CommandText = """
                        INSERT INTO horse_equipment(
                            character_id,equip_index,
                            horse_uid,damage,color,part,
                            in_bank,in_shop)
                        VALUES(
                            $id,$item,-1,$damage,0,$part,0,0)
                        """;
                    Arg(insert, "$id", dbid);
                    Arg(insert, "$item", grant.ItemId);
                    Arg(insert, "$damage",
                        Math.Max(0, grant.Durability));
                    Arg(insert, "$part", grant.Part);

                    if (insert.ExecuteNonQuery() != 1)
                        return false;
                }

                return true;
            }

            default:
                return false;
        }
    }

    public PlayerShopReserveResult SetPlayerShopAssetState(
        long accountId,
        long dbid,
        long uid,
        int tabType,
        int count,
        bool inShop)
    {
        count = Math.Max(1, count);

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(
                db,
                accountId,
                dbid,
                tx))
            return new(false, 0);

        switch (tabType)
        {
            case 0:
            {
                if (!inShop)
                {
                    using var clear = db.CreateCommand();
                    clear.Transaction = tx;
                    clear.CommandText = """
                        UPDATE inventory_items
                           SET in_shop=0
                         WHERE uid=$uid
                           AND character_id=$id
                           AND in_bank=0
                           AND in_shop=1
                        """;
                    Arg(clear, "$uid", uid);
                    Arg(clear, "$id", dbid);
                    if (clear.ExecuteNonQuery() != 1)
                        return new(false, 0);

                    tx.Commit();
                    return new(true, uid);
                }

                int itemIndex;
                int ownedCount;
                using (var get = db.CreateCommand())
                {
                    get.Transaction = tx;
                    get.CommandText = """
                        SELECT item_index,count
                          FROM inventory_items
                         WHERE uid=$uid
                           AND character_id=$id
                           AND in_bank=0
                           AND in_shop=0
                        """;
                    Arg(get, "$uid", uid);
                    Arg(get, "$id", dbid);
                    using var reader = get.ExecuteReader();
                    if (!reader.Read())
                        return new(false, 0);

                    itemIndex = reader.GetInt32(0);
                    ownedCount = reader.GetInt32(1);
                }

                if (count > ownedCount)
                    return new(false, 0);

                if (count == ownedCount)
                {
                    using var mark = db.CreateCommand();
                    mark.Transaction = tx;
                    mark.CommandText = """
                        UPDATE inventory_items
                           SET in_shop=1
                         WHERE uid=$uid
                           AND character_id=$id
                           AND in_bank=0
                           AND in_shop=0
                        """;
                    Arg(mark, "$uid", uid);
                    Arg(mark, "$id", dbid);
                    if (mark.ExecuteNonQuery() != 1)
                        return new(false, 0);
                }
                else
                {
                    using (var take = db.CreateCommand())
                    {
                        take.Transaction = tx;
                        take.CommandText = """
                            UPDATE inventory_items
                               SET count=count-$count
                             WHERE uid=$uid
                               AND character_id=$id
                               AND in_bank=0
                               AND in_shop=0
                               AND count >= $count
                            """;
                        Arg(take, "$count", count);
                        Arg(take, "$uid", uid);
                        Arg(take, "$id", dbid);
                        if (take.ExecuteNonQuery() != 1)
                            return new(false, 0);
                    }

                    using var add = db.CreateCommand();
                    add.Transaction = tx;
                    add.CommandText = """
                        INSERT INTO inventory_items(
                            character_id,item_index,count,in_bank,in_shop)
                        VALUES($id,$item,$count,0,1);
                        SELECT last_insert_rowid();
                        """;
                    Arg(add, "$id", dbid);
                    Arg(add, "$item", itemIndex);
                    Arg(add, "$count", count);
                    uid = Convert.ToInt64(
                        add.ExecuteScalar());
                }

                break;
            }

            case 1:
            {
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = inShop
                    ? """
                      UPDATE equipment_items
                         SET in_shop=1
                       WHERE uid=$uid
                         AND character_id=$id
                         AND part=255
                         AND in_bank=0
                         AND in_shop=0
                      """
                    : """
                      UPDATE equipment_items
                         SET in_shop=0
                       WHERE uid=$uid
                         AND character_id=$id
                         AND in_bank=0
                         AND in_shop=1
                      """;
                Arg(cmd, "$uid", uid);
                Arg(cmd, "$id", dbid);
                if (cmd.ExecuteNonQuery() != 1)
                    return new(false, 0);
                break;
            }

            case 2:
            {
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = inShop
                    ? """
                      UPDATE pets
                         SET in_shop=1
                       WHERE uid=$uid
                         AND character_id=$id
                         AND state=0
                         AND in_shop=0
                         AND equip0=0
                         AND equip1=0
                         AND equip2=0
                      """
                    : """
                      UPDATE pets
                         SET in_shop=0,
                             shop_price=0
                       WHERE uid=$uid
                         AND character_id=$id
                         AND in_shop=1
                      """;
                Arg(cmd, "$uid", uid);
                Arg(cmd, "$id", dbid);
                if (cmd.ExecuteNonQuery() != 1)
                    return new(false, 0);
                break;
            }

            case 3:
            {
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = inShop
                    ? """
                      UPDATE horse_equipment
                         SET in_shop=1
                       WHERE uid=$uid
                         AND character_id=$id
                         AND horse_uid=-1
                         AND in_bank=0
                         AND in_shop=0
                      """
                    : """
                      UPDATE horse_equipment
                         SET in_shop=0
                       WHERE uid=$uid
                         AND character_id=$id
                         AND in_bank=0
                         AND in_shop=1
                      """;
                Arg(cmd, "$uid", uid);
                Arg(cmd, "$id", dbid);
                if (cmd.ExecuteNonQuery() != 1)
                    return new(false, 0);
                break;
            }

            default:
                return new(false, 0);
        }

        tx.Commit();
        return new(true, uid);
    }

    public void ClearPlayerShopAssets(
        long accountId,
        long dbid)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(
                db,
                accountId,
                dbid,
                tx))
            return;

        foreach (var sql in new[]
                 {
                     "UPDATE inventory_items SET in_shop=0 WHERE character_id=$id AND in_shop=1",
                     "UPDATE equipment_items SET in_shop=0 WHERE character_id=$id AND in_shop=1",
                     "UPDATE pets SET in_shop=0,shop_price=0 WHERE character_id=$id AND in_shop=1",
                     "UPDATE horse_equipment SET in_shop=0 WHERE character_id=$id AND in_shop=1"
                 })
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            Arg(cmd, "$id", dbid);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public PlayerShopPurchaseResult BuyPlayerShopAsset(
        long buyerAccountId,
        long buyerDbid,
        long sellerAccountId,
        long sellerDbid,
        long uid,
        int tabType,
        int count,
        long unitPrice)
    {
        count = Math.Max(1, count);
        unitPrice = Math.Max(1, unitPrice);

        if (buyerDbid <= 0 ||
            sellerDbid <= 0 ||
            buyerDbid == sellerDbid)
        {
            return new(
                false,
                "購買資料無效",
                0,
                0,
                false);
        }

        long total;
        try
        {
            total = checked(
                unitPrice * count);
        }
        catch (OverflowException)
        {
            return new(
                false,
                "交易金額過大",
                0,
                0,
                false);
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        long ReadMoney(
            long accountId,
            long dbid)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT money
                  FROM characters
                 WHERE id=$id
                   AND account_id=$account
                """;
            Arg(cmd, "$id", dbid);
            Arg(cmd, "$account", accountId);
            var raw = cmd.ExecuteScalar();
            return raw is null ||
                   raw == DBNull.Value
                ? -1
                : Convert.ToInt64(raw);
        }

        int CountRows(string sql, long dbid)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            Arg(cmd, "$id", dbid);
            return Convert.ToInt32(
                cmd.ExecuteScalar() ?? 0);
        }

        var buyerMoney =
            ReadMoney(
                buyerAccountId,
                buyerDbid);
        var sellerMoney =
            ReadMoney(
                sellerAccountId,
                sellerDbid);

        if (buyerMoney < 0 ||
            sellerMoney < 0)
        {
            return new(
                false,
                "角色不存在",
                Math.Max(0, buyerMoney),
                Math.Max(0, sellerMoney),
                false);
        }

        if (buyerMoney < total)
        {
            return new(
                false,
                "金錢不足",
                buyerMoney,
                sellerMoney,
                false);
        }

        var soldOut = true;

        switch (tabType)
        {
            case 0:
            {
                int itemIndex;
                int listedCount;
                using (var get = db.CreateCommand())
                {
                    get.Transaction = tx;
                    get.CommandText = """
                        SELECT item_index,count
                          FROM inventory_items
                         WHERE uid=$uid
                           AND character_id=$seller
                           AND in_bank=0
                           AND in_shop=1
                        """;
                    Arg(get, "$uid", uid);
                    Arg(get, "$seller", sellerDbid);
                    using var reader = get.ExecuteReader();
                    if (!reader.Read())
                    {
                        return new(
                            false,
                            "商品已下架",
                            buyerMoney,
                            sellerMoney,
                            false);
                    }

                    itemIndex = reader.GetInt32(0);
                    listedCount = reader.GetInt32(1);
                }

                if (count > listedCount)
                {
                    return new(
                        false,
                        "商品數量不足",
                        buyerMoney,
                        sellerMoney,
                        false);
                }

                int buyerItemTotal;
                long buyerStackUid;
                using (var getBuyer = db.CreateCommand())
                {
                    getBuyer.Transaction = tx;
                    getBuyer.CommandText = """
                        SELECT COALESCE(SUM(count),0),
                               COALESCE(MIN(uid),0)
                          FROM inventory_items
                         WHERE character_id=$buyer
                           AND item_index=$item
                           AND in_bank=0
                           AND in_shop=0
                        """;
                    Arg(getBuyer, "$buyer", buyerDbid);
                    Arg(getBuyer, "$item", itemIndex);
                    using var reader = getBuyer.ExecuteReader();
                    reader.Read();
                    buyerItemTotal = reader.GetInt32(0);
                    buyerStackUid = reader.GetInt64(1);
                }

                if (buyerItemTotal + count > 999)
                {
                    return new(
                        false,
                        "物品疊加數量將超過 999",
                        buyerMoney,
                        sellerMoney,
                        false);
                }

                if (buyerStackUid <= 0 &&
                    CountRows(
                        """
                        SELECT COUNT(*)
                          FROM inventory_items
                         WHERE character_id=$id
                           AND in_bank=0
                           AND in_shop=0
                        """,
                        buyerDbid) >=
                    ReadCharacterCapacities(
                        db,
                        buyerDbid,
                        tx).Inventory)
                {
                    return new(
                        false,
                        "背包空間不足",
                        buyerMoney,
                        sellerMoney,
                        false);
                }

                if (buyerStackUid > 0)
                {
                    using var add = db.CreateCommand();
                    add.Transaction = tx;
                    add.CommandText = """
                        UPDATE inventory_items
                           SET count=count+$count
                         WHERE uid=$uid
                        """;
                    Arg(add, "$count", count);
                    Arg(add, "$uid", buyerStackUid);
                    add.ExecuteNonQuery();
                }
                else
                {
                    using var add = db.CreateCommand();
                    add.Transaction = tx;
                    add.CommandText = """
                        INSERT INTO inventory_items(
                            character_id,item_index,count,in_bank,in_shop)
                        VALUES($buyer,$item,$count,0,0)
                        """;
                    Arg(add, "$buyer", buyerDbid);
                    Arg(add, "$item", itemIndex);
                    Arg(add, "$count", count);
                    add.ExecuteNonQuery();
                }

                if (count == listedCount)
                {
                    using var remove = db.CreateCommand();
                    remove.Transaction = tx;
                    remove.CommandText = """
                        DELETE FROM inventory_items
                         WHERE uid=$uid
                           AND character_id=$seller
                           AND in_shop=1
                        """;
                    Arg(remove, "$uid", uid);
                    Arg(remove, "$seller", sellerDbid);
                    if (remove.ExecuteNonQuery() != 1)
                        return new(false, "商品狀態已變更", buyerMoney, sellerMoney, false);
                }
                else
                {
                    soldOut = false;
                    using var remove = db.CreateCommand();
                    remove.Transaction = tx;
                    remove.CommandText = """
                        UPDATE inventory_items
                           SET count=count-$count
                         WHERE uid=$uid
                           AND character_id=$seller
                           AND in_shop=1
                           AND count >= $count
                        """;
                    Arg(remove, "$count", count);
                    Arg(remove, "$uid", uid);
                    Arg(remove, "$seller", sellerDbid);
                    if (remove.ExecuteNonQuery() != 1)
                        return new(false, "商品狀態已變更", buyerMoney, sellerMoney, false);
                }

                break;
            }

            case 1:
            {
                if (count != 1)
                    return new(false, "裝備一次只能購買一件", buyerMoney, sellerMoney, false);

                if (CountRows(
                        """
                        SELECT COUNT(*)
                          FROM equipment_items
                         WHERE character_id=$id
                           AND in_bank=0
                           AND in_shop=0
                        """,
                        buyerDbid) >=
                    ReadCharacterCapacities(
                        db,
                        buyerDbid,
                        tx).Equip)
                {
                    return new(false, "裝備空間不足", buyerMoney, sellerMoney, false);
                }

                using var move = db.CreateCommand();
                move.Transaction = tx;
                move.CommandText = """
                    UPDATE equipment_items
                       SET character_id=$buyer,
                           in_shop=0
                     WHERE uid=$uid
                       AND character_id=$seller
                       AND part=255
                       AND in_bank=0
                       AND in_shop=1
                    """;
                Arg(move, "$buyer", buyerDbid);
                Arg(move, "$uid", uid);
                Arg(move, "$seller", sellerDbid);
                if (move.ExecuteNonQuery() != 1)
                    return new(false, "商品已下架", buyerMoney, sellerMoney, false);
                break;
            }

            case 2:
            {
                if (count != 1)
                    return new(false, "寵物一次只能購買一隻", buyerMoney, sellerMoney, false);

                if (CountRows(
                        "SELECT COUNT(*) FROM pets WHERE character_id=$id",
                        buyerDbid) >=
                    ReadCharacterCapacities(
                        db,
                        buyerDbid,
                        tx).Pet)
                {
                    return new(false, "寵物空間不足", buyerMoney, sellerMoney, false);
                }

                using var move = db.CreateCommand();
                move.Transaction = tx;
                move.CommandText = """
                    UPDATE pets
                       SET character_id=$buyer,
                           in_shop=0,
                           shop_price=0
                     WHERE uid=$uid
                       AND character_id=$seller
                       AND state=0
                       AND in_shop=1
                       AND equip0=0
                       AND equip1=0
                       AND equip2=0
                    """;
                Arg(move, "$buyer", buyerDbid);
                Arg(move, "$uid", uid);
                Arg(move, "$seller", sellerDbid);
                if (move.ExecuteNonQuery() != 1)
                    return new(false, "商品已下架", buyerMoney, sellerMoney, false);
                break;
            }

            case 3:
            {
                if (count != 1)
                    return new(false, "機甲裝備一次只能購買一件", buyerMoney, sellerMoney, false);

                if (CountRows(
                        """
                        SELECT COUNT(*)
                          FROM horse_equipment
                         WHERE character_id=$id
                           AND in_bank=0
                           AND in_shop=0
                        """,
                        buyerDbid) >=
                    ReadCharacterCapacities(
                        db,
                        buyerDbid,
                        tx).HorseEquip)
                {
                    return new(false, "機甲裝備空間不足", buyerMoney, sellerMoney, false);
                }

                using var move = db.CreateCommand();
                move.Transaction = tx;
                move.CommandText = """
                    UPDATE horse_equipment
                       SET character_id=$buyer,
                           in_shop=0
                     WHERE uid=$uid
                       AND character_id=$seller
                       AND horse_uid=-1
                       AND in_bank=0
                       AND in_shop=1
                    """;
                Arg(move, "$buyer", buyerDbid);
                Arg(move, "$uid", uid);
                Arg(move, "$seller", sellerDbid);
                if (move.ExecuteNonQuery() != 1)
                    return new(false, "商品已下架", buyerMoney, sellerMoney, false);
                break;
            }

            default:
                return new(false, "不支援的商品類型", buyerMoney, sellerMoney, false);
        }

        using (var debit = db.CreateCommand())
        {
            debit.Transaction = tx;
            debit.CommandText = """
                UPDATE characters
                   SET money=money-$total
                 WHERE id=$id
                   AND account_id=$account
                   AND money >= $total
                """;
            Arg(debit, "$total", total);
            Arg(debit, "$id", buyerDbid);
            Arg(debit, "$account", buyerAccountId);
            if (debit.ExecuteNonQuery() != 1)
                return new(false, "購買失敗", buyerMoney, sellerMoney, false);
        }

        using (var credit = db.CreateCommand())
        {
            credit.Transaction = tx;
            credit.CommandText = """
                UPDATE characters
                   SET money=money+$total
                 WHERE id=$id
                   AND account_id=$account
                   AND money <= $maxBeforeCredit
                """;
            Arg(credit, "$total", total);
            Arg(credit, "$maxBeforeCredit", long.MaxValue - total);
            Arg(credit, "$id", sellerDbid);
            Arg(credit, "$account", sellerAccountId);
            if (credit.ExecuteNonQuery() != 1)
                return new(false, "購買失敗", buyerMoney, sellerMoney, false);
        }

        var buyerNewMoney = ReadMoney(
            buyerAccountId,
            buyerDbid);
        var sellerNewMoney = ReadMoney(
            sellerAccountId,
            sellerDbid);

        tx.Commit();

        return new(
            true,
            "購買成功",
            buyerNewMoney,
            sellerNewMoney,
            soldOut);
    }

    public AssetPurchaseResult BuyAsset(
        long accountId,
        long dbid,
        AssetPurchaseKind kind,
        int itemIndex,
        int count,
        int unitPrice,
        int itemPart = 255,
        int itemDamage = 0)
    {
        // The application validates wire quantities too, but persistence must
        // never silently turn a forged zero/negative count into one item, or
        // a negative price into a free purchase. Use 64-bit money arithmetic
        // so large submitted quantities fail the funds check instead of
        // raising an Int32 overflow before the transaction starts.
        if (count <= 0 || unitPrice < 0)
            return new(false, "購買資料無效", 0, 0);

        var total = (long)unitPrice * count;

        using var db = Open();
        using var tx = db.BeginTransaction();

        long currentMoney;
        using (var money = db.CreateCommand())
        {
            money.Transaction = tx;
            money.CommandText = """
                SELECT money
                  FROM characters
                 WHERE id=$id AND account_id=$a
                """;
            Arg(money, "$id", dbid);
            Arg(money, "$a", accountId);
            var raw = money.ExecuteScalar();
            if (raw is null)
                return new(false, "角色不存在", 0, 0);
            currentMoney = Convert.ToInt64(raw);
        }

        if (currentMoney < total)
            return new(false, "金錢不足", 0, currentMoney);

        long uid;
        switch (kind)
        {
            case AssetPurchaseKind.General:
            case AssetPurchaseKind.HorseBattery:
            {
                using var add = db.CreateCommand();
                add.Transaction = tx;
                add.CommandText = """
                    INSERT INTO inventory_items(character_id,item_index,count)
                    VALUES($id,$idx,$count);
                    SELECT last_insert_rowid();
                    """;
                Arg(add, "$id", dbid);
                Arg(add, "$idx", itemIndex);
                Arg(add, "$count", count);
                uid = Convert.ToInt64(add.ExecuteScalar());
                break;
            }

            case AssetPurchaseKind.Equipment:
            {
                using var add = db.CreateCommand();
                add.Transaction = tx;
                add.CommandText = """
                    INSERT INTO equipment_items(character_id,item_index,damage,color,part)
                    VALUES($id,$idx,$damage,0,255);
                    SELECT last_insert_rowid();
                    """;
                Arg(add, "$id", dbid);
                Arg(add, "$idx", itemIndex);
                Arg(add, "$damage", Math.Max(0, itemDamage));
                uid = Convert.ToInt64(add.ExecuteScalar());
                break;
            }

            case AssetPurchaseKind.Horse:
            {
                using var add = db.CreateCommand();
                add.Transaction = tx;
                add.CommandText = """
                    INSERT INTO horses(character_id,horse_index,name,horse_type,energy_left)
                    VALUES($id,$idx,$name,'MS',3600);
                    SELECT last_insert_rowid();
                    """;
                Arg(add, "$id", dbid);
                Arg(add, "$idx", itemIndex);
                Arg(add, "$name", $"Mecha {itemIndex}");
                uid = Convert.ToInt64(add.ExecuteScalar());
                break;
            }

            case AssetPurchaseKind.HorseEquipment:
            {
                using var add = db.CreateCommand();
                add.Transaction = tx;
                add.CommandText = """
                    INSERT INTO horse_equipment(character_id,equip_index,horse_uid,damage,color,part)
                    VALUES($id,$idx,-1,$damage,0,$part);
                    SELECT last_insert_rowid();
                    """;
                Arg(add, "$id", dbid);
                Arg(add, "$idx", itemIndex);
                Arg(add, "$damage", Math.Max(0, itemDamage));
                Arg(add, "$part",
                    itemPart is >= 0 and <= 3
                        ? itemPart
                        : 255);
                uid = Convert.ToInt64(add.ExecuteScalar());
                break;
            }

            default:
                return new(false, "不支援的商品類型", 0, currentMoney);
        }

        using (var spend = db.CreateCommand())
        {
            spend.Transaction = tx;
            spend.CommandText = """
                UPDATE characters
                   SET money = money - $total
                 WHERE id=$id
                   AND account_id=$a
                   AND money >= $total
                """;
            Arg(spend, "$total", total);
            Arg(spend, "$id", dbid);
            Arg(spend, "$a", accountId);
            if (spend.ExecuteNonQuery() != 1)
                return new(false, "購買失敗", 0, currentMoney);
        }

        long newMoney;
        using (var readMoney = db.CreateCommand())
        {
            readMoney.Transaction = tx;
            readMoney.CommandText = """
                SELECT money
                  FROM characters
                 WHERE id=$id AND account_id=$a
                """;
            Arg(readMoney, "$id", dbid);
            Arg(readMoney, "$a", accountId);
            newMoney = Convert.ToInt64(
                readMoney.ExecuteScalar() ?? currentMoney);
        }

        tx.Commit();
        return new(true, "購買成功", uid, newMoney);
    }

    public bool SellAsset(
        long accountId,
        long dbid,
        long uid,
        AssetPurchaseKind kind,
        int count,
        int unitPrice,
        out long newMoney)
    {
        newMoney = 0;
        if (count <= 0 || unitPrice < 0)
            return false;

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return false;

        var gain = 0L;
        var removed = false;

        if (kind is AssetPurchaseKind.General or AssetPurchaseKind.HorseBattery)
        {
            using var get = db.CreateCommand();
            get.Transaction = tx;
            get.CommandText = """
                SELECT count FROM inventory_items
                 WHERE uid=$uid AND character_id=$id
                   AND in_bank=0 AND in_shop=0
                """;
            Arg(get, "$uid", uid);
            Arg(get, "$id", dbid);

            var raw = get.ExecuteScalar();
            if (raw is null)
                return false;

            var owned = Convert.ToInt32(raw);
            var sellCount = Math.Min(count, owned);
            gain = (long)unitPrice * sellCount;

            using var update = db.CreateCommand();
            update.Transaction = tx;

            if (sellCount >= owned)
            {
                update.CommandText = """
                    DELETE FROM inventory_items
                     WHERE uid=$uid AND character_id=$id
                    """;
            }
            else
            {
                update.CommandText = """
                    UPDATE inventory_items
                       SET count=count-$count
                     WHERE uid=$uid AND character_id=$id
                    """;
                Arg(update, "$count", sellCount);
            }

            Arg(update, "$uid", uid);
            Arg(update, "$id", dbid);
            removed = update.ExecuteNonQuery() == 1;
        }
        else if (kind == AssetPurchaseKind.Equipment)
        {
            using var del = db.CreateCommand();
            del.Transaction = tx;
            del.CommandText = """
                DELETE FROM equipment_items
                 WHERE uid=$uid AND character_id=$id
                   AND part=255 AND in_bank=0 AND in_shop=0
                """;
            Arg(del, "$uid", uid);
            Arg(del, "$id", dbid);
            removed = del.ExecuteNonQuery() == 1;
            gain = unitPrice;
        }
        else if (kind == AssetPurchaseKind.HorseEquipment)
        {
            using var del = db.CreateCommand();
            del.Transaction = tx;
            del.CommandText = """
                DELETE FROM horse_equipment
                 WHERE uid=$uid AND character_id=$id
                   AND horse_uid=-1 AND in_bank=0 AND in_shop=0
                """;
            Arg(del, "$uid", uid);
            Arg(del, "$id", dbid);
            removed = del.ExecuteNonQuery() == 1;
            gain = unitPrice;
        }
        else if (kind == AssetPurchaseKind.Horse)
        {
            using (var summoned = db.CreateCommand())
            {
                summoned.Transaction = tx;
                summoned.CommandText = """
                    SELECT count(*)
                      FROM characters
                     WHERE id=$id AND account_id=$a
                       AND summoned_horse_uid=$horse
                    """;
                Arg(summoned, "$id", dbid);
                Arg(summoned, "$a", accountId);
                Arg(summoned, "$horse", uid);

                if (Convert.ToInt32(summoned.ExecuteScalar()) != 0)
                    return false;
            }

            using (var attached = db.CreateCommand())
            {
                attached.Transaction = tx;
                attached.CommandText = """
                    SELECT count(*) FROM horse_equipment
                     WHERE character_id=$id AND horse_uid=$horse
                    """;
                Arg(attached, "$id", dbid);
                Arg(attached, "$horse", uid);

                if (Convert.ToInt32(attached.ExecuteScalar()) != 0)
                    return false;
            }

            using var del = db.CreateCommand();
            del.Transaction = tx;
            del.CommandText = """
                DELETE FROM horses
                 WHERE uid=$uid AND character_id=$id
                """;
            Arg(del, "$uid", uid);
            Arg(del, "$id", dbid);
            removed = del.ExecuteNonQuery() == 1;
            gain = unitPrice;
        }

        if (!removed)
            return false;

        using (var addMoney = db.CreateCommand())
        {
            addMoney.Transaction = tx;
            addMoney.CommandText = """
                UPDATE characters
                   SET money=money+$gain
                 WHERE id=$id AND account_id=$a
                """;
            Arg(addMoney, "$gain", gain);
            Arg(addMoney, "$id", dbid);
            Arg(addMoney, "$a", accountId);

            if (addMoney.ExecuteNonQuery() != 1)
                return false;
        }

        using (var read = db.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = """
                SELECT money FROM characters
                 WHERE id=$id AND account_id=$a
                """;
            Arg(read, "$id", dbid);
            Arg(read, "$a", accountId);
            newMoney = Convert.ToInt64(read.ExecuteScalar());
        }

        tx.Commit();
        return true;
    }

    public GroundItemRuntime? CreateGroundItem(
        long ownerDbid,
        int tabType,
        int itemIndex,
        int count,
        int mapId,
        double x,
        double y,
        int damage = 0,
        int color = 0,
        int part = 255,
        int protectSeconds = 0,
        int lifetimeSeconds = 0)
    {
        if (itemIndex <= 0 || count <= 0)
            return null;

        var now = DateTimeOffset.UtcNow;
        var createdMs = now.ToUnixTimeMilliseconds();
        var protectUntil = protectSeconds > 0
            ? now.AddSeconds(protectSeconds)
            : (DateTimeOffset?)null;
        var expiresAt = lifetimeSeconds > 0
            ? now.AddSeconds(lifetimeSeconds)
            : (DateTimeOffset?)null;

        using var db = Open();
        using var add = db.CreateCommand();
        add.CommandText = """
            INSERT INTO ground_items(
                map_id,owner_dbid,tab_type,item_index,count,
                damage,color,part,x,y,
                created_utc_ms,protect_until_utc_ms,expires_utc_ms)
            VALUES(
                $map,$owner,$tab,$item,$count,
                $damage,$color,$part,$x,$y,
                $created,$protect,$expires);
            SELECT last_insert_rowid();
            """;
        Arg(add, "$map", mapId);
        Arg(add, "$owner", ownerDbid);
        Arg(add, "$tab", tabType);
        Arg(add, "$item", itemIndex);
        Arg(add, "$count", count);
        Arg(add, "$damage", damage);
        Arg(add, "$color", color);
        Arg(add, "$part", part);
        Arg(add, "$x", x);
        Arg(add, "$y", y);
        Arg(add, "$created", createdMs);
        Arg(add, "$protect",
            protectUntil?.ToUnixTimeMilliseconds() ?? 0L);
        Arg(add, "$expires",
            expiresAt?.ToUnixTimeMilliseconds() ?? 0L);

        var groundId = Convert.ToInt64(add.ExecuteScalar());
        return new GroundItemRuntime
        {
            GroundItemId = groundId,
            EntityId = checked(6_000_000 + (int)groundId),
            MapId = mapId,
            TabType = tabType,
            ItemIndex = itemIndex,
            Count = count,
            Damage = damage,
            Color = color,
            Part = part,
            X = x,
            Y = y,
            OwnerDbid = ownerDbid,
            CreatedAt = now,
            ProtectUntil = protectUntil,
            ExpiresAt = expiresAt
        };
    }

    public GroundItemRuntime? DropAssetToGround(
        long accountId,
        long dbid,
        long uid,
        int tabType,
        int count,
        int mapId,
        double x,
        double y)
    {
        count = Math.Max(1, count);

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return null;

        int itemIndex;
        int actualCount = 1;
        int damage = 0;
        int color = 0;
        int part = 255;

        if (tabType is 0 or 5)
        {
            using var get = db.CreateCommand();
            get.Transaction = tx;
            get.CommandText = """
                SELECT item_index,count
                  FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND in_bank=0
                   AND in_shop=0
                """;
            Arg(get, "$uid", uid);
            Arg(get, "$id", dbid);

            using var reader = get.ExecuteReader();
            if (!reader.Read())
                return null;

            itemIndex = reader.GetInt32(0);
            var owned = reader.GetInt32(1);
            if (count > owned)
                return null;

            actualCount = count;
            reader.Close();

            using var change = db.CreateCommand();
            change.Transaction = tx;
            if (count == owned)
            {
                change.CommandText = """
                    DELETE FROM inventory_items
                     WHERE uid=$uid AND character_id=$id
                    """;
            }
            else
            {
                change.CommandText = """
                    UPDATE inventory_items
                       SET count=count-$count
                     WHERE uid=$uid
                       AND character_id=$id
                       AND count >= $count
                    """;
                Arg(change, "$count", count);
            }

            Arg(change, "$uid", uid);
            Arg(change, "$id", dbid);
            if (change.ExecuteNonQuery() != 1)
                return null;
        }
        else if (tabType == 1)
        {
            using var get = db.CreateCommand();
            get.Transaction = tx;
            get.CommandText = """
                SELECT item_index,damage,color,part
                  FROM equipment_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND part=255
                   AND in_bank=0
                   AND in_shop=0
                """;
            Arg(get, "$uid", uid);
            Arg(get, "$id", dbid);
            using var reader = get.ExecuteReader();
            if (!reader.Read())
                return null;
            itemIndex = reader.GetInt32(0);
            damage = reader.GetInt32(1);
            color = reader.GetInt32(2);
            part = reader.GetInt32(3);
            reader.Close();

            using var del = db.CreateCommand();
            del.Transaction = tx;
            del.CommandText = """
                DELETE FROM equipment_items
                 WHERE uid=$uid AND character_id=$id
                """;
            Arg(del, "$uid", uid);
            Arg(del, "$id", dbid);
            if (del.ExecuteNonQuery() != 1)
                return null;
        }
        else if (tabType == 3)
        {
            using var get = db.CreateCommand();
            get.Transaction = tx;
            get.CommandText = """
                SELECT equip_index,damage,color,part
                  FROM horse_equipment
                 WHERE uid=$uid
                   AND character_id=$id
                   AND horse_uid=-1
                   AND in_bank=0
                   AND in_shop=0
                """;
            Arg(get, "$uid", uid);
            Arg(get, "$id", dbid);
            using var reader = get.ExecuteReader();
            if (!reader.Read())
                return null;
            itemIndex = reader.GetInt32(0);
            damage = reader.GetInt32(1);
            color = reader.GetInt32(2);
            part = reader.GetInt32(3);
            reader.Close();

            using var del = db.CreateCommand();
            del.Transaction = tx;
            del.CommandText = """
                DELETE FROM horse_equipment
                 WHERE uid=$uid AND character_id=$id
                """;
            Arg(del, "$uid", uid);
            Arg(del, "$id", dbid);
            if (del.ExecuteNonQuery() != 1)
                return null;
        }
        else
        {
            return null;
        }

        long groundId;
        using (var add = db.CreateCommand())
        {
            add.Transaction = tx;
            add.CommandText = """
                INSERT INTO ground_items(
                    map_id,owner_dbid,tab_type,item_index,count,
                    damage,color,part,x,y,
                    created_utc_ms,protect_until_utc_ms,expires_utc_ms)
                VALUES(
                    $map,$owner,$tab,$item,$count,
                    $damage,$color,$part,$x,$y,
                    $created,0,0);
                SELECT last_insert_rowid();
                """;
            Arg(add, "$map", mapId);
            Arg(add, "$owner", dbid);
            Arg(add, "$tab", tabType);
            Arg(add, "$item", itemIndex);
            Arg(add, "$count", actualCount);
            Arg(add, "$damage", damage);
            Arg(add, "$color", color);
            Arg(add, "$part", part);
            Arg(add, "$x", x);
            Arg(add, "$y", y);
            Arg(add, "$created", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            groundId = Convert.ToInt64(add.ExecuteScalar());
        }

        tx.Commit();
        return new GroundItemRuntime
        {
            GroundItemId = groundId,
            EntityId = checked(6_000_000 + (int)groundId),
            MapId = mapId,
            TabType = tabType,
            ItemIndex = itemIndex,
            Count = actualCount,
            Damage = damage,
            Color = color,
            Part = part,
            X = x,
            Y = y,
            OwnerDbid = dbid,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public IReadOnlyList<GroundItemRuntime> GetGroundItems(int mapId)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = new List<GroundItemRuntime>();
        using var db = Open();

        using (var cleanup = db.CreateCommand())
        {
            cleanup.CommandText = """
                DELETE FROM ground_items
                 WHERE map_id=$map
                   AND expires_utc_ms > 0
                   AND expires_utc_ms <= $now
                """;
            Arg(cleanup, "$map", mapId);
            Arg(cleanup, "$now", nowMs);
            cleanup.ExecuteNonQuery();
        }

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT id,map_id,owner_dbid,tab_type,item_index,count,
                   damage,color,part,x,y,
                   created_utc_ms,protect_until_utc_ms,expires_utc_ms
              FROM ground_items
             WHERE map_id=$map
             ORDER BY id
            """;
        Arg(cmd, "$map", mapId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(ReadGroundItem(reader));

        return result;
    }

    public GroundItemRuntime? RelocateGroundItemIfUnchanged(
        long groundItemId,
        int mapId,
        double expectedX, double expectedY,
        double newX, double newY)
    {
        if (!double.IsFinite(expectedX) || !double.IsFinite(expectedY) ||
            !double.IsFinite(newX) || !double.IsFinite(newY))
            return null;

        using var db = Open();
        using var tx = db.BeginTransaction();

        using (var update = db.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = """
                UPDATE ground_items
                   SET x=$newX, y=$newY
                 WHERE id=$id AND map_id=$map
                   AND x=$oldX AND y=$oldY
                   AND (expires_utc_ms=0 OR expires_utc_ms>$now)
                """;
            Arg(update, "$id", groundItemId);
            Arg(update, "$map", mapId);
            Arg(update, "$oldX", expectedX);
            Arg(update, "$oldY", expectedY);
            Arg(update, "$newX", newX);
            Arg(update, "$newY", newY);
            Arg(update, "$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            update.ExecuteNonQuery();
        }

        // Read the authoritative row even after a CAS conflict. A competing
        // WorldReady may have already repaired it, or the item may have
        // been claimed. Do not return stale coordinates to the AOI index.
        GroundItemRuntime? result = null;
        using (var query = db.CreateCommand())
        {
            query.Transaction = tx;
            query.CommandText = """
                SELECT id,map_id,owner_dbid,tab_type,item_index,count,
                       damage,color,part,x,y,
                       created_utc_ms,protect_until_utc_ms,expires_utc_ms
                  FROM ground_items
                 WHERE id=$id AND map_id=$map
                   AND (expires_utc_ms=0 OR expires_utc_ms>$now)
                """;
            Arg(query, "$id", groundItemId);
            Arg(query, "$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Arg(query, "$map", mapId);
            using var reader = query.ExecuteReader();
            if (reader.Read())
                result = ReadGroundItem(reader);
        }

        tx.Commit();
        return result;
    }

    public IReadOnlyList<int> ClearGroundItems(int mapId)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();

        var entityIds = new List<int>();
        using (var select = db.CreateCommand())
        {
            select.Transaction = tx;
            select.CommandText = """
                SELECT id
                  FROM ground_items
                 WHERE map_id=$map
                 ORDER BY id
                """;
            Arg(select, "$map", mapId);

            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                var groundId = reader.GetInt64(0);
                entityIds.Add(
                    checked(6_000_000 + (int)groundId));
            }
        }

        if (entityIds.Count > 0)
        {
            using var delete = db.CreateCommand();
            delete.Transaction = tx;
            delete.CommandText =
                "DELETE FROM ground_items WHERE map_id=$map";
            Arg(delete, "$map", mapId);
            delete.ExecuteNonQuery();
        }

        tx.Commit();
        return entityIds;
    }

    public IReadOnlyList<int> RemoveExpiredGroundItems(
        int mapId,
        DateTimeOffset now)
    {
        var nowMs = now.ToUnixTimeMilliseconds();
        using var db = Open();
        using var tx = db.BeginTransaction();

        var entityIds = new List<int>();
        using (var select = db.CreateCommand())
        {
            select.Transaction = tx;
            select.CommandText = """
                SELECT id
                  FROM ground_items
                 WHERE map_id=$map
                   AND expires_utc_ms > 0
                   AND expires_utc_ms <= $now
                """;
            Arg(select, "$map", mapId);
            Arg(select, "$now", nowMs);

            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                var groundId = reader.GetInt64(0);
                entityIds.Add(
                    checked(6_000_000 + (int)groundId));
            }
        }

        if (entityIds.Count > 0)
        {
            using var delete = db.CreateCommand();
            delete.Transaction = tx;
            delete.CommandText = """
                DELETE FROM ground_items
                 WHERE map_id=$map
                   AND expires_utc_ms > 0
                   AND expires_utc_ms <= $now
                """;
            Arg(delete, "$map", mapId);
            Arg(delete, "$now", nowMs);
            delete.ExecuteNonQuery();
        }

        tx.Commit();
        return entityIds;
    }

    public (bool Success, string Message, GroundItemRuntime? Item)
        PickUpGroundItem(
            long accountId,
            long dbid,
            long groundItemId)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return (false, "角色不存在", null);

        // Claim the row atomically. Concurrent pickup attempts cannot both
        // observe and later delete the same ground item.
        GroundItemRuntime item;
        using (var claim = db.CreateCommand())
        {
            claim.Transaction = tx;
            claim.CommandText = """
                DELETE FROM ground_items
                 WHERE id=$id
                RETURNING id,map_id,owner_dbid,tab_type,item_index,count,
                          damage,color,part,x,y,
                          created_utc_ms,protect_until_utc_ms,expires_utc_ms
                """;
            Arg(claim, "$id", groundItemId);
            using var reader = claim.ExecuteReader();
            if (!reader.Read())
                return (false, "物品已不存在", null);
            item = ReadGroundItem(reader);
        }

        var now = DateTimeOffset.UtcNow;
        if (item.IsExpired(now))
        {
            tx.Commit();
            return (false, "物品已消失", item);
        }

        if (item.IsProtectedFor(dbid, now))
        {
            // Returning without commit rolls the claim back, preserving the
            // ground item for its owner/protection window.
            return (false, "物品目前受掉落者保護", item);
        }

        using (var add = db.CreateCommand())
        {
            add.Transaction = tx;
            if (item.TabType is 0 or 5)
            {
                add.CommandText = """
                    INSERT INTO inventory_items(
                        character_id,item_index,count,in_bank,in_shop)
                    VALUES($char,$item,$count,0,0)
                    """;
                Arg(add, "$count", item.Count);
            }
            else if (item.TabType == 1)
            {
                add.CommandText = """
                    INSERT INTO equipment_items(
                        character_id,item_index,damage,color,part,in_bank,in_shop)
                    VALUES($char,$item,$damage,$color,$part,0,0)
                    """;
                Arg(add, "$damage", item.Damage);
                Arg(add, "$color", item.Color);
                Arg(add, "$part", item.Part);
            }
            else if (item.TabType == 3)
            {
                add.CommandText = """
                    INSERT INTO horse_equipment(
                        character_id,equip_index,horse_uid,damage,color,part,in_bank,in_shop)
                    VALUES($char,$item,-1,$damage,$color,$part,0,0)
                    """;
                Arg(add, "$damage", item.Damage);
                Arg(add, "$color", item.Color);
                Arg(add, "$part", item.Part);
            }
            else
            {
                return (false, "此類物品目前無法撿回", item);
            }

            Arg(add, "$char", dbid);
            Arg(add, "$item", item.ItemIndex);
            if (add.ExecuteNonQuery() != 1)
                return (false, "撿取失敗", item);
        }

        tx.Commit();
        return (true, "撿取成功", item);
    }

    private static GroundItemRuntime ReadGroundItem(
        SqliteDataReader reader)
    {
        var groundId = reader.GetInt64(0);
        var createdMs = reader.GetInt64(11);
        var protectMs = reader.GetInt64(12);
        var expiresMs = reader.GetInt64(13);

        return new GroundItemRuntime
        {
            GroundItemId = groundId,
            EntityId = checked(6_000_000 + (int)groundId),
            MapId = reader.GetInt32(1),
            OwnerDbid = reader.GetInt64(2),
            TabType = reader.GetInt32(3),
            ItemIndex = reader.GetInt32(4),
            Count = reader.GetInt32(5),
            Damage = reader.GetInt32(6),
            Color = reader.GetInt32(7),
            Part = reader.GetInt32(8),
            X = reader.GetDouble(9),
            Y = reader.GetDouble(10),
            CreatedAt = createdMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(createdMs)
                : DateTimeOffset.UnixEpoch,
            ProtectUntil = protectMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(protectMs)
                : null,
            ExpiresAt = expiresMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(expiresMs)
                : null
        };
    }

    public RepairAssetResult RepairAsset(
        long accountId,
        long dbid,
        long uid,
        bool isMechaEquipment,
        int repairedDurability,
        long cost)
    {
        if (uid <= 0 || repairedDurability <= 0 || cost < 0)
            return new(false, "維修資料無效", 0);

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return new(false, "角色不存在", 0);

        var table = isMechaEquipment
            ? "horse_equipment"
            : "equipment_items";

        // Only a genuinely damaged row can transition to the requested
        // durability. The conditional mutation makes retries/concurrent
        // requests for the same UID idempotent; the money debit below is in
        // the same transaction, so a failed debit rolls this change back.
        using (var updateItem = db.CreateCommand())
        {
            updateItem.Transaction = tx;
            updateItem.CommandText = $"""
                UPDATE {table}
                   SET damage=$damage
                 WHERE uid=$uid
                   AND character_id=$id
                   AND in_bank=0
                   AND in_shop=0
                   AND damage < $damage
                """;
            Arg(updateItem, "$damage", repairedDurability);
            Arg(updateItem, "$uid", uid);
            Arg(updateItem, "$id", dbid);

            if (updateItem.ExecuteNonQuery() != 1)
                return new(false, "找不到可維修的裝備", 0);
        }

        using (var debit = db.CreateCommand())
        {
            debit.Transaction = tx;
            debit.CommandText = """
                UPDATE characters
                   SET money=money-$cost
                 WHERE id=$id
                   AND account_id=$account
                   AND money >= $cost
                """;
            Arg(debit, "$cost", cost);
            Arg(debit, "$id", dbid);
            Arg(debit, "$account", accountId);

            if (debit.ExecuteNonQuery() != 1)
            {
                using var current = db.CreateCommand();
                current.Transaction = tx;
                current.CommandText = """
                    SELECT money
                      FROM characters
                     WHERE id=$id
                       AND account_id=$account
                    """;
                Arg(current, "$id", dbid);
                Arg(current, "$account", accountId);
                var raw = current.ExecuteScalar();
                var money = raw is null || raw == DBNull.Value
                    ? 0
                    : Convert.ToInt64(raw);

                // No commit: disposing the transaction rolls back the
                // equipment mutation together with the failed debit.
                return new(false, "金錢不足", money);
            }
        }

        long newMoney;
        using (var money = db.CreateCommand())
        {
            money.Transaction = tx;
            money.CommandText = """
                SELECT money
                  FROM characters
                 WHERE id=$id
                   AND account_id=$account
                """;
            Arg(money, "$id", dbid);
            Arg(money, "$account", accountId);
            var raw = money.ExecuteScalar();
            if (raw is null || raw == DBNull.Value)
                return new(false, "角色不存在", 0);
            newMoney = Convert.ToInt64(raw);
        }

        tx.Commit();
        return new(true, "維修完成", newMoney);
    }

    public (bool Success, string Message) DiscardAsset(
        long accountId,
        long dbid,
        long uid,
        int tabType,
        int count)
    {
        count = Math.Max(1, count);

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return (false, "角色不存在");

        if (tabType == 2)
        {
            using var pet = db.CreateCommand();
            pet.Transaction = tx;
            pet.CommandText = """
                DELETE FROM pets
                 WHERE uid=$uid
                   AND character_id=$id
                   AND state=0
                   AND in_shop=0
                   AND equip0=0
                   AND equip1=0
                   AND equip2=0
                """;
            Arg(pet, "$uid", uid);
            Arg(pet, "$id", dbid);

            if (pet.ExecuteNonQuery() != 1)
                return (
                    false,
                    "寵物需先收回並卸下所有裝備才能永久丟棄");

            tx.Commit();
            return (true, "寵物已永久丟棄");
        }

        if (tabType == 4)
        {
            using (var attached = db.CreateCommand())
            {
                attached.Transaction = tx;
                attached.CommandText = """
                    SELECT count(*)
                      FROM horse_equipment
                     WHERE character_id=$id
                       AND horse_uid=$horse
                    """;
                Arg(attached, "$id", dbid);
                Arg(attached, "$horse", uid);

                if (Convert.ToInt32(
                        attached.ExecuteScalar() ?? 0) > 0)
                {
                    return (
                        false,
                        "請先卸下機甲上的所有裝備");
                }
            }

            using var horse = db.CreateCommand();
            horse.Transaction = tx;
            horse.CommandText = """
                DELETE FROM horses
                 WHERE uid=$uid
                   AND character_id=$id
                   AND NOT EXISTS(
                       SELECT 1
                         FROM characters
                        WHERE id=$id
                          AND summoned_horse_uid=$uid)
                """;
            Arg(horse, "$uid", uid);
            Arg(horse, "$id", dbid);

            if (horse.ExecuteNonQuery() != 1)
                return (
                    false,
                    "目前駕駛中的機甲無法永久丟棄");

            tx.Commit();
            return (true, "機甲已永久丟棄");
        }

        if (tabType is 0 or 5)
        {
            int ownedCount;
            using (var get = db.CreateCommand())
            {
                get.Transaction = tx;
                get.CommandText = """
                    SELECT count
                      FROM inventory_items
                     WHERE uid=$uid
                       AND character_id=$id
                       AND in_bank=0
                       AND in_shop=0
                    """;
                Arg(get, "$uid", uid);
                Arg(get, "$id", dbid);
                var raw = get.ExecuteScalar();
                if (raw is null || raw == DBNull.Value)
                    return (false, "找不到可丟棄的物品");
                ownedCount = Convert.ToInt32(raw);
            }

            if (count > ownedCount)
                return (false, "丟棄數量超過持有數量");

            if (count == ownedCount)
            {
                using var delete = db.CreateCommand();
                delete.Transaction = tx;
                delete.CommandText = """
                    DELETE FROM inventory_items
                     WHERE uid=$uid AND character_id=$id
                    """;
                Arg(delete, "$uid", uid);
                Arg(delete, "$id", dbid);
                if (delete.ExecuteNonQuery() != 1)
                    return (false, "丟棄失敗");
            }
            else
            {
                using var reduce = db.CreateCommand();
                reduce.Transaction = tx;
                reduce.CommandText = """
                    UPDATE inventory_items
                       SET count=count-$count
                     WHERE uid=$uid
                       AND character_id=$id
                       AND count >= $count
                    """;
                Arg(reduce, "$count", count);
                Arg(reduce, "$uid", uid);
                Arg(reduce, "$id", dbid);
                if (reduce.ExecuteNonQuery() != 1)
                    return (false, "丟棄失敗");
            }

            tx.Commit();
            return (true, "已丟棄物品");
        }

        string table;
        string availabilityPredicate;

        switch (tabType)
        {
            case 1:
                table = "equipment_items";
                availabilityPredicate = "part=255";
                break;

            case 3:
                table = "horse_equipment";
                availabilityPredicate = "horse_uid=-1";
                break;

            default:
                return (false, "不支援的丟棄分類");
        }

        using (var delete = db.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = $"""
                DELETE FROM {table}
                 WHERE uid=$uid
                   AND character_id=$id
                   AND in_bank=0
                   AND in_shop=0
                   AND {availabilityPredicate}
                """;
            Arg(delete, "$uid", uid);
            Arg(delete, "$id", dbid);

            if (delete.ExecuteNonQuery() != 1)
                return (false, "此物品正在使用或不存在");
        }

        tx.Commit();
        return (true, "已丟棄物品");
    }

    public TradeCommitResult CommitTrade(
        long firstAccountId,
        long firstDbid,
        long firstMoney,
        IReadOnlyList<TradeAssetOffer> firstOffers,
        long secondAccountId,
        long secondDbid,
        long secondMoney,
        IReadOnlyList<TradeAssetOffer> secondOffers)
    {
        if (firstDbid <= 0 ||
            secondDbid <= 0 ||
            firstDbid == secondDbid ||
            firstMoney < 0 ||
            secondMoney < 0)
        {
            return new(false, "交易資料無效", 0, 0);
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        long ReadMoney(
            long accountId,
            long dbid)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT money
                  FROM characters
                 WHERE id=$id
                   AND account_id=$account
                """;
            Arg(cmd, "$id", dbid);
            Arg(cmd, "$account", accountId);
            var raw = cmd.ExecuteScalar();
            return raw is null || raw == DBNull.Value
                ? -1
                : Convert.ToInt64(raw);
        }

        int ScalarInt(string sql, params (string Key, object Value)[] args)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            foreach (var arg in args)
                Arg(cmd, arg.Key, arg.Value);
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }

        bool ValidateOffers(
            long ownerDbid,
            long targetDbid,
            IReadOnlyList<TradeAssetOffer> offers,
            out string error)
        {
            error = "";
            var seen = new HashSet<(int TabType, long Uid)>();
            var incomingGeneral = new Dictionary<int, int>();
            var incomingGeneralNewSlots = new HashSet<int>();
            var incomingEquip = 0;
            var incomingPet = 0;
            var incomingMecha = 0;

            foreach (var offer in offers)
            {
                if (offer.Uid <= 0 ||
                    offer.Count <= 0 ||
                    offer.TabType is < 0 or > 3 ||
                    !seen.Add((offer.TabType, offer.Uid)))
                {
                    error = "交易物品資料無效";
                    return false;
                }

                switch (offer.TabType)
                {
                    case 0:
                    {
                        using var cmd = db.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = """
                            SELECT item_index,count
                              FROM inventory_items
                             WHERE uid=$uid
                               AND character_id=$owner
                               AND in_bank=0
                               AND in_shop=0
                            """;
                        Arg(cmd, "$uid", offer.Uid);
                        Arg(cmd, "$owner", ownerDbid);
                        using var reader = cmd.ExecuteReader();
                        if (!reader.Read())
                        {
                            error = "交易物品已不存在";
                            return false;
                        }

                        var itemIndex = reader.GetInt32(0);
                        var ownedCount = reader.GetInt32(1);
                        if (itemIndex != offer.ItemIndex ||
                            offer.Count > ownedCount)
                        {
                            error = "交易物品數量已變更";
                            return false;
                        }

                        incomingGeneral[itemIndex] =
                            incomingGeneral.GetValueOrDefault(itemIndex) +
                            offer.Count;
                        break;
                    }

                    case 1:
                    {
                        using var cmd = db.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = """
                            SELECT item_index
                              FROM equipment_items
                             WHERE uid=$uid
                               AND character_id=$owner
                               AND part=255
                               AND in_bank=0
                               AND in_shop=0
                            """;
                        Arg(cmd, "$uid", offer.Uid);
                        Arg(cmd, "$owner", ownerDbid);
                        var raw = cmd.ExecuteScalar();
                        if (raw is null ||
                            Convert.ToInt32(raw) != offer.ItemIndex ||
                            offer.Count != 1)
                        {
                            error = "裝備狀態已變更";
                            return false;
                        }

                        incomingEquip++;
                        break;
                    }

                    case 2:
                    {
                        using var cmd = db.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = """
                            SELECT pet_id,state,in_shop,equip0,equip1,equip2
                              FROM pets
                             WHERE uid=$uid
                               AND character_id=$owner
                            """;
                        Arg(cmd, "$uid", offer.Uid);
                        Arg(cmd, "$owner", ownerDbid);
                        using var reader = cmd.ExecuteReader();
                        if (!reader.Read() ||
                            reader.GetInt32(0) != offer.ItemIndex ||
                            reader.GetInt32(1) != 0 ||
                            reader.GetInt32(2) != 0 ||
                            reader.GetInt64(3) > 0 ||
                            reader.GetInt64(4) > 0 ||
                            reader.GetInt64(5) > 0 ||
                            offer.Count != 1)
                        {
                            error = "寵物需收回且卸下裝備後才能交易";
                            return false;
                        }

                        incomingPet++;
                        break;
                    }

                    case 3:
                    {
                        using var cmd = db.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = """
                            SELECT equip_index
                              FROM horse_equipment
                             WHERE uid=$uid
                               AND character_id=$owner
                               AND horse_uid=-1
                               AND in_bank=0
                               AND in_shop=0
                            """;
                        Arg(cmd, "$uid", offer.Uid);
                        Arg(cmd, "$owner", ownerDbid);
                        var raw = cmd.ExecuteScalar();
                        if (raw is null ||
                            Convert.ToInt32(raw) != offer.ItemIndex ||
                            offer.Count != 1)
                        {
                            error = "機甲裝備狀態已變更";
                            return false;
                        }

                        incomingMecha++;
                        break;
                    }
                }
            }

            foreach (var pair in incomingGeneral)
            {
                var currentCount = ScalarInt(
                    """
                    SELECT COALESCE(SUM(count),0)
                      FROM inventory_items
                     WHERE character_id=$target
                       AND item_index=$item
                       AND in_bank=0
                       AND in_shop=0
                    """,
                    ("$target", targetDbid),
                    ("$item", pair.Key));

                if (currentCount + pair.Value > 999)
                {
                    error = "對方物品疊加數量將超過 999";
                    return false;
                }

                if (currentCount == 0)
                    incomingGeneralNewSlots.Add(pair.Key);
            }

            var targetGeneralSlots = ScalarInt(
                """
                SELECT COUNT(*)
                  FROM inventory_items
                 WHERE character_id=$target
                   AND in_bank=0
                   AND in_shop=0
                """,
                ("$target", targetDbid));

            var targetEquipSlots = ScalarInt(
                """
                SELECT COUNT(*)
                  FROM equipment_items
                 WHERE character_id=$target
                   AND in_bank=0
                   AND in_shop=0
                """,
                ("$target", targetDbid));

            var targetPetSlots = ScalarInt(
                """
                SELECT COUNT(*)
                  FROM pets
                 WHERE character_id=$target
                """,
                ("$target", targetDbid));

            var targetMechaSlots = ScalarInt(
                """
                SELECT COUNT(*)
                  FROM horse_equipment
                 WHERE character_id=$target
                   AND in_bank=0
                   AND in_shop=0
                """,
                ("$target", targetDbid));

            var targetCapacities = ReadCharacterCapacities(
                db,
                targetDbid,
                tx);

            if (targetGeneralSlots +
                    incomingGeneralNewSlots.Count >
                    targetCapacities.Inventory ||
                targetEquipSlots + incomingEquip >
                    targetCapacities.Equip ||
                targetPetSlots + incomingPet >
                    targetCapacities.Pet ||
                targetMechaSlots + incomingMecha >
                    targetCapacities.HorseEquip)
            {
                error = "對方背包剩餘空間不足";
                return false;
            }

            return true;
        }

        void TransferOffers(
            long ownerDbid,
            long targetDbid,
            IReadOnlyList<TradeAssetOffer> offers)
        {
            foreach (var offer in offers)
            {
                switch (offer.TabType)
                {
                    case 0:
                    {
                        int sourceCount;
                        using (var get = db.CreateCommand())
                        {
                            get.Transaction = tx;
                            get.CommandText = """
                                SELECT count
                                  FROM inventory_items
                                 WHERE uid=$uid
                                   AND character_id=$owner
                                """;
                            Arg(get, "$uid", offer.Uid);
                            Arg(get, "$owner", ownerDbid);
                            sourceCount =
                                Convert.ToInt32(get.ExecuteScalar());
                        }

                        long targetUid;
                        using (var find = db.CreateCommand())
                        {
                            find.Transaction = tx;
                            find.CommandText = """
                                SELECT uid
                                  FROM inventory_items
                                 WHERE character_id=$target
                                   AND item_index=$item
                                   AND in_bank=0
                                   AND in_shop=0
                                 ORDER BY uid
                                 LIMIT 1
                                """;
                            Arg(find, "$target", targetDbid);
                            Arg(find, "$item", offer.ItemIndex);
                            var raw = find.ExecuteScalar();
                            targetUid = raw is null ||
                                        raw == DBNull.Value
                                ? 0
                                : Convert.ToInt64(raw);
                        }

                        if (targetUid > 0)
                        {
                            using var add = db.CreateCommand();
                            add.Transaction = tx;
                            add.CommandText = """
                                UPDATE inventory_items
                                   SET count=count+$count
                                 WHERE uid=$uid
                                """;
                            Arg(add, "$count", offer.Count);
                            Arg(add, "$uid", targetUid);
                            add.ExecuteNonQuery();

                            using var take = db.CreateCommand();
                            take.Transaction = tx;
                            take.CommandText =
                                sourceCount == offer.Count
                                    ? "DELETE FROM inventory_items WHERE uid=$uid"
                                    : """
                                      UPDATE inventory_items
                                         SET count=count-$count
                                       WHERE uid=$uid
                                      """;
                            Arg(take, "$uid", offer.Uid);
                            if (sourceCount != offer.Count)
                                Arg(take, "$count", offer.Count);
                            take.ExecuteNonQuery();
                        }
                        else if (sourceCount == offer.Count)
                        {
                            using var move = db.CreateCommand();
                            move.Transaction = tx;
                            move.CommandText = """
                                UPDATE inventory_items
                                   SET character_id=$target
                                 WHERE uid=$uid
                                """;
                            Arg(move, "$target", targetDbid);
                            Arg(move, "$uid", offer.Uid);
                            move.ExecuteNonQuery();
                        }
                        else
                        {
                            using var take = db.CreateCommand();
                            take.Transaction = tx;
                            take.CommandText = """
                                UPDATE inventory_items
                                   SET count=count-$count
                                 WHERE uid=$uid
                                """;
                            Arg(take, "$count", offer.Count);
                            Arg(take, "$uid", offer.Uid);
                            take.ExecuteNonQuery();

                            using var add = db.CreateCommand();
                            add.Transaction = tx;
                            add.CommandText = """
                                INSERT INTO inventory_items(
                                    character_id,item_index,count)
                                VALUES($target,$item,$count)
                                """;
                            Arg(add, "$target", targetDbid);
                            Arg(add, "$item", offer.ItemIndex);
                            Arg(add, "$count", offer.Count);
                            add.ExecuteNonQuery();
                        }

                        break;
                    }

                    case 1:
                    {
                        using var move = db.CreateCommand();
                        move.Transaction = tx;
                        move.CommandText = """
                            UPDATE equipment_items
                               SET character_id=$target
                             WHERE uid=$uid
                               AND character_id=$owner
                            """;
                        Arg(move, "$target", targetDbid);
                        Arg(move, "$uid", offer.Uid);
                        Arg(move, "$owner", ownerDbid);
                        move.ExecuteNonQuery();
                        break;
                    }

                    case 2:
                    {
                        using var move = db.CreateCommand();
                        move.Transaction = tx;
                        move.CommandText = """
                            UPDATE pets
                               SET character_id=$target
                             WHERE uid=$uid
                               AND character_id=$owner
                            """;
                        Arg(move, "$target", targetDbid);
                        Arg(move, "$uid", offer.Uid);
                        Arg(move, "$owner", ownerDbid);
                        move.ExecuteNonQuery();
                        break;
                    }

                    case 3:
                    {
                        using var move = db.CreateCommand();
                        move.Transaction = tx;
                        move.CommandText = """
                            UPDATE horse_equipment
                               SET character_id=$target
                             WHERE uid=$uid
                               AND character_id=$owner
                            """;
                        Arg(move, "$target", targetDbid);
                        Arg(move, "$uid", offer.Uid);
                        Arg(move, "$owner", ownerDbid);
                        move.ExecuteNonQuery();
                        break;
                    }
                }
            }
        }

        var firstBalance =
            ReadMoney(firstAccountId, firstDbid);
        var secondBalance =
            ReadMoney(secondAccountId, secondDbid);

        if (firstBalance < 0 ||
            secondBalance < 0)
            return new(false, "交易角色不存在", 0, 0);

        if (firstBalance < firstMoney ||
            secondBalance < secondMoney)
        {
            return new(
                false,
                "交易金額已超過目前持有金錢",
                firstBalance,
                secondBalance);
        }

        if (!ValidateOffers(
                firstDbid,
                secondDbid,
                firstOffers,
                out var firstError))
        {
            return new(
                false,
                firstError,
                firstBalance,
                secondBalance);
        }

        if (!ValidateOffers(
                secondDbid,
                firstDbid,
                secondOffers,
                out var secondError))
        {
            return new(
                false,
                secondError,
                firstBalance,
                secondBalance);
        }

        TransferOffers(
            firstDbid,
            secondDbid,
            firstOffers);
        TransferOffers(
            secondDbid,
            firstDbid,
            secondOffers);

        var firstNewMoney = checked(
            firstBalance -
            firstMoney +
            secondMoney);
        var secondNewMoney = checked(
            secondBalance -
            secondMoney +
            firstMoney);

        using (var first = db.CreateCommand())
        {
            first.Transaction = tx;
            first.CommandText = """
                UPDATE characters
                   SET money=$money
                 WHERE id=$id
                   AND account_id=$account
                """;
            Arg(first, "$money", firstNewMoney);
            Arg(first, "$id", firstDbid);
            Arg(first, "$account", firstAccountId);
            if (first.ExecuteNonQuery() != 1)
                return new(false, "交易提交失敗", firstBalance, secondBalance);
        }

        using (var second = db.CreateCommand())
        {
            second.Transaction = tx;
            second.CommandText = """
                UPDATE characters
                   SET money=$money
                 WHERE id=$id
                   AND account_id=$account
                """;
            Arg(second, "$money", secondNewMoney);
            Arg(second, "$id", secondDbid);
            Arg(second, "$account", secondAccountId);
            if (second.ExecuteNonQuery() != 1)
                return new(false, "交易提交失敗", firstBalance, secondBalance);
        }

        tx.Commit();

        return new(
            true,
            "交易完成",
            firstNewMoney,
            secondNewMoney);
    }

    public DirectTransferResult TransferMoney(
        long accountId,
        long dbid,
        long targetDbid,
        long amount)
    {
        if (amount <= 0)
        {
            return new(
                false,
                "轉帳金額必須大於 0",
                0,
                0,
                0);
        }

        if (dbid == targetDbid)
        {
            return new(
                false,
                "不能轉帳給自己",
                0,
                0,
                0);
        }

        using var db = Open();
        using var tx = db.BeginTransaction();

        long senderMoney;
        using (var sender = db.CreateCommand())
        {
            sender.Transaction = tx;
            sender.CommandText = """
                SELECT money
                  FROM characters
                 WHERE id=$id
                   AND account_id=$account
                """;
            Arg(sender, "$id", dbid);
            Arg(sender, "$account", accountId);

            var raw = sender.ExecuteScalar();
            if (raw is null || raw == DBNull.Value)
            {
                return new(
                    false,
                    "角色不存在",
                    0,
                    0,
                    0);
            }

            senderMoney = Convert.ToInt64(raw);
        }

        long targetAccountId;
        long targetMoney;
        using (var target = db.CreateCommand())
        {
            target.Transaction = tx;
            target.CommandText = """
                SELECT account_id,money
                  FROM characters
                 WHERE id=$id
                """;
            Arg(target, "$id", targetDbid);

            using var reader = target.ExecuteReader();
            if (!reader.Read())
            {
                return new(
                    false,
                    "找不到目標角色",
                    senderMoney,
                    0,
                    0);
            }

            targetAccountId = reader.GetInt64(0);
            targetMoney = reader.GetInt64(1);
        }

        if (senderMoney < amount)
        {
            return new(
                false,
                "金錢不足",
                senderMoney,
                targetMoney,
                targetAccountId);
        }

        using (var debit = db.CreateCommand())
        {
            debit.Transaction = tx;
            debit.CommandText = """
                UPDATE characters
                   SET money=money-$amount
                 WHERE id=$id
                   AND account_id=$account
                   AND money >= $amount
                """;
            Arg(debit, "$amount", amount);
            Arg(debit, "$id", dbid);
            Arg(debit, "$account", accountId);

            if (debit.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    "轉帳失敗",
                    senderMoney,
                    targetMoney,
                    targetAccountId);
            }
        }

        using (var credit = db.CreateCommand())
        {
            credit.Transaction = tx;
            credit.CommandText = """
                UPDATE characters
                   SET money=money+$amount
                 WHERE id=$id
                """;
            Arg(credit, "$amount", amount);
            Arg(credit, "$id", targetDbid);

            if (credit.ExecuteNonQuery() != 1)
            {
                return new(
                    false,
                    "轉帳失敗",
                    senderMoney,
                    targetMoney,
                    targetAccountId);
            }
        }

        tx.Commit();

        return new(
            true,
            "轉帳成功",
            senderMoney - amount,
            targetMoney + amount,
            targetAccountId);
    }

    public (bool Success, string Message) BankTransfer(
        long accountId,
        long dbid,
        long uid,
        int tabType,
        int amount,
        bool toBank)
    {
        amount = Math.Max(1, amount);
        var targetBank = toBank ? 1 : 0;
        var sourceBank = toBank ? 0 : 1;

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return (false, "角色不存在");

        if (tabType == 0)
        {
            int itemIndex;
            int sourceCount;

            using (var get = db.CreateCommand())
            {
                get.Transaction = tx;
                get.CommandText = """
                    SELECT item_index,count
                      FROM inventory_items
                     WHERE uid=$uid
                       AND character_id=$id
                       AND in_bank=$source
                       AND in_shop=0
                    """;
                Arg(get, "$uid", uid);
                Arg(get, "$id", dbid);
                Arg(get, "$source", sourceBank);

                using var r = get.ExecuteReader();
                if (!r.Read())
                    return (false, "找不到可轉移的物品");

                itemIndex = r.GetInt32(0);
                sourceCount = r.GetInt32(1);
            }

            if (amount > sourceCount)
                return (false, "轉移數量超過持有數量");

            long? targetUid = null;
            using (var findTarget = db.CreateCommand())
            {
                findTarget.Transaction = tx;
                findTarget.CommandText = """
                    SELECT uid
                      FROM inventory_items
                     WHERE character_id=$id
                       AND item_index=$item
                       AND in_bank=$target
                       AND in_shop=0
                     ORDER BY uid
                     LIMIT 1
                    """;
                Arg(findTarget, "$id", dbid);
                Arg(findTarget, "$item", itemIndex);
                Arg(findTarget, "$target", targetBank);
                var scalar = findTarget.ExecuteScalar();
                if (scalar is not null && scalar != DBNull.Value)
                    targetUid = Convert.ToInt64(scalar);
            }

            if (targetUid is null)
            {
                using var countTarget = db.CreateCommand();
                countTarget.Transaction = tx;
                countTarget.CommandText = """
                    SELECT count(*)
                      FROM inventory_items
                     WHERE character_id=$id
                       AND in_bank=$target
                       AND in_shop=0
                    """;
                Arg(countTarget, "$id", dbid);
                Arg(countTarget, "$target", targetBank);
                if (Convert.ToInt32(countTarget.ExecuteScalar()) >=
                    ReadCharacterCapacities(
                        db,
                        dbid,
                        tx).Inventory)
                    return (false, "目標區域空間已滿");
            }

            if (amount == sourceCount && targetUid is null)
            {
                using var moveWhole = db.CreateCommand();
                moveWhole.Transaction = tx;
                moveWhole.CommandText = """
                    UPDATE inventory_items
                       SET in_bank=$target
                     WHERE uid=$uid AND character_id=$id
                    """;
                Arg(moveWhole, "$target", targetBank);
                Arg(moveWhole, "$uid", uid);
                Arg(moveWhole, "$id", dbid);
                if (moveWhole.ExecuteNonQuery() != 1)
                    return (false, "銀行轉移失敗");
            }
            else
            {
                using (var reduce = db.CreateCommand())
                {
                    reduce.Transaction = tx;
                    reduce.CommandText = """
                        UPDATE inventory_items
                           SET count=count-$amount
                         WHERE uid=$uid
                           AND character_id=$id
                           AND count >= $amount
                        """;
                    Arg(reduce, "$amount", amount);
                    Arg(reduce, "$uid", uid);
                    Arg(reduce, "$id", dbid);
                    if (reduce.ExecuteNonQuery() != 1)
                        return (false, "銀行轉移失敗");
                }

                if (targetUid.HasValue)
                {
                    using var merge = db.CreateCommand();
                    merge.Transaction = tx;
                    merge.CommandText = """
                        UPDATE inventory_items
                           SET count=count+$amount
                         WHERE uid=$uid AND character_id=$id
                        """;
                    Arg(merge, "$amount", amount);
                    Arg(merge, "$uid", targetUid.Value);
                    Arg(merge, "$id", dbid);
                    merge.ExecuteNonQuery();
                }
                else
                {
                    using var insert = db.CreateCommand();
                    insert.Transaction = tx;
                    insert.CommandText = """
                        INSERT INTO inventory_items(
                            character_id,item_index,count,in_bank,in_shop)
                        VALUES($id,$item,$amount,$target,0)
                        """;
                    Arg(insert, "$id", dbid);
                    Arg(insert, "$item", itemIndex);
                    Arg(insert, "$amount", amount);
                    Arg(insert, "$target", targetBank);
                    insert.ExecuteNonQuery();
                }

                if (amount == sourceCount)
                {
                    using var deleteEmpty = db.CreateCommand();
                    deleteEmpty.Transaction = tx;
                    deleteEmpty.CommandText = """
                        DELETE FROM inventory_items
                         WHERE uid=$uid
                           AND character_id=$id
                           AND count <= 0
                        """;
                    Arg(deleteEmpty, "$uid", uid);
                    Arg(deleteEmpty, "$id", dbid);
                    deleteEmpty.ExecuteNonQuery();
                }
            }

            tx.Commit();
            return (true, toBank ? "已存入銀行" : "已從銀行提領");
        }

        string table;
        string uidColumn;
        string extraPredicate;
        int maxSize;

        switch (tabType)
        {
            case 1:
                table = "equipment_items";
                uidColumn = "uid";
                extraPredicate = " AND part=255";
                maxSize = ReadCharacterCapacities(
                    db,
                    dbid,
                    tx).Equip;
                break;
            case 3:
                table = "horse_equipment";
                uidColumn = "uid";
                extraPredicate = " AND horse_uid=-1";
                maxSize = ReadCharacterCapacities(
                    db,
                    dbid,
                    tx).HorseEquip;
                break;
            default:
                return (false, "不支援的銀行分類");
        }

        using (var exists = db.CreateCommand())
        {
            exists.Transaction = tx;
            exists.CommandText = $"""
                SELECT count(*)
                  FROM {table}
                 WHERE {uidColumn}=$uid
                   AND character_id=$id
                   AND in_bank=$source
                   AND in_shop=0
                   {extraPredicate}
                """;
            Arg(exists, "$uid", uid);
            Arg(exists, "$id", dbid);
            Arg(exists, "$source", sourceBank);
            if (Convert.ToInt32(exists.ExecuteScalar()) != 1)
                return (false, "找不到可轉移的裝備");
        }

        using (var countTarget = db.CreateCommand())
        {
            countTarget.Transaction = tx;
            countTarget.CommandText = $"""
                SELECT count(*)
                  FROM {table}
                 WHERE character_id=$id
                   AND in_bank=$target
                   AND in_shop=0
                   {extraPredicate}
                """;
            Arg(countTarget, "$id", dbid);
            Arg(countTarget, "$target", targetBank);
            if (Convert.ToInt32(countTarget.ExecuteScalar()) >= maxSize)
                return (false, "目標區域空間已滿");
        }

        using (var move = db.CreateCommand())
        {
            move.Transaction = tx;
            move.CommandText = $"""
                UPDATE {table}
                   SET in_bank=$target
                 WHERE {uidColumn}=$uid
                   AND character_id=$id
                """;
            Arg(move, "$target", targetBank);
            Arg(move, "$uid", uid);
            Arg(move, "$id", dbid);
            if (move.ExecuteNonQuery() != 1)
                return (false, "銀行轉移失敗");
        }

        tx.Commit();
        return (true, toBank ? "已存入銀行" : "已從銀行提領");
    }

    public Dictionary<string, object>? UseRecoveryItem(
        long accountId,
        long dbid,
        long itemUid,
        int recoverHp,
        int recoverMp,
        int recoverSp,
        int? effectiveMaxHp = null,
        int? effectiveMaxMp = null,
        int? effectiveMaxSp = null)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return null;

        using (var item = db.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = """
                SELECT count FROM inventory_items
                 WHERE uid=$uid AND character_id=$id
                   AND in_bank=0 AND in_shop=0
                """;
            Arg(item, "$uid", itemUid);
            Arg(item, "$id", dbid);
            var raw = item.ExecuteScalar();
            if (raw is null || Convert.ToInt32(raw) <= 0)
                return null;
        }

        using (var update = db.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = """
                UPDATE characters
                   SET hp = MIN(COALESCE($maxHp, max_hp), hp + $hp),
                       mp = MIN(COALESCE($maxMp, max_mp), mp + $mp),
                       sp = MIN(COALESCE($maxSp, max_sp), sp + $sp)
                 WHERE id=$id AND account_id=$a
                   AND state <> 'death'
                """;
            Arg(update, "$hp", Math.Max(0, recoverHp));
            Arg(update, "$mp", Math.Max(0, recoverMp));
            Arg(update, "$sp", Math.Max(0, recoverSp));
            Arg(update, "$maxHp", effectiveMaxHp);
            Arg(update, "$maxMp", effectiveMaxMp);
            Arg(update, "$maxSp", effectiveMaxSp);
            Arg(update, "$id", dbid);
            Arg(update, "$a", accountId);

            if (update.ExecuteNonQuery() != 1)
                return null;
        }

        ConsumeInventoryItem(db, tx, dbid, itemUid);
        tx.Commit();
        return GetCharacter(accountId, dbid);
    }

    public Dictionary<string, object>? UseDeathExpReductionItem(
        long accountId,
        long dbid,
        long itemUid,
        int itemIndex,
        int reductionPercent)
    {
        reductionPercent = Math.Clamp(
            reductionPercent,
            0,
            100);

        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(
                db,
                accountId,
                dbid,
                tx))
            return null;

        using (var item = db.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = """
                SELECT count
                  FROM inventory_items
                 WHERE uid=$uid
                   AND character_id=$id
                   AND item_index=$item
                   AND in_bank=0
                   AND in_shop=0
                 LIMIT 1
                """;
            Arg(item, "$uid", itemUid);
            Arg(item, "$id", dbid);
            Arg(item, "$item", itemIndex);

            var raw = item.ExecuteScalar();
            if (raw is null ||
                raw == DBNull.Value ||
                Convert.ToInt32(raw) <= 0)
                return null;
        }

        using (var update = db.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = """
                UPDATE characters
                   SET exp_death =
                       MAX(
                           0,
                           exp_death * (100 - $reduction) / 100)
                 WHERE id=$id
                   AND account_id=$account
                """;
            Arg(update, "$reduction", reductionPercent);
            Arg(update, "$id", dbid);
            Arg(update, "$account", accountId);

            if (update.ExecuteNonQuery() != 1)
                return null;
        }

        ConsumeInventoryItem(
            db,
            tx,
            dbid,
            itemUid);

        tx.Commit();
        return GetCharacter(
            accountId,
            dbid);
    }

    public Dictionary<string, object>? ActivateHorseCard(
        long accountId,
        long dbid,
        long itemUid,
        int sourceItemIndex,
        int horseIndex,
        string name,
        string horseType,
        int initialEnergy)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();

        if (!OwnsCharacter(db, accountId, dbid, tx))
            return null;

        using (var item = db.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = """
                SELECT count,item_index FROM inventory_items
                 WHERE uid=$uid AND character_id=$id
                   AND in_bank=0 AND in_shop=0
                """;
            Arg(item, "$uid", itemUid);
            Arg(item, "$id", dbid);

            using var r = item.ExecuteReader();
            if (!r.Read() ||
                r.GetInt32(0) <= 0 ||
                r.GetInt32(1) != sourceItemIndex)
                return null;
        }

        long horseUid;
        using (var add = db.CreateCommand())
        {
            add.Transaction = tx;
            add.CommandText = """
                INSERT INTO horses(
                    character_id,horse_index,name,horse_type,energy_left)
                VALUES($id,$idx,$name,$type,$energy);
                SELECT last_insert_rowid();
                """;
            Arg(add, "$id", dbid);
            Arg(add, "$idx", horseIndex);
            Arg(add, "$name", name);
            Arg(add, "$type",
                string.IsNullOrWhiteSpace(horseType) ? "MS" : horseType);
            Arg(add, "$energy", Math.Max(0, initialEnergy));
            horseUid = Convert.ToInt64(add.ExecuteScalar());
        }

        ConsumeInventoryItem(db, tx, dbid, itemUid);
        tx.Commit();

        return GetCharacterAssets(accountId, dbid).Horses
            .FirstOrDefault(x =>
                Convert.ToInt64(x["HorseUID"]) == horseUid);
    }

    private static void ConsumeInventoryItem(
        SqliteConnection db,
        SqliteTransaction tx,
        long dbid,
        long itemUid)
    {
        using (var consume = db.CreateCommand())
        {
            consume.Transaction = tx;
            consume.CommandText = """
                UPDATE inventory_items
                   SET count = count - 1
                 WHERE uid=$uid AND character_id=$id
                """;
            Arg(consume, "$uid", itemUid);
            Arg(consume, "$id", dbid);
            consume.ExecuteNonQuery();
        }

        using var cleanup = db.CreateCommand();
        cleanup.Transaction = tx;
        cleanup.CommandText = """
            DELETE FROM inventory_items
             WHERE uid=$uid AND character_id=$id AND count <= 0
            """;
        Arg(cleanup, "$uid", itemUid);
        Arg(cleanup, "$id", dbid);
        cleanup.ExecuteNonQuery();
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

    public Dictionary<string, object>? ApplyHeal(
        long accountId,
        long dbid,
        int amount)
    {
        amount = Math.Max(0, amount);
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET hp = MIN(max_hp, hp + $amount)
             WHERE id=$id AND account_id=$a
            """;
        Arg(cmd, "$amount", amount);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        return GetCharacter(accountId, dbid);
    }

    public Dictionary<string, object>? RecoverResources(
        long accountId,
        long dbid,
        int hpAmount,
        int mpAmount)
        => RecoverResources(
            accountId,
            dbid,
            hpAmount,
            mpAmount,
            int.MaxValue,
            int.MaxValue);

    public Dictionary<string, object>? RecoverResources(
        long accountId,
        long dbid,
        int hpAmount,
        int mpAmount,
        int effectiveMaxHp,
        int effectiveMaxMp)
    {
        hpAmount = Math.Max(0, hpAmount);
        mpAmount = Math.Max(0, mpAmount);

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET hp = MIN(CASE WHEN $maxHp = 2147483647 THEN max_hp ELSE $maxHp END, hp + $hp),
                   mp = MIN(CASE WHEN $maxMp = 2147483647 THEN max_mp ELSE $maxMp END, mp + $mp)
             WHERE id=$id
               AND account_id=$a
               AND hp > 0
            """;
        Arg(cmd, "$hp", hpAmount);
        Arg(cmd, "$mp", mpAmount);
        Arg(cmd, "$maxHp", effectiveMaxHp);
        Arg(cmd, "$maxMp", effectiveMaxMp);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        return GetCharacter(accountId, dbid);
    }

    public Dictionary<string, object>? RecoverResources(
        long accountId,
        long dbid,
        int hpAmount,
        int mpAmount,
        int spAmount)
        => RecoverResources(
            accountId,
            dbid,
            hpAmount,
            mpAmount,
            spAmount,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue);

    public Dictionary<string, object>? RecoverResources(
        long accountId,
        long dbid,
        int hpAmount,
        int mpAmount,
        int spAmount,
        int effectiveMaxHp,
        int effectiveMaxMp,
        int effectiveMaxSp)
    {
        hpAmount = Math.Max(0, hpAmount);
        mpAmount = Math.Max(0, mpAmount);
        spAmount = Math.Max(0, spAmount);

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET hp = MIN(CASE WHEN $maxHp = 2147483647 THEN max_hp ELSE $maxHp END, hp + $hp),
                   mp = MIN(CASE WHEN $maxMp = 2147483647 THEN max_mp ELSE $maxMp END, mp + $mp),
                   sp = MIN(CASE WHEN $maxSp = 2147483647 THEN max_sp ELSE $maxSp END, sp + $sp)
             WHERE id=$id
               AND account_id=$a
               AND hp > 0
            """;
        Arg(cmd, "$hp", hpAmount);
        Arg(cmd, "$mp", mpAmount);
        Arg(cmd, "$sp", spAmount);
        Arg(cmd, "$maxHp", effectiveMaxHp);
        Arg(cmd, "$maxMp", effectiveMaxMp);
        Arg(cmd, "$maxSp", effectiveMaxSp);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

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
               SET exp_acc = MIN($maxInt, MAX(0, exp_acc) + $exp),
                   money = money + $money
             WHERE id=$id AND account_id=$a
            """;
        Arg(cmd, "$exp", Math.Max(0, exp));
        Arg(cmd, "$maxInt", int.MaxValue);
        Arg(cmd, "$money", Math.Max(0, money));
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        tx.Commit();
        return GetCharacter(accountId, dbid);
    }

    public Dictionary<string, object>? AdjustMoney(
        long accountId,
        long dbid,
        int delta)
    {
        if (delta == 0)
            return GetCharacter(accountId, dbid);

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET money = money + $delta
             WHERE id=$id
               AND account_id=$account
               AND money + $delta >= 0
            """;
        Arg(cmd, "$delta", delta);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$account", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        return GetCharacter(accountId, dbid);
    }

    public Dictionary<string, object>? SetExperienceProgress(
        long accountId,
        long dbid,
        int level,
        int maxExp,
        int abilityPointsToAdd)
    {
        level = Math.Clamp(level, 1, 255);
        maxExp = Math.Max(1, maxExp);
        abilityPointsToAdd = Math.Max(0, abilityPointsToAdd);

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET level=$level,
                   max_exp=$maxExp,
                   exp_use=exp_use+$points
             WHERE id=$id AND account_id=$a
            """;
        Arg(cmd, "$level", level);
        Arg(cmd, "$maxExp", maxExp);
        Arg(cmd, "$points", abilityPointsToAdd);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        return GetCharacter(accountId, dbid);
    }

    public Dictionary<string, object>? SetLevel(
        long accountId,
        long dbid,
        int level)
    {
        level = Math.Clamp(level, 1, 255);

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET exp_use = exp_use +
                   CASE
                       WHEN $level > level THEN ($level - level) * 2
                       ELSE 0
                   END,
                   level=$level,
                   exp_acc=0,
                   exp_death=0
             WHERE id=$id AND account_id=$a
            """;
        Arg(cmd, "$level", level);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        return GetCharacter(accountId, dbid);
    }

    public Dictionary<string, object>? SetAttackSpeed(
        long accountId,
        long dbid,
        int attackSpeed)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET attack_speed=$speed
             WHERE id=$id AND account_id=$a
            """;
        Arg(cmd, "$speed", Math.Clamp(attackSpeed, 1, 1000));
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        return GetCharacter(accountId, dbid);
    }

    public Dictionary<string, object>? SetCareer(
        long accountId,
        long dbid,
        int career,
        int job)
    {
        if (career is < 0 or > 10 ||
            job is < 0 or > 2)
            return null;

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET career=$career,
                   job=$job
             WHERE id=$id
               AND account_id=$account
            """;
        Arg(cmd, "$career", career);
        Arg(cmd, "$job", job);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$account", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

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

    public Dictionary<string, object>? ReviveBySkill(
        long accountId,
        long dbid,
        int hpPercent,
        int mpPercent,
        int deathExpReductionPercent)
    {
        hpPercent = Math.Clamp(hpPercent, 1, 100);
        mpPercent = Math.Clamp(mpPercent, 0, 100);
        deathExpReductionPercent = Math.Clamp(
            deathExpReductionPercent,
            0,
            100);

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET hp=MAX(1, max_hp * $hpPct / 100),
                   mp=max_mp * $mpPct / 100,
                   state='idle',
                   exp_death=MAX(
                       0,
                       exp_death -
                       (exp_death * $deathExpReductionPct / 100))
             WHERE id=$id
               AND account_id=$a
               AND hp <= 0
            """;
        Arg(cmd, "$hpPct", hpPercent);
        Arg(cmd, "$mpPct", mpPercent);
        Arg(
            cmd,
            "$deathExpReductionPct",
            deathExpReductionPercent);
        Arg(cmd, "$id", dbid);
        Arg(cmd, "$a", accountId);

        if (cmd.ExecuteNonQuery() != 1)
            return null;

        return GetCharacter(accountId, dbid);
    }
}
