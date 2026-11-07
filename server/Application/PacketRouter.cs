using System.Text.Json;
using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Assets;
using SGO.Server.Application.Combat;
using SGO.Server.Application.Crafting;
using SGO.Server.Application.Club;
using SGO.Server.Application.Character;
using SGO.Server.Application.Gm;
using SGO.Server.Application.Items;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.Mall;
using SGO.Server.Application.Packets;
using SGO.Server.Application.Party;
using SGO.Server.Application.Rank;
using SGO.Server.Application.Shops;
using SGO.Server.Application.Trade;
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
    private readonly CraftingService _crafting;
    private readonly ClubService _club;
    private readonly ShopService _shopService;
    private readonly PlayerShopService _playerShop;
    private readonly IReadOnlyDictionary<string, IClientPacketHandler> _packetHandlers;
    private readonly GmCommandService _gm;
    private readonly DollService _dolls;
    private readonly HorseBatteryCatalog _horseBatteries;
    private readonly RecoveryItemCatalog _recoveryItems;
    private readonly HorseCardCatalog _horseCards;
    private readonly CurrentClientDataCatalog _current;
    private readonly LegacyPlayerGrowthCatalog _growth;
    private readonly LegacyPetGrowthCatalog _petGrowth;
    private readonly PetSkillCatalog _petSkills;
    private readonly MallService _mall;
    private readonly ServerBroadcastHub _broadcasts;
    private readonly PartyService _party;
    private readonly RankService _rank;
    private readonly TradeService _trade;
    private readonly ILogger<PacketRouter> _log;

    public PacketRouter(
        IGameStore store,
        WorldDataService world,
        QuestRuntime quests,
        CombatService combat,
        ResourceRecoveryService resourceRecovery,
        CraftingService crafting,
        ClubService club,
        ShopService shopService,
        PlayerShopService playerShop,
        IEnumerable<IClientPacketHandler> packetHandlers,
        GmCommandService gm,
        DollService dolls,
        HorseBatteryCatalog horseBatteries,
        RecoveryItemCatalog recoveryItems,
        HorseCardCatalog horseCards,
        CurrentClientDataCatalog current,
        LegacyPlayerGrowthCatalog growth,
        LegacyPetGrowthCatalog petGrowth,
        PetSkillCatalog petSkills,
        MallService mall,
        ServerBroadcastHub broadcasts,
        PartyService party,
        RankService rank,
        TradeService trade,
        ILogger<PacketRouter> log)
    {
        _store = store;
        _world = world;
        _quests = quests;
        _combat = combat;
        _resourceRecovery = resourceRecovery;
        _crafting = crafting;
        _club = club;
        _shopService = shopService;
        _playerShop = playerShop;
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
        _petGrowth = petGrowth;
        _petSkills = petSkills;
        _mall = mall;
        _broadcasts = broadcasts;
        _party = party;
        _rank = rank;
        _trade = trade;
        _log = log;
    }

    public void SyncPresence(ClientSession state)
    {
        var hasCurrent =
            state.AccountId.HasValue &&
            state.CharacterId.HasValue &&
            state.CurrentMapId > 0;

        var nextMapId = hasCurrent
            ? state.CurrentMapId
            : 0;
        var nextCharacterId = hasCurrent
            ? state.CharacterId!.Value
            : 0L;

        var changed =
            state.PublishedMapId != nextMapId ||
            state.PublishedCharacterId != nextCharacterId;

        if (state.PublishedCharacterId > 0 &&
            (state.PublishedCharacterId != nextCharacterId ||
             state.PublishedMapId != nextMapId))
        {
            _playerShop.Disconnect(
                state.ConnectionId,
                state.PublishedMapId,
                state.AccountId ?? 0,
                state.PublishedCharacterId);
        }

        if (state.PublishedCharacterId > 0 &&
            state.PublishedCharacterId != nextCharacterId)
        {
            _party.Disconnect(
                state.PublishedCharacterId);
            _trade.Disconnect(
                state.PublishedCharacterId);
        }

        if (changed &&
            state.PublishedMapId > 0 &&
            state.PublishedCharacterId > 0)
        {
            _broadcasts.PublishToMap(
                state.PublishedMapId,
                new
                {
                    Type = "DespawnEntity",
                    EntityID = state.PublishedCharacterId
                },
                state.ConnectionId);
        }

        _broadcasts.UpdatePresence(
            state.ConnectionId,
            nextMapId,
            hasCurrent ? nextCharacterId : null,
            hasCurrent ? state.AccountId : null);

        if (changed &&
            hasCurrent)
        {
            var spawn = BuildPlayerSpawnPacket(
                state.AccountId!.Value,
                nextCharacterId);

            if (spawn is not null)
            {
                _broadcasts.PublishToMap(
                    nextMapId,
                    spawn,
                    state.ConnectionId);
            }
        }

        state.PublishedMapId = nextMapId;
        state.PublishedCharacterId = nextCharacterId;
    }

    public void DisconnectPresence(ClientSession state)
    {
        var characterId =
            state.PublishedCharacterId > 0
                ? state.PublishedCharacterId
                : state.CharacterId ?? 0;

        if (characterId > 0)
        {
            _party.Disconnect(
                characterId);
            _trade.Disconnect(
                characterId);
        }

        if (state.PublishedMapId > 0 &&
            state.PublishedCharacterId > 0)
        {
            _broadcasts.PublishToMap(
                state.PublishedMapId,
                new
                {
                    Type = "DespawnEntity",
                    EntityID = state.PublishedCharacterId
                },
                state.ConnectionId);
        }

        _broadcasts.UpdatePresence(
            state.ConnectionId,
            0,
            null,
            null);

        state.PublishedMapId = 0;
        state.PublishedCharacterId = 0;
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
        {
            return [new
            {
                Type = "AnnouncementListResponse",
                Success = true,
                Message = "",
                Announcements = Array.Empty<object>()
            }];
        }

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
            case "MallAction":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                return _mall.Handle(
                    accountId,
                    state.CharacterId.Value,
                    req.Str("Action"),
                    req.Int("MallID"),
                    req.Int("Count", 1),
                    req.Int("Amount"),
                    req.Int("RewardID"));
            }

            case "TrainTravelReady":
                return [new
                {
                    Type = "SystemMessageFlash",
                    Message = "目前沒有等待中的列車旅行"
                }];

            case "ReviveAccept":
            {
                return _combat.AcceptRevive(
                    state,
                    accountId,
                    req.Int("SkillID"),
                    req.Int("SkillLevel", 1),
                    DateTimeOffset.UtcNow);
            }

            case "AbandonQuest":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var questId = req.Int("QuestID");
                var ok = _store.AbandonQuest(
                    accountId,
                    state.CharacterId.Value,
                    questId);

                return
                [
                    new
                    {
                        Type = "SystemMessageFlash",
                        Message = ok
                            ? "已放棄任務"
                            : "找不到可放棄的任務"
                    },
                    new
                    {
                        Type = "QuestSync",
                        Quests = _store.GetQuests(
                            accountId,
                            state.CharacterId.Value)
                    }
                ];
            }

            case "ClubAction":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var action = req.Str("Action");
                var packets = _club.Handle(
                        accountId,
                        state.CharacterId.Value,
                        action,
                        req.Int("TargetDBID"),
                        req.Str("ClubName"),
                        req.Str("ClubMsg"),
                        req.Int("Amount"),
                        req.Int("ResearchID"))
                    .ToList();

                if (action is "DonateMoney" or "DonateExp")
                {
                    packets.Add(
                        BuildEffectiveStatusPacket(
                            accountId,
                            state.CharacterId.Value,
                            _store.GetCharacterAssets(
                                accountId,
                                state.CharacterId.Value)));
                }

                return packets;
            }

            case "RankDataRequest":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                return _rank.GetRankData(
                    accountId,
                    state.CharacterId.Value,
                    req.Int("RankType", 1));
            }

            case "UpdateRankMessage":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                return _rank.UpdateMessage(
                    accountId,
                    state.CharacterId.Value,
                    req.Int("RankType", 1),
                    req.Str("Message"));
            }

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

                var storedLevel = Math.Clamp(
                    Convert.ToInt32(character["Level"]),
                    1,
                    255);
                var resolvedLevel = _growth.ResolveLevel(
                    storedLevel,
                    Convert.ToInt32(character["ExpAcc"]));
                var gainedPoints = _growth.AbilityPointsGained(
                    storedLevel,
                    resolvedLevel);

                character = _store.SetExperienceProgress(
                                accountId,
                                dbid,
                                resolvedLevel,
                                _growth.ExperienceThresholdForLevel(
                                    resolvedLevel),
                                gainedPoints)
                            ?? character;

                if (resolvedLevel > storedLevel)
                {
                    var growth = _growth.Calculate(character);
                    character = _store.ApplyGrowthStats(
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
                                    growth.MoveSpeed)
                                ?? character;
                }

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
                character["Quests"] = _store.GetQuests(accountId, dbid);
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
                    _club.BuildSync(accountId, dbid),
                    new { Type = "SyncBuffs", Buffs = Array.Empty<object>() },
                    new
                    {
                        Type = "QuestSync",
                        Quests = _store.GetQuests(accountId, dbid)
                    },
                    new { Type = "FestivalThemeSync", Theme = "normal", Festival = "" }
                ];
            }

            case "SyncPosition":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                if (state.AutoSaintEnabled)
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

                _broadcasts.PublishToMap(
                    state.CurrentMapId,
                    new
                    {
                        Type = "MoveEntity",
                        EntityID = state.CharacterId.Value,
                        X = x,
                        Y = y,
                        Dir = req.Str("Dir", "1"),
                        SpeedTime = 0.25
                    },
                    state.ConnectionId);

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

                if (itemIndex == 2803)
                {
                    if (Convert.ToInt32(
                            inventoryItem["InBank"]) != 0 ||
                        Convert.ToInt32(
                            inventoryItem["InShop"]) != 0 ||
                        Convert.ToInt32(
                            inventoryItem["Count"]) <= 0)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = "大聲公目前無法使用"
                            }
                        ];
                    }

                    return
                    [
                        new
                        {
                            Type = "OpenMegaphoneWindow",
                            ItemUID = itemUid
                        }
                    ];
                }

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
                    var assets = _store.GetCharacterAssets(
                        accountId,
                        state.CharacterId.Value);
                    var beforeRecovery = _store.GetCharacter(
                        accountId,
                        state.CharacterId.Value);
                    if (beforeRecovery is null)
                        return [new { Type = "SystemMessageFlash", Message = "無法使用此恢復物品" }];

                    var effectiveStats = CharacterStatCalculator.Calculate(
                        beforeRecovery,
                        assets,
                        _current);

                    var character = _store.UseRecoveryItem(
                        accountId,
                        state.CharacterId.Value,
                        itemUid,
                        recovery.RecoverHp,
                        recovery.RecoverMp,
                        recovery.RecoverSp,
                        effectiveStats.MaxHP,
                        effectiveStats.MaxMP,
                        effectiveStats.MaxSP);

                    if (character is null)
                        return [new { Type = "SystemMessageFlash", Message = "無法使用此恢復物品" }];

                    return
                    [
                        BuildEffectiveStatusPacket(
                            accountId,
                            state.CharacterId.Value,
                            assets),
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
                                equip?.EquipSlot ?? 255,
                                equip?.Durability ?? 0);
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
                    BuildAndBroadcastAppearancePacket(state, accountId, state.CharacterId.Value, assets)
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
                    BuildAndBroadcastAppearancePacket(state, accountId, state.CharacterId.Value, assets)
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
                    response.Add(BuildAndBroadcastAppearancePacket(
                        state,
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
                {
                    response.Add(BuildEffectiveStatusPacket(
                        accountId,
                        state.CharacterId.Value,
                        assets));
                    response.Add(BuildAndBroadcastAppearancePacket(
                        state,
                        accountId,
                        state.CharacterId.Value,
                        assets));
                }

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
                {
                    response.Add(BuildEffectiveStatusPacket(
                        accountId,
                        state.CharacterId.Value,
                        assets));
                    response.Add(BuildAndBroadcastAppearancePacket(
                        state,
                        accountId,
                        state.CharacterId.Value,
                        assets));
                }

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
                        accountId,
                        activePetUid,
                        Math.Max(1, Convert.ToInt32(activePet["Attack"])),
                        targetEntityId,
                        activePetPosition.X,
                        activePetPosition.Y);
                }

                var petUid = req.Int("PetUID");
                var assetsBeforePetAction = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);
                var selectedPetBefore = assetsBeforePetAction.Pets
                    .FirstOrDefault(x =>
                        Convert.ToInt64(x["UID"]) == petUid);
                var previouslyActivePets = assetsBeforePetAction.Pets
                    .Where(x => Convert.ToInt32(x["State"]) == 1)
                    .Select(x => Convert.ToInt64(x["UID"]))
                    .ToArray();

                bool success;
                PetMutationResult? mutation = null;

                switch (action)
                {
                    case "Summon":
                    {
                        var owner = _store.GetCharacter(
                            accountId,
                            state.CharacterId.Value);
                        var nowMs = DateTimeOffset.UtcNow
                            .ToUnixTimeMilliseconds();

                        var canSummon =
                            selectedPetBefore is not null &&
                            owner is not null &&
                            Convert.ToInt32(
                                selectedPetBefore["LifeCount"]) > 0 &&
                            Convert.ToInt32(
                                selectedPetBefore["FeedValue"]) > 0 &&
                            Convert.ToInt64(
                                selectedPetBefore["SummonWaitCount"]) <= nowMs &&
                            Convert.ToInt32(
                                selectedPetBefore["HP"]) > 0 &&
                            Convert.ToInt32(owner["Level"]) >=
                                Convert.ToInt32(
                                    selectedPetBefore["Level"]);

                        success = canSummon &&
                            _store.SetPetState(
                                accountId,
                                state.CharacterId.Value,
                                petUid,
                                1);
                        break;
                    }

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

                    case "Feed":
                    {
                        var itemUid = req.Long("ItemUID");
                        var item = assetsBeforePetAction.Inventory
                            .FirstOrDefault(x =>
                                Convert.ToInt64(x["ItemUID"]) == itemUid &&
                                Convert.ToInt32(x["InBank"]) == 0 &&
                                Convert.ToInt32(x["InShop"]) == 0);

                        if (item is null)
                        {
                            mutation = new(
                                false,
                                "找不到可使用的餵食道具",
                                null);
                            success = false;
                            break;
                        }

                        var itemId = Convert.ToInt32(
                            item["ItemIndex"]);
                        var recovery = _recoveryItems.Find(itemId);
                        var feedAmount = recovery is null
                            ? 0
                            : Math.Max(0, recovery.RecoverHp) +
                              Math.Max(0, recovery.RecoverMp) +
                              Math.Max(0, recovery.RecoverSp);

                        mutation = feedAmount > 0
                            ? _store.FeedPet(
                                accountId,
                                state.CharacterId.Value,
                                petUid,
                                itemUid,
                                itemId,
                                feedAmount)
                            : new(
                                false,
                                "此物品不能用來餵食寵物",
                                null);
                        success = mutation.Success;
                        break;
                    }

                    case "SetMainSkill":
                    {
                        if (selectedPetBefore is null)
                        {
                            mutation = new(
                                false,
                                "找不到寵物",
                                null);
                            success = false;
                            break;
                        }

                        var skillId = req.Int("SkillID");
                        var petId = Convert.ToInt32(
                            selectedPetBefore["PETID"]);
                        var evolId = Convert.ToInt32(
                            selectedPetBefore["EvolID"]);

                        mutation = _petSkills.CanUseAsMainSkill(
                                petId,
                                evolId,
                                skillId)
                            ? _store.SetPetMainSkill(
                                accountId,
                                state.CharacterId.Value,
                                petUid,
                                skillId)
                            : new(
                                false,
                                "此進化型態尚未學會該技能",
                                null);
                        success = mutation.Success;
                        break;
                    }

                    case "AllocateStat":
                    {
                        if (selectedPetBefore is null)
                        {
                            mutation = new(
                                false,
                                "找不到寵物",
                                null);
                            success = false;
                            break;
                        }

                        var statType = req.Str("StatType")
                            .Trim()
                            .ToUpperInvariant();

                        var baseValue = statType switch
                        {
                            "STR" => Convert.ToInt32(
                                selectedPetBefore["STR"]),
                            "DEX" => Convert.ToInt32(
                                selectedPetBefore["DEX"]),
                            "CON" => Convert.ToInt32(
                                selectedPetBefore["CON"]),
                            "WIS" => Convert.ToInt32(
                                selectedPetBefore["WIS"]),
                            _ => -1
                        };

                        var extraValue = statType switch
                        {
                            "STR" => Convert.ToInt32(
                                selectedPetBefore["ExtraSTR"]),
                            "DEX" => Convert.ToInt32(
                                selectedPetBefore["ExtraDEX"]),
                            "CON" => Convert.ToInt32(
                                selectedPetBefore["ExtraCON"]),
                            "WIS" => Convert.ToInt32(
                                selectedPetBefore["ExtraWIS"]),
                            _ => -1
                        };

                        mutation = baseValue >= 0 &&
                                   extraValue >= 0
                            ? _store.AllocatePetStat(
                                accountId,
                                state.CharacterId.Value,
                                petUid,
                                statType,
                                _petGrowth.AllocationDelta(
                                    statType,
                                    baseValue,
                                    extraValue))
                            : new(
                                false,
                                "未知的寵物能力值",
                                null);
                        success = mutation.Success;
                        break;
                    }

                    case "AddLife":
                        mutation = _store.AddPetLife(
                            accountId,
                            state.CharacterId.Value,
                            petUid,
                            itemId: 2615,
                            maxLife: 10);
                        success = mutation.Success;
                        break;

                    case "ResetStat":
                    {
                        if (selectedPetBefore is null)
                        {
                            mutation = new(
                                false,
                                "找不到寵物",
                                null);
                            success = false;
                            break;
                        }

                        var adjustment = _petGrowth.ResetDelta(
                            Convert.ToInt32(selectedPetBefore["STR"]),
                            Convert.ToInt32(selectedPetBefore["ExtraSTR"]),
                            Convert.ToInt32(selectedPetBefore["DEX"]),
                            Convert.ToInt32(selectedPetBefore["ExtraDEX"]),
                            Convert.ToInt32(selectedPetBefore["CON"]),
                            Convert.ToInt32(selectedPetBefore["ExtraCON"]),
                            Convert.ToInt32(selectedPetBefore["WIS"]),
                            Convert.ToInt32(selectedPetBefore["ExtraWIS"]));

                        mutation = _store.ResetPetStats(
                            accountId,
                            state.CharacterId.Value,
                            petUid,
                            itemId: 2617,
                            adjustment);
                        success = mutation.Success;
                        break;
                    }

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
                        Message = mutation?.Message ??
                            (success
                                ? action switch
                                {
                                    "Summon" => "寵物已召喚",
                                    "Recall" => "寵物已收回",
                                    "Rename" => "寵物名稱已更新",
                                    "Equip" => "寵物裝備成功",
                                    "Unequip" => "寵物裝備已卸下",
                                    _ => "寵物操作成功"
                                }
                                : "寵物操作失敗")
                    }
                };

                if (mutation?.InventoryChanged == true)
                {
                    responses.Add(new
                    {
                        Type = "InventorySync",
                        Items = assets.Inventory,
                        MaxSize = assets.InventoryMaxSize
                    });
                }

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

                BroadcastMapEntityPackets(
                    state,
                    responses);

                return responses;
            }

            case "SetTitleRequest":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var titleId = req.Int("TitleID");
                if (titleId is < 0 or > 26 ||
                    !_store.SetCurrentTitle(
                        accountId,
                        state.CharacterId.Value,
                        titleId))
                {
                    return [new
                    {
                        Type = "SystemMessageFlash",
                        Message = titleId == 0
                            ? "稱號設定失敗"
                            : "尚未解鎖此稱號"
                    }];
                }

                var titlePacket = new
                {
                    Type = "UpdateAppearance",
                    EntityID = state.CharacterId.Value,
                    Title = titleId
                };

                _broadcasts.PublishToMap(
                    state.CurrentMapId,
                    titlePacket,
                    state.ConnectionId);

                return [titlePacket];
            }

            case "PartyAction":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                return _party.Handle(
                    state,
                    accountId,
                    req.Str("Action"),
                    req.Long("TargetDBID", -1),
                    DateTimeOffset.UtcNow);
            }

            case "DropItemRequest":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var tabType = req.Int("TabType");
                if (tabType is 2 or 4)
                {
                    var (discarded, discardMessage) =
                        _store.DiscardAsset(
                            accountId,
                            state.CharacterId.Value,
                            req.Long("ItemUID"),
                            tabType,
                            1);

                    var assetsAfterDiscard =
                        _store.GetCharacterAssets(
                            accountId,
                            state.CharacterId.Value);

                    var responses = new List<object>
                    {
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = discardMessage
                        }
                    };

                    if (discarded)
                    {
                        if (tabType == 2)
                        {
                            responses.Add(new
                            {
                                Type = "PetSync",
                                Pets = assetsAfterDiscard.Pets,
                                MaxSize = assetsAfterDiscard.PetMaxSize
                            });
                        }
                        else
                        {
                            responses.Add(new
                            {
                                Type = "HorseSync",
                                Horses = assetsAfterDiscard.Horses,
                                HorseEquips = assetsAfterDiscard.HorseEquips,
                                MaxHorseEquipSize =
                                    assetsAfterDiscard.HorseEquipMaxSize
                            });
                        }
                    }

                    return responses;
                }

                var ground = _store.DropAssetToGround(
                    accountId,
                    state.CharacterId.Value,
                    req.Int("ItemUID"),
                    tabType,
                    Math.Max(1, req.Int("Count", 1)),
                    state.CurrentMapId,
                    req.Int("DropX", (int)state.LastX),
                    req.Int("DropY", (int)state.LastY));

                if (ground is null)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "物品無法丟棄"
                        }
                    ];
                }

                state.GroundItems[ground.EntityId] = ground;
                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);
                var spawnPacket =
                    BuildGroundItemSpawnPacket(ground);

                _broadcasts.PublishToMap(
                    state.CurrentMapId,
                    spawnPacket,
                    state.ConnectionId);

                return
                [
                    spawnPacket,
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
                if (!state.GroundItems.TryGetValue(
                        entityId,
                        out var ground) ||
                    ground.MapId != state.CurrentMapId)
                {
                    ground = _store.GetGroundItems(
                            state.CurrentMapId)
                        .FirstOrDefault(x =>
                            x.EntityId == entityId);

                    if (ground is null)
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

                    state.GroundItems[entityId] = ground;
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

                var (picked, message, _) = _store.PickUpGroundItem(
                    accountId,
                    state.CharacterId.Value,
                    ground.GroundItemId);

                if (!picked)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = message
                        }
                    ];
                }

                state.GroundItems.Remove(entityId);
                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);
                var despawnPacket = new
                {
                    Type = "DespawnEntity",
                    EntityID = entityId
                };

                _broadcasts.PublishToMap(
                    state.CurrentMapId,
                    despawnPacket,
                    state.ConnectionId);

                var pickedName = _current.FindItem(ground.ItemIndex)?.Name
                    ?? $"Item {ground.ItemIndex}";

                return
                [
                    despawnPacket,
                    new
                    {
                        Type = "ItemPickupFeedback",
                        ItemIndex = ground.ItemIndex,
                        Count = ground.Count,
                        Name = pickedName,
                        Message = ground.Count > 1
                            ? $"獲得：{pickedName} x{ground.Count}"
                            : $"獲得：{pickedName}"
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

            case "TradeRequest":
                return _trade.Request(
                    state,
                    accountId,
                    req.Long("TargetEntityID"),
                    DateTimeOffset.UtcNow);

            case "TradeReply":
            {
                var accept =
                    req.TryGetProperty(
                        "Accept",
                        out var accepted) &&
                    accepted.ValueKind == JsonValueKind.True;

                return _trade.Reply(
                    state,
                    accountId,
                    req.Long("RequesterDBID"),
                    accept,
                    DateTimeOffset.UtcNow);
            }

            case "TradeAddItem":
            case "TradeRemoveItem":
            case "TradeSetMoney":
            case "TradeLock":
            case "TradeFinalConfirm":
            case "TradeCancel":
                return _trade.Action(
                    state,
                    type,
                    req);

            case "MegaphoneMessage":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var itemUid = req.Long("ItemUID");
                var message = req.Str("Message").Trim();

                if (message.Length is < 1 or > 50)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "大聲公訊息需為 1～50 個字"
                        }
                    ];
                }

                var assetsBefore = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                var megaphone = assetsBefore.Inventory.FirstOrDefault(x =>
                    Convert.ToInt64(x["ItemUID"]) == itemUid &&
                    Convert.ToInt32(x["ItemIndex"]) == 2803 &&
                    Convert.ToInt32(x["InBank"]) == 0 &&
                    Convert.ToInt32(x["InShop"]) == 0 &&
                    Convert.ToInt32(x["Count"]) > 0);

                if (megaphone is null)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "找不到可使用的大聲公"
                        }
                    ];
                }

                var consumed = _store.DiscardAsset(
                    accountId,
                    state.CharacterId.Value,
                    itemUid,
                    0,
                    1);

                if (!consumed.Success)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "大聲公使用失敗"
                        }
                    ];
                }

                var character = _store.GetCharacter(
                    accountId,
                    state.CharacterId.Value);

                var senderName = character is not null &&
                                 character.TryGetValue(
                                     "NAME",
                                     out var rawName)
                    ? rawName?.ToString() ??
                      $"角色{state.CharacterId.Value}"
                    : $"角色{state.CharacterId.Value}";

                _broadcasts.Publish(new
                {
                    Type = "SystemMessageBroadcast",
                    Message = $"[大聲公] {senderName}：{message}"
                });

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
                        Type = "SystemMessageFlash",
                        Message = "大聲公已發送"
                    }
                ];
            }

            case "CraftItemRequest":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                return _crafting.Craft(
                    accountId,
                    state.CharacterId.Value,
                    req.Int("RecipeID"),
                    req.Int("BatchCount", 1));
            }

            case "DismantleItemRequest":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var itemUids = new List<long>();

                if (req.TryGetProperty(
                        "ItemUIDs",
                        out var rawUids) &&
                    rawUids.ValueKind ==
                        JsonValueKind.Array)
                {
                    foreach (var rawUid in
                             rawUids.EnumerateArray())
                    {
                        if (rawUid.TryGetInt64(
                                out var uid) &&
                            uid > 0)
                        {
                            itemUids.Add(uid);
                        }
                    }
                }

                return _crafting.Dismantle(
                    accountId,
                    state.CharacterId.Value,
                    itemUids,
                    req.Str("ItemType"));
            }

            case "ToggleAutoSaint":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var enabled =
                    req.TryGetProperty(
                        "IsOn",
                        out var isOn) &&
                    isOn.ValueKind == JsonValueKind.True;

                state.AutoSaintEnabled = enabled;
                state.AutoSaintMpThreshold = Math.Clamp(
                    req.Int("MpThreshold", 30),
                    0,
                    100);
                state.AutoSaintSpThreshold = Math.Clamp(
                    req.Int("SpThreshold", 30),
                    0,
                    100);

                if (enabled)
                {
                    state.PetTargets.Clear();

                    foreach (var monster in state.Monsters.Values)
                    {
                        if (monster.TargetCharacterId !=
                            state.CharacterId.Value)
                            continue;

                        monster.HasTarget = false;
                        monster.TargetCharacterId = 0;
                    }
                }

                return
                [
                    new
                    {
                        Type = "ToggleAutoSaintResponse",
                        IsOn = state.AutoSaintEnabled,
                        MpThreshold =
                            state.AutoSaintMpThreshold,
                        SpThreshold =
                            state.AutoSaintSpThreshold
                    }
                ];
            }

            case "RepairItemRequest":
            {
                if (!state.CharacterId.HasValue)
                    return [new
                    {
                        Type = "SystemMessageFlash",
                        Message = "尚未選擇角色"
                    }];

                var uid = req.Long("UID");
                var itemType = req.Str("ItemType");
                var isMechaEquipment = string.Equals(
                    itemType,
                    "HorseEquip",
                    StringComparison.OrdinalIgnoreCase);

                if (!isMechaEquipment &&
                    !string.Equals(
                        itemType,
                        "Equip",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return [new
                    {
                        Type = "SystemMessageFlash",
                        Message = "不支援的維修類型"
                    }];
                }

                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                Dictionary<string, object>? asset = isMechaEquipment
                    ? assets.HorseEquips.FirstOrDefault(x =>
                        Convert.ToInt64(x["EquipUID"]) == uid &&
                        Convert.ToInt32(x["InBank"]) == 0 &&
                        Convert.ToInt32(x["InShop"]) == 0)
                    : assets.Equips.FirstOrDefault(x =>
                        Convert.ToInt64(x["ItemID"]) == uid &&
                        Convert.ToInt32(x["InBank"]) == 0 &&
                        Convert.ToInt32(x["InShop"]) == 0);

                if (asset is null)
                {
                    return [new
                    {
                        Type = "SystemMessageFlash",
                        Message = "找不到可維修的裝備"
                    }];
                }

                var itemIndex = Convert.ToInt32(
                    asset[isMechaEquipment
                        ? "EquipIdx"
                        : "ItemIdx"]);
                var definition = _current.FindItem(itemIndex);

                if (definition is null ||
                    definition.Durability <= 0 ||
                    definition.CanRepair != 1)
                {
                    return [new
                    {
                        Type = "SystemMessageFlash",
                        Message = "此裝備無法維修"
                    }];
                }

                var currentDurability = Math.Max(
                    0,
                    Convert.ToInt32(asset["Damage"]));
                var maxDurability = definition.Durability;

                if (currentDurability >= maxDurability)
                {
                    return [new
                    {
                        Type = "SystemMessageFlash",
                        Message = "此裝備和新的一樣，不需要修理！"
                    }];
                }

                var usedDurability = maxDurability - currentDurability;
                var repairCost = Math.Max(
                    1L,
                    (long)Math.Floor(
                        (double)usedDurability /
                        maxDurability *
                        definition.Price));

                var repair = _store.RepairAsset(
                    accountId,
                    state.CharacterId.Value,
                    uid,
                    isMechaEquipment,
                    maxDurability,
                    repairCost);

                if (!repair.Success)
                {
                    return [new
                    {
                        Type = "SystemMessageFlash",
                        Message = repair.Message
                    }];
                }

                assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                var packets = new List<object>
                {
                    new
                    {
                        Type = "SystemMessageFlash",
                        Message =
                            $"維修完成：{definition.Name}，花費 {repairCost}"
                    },
                    new
                    {
                        Type = "SyncMoney",
                        Money = repair.Money
                    }
                };

                if (isMechaEquipment)
                {
                    packets.Add(new
                    {
                        Type = "HorseSync",
                        Horses = assets.Horses,
                        HorseEquips = assets.HorseEquips,
                        MaxHorseEquipSize = assets.HorseEquipMaxSize
                    });
                }
                else
                {
                    packets.Add(new
                    {
                        Type = "EquipSync",
                        Equips = assets.Equips,
                        MaxSize = assets.EquipMaxSize
                    });
                }

                return packets;
            }

            case "DirectTransfer":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var targetDbid = req.Long("TargetDBID");
                var amount = req.Long("Amount");

                if (targetDbid <= 0 ||
                    amount <= 0)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "轉帳資料無效"
                        }
                    ];
                }

                if (!_broadcasts.IsCharacterInMap(
                        targetDbid,
                        state.CurrentMapId))
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "目標玩家不在附近或已離線"
                        }
                    ];
                }

                var transfer = _store.TransferMoney(
                    accountId,
                    state.CharacterId.Value,
                    targetDbid,
                    amount);

                if (!transfer.Success)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = transfer.Message
                        }
                    ];
                }

                var sender = _store.GetCharacter(
                    accountId,
                    state.CharacterId.Value);
                var target = _store.GetCharacter(
                    transfer.TargetAccountId,
                    targetDbid);

                _broadcasts.PublishToCharacter(
                    targetDbid,
                    new
                    {
                        Type = "SyncMoney",
                        Money = transfer.TargetMoney
                    });
                _broadcasts.PublishToCharacter(
                    targetDbid,
                    new
                    {
                        Type = "SystemMessageFlash",
                        Message =
                            $"{sender?.GetValueOrDefault("NAME") ?? "玩家"} 轉給你 {amount} 金錢"
                    });

                return
                [
                    new
                    {
                        Type = "SyncMoney",
                        Money = transfer.SenderMoney
                    },
                    new
                    {
                        Type = "SystemMessageFlash",
                        Message =
                            $"已轉給 {target?.GetValueOrDefault("NAME") ?? "玩家"} {amount} 金錢"
                    }
                ];
            }

            case "ShopAction":
            {
                if (!state.CharacterId.HasValue)
                    return [];

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

                return _playerShop.Handle(
                    state,
                    accountId,
                    action,
                    req.Long("UID"),
                    req.Int("TabType", -1),
                    Math.Max(1, req.Int("Count", 1)),
                    Math.Max(1L, req.Long("Price", 1)),
                    req.Long("TargetDBID", -1),
                    req.Str("ShopName", "我的商店"));
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
                if (state.AutoSaintEnabled)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "自動聖靈啟動中，禁止手動攻擊"
                        }
                    ];
                }

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
                if (state.AutoSaintEnabled)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "自動聖靈啟動中，禁止手動施法"
                        }
                    ];
                }

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
                    var playerState =
                        req.Str("State", "idle");

                    _store.UpdateState(
                        accountId,
                        state.CharacterId.Value,
                        playerState);

                    _broadcasts.PublishToMap(
                        state.CurrentMapId,
                        new
                        {
                            Type = "SyncState",
                            EntityID = state.CharacterId.Value,
                            State = playerState,
                            Dir = req.Str("Dir", "1")
                        },
                        state.ConnectionId);
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

                var chatPacket = new
                {
                    Type = "ChatBroadcast",
                    EntityID = state.CharacterId.Value,
                    SenderName = character["NAME"],
                    Race = character["Race"],
                    Channel = req.Int("Channel"),
                    Message = message,
                    BubbleType = req.Int("BubbleType")
                };

                _broadcasts.PublishToMap(
                    state.CurrentMapId,
                    chatPacket,
                    state.ConnectionId);

                return [chatPacket];
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

                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                return
                [
                    BuildEffectiveStatusPacket(
                        accountId,
                        state.CharacterId.Value,
                        assets),
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
                state.GroundItems.Clear();
                state.AutoSaintEnabled = false;
                state.AutoSaintMpThreshold = 30;
                state.AutoSaintSpThreshold = 30;
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

        // Server-initiated travel (for example a party-return skill) updates
        // persistence first and sends ChangeMap directly to the target socket.
        // Reconcile this connection's runtime state from the authoritative DB
        // on its next world tick.
        var persistedCharacter = _store.GetCharacter(
            accountId,
            dbid);

        if (persistedCharacter is not null)
        {
            var persistedMapId = Convert.ToInt32(
                persistedCharacter["MapID"]);

            if (persistedMapId > 0 &&
                persistedMapId != state.CurrentMapId)
            {
                state.CurrentMapId = persistedMapId;
                state.LastX = Convert.ToDouble(
                    persistedCharacter["LocX"]);
                state.LastY = Convert.ToDouble(
                    persistedCharacter["LocY"]);
                state.PendingMapSpawn = true;
                state.LoadedMonsterMapId = 0;
                state.WorldSnapshotRetriesRemaining = 0;
                state.PetTargets.Clear();
                state.PetOutOfRangeSince.Clear();
            }
        }

        packets.AddRange(
            _resourceRecovery.Tick(state, now));
        packets.AddRange(
            _combat.TickBuffs(state, now));
        packets.AddRange(
            _combat.TickRespawns(state, now));

        if (now - state.LastPartyStatAt >=
            TimeSpan.FromSeconds(1))
        {
            state.LastPartyStatAt = now;
            _party.PublishStat(
                accountId,
                dbid);
        }

        if (now - state.LastGroundItemCleanupAt >=
            TimeSpan.FromSeconds(1))
        {
            state.LastGroundItemCleanupAt = now;

            var expiredEntityIds = state.GroundItems.Values
                .Where(x => x.IsExpired(now))
                .Select(x => x.EntityId)
                .ToHashSet();

            expiredEntityIds.UnionWith(
                _store.RemoveExpiredGroundItems(
                    state.CurrentMapId,
                    now));

            foreach (var entityId in expiredEntityIds)
            {
                state.GroundItems.Remove(entityId);
                var despawnPacket = new
                {
                    Type = "DespawnEntity",
                    EntityID = entityId
                };
                packets.Add(despawnPacket);
                _broadcasts.PublishToMap(
                    state.CurrentMapId,
                    despawnPacket,
                    state.ConnectionId);
            }
        }

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
                BuildGroundItemSnapshotPackets(state));
            packets.AddRange(
                BuildOtherPlayerSnapshotPackets(state));
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
                    BuildGroundItemSnapshotPackets(state));
                packets.AddRange(
                    BuildOtherPlayerSnapshotPackets(state));
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

    private IReadOnlyList<object> BuildOtherPlayerSnapshotPackets(
        ClientSession state)
    {
        var packets = new List<object>();

        foreach (var player in _broadcasts.PlayersInMap(
                     state.CurrentMapId,
                     state.ConnectionId))
        {
            var spawn = BuildPlayerSpawnPacket(
                player.AccountId,
                player.CharacterId);

            if (spawn is not null)
                packets.Add(spawn);
        }

        return packets;
    }

    private object? BuildPlayerSpawnPacket(
        long accountId,
        long dbid)
    {
        var character = _store.GetCharacter(
            accountId,
            dbid);

        if (character is null)
            return null;

        var assets = _store.GetCharacterAssets(
            accountId,
            dbid);

        ApplyAssetsToCharacterBootstrap(
            character,
            assets);
        ApplyEffectiveStats(
            character,
            assets);

        var shopState =
            _playerShop.GetPublicState(dbid);

        var extra = new Dictionary<string, object>(
            character)
        {
            ["State"] =
                character.GetValueOrDefault(
                    "State",
                    "idle"),
            ["Dir"] = "1",
            ["PartyID"] = _party.GetPartyId(dbid),
            ["IsShopOpen"] = shopState.IsOpen,
            ["ShopName"] = shopState.ShopName
        };

        return new
        {
            Type = "SpawnEntity",
            EntityID = dbid,
            EntityType = 0,
            Name = character["NAME"],
            LocX = character["LocX"],
            LocY = character["LocY"],
            ExtraData = extra
        };
    }

    private IReadOnlyList<object> BuildGroundItemSnapshotPackets(
        ClientSession state)
    {
        var packets = new List<object>();
        state.GroundItems.Clear();

        foreach (var ground in _store.GetGroundItems(state.CurrentMapId))
        {
            state.GroundItems[ground.EntityId] = ground;
            packets.Add(BuildGroundItemSpawnPacket(ground));
        }

        return packets;
    }

    private object BuildGroundItemSpawnPacket(
        GroundItemRuntime ground)
    {
        var now = DateTimeOffset.UtcNow;
        return new
        {
            Type = "SpawnEntity",
            EntityID = ground.EntityId,
            EntityType = 3,
            Name = _current.FindItem(ground.ItemIndex)?.Name
                ?? $"Item {ground.ItemIndex}",
            LocX = ground.X,
            LocY = ground.Y,
            ExtraData = new
            {
                ItemIndex = ground.ItemIndex,
                Count = ground.Count,
                TabType = ground.TabType,
                Damage = ground.Damage,
                Color = ground.Color,
                Part = ground.Part,
                RemainingTime = ground.RemainingSeconds(now),
                OwnerDBID = ground.OwnerDbid,
                ProtectTimeSeconds = ground.ProtectSeconds(now)
            }
        };
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
            BuildGroundItemSnapshotPackets(state));

        packets.AddRange(
            BuildOtherPlayerSnapshotPackets(state));

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
            var assetsChanged = false;
            var skillsChanged = false;
            var careerChanged = false;
            var dbid = state.CharacterId ?? 0;
            var questTeamMembers = dbid > 0
                ? _party.GetMembers(dbid)
                : Array.Empty<(long AccountId, long CharacterId)>();

            long ResolveQuestTargetAccount(long targetDbid)
            {
                if (targetDbid == dbid)
                    return accountId;

                var member = questTeamMembers.FirstOrDefault(x =>
                    x.CharacterId == targetDbid);

                return member.AccountId;
            }

            var result = _quests.Execute(
                npc.Script,
                function,
                npc.EntityId,
                npc.Name,
                choice,
                name => GetCharacterData(character, name),
                new QuestRuntimeBindings
                {
                    GetFlag = name =>
                        dbid > 0
                            ? _store.GetQuestFlag(
                                accountId,
                                dbid,
                                name)
                            : 0,
                    SetFlag = (name, value) =>
                    {
                        if (dbid <= 0 ||
                            !_store.SetQuestFlag(
                                accountId,
                                dbid,
                                name,
                                value))
                        {
                            throw new InvalidOperationException(
                                $"Unable to persist quest flag {name}");
                        }
                    },
                    GetItemCount = (itemId, tabType) =>
                        dbid > 0
                            ? GetQuestItemCount(
                                accountId,
                                dbid,
                                itemId,
                                tabType)
                            : 0,
                    ItemListCountLeft = tabType =>
                        dbid > 0
                            ? GetQuestItemListCountLeft(
                                accountId,
                                dbid,
                                tabType)
                            : 0,
                    AddItem = (itemId, tabType, count) =>
                    {
                        if (dbid <= 0)
                            return false;

                        var success = AddQuestItem(
                            accountId,
                            dbid,
                            itemId,
                            tabType,
                            count);
                        assetsChanged |= success;
                        return success;
                    },
                    RemoveItem = (itemId, tabType, count) =>
                    {
                        if (dbid <= 0)
                            return false;

                        var success = RemoveQuestItem(
                            accountId,
                            dbid,
                            itemId,
                            tabType,
                            count);
                        assetsChanged |= success;
                        return success;
                    },
                    CallerDbid = dbid,
                    GetTeamMemberIds = () =>
                        questTeamMembers
                            .Where(x =>
                                x.CharacterId != dbid)
                            .Select(x => x.CharacterId)
                            .ToArray(),
                    SetTargetFlag = (targetDbid, flag, value) =>
                    {
                        var targetAccountId =
                            ResolveQuestTargetAccount(targetDbid);

                        if (targetAccountId <= 0 ||
                            !_store.SetQuestFlag(
                                targetAccountId,
                                targetDbid,
                                flag,
                                value))
                        {
                            throw new InvalidOperationException(
                                $"Unable to persist team quest flag {flag} for {targetDbid}");
                        }
                    },
                    GetTargetData = (targetDbid, key) =>
                    {
                        var targetAccountId =
                            ResolveQuestTargetAccount(targetDbid);

                        if (targetAccountId <= 0)
                            return 0;

                        var targetCharacter =
                            _store.GetCharacter(
                                targetAccountId,
                                targetDbid);

                        return targetCharacter is null
                            ? 0
                            : GetCharacterData(
                                targetCharacter,
                                key);
                    },
                    GetTargetXY = targetDbid =>
                    {
                        var targetAccountId =
                            ResolveQuestTargetAccount(targetDbid);

                        if (targetAccountId <= 0)
                            return (0.0, 0.0);

                        var targetCharacter =
                            _store.GetCharacter(
                                targetAccountId,
                                targetDbid);

                        return targetCharacter is null
                            ? (0.0, 0.0)
                            : (
                                Convert.ToDouble(
                                    targetCharacter["LocX"]),
                                Convert.ToDouble(
                                    targetCharacter["LocY"]));
                    },
                    GetTargetEmotion = targetDbid =>
                    {
                        // The recovered client does not currently persist an
                        // emote state. Keep the old API deterministic until a
                        // real client emote packet is available.
                        return ResolveQuestTargetAccount(targetDbid) > 0
                            ? 0
                            : -1;
                    },
                    HasTitle = titleId =>
                        dbid > 0 &&
                        _store.HasTitle(
                            accountId,
                            dbid,
                            titleId),
                    UnlockTitle = titleId =>
                        dbid > 0 &&
                        _store.UnlockTitle(
                            accountId,
                            dbid,
                            titleId),
                    HasSkill = currentSkillId =>
                        dbid > 0 &&
                        _store.GetSkills(
                                accountId,
                                dbid)
                            .Any(x =>
                                Convert.ToInt32(x["SkillID"]) ==
                                currentSkillId),
                    AddSkill = currentSkillId =>
                    {
                        if (dbid <= 0)
                            return false;

                        var success = _store.SetSkill(
                            accountId,
                            dbid,
                            currentSkillId,
                            1);
                        skillsChanged |= success;
                        return success;
                    },
                    SetCareer = (career, job) =>
                    {
                        if (dbid <= 0)
                            return false;

                        var updated = _store.SetCareer(
                            accountId,
                            dbid,
                            career,
                            job);
                        careerChanged |= updated is not null;
                        return updated is not null;
                    }
                });

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

            if (assetsChanged &&
                state.CharacterId.HasValue)
            {
                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                result.Packets.Add(new
                {
                    Type = "InventorySync",
                    Items = assets.Inventory,
                    MaxSize = assets.InventoryMaxSize
                });
                result.Packets.Add(new
                {
                    Type = "EquipSync",
                    Equips = assets.Equips,
                    MaxSize = assets.EquipMaxSize
                });
                result.Packets.Add(
                    BuildEffectiveStatusPacket(
                        accountId,
                        state.CharacterId.Value,
                        assets));
                result.Packets.Add(
                    BuildAndBroadcastAppearancePacket(
                        state,
                        accountId,
                        state.CharacterId.Value,
                        assets));
            }

            if (skillsChanged &&
                state.CharacterId.HasValue)
            {
                result.Packets.Add(new
                {
                    Type = "SkillSync",
                    Skills = _store.GetSkills(
                        accountId,
                        state.CharacterId.Value)
                });
            }

            if (careerChanged &&
                state.CharacterId.HasValue)
            {
                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                result.Packets.Add(
                    BuildEffectiveStatusPacket(
                        accountId,
                        state.CharacterId.Value,
                        assets));

                result.Packets.Add(
                    BuildAndBroadcastAppearancePacket(
                        state,
                        accountId,
                        state.CharacterId.Value,
                        assets));
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

    private int GetQuestItemCount(
        long accountId,
        long dbid,
        int itemId,
        int tabType)
    {
        var assets = _store.GetCharacterAssets(
            accountId,
            dbid);

        if (tabType == 0)
        {
            return assets.Inventory
                .Where(x =>
                    Convert.ToInt32(x["ItemIndex"]) == itemId &&
                    Convert.ToInt32(x["InBank"]) == 0 &&
                    Convert.ToInt32(x["InShop"]) == 0)
                .Sum(x => Math.Max(
                    0,
                    Convert.ToInt32(x["Count"])));
        }

        if (tabType == 1)
        {
            return assets.Equips.Count(x =>
                Convert.ToInt32(x["ItemIdx"]) == itemId &&
                Convert.ToInt32(x["InBank"]) == 0 &&
                Convert.ToInt32(x["InShop"]) == 0);
        }

        return 0;
    }

    private int GetQuestItemListCountLeft(
        long accountId,
        long dbid,
        int tabType)
    {
        var assets = _store.GetCharacterAssets(
            accountId,
            dbid);

        return tabType switch
        {
            0 => Math.Max(
                0,
                assets.InventoryMaxSize -
                assets.Inventory.Count(x =>
                    Convert.ToInt32(x["InBank"]) == 0 &&
                    Convert.ToInt32(x["InShop"]) == 0)),
            1 => Math.Max(
                0,
                assets.EquipMaxSize -
                assets.Equips.Count(x =>
                    Convert.ToInt32(x["InBank"]) == 0 &&
                    Convert.ToInt32(x["InShop"]) == 0)),
            _ => 0
        };
    }

    private bool AddQuestItem(
        long accountId,
        long dbid,
        int itemId,
        int tabType,
        int count)
    {
        count = Math.Max(1, count);

        if (GetQuestItemListCountLeft(
                accountId,
                dbid,
                tabType) < (tabType == 0 ? 1 : count))
            return false;

        if (tabType == 0)
        {
            return _store.AddInventoryItem(
                       accountId,
                       dbid,
                       itemId,
                       count) > 0;
        }

        if (tabType != 1)
            return false;

        for (var i = 0; i < count; i++)
        {
            if (_store.AddEquipment(
                    accountId,
                    dbid,
                    itemId,
                    255,
                    _current.FindItem(itemId)?.Durability ?? 0) <= 0)
                return false;
        }

        return true;
    }

    private bool RemoveQuestItem(
        long accountId,
        long dbid,
        int itemId,
        int tabType,
        int count)
    {
        count = Math.Max(1, count);
        var assets = _store.GetCharacterAssets(
            accountId,
            dbid);

        if (tabType == 0)
        {
            var stacks = assets.Inventory
                .Where(x =>
                    Convert.ToInt32(x["ItemIndex"]) == itemId &&
                    Convert.ToInt32(x["InBank"]) == 0 &&
                    Convert.ToInt32(x["InShop"]) == 0)
                .ToArray();

            if (stacks.Sum(x =>
                    Math.Max(
                        0,
                        Convert.ToInt32(x["Count"]))) < count)
                return false;

            var remaining = count;
            foreach (var stack in stacks)
            {
                if (remaining <= 0)
                    break;

                var stackCount = Math.Max(
                    0,
                    Convert.ToInt32(stack["Count"]));
                var remove = Math.Min(
                    remaining,
                    stackCount);

                var result = _store.DiscardAsset(
                    accountId,
                    dbid,
                    Convert.ToInt64(stack["ItemUID"]),
                    0,
                    remove);

                if (!result.Success)
                    return false;

                remaining -= remove;
            }

            return remaining == 0;
        }

        if (tabType != 1)
            return false;

        var equips = assets.Equips
            .Where(x =>
                Convert.ToInt32(x["ItemIdx"]) == itemId &&
                Convert.ToInt32(x["Part"]) == 255 &&
                Convert.ToInt32(x["InBank"]) == 0 &&
                Convert.ToInt32(x["InShop"]) == 0)
            .Take(count)
            .ToArray();

        if (equips.Length < count)
            return false;

        foreach (var equip in equips)
        {
            var result = _store.DiscardAsset(
                accountId,
                dbid,
                Convert.ToInt64(equip["ItemID"]),
                1,
                1);

            if (!result.Success)
                return false;
        }

        return true;
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
                        accountId,
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

        BroadcastMapEntityPackets(
            state,
            packets);

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

    private void BroadcastMapEntityPackets(
        ClientSession state,
        IEnumerable<object> packets)
    {
        if (state.CurrentMapId <= 0)
            return;

        foreach (var packet in packets)
        {
            var type = packet.GetType()
                .GetProperty("Type")
                ?.GetValue(packet)
                ?.ToString();

            if (type is not (
                    "SpawnEntity" or
                    "DespawnEntity" or
                    "MoveEntity"))
                continue;

            _broadcasts.PublishToMap(
                state.CurrentMapId,
                packet,
                state.ConnectionId);
        }
    }

    private object BuildAndBroadcastAppearancePacket(
        ClientSession state,
        long accountId,
        long dbid,
        SGO.Server.Domain.Assets.CharacterAssets assets)
    {
        var packet = BuildAppearancePacket(
            accountId,
            dbid,
            assets);

        _broadcasts.PublishToMap(
            state.CurrentMapId,
            packet,
            state.ConnectionId);

        return packet;
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
