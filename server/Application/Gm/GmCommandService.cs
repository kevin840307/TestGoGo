using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Combat;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Gm;

public sealed class GmCommandService
{
    private readonly IGameStore _store;
    private readonly SkillCatalog _skills;
    private readonly HorseCardCatalog _horseCards;
    private readonly CurrentClientDataCatalog _current;
    private readonly IConfiguration _config;

    public GmCommandService(
        IGameStore store,
        SkillCatalog skills,
        HorseCardCatalog horseCards,
        CurrentClientDataCatalog current,
        IConfiguration config)
    {
        _store = store;
        _skills = skills;
        _horseCards = horseCards;
        _current = current;
        _config = config;
    }

    public IReadOnlyList<object> Execute(
        string message,
        ClientSession session)
    {
        if (!Enabled())
            return [Chat("GM 指令目前已由 Server 關閉")];

        if (!session.AccountId.HasValue ||
            !session.CharacterId.HasValue)
            return [Chat("GM 指令需要先登入角色")];

        var text = message.Trim();
        var parts = text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
            return [Chat(HelpText())];

        var accountId = session.AccountId.Value;
        var dbid = session.CharacterId.Value;
        var command = parts[1].ToLowerInvariant();

        try
        {
            return command switch
            {
                "help" => [Chat(HelpText())],
                "pos" => Position(accountId, dbid),
                "map" or "warp" or "teleport"
                    => Teleport(parts, accountId, dbid, session),
                "heal" => Heal(accountId, dbid),
                "level" => Level(parts, accountId, dbid),
                "money" => Money(parts, accountId, dbid),
                "item" => Item(parts, accountId, dbid),
                "equip" => Equipment(parts, accountId, dbid),
                "mecha" or "horse"
                    => Mecha(parts, accountId, dbid),
                "pet" => Pet(parts, accountId, dbid),
                "skill" => Skill(parts, accountId, dbid),
                "skills" => SkillList(accountId, dbid),
                "clearskills" => ClearSkills(accountId, dbid),
                "killall" => KillAll(session),
                _ => [Chat($"未知 GM 指令：{command}。使用 /gm help")]
            };
        }
        catch (Exception ex)
        {
            return [Chat($"GM 指令失敗：{ex.Message}")];
        }
    }

    private bool Enabled()
        => _config.GetValue<bool?>("Game:GmCommandsEnabled") ?? true;

    private IReadOnlyList<object> Position(long accountId, long dbid)
    {
        var ch = _store.GetCharacter(accountId, dbid);
        if (ch is null) return [Chat("找不到角色")];

        return
        [
            Chat(
                $"DBID={dbid} Map={ch["MapID"]} " +
                $"X={ch["LocX"]} Y={ch["LocY"]}")
        ];
    }

    private IReadOnlyList<object> Teleport(
        string[] parts,
        long accountId,
        long dbid,
        ClientSession session)
    {
        if (parts.Length < 3 ||
            !int.TryParse(parts[2], out var mapId))
            return [Chat("用法：/gm map <MapID> [X] [Y]")];

        var ch = _store.GetCharacter(accountId, dbid);
        if (ch is null) return [Chat("找不到角色")];

        var x = parts.Length > 3 && int.TryParse(parts[3], out var px)
            ? px
            : Convert.ToInt32(Convert.ToDouble(ch["LocX"]));
        var y = parts.Length > 4 && int.TryParse(parts[4], out var py)
            ? py
            : Convert.ToInt32(Convert.ToDouble(ch["LocY"]));

        if (!_store.UpdateLocation(accountId, dbid, mapId, x, y))
            return [Chat("傳送失敗")];

        session.CurrentMapId = mapId;
        session.LastX = x;
        session.LastY = y;
        session.PendingMapSpawn = true;
        session.Monsters.Clear();

        return
        [
            new
            {
                Type = "ChangeMap",
                MapID = mapId,
                X = x,
                Y = y
            },
            Chat($"已傳送到 Map{mapId} ({x},{y})")
        ];
    }

    private IReadOnlyList<object> Heal(long accountId, long dbid)
    {
        var ch = _store.Revive(accountId, dbid);
        if (ch is null) return [Chat("補滿狀態失敗")];

        return
        [
            new
            {
                Type = "PlayerStatusSync",
                HP = ch["HP"],
                MP = ch["MP"],
                SP = ch["SP"],
                MaxHP = ch["MaxHP"],
                MaxMP = ch["MaxMP"],
                MaxSP = ch["MaxSP"]
            },
            Chat("HP/MP/SP 已補滿")
        ];
    }

    private IReadOnlyList<object> Level(
        string[] parts,
        long accountId,
        long dbid)
    {
        if (parts.Length < 3 ||
            !int.TryParse(parts[2], out var level))
            return [Chat("用法：/gm level <1-255>")];

        var ch = _store.SetLevel(
            accountId,
            dbid,
            level);

        if (ch is null)
            return [Chat("修改等級失敗")];

        return
        [
            new
            {
                Type = "PlayerStatusSync",
                Level = ch["Level"],
                HP = ch["HP"],
                MP = ch["MP"],
                SP = ch["SP"],
                MaxHP = ch["MaxHP"],
                MaxMP = ch["MaxMP"],
                MaxSP = ch["MaxSP"]
            },
            Chat($"角色等級已設為 {ch["Level"]}")
        ];
    }

    private IReadOnlyList<object> Money(
        string[] parts,
        long accountId,
        long dbid)
    {
        if (parts.Length < 3 ||
            !int.TryParse(parts[2], out var amount))
            return [Chat("用法：/gm money <增加金額>")];

        var ch = _store.AddRewards(accountId, dbid, 0, amount);
        if (ch is null) return [Chat("修改金錢失敗")];

        return
        [
            new { Type = "SyncMoney", Money = ch["Money"] },
            Chat($"目前金錢：{ch["Money"]}")
        ];
    }

    private IReadOnlyList<object> Item(
        string[] parts,
        long accountId,
        long dbid)
    {
        if (parts.Length < 3 ||
            !int.TryParse(parts[2], out var itemIndex))
            return [Chat("用法：/gm item <ItemIndex> [Count]")];

        var currentItem = _current.FindItem(itemIndex);
        if (currentItem is null)
            return [Chat($"新版 GameData 找不到 ItemIndex={itemIndex}")];

        if (currentItem.Category is not (
                "general" or "recovery" or "booster" or "horsebattery"))
            return [Chat(
                $"ItemIndex={itemIndex} 類型為 {currentItem.Category}，請使用對應 GM 指令")];

        var count = parts.Length > 3 &&
                    int.TryParse(parts[3], out var parsedCount)
            ? Math.Clamp(parsedCount, 1, 9999)
            : 1;

        var uid = _store.AddInventoryItem(
            accountId,
            dbid,
            itemIndex,
            count);

        if (uid <= 0) return [Chat("給物品失敗")];

        var assets = _store.GetCharacterAssets(accountId, dbid);
        return
        [
            new
            {
                Type = "InventorySync",
                Items = assets.Inventory,
                MaxSize = assets.InventoryMaxSize
            },
            Chat($"已給物品 ItemIndex={itemIndex} x{count}")
        ];
    }

    private IReadOnlyList<object> Equipment(
        string[] parts,
        long accountId,
        long dbid)
    {
        if (parts.Length < 3 ||
            !int.TryParse(parts[2], out var itemIndex))
            return [Chat("用法：/gm equip <ItemIndex>")];

        var currentItem = _current.FindItem(itemIndex);
        if (currentItem is null ||
            currentItem.Category is not ("equip" or "weapon"))
            return [Chat($"新版 GameData 找不到可穿戴裝備 ItemIndex={itemIndex}")];

        var uid = _store.AddEquipment(
            accountId,
            dbid,
            itemIndex);

        if (uid <= 0) return [Chat("給裝備失敗")];

        var assets = _store.GetCharacterAssets(accountId, dbid);
        return
        [
            new
            {
                Type = "EquipSync",
                Equips = assets.Equips,
                MaxSize = assets.EquipMaxSize
            },
            Chat($"已給裝備 ItemIndex={itemIndex}")
        ];
    }

    private IReadOnlyList<object> Mecha(
        string[] parts,
        long accountId,
        long dbid)
    {
        if (parts.Length < 3 ||
            !int.TryParse(parts[2], out var itemIndex))
            return [Chat("用法：/gm mecha <CardID>")];

        var currentItem = _current.FindItem(itemIndex);
        if (currentItem is null ||
            !currentItem.Category.Equals(
                "horse",
                StringComparison.OrdinalIgnoreCase))
            return [Chat($"新版 GameData 找不到機甲 ItemIndex={itemIndex}")];

        var card = _horseCards.Find(itemIndex);
        var uid = _store.AddHorse(
            accountId,
            dbid,
            itemIndex,
            card?.Name ?? $"Mecha {itemIndex}",
            card?.HorseType ?? "MS",
            card?.MaxBattery ?? 3600);

        if (uid <= 0) return [Chat("建立機甲失敗")];

        var assets = _store.GetCharacterAssets(accountId, dbid);
        return
        [
            new
            {
                Type = "HorseSync",
                Horses = assets.Horses,
                HorseEquips = assets.HorseEquips,
                MaxHorseEquipSize = assets.HorseEquipMaxSize
            },
            Chat($"已建立機甲 {card?.Name ?? itemIndex.ToString()}")
        ];
    }

    private IReadOnlyList<object> Pet(
        string[] parts,
        long accountId,
        long dbid)
    {
        if (parts.Length < 3 ||
            !int.TryParse(parts[2], out var petId))
            return [Chat("用法：/gm pet <PetID> [Name]")];

        var name = parts.Length > 3
            ? string.Join(' ', parts.Skip(3))
            : $"Pet {petId}";

        var uid = _store.AddPet(
            accountId,
            dbid,
            petId,
            name);

        if (uid <= 0) return [Chat("建立寵物失敗")];

        var assets = _store.GetCharacterAssets(accountId, dbid);
        return
        [
            new
            {
                Type = "PetSync",
                Pets = assets.Pets,
                MaxSize = assets.PetMaxSize
            },
            Chat($"已建立寵物 {name}")
        ];
    }

    private IReadOnlyList<object> Skill(
        string[] parts,
        long accountId,
        long dbid)
    {
        if (parts.Length < 3)
            return [Chat("用法：/gm skill <SkillID|all> [Level]")];

        var level = parts.Length > 3 &&
                    int.TryParse(parts[3], out var parsedLevel)
            ? Math.Clamp(parsedLevel, 1, 99)
            : 1;

        if (parts[2].Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var count = 0;
            foreach (var skill in _skills.All)
            {
                if (_store.SetSkill(
                        accountId,
                        dbid,
                        skill.SkillId,
                        level))
                    count++;
            }

            return
            [
                BuildSkillSync(accountId, dbid),
                Chat($"已學會 {count} 個技能，等級 {level}")
            ];
        }

        if (!int.TryParse(parts[2], out var skillId))
            return [Chat("SkillID 必須是數字或 all")];

        var definition = _skills.Find(skillId);
        if (definition is null)
            return [Chat($"找不到 SkillID={skillId}")];

        if (!_store.SetSkill(accountId, dbid, skillId, level))
            return [Chat("學習技能失敗")];

        return
        [
            BuildSkillSync(accountId, dbid),
            Chat($"已學會 {definition.Name} ({skillId}) Lv.{level}")
        ];
    }

    private IReadOnlyList<object> SkillList(
        long accountId,
        long dbid)
    {
        var skills = _store.GetSkills(accountId, dbid);
        return
        [
            BuildSkillSync(accountId, dbid),
            Chat($"目前已學會 {skills.Count} 個技能")
        ];
    }

    private IReadOnlyList<object> ClearSkills(
        long accountId,
        long dbid)
    {
        if (!_store.ClearSkills(accountId, dbid))
            return [Chat("清除技能失敗")];

        return
        [
            BuildSkillSync(accountId, dbid),
            Chat("技能已清除")
        ];
    }

    private static IReadOnlyList<object> KillAll(
        ClientSession session)
    {
        var packets = new List<object>();
        foreach (var monster in session.Monsters.Values)
        {
            if (!monster.IsAlive) continue;

            monster.CurrentHp = 0;
            monster.HasTarget = false;
            monster.RespawnAt =
                DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);

            packets.Add(new
            {
                Type = "EntityDeath",
                EntityID = monster.EntityId,
                EntityType = 1,
                KillerID = session.CharacterId ?? 0
            });
        }

        packets.Add(Chat($"已清除 {packets.Count} 隻目前怪物"));
        return packets;
    }

    private object BuildSkillSync(long accountId, long dbid)
        => new
        {
            Type = "SkillSync",
            Skills = _store.GetSkills(accountId, dbid)
        };

    private static object Chat(string message)
        => new
        {
            Type = "SystemMessageChat",
            Message = message
        };

    private static string HelpText()
        => "GM: help | pos | map <id> [x] [y] | heal | level <n> | " +
           "money <n> | item <id> [count] | equip <id> | " +
           "mecha <cardId> | pet <petId> [name] | " +
           "skill <id|all> [lv] | skills | clearskills | killall";
}
