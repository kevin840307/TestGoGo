using System.Text.Json;
using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Assets;
using SGO.Server.Application.Combat;
using SGO.Server.Application.Character;
using SGO.Server.Application.Gm;
using SGO.Server.Application.Items;
using SGO.Server.Application.Packets;
using SGO.Server.Application.Shops;
using SGO.Server.Domain.Sessions;
using SGO.Server.Domain.World;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Quest;
using SGO.Server.Infrastructure.WorldData;
using SGO.Server.Protocol;

namespace SGO.Server.Application;

public sealed class PacketRouter
{
    private const int NpcInteractRange = 500;

    private readonly IGameStore _store;
    private readonly WorldDataService _world;
    private readonly QuestRuntime _quests;
    private readonly CombatService _combat;
    private readonly ResourceRecoveryService _resourceRecovery;
    private readonly ShopService _shopService;
    private readonly IReadOnlyDictionary<string, IClientPacketHandler> _packetHandlers;
    private readonly GmCommandService _gm;
    private readonly DollService _dolls;
    private readonly HorseBatteryCatalog _horseBatteries;
    private readonly RecoveryItemCatalog _recoveryItems;
    private readonly HorseCardCatalog _horseCards;
    private readonly CurrentClientDataCatalog _current;
    private readonly LegacyPlayerGrowthCatalog _growth;
    private readonly ILogger<PacketRouter> _log;

    public PacketRouter(
        IGameStore store,
        WorldDataService world,
        QuestRuntime quests,
        CombatService combat,
        ResourceRecoveryService resourceRecovery,
        ShopService shopService,
        IEnumerable<IClientPacketHandler> packetHandlers,
        GmCommandService gm,
        DollService dolls,
        HorseBatteryCatalog horseBatteries,
        RecoveryItemCatalog recoveryItems,
        HorseCardCatalog horseCards,
        CurrentClientDataCatalog current,
        LegacyPlayerGrowthCatalog growth,
        ILogger<PacketRouter> log)
    {
        _store = store;
        _world = world;
        _quests = quests;
        _combat = combat;
        _resourceRecovery = resourceRecovery;
        _shopService = shopService;
        _packetHandlers = packetHandlers.ToDictionary(
            x => x.PacketType,
            StringComparer.Ordinal);
        _gm = gm;
        _dolls = dolls;
        _horseBatteries = horseBatteries;
        _recoveryItems = recoveryItems;
        _horseCards = horseCards;
        _current = current;
        _growth = growth;
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

        if (_packetHandlers.TryGetValue(type, out var moduleHandler))
            return moduleHandler.Handle(req, state, accountId);

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
                state.LastX = Convert.ToDouble(character["LocX"]);
                state.LastY = Convert.ToDouble(character["LocY"]);
                state.LastResourceRecoveryAt = DateTimeOffset.UtcNow;
                state.PendingMapSpawn = true;
                character["Type"] = "EnterWorldSuccess";

                var assets = _store.GetCharacterAssets(accountId, dbid);
                ApplyAssetsToCharacterBootstrap(character, assets);
                character["Shortcuts"] = _store.GetShortcuts(accountId, dbid);
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
                    new
                    {
                        Type = "SkillSync",
                        Skills = _store.GetSkills(accountId, dbid)
                    },
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
                state.LastX = x;
                state.LastY = y;
                _store.UpdatePosition(
                    accountId,
                    state.CharacterId.Value,
                    x,
                    y);

                // World spawn/AI is driven by the server tick. Position packets
                // now only update authoritative player coordinates.
                return [];
            }

            case "UpdateShortcut":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var ok = _store.UpdateShortcut(
                    accountId,
                    state.CharacterId.Value,
                    req.Int("SlotIndex", -1),
                    req.Int("ShortcutType"),
                    req.Int("TargetID"));

                if (!ok)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "快速鍵儲存失敗"
                        }
                    ];
                }

                return [];
            }

            case "UseItemRequest":
            {
                if (!state.CharacterId.HasValue) return [];

                var itemUid = req.Int("ItemUID");
                var assetsBefore = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                var inventoryItem = assetsBefore.Inventory.FirstOrDefault(x =>
                    Convert.ToInt64(x["ItemUID"]) == itemUid);

                if (inventoryItem is null)
                    return [new { Type = "SystemMessageFlash", Message = "找不到物品" }];

                var itemIndex = Convert.ToInt32(
                    inventoryItem["ItemIndex"]);

                if (_dolls.Supports(itemIndex))
                {
                    return _dolls.Open(
                        accountId,
                        state.CharacterId.Value,
                        itemUid,
                        itemIndex);
                }

                var recovery = _recoveryItems.Find(itemIndex);
                if (recovery is not null)
                {
                    var character = _store.UseRecoveryItem(
                        accountId,
                        state.CharacterId.Value,
                        itemUid,
                        recovery.RecoverHp,
                        recovery.RecoverMp,
                        recovery.RecoverSp);

                    if (character is null)
                        return [new { Type = "SystemMessageFlash", Message = "無法使用此恢復物品" }];

                    var assets = _store.GetCharacterAssets(
                        accountId,
                        state.CharacterId.Value);

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
                            Type = "InventorySync",
                            Items = assets.Inventory,
                            MaxSize = assets.InventoryMaxSize
                        },
                        new
                        {
                            Type = "PlayItemEffect",
                            EntityID = state.CharacterId.Value,
                            ItemIndex = itemIndex
                        }
                    ];
                }

                var horseCard = _horseCards.Find(itemIndex);
                if (horseCard is not null)
                {
                    var horse = _store.ActivateHorseCard(
                        accountId,
                        state.CharacterId.Value,
                        itemUid,
                        horseCard.ItemIndex,
                        horseCard.Name,
                        horseCard.HorseType,
                        horseCard.MaxBattery);

                    if (horse is null)
                        return [new { Type = "SystemMessageFlash", Message = "機甲卡啟用失敗" }];

                    var activatedHorseUid =
                        Convert.ToInt64(horse["HorseUID"]);
                    var preset = _current.FindHorsePreset(
                        horseCard.ItemIndex);

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
                                state.CharacterId.Value,
                                equipIndex,
                                activatedHorseUid,
                                equip?.EquipSlot ?? 255);
                        }
                    }

                    var assets = _store.GetCharacterAssets(
                        accountId,
                        state.CharacterId.Value);

                    return
                    [
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
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = preset is null
                                ? $"已啟用機甲：{horseCard.Name}"
                                : $"已啟用機甲：{horseCard.Name}（含預設裝備）"
                        }
                    ];
                }

                return
                [
                    new
                    {
                        Type = "SystemMessageFlash",
                        Message = "此物品的使用效果尚未支援"
                    }
                ];
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

                var equipUid = req.Int("EquipUID");
                var horseUid = req.Int("HorseUID");
                var assetsBefore = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                var equipRecord = assetsBefore.HorseEquips.FirstOrDefault(x =>
                    Convert.ToInt64(x["EquipUID"]) == equipUid);
                var horseRecord = assetsBefore.Horses.FirstOrDefault(x =>
                    Convert.ToInt64(x["HorseUID"]) == horseUid);

                if (equipRecord is null || horseRecord is null)
                {
                    return [new
                    {
                        Type = "SystemMessageFlash",
                        Message = "找不到機甲或機甲裝備"
                    }];
                }

                var item = _current.FindItem(
                    Convert.ToInt32(equipRecord["EquipIdx"]));

                if (item is null)
                {
                    return [new
                    {
                        Type = "SystemMessageFlash",
                        Message = "機甲裝備資料不存在"
                    }];
                }

                if (Convert.ToInt32(horseRecord["Level"]) < item.ReqLevel)
                {
                    return [new
                    {
                        Type = "SystemMessageFlash",
                        Message = $"機甲等級不足，需要 Lv.{item.ReqLevel}"
                    }];
                }

                var horseType = horseRecord["HorseType"]?.ToString() ?? "MS";
                if (!string.IsNullOrWhiteSpace(item.HorseType) &&
                    !string.Equals(
                        item.HorseType,
                        horseType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return [new
                    {
                        Type = "SystemMessageFlash",
                        Message = "此強化/裝備不適用於這種機甲類型"
                    }];
                }

                var success = _store.EquipHorseItem(
                    accountId,
                    state.CharacterId.Value,
                    equipUid,
                    horseUid);

                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                var response = new List<object>
                {
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
                };

                if (success)
                    response.Add(BuildEffectiveStatusPacket(
                        accountId,
                        state.CharacterId.Value,
                        assets));

                return response;
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

                var response = new List<object>
                {
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
                };

                if (success)
                    response.Add(BuildEffectiveStatusPacket(
                        accountId,
                        state.CharacterId.Value,
                        assets));

                return response;
            }

            case "PetAction":
            {
                if (!state.CharacterId.HasValue) return [];

                var action = req.Str("Action");

                if (action == "SetCombatMode")
                {
                    state.PetCombatMode = Math.Clamp(
                        req.Int("CombatMode"),
                        0,
                        3);
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "寵物戰鬥模式已更新"
                        }
                    ];
                }

                if (action == "CommandAttack")
                {
                    var assetsForAttack = _store.GetCharacterAssets(
                        accountId,
                        state.CharacterId.Value);
                    var activePet = assetsForAttack.Pets.FirstOrDefault(x =>
                        Convert.ToInt32(x["State"]) == 1);

                    if (activePet is null)
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = "目前沒有召喚中的寵物"
                            }
                        ];

                    var activePetUid = Convert.ToInt64(activePet["UID"]);
                    var targetEntityId = req.Int("TargetEntityID");
                    if (state.Monsters.TryGetValue(
                            targetEntityId,
                            out var commandTarget) &&
                        commandTarget.IsAlive &&
                        commandTarget.MapId == state.CurrentMapId)
                    {
                        state.PetTargets[activePetUid] = targetEntityId;
                    }

                    var activePetPosition =
                        state.PetPositions.TryGetValue(
                            activePetUid,
                            out var runtimePosition)
                            ? runtimePosition
                            : (
                                X: state.LastX + 40,
                                Y: state.LastY + 20);

                    return _combat.PetAttack(
                        state,
                        activePetUid,
                        Math.Max(1, Convert.ToInt32(activePet["Attack"])),
                        targetEntityId,
                        activePetPosition.X,
                        activePetPosition.Y);
                }

                var petUid = req.Int("PetUID");
                var previouslyActivePets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value).Pets
                    .Where(x => Convert.ToInt32(x["State"]) == 1)
                    .Select(x => Convert.ToInt64(x["UID"]))
                    .ToArray();

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
                    foreach (var oldPetUid in previouslyActivePets)
                    {
                        if (oldPetUid == petUid) continue;
                        state.PetPositions.Remove(oldPetUid);
                        state.PetLastAttackAt.Remove(oldPetUid);
                        state.PetTargets.Remove(oldPetUid);
                        state.PetOutOfRangeSince.Remove(oldPetUid);
                        responses.Add(new
                        {
                            Type = "DespawnEntity",
                            EntityID = 4_000_000L + oldPetUid
                        });
                    }

                    var pet = assets.Pets.FirstOrDefault(x =>
                        Convert.ToInt64(x["UID"]) == petUid);
                    var character = _store.GetCharacter(
                        accountId,
                        state.CharacterId.Value);
                    if (pet is not null && character is not null)
                    {
                        var spawnX = Convert.ToDouble(character["LocX"]) + 40;
                        var spawnY = Convert.ToDouble(character["LocY"]) + 20;
                        state.PetPositions[petUid] = (spawnX, spawnY);
                        responses.Add(BuildPetSpawnPacket(
                            state.CharacterId.Value,
                            pet,
                            spawnX,
                            spawnY));
                    }
                }
                else if (success && action == "Recall")
                {
                    state.PetPositions.Remove(petUid);
                    state.PetLastAttackAt.Remove(petUid);
                    state.PetTargets.Remove(petUid);
                    state.PetOutOfRangeSince.Remove(petUid);
                    responses.Add(new
                    {
                        Type = "DespawnEntity",
                        EntityID = 4_000_000L + petUid
                    });
                }

                return responses;
            }

            case "SetTitleRequest":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var titleId = Math.Clamp(req.Int("TitleID"), 0, 26);
                var character = _store.GetCharacter(
                    accountId,
                    state.CharacterId.Value);

                if (character is null)
                    return [];

                // The current lightweight persistence schema does not yet
                // expose a dedicated title write port. Keep the client and
                // nearby entity presentation in sync for this session.
                character["Title"] = titleId;

                return
                [
                    new
                    {
                        Type = "UpdateAppearance",
                        EntityID = state.CharacterId.Value,
                        Title = titleId
                    }
                ];
            }

            case "PartyAction":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var action = req.Str("Action");
                var self = _store.GetCharacter(
                    accountId,
                    state.CharacterId.Value);

                if (self is null)
                    return [];

                if (action == "Create")
                {
                    if (string.IsNullOrWhiteSpace(state.PartyId))
                    {
                        state.PartyId = $"local-{state.CharacterId.Value}";
                        state.PartyLeaderDbid = state.CharacterId.Value;
                        state.PartyMembers.Clear();
                        state.PartyMembers.Add(new Dictionary<string, object>
                        {
                            ["DBID"] = state.CharacterId.Value,
                            ["Name"] = self["NAME"],
                            ["Level"] = self["Level"],
                            ["HP"] = self["HP"],
                            ["MaxHP"] = self["MaxHP"],
                            ["MP"] = self["MP"],
                            ["MaxMP"] = self["MaxMP"],
                            ["SP"] = self["SP"],
                            ["MaxSP"] = self["MaxSP"]
                        });
                    }

                    return
                    [
                        new
                        {
                            Type = "PartySync",
                            PartyID = state.PartyId,
                            LeaderDBID = state.PartyLeaderDbid,
                            Members = state.PartyMembers
                        }
                    ];
                }

                if (action is "Leave" or "Disband")
                {
                    state.PartyId = "";
                    state.PartyLeaderDbid = 0;
                    state.PartyMembers.Clear();

                    return
                    [
                        new
                        {
                            Type = "PartySync",
                            PartyID = "",
                            LeaderDBID = 0,
                            Members = Array.Empty<object>()
                        }
                    ];
                }

                return
                [
                    new
                    {
                        Type = "SystemMessageFlash",
                        Message = $"隊伍操作尚未支援：{action}"
                    }
                ];
            }

            case "DropItemRequest":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var tabType = req.Int("TabType");
                var uid = req.Int("ItemUID");
                var requestedCount = Math.Max(1, req.Int("Count", 1));

                // Pet and whole-mecha discard are intentionally not treated as
                // ground loot yet because restoring their full runtime state
                // requires a richer persistence payload.
                if (tabType is 2 or 4)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = tabType == 2
                                ? "寵物丟棄尚未開放"
                                : "整台機甲丟棄尚未開放"
                        }
                    ];
                }

                var assetsBefore = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                Dictionary<string, object>? source = tabType switch
                {
                    0 or 5 => assetsBefore.Inventory.FirstOrDefault(x =>
                        Convert.ToInt64(x["ItemUID"]) == uid),
                    1 => assetsBefore.Equips.FirstOrDefault(x =>
                        Convert.ToInt64(x["ItemID"]) == uid),
                    3 => assetsBefore.HorseEquips.FirstOrDefault(x =>
                        Convert.ToInt64(x["EquipUID"]) == uid),
                    _ => null
                };

                if (source is null)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "找不到可丟棄的物品"
                        }
                    ];
                }

                var itemIndex = tabType switch
                {
                    0 or 5 => Convert.ToInt32(source["ItemIndex"]),
                    1 => Convert.ToInt32(source["ItemIdx"]),
                    3 => Convert.ToInt32(source["EquipIdx"]),
                    _ => 0
                };

                var actualCount = tabType is 0 or 5
                    ? Math.Min(
                        requestedCount,
                        Convert.ToInt32(source["Count"]))
                    : 1;

                var damage = source.TryGetValue("Damage", out var damageValue)
                    ? Convert.ToInt32(damageValue)
                    : 0;
                var color = source.TryGetValue("Color", out var colorValue)
                    ? Convert.ToInt32(colorValue)
                    : 0;
                var part = source.TryGetValue("Part", out var partValue)
                    ? Convert.ToInt32(partValue)
                    : 255;

                var (discarded, discardMessage) = _store.DiscardAsset(
                    accountId,
                    state.CharacterId.Value,
                    uid,
                    tabType,
                    actualCount);

                if (!discarded)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = discardMessage
                        }
                    ];
                }

                var entityId = state.NextGroundItemEntityId++;
                var dropX = req.Int("DropX", (int)state.LastX);
                var dropY = req.Int("DropY", (int)state.LastY);

                var ground = new GroundItemRuntime
                {
                    EntityId = entityId,
                    MapId = state.CurrentMapId,
                    TabType = tabType,
                    ItemIndex = itemIndex,
                    Count = actualCount,
                    Damage = damage,
                    Color = color,
                    Part = part,
                    X = dropX,
                    Y = dropY,
                    OwnerDbid = state.CharacterId.Value
                };

                state.GroundItems[entityId] = ground;
                var itemName =
                    _current.FindItem(itemIndex)?.Name ?? $"Item {itemIndex}";
                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                return
                [
                    new
                    {
                        Type = "SpawnEntity",
                        EntityID = entityId,
                        EntityType = 3,
                        Name = itemName,
                        LocX = dropX,
                        LocY = dropY,
                        ExtraData = new
                        {
                            ItemIndex = itemIndex,
                            Count = actualCount,
                            RemainingTime = 60,
                            OwnerDBID = state.CharacterId.Value,
                            ProtectTimeSeconds = 5
                        }
                    },
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
                        Type = "HorseSync",
                        Horses = assets.Horses,
                        HorseEquips = assets.HorseEquips,
                        MaxHorseEquipSize = assets.HorseEquipMaxSize
                    }
                ];
            }

            case "PickUpItemRequest":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var entityId = req.Int("EntityID");
                if (!state.GroundItems.TryGetValue(entityId, out var ground) ||
                    ground.MapId != state.CurrentMapId)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "物品已不存在"
                        }
                    ];
                }

                var dx = ground.X - state.LastX;
                var dy = ground.Y - state.LastY;
                if (dx * dx + dy * dy > 100.0 * 100.0)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "距離物品太遠"
                        }
                    ];
                }

                switch (ground.TabType)
                {
                    case 0:
                    case 5:
                        _store.AddInventoryItem(
                            accountId,
                            state.CharacterId.Value,
                            ground.ItemIndex,
                            ground.Count);
                        break;

                    case 1:
                        _store.AddEquipment(
                            accountId,
                            state.CharacterId.Value,
                            ground.ItemIndex,
                            ground.Part,
                            ground.Damage,
                            ground.Color);
                        break;

                    case 3:
                        _store.AddHorseEquipment(
                            accountId,
                            state.CharacterId.Value,
                            ground.ItemIndex,
                            -1,
                            ground.Part,
                            ground.Damage,
                            ground.Color);
                        break;

                    default:
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = "此類物品目前無法撿回"
                            }
                        ];
                }

                state.GroundItems.Remove(entityId);
                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                return
                [
                    new
                    {
                        Type = "DespawnEntity",
                        EntityID = entityId
                    },
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
                        Type = "HorseSync",
                        Horses = assets.Horses,
                        HorseEquips = assets.HorseEquips,
                        MaxHorseEquipSize = assets.HorseEquipMaxSize
                    }
                ];
            }

            case "ShopAction":
            {
                if (!state.CharacterId.HasValue) return [];

                var action = req.Str("Action");

                if (action == "BuyNpcItem")
                {
                    return _shopService.Buy(
                        accountId,
                        state.CharacterId.Value,
                        req.Int("ShopID"),
                        req.Int("ItemIndex"),
                        req.Int("Count", 1));
                }

                if (action == "SellNpcItem")
                {
                    return _shopService.Sell(
                        accountId,
                        state.CharacterId.Value,
                        req.Int("UID"),
                        req.Int("TabType"),
                        req.Int("Count", 1));
                }

                if (action == "SyncMyShop")
                {
                    return
                    [
                        new
                        {
                            Type = "ShopSync",
                            Items = state.PlayerShopItems
                        }
                    ];
                }

                if (action == "AddItem")
                {
                    var uid = req.Int("UID");
                    var tabType = req.Int("TabType");
                    var count = Math.Max(1, req.Int("Count", 1));
                    var price = Math.Max(1, req.Int("Price", 1));

                    state.PlayerShopItems.RemoveAll(x =>
                        Convert.ToInt64(x["UID"]) == uid &&
                        Convert.ToInt32(x["TabType"]) == tabType);

                    state.PlayerShopItems.Add(new Dictionary<string, object>
                    {
                        ["UID"] = uid,
                        ["Count"] = count,
                        ["Price"] = price,
                        ["TabType"] = tabType
                    });

                    return
                    [
                        new
                        {
                            Type = "ShopSync",
                            Items = state.PlayerShopItems
                        }
                    ];
                }

                if (action == "RemoveItem")
                {
                    var uid = req.Int("UID");
                    var tabType = req.Int("TabType");

                    state.PlayerShopItems.RemoveAll(x =>
                        Convert.ToInt64(x["UID"]) == uid &&
                        Convert.ToInt32(x["TabType"]) == tabType);

                    return
                    [
                        new
                        {
                            Type = "ShopSync",
                            Items = state.PlayerShopItems
                        }
                    ];
                }

                if (action == "OpenShop")
                {
                    state.IsPlayerShopOpen = true;
                    state.PlayerShopName = req.Str(
                        "ShopName",
                        "我的商店");

                    return
                    [
                        new
                        {
                            Type = "SyncShopBoard",
                            EntityID = state.CharacterId.Value,
                            IsOpen = true,
                            ShopName = state.PlayerShopName
                        }
                    ];
                }

                if (action == "CloseShop")
                {
                    state.IsPlayerShopOpen = false;

                    return
                    [
                        new
                        {
                            Type = "SyncShopBoard",
                            EntityID = state.CharacterId.Value,
                            IsOpen = false,
                            ShopName = state.PlayerShopName
                        }
                    ];
                }

                return
                [
                    new
                    {
                        Type = "SystemMessageFlash",
                        Message = $"玩家商店操作尚未支援：{action}"
                    }
                ];
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
            {
                var packets = EnsureCurrentCombatMapLoaded(
                    state,
                    accountId);

                packets.AddRange(_combat.Attack(
                    state,
                    accountId,
                    req.Int("TargetEntityID"),
                    req.Int("TargetType", 1)));

                return packets;
            }

            case "UseSkill":
            {
                var packets = EnsureCurrentCombatMapLoaded(
                    state,
                    accountId);

                packets.AddRange(_combat.UseSkill(
                    state,
                    accountId,
                    req.Int("SkillID"),
                    req.Int("TargetID"),
                    req.Int("TargetType", 1),
                    req.Int("TargetX"),
                    req.Int("TargetY"),
                    DateTimeOffset.UtcNow));

                return packets;
            }

            case "AllocateStatRequest":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var updated = _store.AllocateStat(
                    accountId,
                    state.CharacterId.Value,
                    req.Str("StatType"));

                if (updated is null)
                {
                    return [new
                    {
                        Type = "AllocateStatResponse",
                        Success = false,
                        Message = "沒有可用能力點或屬性已達上限"
                    }];
                }

                var growth = _growth.Calculate(updated);
                updated = _store.ApplyGrowthStats(
                    accountId,
                    state.CharacterId.Value,
                    growth.MaxHP,
                    growth.MaxMP,
                    growth.MaxSP,
                    growth.Attack,
                    growth.Defense,
                    growth.MagicAttack,
                    growth.MagicDefense,
                    growth.Hit,
                    growth.Dodge,
                    growth.MoveSpeed) ?? updated;

                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);
                var stats = CharacterStatCalculator.Calculate(
                    updated,
                    assets,
                    _current);

                return [new
                {
                    Type = "AllocateStatResponse",
                    Success = true,
                    ExpUse = updated["ExpUse"],
                    STR = updated["Str"],
                    DEX = updated["Dex"],
                    CON = updated["Con"],
                    WIS = updated["Wis"],
                    MaxHP = updated["MaxHP"],
                    MaxMP = updated["MaxMP"],
                    MaxSP = updated["MaxSP"],
                    Attack = stats.Attack,
                    Defense = stats.Defense,
                    MagicAttack = stats.MagicAttack,
                    MagicDefense = stats.MagicDefense,
                    Hit = stats.Hit,
                    Dodge = stats.Dodge,
                    Lucky = stats.Lucky,
                    AttackSpeed = stats.AttackSpeed,
                    AttackRange = stats.AttackRange,
                    MoveSpeed = stats.MoveSpeed,
                    CritRate = stats.CritRate,
                    CritDamage = stats.CritDamage
                }];
            }

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

                if (message.StartsWith(
                        "/gm",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return _gm.Execute(message, state);
                }

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
                state.MonstersByMap.Clear();
                state.LoadedMonsterMapId = 0;
                state.ActiveSkillBuffs.Clear();
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
                _log.LogWarning(
                    "[UNHANDLED_PACKET] Type={Type} Payload={Payload}",
                    type,
                    TruncatePayload(req.GetRawText()));
                return [];
        }
    }

    public IReadOnlyList<object> Tick(
        ClientSession state,
        DateTimeOffset now)
    {
        if (!state.AccountId.HasValue ||
            !state.CharacterId.HasValue ||
            state.CurrentMapId <= 0)
            return [];

        var accountId = state.AccountId.Value;
        var dbid = state.CharacterId.Value;
        var packets = new List<object>();

        packets.AddRange(
            _resourceRecovery.Tick(state, now));
        packets.AddRange(
            _combat.TickBuffs(state, now));
        packets.AddRange(
            _combat.TickRespawns(state, now));

        if (state.PendingMapSpawn)
        {
            state.PendingMapSpawn = false;
            state.PetTargets.Clear();
            state.PetOutOfRangeSince.Clear();
            packets.AddRange(
                _combat.EnterMap(
                    state,
                    state.CurrentMapId));
            packets.AddRange(
                BuildActivePetSpawnPackets(
                    state,
                    accountId,
                    dbid,
                    state.LastX,
                    state.LastY));

            // The Godot map scene is loaded asynchronously. Re-send NPC,
            // monster and active-pet snapshots long enough for the target
            // scene to finish loading even when the first spawn packets arrive
            // too early.
            state.WorldSnapshotRetriesRemaining = 8;
            state.NextWorldSnapshotAt =
                now + TimeSpan.FromMilliseconds(500);
        }
        else
        {
            if (state.WorldSnapshotRetriesRemaining > 0 &&
                now >= state.NextWorldSnapshotAt)
            {
                packets.AddRange(
                    _combat.BuildCurrentMapSnapshot(state));
                packets.AddRange(
                    BuildActivePetSnapshotPackets(
                        state,
                        accountId,
                        dbid,
                        state.LastX,
                        state.LastY));

                state.WorldSnapshotRetriesRemaining--;
                state.NextWorldSnapshotAt =
                    now + TimeSpan.FromMilliseconds(750);
            }

            // Pet AI runs before monster AI so an active pet can
            // acquire/attack nearby enemies even when the monster is already
            // close enough to attack the player on the same server tick.
            packets.AddRange(
                BuildPetFollowPackets(
                    state,
                    accountId,
                    dbid,
                    state.LastX,
                    state.LastY,
                    now));
            packets.AddRange(
                _combat.TickAi(
                    state,
                    accountId,
                    state.LastX,
                    state.LastY,
                    now));
        }

        return packets;
    }

    private List<object> EnsureCurrentCombatMapLoaded(
        ClientSession state,
        long accountId)
    {
        var packets = new List<object>();

        if (!state.CharacterId.HasValue ||
            state.CurrentMapId <= 0)
            return packets;

        if (!state.PendingMapSpawn &&
            state.LoadedMonsterMapId == state.CurrentMapId)
            return packets;

        state.PendingMapSpawn = false;
        packets.AddRange(
            _combat.EnterMap(
                state,
                state.CurrentMapId));

        packets.AddRange(
            BuildActivePetSpawnPackets(
                state,
                accountId,
                state.CharacterId.Value,
                state.LastX,
                state.LastY));

        state.WorldSnapshotRetriesRemaining = 4;
        state.NextWorldSnapshotAt =
            DateTimeOffset.UtcNow +
            TimeSpan.FromMilliseconds(750);

        return packets;
    }

    private static string TruncatePayload(
        string payload,
        int maxLength = 1200)
        => payload.Length <= maxLength
            ? payload
            : payload[..maxLength] + "...<truncated>";

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
                state.LastX = result.NewX.Value;
                state.LastY = result.NewY.Value;
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

    private IReadOnlyList<object> BuildActivePetSpawnPackets(
        ClientSession state,
        long accountId,
        long dbid,
        double playerX,
        double playerY)
    {
        var assets = _store.GetCharacterAssets(accountId, dbid);
        var packets = new List<object>();

        foreach (var pet in assets.Pets.Where(x =>
                     Convert.ToInt32(x["State"]) == 1))
        {
            var petUid = Convert.ToInt64(pet["UID"]);
            var spawnX = playerX + 40;
            var spawnY = playerY + 20;

            state.PetPositions[petUid] = (spawnX, spawnY);
            state.PetLastAttackAt.Remove(petUid);

            packets.Add(BuildPetSpawnPacket(
                dbid,
                pet,
                spawnX,
                spawnY));
        }

        return packets;
    }

    private IReadOnlyList<object> BuildActivePetSnapshotPackets(
        ClientSession state,
        long accountId,
        long dbid,
        double playerX,
        double playerY)
    {
        var assets = _store.GetCharacterAssets(accountId, dbid);
        var packets = new List<object>();

        foreach (var pet in assets.Pets.Where(x =>
                     Convert.ToInt32(x["State"]) == 1))
        {
            var petUid = Convert.ToInt64(pet["UID"]);

            if (!state.PetPositions.TryGetValue(
                    petUid,
                    out var position))
            {
                position = (
                    X: playerX + 40,
                    Y: playerY + 20);
                state.PetPositions[petUid] = position;
            }

            packets.Add(BuildPetSpawnPacket(
                dbid,
                pet,
                position.X,
                position.Y));
        }

        return packets;
    }

    private IReadOnlyList<object> BuildPetFollowPackets(
        ClientSession state,
        long accountId,
        long dbid,
        double playerX,
        double playerY,
        DateTimeOffset now)
    {
        // WebSocketEndpoint already drives PacketRouter.Tick on the fixed
        // 250 ms world cadence. Do not add a second 250 ms gate here or a
        // 249.x ms timer tick can be skipped and make pet movement stutter.
        state.LastPetFollowTick = now;

        var assets = _store.GetCharacterAssets(accountId, dbid);
        var character = _store.GetCharacter(accountId, dbid);
        var playerAlive = character is not null &&
                          Convert.ToInt32(character["HP"]) > 0;
        var packets = new List<object>();

        foreach (var pet in assets.Pets.Where(x =>
                     Convert.ToInt32(x["State"]) == 1))
        {
            var petUid = Convert.ToInt64(pet["UID"]);
            var petEntityId = 4_000_000L + petUid;

            if (!state.PetPositions.TryGetValue(
                    petUid,
                    out var petPosition))
            {
                petPosition = (playerX + 40, playerY + 20);
                state.PetPositions[petUid] = petPosition;
                packets.Add(BuildPetSpawnPacket(
                    dbid,
                    pet,
                    petPosition.X,
                    petPosition.Y));
            }

            // Let the pet fall behind naturally. Only hard-return after it
            // stays roughly beyond a full-screen distance for several seconds.
            // The server does not know the exact client viewport, so 900 px is
            // used as a conservative "off-screen" leash approximation.
            var ownerFollowX = playerX + 42;
            var ownerFollowY = playerY + 24;
            var ownerDx = ownerFollowX - petPosition.X;
            var ownerDy = ownerFollowY - petPosition.Y;
            var ownerDistanceSquared =
                ownerDx * ownerDx +
                ownerDy * ownerDy;

            const double petTeleportDistance = 900.0;
            var beyondTeleportLeash =
                ownerDistanceSquared >
                petTeleportDistance * petTeleportDistance;

            if (beyondTeleportLeash)
            {
                if (!state.PetOutOfRangeSince.ContainsKey(petUid))
                    state.PetOutOfRangeSince[petUid] = now;

                if (now - state.PetOutOfRangeSince[petUid] >=
                    TimeSpan.FromSeconds(3))
                {
                    state.PetPositions[petUid] =
                        (ownerFollowX, ownerFollowY);
                    state.PetLastAttackAt.Remove(petUid);
                    state.PetTargets.Remove(petUid);
                    state.PetOutOfRangeSince.Remove(petUid);

                    packets.Add(new
                    {
                        Type = "MoveEntity",
                        EntityID = petEntityId,
                        X = ownerFollowX,
                        Y = ownerFollowY,
                        Dir = DirectionFromDelta(ownerDx, ownerDy),
                        SpeedTime = 0.01
                    });

                    continue;
                }
            }
            else
            {
                state.PetOutOfRangeSince.Remove(petUid);
            }

            MonsterRuntimeState? stickyTarget = null;
            if (playerAlive &&
                state.PetTargets.TryGetValue(
                    petUid,
                    out var stickyTargetId) &&
                state.Monsters.TryGetValue(
                    stickyTargetId,
                    out var remembered) &&
                remembered.IsAlive &&
                remembered.MapId == state.CurrentMapId)
            {
                var stickyDx = remembered.X - petPosition.X;
                var stickyDy = remembered.Y - petPosition.Y;

                // Keep an existing target over a wider leash than initial
                // acquisition so two nearby monsters do not make the pet
                // switch direction every AI tick.
                if (stickyDx * stickyDx + stickyDy * stickyDy <=
                    650.0 * 650.0)
                {
                    stickyTarget = remembered;
                }
            }

            if (stickyTarget is null)
                state.PetTargets.Remove(petUid);

            var target = playerAlive
                ? (stickyTarget is not null
                    ? new
                    {
                        Monster = stickyTarget,
                        DistanceSquared =
                            Math.Pow(stickyTarget.X - petPosition.X, 2) +
                            Math.Pow(stickyTarget.Y - petPosition.Y, 2)
                    }
                    : state.Monsters.Values
                        .Where(x =>
                            x.IsAlive &&
                            x.MapId == state.CurrentMapId)
                        .Select(x => new
                        {
                            Monster = x,
                            DistanceSquared =
                                Math.Pow(x.X - petPosition.X, 2) +
                                Math.Pow(x.Y - petPosition.Y, 2)
                        })
                        .Where(x =>
                            x.DistanceSquared <= 420.0 * 420.0)
                        .OrderBy(x => x.DistanceSquared)
                        .FirstOrDefault())
                : null;

            if (target is not null)
                state.PetTargets[petUid] = target.Monster.EntityId;

            if (target is not null)
            {
                var monster = target.Monster;
                var dx = monster.X - petPosition.X;
                var dy = monster.Y - petPosition.Y;
                var distance = Math.Sqrt(target.DistanceSquared);

                if (distance > 115.0)
                {
                    var step = Math.Min(
                        65.0,
                        Math.Max(0.0, distance - 110.0));
                    if (step > 0.0)
                    {
                        var nx = petPosition.X + dx / distance * step;
                        var ny = petPosition.Y + dy / distance * step;
                        state.PetPositions[petUid] = (nx, ny);

                        packets.Add(new
                        {
                            Type = "MoveEntity",
                            EntityID = petEntityId,
                            X = nx,
                            Y = ny,
                            Dir = DirectionFromDelta(dx, dy),
                            SpeedTime = 0.25
                        });
                    }

                    continue;
                }

                var lastAttackAt =
                    state.PetLastAttackAt.GetValueOrDefault(
                        petUid,
                        DateTimeOffset.MinValue);

                if (now - lastAttackAt >=
                    TimeSpan.FromMilliseconds(1200))
                {
                    var attackPackets = _combat.PetAttack(
                        state,
                        petUid,
                        Math.Max(1, Convert.ToInt32(pet["Attack"])),
                        monster.EntityId,
                        petPosition.X,
                        petPosition.Y);

                    if (attackPackets.Count > 0)
                    {
                        state.PetLastAttackAt[petUid] = now;
                        packets.AddRange(attackPackets);
                    }
                }

                continue;
            }

            var followX = playerX + 42;
            var followY = playerY + 24;
            var followDx = followX - petPosition.X;
            var followDy = followY - petPosition.Y;
            var followDistance = Math.Sqrt(
                followDx * followDx +
                followDy * followDy);

            if (followDistance <= 10.0)
                continue;

            var followStep = Math.Min(70.0, followDistance);
            var nextFollowX =
                petPosition.X + followDx / followDistance * followStep;
            var nextFollowY =
                petPosition.Y + followDy / followDistance * followStep;

            state.PetPositions[petUid] =
                (nextFollowX, nextFollowY);
            packets.Add(new
            {
                Type = "MoveEntity",
                EntityID = petEntityId,
                X = nextFollowX,
                Y = nextFollowY,
                Dir = DirectionFromDelta(followDx, followDy),
                SpeedTime = 0.25
            });
        }

        return packets;
    }

    private static string DirectionFromDelta(
        double dx,
        double dy)
    {
        if (Math.Abs(dx) < 0.001 &&
            Math.Abs(dy) < 0.001)
            return "1";

        var degrees = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (degrees < 0)
            degrees += 360.0;

        var dirs = new[] { "6", "7", "8", "1", "2", "3", "4", "5" };
        var index = (int)Math.Round(degrees / 45.0) % 8;
        return dirs[index];
    }

    private object BuildPetSpawnPacket(
        long ownerId,
        Dictionary<string, object> pet,
        double x,
        double y)
    {
        var petId = Convert.ToInt32(pet["PETID"]);
        var evolId = Convert.ToInt32(pet["EvolID"]);
        var evolution = _current.FindPetEvolution(
            petId,
            evolId);

        if (evolution is null)
        {
            _log.LogWarning(
                "[CURRENT_PET_APPEARANCE_MISSING] PetID={PetID} EvolID={EvolID}",
                petId,
                evolId);
        }

        return new
        {
            Type = "SpawnEntity",
            EntityID = 4_000_000L + Convert.ToInt64(pet["UID"]),
            EntityType = 4,
            Name = pet["Name"],
            LocX = x,
            LocY = y,
            ExtraData = new
            {
                OwnerID = ownerId,
                Sprite = evolution?.Outlook ?? "EN010A",
                Level = pet["Level"],
                MaxHP = pet["MaxHP"],
                CurrentHP = pet["HP"],
                ColorIndex = evolution?.Color ?? 0,
                AttackRange = evolution?.AttackRange ?? 80
            }
        };
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
            assets,
            _current);
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

    private void ApplyEffectiveStats(
        Dictionary<string, object> character,
        SGO.Server.Domain.Assets.CharacterAssets assets)
    {
        var stats = CharacterStatCalculator.Calculate(
            character,
            assets,
            _current);

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
