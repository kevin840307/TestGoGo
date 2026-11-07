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
        SqliteSchemaMigrator.Apply(db);
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
                   reborn_map_id,reborn_x,reborn_y,summoned_horse_uid
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
        var assets = new CharacterAssets();
        using var db = Open();
        if (!OwnsCharacter(db, accountId, dbid))
            return assets;

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

    public (bool Success, string Message, long RewardUid) OpenDoll(
        long accountId,
        long dbid,
        long dollUid,
        int dollItemIndex,
        int rewardItemIndex,
        AssetPurchaseKind rewardKind,
        int rewardCount = 1,
        int rewardPart = 255)
    {
        rewardCount = Math.Max(1, rewardCount);

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

        long rewardUid = 0;

        switch (rewardKind)
        {
            case AssetPurchaseKind.General:
            case AssetPurchaseKind.HorseBattery:
            {
                using var existing = db.CreateCommand();
                existing.Transaction = tx;
                existing.CommandText = """
                    SELECT uid
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

                var existingUid = existing.ExecuteScalar();
                if (existingUid is not null && existingUid != DBNull.Value)
                {
                    rewardUid = Convert.ToInt64(existingUid);

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
                using var add = db.CreateCommand();
                add.Transaction = tx;
                add.CommandText = """
                    INSERT INTO equipment_items(
                        character_id,item_index,damage,color,part,in_bank,in_shop)
                    VALUES($id,$idx,0,0,$part,0,0);
                    SELECT last_insert_rowid();
                    """;
                Arg(add, "$id", dbid);
                Arg(add, "$idx", rewardItemIndex);
                Arg(add, "$part", rewardPart);
                rewardUid = Convert.ToInt64(add.ExecuteScalar());
                break;
            }

            case AssetPurchaseKind.HorseEquipment:
            {
                using var add = db.CreateCommand();
                add.Transaction = tx;
                add.CommandText = """
                    INSERT INTO horse_equipment(
                        character_id,equip_index,horse_uid,damage,color,part,in_bank,in_shop)
                    VALUES($id,$idx,-1,0,0,$part,0,0);
                    SELECT last_insert_rowid();
                    """;
                Arg(add, "$id", dbid);
                Arg(add, "$idx", rewardItemIndex);
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
        int energyAmount)
    {
        if (energyAmount <= 0)
            return null;

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
                   SET energy_left = energy_left + $energy
                 WHERE uid=$horse AND character_id=$id
                """;
            Arg(horse, "$energy", energyAmount);
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
        using var db = Open();
        using var tx = db.BeginTransaction();
        if (!OwnsCharacter(db, accountId, dbid, tx)) return false;

        if (state == 1)
        {
            using var recall = db.CreateCommand();
            recall.Transaction = tx;
            recall.CommandText = """
                UPDATE pets
                   SET state=0
                 WHERE character_id=$id
                """;
            Arg(recall, "$id", dbid);
            recall.ExecuteNonQuery();
        }

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE pets
               SET state=$state
             WHERE uid=$uid AND character_id=$id
            """;
        Arg(cmd, "$state", state);
        Arg(cmd, "$uid", petUid);
        Arg(cmd, "$id", dbid);

        if (cmd.ExecuteNonQuery() != 1)
            return false;

        tx.Commit();
        return true;
    }

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

    public AssetPurchaseResult BuyAsset(
        long accountId,
        long dbid,
        AssetPurchaseKind kind,
        int itemIndex,
        int count,
        int unitPrice,
        int itemPart = 255)
    {
        count = Math.Max(1, count);
        unitPrice = Math.Max(0, unitPrice);
        var total = checked(unitPrice * count);

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
                    VALUES($id,$idx,0,0,255);
                    SELECT last_insert_rowid();
                    """;
                Arg(add, "$id", dbid);
                Arg(add, "$idx", itemIndex);
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
                    VALUES($id,$idx,-1,0,0,$part);
                    SELECT last_insert_rowid();
                    """;
                Arg(add, "$id", dbid);
                Arg(add, "$idx", itemIndex);
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
                 WHERE id=$id AND account_id=$a
                """;
            Arg(spend, "$total", total);
            Arg(spend, "$id", dbid);
            Arg(spend, "$a", accountId);
            if (spend.ExecuteNonQuery() != 1)
                return new(false, "購買失敗", 0, currentMoney);
        }

        tx.Commit();
        return new(true, "購買成功", uid, currentMoney - total);
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
        count = Math.Max(1, count);
        unitPrice = Math.Max(0, unitPrice);

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

        GroundItemRuntime item;
        using (var get = db.CreateCommand())
        {
            get.Transaction = tx;
            get.CommandText = """
                SELECT id,map_id,owner_dbid,tab_type,item_index,count,
                       damage,color,part,x,y,
                       created_utc_ms,protect_until_utc_ms,expires_utc_ms
                  FROM ground_items
                 WHERE id=$id
                """;
            Arg(get, "$id", groundItemId);
            using var reader = get.ExecuteReader();
            if (!reader.Read())
                return (false, "物品已不存在", null);
            item = ReadGroundItem(reader);
        }

        var now = DateTimeOffset.UtcNow;
        if (item.IsExpired(now))
        {
            using var expire = db.CreateCommand();
            expire.Transaction = tx;
            expire.CommandText =
                "DELETE FROM ground_items WHERE id=$id";
            Arg(expire, "$id", groundItemId);
            expire.ExecuteNonQuery();
            tx.Commit();
            return (false, "物品已消失", item);
        }

        if (item.IsProtectedFor(dbid, now))
            return (false, "物品目前受掉落者保護", item);

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

        using (var del = db.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText =
                "DELETE FROM ground_items WHERE id=$id";
            Arg(del, "$id", groundItemId);
            if (del.ExecuteNonQuery() != 1)
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
                if (Convert.ToInt32(countTarget.ExecuteScalar()) >= 200)
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
                maxSize = 200;
                break;
            case 3:
                table = "horse_equipment";
                uidColumn = "uid";
                extraPredicate = " AND horse_uid=-1";
                maxSize = 100;
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
        int recoverSp)
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
                   SET hp = MIN(max_hp, hp + $hp),
                       mp = MIN(max_mp, mp + $mp),
                       sp = MIN(max_sp, sp + $sp)
                 WHERE id=$id AND account_id=$a
                   AND state <> 'death'
                """;
            Arg(update, "$hp", Math.Max(0, recoverHp));
            Arg(update, "$mp", Math.Max(0, recoverMp));
            Arg(update, "$sp", Math.Max(0, recoverSp));
            Arg(update, "$id", dbid);
            Arg(update, "$a", accountId);

            if (update.ExecuteNonQuery() != 1)
                return null;
        }

        ConsumeInventoryItem(db, tx, dbid, itemUid);
        tx.Commit();
        return GetCharacter(accountId, dbid);
    }

    public Dictionary<string, object>? ActivateHorseCard(
        long accountId,
        long dbid,
        long itemUid,
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
                r.GetInt32(1) != horseIndex)
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
    {
        hpAmount = Math.Max(0, hpAmount);
        mpAmount = Math.Max(0, mpAmount);

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE characters
               SET hp = MIN(max_hp, hp + $hp),
                   mp = MIN(max_mp, mp + $mp)
             WHERE id=$id
               AND account_id=$a
               AND hp > 0
            """;
        Arg(cmd, "$hp", hpAmount);
        Arg(cmd, "$mp", mpAmount);
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
                   level=$level
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
