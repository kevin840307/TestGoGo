using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Assets;
using SGO.Server.Application.Combat;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.World;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Gm;

public sealed class GmCommandService
{
    private readonly IGameStore _store;
    private readonly SkillCatalog _skills;
    private readonly HorseCardCatalog _horseCards;
    private readonly CurrentClientDataCatalog _current;
    private readonly LegacyClientDataCatalog _legacyClient;
    private readonly LegacyPlayerGrowthCatalog _growth;
    private readonly CombatService _combat;
    private readonly ServerBroadcastHub _broadcasts;
    private readonly IConfiguration _config;
    private readonly PositionPersistenceService? _positionWrites;

    public GmCommandService(
        IGameStore store,
        SkillCatalog skills,
        HorseCardCatalog horseCards,
        CurrentClientDataCatalog current,
        LegacyClientDataCatalog legacyClient,
        LegacyPlayerGrowthCatalog growth,
        CombatService combat,
        ServerBroadcastHub broadcasts,
        IConfiguration config,
        PositionPersistenceService? positionWrites = null)
    {
        _store = store;
        _skills = skills;
        _horseCards = horseCards;
        _current = current;
        _legacyClient = legacyClient;
        _growth = growth;
        _combat = combat;
        _broadcasts = broadcasts;
        _config = config;
        _positionWrites = positionWrites;
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
                "heal" or "restore" or "fullheal"
                    => Heal(accountId, dbid),
                "level" => Level(parts, accountId, dbid),
                "maxstats" or "maxstat"
                    => MaxStats(accountId, dbid),
                // Explicit opt-in to protect ordinary game servers from
                // synthetic stats. Production never enables this by default.
                "testbot" => _config.GetValue<bool>("Game:TestBotCommandsEnabled")
                    ? TestBotProfile(accountId, dbid)
                    : [Chat("testbot 未啟用。請在執行中的 Server 設定 " +
                        "Game__TestBotCommandsEnabled=true，重新啟動 Server 後再試")],
                "attackspeed" or "aspd"
                    => AttackSpeed(parts, accountId, dbid),
                "money" => Money(parts, accountId, dbid),
                "mallpoints" or "mallpoint" or "points"
                    => MallPoints(parts, accountId),
                "donation" or "donate"
                    => Donation(parts, accountId),
                "item" => Item(parts, accountId, dbid),
                "equip" => Equipment(parts, accountId, dbid),
                "mecha" or "horse"
                    => Mecha(parts, accountId, dbid),
                "mechacard"
                    => MechaCard(parts, accountId, dbid),
                "mechaequip"
                    => MechaEquip(parts, accountId, dbid),
                "mechalevel"
                    => MechaLevel(parts, accountId, dbid),
                "pet" => Pet(parts, accountId, dbid),
                "skill" => Skill(parts, accountId, dbid),
                "skills" => SkillList(accountId, dbid),
                "clearskills" => ClearSkills(accountId, dbid),
                "spawn" => Spawn(parts, session),
                "killall" => KillAll(session),
                "cleardrops" or "clearground" or "clearloot"
                    => ClearGroundItems(session),
                _ => [Chat($"未知 GM 指令：{command}。使用 /gm help")]
            };
        }
        catch (Exception ex)
        {
            return [Chat($"GM 指令失敗：{ex.Message}")];
        }
    }

    private bool Enabled()
        // The local SGO appsettings explicitly opts in. Keep the code-level
        // fallback denied so deployments can still disable GM commands by
        // omitting/overriding Game:GmCommandsEnabled.
        => _config.GetValue<bool?>("Game:GmCommandsEnabled") ?? false;

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

        // A same-map GM warp changes the position without changing map_id.
        // Unlike cross-map transitions, the SQL map guard cannot reject
        // an old queued snapshot from the same map. Fence it first.
        _positionWrites?.FenceAndDiscard(accountId, dbid);
        if (!_store.UpdateLocation(accountId, dbid, mapId, x, y))
            return [Chat("傳送失敗")];

        session.CurrentMapId = mapId;
        session.LastX = x;
        session.LastY = y;
        session.PositionDirty = false;
        session.LastPositionPersistAt =
            DateTimeOffset.UtcNow;
        session.PendingMapSpawn = true;
        session.WorldReady = false;
        session.LoadedMonsterMapId = 0;
        session.WorldSnapshotRetriesRemaining = 0;
        session.PetTargets.Clear();
        session.PetOutOfRangeSince.Clear();
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

        var currentLevel = Convert.ToInt32(ch["Level"]);
        ch = _store.SetExperienceProgress(
                 accountId,
                 dbid,
                 currentLevel,
                 _growth.ExperienceThresholdForLevel(currentLevel),
                 0)
             ?? ch;

        var growth = _growth.Calculate(ch);
        ch = _store.ApplyGrowthStats(
            accountId,
            dbid,
            growth.MaxHP,
            growth.MaxMP,
            growth.MaxSP,
            growth.Attack,
            growth.Defense,
            growth.MagicAttack,
            growth.MagicDefense,
            growth.Hit,
            growth.Dodge,
            growth.MoveSpeed) ?? ch;

        var assets = _store.GetCharacterAssets(accountId, dbid);
        var responses = new List<object>
        {
            CharacterStatCalculator.ToPlayerStatusPacket(
                ch,
                assets,
                _current),
            Chat(
                $"角色等級已設為 {ch["Level"]}，可用能力點 {ch["ExpUse"]}；" +
                $"HP {ch["MaxHP"]} MP {ch["MaxMP"]}")
        };

        return responses;
    }

    private IReadOnlyList<object> TestBotProfile(
        long accountId,
        long dbid)
    {
        // The live server may use its existing database. Never allow this
        // destructive stat reset on any ordinary player's account/character.
        if (!_store.IsLoadTestBot(accountId, dbid))
            return [Chat("testbot 僅允許多人測試工具建立的角色使用")];

        var ch = _store.SetLevel(accountId, dbid, 1);
        if (ch is null)
            return [Chat("TestBot profile: level update failed")];

        ch = _store.SetPrimaryStats(accountId, dbid, 5, 5, 5, 5, 0);
        if (ch is null)
            return [Chat("TestBot profile: attributes update failed")];

        ch = _store.ApplyGrowthStats(
            accountId, dbid,
            20000, 5000, 5000,
            1, 1000,
            1, 1000,
            1, 25,
            Math.Max(1, Convert.ToInt32(ch["MoveSpeed"])));

        if (ch is null)
            return [Chat("TestBot profile: combat stats update failed")];

        ch = _store.Revive(accountId, dbid);
        if (ch is null)
            return [Chat("TestBot profile: refill failed")];

        var assets = _store.GetCharacterAssets(accountId, dbid);
        return
        [
            CharacterStatCalculator.ToPlayerStatusPacket(ch, assets, _current),
            Chat("TestBot profile applied: Lv1 ATK=1 MATK=1, HP=20000, DEF=1000")
        ];
    }

    private IReadOnlyList<object> MaxStats(
        long accountId,
        long dbid)
    {
        var ch = _store.SetPrimaryStats(
            accountId,
            dbid,
            100,
            100,
            100,
            100,
            0);

        if (ch is null)
            return [Chat("能力值設滿失敗")];

        var growth = _growth.Calculate(ch);
        ch = _store.ApplyGrowthStats(
            accountId,
            dbid,
            growth.MaxHP,
            growth.MaxMP,
            growth.MaxSP,
            growth.Attack,
            growth.Defense,
            growth.MagicAttack,
            growth.MagicDefense,
            growth.Hit,
            growth.Dodge,
            growth.MoveSpeed) ?? ch;

        var assets = _store.GetCharacterAssets(accountId, dbid);
        var effective = CharacterStatCalculator.Calculate(
            ch,
            assets,
            _current);

        return
        [
            new
            {
                Type = "AllocateStatResponse",
                Success = true,
                ExpUse = ch["ExpUse"],
                STR = ch["Str"],
                DEX = ch["Dex"],
                CON = ch["Con"],
                WIS = ch["Wis"],
                MaxHP = ch["MaxHP"],
                MaxMP = ch["MaxMP"],
                MaxSP = ch["MaxSP"],
                Attack = effective.Attack,
                Defense = effective.Defense,
                MagicAttack = effective.MagicAttack,
                MagicDefense = effective.MagicDefense,
                Hit = effective.Hit,
                Dodge = effective.Dodge,
                Lucky = effective.Lucky,
                AttackSpeed = effective.AttackSpeed,
                AttackRange = effective.AttackRange,
                MoveSpeed = effective.MoveSpeed,
                CritRate = effective.CritRate,
                CritDamage = effective.CritDamage
            },
            CharacterStatCalculator.ToPlayerStatusPacket(
                ch,
                assets,
                _current),
            Chat(
                $"能力值已滿：STR/DEX/CON/WIS=100；" +
                $"HP={ch["MaxHP"]} MP={ch["MaxMP"]} " +
                $"ATK={effective.Attack} MATK={effective.MagicAttack} " +
                $"DEF={effective.Defense} DODGE={effective.Dodge} " +
                $"MOVE={effective.MoveSpeed}")
        ];
    }

    private IReadOnlyList<object> AttackSpeed(
        string[] parts,
        long accountId,
        long dbid)
    {
        if (parts.Length < 3 ||
            !int.TryParse(parts[2], out var speed))
            return [Chat("用法：/gm attackspeed <1-1000>")];

        var ch = _store.SetAttackSpeed(
            accountId,
            dbid,
            speed);

        if (ch is null)
            return [Chat("修改攻擊速度失敗")];

        var assets = _store.GetCharacterAssets(accountId, dbid);
        var effective = CharacterStatCalculator.Calculate(
            ch,
            assets,
            _current);

        return
        [
            CharacterStatCalculator.ToPlayerStatusPacket(
                ch,
                assets,
                _current),
            Chat($"攻擊速度已設為 {effective.AttackSpeed}")
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

    private IReadOnlyList<object> MallPoints(
        string[] parts,
        long accountId)
    {
        if (parts.Length < 3 ||
            !long.TryParse(parts[2], out var delta))
        {
            return [Chat(
                "用法：/gm mallpoints <變更量>，例如 1000 或 -100")];
        }

        if (!_store.AdjustMallPoints(
                accountId,
                delta,
                out var balance))
        {
            return [Chat(
                "修改商城點數失敗（帳號不存在或餘額不可低於 0）")];
        }

        return
        [
            new
            {
                Type = "MallPointsSync",
                Points = balance
            },
            Chat($"目前商城點數：{balance}")
        ];
    }

    private IReadOnlyList<object> Donation(
        string[] parts,
        long accountId)
    {
        if (parts.Length < 3 ||
            !long.TryParse(parts[2], out var delta))
        {
            return [Chat(
                "用法：/gm donation <變更量>，例如 5000 或 -500")];
        }

        if (!_store.AdjustLifetimeDonation(
                accountId,
                delta,
                out var total))
        {
            return [Chat(
                "修改累積贊助失敗（帳號不存在或累積值不可低於 0）")];
        }

        return
        [
            Chat($"目前累積贊助：{total} 點")
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
            return [Chat("用法：/gm equip <ItemIndex> [Durability]")];

        var currentItem = _current.FindItem(itemIndex);
        if (currentItem is null ||
            currentItem.Category is not ("equip" or "weapon"))
            return [Chat($"新版 GameData 找不到可穿戴裝備 ItemIndex={itemIndex}")];

        var durability = parts.Length > 3 &&
                         int.TryParse(parts[3], out var requestedDurability)
            ? Math.Clamp(
                requestedDurability,
                0,
                Math.Max(0, currentItem.Durability))
            : currentItem.Durability;

        var uid = _store.AddEquipment(
            accountId,
            dbid,
            itemIndex,
            255,
            durability);

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
            Chat($"已給裝備 ItemIndex={itemIndex} Durability={durability}")
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

        var requestedItemIndex = itemIndex;
        var currentItem = _current.FindItem(itemIndex);

        if (currentItem is null ||
            !currentItem.Category.Equals(
                "horse",
                StringComparison.OrdinalIgnoreCase))
        {
            var legacyName = _legacyClient.FindItemName(
                requestedItemIndex,
                "horse");

            currentItem = string.IsNullOrWhiteSpace(legacyName)
                ? null
                : _current.FindItemByName(
                    legacyName,
                    "horse");

            if (currentItem is null)
                return [Chat(
                    $"找不到舊機甲 {requestedItemIndex} 對應的新版 GameData")];

            itemIndex = currentItem.ItemId;
        }

        var card = _horseCards.Find(itemIndex);
        var uid = _store.AddHorse(
            accountId,
            dbid,
            itemIndex,
            card?.Name ?? $"Mecha {itemIndex}",
            card?.HorseType ?? "MS",
            card?.MaxBattery ?? 3600);

        if (uid <= 0) return [Chat("建立機甲失敗")];

        var preset = _current.FindHorsePreset(itemIndex);
        if (preset is not null)
        {
            foreach (var equipIndex in new[]
                     {
                         preset.BodyId,
                         preset.WeaponId,
                         preset.EngineId
                     }.Where(x => x > 0).Distinct())
            {
                var equip = _current.FindItem(equipIndex);
                _store.AddHorseEquipment(
                    accountId,
                    dbid,
                    equipIndex,
                    uid,
                    equip?.EquipSlot ?? 255,
                    equip?.Durability ?? 0);
            }
        }

        var assets = _store.GetCharacterAssets(accountId, dbid);
        var character = _store.GetCharacter(accountId, dbid);
        var status = character is null
            ? null
            : CharacterStatCalculator.ToPlayerStatusPacket(
                character,
                assets,
                _current);

        var responses = new List<object>
        {
            new
            {
                Type = "HorseSync",
                Horses = assets.Horses,
                HorseEquips = assets.HorseEquips,
                MaxHorseEquipSize = assets.HorseEquipMaxSize
            }
        };

        if (status is not null)
            responses.Add(status);

        responses.Add(Chat(
            preset is null
                ? $"已建立機甲 {card?.Name ?? itemIndex.ToString()}"
                : $"已建立機甲 {card?.Name ?? itemIndex.ToString()} 並裝上預設機甲裝備"));

        return responses;
    }

    private IReadOnlyList<object> MechaCard(
        string[] parts,
        long accountId,
        long dbid)
    {
        if (parts.Length < 3 ||
            !int.TryParse(parts[2], out var itemIndex))
            return [Chat("用法：/gm mechacard <CardID>")];

        var item = _current.FindItem(itemIndex);
        if (item is null ||
            !item.Category.Equals(
                "horse",
                StringComparison.OrdinalIgnoreCase))
            return [Chat($"新版 GameData 找不到機甲卡 ItemIndex={itemIndex}")];

        var uid = _store.AddInventoryItem(
            accountId,
            dbid,
            itemIndex,
            1);

        if (uid <= 0)
            return [Chat("給機甲卡失敗")];

        var assets = _store.GetCharacterAssets(accountId, dbid);
        return
        [
            new
            {
                Type = "InventorySync",
                Items = assets.Inventory,
                MaxSize = assets.InventoryMaxSize
            },
            Chat($"已給機甲卡 {item.Name} ({itemIndex})")
        ];
    }

    private IReadOnlyList<object> MechaEquip(
        string[] parts,
        long accountId,
        long dbid)
    {
        if (parts.Length < 3 ||
            !int.TryParse(parts[2], out var itemIndex))
            return [Chat("用法：/gm mechaequip <EquipID> [HorseUID]")];

        var item = _current.FindItem(itemIndex);
        if (item is null ||
            !item.Category.Equals(
                "horseequip",
                StringComparison.OrdinalIgnoreCase))
            return [Chat($"新版 GameData 找不到機甲裝備 ItemIndex={itemIndex}")];

        var horseUid = -1L;
        if (parts.Length > 3 &&
            long.TryParse(parts[3], out var parsedHorseUid))
            horseUid = parsedHorseUid;

        var uid = _store.AddHorseEquipment(
            accountId,
            dbid,
            itemIndex,
            horseUid,
            item.EquipSlot,
            item.Durability);

        if (uid <= 0)
            return [Chat("給機甲裝備失敗")];

        var assets = _store.GetCharacterAssets(accountId, dbid);
        var responses = new List<object>
        {
            new
            {
                Type = "HorseSync",
                Horses = assets.Horses,
                HorseEquips = assets.HorseEquips,
                MaxHorseEquipSize = assets.HorseEquipMaxSize
            }
        };

        if (horseUid > 0)
        {
            var ch = _store.GetCharacter(accountId, dbid);
            if (ch is not null)
                responses.Add(
                    CharacterStatCalculator.ToPlayerStatusPacket(
                        ch,
                        assets,
                        _current));
        }

        responses.Add(Chat(
            horseUid > 0
                ? $"已建立並裝上機甲裝備 {item.Name} ({itemIndex})"
                : $"已建立機甲裝備 {item.Name} ({itemIndex})"));

        return responses;
    }

    private IReadOnlyList<object> MechaLevel(
        string[] parts,
        long accountId,
        long dbid)
    {
        if (parts.Length < 4 ||
            !long.TryParse(parts[2], out var horseUid) ||
            !int.TryParse(parts[3], out var level))
            return [Chat("用法：/gm mechalevel <HorseUID> <1-200>")];

        var horse = _store.SetHorseLevel(
            accountId,
            dbid,
            horseUid,
            level);

        if (horse is null)
            return [Chat($"找不到機甲 HorseUID={horseUid}")];

        var assets = _store.GetCharacterAssets(accountId, dbid);

        return
        [
            new
            {
                Type = "SyncMechaEnergy",
                HorseUID = horseUid,
                EnergyLeft = horse["EnergyLeft"],
                Exp = horse["Exp"],
                MaxExp = horse["MaxExp"],
                Level = horse["Level"]
            },
            new
            {
                Type = "HorseSync",
                Horses = assets.Horses,
                HorseEquips = assets.HorseEquips,
                MaxHorseEquipSize = assets.HorseEquipMaxSize
            },
            Chat($"機甲 {horseUid} 已設為 Lv.{horse["Level"]}")
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

        var petDefinition = _current.FindPet(petId);
        if (petDefinition is null)
            return [Chat($"新版 GameData 找不到 PetID={petId}")];

        var name = parts.Length > 3
            ? string.Join(' ', parts.Skip(3))
            : petDefinition.Name;

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

        var requestedSkillId = skillId;
        var definition = _skills.Find(skillId);

        if (definition is null)
        {
            var legacyName =
                _legacyClient.FindSkillName(requestedSkillId);

            var currentSkill =
                string.IsNullOrWhiteSpace(legacyName)
                    ? null
                    : _current.FindSkillByName(legacyName);

            if (currentSkill is null)
                return [Chat(
                    $"找不到舊 SkillID={requestedSkillId} 對應的新版技能")];

            skillId = currentSkill.SkillId;
            definition = _skills.Find(skillId);
        }

        if (definition is null ||
            !_store.SetSkill(accountId, dbid, skillId, level))
            return [Chat("學習技能失敗")];

        return
        [
            BuildSkillSync(accountId, dbid),
            Chat(
                requestedSkillId == skillId
                    ? $"已學會 {definition.Name} ({skillId}) Lv.{level}"
                    : $"已對齊舊技能 {requestedSkillId} -> 新技能 {skillId}：{definition.Name} Lv.{level}")
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

    private IReadOnlyList<object> Spawn(
        string[] parts,
        ClientSession session)
    {
        if (parts.Length < 3 ||
            !int.TryParse(parts[2], out var enemyId))
            return [Chat("用法：/gm spawn <EnemyID> [X] [Y]")];

        var x = parts.Length > 3 &&
                double.TryParse(parts[3], out var parsedX)
            ? parsedX
            : session.LastX + 120;

        var y = parts.Length > 4 &&
                double.TryParse(parts[4], out var parsedY)
            ? parsedY
            : session.LastY;

        var packets = _combat.SpawnLegacyEnemy(
            session,
            enemyId,
            x,
            y);

        if (packets.Count == 0)
            return
            [
                Chat(
                    $"目前 Map{session.CurrentMapId} 找不到 EnemyID={enemyId}")
            ];

        return packets
            .Concat(new[]
            {
                Chat(
                    $"已生成 EnemyID={enemyId} 於 ({x:0},{y:0})")
            })
            .ToArray();
    }

    private IReadOnlyList<object> ClearGroundItems(
        ClientSession session)
    {
        if (session.CurrentMapId <= 0)
            return [Chat("目前不在地圖中")];

        var mapId = session.CurrentMapId;
        var entityIds = _store.ClearGroundItems(mapId);

        foreach (var entityId in entityIds)
        {
            session.GroundItems.Remove(entityId);

            _broadcasts.PublishToMap(
                mapId,
                new
                {
                    Type = "DespawnEntity",
                    EntityID = entityId
                });
        }

        return
        [
            Chat(
                $"已清除 Map{mapId} 上 {entityIds.Count} 個掉落物")
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
        => "GM: help | pos | map <id> [x] [y] | heal/restore/fullheal | level <n> | " +
           "maxstats | attackspeed <1-1000> | money <n> | mallpoints <delta> | donation <delta> | item <id> [count] | equip <id> | " +
           "mecha <cardId> | mechacard <cardId> | mechaequip <id> [horseUid] | mechalevel <horseUid> <level> | pet <petId> [name] | " +
           "skill <id|all> [lv] | skills | clearskills | " +
           "spawn <enemyId> [x] [y] | killall | cleardrops";
}
