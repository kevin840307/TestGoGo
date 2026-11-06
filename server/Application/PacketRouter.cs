using System.Text.Json;
using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Assets;
using SGO.Server.Application.Combat;
using SGO.Server.Application.Shops;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Quest;
using SGO.Server.Infrastructure.World;
using SGO.Server.Protocol;

namespace SGO.Server.Application;

public sealed class PacketRouter
{
    private const int NpcInteractRange = 500;

    private readonly IGameStore _store;
    private readonly WorldDataService _world;
    private readonly QuestRuntime _quests;
    private readonly CombatService _combat;
    private readonly ShopService _shopService;
    private readonly HorseBatteryCatalog _horseBatteries;
    private readonly ILogger<PacketRouter> _log;

    public PacketRouter(
        IGameStore store,
        WorldDataService world,
        QuestRuntime quests,
        CombatService combat,
        ShopService shopService,
        HorseBatteryCatalog horseBatteries,
        ILogger<PacketRouter> log)
    {
        _store = store;
        _world = world;
        _quests = quests;
        _combat = combat;
        _shopService = shopService;
        _horseBatteries = horseBatteries;
        _log = log;
    }

    public IReadOnlyList<object> Handle(JsonElement req, ClientSession state)
    {
        var type = req.Str("Type");

        if (type == "RegisterAccountRequest")
        {
            var (ok, msg) = _store.Register(
                req.Str("LoginName"), req.Str("Password"), req.Str("EMAIL"));
            return [new { Type = "RegisterAccountResponse", Success = ok, Message = msg }];
        }

        if (type == "LoginRequest")
        {
            var id = _store.Login(req.Str("User"), req.Str("Pass"));
            state.AccountId = id;
            state.CharacterId = null;
            state.CurrentMapId = 0;
            state.PendingMapSpawn = false;

            return [new
            {
                Type = "LoginResponse",
                Success = id.HasValue,
                Message = id.HasValue ? "登入成功" : "帳號或密碼錯誤",
                Characters = id.HasValue
                    ? _store.Characters(id.Value)
                    : new List<Dictionary<string, object>>()
            }];
        }

        if (type == "AnnouncementListRequest")
            return [new { Type = "AnnouncementListResponse", Announcements = Array.Empty<object>() }];

        if (!state.AccountId.HasValue)
        {
            _log.LogWarning("Unauthenticated request: {Type}", type);
            return [];
        }

        var accountId = state.AccountId.Value;

        switch (type)
        {
            case "CreateCharacterRequest":
            {
                var (ok, msg) = _store.Create(accountId, req);
                return [new { Type = "CreateCharacterResponse", Success = ok, Message = msg }];
            }

            case "DeleteCharacterRequest":
            {
                var dbid = req.Int("DBID");
                var ok = _store.Delete(accountId, dbid, req.Str("Password"));
                if (state.CharacterId == dbid)
                {
                    state.CharacterId = null;
                    state.CurrentMapId = 0;
                    state.PendingMapSpawn = false;
                }

                return [new
                {
                    Type = "DeleteCharacterResponse",
                    Success = ok,
                    Message = ok ? "角色已刪除" : "無法刪除角色，請確認密碼",
                    CharSlot = req.Int("CharSlot", -1),
                    DeletedDBID = ok ? dbid : -1
                }];
            }

            case "ModifyPasswordRequest":
            {
                var newPwd = req.Str("NewPwd");
                var ok = newPwd.Length is >= 1 and <= 100 &&
                         _store.ChangeCharacterPassword(
                             accountId, req.Int("DBID"), newPwd);

                return [new
                {
                    Type = "ModifyPasswordResponse",
                    Success = ok,
                    Message = ok ? "修改成功" : "修改失敗",
                    CharSlot = req.Int("CharSlot", -1),
                    NewPwd = newPwd
                }];
            }

            case "SelectCharacterRequest":
            {
                var dbid = req.Int("DBID");
                var character = _store.GetCharacter(accountId, dbid);
                if (character is null)
                    return [new { Type = "SystemMessageFlash", Message = "角色不存在" }];

                state.CharacterId = dbid;
                state.CurrentMapId = Convert.ToInt32(character["MapID"]);
                state.PendingMapSpawn = true;
                character["Type"] = "EnterWorldSuccess";

                var assets = _store.GetCharacterAssets(accountId, dbid);
                ApplyAssetsToCharacterBootstrap(character, assets);
                ApplyEffectiveStats(character, assets);

                return
                [
                    character,
                    new
                    {
                        Type = "InventorySync",
                        Items = assets.Inventory,
                        MaxSize = assets.InventoryMaxSize
                    },
                    new
                    {
                        Type = "EquipSync",
                        Equips = assets.Equips,
                        MaxSize = assets.EquipMaxSize
                    },
                    new
                    {
                        Type = "PetSync",
                        Pets = assets.Pets,
                        MaxSize = assets.PetMaxSize
                    },
                    new
                    {
                        Type = "HorseSync",
                        Horses = assets.Horses,
                        HorseEquips = assets.HorseEquips,
                        MaxHorseEquipSize = assets.HorseEquipMaxSize
                    },
                    new { Type = "SkillSync", Skills = Array.Empty<object>() },
                    new { Type = "SyncBuffs", Buffs = Array.Empty<object>() },
                    new { Type = "QuestSync", Quests = Array.Empty<object>() },
                    new { Type = "FestivalThemeSync", Theme = "normal", Festival = "" }
                ];
            }

            case "SyncPosition":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var x = req.Int("X");
                var y = req.Int("Y");
                _store.UpdatePosition(accountId, state.CharacterId.Value, x, y);

                var now = DateTimeOffset.UtcNow;
                var packets = new List<object>();
                packets.AddRange(_combat.TickRespawns(
                    state,
                    now));

                if (state.PendingMapSpawn)
                {
                    state.PendingMapSpawn = false;
                    packets.AddRange(
                        _combat.EnterMap(
                            state,
                            state.CurrentMapId));
                }
                else
                {
                    packets.AddRange(
                        _combat.TickAi(
                            state,
                            accountId,
                            x,
                            y,
                            now));
                }

                return packets;
            }

            case "EquipRequest":
            {
                if (!state.CharacterId.HasValue) return [];
                var success = _store.EquipItem(
                    accountId,
                    state.CharacterId.Value,
                    req.Int("ItemUID"),
                    req.Int("TargetSlot", 255));

                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                return
                [
                    new
                    {
                        Type = "EquipResponse",
                        Success = success,
                        Message = success ? "裝備成功" : "無法裝備"
                    },
                    new
                    {
                        Type = "EquipSync",
                        Equips = assets.Equips,
                        MaxSize = assets.EquipMaxSize
                    },
                    BuildEffectiveStatusPacket(accountId, state.CharacterId.Value, assets),
                    BuildAppearancePacket(accountId, state.CharacterId.Value, assets)
                ];
            }

            case "UnequipRequest":
            {
                if (!state.CharacterId.HasValue) return [];
                var success = _store.UnequipItem(
                    accountId,
                    state.CharacterId.Value,
                    req.Int("TargetSlot", 255));

                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                return
                [
                    new
                    {
                        Type = "EquipResponse",
                        Success = success,
                        Message = success ? "卸下成功" : "無法卸下"
                    },
                    new
                    {
                        Type = "EquipSync",
                        Equips = assets.Equips,
                        MaxSize = assets.EquipMaxSize
                    },
                    BuildEffectiveStatusPacket(accountId, state.CharacterId.Value, assets),
                    BuildAppearancePacket(accountId, state.CharacterId.Value, assets)
                ];
            }

            case "DriveMechaRequest":
            {
                if (!state.CharacterId.HasValue) return [];
                var horseUid = req.Int("HorseUID", -1);
                var success = _store.SetSummonedHorse(
                    accountId,
                    state.CharacterId.Value,
                    horseUid);

                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                var response = new List<object>
                {
                    new
                    {
                        Type = "DriveMechaResponse",
                        Success = success,
                        HorseUID = success ? horseUid : -1,
                        Message = success
                            ? (horseUid == -1 ? "已解除駕駛" : "開始駕駛")
                            : "無法駕駛此機甲"
                    }
                };

                if (success)
                {
                    response.Add(BuildEffectiveStatusPacket(
                        accountId,
                        state.CharacterId.Value,
                        assets));
                    response.Add(BuildAppearancePacket(
                        accountId,
                        state.CharacterId.Value,
                        assets));
                }

                return response;
            }

            case "HorseEquipRequest":
            {
                if (!state.CharacterId.HasValue) return [];
                var success = _store.EquipHorseItem(
                    accountId,
                    state.CharacterId.Value,
                    req.Int("EquipUID"),
                    req.Int("HorseUID"));

                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                return
                [
                    new
                    {
                        Type = "HorseSync",
                        Horses = assets.Horses,
                        HorseEquips = assets.HorseEquips,
                        MaxHorseEquipSize = assets.HorseEquipMaxSize
                    },
                    new
                    {
                        Type = "SystemMessageFlash",
                        Message = success ? "機甲裝備成功" : "機甲裝備失敗"
                    }
                ];
            }

            case "HorseUnequipRequest":
            {
                if (!state.CharacterId.HasValue) return [];
                var success = _store.UnequipHorseItem(
                    accountId,
                    state.CharacterId.Value,
                    req.Int("EquipUID"),
                    req.Int("HorseUID"));

                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                return
                [
                    new
                    {
                        Type = "HorseSync",
                        Horses = assets.Horses,
                        HorseEquips = assets.HorseEquips,
                        MaxHorseEquipSize = assets.HorseEquipMaxSize
                    },
                    new
                    {
                        Type = "SystemMessageFlash",
                        Message = success ? "已卸下機甲裝備" : "無法卸下機甲裝備"
                    }
                ];
            }

            case "PetAction":
            {
                if (!state.CharacterId.HasValue) return [];

                var action = req.Str("Action");
                var petUid = req.Int("PetUID");
                bool success;

                switch (action)
                {
                    case "Summon":
                        success = _store.SetPetState(
                            accountId,
                            state.CharacterId.Value,
                            petUid,
                            1);
                        break;

                    case "Recall":
                        success = _store.SetPetState(
                            accountId,
                            state.CharacterId.Value,
                            petUid,
                            0);
                        break;

                    case "Rename":
                        success = _store.RenamePet(
                            accountId,
                            state.CharacterId.Value,
                            petUid,
                            req.Str("NewName"));
                        break;

                    case "Equip":
                        success = _store.EquipPetItem(
                            accountId,
                            state.CharacterId.Value,
                            petUid,
                            req.Int("ItemUID"),
                            req.Int("TargetSlot", -1));
                        break;

                    case "Unequip":
                        success = _store.UnequipPetItem(
                            accountId,
                            state.CharacterId.Value,
                            petUid,
                            req.Int("TargetSlot", -1));
                        break;

                    default:
                        _log.LogDebug(
                            "Unimplemented PetAction: {Action}",
                            action);
                        return [];
                }

                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                var responses = new List<object>
                {
                    new
                    {
                        Type = "PetSync",
                        Pets = assets.Pets,
                        MaxSize = assets.PetMaxSize
                    },
                    new
                    {
                        Type = "SystemMessageFlash",
                        Message = success
                            ? action switch
                            {
                                "Summon" => "寵物已召喚",
                                "Recall" => "寵物已收回",
                                "Rename" => "寵物名稱已更新",
                                "Equip" => "寵物裝備成功",
                                "Unequip" => "寵物裝備已卸下",
                                _ => "寵物操作成功"
                            }
                            : "寵物操作失敗"
                    }
                };

                if (success && action == "Summon")
                {
                    var pet = assets.Pets.FirstOrDefault(x =>
                        Convert.ToInt64(x["UID"]) == petUid);
                    var character = _store.GetCharacter(
                        accountId,
                        state.CharacterId.Value);
                    if (pet is not null && character is not null)
                    {
                        responses.Add(new
                        {
                            Type = "SpawnEntity",
                            EntityID = 4_000_000L + petUid,
                            EntityType = 4,
                            Name = pet["Name"],
                            LocX = Convert.ToDouble(character["LocX"]) + 40,
                            LocY = Convert.ToDouble(character["LocY"]) + 20,
                            ExtraData = new
                            {
                                OwnerID = state.CharacterId.Value,
                                Sprite = "EN010A",
                                Level = pet["Level"],
                                MaxHP = pet["MaxHP"],
                                CurrentHP = pet["HP"],
                                ColorIndex = 0
                            }
                        });
                    }
                }
                else if (success && action == "Recall")
                {
                    responses.Add(new
                    {
                        Type = "DespawnEntity",
                        EntityID = 4_000_000L + petUid
                    });
                }

                return responses;
            }

            case "ShopAction":
            {
                if (!state.CharacterId.HasValue) return [];

                var action = req.Str("Action");
                return action switch
                {
                    "BuyNpcItem" => _shopService.Buy(
                        accountId,
                        state.CharacterId.Value,
                        req.Int("ShopID"),
                        req.Int("ItemIndex"),
                        req.Int("Count", 1)),

                    "SellNpcItem" => _shopService.Sell(
                        accountId,
                        state.CharacterId.Value,
                        req.Int("UID"),
                        req.Int("TabType"),
                        req.Int("Count", 1)),

                    _ => []
                };
            }

            case "HorseChargeRequest":
            {
                if (!state.CharacterId.HasValue) return [];

                var horseUid = req.Int("HorseUID");
                var itemUid = req.Int("ItemUID");
                var assetsBefore = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                var battery = assetsBefore.Inventory.FirstOrDefault(x =>
                    Convert.ToInt64(x["ItemUID"]) == itemUid);

                if (battery is null)
                    return [new { Type = "SystemMessageFlash", Message = "找不到機甲電池" }];

                var itemIndex = Convert.ToInt32(battery["ItemIndex"]);
                var energy = _horseBatteries.GetEnergy(itemIndex);
                if (energy <= 0)
                    return [new { Type = "SystemMessageFlash", Message = "此物品不是可用的機甲電池" }];

                var horse = _store.ChargeHorse(
                    accountId,
                    state.CharacterId.Value,
                    horseUid,
                    itemUid,
                    energy);

                if (horse is null)
                    return [new { Type = "SystemMessageFlash", Message = "機甲充能失敗" }];

                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

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
                        Type = "InventorySync",
                        Items = assets.Inventory,
                        MaxSize = assets.InventoryMaxSize
                    },
                    new
                    {
                        Type = "HorseSync",
                        Horses = assets.Horses,
                        HorseEquips = assets.HorseEquips,
                        MaxHorseEquipSize = assets.HorseEquipMaxSize
                    },
                    new { Type = "SystemMessageFlash", Message = "機甲充能完成" }
                ];
            }

            case "AttackEntity":
                return _combat.Attack(
                    state,
                    accountId,
                    req.Int("TargetEntityID"),
                    req.Int("TargetType", 1));

            case "SyncState":
                if (state.CharacterId.HasValue)
                {
                    _store.UpdateState(
                        accountId,
                        state.CharacterId.Value,
                        req.Str("State", "idle"));
                }
                return [];

            case "InteractRequest":
                return HandleInteract(req, state, accountId);

            case "NpcOptionRequest":
                return HandleNpcOption(req, state, accountId);

            case "ChatMessage":
            {
                if (!state.CharacterId.HasValue) return [];
                var character = _store.GetCharacter(accountId, state.CharacterId.Value);
                if (character is null) return [];

                var message = req.Str("Message");
                if (message.Length > 200)
                    message = message[..200];

                return [new
                {
                    Type = "ChatBroadcast",
                    EntityID = state.CharacterId.Value,
                    SenderName = character["NAME"],
                    Race = character["Race"],
                    Channel = req.Int("Channel"),
                    Message = message,
                    BubbleType = req.Int("BubbleType")
                }];
            }

            case "ReviveRequest":
            {
                if (!state.CharacterId.HasValue) return [];
                var character = _store.Revive(accountId, state.CharacterId.Value);
                if (character is null) return [];

                var rebornMapId = Convert.ToInt32(character["RebornMapID"]);
                var rebornX = Convert.ToInt32(Convert.ToDouble(character["RebornX"]));
                var rebornY = Convert.ToInt32(Convert.ToDouble(character["RebornY"]));

                _store.UpdateLocation(
                    accountId,
                    state.CharacterId.Value,
                    rebornMapId,
                    rebornX,
                    rebornY);

                state.CurrentMapId = rebornMapId;
                state.PendingMapSpawn = true;
                state.Monsters.Clear();

                return
                [
                    new
                    {
                        Type = "PlayerStatusSync",
                        HP = character["HP"],
                        MP = character["MP"],
                        SP = character["SP"],
                        MaxHP = character["MaxHP"],
                        MaxMP = character["MaxMP"],
                        MaxSP = character["MaxSP"]
                    },
                    new
                    {
                        Type = "ChangeMap",
                        MapID = rebornMapId,
                        X = rebornX,
                        Y = rebornY
                    }
                ];
            }

            case "LogoutRequest":
                state.AccountId = null;
                state.CharacterId = null;
                state.CurrentMapId = 0;
                state.PendingMapSpawn = false;
                state.Monsters.Clear();
                state.CloseRequested = true;

                return [new
                {
                    Type = "LogoutResponse",
                    IsBackToLogin =
                        req.TryGetProperty("IsBackToLogin", out var back) &&
                        back.ValueKind == JsonValueKind.True
                }];

            case "Pong":
                return [];

            default:
                _log.LogDebug("Unimplemented client packet: {Type}", type);
                return [];
        }
    }

    private IReadOnlyList<object> HandleInteract(
        JsonElement req,
        ClientSession state,
        long accountId)
    {
        if (!state.CharacterId.HasValue || state.CurrentMapId <= 0)
            return [];

        var entityId = req.Int("EntityID");
        var npc = _world.FindNpc(state.CurrentMapId, entityId);
        if (npc is null)
            return [];

        var character = _store.GetCharacter(accountId, state.CharacterId.Value);
        if (character is null)
            return [];

        var px = Convert.ToDouble(character["LocX"]);
        var py = Convert.ToDouble(character["LocY"]);
        var dx = px - npc.X;
        var dy = py - npc.Y;

        if ((dx * dx) + (dy * dy) > NpcInteractRange * NpcInteractRange)
        {
            return [new
            {
                Type = "SystemMessageChat",
                Message = "距離 NPC 太遠"
            }];
        }

        if (string.IsNullOrWhiteSpace(npc.Script))
            return [];

        return ExecuteQuest(
            state,
            accountId,
            npc,
            "start",
            null,
            character);
    }

    private IReadOnlyList<object> HandleNpcOption(
        JsonElement req,
        ClientSession state,
        long accountId)
    {
        if (!state.CharacterId.HasValue || state.CurrentMapId <= 0)
            return [];

        var entityId = req.Int("EntityID");
        var npc = _world.FindNpc(state.CurrentMapId, entityId);
        if (npc is null)
            return [];

        var requestedScript = req.Str("ScriptName");
        if (!string.Equals(
                Path.GetFileName(requestedScript),
                Path.GetFileName(npc.Script),
                StringComparison.OrdinalIgnoreCase))
        {
            _log.LogWarning(
                "Rejected NPC script mismatch. Entity={EntityID}, Requested={Requested}, Actual={Actual}",
                entityId,
                requestedScript,
                npc.Script);
            return [];
        }

        var target = req.Str("TargetNode", "End");
        int? choice = null;

        var separator = target.LastIndexOf('#');
        if (separator > 0 &&
            int.TryParse(target[(separator + 1)..], out var parsedChoice))
        {
            choice = parsedChoice;
            target = target[..separator];
        }

        var character = _store.GetCharacter(accountId, state.CharacterId.Value);
        if (character is null)
            return [];

        return ExecuteQuest(
            state,
            accountId,
            npc,
            target,
            choice,
            character);
    }

    private IReadOnlyList<object> ExecuteQuest(
        ClientSession state,
        long accountId,
        WorldNpc npc,
        string function,
        int? choice,
        Dictionary<string, object> character)
    {
        try
        {
            var result = _quests.Execute(
                npc.Script,
                function,
                npc.EntityId,
                npc.Name,
                choice,
                name => GetCharacterData(character, name));

            if (result.RebornMapId.HasValue &&
                result.RebornX.HasValue &&
                result.RebornY.HasValue &&
                state.CharacterId.HasValue)
            {
                _store.SetRebornLocation(
                    accountId,
                    state.CharacterId.Value,
                    result.RebornMapId.Value,
                    result.RebornX.Value,
                    result.RebornY.Value);
            }

            if (result.NewMapId.HasValue &&
                result.NewX.HasValue &&
                result.NewY.HasValue &&
                state.CharacterId.HasValue)
            {
                _store.UpdateLocation(
                    accountId,
                    state.CharacterId.Value,
                    result.NewMapId.Value,
                    result.NewX.Value,
                    result.NewY.Value);

                state.CurrentMapId = result.NewMapId.Value;
                state.PendingMapSpawn = true;
            }

            return result.Packets;
        }
        catch (Exception ex)
        {
            _log.LogError(
                ex,
                "Legacy quest failed. Script={Script}, Function={Function}",
                npc.Script,
                function);

            return [new
            {
                Type = "SystemMessageChat",
                Message = $"NPC 腳本執行失敗: {npc.Script}"
            }];
        }
    }

    private static void ApplyAssetsToCharacterBootstrap(
        Dictionary<string, object> character,
        SGO.Server.Domain.Assets.CharacterAssets assets)
    {
        character["Inventory"] = assets.Inventory;
        character["InventoryMaxSize"] = assets.InventoryMaxSize;
        character["EquipList"] = assets.Equips;
        character["EquipMaxSize"] = assets.EquipMaxSize;
        character["PetList"] = assets.Pets;
        character["PetMaxSize"] = assets.PetMaxSize;
        character["HorseList"] = assets.Horses;
        character["HorseEquipList"] = assets.HorseEquips;
        character["HorseEquipMaxSize"] = assets.HorseEquipMaxSize;

        foreach (var eq in assets.Equips)
        {
            if (Convert.ToInt32(eq["InBank"]) != 0 ||
                Convert.ToInt32(eq["InShop"]) != 0)
                continue;

            var part = Convert.ToInt32(eq["Part"]);
            var itemIdx = Convert.ToInt32(eq["ItemIdx"]);
            var color = Convert.ToInt32(eq["Color"]);

            switch (part)
            {
                case 0: character["Bonnet"] = itemIdx; break;
                case 1: character["Left_hand"] = itemIdx; break;
                case 2: character["Right_hand"] = itemIdx; break;
                case 3: character["Upper_armor"] = itemIdx; break;
                case 4: character["Lower_armor"] = itemIdx; break;
                case 5: character["Weapon"] = itemIdx; break;
                case 6: character["Shield"] = itemIdx; break;
                case 7: character["Back"] = itemIdx; break;
                case 8: character["Amulet"] = itemIdx; break;
            }

            if (part is >= 0 and <= 8)
                character[$"EquipColor{part}"] = color;
        }

        var summonedHorseUid = Convert.ToInt64(
            character.GetValueOrDefault("SummonedHorseUID", -1L));

        if (summonedHorseUid > 0)
        {
            var horse = assets.Horses.FirstOrDefault(x =>
                Convert.ToInt64(x["HorseUID"]) == summonedHorseUid);

            if (horse is not null)
            {
                character["MechaType"] =
                    Convert.ToString(horse["HorseType"]) ?? "MS";
            }

            foreach (var eq in assets.HorseEquips)
            {
                if (Convert.ToInt64(eq["HorseUID"]) != summonedHorseUid)
                    continue;

                var part = Convert.ToInt32(eq["Part"]);
                var itemIdx = Convert.ToInt32(eq["EquipIdx"]);
                var color = Convert.ToInt32(eq["Color"]);

                switch (part)
                {
                    case 0:
                        character["MechaBody"] = itemIdx;
                        character["EquipColor0"] = color;
                        break;
                    case 1:
                        character["MechaWeapon"] = itemIdx;
                        character["EquipColor1"] = color;
                        break;
                    case 2:
                        character["MechaEngine"] = itemIdx;
                        character["EquipColor2"] = color;
                        break;
                }
            }
        }
    }

    private object BuildEffectiveStatusPacket(
        long accountId,
        long dbid,
        SGO.Server.Domain.Assets.CharacterAssets assets)
    {
        var character = _store.GetCharacter(accountId, dbid);
        if (character is null)
            return new { Type = "PlayerStatusSync" };

        return CharacterStatCalculator.ToPlayerStatusPacket(
            character,
            assets);
    }

    private object BuildAppearancePacket(
        long accountId,
        long dbid,
        SGO.Server.Domain.Assets.CharacterAssets assets)
    {
        var character = _store.GetCharacter(accountId, dbid);
        if (character is null)
            return new
            {
                Type = "UpdateAppearance",
                EntityID = dbid
            };

        ApplyAssetsToCharacterBootstrap(character, assets);

        var packet = new Dictionary<string, object>(character)
        {
            ["Type"] = "UpdateAppearance",
            ["EntityID"] = dbid
        };

        return packet;
    }

    private static void ApplyEffectiveStats(
        Dictionary<string, object> character,
        SGO.Server.Domain.Assets.CharacterAssets assets)
    {
        var stats = CharacterStatCalculator.Calculate(
            character,
            assets);

        character["Attack"] = stats.Attack;
        character["Defense"] = stats.Defense;
        character["MagicAttack"] = stats.MagicAttack;
        character["MagicDefense"] = stats.MagicDefense;
        character["Hit"] = stats.Hit;
        character["Dodge"] = stats.Dodge;
        character["Lucky"] = stats.Lucky;
        character["AttackSpeed"] = stats.AttackSpeed;
        character["AttackRange"] = stats.AttackRange;
        character["MoveSpeed"] = stats.MoveSpeed;
        character["CritRate"] = stats.CritRate;
        character["CritDamage"] = stats.CritDamage;
    }

    private static object? GetCharacterData(
        Dictionary<string, object> character,
        string name)
    {
        if (character.TryGetValue(name, out var exact))
            return exact;

        var pair = character.FirstOrDefault(
            x => x.Key.Equals(name, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrEmpty(pair.Key) ? 0 : pair.Value;
    }
}
