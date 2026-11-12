using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SGO.Server.Application.Announcements;
using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Assets;
using SGO.Server.Application.Combat;
using SGO.Server.Application.Crafting;
using SGO.Server.Application.Diagnostics;
using SGO.Server.Application.Club;
using SGO.Server.Application.Character;
using SGO.Server.Application.Gm;
using SGO.Server.Application.Items;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.Mall;
using SGO.Server.Application.Packets;
using SGO.Server.Application.Party;
using SGO.Server.Application.Rank;
using SGO.Server.Application.Quest;
using SGO.Server.Application.Shops;
using SGO.Server.Application.Trade;
using SGO.Server.Application.World;
using SGO.Server.Domain.Assets;
using SGO.Server.Domain.Sessions;
using SGO.Server.Domain.World;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Quest;
using SGO.Server.Infrastructure.WorldData;
using SGO.Server.Protocol;

namespace SGO.Server.Application;

public sealed class PacketRouter
{
    private const int NpcInteractRange = 700;
    private static readonly TimeSpan PositionPersistInterval =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PositionBroadcastInterval =
        TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LocationReconcileInterval =
        TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan AutoSaintActionInterval =
        TimeSpan.FromMilliseconds(750);

    private sealed record TrainTravelSpec(
        string Continuation,
        string SourceVideo,
        string DestinationVideo);

    private readonly IGameStore _store;
    private readonly PositionPersistenceService? _positionWrites;
    private readonly WorldDataService _world;
    private readonly BlockingMapService _blocking;
    private readonly GroundDropPlacementService _dropPlacement;
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
    private readonly PetIdleSpeechCatalog _petIdleSpeech;
    private readonly PetEvolutionService _petEvolution;
    private readonly MallService _mall;
    private readonly ServerBroadcastHub _broadcasts;
    private readonly AccountSessionRegistry _sessions;
    private readonly PartyService _party;
    private readonly RankService _rank;
    private readonly QuestProgressService _questProgress;
    private readonly TradeService _trade;
    private readonly ServerDiagnostics _diagnostics;
    private readonly ILogger<PacketRouter> _log;
    private readonly IConfiguration? _configuration;

    public PacketRouter(
        IGameStore store,
        WorldDataService world,
        BlockingMapService blocking,
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
        PetIdleSpeechCatalog petIdleSpeech,
        PetEvolutionService petEvolution,
        MallService mall,
        ServerBroadcastHub broadcasts,
        AccountSessionRegistry sessions,
        PartyService party,
        RankService rank,
        QuestProgressService questProgress,
        TradeService trade,
        ServerDiagnostics diagnostics,
        ILogger<PacketRouter> log,
        IConfiguration? configuration = null,
        PositionPersistenceService? positionWrites = null)
    {
        _store = store;
        _positionWrites = positionWrites;
        _world = world;
        _blocking = blocking;
        _dropPlacement = new GroundDropPlacementService(blocking);
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
        _petIdleSpeech = petIdleSpeech;
        _petEvolution = petEvolution;
        _mall = mall;
        _broadcasts = broadcasts;
        _sessions = sessions;
        _party = party;
        _rank = rank;
        _questProgress = questProgress;
        _trade = trade;
        _diagnostics = diagnostics;
        _log = log;
        _configuration = configuration;
    }

    private void PersistPendingPosition(ClientSession state)
    {
        if (!state.AccountId.HasValue ||
            !state.CharacterId.HasValue)
            return;

        // Logout/disconnect must remain durable. Waiting for the shared
        // position writer here fences earlier periodic snapshots so none
        // can overwrite this final authoritative session position.
        if (_positionWrites is not null)
        {
            _positionWrites.FlushFinal(
                state.AccountId.Value,
                state.CharacterId.Value,
                (int)Math.Round(state.LastX),
                (int)Math.Round(state.LastY),
                state.PositionDirty);
            state.PositionDirty = false;
            state.LastPositionPersistAt = DateTimeOffset.UtcNow;
            return;
        }

        // Keep the existing synchronous path for standalone SmokeTests
        // that construct a PacketRouter without hosted runtime services.
        if (!state.PositionDirty)
            return;
        _store.UpdatePosition(
            state.AccountId.Value,
            state.CharacterId.Value,
            (int)Math.Round(state.LastX),
            (int)Math.Round(state.LastY));
        state.PositionDirty = false;
        state.LastPositionPersistAt = DateTimeOffset.UtcNow;
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

        // A selected character must not appear for every player across
        // the whole map. Only WorldReady clients join the spatial AOI.
        // Remove old visibility before a map/identity transition.
        // Session / map changes invalidate visibility immediately. The
        // previous code re-ran both AOI spatial-index reconciliations on
        // EVERY packet and every 250ms world tick, even at identical X/Y.
        // Only identity, world readiness and approved position changes can
        // affect observer visibility. Moving monsters/items update their
        // own world AOI indexes through the existing entity-publish path.
        var lostWorldReady = !state.WorldReady &&
                             state.PresenceAoiInitialized;
        if (changed || lostWorldReady)
        {
            PublishPlayerAoiChanges(
                _broadcasts.RemovePlayerAoi(state.ConnectionId));
            _broadcasts.RemoveWorldAoiObserver(
                state.ConnectionId);
            _broadcasts.ClearPlayerCompanions(
                state.ConnectionId);
            state.PresenceAoiInitialized = false;
        }

        _broadcasts.UpdatePresence(
            state.ConnectionId,
            nextMapId,
            hasCurrent ? nextCharacterId : null,
            hasCurrent ? state.AccountId : null,
            hasCurrent ? state.LastX : 0,
            hasCurrent ? state.LastY : 0,
            hasCurrent ? state.CharacterName : "");

        var needsSpatialReconcile =
            changed ||
            !state.PresenceAoiInitialized ||
            state.LastPresenceAoiX != state.LastX ||
            state.LastPresenceAoiY != state.LastY;
        if (hasCurrent && state.WorldReady && needsSpatialReconcile)
        {
            PublishPlayerAoiChanges(
                _broadcasts.ReconcilePlayerAoi(state.ConnectionId));
            PublishWorldAoiChanges(
                _broadcasts.ReconcileWorldAoi(state.ConnectionId));
            state.PresenceAoiInitialized = true;
            state.LastPresenceAoiX = state.LastX;
            state.LastPresenceAoiY = state.LastY;
        }

        state.PublishedMapId = nextMapId;
        state.PublishedCharacterId = nextCharacterId;
    }

    public void DisconnectPresence(ClientSession state)
    {
        PersistPendingPosition(state);
        _sessions.Unregister(state.ConnectionId);

        var characterId =
            state.PublishedCharacterId > 0
                ? state.PublishedCharacterId
                : state.CharacterId ?? 0;

        if (characterId > 0)
        {
            _playerShop.Disconnect(
                state.ConnectionId,
                state.PublishedMapId > 0
                    ? state.PublishedMapId
                    : state.CurrentMapId,
                state.AccountId ?? 0,
                characterId);
            _party.Disconnect(
                characterId);
            _trade.Disconnect(
                characterId);
        }

        // Only clients that actually saw this player need the despawn.
        PublishPlayerAoiChanges(
            _broadcasts.RemovePlayerAoi(state.ConnectionId));
        _broadcasts.RemoveWorldAoiObserver(
            state.ConnectionId);
        _broadcasts.ClearPlayerCompanions(
            state.ConnectionId);

        _broadcasts.UpdatePresence(
            state.ConnectionId,
            0,
            null,
            null);

        state.PublishedMapId = 0;
        state.PublishedCharacterId = 0;
    }

    private void PublishPlayerAoiChanges(
        IReadOnlyList<PlayerSpatialAoi.Change> changes,
        Guid skipObserver = default)
    {
        if (changes.Count == 0)
            return;

        var spawnCache = new Dictionary<Guid, object?>();
        foreach (var change in changes)
        {
            if (change.ObserverConnectionId == skipObserver)
                continue;

            // Concurrent movement may supersede a queued edge change.
            if (_broadcasts.IsPlayerAoiVisibleAs(
                    change.ObserverConnectionId,
                    change.Subject.ConnectionId,
                    change.Subject.CharacterId) != change.Appeared)
                continue;

            if (!change.Appeared)
            {
                // Remove companion entities before their owner so a client
                // never retains an orphan pet after the player leaves AOI.
                foreach (var companionId in
                         _broadcasts.PlayerCompanionEntityIds(
                             change.Subject.ConnectionId))
                {
                    _broadcasts.PublishToConnection(
                        change.ObserverConnectionId,
                        new
                        {
                            Type = "DespawnEntity",
                            EntityID = companionId
                        });
                }

                _broadcasts.PublishToConnection(
                    change.ObserverConnectionId,
                    new
                    {
                        Type = "DespawnEntity",
                        EntityID = change.Subject.CharacterId
                    });
                continue;
            }

            if (!spawnCache.TryGetValue(
                    change.Subject.ConnectionId, out var packet))
            {
                packet = BuildPlayerSpawnPacket(
                    change.Subject.AccountId,
                    change.Subject.CharacterId,
                    change.Subject.X,
                    change.Subject.Y);
                spawnCache[change.Subject.ConnectionId] = packet;
            }

            if (packet is null)
                continue;

            // Owner must exist before its separate pet entities.
            _broadcasts.PublishToConnection(
                change.ObserverConnectionId,
                packet);

            foreach (var companion in
                     _broadcasts.PlayerCompanionSnapshots(
                         change.Subject.ConnectionId))
            {
                _broadcasts.PublishToConnection(
                    change.ObserverConnectionId,
                    companion);
            }
        }
    }

    private void PublishWorldAoiChanges(
        IReadOnlyList<WorldEntitySpatialAoi.Change> changes,
        Guid skipObserver = default)
    {
        foreach (var change in changes)
        {
            if (change.ObserverConnectionId == skipObserver)
                continue;

            // A concurrent player/entity movement may supersede the copied
            // change before it reaches the queue.
            if (_broadcasts.IsWorldAoiVisible(
                    change.ObserverConnectionId,
                    change.Entity.EntityId) != change.Appeared)
                continue;

            _broadcasts.PublishToConnection(
                change.ObserverConnectionId,
                change.Appeared
                    ? change.Entity.SpawnPacketFactory()
                    : new
                    {
                        Type = "DespawnEntity",
                        EntityID = change.Entity.EntityId
                    });
        }
    }

    private IReadOnlyList<object> BuildWorldAoiSnapshotPackets(
        ClientSession state)
    {
        if (!state.WorldReady ||
            state.CurrentMapId <= 0)
            return [];

        var changes =
            _broadcasts.ReconcileWorldAoi(
                state.ConnectionId);

        var packets = new List<object>();
        foreach (var change in changes)
        {
            if (change.ObserverConnectionId !=
                state.ConnectionId)
                continue;

            if (!_broadcasts.IsWorldAoiVisible(
                    state.ConnectionId,
                    change.Entity.EntityId))
                continue;

            if (change.Appeared)
                packets.Add(
                    change.Entity.SpawnPacketFactory());
        }

        return packets;
    }

    public IReadOnlyList<object> Handle(JsonElement req, ClientSession state)
    {
        if (req.ValueKind != JsonValueKind.Object)
            return [new
            {
                Type = "PacketError",
                RequestType = "",
                Code = "INVALID_PACKET",
                Message = "封包必須是 JSON 物件"
            }];

        if (!req.TryGetProperty("Type", out var incomingTypeElement) ||
            incomingTypeElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(incomingTypeElement.GetString()) ||
            incomingTypeElement.GetString()!.Length > 128)
        {
            return [new
            {
                Type = "PacketError",
                RequestType = "",
                Code = "INVALID_PACKET",
                Message = "封包 Type 必須是非空字串"
            }];
        }

        var type = incomingTypeElement.GetString()!;

        if (type == "RegisterAccountRequest")
        {
            if (!PacketSchemaValidator.TryValidate(req, type, out var registerSchemaError))
                return [new
                {
                    Type = "RegisterAccountResponse",
                    Success = false,
                    Message = registerSchemaError
                }];
            var (ok, msg) = _store.Register(
                req.Str("LoginName"), req.Str("Password"), req.Str("EMAIL"));
            return [new { Type = "RegisterAccountResponse", Success = ok, Message = msg }];
        }

        if (type == "LoginRequest")
        {
            if (!PacketSchemaValidator.TryValidate(req, type, out var loginSchemaError))
                return [new
                {
                    Type = "LoginResponse",
                    Success = false,
                    Message = loginSchemaError,
                    Characters = Array.Empty<object>()
                }];
            if (state.CharacterId.HasValue ||
                state.PublishedCharacterId > 0)
            {
                DisconnectPresence(state);
            }
            else
            {
                _sessions.Unregister(state.ConnectionId);
            }

            var id = _store.Login(req.Str("User"), req.Str("Pass"));
            state.AccountId = id;
            state.CharacterId = null;
            state.CharacterName = "";
            state.CurrentMapId = 0;
            state.PendingMapSpawn = false;
            state.WorldReady = false;
            state.LastPongAt = DateTimeOffset.UtcNow;
            state.LastPingAt = DateTimeOffset.MinValue;
            ClearPendingTrainTravel(state);

            if (id.HasValue)
            {
                var replacedConnection =
                    _sessions.Register(
                        id.Value,
                        state.ConnectionId);

                if (replacedConnection.HasValue &&
                    replacedConnection.Value !=
                        state.ConnectionId)
                {
                    _broadcasts.PublishToConnection(
                        replacedConnection.Value,
                        new
                        {
                            Type = "ForceLogout",
                            Message =
                                "您的帳號已在其他地方登入！"
                        });
                }
            }

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
                Announcements = AnnouncementCatalog.Read(_configuration)
            }];
        }

        if (type == "Pong")
        {
            state.LastPongAt = DateTimeOffset.UtcNow;
            return [];
        }

        if (!state.AccountId.HasValue)
        {
            _diagnostics.Record(
                "Warning",
                "Auth",
                $"Unauthenticated request: {type}",
                state.ConnectionId);
            _log.LogWarning(
                "[UNAUTHENTICATED_REQUEST] Type={Type} Connection={ConnectionID}",
                type,
                state.ConnectionId);
            return
            [
                new
                {
                    Type = "PacketError",
                    RequestType = type,
                    Code = "AUTH_REQUIRED",
                    Message = "請先登入後再進行此操作"
                }
            ];
        }

        var accountId = state.AccountId.Value;

        if (!_sessions.IsCurrent(
                accountId,
                state.ConnectionId))
        {
            state.CloseRequested = true;
            return [new
            {
                Type = "ForceLogout",
                Message = "您的帳號已在其他地方登入！"
            }];
        }

        if (!PacketSchemaValidator.TryValidate(req, type, out var schemaError))
        {
            _diagnostics.PacketError();
            _diagnostics.Record(
                "Warning",
                "InvalidPacket",
                $"{type}: {schemaError}",
                state.ConnectionId,
                state.AccountId,
                state.CharacterId,
                state.CurrentMapId);
            _log.LogWarning(
                "[INVALID_PACKET] Type={Type} Reason={Reason} Connection={ConnectionID} Account={AccountID} Character={CharacterID} Map={MapID}",
                type,
                schemaError,
                state.ConnectionId,
                state.AccountId,
                state.CharacterId,
                state.CurrentMapId);
            return
            [
                new
                {
                    Type = "PacketError",
                    RequestType = type,
                    Code = "INVALID_PACKET",
                    Message = schemaError
                }
            ];
        }

        if (!state.CharacterId.HasValue &&
            RequiresSelectedCharacter(type))
        {
            return
            [
                new
                {
                    Type = "PacketError",
                    RequestType = type,
                    Code = "WORLD_REQUIRED",
                    Message = "請先選擇角色並進入遊戲世界"
                }
            ];
        }

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
                    req.Int("RewardID"),
                    req.Str("LoginName"),
                    req.Str("Password"),
                    req.Str("RequestID"));
            }

            case "TrainTravelReady":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var travelId = req.Str("TravelID");
                if (string.IsNullOrWhiteSpace(
                        state.PendingTrainTravelId) ||
                    !string.Equals(
                        travelId,
                        state.PendingTrainTravelId,
                        StringComparison.Ordinal) ||
                    state.PendingTrainExpiresAt <
                        DateTimeOffset.UtcNow)
                {
                    ClearPendingTrainTravel(state);
                    return [new
                    {
                        Type = "SystemMessageFlash",
                        Message = "列車行程已失效"
                    }];
                }

                var npc = _world.FindNpc(
                    state.CurrentMapId,
                    state.PendingTrainNpcEntityId);

                if (npc is null ||
                    !string.Equals(
                        npc.Script,
                        state.PendingTrainScript,
                        StringComparison.OrdinalIgnoreCase))
                {
                    ClearPendingTrainTravel(state);
                    return [new
                    {
                        Type = "SystemMessageFlash",
                        Message = "找不到原列車車長"
                    }];
                }

                var continuation =
                    state.PendingTrainContinuation;
                ClearPendingTrainTravel(state);

                var character = _store.GetCharacter(
                    accountId,
                    state.CharacterId.Value);

                if (character is null)
                    return [];

                return ExecuteQuest(
                    state,
                    accountId,
                    npc,
                    continuation,
                    null,
                    character,
                    suppressLegacyMediaDialogs: true);
            }

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
                        req.Long("TargetDBID"),
                        req.Str("ClubName"),
                        req.Str("ClubMsg"),
                        req.Long("Amount"),
                        req.Int("ResearchID"))
                    .ToList();

                if (action is
                    "DonateMoney" or
                    "DonateExp" or
                    "Create")
                {
                    var assets = _store.GetCharacterAssets(
                        accountId,
                        state.CharacterId.Value);

                    packets.Add(
                        BuildAndBroadcastEffectiveStatusPacket(
                            state,
                            accountId,
                            state.CharacterId.Value,
                            assets));

                    if (action == "Create")
                    {
                        packets.Add(new
                        {
                            Type = "InventorySync",
                            Items = assets.Inventory,
                            MaxSize = assets.InventoryMaxSize
                        });
                    }
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
                var dbid = req.Long("DBID");
                _positionWrites?.FenceAndDiscard(accountId, dbid);
                var ok = _store.Delete(accountId, dbid, req.Str("Password"));
                if (ok &&
                    state.CharacterId == dbid)
                {
                    state.CharacterId = null;
                    state.CharacterName = "";
                    state.CharacterRace = 0;
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
                             accountId, req.Long("DBID"), newPwd);

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
                var dbid = req.Long("DBID");
                _positionWrites?.FlushQueued(accountId, dbid);
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
                state.CharacterName = character.TryGetValue("NAME", out var selectedName)
                    ? selectedName?.ToString() ?? $"角色{dbid}"
                    : $"角色{dbid}";
                state.CharacterRace = Convert.ToInt32(character["Race"]);
                state.CurrentMapId = Convert.ToInt32(character["MapID"]);
                state.LastX = Convert.ToDouble(character["LocX"]);
                state.LastY = Convert.ToDouble(character["LocY"]);
                state.PositionDirty = false;
                state.LastPositionPersistAt = DateTimeOffset.UtcNow;
                state.LastPositionBroadcastAt = DateTimeOffset.MinValue;
                state.LastBroadcastPositionX = null;
                state.LastBroadcastPositionY = null;
                state.LastResourceRecoveryAt = DateTimeOffset.UtcNow;
                state.LastCompanionRuntimeAt = DateTimeOffset.UtcNow;
                state.LastCompanionClientSyncAt = DateTimeOffset.UtcNow;
                state.PendingMapSpawn = true;
                state.WorldReady = false;
                character["Type"] = "EnterWorldSuccess";

                // A process restart loses the in-memory player-shop registry
                // but SQLite may still contain InShop=1 rows from a shop that
                // was open at the crash. Recover those orphaned reservations
                // before the first asset bootstrap reaches the client.
                _playerShop.RecoverStaleReservations(
                    accountId,
                    dbid);

                // Reconcile collection objectives against authoritative
                // inventory before sending the first QuestSync after login.
                _questProgress.RefreshCollectedItems(
                    accountId,
                    dbid);

                var assets = _store.GetCharacterAssets(accountId, dbid);
                ApplyAssetsToCharacterBootstrap(character, assets);
                ApplyCosmeticOverrides(
                    character,
                    accountId,
                    dbid);
                state.CanCrossSpecialTerrain =
                    ResolveSpecialTerrainCapability(
                        character,
                        assets);
                var transformExpiresUtcMs = Convert.ToInt64(
                    character.GetValueOrDefault(
                        "TransformExpiresUtcMs",
                        0L));
                state.ActiveTransformExpiresAt =
                    transformExpiresUtcMs > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(
                            transformExpiresUtcMs)
                        : DateTimeOffset.MinValue;
                character["Shortcuts"] = _store.GetShortcuts(accountId, dbid);
                character["Quests"] = _store.GetQuests(accountId, dbid);
                character["SkillList"] = _store.GetSkills(accountId, dbid);
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
                        MaxSize = assets.PetMaxSize,
                        MaxSummon = assets.PetSummonMax
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
                    BuildTimedEffectSync(accountId, dbid),
                    new
                    {
                        Type = "QuestSync",
                        Quests = _store.GetQuests(accountId, dbid)
                    },
                    BuildFestivalThemeSync()
                ];
            }

            case "ViewportAoi":
            {
                // Unlike SyncPosition, a stationary player's resized
                // viewport must update the visibility known-sets immediately.
                // No collision checks, persistence, AI or movement broadcast.
                if (!state.WorldReady || !state.CharacterId.HasValue ||
                    state.CurrentMapId <= 0)
                    return [];
                if (_broadcasts.SetViewRadius(
                        state.ConnectionId, req.Int("Radius", 1800)))
                {
                    PublishPlayerAoiChanges(
                        _broadcasts.ReconcilePlayerAoi(state.ConnectionId));
                    PublishWorldAoiChanges(
                        _broadcasts.ReconcileWorldAoi(state.ConnectionId));
                }
                return [];
            }

            case "WorldReady":
            {
                if (!state.CharacterId.HasValue || state.CurrentMapId <= 0)
                {
                    return
                    [
                        new
                        {
                            Type = "WorldReadyResponse",
                            Success = false,
                            MapID = state.CurrentMapId,
                            Message = "角色尚未進入地圖"
                        }
                    ];
                }

                var requestedMapId = req.Int("MapID", state.CurrentMapId);
                if (requestedMapId != state.CurrentMapId)
                {
                    return
                    [
                        new
                        {
                            Type = "WorldReadyResponse",
                            Success = false,
                            MapID = state.CurrentMapId,
                            Message = "WorldReady 地圖與目前地圖不一致"
                        }
                    ];
                }

                _broadcasts.SetViewRadius(
                    state.ConnectionId, req.Int("ViewRadius", 1800));
                state.WorldReady = true;

                // Map loading is asynchronous on the Godot client. Ground-item
                // spawn packets sent during the scene swap can be discarded
                // when the old map entity queue is cleared. WorldReady is the
                // first reliable point where the new scene is fully active, so
                // send a fresh snapshot here as the authoritative final sync.
                var worldReadyPackets = new List<object>
                {
                    new
                    {
                        Type = "WorldReadyResponse",
                        Success = true,
                        MapID = state.CurrentMapId
                    }
                };
                // Players are interactive and more latency sensitive than
                // bulk map objects. Send their initial SpawnEntity data just
                // after WorldReadyResponse, before the potentially large
                // NPC/monster/ground snapshot. Keep all world-object ordering
                // and AOI rules otherwise exactly as before.
                worldReadyPackets.AddRange(
                    BuildOtherPlayerSnapshotPackets(state));
                worldReadyPackets.AddRange(
                    BuildActivePetSnapshotPackets(
                        state,
                        accountId,
                        state.CharacterId.Value,
                        state.LastX,
                        state.LastY));
                // Late authoritative snapshot: async client map teardown can
                // discard early NPC/monster spawns. Reconcile world entities
                // only when the new Godot scene is confirmed ready.
                worldReadyPackets.AddRange(
                    _combat.BuildCurrentMapSnapshot(state));
                worldReadyPackets.AddRange(
                    BuildGroundItemSnapshotPackets(state));
                worldReadyPackets.AddRange(
                    BuildWorldAoiSnapshotPackets(state));

                // Replaces up to eight redundant periodic full-map snapshots.
                state.WorldSnapshotRetriesRemaining = 0;
                state.NextWorldSnapshotAt = DateTimeOffset.MinValue;
                _log.LogDebug(
                    "[WORLD_READY_SNAPSHOT] Map={MapID} Packets={Count} GroundItems={GroundItems}",
                    state.CurrentMapId,
                    worldReadyPackets.Count,
                    state.GroundItems.Count);
                return worldReadyPackets;
            }

            case "SyncPosition":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                if (state.AutoSaintEnabled)
                    return [];

                var x = req.Int("X");
                var y = req.Int("Y");

                // An unchanged position is not a movement event. Avoid
                // redundant Blocking.dat validation, spatial-AOI work,
                // SQLite position dirties and broadcast packets. Ping and
                // gameplay messages are handled independently.
                if (state.LastX == x && state.LastY == y)
                    return [];

                var movementStarted = Stopwatch.GetTimestamp();
                var validationStarted = movementStarted;
                var aoiMs = 0.0;
                var persistMs = 0.0;
                var broadcastMs = 0.0;

                var moveValidation = _blocking.ValidateMove(
                    state.CurrentMapId,
                    state.LastX,
                    state.LastY,
                    x,
                    y,
                    state.CanCrossSpecialTerrain);
                var validationMs = Stopwatch.GetElapsedTime(validationStarted).TotalMilliseconds;

                if (!moveValidation.Allowed)
                {
                    _log.LogWarning(
                        "[MOVE_BLOCKED] DBID={DBID} Map={MapID} Reason={Reason} From=({FromX},{FromY}) To=({ToX},{ToY}) Cells=({FromCellX},{FromCellY})->({ToCellX},{ToCellY}) Special={Special}",
                        state.CharacterId.Value,
                        state.CurrentMapId,
                        moveValidation.Reason,
                        state.LastX,
                        state.LastY,
                        x,
                        y,
                        moveValidation.FromCellX,
                        moveValidation.FromCellY,
                        moveValidation.ToCellX,
                        moveValidation.ToCellY,
                        state.CanCrossSpecialTerrain);

                    return
                    [
                        new
                        {
                            Type = "PositionCorrection",
                            X = (int)Math.Round(state.LastX),
                            Y = (int)Math.Round(state.LastY)
                        }
                    ];
                }

                var aoiStarted = Stopwatch.GetTimestamp();
                state.LastX = x;
                state.LastY = y;
                state.PositionDirty = true;
                state.IsResting = false;
                _broadcasts.UpdatePosition(
                    state.ConnectionId,
                    x,
                    y);
                if (state.WorldReady)
                {
                    PublishPlayerAoiChanges(
                        _broadcasts.ReconcilePlayerAoi(state.ConnectionId));
                    PublishWorldAoiChanges(
                        _broadcasts.ReconcileWorldAoi(state.ConnectionId));
                    // WebSocketEndpoint calls SyncPresence immediately after
                    // Handle(). The approved movement already reconciled both
                    // indexes here: do not repeat the same expensive scan.
                    state.PresenceAoiInitialized = true;
                    state.LastPresenceAoiX = x;
                    state.LastPresenceAoiY = y;
                }
                aoiMs = Stopwatch.GetElapsedTime(aoiStarted).TotalMilliseconds;

                var now = DateTimeOffset.UtcNow;
                if (now - state.LastPositionPersistAt >=
                    PositionPersistInterval)
                {
                    var persistStarted = Stopwatch.GetTimestamp();
                    if (_positionWrites is not null)
                    {
                        // No SQLite writer / busy_timeout while processing
                        // live SyncPosition or holding the session lock.
                        // This only stages the newest validated coordinates.
                        _positionWrites.Stage(
                            state.AccountId!.Value,
                            state.CharacterId.Value,
                            state.CurrentMapId,
                            x, y);
                        state.LastPositionPersistAt = now;
                        // Keep PositionDirty until the final logout fence.
                    }
                    else
                    {
                        PersistPendingPosition(state);
                    }
                    persistMs = Stopwatch.GetElapsedTime(persistStarted).TotalMilliseconds;
                }

                var moveDir = req.Str("Dir", "");
                if (moveDir is not (
                    "1" or "2" or "3" or "4" or
                    "5" or "6" or "7" or "8"))
                {
                    // Legacy/test clients may omit Dir. Never fabricate "1":
                    // the new Client can derive facing from displacement when
                    // the movement snapshot carries no trustworthy heading.
                    moveDir = "";
                }

                var broadcastStarted = Stopwatch.GetTimestamp();
                if (now - state.LastPositionBroadcastAt >=
                    PositionBroadcastInterval)
                {
                    // Describe the actual interval between the authorized
                    // position snapshots, not a hardcoded 100ms. Real players
                    // often publish every ~100ms; bots on 350ms cadence
                    // otherwise look like they stop after each short tween.
                    var fullSnapshotSeconds =
                        state.LastPositionBroadcastAt == DateTimeOffset.MinValue
                            ? PositionBroadcastInterval.TotalSeconds
                            : (now - state.LastPositionBroadcastAt).TotalSeconds;
                    var snapshotSeconds = Math.Clamp(fullSnapshotSeconds, 0.05, 0.75);
                    state.LastPositionBroadcastAt = now;
                    state.LastBroadcastPositionX = x;
                    state.LastBroadcastPositionY = y;
                    // A server stall can exceed 750ms before this packet
                    // even enters the outbound queue. Preserve its full
                    // span so the remote client does not infer a fast dash.
                    // Ordinary 100ms snapshots keep their original size.
                    if (fullSnapshotSeconds > 0.75)
                    {
                        _broadcasts.PublishToPlayerObservers(
                            state.ConnectionId,
                            new
                            {
                                Type = "MoveEntity",
                                EntityID = state.CharacterId.Value,
                                X = x,
                                Y = y,
                                Dir = moveDir,
                                SpeedTime = snapshotSeconds,
                                SnapshotSpanSeconds = Math.Min(fullSnapshotSeconds, 8.0)
                            });
                    }
                    else
                    {
                        _broadcasts.PublishToPlayerObservers(
                            state.ConnectionId,
                            new
                            {
                                Type = "MoveEntity",
                                EntityID = state.CharacterId.Value,
                                X = x,
                                Y = y,
                                Dir = moveDir,
                                SpeedTime = snapshotSeconds
                            });
                    }
                }
                broadcastMs = Stopwatch.GetElapsedTime(broadcastStarted).TotalMilliseconds;
                var movementMs = Stopwatch.GetElapsedTime(movementStarted).TotalMilliseconds;
                if (movementMs >= 200 &&
                    now - state.LastSlowMovementDiagAt >= TimeSpan.FromSeconds(5))
                {
                    state.LastSlowMovementDiagAt = now;
                    _log.LogWarning(
                        "[SLOW_MOVEMENT_HANDLER] TotalMs={TotalMs:F1} CollisionMs={CollisionMs:F1} AoiMs={AoiMs:F1} PersistMs={PersistMs:F1} BroadcastMs={BroadcastMs:F1} Character={CharacterID} Map={MapID}",
                        movementMs, validationMs, aoiMs, persistMs, broadcastMs,
                        state.CharacterId, state.CurrentMapId);
                }

                return [];
            }

            case "UpdateShortcut":
            {
                if (!state.CharacterId.HasValue)
                {
                    return
                    [
                        new
                        {
                            Type = "UpdateShortcutResponse",
                            Success = false,
                            SlotIndex = req.Int("SlotIndex", -1),
                            ShortcutType = req.Int("ShortcutType"),
                            TargetID = req.Int("TargetID"),
                            Message = "角色尚未登入"
                        }
                    ];
                }

                var slotIndex = req.Int("SlotIndex", -1);
                var shortcutType = req.Int("ShortcutType");
                var targetId = req.Int("TargetID");

                var ok = _store.UpdateShortcut(
                    accountId,
                    state.CharacterId.Value,
                    slotIndex,
                    shortcutType,
                    targetId);

                if (!ok)
                {
                    return
                    [
                        new
                        {
                            Type = "UpdateShortcutResponse",
                            Success = false,
                            SlotIndex = slotIndex,
                            ShortcutType = shortcutType,
                            TargetID = targetId,
                            Message = "快速鍵儲存失敗"
                        },
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "快速鍵儲存失敗"
                        }
                    ];
                }

                return
                [
                    new
                    {
                        Type = "UpdateShortcutResponse",
                        Success = true,
                        SlotIndex = slotIndex,
                        ShortcutType = shortcutType,
                        TargetID = targetId
                    }
                ];
            }

            case "UseItemRequest":
            {
                if (!state.CharacterId.HasValue) return [];

                var itemUid = req.Long("ItemUID");
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

                if (itemIndex ==
                    TransformationCatalog.TransformScrollItemId)
                {
                    var spec = TransformationCatalog.Resolve(
                        _world,
                        assetsBefore);

                    if (spec is null)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = "目前沒有可用的變身外型資料"
                            }
                        ];
                    }

                    var now = DateTimeOffset.UtcNow;
                    var startedUtcMs =
                        now.ToUnixTimeMilliseconds();
                    var expiresUtcMs = checked(
                        startedUtcMs +
                        (long)TransformationCatalog.DurationSeconds *
                        1000L);
                    var mutation =
                        _store.ActivateTransformFromItem(
                            accountId,
                            state.CharacterId.Value,
                            itemUid,
                            itemIndex,
                            new CharacterTransformState(
                                1,
                                spec.TransformId,
                                0,
                                itemIndex,
                                startedUtcMs,
                                expiresUtcMs,
                                spec.AttackBonus,
                                spec.DefenseBonus,
                                spec.MagicAttackBonus,
                                spec.MagicDefenseBonus,
                                spec.HitBonus,
                                spec.DodgeBonus));

                    if (!mutation.Success)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = mutation.Message
                            }
                        ];
                    }

                    state.ActiveTransformExpiresAt =
                        DateTimeOffset.FromUnixTimeMilliseconds(
                            expiresUtcMs);

                    var assets = _store.GetCharacterAssets(
                        accountId,
                        state.CharacterId.Value);

                    _party.PublishStat(
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
                        BuildAndBroadcastAppearancePacket(
                            state,
                            accountId,
                            state.CharacterId.Value,
                            assets),
                        BuildAndBroadcastEffectiveStatusPacket(
                            state,
                            accountId,
                            state.CharacterId.Value,
                            assets),
                        BuildTimedEffectSync(
                            accountId,
                            state.CharacterId.Value),
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex),
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message =
                                $"已變身為：{spec.DisplayName}（1 小時）"
                        }
                    ];
                }

                if (itemIndex ==
                    TransformationCatalog.RestoreScrollItemId)
                {
                    var mutation =
                        _store.ClearTransformFromItem(
                            accountId,
                            state.CharacterId.Value,
                            itemUid,
                            itemIndex);

                    if (!mutation.Success)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = mutation.Message
                            }
                        ];
                    }

                    state.ActiveTransformExpiresAt =
                        DateTimeOffset.MinValue;

                    var assets = _store.GetCharacterAssets(
                        accountId,
                        state.CharacterId.Value);

                    _party.PublishStat(
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
                        BuildAndBroadcastAppearancePacket(
                            state,
                            accountId,
                            state.CharacterId.Value,
                            assets),
                        BuildAndBroadcastEffectiveStatusPacket(
                            state,
                            accountId,
                            state.CharacterId.Value,
                            assets),
                        BuildTimedEffectSync(
                            accountId,
                            state.CharacterId.Value),
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex),
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "已恢復原本外觀"
                        }
                    ];
                }

                if (itemIndex ==
                    LanternTransformationCatalog.SourceItemId)
                {
                    var lantern =
                        LanternTransformationCatalog.Resolve(
                            _current,
                            assetsBefore);

                    if (lantern is null)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message =
                                    "請先裝備可辨識的武器後再使用燈籠變身器"
                            }
                        ];
                    }

                    var mutation =
                        _store.ApplyCosmeticOverridesFromItem(
                            accountId,
                            state.CharacterId.Value,
                            itemUid,
                            itemIndex,
                            lantern.Overrides,
                            lantern.ConsumeSource);

                    if (!mutation.Success)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = mutation.Message
                            }
                        ];
                    }

                    var assets =
                        _store.GetCharacterAssets(
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
                        BuildAndBroadcastAppearancePacket(
                            state,
                            accountId,
                            state.CharacterId.Value,
                            assets),
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex),
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "已套用燈籠武器外觀"
                        }
                    ];
                }

                if (CosmeticTransformationCatalog.Supports(
                        itemIndex))
                {
                    var character =
                        _store.GetCharacter(
                            accountId,
                            state.CharacterId.Value);

                    if (character is null)
                        return [];

                    var race = Convert.ToInt32(
                        character.GetValueOrDefault(
                            "Race",
                            -1));

                    var cosmetic =
                        CosmeticTransformationCatalog.Resolve(
                            _current,
                            itemIndex,
                            race);

                    if (cosmetic is null)
                    {
                        var requiredRace =
                            CosmeticTransformationCatalog.RequiredRace(
                                itemIndex);

                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = requiredRace.HasValue &&
                                          requiredRace.Value != race
                                    ? "此換裝變身器不適用於目前角色種族"
                                    : "換裝外觀資料不完整，未消耗道具"
                            }
                        ];
                    }

                    var mutation =
                        _store.ApplyCosmeticOverridesFromItem(
                            accountId,
                            state.CharacterId.Value,
                            itemUid,
                            itemIndex,
                            cosmetic.Overrides,
                            cosmetic.ConsumeSource);

                    if (!mutation.Success)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = mutation.Message
                            }
                        ];
                    }

                    var assets =
                        _store.GetCharacterAssets(
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
                        BuildAndBroadcastAppearancePacket(
                            state,
                            accountId,
                            state.CharacterId.Value,
                            assets),
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex),
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message =
                                $"已套用外觀：{cosmetic.Name}"
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

                var fixedRewardBox =
                    FixedRewardBoxCatalog.Find(itemIndex);

                if (fixedRewardBox is not null)
                {
                    var reward =
                        _current.FindItem(
                            fixedRewardBox.RewardItemId);

                    if (reward is null)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message =
                                    "寶箱獎勵資料不存在，未消耗寶箱"
                            }
                        ];
                    }

                    var opened =
                        _store.OpenFixedRewardBox(
                            accountId,
                            state.CharacterId.Value,
                            itemUid,
                            fixedRewardBox.BoxItemId,
                            fixedRewardBox.RewardItemId,
                            fixedRewardBox.RewardKind,
                            rewardPart: reward.EquipSlot,
                            rewardDamage: reward.Durability);

                    if (!opened.Success)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = opened.Message
                            }
                        ];
                    }

                    var assets =
                        _store.GetCharacterAssets(
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
                            Type = "EquipSync",
                            Equips = assets.Equips,
                            MaxSize = assets.EquipMaxSize
                        },
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex),
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message =
                                $"開箱獲得：{fixedRewardBox.RewardName}"
                        }
                    ];
                }

                var recoveredBox =
                    RecoveredBoxCatalog.Resolve(
                        _current,
                        itemIndex);

                if (recoveredBox is not null)
                {
                    var grants = recoveredBox.Rewards
                        .Select(reward =>
                            new FixedRewardGrant(
                                reward.ItemIndex,
                                reward.Kind,
                                1,
                                reward.Part,
                                reward.Damage))
                        .ToArray();

                    var opened =
                        _store.OpenFixedRewardBundle(
                            accountId,
                            state.CharacterId.Value,
                            itemUid,
                            itemIndex,
                            grants);

                    if (!opened.Success)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = opened.Message
                            }
                        ];
                    }

                    var assets =
                        _store.GetCharacterAssets(
                            accountId,
                            state.CharacterId.Value);

                    var packets = new List<object>
                    {
                        new
                        {
                            Type = "InventorySync",
                            Items = assets.Inventory,
                            MaxSize = assets.InventoryMaxSize
                        }
                    };

                    if (recoveredBox.Rewards.Any(x =>
                            x.Kind == AssetPurchaseKind.Equipment))
                    {
                        packets.Add(new
                        {
                            Type = "EquipSync",
                            Equips = assets.Equips,
                            MaxSize = assets.EquipMaxSize
                        });
                    }

                    if (recoveredBox.Rewards.Any(x =>
                            x.Kind == AssetPurchaseKind.HorseEquipment))
                    {
                        packets.Add(new
                        {
                            Type = "HorseSync",
                            Horses = assets.Horses,
                            HorseEquips = assets.HorseEquips,
                            MaxHorseEquipSize =
                                assets.HorseEquipMaxSize
                        });
                    }

                    packets.Add(
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex));

                    packets.Add(new
                    {
                        Type = "SystemMessageFlash",
                        Message =
                            "開箱獲得：" +
                            string.Join(
                                "、",
                                recoveredBox.Rewards
                                    .Select(x => x.Name))
                    });

                    return packets;
                }

                if (itemIndex == 2301)
                {
                    var expanded =
                        _store.ExpandPetSummonCapacityFromItem(
                            accountId,
                            state.CharacterId.Value,
                            itemUid,
                            itemIndex);

                    if (!expanded.Success)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = expanded.Message
                            }
                        ];
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
                            Type = "PetSync",
                            Pets = assets.Pets,
                            MaxSize = assets.PetMaxSize,
                            MaxSummon = assets.PetSummonMax
                        },
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex),
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = expanded.Message
                        }
                    ];
                }

                if (itemIndex == 2000)
                {
                    const int capitalMapId = 15;
                    const int capitalX = 9408;
                    const int capitalY = 10336;

                    _positionWrites?.FenceAndDiscard(
                        accountId, state.CharacterId.Value);
                    var teleported =
                        _store.UseTeleportItem(
                            accountId,
                            state.CharacterId.Value,
                            itemUid,
                            itemIndex,
                            capitalMapId,
                            capitalX,
                            capitalY);

                    if (!teleported.Success)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = teleported.Message
                            }
                        ];
                    }

                    state.CurrentMapId =
                        teleported.MapId;
                    state.LastX = teleported.X;
                    state.LastY = teleported.Y;
                    state.PositionDirty = false;
                    state.LastPositionPersistAt =
                        DateTimeOffset.UtcNow;
                    state.PendingMapSpawn = true;
                    state.WorldReady = false;
                    state.LoadedMonsterMapId = 0;
                    state.PetTargets.Clear();
                    state.PetOutOfRangeSince.Clear();

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
                            Type = "ChangeMap",
                            MapID = teleported.MapId,
                            X = teleported.X,
                            Y = teleported.Y
                        },
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message =
                                "已傳送回首都．尼尼亞皇城"
                        }
                    ];
                }

                var capacityExpansion = itemIndex switch
                {
                    2302 => (
                        Inventory: 100,
                        Equip: 0,
                        Pet: 0,
                        HorseEquip: 0,
                        Label: "道具空間"),
                    2303 => (
                        Inventory: 0,
                        Equip: 100,
                        Pet: 0,
                        HorseEquip: 0,
                        Label: "裝備空間"),
                    2304 => (
                        Inventory: 0,
                        Equip: 0,
                        Pet: 10,
                        HorseEquip: 0,
                        Label: "寵物空間"),
                    2305 => (
                        Inventory: 0,
                        Equip: 0,
                        Pet: 0,
                        HorseEquip: 25,
                        Label: "機甲裝備空間"),
                    _ => (
                        Inventory: 0,
                        Equip: 0,
                        Pet: 0,
                        HorseEquip: 0,
                        Label: "")
                };

                if (capacityExpansion.Inventory +
                    capacityExpansion.Equip +
                    capacityExpansion.Pet +
                    capacityExpansion.HorseEquip > 0)
                {
                    var expanded =
                        _store.ExpandCapacityFromItem(
                            accountId,
                            state.CharacterId.Value,
                            itemUid,
                            itemIndex,
                            capacityExpansion.Inventory,
                            capacityExpansion.Equip,
                            capacityExpansion.Pet,
                            capacityExpansion.HorseEquip);

                    if (!expanded.Success)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = expanded.Message
                            }
                        ];
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
                            MaxHorseEquipSize =
                                assets.HorseEquipMaxSize
                        },
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex),
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message =
                                $"{capacityExpansion.Label}擴充完成"
                        }
                    ];
                }

                if (itemIndex == 2616)
                {
                    var currentCharacter =
                        _store.GetCharacter(
                            accountId,
                            state.CharacterId.Value);

                    if (currentCharacter is null)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = "角色資料不存在"
                            }
                        ];
                    }

                    var preview =
                        new Dictionary<string, object>(
                            currentCharacter)
                        {
                            ["Str"] = 1,
                            ["Dex"] = 1,
                            ["Con"] = 1,
                            ["Wis"] = 1
                        };

                    var growth = _growth.Calculate(
                        preview);

                    var reset =
                        _store.ResetCharacterStatsFromItem(
                            accountId,
                            state.CharacterId.Value,
                            itemUid,
                            itemIndex,
                            1,
                            1,
                            1,
                            1,
                            growth.MaxHP,
                            growth.MaxMP,
                            growth.MaxSP,
                            growth.Attack,
                            growth.Defense,
                            growth.MagicAttack,
                            growth.MagicDefense,
                            growth.Hit,
                            growth.Dodge,
                            growth.MoveSpeed);

                    if (!reset.Success ||
                        reset.Character is null)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = reset.Message
                            }
                        ];
                    }

                    var updated = reset.Character;
                    var assets = _store.GetCharacterAssets(
                        accountId,
                        state.CharacterId.Value);
                    var effective =
                        CharacterStatCalculator.Calculate(
                            updated,
                            assets,
                            _current);

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
                            Type = "AllocateStatResponse",
                            Success = true,
                            Message = reset.Message,
                            ExpUse = updated["ExpUse"],
                            STR = updated["Str"],
                            DEX = updated["Dex"],
                            CON = updated["Con"],
                            WIS = updated["Wis"],
                            MaxHP = updated["MaxHP"],
                            MaxMP = updated["MaxMP"],
                            MaxSP = updated["MaxSP"],
                            Attack = effective.Attack,
                            Defense = effective.Defense,
                            MagicAttack =
                                effective.MagicAttack,
                            MagicDefense =
                                effective.MagicDefense,
                            Hit = effective.Hit,
                            Dodge = effective.Dodge,
                            Lucky = effective.Lucky,
                            AttackSpeed =
                                effective.AttackSpeed,
                            AttackRange =
                                effective.AttackRange,
                            MoveSpeed =
                                effective.MoveSpeed,
                            CritRate =
                                effective.CritRate,
                            CritDamage =
                                effective.CritDamage
                        },
                        BuildAndBroadcastEffectiveStatusPacket(
                            state,
                            accountId,
                            state.CharacterId.Value,
                            assets),
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex),
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = reset.Message
                        }
                    ];
                }

                var boosterItem =
                    _current.FindItem(itemIndex);

                if (boosterItem is not null &&
                    boosterItem.Category.Equals(
                        "booster",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var effects =
                        new Dictionary<string, int>(
                            StringComparer.Ordinal);

                    void AddEffect(
                        string key,
                        int value)
                    {
                        if (value > 0)
                            effects[key] = value;
                    }

                    AddEffect("stat_max_hp", boosterItem.MaxHpBonus);
                    AddEffect("stat_max_mp", boosterItem.MaxMpBonus);
                    AddEffect("stat_max_sp", boosterItem.MaxSpBonus);
                    AddEffect("stat_attack", boosterItem.Atk);
                    AddEffect("stat_defense", boosterItem.Def);
                    AddEffect("stat_magic_attack", boosterItem.MAtk);
                    AddEffect("stat_magic_defense", boosterItem.MDef);
                    AddEffect("stat_hit", boosterItem.Hit);
                    AddEffect("stat_dodge", boosterItem.Flee);
                    AddEffect("stat_lucky", boosterItem.Luk);
                    AddEffect("stat_attack_speed", boosterItem.Aspd);
                    AddEffect("stat_move_speed", boosterItem.MoveSpeed);
                    AddEffect("stat_crit_rate", boosterItem.CritRate);
                    AddEffect("stat_crit_damage", boosterItem.CritDamage);

                    if (effects.Count == 0)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = "此增益道具沒有可套用的能力效果"
                            }
                        ];
                    }

                    var durationSeconds = Math.Clamp(
                        _configuration?.GetValue<int?>(
                            "Gameplay:Items:BoosterDurationSeconds")
                        ?? 600,
                        1,
                        86400);

                    var nowUtcMs =
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    var activated =
                        _store.ActivateTimedEffectsFromItem(
                            accountId,
                            state.CharacterId.Value,
                            itemUid,
                            itemIndex,
                            effects,
                            durationSeconds,
                            nowUtcMs,
                            sourceType: 1,
                            sourceValue: itemIndex);

                    if (!activated.Success)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = activated.Message
                            }
                        ];
                    }

                    var assets =
                        _store.GetCharacterAssets(
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
                        BuildAndBroadcastEffectiveStatusPacket(
                            state,
                            accountId,
                            state.CharacterId.Value,
                            assets),
                        BuildTimedEffectSync(
                            accountId,
                            state.CharacterId.Value),
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex),
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message =
                                $"{boosterItem.Name} 已啟動 {durationSeconds / 60.0:0.#} 分鐘"
                        }
                    ];
                }

                var recovery = _recoveryItems.Find(itemIndex);
                if (recovery is not null)
                {
                    var assetsBeforeRecovery =
                        _store.GetCharacterAssets(
                            accountId,
                            state.CharacterId.Value);
                    var beforeRecovery = _store.GetCharacter(
                        accountId,
                        state.CharacterId.Value);
                    if (beforeRecovery is null)
                        return [new { Type = "SystemMessageFlash", Message = "無法使用此恢復物品" }];

                    var effectiveStats = CharacterStatCalculator.Calculate(
                        beforeRecovery,
                        assetsBeforeRecovery,
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

                    var assetsAfterRecovery =
                        _store.GetCharacterAssets(
                            accountId,
                            state.CharacterId.Value);

                    return
                    [
                        BuildAndBroadcastEffectiveStatusPacket(state, 
                            accountId,
                            state.CharacterId.Value,
                            assetsAfterRecovery),
                        new
                        {
                            Type = "InventorySync",
                            Items = assetsAfterRecovery.Inventory,
                            MaxSize = assetsAfterRecovery.InventoryMaxSize
                        },
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex)
                    ];
                }

                var batteryEnergy =
                    _horseBatteries.GetEnergy(itemIndex);

                if (batteryEnergy > 0)
                {
                    var character = _store.GetCharacter(
                        accountId,
                        state.CharacterId.Value);

                    if (character is null)
                        return [];

                    var summonedHorseUid = Convert.ToInt64(
                        character["SummonedHorseUID"]);

                    if (summonedHorseUid <= 0)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = "請先駕駛要充能的機甲"
                            }
                        ];
                    }

                    var horseBefore = assetsBefore.Horses
                        .FirstOrDefault(x =>
                            Convert.ToInt64(x["HorseUID"]) ==
                            summonedHorseUid);

                    if (horseBefore is null)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = "找不到目前駕駛的機甲"
                            }
                        ];
                    }

                    var horseIndex = Convert.ToInt32(
                        horseBefore["HorseIdx"]);
                    var maxEnergy =
                        _horseCards.Find(horseIndex)?.MaxBattery
                        ?? int.MaxValue;

                    var horse = _store.ChargeHorse(
                        accountId,
                        state.CharacterId.Value,
                        summonedHorseUid,
                        itemUid,
                        batteryEnergy,
                        maxEnergy);

                    if (horse is null)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = "機甲能源已滿或充能失敗"
                            }
                        ];
                    }

                    var assets = _store.GetCharacterAssets(
                        accountId,
                        state.CharacterId.Value);

                    return
                    [
                        new
                        {
                            Type = "SyncMechaEnergy",
                            HorseUID = summonedHorseUid,
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
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex),
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "機甲充能完成"
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
                        horseCard.SourceItemIndex,
                        horseCard.HorseIndex,
                        horseCard.Name,
                        horseCard.HorseType,
                        horseCard.MaxBattery);

                    if (horse is null)
                        return [new { Type = "SystemMessageFlash", Message = "機甲卡啟用失敗" }];

                    var activatedHorseUid =
                        Convert.ToInt64(horse["HorseUID"]);
                    var preset = _current.FindHorsePreset(
                        horseCard.HorseIndex);

                    var packageEquipment =
                        new[]
                        {
                            horseCard.BodyId,
                            horseCard.WeaponId,
                            horseCard.EngineId
                        }
                        .Where(x => x > 0)
                        .Distinct()
                        .ToArray();

                    var equipmentToCreate =
                        packageEquipment.Length > 0
                            ? packageEquipment
                            : preset is null
                                ? Array.Empty<int>()
                                : new[]
                                {
                                    preset.BodyId,
                                    preset.WeaponId,
                                    preset.EngineId
                                }
                                .Where(x => x > 0)
                                .Distinct()
                                .ToArray();

                    foreach (var equipIndex in equipmentToCreate)
                    {
                        var equip =
                            _current.FindItem(
                                equipIndex);

                        _store.AddHorseEquipment(
                            accountId,
                            state.CharacterId.Value,
                            equipIndex,
                            activatedHorseUid,
                            equip?.EquipSlot ?? 255,
                            equip?.Durability ?? 0);
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
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex),
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = equipmentToCreate.Length == 0
                                ? $"已啟用機甲：{horseCard.Name}"
                                : $"已啟用機甲：{horseCard.Name}（含預設裝備）"
                        }
                    ];
                }

                var scriptedItem =
                    _current.FindItem(itemIndex);

                if (scriptedItem is not null &&
                    scriptedItem.ScriptName.Equals(
                        "無罪之書",
                        StringComparison.Ordinal))
                {
                    var character =
                        _store.UseDeathExpReductionItem(
                            accountId,
                            state.CharacterId.Value,
                            itemUid,
                            itemIndex,
                            90);

                    if (character is null)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = "無法使用無罪之書"
                            }
                        ];
                    }

                    var assets =
                        _store.GetCharacterAssets(
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
                            Type = "PlayerStatusSync",
                            ExpDeath = character["ExpDeath"]
                        },
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex),
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "死亡經驗值已減少 90%"
                        }
                    ];
                }

                var skillBook =
                    _current.ResolveSkillBook(itemIndex);

                if (skillBook is not null)
                {
                    var learned =
                        _store.LearnSkillFromBook(
                            accountId,
                            state.CharacterId.Value,
                            itemUid,
                            itemIndex,
                            skillBook.SkillId,
                            skillBook.SkillLevel);

                    if (!learned.Success)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = learned.Message
                            }
                        ];
                    }

                    var assets =
                        _store.GetCharacterAssets(
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
                            Type = "SkillSync",
                            Skills = _store.GetSkills(
                                accountId,
                                state.CharacterId.Value)
                        },
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex),
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message =
                                $"已學會 {skillBook.SkillName} Lv.{skillBook.SkillLevel}"
                        }
                    ];
                }

                var experienceCard =
                    _current.ResolveExperienceCard(itemIndex);

                if (experienceCard is not null)
                {
                    var now = DateTimeOffset.UtcNow;
                    var activated =
                        _store.ActivateTimedEffectFromItem(
                            accountId,
                            state.CharacterId.Value,
                            itemUid,
                            itemIndex,
                            "exp_multiplier_percent",
                            experienceCard.MultiplierPercent,
                            experienceCard.DurationSeconds,
                            now.ToUnixTimeMilliseconds(),
                            sourceType: 1,
                            sourceValue: itemIndex);

                    if (!activated.Success)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = activated.Message
                            }
                        ];
                    }

                    var assets =
                        _store.GetCharacterAssets(
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
                        BuildAndBroadcastItemEffect(
                            state,
                            itemIndex),
                        BuildTimedEffectSync(
                            accountId,
                            state.CharacterId.Value),
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message =
                                $"雙倍經驗值已啟動 {experienceCard.DurationSeconds / 3600} 小時"
                        }
                    ];
                }

                var choiceReward =
                    ChoiceRewardCatalog.Resolve(
                        _current,
                        itemIndex);

                if (choiceReward is not null)
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
                                Message =
                                    "此選擇型道具目前無法使用"
                            }
                        ];
                    }

                    return
                    [
                        new
                        {
                            Type = "ItemChoiceRequired",
                            ItemUID = itemUid,
                            ItemIndex = itemIndex,
                            Title = choiceReward.SourceName,
                            Options = choiceReward.Options
                                .Select(option =>
                                    new
                                    {
                                        ItemIndex =
                                            option.ItemIndex,
                                        Name =
                                            option.Name
                                    })
                                .ToArray()
                        }
                    ];
                }

                var keyedChest =
                    LegacyKeyedChestCatalog.Resolve(
                        _current,
                        itemIndex);

                if (keyedChest is not null)
                {
                    var hasKey =
                        assetsBefore.Inventory.Any(x =>
                            Convert.ToInt32(
                                x["ItemIndex"]) ==
                                keyedChest.KeyItemId &&
                            Convert.ToInt32(
                                x["Count"]) > 0 &&
                            Convert.ToInt32(
                                x["InBank"]) == 0 &&
                            Convert.ToInt32(
                                x["InShop"]) == 0);

                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = hasKey
                                ? $"已找到{keyedChest.KeyName}，但原始寶箱獎勵表尚未恢復；為避免錯誤發獎，寶箱與鑰匙均未消耗"
                                : $"開啟{keyedChest.ChestName}需要{keyedChest.KeyName}，未消耗寶箱"
                        }
                    ];
                }

                if (AmbiguousRewardCatalog.IsAmbiguous(
                        _current,
                        itemIndex))
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message =
                                "此舊版道具的獎勵規則無法唯一對應新版資料，未消耗道具"
                        }
                    ];
                }

                if (scriptedItem is not null &&
                    (IsCraftingMaterialItem(scriptedItem) ||
                     CraftingArtifactCatalog.Resolve(
                         _current,
                         itemIndex) is not null))
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message =
                                "此物品是製作材料／藍圖，請在製作介面使用"
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

            case "UseItemChoiceRequest":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var itemUid =
                    req.Long("ItemUID");
                var selectedItemIndex =
                    req.Int("ItemIndex");

                var assetsBefore =
                    _store.GetCharacterAssets(
                        accountId,
                        state.CharacterId.Value);
                var sourceItem =
                    assetsBefore.Inventory
                        .FirstOrDefault(x =>
                            Convert.ToInt64(
                                x["ItemUID"]) ==
                            itemUid);

                if (sourceItem is null)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message =
                                "找不到可使用的選擇型道具"
                        }
                    ];
                }

                var sourceItemIndex =
                    Convert.ToInt32(
                        sourceItem["ItemIndex"]);
                var choiceReward =
                    ChoiceRewardCatalog.Resolve(
                        _current,
                        sourceItemIndex);

                var selected =
                    choiceReward?.Options
                        .FirstOrDefault(x =>
                            x.ItemIndex ==
                            selectedItemIndex);

                if (choiceReward is null ||
                    selected is null)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message =
                                "獎勵選項已失效，請重新使用道具"
                        }
                    ];
                }

                var opened =
                    _store.OpenFixedRewardBundle(
                        accountId,
                        state.CharacterId.Value,
                        itemUid,
                        sourceItemIndex,
                        [
                            new FixedRewardGrant(
                                selected.ItemIndex,
                                selected.Kind,
                                1,
                                selected.Part,
                                selected.Damage)
                        ]);

                if (!opened.Success)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = opened.Message
                        }
                    ];
                }

                var assets =
                    _store.GetCharacterAssets(
                        accountId,
                        state.CharacterId.Value);
                var packets =
                    new List<object>
                    {
                        new
                        {
                            Type = "InventorySync",
                            Items = assets.Inventory,
                            MaxSize =
                                assets.InventoryMaxSize
                        }
                    };

                if (selected.Kind ==
                    AssetPurchaseKind.Equipment)
                {
                    packets.Add(new
                    {
                        Type = "EquipSync",
                        Equips = assets.Equips,
                        MaxSize =
                            assets.EquipMaxSize
                    });
                }

                if (selected.Kind ==
                    AssetPurchaseKind.HorseEquipment)
                {
                    packets.Add(new
                    {
                        Type = "HorseSync",
                        Horses = assets.Horses,
                        HorseEquips =
                            assets.HorseEquips,
                        MaxHorseEquipSize =
                            assets.HorseEquipMaxSize
                    });
                }

                packets.Add(
                    BuildAndBroadcastItemEffect(
                        state,
                        sourceItemIndex));
                packets.Add(new
                {
                    Type = "SystemMessageFlash",
                    Message =
                        $"已選擇獎勵：{selected.Name}"
                });

                return packets;
            }

            case "EquipRequest":
            {
                if (!state.CharacterId.HasValue) return [];
                var success = _store.EquipItem(
                    accountId,
                    state.CharacterId.Value,
                    req.Long("ItemUID"),
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
                    BuildAndBroadcastEffectiveStatusPacket(state, accountId, state.CharacterId.Value, assets),
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
                    BuildAndBroadcastEffectiveStatusPacket(state, accountId, state.CharacterId.Value, assets),
                    BuildAndBroadcastAppearancePacket(state, accountId, state.CharacterId.Value, assets)
                ];
            }

            case "DriveMechaRequest":
            {
                if (!state.CharacterId.HasValue) return [];
                var horseUid = req.Long("HorseUID", -1);

                // A mecha can stand on terrain its rider cannot traverse
                // on foot. Do not remove that capability while the server's
                // current position is not valid for walking: the player
                // would be stuck and repeatedly receive PositionCorrection.
                if (horseUid == -1 && state.CanCrossSpecialTerrain &&
                    !_blocking.IsMoveAllowed(
                        state.CurrentMapId,
                        state.LastX, state.LastY,
                        state.LastX, state.LastY,
                        canCrossSpecialTerrain: false))
                {
                    return
                    [
                        new
                        {
                            Type = "DriveMechaResponse",
                            Success = false,
                            HorseUID = -1,
                            Message = "請先移動到可步行地面後再解除機甲"
                        }
                    ];
                }

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
                        // The client must rebase its cached walkable position
                        // when stepping out of a mecha. These are SERVER
                        // coordinates, not a client-proposed move target.
                        MapID = state.CurrentMapId,
                        X = state.LastX,
                        Y = state.LastY,
                        Message = success
                            ? (horseUid == -1 ? "已解除駕駛" : "開始駕駛")
                            : "無法駕駛此機甲"
                    }
                };

                if (success)
                {
                    var character = _store.GetCharacter(
                        accountId,
                        state.CharacterId.Value);
                    if (character is not null)
                    {
                        state.CanCrossSpecialTerrain =
                            ResolveSpecialTerrainCapability(
                                character,
                                assets);
                    }

#if DEBUG
                    if (horseUid == -1)
                    {
                        _log.LogInformation(
                            "[MECHA_DISMOUNT_SERVER] DBID={DBID} Map={MapID} Position=({X:F1},{Y:F1}) SpecialAfter={Special}",
                            state.CharacterId.Value, state.CurrentMapId,
                            state.LastX, state.LastY, state.CanCrossSpecialTerrain);
                    }
#endif

                    response.Add(BuildAndBroadcastEffectiveStatusPacket(state, 
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

                var equipUid = req.Long("EquipUID");
                var horseUid = req.Long("HorseUID");
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
                    horseUid,
                    item.EquipSlot);

                if (!success)
                {
                    _log.LogWarning(
                        "[MECHA_EQUIP_FAILED] DBID={DBID} EquipUID={EquipUID} EquipIndex={EquipIndex} HorseUID={HorseUID} StoredPart={StoredPart} AuthoritativePart={AuthoritativePart} HorseType={HorseType} ItemHorseType={ItemHorseType}",
                        state.CharacterId.Value,
                        equipUid,
                        Convert.ToInt32(equipRecord["EquipIdx"]),
                        horseUid,
                        Convert.ToInt32(equipRecord["Part"]),
                        item.EquipSlot,
                        horseType,
                        item.HorseType);
                }

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
                    response.Add(BuildAndBroadcastEffectiveStatusPacket(state, 
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
                    req.Long("EquipUID"),
                    req.Long("HorseUID"));

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
                    response.Add(BuildAndBroadcastEffectiveStatusPacket(state, 
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
                    var activePets = assetsForAttack.Pets
                        .Where(x =>
                            Convert.ToInt32(x["State"]) == 1)
                        .OrderBy(x =>
                            Convert.ToInt64(x["UID"]))
                        .ToArray();

                    if (activePets.Length == 0)
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = "目前沒有召喚中的寵物"
                            }
                        ];

                    var targetEntityId =
                        req.Int("TargetEntityID");
                    if (!state.Monsters.TryGetValue(
                            targetEntityId,
                            out var commandTarget) ||
                        !commandTarget.IsAlive ||
                        commandTarget.MapId !=
                            state.CurrentMapId)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = "攻擊目標不存在或已離開目前地圖"
                            }
                        ];
                    }

                    var packets = new List<object>();

                    for (var i = 0;
                         i < activePets.Length;
                         i++)
                    {
                        if (!commandTarget.IsAlive)
                            break;

                        var activePet = activePets[i];
                        var activePetUid =
                            Convert.ToInt64(
                                activePet["UID"]);
                        state.PetTargets[activePetUid] =
                            targetEntityId;

                        var formation =
                            PetFormationOffset(i);
                        var activePetPosition =
                            state.PetPositions.TryGetValue(
                                activePetUid,
                                out var runtimePosition)
                                ? runtimePosition
                                : (
                                    X: state.LastX +
                                       formation.X,
                                    Y: state.LastY +
                                       formation.Y);

                        state.PetPositions[activePetUid] =
                            activePetPosition;

                        packets.AddRange(
                            _combat.PetAttack(
                                state,
                                accountId,
                                activePetUid,
                                Math.Max(
                                    1,
                                    Convert.ToInt32(
                                        activePet["Attack"])),
                                targetEntityId,
                                activePetPosition.X,
                                activePetPosition.Y));
                    }

                    return packets;
                }

                var petUid = req.Long("PetUID");
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
                PetEvolutionAdvanceResult? evolutionAdvance = null;

                switch (action)
                {
                    case "Summon":
                    {
                        var owner = _store.GetCharacter(
                            accountId,
                            state.CharacterId.Value);
                        var nowMs = DateTimeOffset.UtcNow
                            .ToUnixTimeMilliseconds();

                        var alreadyActive =
                            selectedPetBefore is not null &&
                            Convert.ToInt32(
                                selectedPetBefore["State"]) == 1;
                        var hasSummonSlot =
                            alreadyActive ||
                            previouslyActivePets.Length <
                                assetsBeforePetAction
                                    .PetSummonMax;

                        if (!hasSummonSlot)
                        {
                            mutation = new(
                                false,
                                $"召喚寵物空間已滿（{assetsBeforePetAction.PetSummonMax}）",
                                null);
                            success = false;
                            break;
                        }

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
                            req.Long("ItemUID"),
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

                if (success &&
                    action == "AllocateStat" &&
                    mutation?.Pet is not null)
                {
                    evolutionAdvance = _petEvolution.Advance(
                        accountId,
                        state.CharacterId.Value,
                        petUid,
                        mutation.Pet);
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
                        MaxSize = assets.PetMaxSize,
                        MaxSummon = assets.PetSummonMax
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

                if (evolutionAdvance?.Changed == true)
                {
                    var evolvedPet = evolutionAdvance.Pet;

                    responses.Add(new
                    {
                        Type = "SystemMessageFlash",
                        Message =
                            $"寵物進化！Evol {evolutionAdvance.FromEvolId} → {evolutionAdvance.ToEvolId}"
                    });

                    if (Convert.ToInt32(
                            evolvedPet["State"]) == 1)
                    {
                        var entityId =
                            4_000_000L + petUid;

                        responses.Add(new
                        {
                            Type = "DespawnEntity",
                            EntityID = entityId
                        });

                        var position =
                            state.PetPositions.TryGetValue(
                                petUid,
                                out var runtimePosition)
                                ? runtimePosition
                                : (
                                    X: state.LastX + 40,
                                    Y: state.LastY + 20);

                        state.PetPositions[petUid] = position;

                        responses.Add(BuildPetSpawnPacket(
                            state.CharacterId.Value,
                            evolvedPet,
                            position.X,
                            position.Y));
                    }
                }

                if (success && action == "Summon")
                {
                    var wasAlreadyActive =
                        previouslyActivePets.Contains(
                            petUid);
                    var pet = assets.Pets.FirstOrDefault(x =>
                        Convert.ToInt64(x["UID"]) == petUid);
                    var character = _store.GetCharacter(
                        accountId,
                        state.CharacterId.Value);

                    if (!wasAlreadyActive &&
                        pet is not null &&
                        character is not null)
                    {
                        var activeOrder = assets.Pets
                            .Where(x =>
                                Convert.ToInt32(
                                    x["State"]) == 1)
                            .OrderBy(x =>
                                Convert.ToInt64(
                                    x["UID"]))
                            .Select(x =>
                                Convert.ToInt64(
                                    x["UID"]))
                            .ToArray();
                        var slotIndex = Math.Max(
                            0,
                            Array.IndexOf(
                                activeOrder,
                                petUid));
                        var formation =
                            PetFormationOffset(
                                slotIndex);
                        var spawnX =
                            Convert.ToDouble(
                                character["LocX"]) +
                            formation.X;
                        var spawnY =
                            Convert.ToDouble(
                                character["LocY"]) +
                            formation.Y;

                        state.PetPositions[petUid] =
                            (spawnX, spawnY);
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
                    state.PetNextIdleSpeechAt.Remove(petUid);
                    state.PetIdleSpeechSequence.Remove(petUid);
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

                _broadcasts.PublishToPlayerObservers(
                    state.ConnectionId,
                    titlePacket);

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

                // Validate the intended mouse drop point BEFORE opening
                // the DB transaction that removes the inventory asset.
                // Never accept a blocked/wall-side client-supplied location.
                if (!_dropPlacement.TryResolve(
                    state.CurrentMapId,
                    state.LastX, state.LastY,
                    req.Int("DropX", (int)state.LastX),
                    req.Int("DropY", (int)state.LastY),
                    state.LastX, state.LastY,
                    out var dropPosition))
                {
                    _log.LogWarning(
                        "[GROUND_DROP_NO_SAFE_TILE] DBID={DBID} Map={MapID} Position=({X:F0},{Y:F0})",
                        state.CharacterId.Value, state.CurrentMapId,
                        state.LastX, state.LastY);
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "附近沒有可放置物品的地面，物品已保留"
                        }
                    ];
                }

                if (!dropPosition.CollisionVerified &&
                    _dropPlacement.ShouldReportMissingMap(state.CurrentMapId))
                {
                    _log.LogWarning(
                        "[GROUND_DROP_MAP_MISSING] Map={MapID} DBID={DBID} Blocking.dat unavailable; legacy placement retained",
                        state.CurrentMapId, state.CharacterId.Value);
                }

                var requestedCount = req.Int("Count", 1);
                if (requestedCount <= 0 ||
                    ((tabType is 1 or 3) && requestedCount != 1))
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "丟棄數量無效，物品已保留"
                        }
                    ];
                }

                var ground = _store.DropAssetToGround(
                    accountId,
                    state.CharacterId.Value,
                    req.Long("ItemUID"),
                    tabType,
                    requestedCount,
                    state.CurrentMapId,
                    dropPosition.X,
                    dropPosition.Y);

                if (ground is null)
                {
                    _diagnostics.DatabaseFailure();
                    _diagnostics.Record(
                        "Warning",
                        "GroundDrop",
                        $"DropAssetToGround rejected UID={req.Long("ItemUID")} TabType={tabType}",
                        state.ConnectionId,
                        state.AccountId,
                        state.CharacterId,
                        state.CurrentMapId);
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

                PublishWorldAoiChanges(
                    _broadcasts.UpsertWorldEntity(
                        new WorldAoiEntity(
                            ground.EntityId,
                            3,
                            ground.MapId,
                            ground.X,
                            ground.Y,
                            () => BuildGroundItemSpawnPacket(
                                ground,
                                isSnapshot: true))),
                    skipObserver: state.ConnectionId);

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
                        // A peer may have received the ground SpawnEntity
                        // after its own WorldReady snapshot. If someone else
                        // already picked it up, the DB is authoritative:
                        // send a local Despawn even though this connection
                        // never had an entry in state.GroundItems. Limit
                        // this recovery to the reserved ground-item range so
                        // forged requests cannot despawn local monsters.
                        state.GroundItems.Remove(entityId);
                        if (entityId >= 6_000_000 &&
                            entityId < 9_000_000)
                        {
                            return
                            [
                                new
                                {
                                    Type = "DespawnEntity",
                                    EntityID = entityId
                                },
                                new
                                {
                                    Type = "SystemMessageFlash",
                                    Message = "物品已不存在"
                                }
                            ];
                        }

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

                var pickupTerrain = _dropPlacement.CheckPickup(
                    state.CurrentMapId,
                    state.LastX, state.LastY,
                    ground.X, ground.Y,
                    canCrossSpecialTerrain: state.CanCrossSpecialTerrain);
                if (pickupTerrain is GroundPickupReachability.TooFar or
                    GroundPickupReachability.Blocked)
                {
                    // Do not call PickUpGroundItem: its DB transaction
                    // transfers ownership. Both distance and LOS must be
                    // validated against server coordinates first.
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = pickupTerrain == GroundPickupReachability.TooFar
                                ? "距離物品太遠"
                                : "地形阻擋，無法撿取物品"
                        }
                    ];
                }

                var (picked, message, _) = _store.PickUpGroundItem(
                    accountId,
                    state.CharacterId.Value,
                    ground.GroundItemId);

                if (!picked)
                {
                    // Only a *terminal* persisted-row failure means the
                    // ground entity is gone. "Protected" and "Too far"
                    // must keep the drop. In particular, a generic
                    // Contains("不存在") incorrectly also matched
                    // "角色不存在" and could hide an existing item.
                    var groundGone = message is
                        "物品已不存在" or "物品已消失";
                    if (groundGone)
                    {
                        _diagnostics.PickupConflict();
                        _diagnostics.Record(
                            "Warning",
                            "PickupConflict",
                            $"EntityID={entityId} GroundItemID={ground.GroundItemId} Result={message}",
                            state.ConnectionId,
                            state.AccountId,
                            state.CharacterId,
                            state.CurrentMapId);

                        // The item may have been picked up by another
                        // connection just before this one. Ensure the stale
                        // local pickup target and every AOI known-set agree
                        // with SQLite, even if the winning socket's
                        // broadcast was missed during map load.
                        state.GroundItems.Remove(entityId);
                        PublishWorldAoiChanges(
                            _broadcasts.RemoveWorldEntity(
                                entityId,
                                notifyObservers: true),
                            skipObserver: state.ConnectionId);
                        return
                        [
                            new
                            {
                                Type = "DespawnEntity",
                                EntityID = entityId
                            },
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message = message
                            }
                        ];
                    }

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

                _broadcasts.PublishToWorldEntityObservers(
                    entityId,
                    despawnPacket,
                    state.ConnectionId);
                _broadcasts.RemoveWorldEntity(
                    entityId,
                    notifyObservers: false);

                var pickedName = _current.FindItem(ground.ItemIndex)?.Name
                    ?? $"Item {ground.ItemIndex}";

                var responses = new List<object>
                {
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
                };

                responses.AddRange(
                    _questProgress.RefreshCollectedItems(
                        accountId,
                        state.CharacterId.Value,
                        ground.ItemIndex));

                return responses;
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
                    var learnedSkills = _store.GetSkills(
                        accountId,
                        state.CharacterId.Value);

                    if (!HasLearnedSkill(
                            learnedSkills,
                            28107) ||
                        !HasLearnedSkill(
                            learnedSkills,
                            28102))
                    {
                        state.AutoSaintEnabled = false;
                        state.LastAutoSaintActionAt =
                            DateTimeOffset.MinValue;

                        return
                        [
                            new
                            {
                                Type = "ToggleAutoSaintResponse",
                                IsOn = false,
                                MpThreshold =
                                    state.AutoSaintMpThreshold,
                                SpThreshold =
                                    state.AutoSaintSpThreshold
                            },
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message =
                                    "自動聖靈需要先學會治療風暴與心靈轉換"
                            }
                        ];
                    }

                    state.AutoSaintEnabled = true;
                    state.LastAutoSaintActionAt =
                        DateTimeOffset.MinValue;
                    state.PetTargets.Clear();

                    foreach (var monster in
                             state.Monsters.Values)
                    {
                        if (monster.TargetCharacterId !=
                            state.CharacterId.Value)
                            continue;

                        monster.HasTarget = false;
                        monster.TargetCharacterId = 0;
                    }
                }
                else
                {
                    state.AutoSaintEnabled = false;
                    state.LastAutoSaintActionAt =
                        DateTimeOffset.MinValue;
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
                    },
                    new
                    {
                        Type = "SystemMessageFlash",
                        Message = state.AutoSaintEnabled
                            ? "自動聖靈已啟動"
                            : "自動聖靈已關閉"
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
                        req.Long("UID"),
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

                var horseUid = req.Long("HorseUID");
                var itemUid = req.Long("ItemUID");
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

                var horseBefore = assetsBefore.Horses
                    .FirstOrDefault(x =>
                        Convert.ToInt64(x["HorseUID"]) == horseUid);

                if (horseBefore is null)
                    return [new { Type = "SystemMessageFlash", Message = "找不到機甲" }];

                var horseIndex = Convert.ToInt32(
                    horseBefore["HorseIdx"]);
                var maxEnergy =
                    _horseCards.Find(horseIndex)?.MaxBattery
                    ?? int.MaxValue;

                var horse = _store.ChargeHorse(
                    accountId,
                    state.CharacterId.Value,
                    horseUid,
                    itemUid,
                    energy,
                    maxEnergy);

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
                    BuildAndBroadcastItemEffect(
                        state,
                        itemIndex),
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

                var skillId = req.Int("SkillID");
                var targetId = req.Int("TargetID");
                var profileStarted = Stopwatch.GetTimestamp();

                var ensureStarted = Stopwatch.GetTimestamp();
                var packets = EnsureCurrentCombatMapLoaded(
                    state,
                    accountId);
                var ensureMapMs =
                    Stopwatch.GetElapsedTime(ensureStarted)
                        .TotalMilliseconds;

                var combatStarted = Stopwatch.GetTimestamp();
                packets.AddRange(_combat.UseSkill(
                    state,
                    accountId,
                    skillId,
                    targetId,
                    req.Int("TargetType", 1),
                    req.Int("TargetX"),
                    req.Int("TargetY"),
                    DateTimeOffset.UtcNow));
                var combatMs =
                    Stopwatch.GetElapsedTime(combatStarted)
                        .TotalMilliseconds;
                var totalMs =
                    Stopwatch.GetElapsedTime(profileStarted)
                        .TotalMilliseconds;

                if (totalMs >= 100.0)
                {
                    _log.LogWarning(
                        "[USE_SKILL_PROFILE] TotalMs={TotalMs:F1} EnsureMapMs={EnsureMapMs:F1} CombatMs={CombatMs:F1} SkillID={SkillID} TargetID={TargetID} Map={MapID} Character={CharacterID}",
                        totalMs,
                        ensureMapMs,
                        combatMs,
                        skillId,
                        targetId,
                        state.CurrentMapId,
                        state.CharacterId);
                }

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
                    var stateDir = req.Str("Dir", "");
                    if (stateDir is not (
                        "1" or "2" or "3" or "4" or
                        "5" or "6" or "7" or "8"))
                    {
                        // SyncState is presentation state, not a license to
                        // invent a facing. Legacy/test clients may omit Dir.
                        stateDir = "";
                    }
                    state.IsResting = playerState.Equals(
                        "crouch",
                        StringComparison.OrdinalIgnoreCase);

                    // Persist cosmetic movement/idle state through the
                    // existing position writer. A 250-450ms SQLite busy
                    // wait must not hold the packet's Session lock while
                    // the shared monster AI needs to inspect this player.
                    // Standalone smoke tests without the worker retain
                    // the synchronous legacy persistence contract.
                    if (_positionWrites is not null)
                        _positionWrites.StageState(
                            accountId, state.CharacterId.Value, playerState);
                    else
                        _store.UpdateState(
                            accountId, state.CharacterId.Value, playerState);

                    // The local Client sends its final SyncPosition BEFORE
                    // SyncState(idle). A valid final position can fall inside
                    // the 100ms continuous-movement broadcast throttle.
                    // Publish that unsent endpoint exactly once before idle,
                    // otherwise observers stop at an older position and later
                    // rubber-band when combat/another movement corrects it.
                    // No extra packets are emitted for an already published
                    // endpoint, so standing players and repeated idle states
                    // cannot create a movement broadcast loop.
                    if (state.WorldReady &&
                        playerState.Equals("idle",
                            StringComparison.OrdinalIgnoreCase) &&
                        state.PositionDirty &&
                        (state.LastBroadcastPositionX != state.LastX ||
                         state.LastBroadcastPositionY != state.LastY))
                    {
                        state.LastBroadcastPositionX = state.LastX;
                        state.LastBroadcastPositionY = state.LastY;
                        state.LastPositionBroadcastAt = DateTimeOffset.UtcNow;
                        _broadcasts.PublishToPlayerObservers(
                            state.ConnectionId,
                            new
                            {
                                Type = "MoveEntity",
                                EntityID = state.CharacterId.Value,
                                X = state.LastX,
                                Y = state.LastY,
                                Dir = stateDir,
                                SpeedTime = 0.0
                            });
                    }

                    _broadcasts.PublishToPlayerObservers(
                        state.ConnectionId,
                        new
                        {
                            Type = "SyncState",
                            EntityID = state.CharacterId.Value,
                            State = playerState,
                            Dir = stateDir
                        });
                }
                return [];

            case "InteractRequest":
                return HandleInteract(req, state, accountId);

            case "NpcOptionRequest":
                return HandleNpcOption(req, state, accountId);

            case "ChatMessage":
            {
                if (!state.CharacterId.HasValue)
                    return [];

                var selfDbid = state.CharacterId.Value;
                var chatStarted = Stopwatch.GetTimestamp();
                var identityLoadMs = 0.0;
                var clubCommandMs = 0.0;
                var broadcastMs = 0.0;
                var neededIdentityFallback = false;
                var message = req.Str("Message").Trim();
                if (message.Length == 0)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "聊天內容不可為空白"
                        }
                    ];
                }

                if (message.Length > 200)
                    message = message[..200];

                // This identity came from the authorized EnterWorld load.
                // Ordinary chat should never open SQLite or hold the
                // player's packet lock while a writer is busy. Existing
                // standalone smoke tests may populate only CharacterId,
                // so keep the DB lookup as a compatibility fallback.
                var senderName = state.CharacterName;
                var senderRace = state.CharacterRace;
                if (string.IsNullOrWhiteSpace(senderName))
                {
                    neededIdentityFallback = true;
                    var identityStarted = Stopwatch.GetTimestamp();
                    var currentCharacter = _store.GetCharacter(
                        accountId, selfDbid);
                    if (currentCharacter is null)
                        return [];
                    senderName = currentCharacter["NAME"]?.ToString() ?? "";
                    senderRace = Convert.ToInt32(currentCharacter["Race"]);
                    identityLoadMs = Stopwatch.GetElapsedTime(
                        identityStarted).TotalMilliseconds;
                }

                if (message.StartsWith(
                        "/gm",
                        StringComparison.OrdinalIgnoreCase))
                {
                    // The load probe uses /gm map and /gm spawn during
                    // setup, and these may perform SQLite mutations.
                    // Classify their cost separately from actual ChatMessage
                    // traffic so a slow GM setup is not misdiagnosed as
                    // normal 20-player chat latency.
                    var gmStarted = Stopwatch.GetTimestamp();
                    var gmPackets = _gm.Execute(message, state);
                    var gmMs = Stopwatch.GetElapsedTime(
                        gmStarted).TotalMilliseconds;
                    var gmNow = DateTimeOffset.UtcNow;
                    if (gmMs >= 250 &&
                        gmNow - state.LastSlowChatDiagAt >=
                            TimeSpan.FromSeconds(5))
                    {
                        state.LastSlowChatDiagAt = gmNow;
                        var tokens = message.Split(' ',
                            3,
                            StringSplitOptions.RemoveEmptyEntries);
                        var gmVerb = tokens.Length >= 2
                            ? tokens[1].ToLowerInvariant()
                            : "<missing>";
                        // Never print command arguments (which can contain
                        // player data); only the command class and elapsed.
                        _log.LogWarning(
                            "[SLOW_GM_COMMAND] Command={Command} ElapsedMs={ElapsedMs:F1} Character={Character}",
                            gmVerb, gmMs, selfDbid);
                    }
                    return gmPackets;
                }

                if (message.StartsWith(
                        "/name ",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var parts = message.Split(
                        ' ',
                        3,
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries);

                    if (parts.Length < 3 ||
                        string.IsNullOrWhiteSpace(parts[1]) ||
                        string.IsNullOrWhiteSpace(parts[2]))
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message =
                                    "密語用法：/name 玩家名 訊息"
                            }
                        ];
                    }

                    var targetName = parts[1];
                    var privateText = parts[2];
                    OnlinePlayerPresence? target = null;

                    // Private /name is a rare explicit lookup. Preserve
                    // the original authoritative DB-name matching contract;
                    // only ordinary nearby/world chat uses the cache fast path.
                    foreach (var presence in
                             _broadcasts.OnlinePlayers(
                                 state.ConnectionId))
                    {
                        var targetCharacter =
                            _store.GetCharacter(
                                presence.AccountId,
                                presence.CharacterId);

                        if (targetCharacter is null ||
                            !targetCharacter.TryGetValue(
                                "NAME",
                                out var rawTargetName))
                            continue;

                        if (!string.Equals(
                                rawTargetName?.ToString(),
                                targetName,
                                StringComparison.OrdinalIgnoreCase))
                            continue;

                        target = presence;
                        break;
                    }

                    if (target is null)
                    {
                        return
                        [
                            new
                            {
                                Type = "SystemMessageFlash",
                                Message =
                                    $"找不到在線玩家：{targetName}"
                            }
                        ];
                    }

                    var privatePacket = new
                    {
                        Type = "ChatBroadcast",
                        EntityID = selfDbid,
                        SenderName = senderName,
                        Race = senderRace,
                        Channel = 4,
                        Message = privateText,
                        BubbleType = 0
                    };

                    _broadcasts.PublishToConnection(
                        target.ConnectionId,
                        privatePacket);

                    return [privatePacket];
                }

                var clubStarted = Stopwatch.GetTimestamp();
                var clubCommand =
                    _club.TryHandleChatCommand(
                        accountId,
                        selfDbid,
                        message);
                clubCommandMs = Stopwatch.GetElapsedTime(
                    clubStarted).TotalMilliseconds;

                if (clubCommand is not null)
                    return clubCommand;

                var channel = req.Int("Channel", 0);
                if (channel is < 0 or > 3)
                {
                    return
                    [
                        new
                        {
                            Type = "SystemMessageFlash",
                            Message = "不支援的聊天頻道"
                        }
                    ];
                }

                var bubbleType = Math.Clamp(
                    req.Int("BubbleType", 0),
                    0,
                    5);

                var chatPacket = new
                {
                    Type = "ChatBroadcast",
                    EntityID = selfDbid,
                    SenderName = senderName,
                    Race = senderRace,
                    Channel = channel,
                    Message = message,
                    BubbleType = bubbleType
                };

                var broadcastStarted = Stopwatch.GetTimestamp();
                switch (channel)
                {
                    // Nearby chat is a visual proximity event. The
                    // sender gets its own response; distant map peers do
                    // not need to receive or decode its chat bubble.
                    case 0:
                        _broadcasts.PublishToPlayerObservers(
                            state.ConnectionId,
                            chatPacket);
                        break;

                    // Faction/world channel in the recovered UI has no
                    // separate faction identifier on the wire. Broadcast to
                    // all authenticated online characters, across maps.
                    case 1:
                        _broadcasts.PublishToAllExcept(
                            state.ConnectionId,
                            chatPacket);
                        break;

                    // Club: persistence is authoritative for membership;
                    // only currently-online club members receive the packet.
                    case 2:
                    {
                        var club = _store.GetClubSnapshot(
                            accountId,
                            selfDbid,
                            DateTimeOffset.UtcNow
                                .ToUnixTimeMilliseconds());

                        if (club is null)
                        {
                            return
                            [
                                new
                                {
                                    Type = "SystemMessageFlash",
                                    Message = "你尚未加入戰團"
                                }
                            ];
                        }

                        foreach (var member in club.Members)
                        {
                            if (member.DbId == selfDbid)
                                continue;

                            _broadcasts.PublishToCharacter(
                                member.DbId,
                                chatPacket);
                        }

                        break;
                    }

                    // Party: PartyService owns the current in-memory party
                    // membership and is already used by combat sharing.
                    case 3:
                    {
                        if (string.IsNullOrWhiteSpace(
                                _party.GetPartyId(selfDbid)))
                        {
                            return
                            [
                                new
                                {
                                    Type = "SystemMessageFlash",
                                    Message = "你尚未加入隊伍"
                                }
                            ];
                        }

                        foreach (var member in
                                 _party.GetMembers(selfDbid))
                        {
                            if (member.CharacterId == selfDbid)
                                continue;

                            _broadcasts.PublishToCharacter(
                                member.CharacterId,
                                chatPacket);
                        }

                        break;
                    }
                }

                broadcastMs = Stopwatch.GetElapsedTime(
                    broadcastStarted).TotalMilliseconds;
                var chatElapsed = Stopwatch.GetElapsedTime(
                    chatStarted).TotalMilliseconds;
                var diagnosedAt = DateTimeOffset.UtcNow;
                if (chatElapsed >= 250 &&
                    diagnosedAt - state.LastSlowChatDiagAt >=
                        TimeSpan.FromSeconds(5))
                {
                    state.LastSlowChatDiagAt = diagnosedAt;
                    _log.LogWarning(
                        "[SLOW_CHAT_PHASE] TotalMs={TotalMs:F1} IdentityFallback={IdentityFallback} IdentityMs={IdentityMs:F1} ClubCommandMs={ClubCommandMs:F1} PublishMs={PublishMs:F1} Channel={Channel} Character={Character}",
                        chatElapsed, neededIdentityFallback, identityLoadMs,
                        clubCommandMs, broadcastMs, channel, selfDbid);
                }

                return [chatPacket];
            }

            case "ReviveRequest":
            {
                if (!state.CharacterId.HasValue) return [];
                _positionWrites?.FenceAndDiscard(
                    accountId, state.CharacterId.Value);
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
                state.WorldReady = false;
                state.Monsters.Clear();

                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                return
                [
                    BuildAndBroadcastEffectiveStatusPacket(state, 
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
                DisconnectPresence(state);
                state.AccountId = null;
                state.CharacterId = null;
                state.CharacterName = "";
                state.CharacterRace = 0;
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
                state.LastAutoSaintActionAt =
                    DateTimeOffset.MinValue;
                ClearPendingTrainTravel(state);
                state.CloseRequested = true;

                return [new
                {
                    Type = "LogoutResponse",
                    IsBackToLogin =
                        req.TryGetProperty("IsBackToLogin", out var back) &&
                        back.ValueKind == JsonValueKind.True
                }];

            default:
                // Do not log request JSON; account/password actions can
                // carry secrets even when Type is unsupported.
                _diagnostics.PacketError();
                _diagnostics.Record(
                    "Warning",
                    "UnhandledPacket",
                    type,
                    state.ConnectionId,
                    state.AccountId,
                    state.CharacterId,
                    state.CurrentMapId);
                _log.LogWarning(
                    "[UNHANDLED_PACKET] Type={Type} Connection={ConnectionID} Account={AccountID} Character={CharacterID} Map={MapID}",
                    type,
                    state.ConnectionId,
                    state.AccountId,
                    state.CharacterId,
                    state.CurrentMapId);
                return
                [
                    new
                    {
                        Type = "PacketError",
                        RequestType = type,
                        Code = "UNHANDLED_PACKET",
                        Message = "伺服器不支援此操作"
                    }
                ];
        }
    }

    public IReadOnlyList<object> Tick(
        ClientSession state,
        DateTimeOffset now,
        bool includeResourceRecovery = true)
    {
        var periodicStarted = Stopwatch.GetTimestamp();
        var resourceRecoveryMs = 0.0;
        var companionMs = 0.0;
        var questRefreshMs = 0.0;
        var groundCleanupMs = 0.0;
        var petFollowMs = 0.0;
        if (!state.AccountId.HasValue ||
            !state.CharacterId.HasValue ||
            state.CurrentMapId <= 0)
            return [];

        var accountId = state.AccountId.Value;
        var dbid = state.CharacterId.Value;
        var packets = new List<object>();

        // Server-initiated travel may update persistence outside this
        // connection. Reconcile periodically instead of hitting SQLite on
        // every world tick.
        if (now - state.LastLocationReconcileAt >=
            LocationReconcileInterval)
        {
            state.LastLocationReconcileAt = now;
            var locationReadStarted = Stopwatch.GetTimestamp();
            var persistedLocation = _store.GetCharacterLocation(
                accountId,
                dbid);
            var locationReadMs = Stopwatch.GetElapsedTime(
                locationReadStarted).TotalMilliseconds;
            if (locationReadMs >= 250 &&
                now - state.LastSlowLocationDiagAt >= TimeSpan.FromSeconds(5))
            {
                state.LastSlowLocationDiagAt = now;
                _log.LogWarning(
                    "[SLOW_LOCATION_RECONCILE] ReadMs={ReadMs:F1} Character={Character} Map={Map}",
                    locationReadMs, dbid, state.CurrentMapId);
            }

            if (persistedLocation is { } location)
            {
                var persistedMapId = location.MapId;

                if (persistedMapId > 0 &&
                    persistedMapId != state.CurrentMapId)
                {
                    _positionWrites?.FenceAndDiscard(accountId, dbid);
                    state.CurrentMapId = persistedMapId;
                    state.LastX = location.X;
                    state.LastY = location.Y;
                    state.PositionDirty = false;
                    state.LastPositionPersistAt = now;
                    state.PendingMapSpawn = true;
                    state.WorldReady = false;
                    state.LoadedMonsterMapId = 0;
                    state.WorldSnapshotRetriesRemaining = 0;
                    state.PetTargets.Clear();
                    state.PetOutOfRangeSince.Clear();

                    // Reconcile presence immediately instead of leaving this
                    // connection in the old map AOI until the next 250 ms
                    // transport tick. WorldReady=false keeps it out of the
                    // destination AOI until the new scene is actually ready.
                    SyncPresence(state);
                }
            }
        }

        if (state.ActiveTransformExpiresAt !=
                DateTimeOffset.MinValue &&
            now >= state.ActiveTransformExpiresAt)
        {
            state.ActiveTransformExpiresAt =
                DateTimeOffset.MinValue;

            // GetCharacter removes the expired transform row before
            // rebuilding appearance/stats, so expiry is authoritative even
            // when the client stays connected for the whole hour.
            var characterAfterTransform =
                _store.GetCharacter(accountId, dbid);
            if (characterAfterTransform is not null)
            {
                var assetsAfterTransform =
                    _store.GetCharacterAssets(accountId, dbid);

                packets.Add(
                    BuildAndBroadcastAppearancePacket(
                        state,
                        accountId,
                        dbid,
                        assetsAfterTransform));
                packets.Add(
                    BuildAndBroadcastEffectiveStatusPacket(
                        state,
                        accountId,
                        dbid,
                        assetsAfterTransform));
                packets.Add(
                    BuildTimedEffectSync(
                        accountId,
                        dbid));

                _party.PublishStat(
                    accountId,
                    dbid);
            }
        }

        var companionStarted = Stopwatch.GetTimestamp();
        packets.AddRange(
            TickCompanionRuntime(
                state,
                accountId,
                dbid,
                now));
        companionMs = Stopwatch.GetElapsedTime(companionStarted).TotalMilliseconds;
        if (includeResourceRecovery)
        {
            var recoveryStarted = Stopwatch.GetTimestamp();
            packets.AddRange(_resourceRecovery.Tick(state, now));
            resourceRecoveryMs = Stopwatch.GetElapsedTime(recoveryStarted).TotalMilliseconds;
        }
        packets.AddRange(
            _combat.TickBuffs(state, now));
        packets.AddRange(
            TickAutoSaint(
                state,
                accountId,
                dbid,
                now));
        if (!state.SharedAiManaged)
            packets.AddRange(_combat.TickRespawns(state, now));

        if (now - state.LastPartyStatAt >=
            TimeSpan.FromSeconds(1))
        {
            state.LastPartyStatAt = now;
            _party.PublishStat(
                accountId,
                dbid);
        }

        if (now - state.LastQuestProgressAt >=
            TimeSpan.FromSeconds(2))
        {
            state.LastQuestProgressAt = now;
            var questStarted = Stopwatch.GetTimestamp();
            packets.AddRange(
                _questProgress.RefreshCollectedItems(
                    accountId,
                    dbid));
            questRefreshMs = Stopwatch.GetElapsedTime(questStarted).TotalMilliseconds;
        }

        if (now - state.LastGroundItemCleanupAt >=
            TimeSpan.FromSeconds(1))
        {
            var cleanupStarted = Stopwatch.GetTimestamp();
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

                var visibleToSelf =
                    _broadcasts.IsWorldAoiVisible(
                        state.ConnectionId,
                        entityId);

                _broadcasts.PublishToWorldEntityObservers(
                    entityId,
                    despawnPacket,
                    state.ConnectionId);
                _broadcasts.RemoveWorldEntity(
                    entityId,
                    notifyObservers: false);

                if (visibleToSelf)
                    packets.Add(despawnPacket);
            }
            groundCleanupMs = Stopwatch.GetElapsedTime(cleanupStarted).TotalMilliseconds;
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
            var petStarted = Stopwatch.GetTimestamp();
            packets.AddRange(
                BuildPetFollowPackets(
                    state,
                    accountId,
                    dbid,
                    state.LastX,
                    state.LastY,
                    now));
            petFollowMs = Stopwatch.GetElapsedTime(petStarted).TotalMilliseconds;
            // Standalone smoke sessions retain their legacy local tick.
            // Live WebSockets use SharedMapAiTicker once per active map.
            if (!state.SharedAiManaged)
                packets.AddRange(_combat.TickAi(
                    state,
                    accountId,
                    state.LastX,
                    state.LastY,
                    now));
        }

        var periodicMs = Stopwatch.GetElapsedTime(periodicStarted).TotalMilliseconds;
        if (periodicMs >= 200.0 &&
            now - state.LastSlowPeriodicTickDiagAt >= TimeSpan.FromSeconds(5))
        {
            state.LastSlowPeriodicTickDiagAt = now;
            _log.LogWarning(
                "[SLOW_PERIODIC_TICK_PHASE] TotalMs={TotalMs:F1} CompanionMs={CompanionMs:F1} RecoveryMs={RecoveryMs:F1} QuestMs={QuestMs:F1} GroundMs={GroundMs:F1} PetFollowMs={PetFollowMs:F1} OtherMs={OtherMs:F1} Map={Map} Character={Character}",
                periodicMs, companionMs, resourceRecoveryMs, questRefreshMs,
                groundCleanupMs, petFollowMs,
                Math.Max(0.0, periodicMs - companionMs - resourceRecoveryMs - questRefreshMs - groundCleanupMs - petFollowMs),
                state.CurrentMapId, dbid);
        }
        return packets;
    }

    private IReadOnlyList<object> TickCompanionRuntime(
        ClientSession state,
        long accountId,
        long dbid,
        DateTimeOffset now)
    {
        if (state.LastCompanionRuntimeAt ==
            DateTimeOffset.MinValue)
        {
            state.LastCompanionRuntimeAt = now;
            state.LastCompanionClientSyncAt = now;
            return [];
        }

        var elapsed = (int)Math.Floor(
            (now - state.LastCompanionRuntimeAt)
                .TotalSeconds);

        if (elapsed <= 0)
            return [];

        // Consume only whole seconds and preserve the fractional remainder.
        // A bounded catch-up protects against an unexpected system-clock jump;
        // subsequent ticks continue draining until caught up.
        elapsed = Math.Min(elapsed, 300);
        state.LastCompanionRuntimeAt =
            state.LastCompanionRuntimeAt
                .AddSeconds(elapsed);

        var drained = _store.DrainCompanionRuntime(
            accountId,
            dbid,
            elapsed);

        var transition =
            drained.MechaDismounted ||
            drained.RecalledPetUids.Count > 0;

        var shouldCorrectClient =
            transition ||
            now - state.LastCompanionClientSyncAt >=
                TimeSpan.FromSeconds(10);

        if (!shouldCorrectClient)
            return [];

        state.LastCompanionClientSyncAt = now;

        // A transport tick can reach the ten-second reconciliation marker
        // for every one of 20+ online characters at once. With no active
        // pet/horse, recall or dismount, there is nothing to send; fetching
        // their entire inventory/equipment/pet/mecha asset graph under the
        // session lock only blocks movement and the shared AI scheduler.
        // Keep the original one-second drain, ten-second correction clock,
        // and every actual companion transition unchanged.
        if (!transition && drained.Horse is null && drained.Pets.Count == 0)
            return [];

        var packets = new List<object>();
        var assets = _store.GetCharacterAssets(
            accountId,
            dbid);

        if (drained.Horse is not null)
        {
            packets.Add(new
            {
                Type = "SyncMechaEnergy",
                HorseUID = drained.Horse["HorseUID"],
                EnergyLeft = drained.Horse["EnergyLeft"],
                Exp = drained.Horse["Exp"],
                MaxExp = drained.Horse["MaxExp"],
                Level = drained.Horse["Level"]
            });
        }

        if (drained.MechaDismounted)
        {
            packets.Add(new
            {
                Type = "DriveMechaResponse",
                Success = true,
                HorseUID = -1,
                MapID = state.CurrentMapId,
                X = state.LastX,
                Y = state.LastY,
                Message = "機甲能源耗盡，已自動解除駕駛"
            });

            packets.Add(
                BuildAndBroadcastEffectiveStatusPacket(
                    state,
                    accountId,
                    dbid,
                    assets));

            packets.Add(
                BuildAndBroadcastAppearancePacket(
                    state,
                    accountId,
                    dbid,
                    assets));
        }

        if (drained.Pets.Count > 0)
        {
            packets.Add(new
            {
                Type = "PetSync",
                Pets = assets.Pets,
                MaxSize = assets.PetMaxSize
            });
        }

        foreach (var petUid in drained.RecalledPetUids)
        {
            state.PetPositions.Remove(petUid);
            state.PetLastAttackAt.Remove(petUid);
            state.PetTargets.Remove(petUid);
            state.PetOutOfRangeSince.Remove(petUid);
            state.PetNextIdleSpeechAt.Remove(petUid);
            state.PetIdleSpeechSequence.Remove(petUid);

            packets.Add(new
            {
                Type = "DespawnEntity",
                EntityID = 4_000_000L + petUid
            });
        }

        if (drained.RecalledPetUids.Count > 0)
        {
            packets.Add(new
            {
                Type = "SystemMessageFlash",
                Message = "寵物飽食度已耗盡，已自動收回"
            });
        }

        BroadcastMapEntityPackets(
            state,
            packets);

        return packets;
    }

    private IReadOnlyList<object> BuildOtherPlayerSnapshotPackets(
        ClientSession state)
    {
        if (!state.WorldReady || state.CurrentMapId <= 0)
            return [];

        // Perform the first reconcile only after the client has loaded the
        // real Godot map. New pair edges publish SpawnEntity to existing
        // observers; the local client receives the snapshot as the normal
        // response to WorldReady (never an early background queue packet).
        PublishPlayerAoiChanges(
            _broadcasts.ReconcilePlayerAoi(state.ConnectionId),
            skipObserver: state.ConnectionId);

        var packets = new List<object>();
        foreach (var player in _broadcasts.VisiblePlayerPeers(state.ConnectionId))
        {
            var spawn = BuildPlayerSpawnPacket(
                player.AccountId,
                player.CharacterId,
                player.X,
                player.Y);
            if (spawn is null)
                continue;

            packets.Add(spawn);
            packets.AddRange(
                _broadcasts.PlayerCompanionSnapshots(
                    player.ConnectionId));
        }

        return packets;
    }

    private object? BuildPlayerSpawnPacket(
        long accountId,
        long dbid,
        double? runtimeX = null,
        double? runtimeY = null)
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
        ApplyCosmeticOverrides(
            character,
            accountId,
            dbid);
        ApplyEffectiveStats(
            character,
            assets);

        var shopState =
            _playerShop.GetPublicState(dbid);

        var extra = BuildPublicPlayerData(character);
        extra["State"] =
            character.GetValueOrDefault(
                "State",
                "idle");
        extra["Dir"] = "1";
        extra["PartyID"] = _party.GetPartyId(dbid);
        extra["IsShopOpen"] = shopState.IsOpen;
        extra["ShopName"] = shopState.ShopName;
        if (runtimeX.HasValue && runtimeY.HasValue)
        {
            extra["LocX"] = runtimeX.Value;
            extra["LocY"] = runtimeY.Value;
        }

        return new
        {
            Type = "SpawnEntity",
            EntityID = dbid,
            EntityType = 0,
            Name = character["NAME"],
            LocX = runtimeX ?? Convert.ToDouble(character["LocX"]),
            LocY = runtimeY ?? Convert.ToDouble(character["LocY"]),
            ExtraData = extra
        };
    }

    private IReadOnlyList<object> BuildGroundItemSnapshotPackets(
        ClientSession state)
    {
        state.GroundItems.Clear();

        // Capture the AOI index generation BEFORE querying SQLite. The SELECT
        // and AOI purge are not one atomic operation: another player can
        // drop/commit an item after this query starts but before we prune
        // stale index entries. Never purge an entity indexed after the query
        // boundary, even if this snapshot could not yet see its DB row.
        using var worldSnapshot =
            _broadcasts.BeginWorldEntitySnapshot();
        var snapshotGeneration = worldSnapshot.Generation;
        var groundItems =
            _store.GetGroundItems(
                    state.CurrentMapId)
                .ToArray();
        var authoritativeIds =
            groundItems
                .Select(x =>
                    (long)x.EntityId)
                .ToHashSet();

        // GetGroundItems removes expired DB rows. Reconcile the in-memory AOI
        // group before registering the current rows so a map that had no
        // active players cannot resurrect an expired/picked-up ground item
        // when somebody returns later.
        PublishWorldAoiChanges(
            _broadcasts.RemoveMissingWorldEntities(
                state.CurrentMapId,
                3,
                authoritativeIds,
                snapshotGeneration),
            skipObserver: state.ConnectionId);

        foreach (var ground in groundItems)
        {
            var resolvedGround = ground;
            if (_dropPlacement.TryRecoverPersistedDrop(
                    ground.MapId, ground.X, ground.Y,
                    out var corrected))
            {
                // This is an old DB row created before the collision-aware
                // drop policy. Conditional UPDATE prevents concurrent map
                // loads from shuffling it or recreating an already-picked item.
                var latest = _store.RelocateGroundItemIfUnchanged(
                    ground.GroundItemId,
                    ground.MapId,
                    ground.X, ground.Y,
                    corrected.X, corrected.Y);
                if (latest is null)
                    continue; // concurrently picked/expired

                resolvedGround = latest;
                if (resolvedGround.X != ground.X ||
                    resolvedGround.Y != ground.Y)
                {
                    _log.LogInformation(
                        "[GROUND_DROP_LEGACY_RELOCATED] Map={MapId} Entity={EntityId} From=({FromX:F0},{FromY:F0}) To=({ToX:F0},{ToY:F0})",
                        ground.MapId, ground.EntityId,
                        ground.X, ground.Y,
                        resolvedGround.X, resolvedGround.Y);
                }
            }
            else if (_dropPlacement.HasVerifiedBlockedPosition(
                         ground.MapId, ground.X, ground.Y) &&
                     _dropPlacement.ShouldReportUnrepairableMap(
                         ground.MapId))
            {
                _log.LogWarning(
                    "[GROUND_DROP_LEGACY_UNREPAIRED] Map={MapId} Entity={EntityId} Location=({X:F0},{Y:F0}) No legal nearby tile; original DB item preserved",
                    ground.MapId, ground.EntityId, ground.X, ground.Y);
            }

            // A short-lived drop can expire after the SELECT begins but
            // before this queued WorldReady reconstruction reaches the row.
            // Never create a new visual entity with zero remaining lifetime.
            if (resolvedGround.IsExpired(DateTimeOffset.UtcNow))
                continue;

            // The SELECT above may have completed before another socket
            // picked up this row or updated its position. Atomic generation
            // checks against the in-flight snapshot lease prevent a stale
            // row from resurrecting a consumed item or replacing new data.
            // Do not cache a rejected row in this client's pickup targets.
            if (!_broadcasts.TryUpsertWorldEntityFromSnapshot(
                    new WorldAoiEntity(
                        resolvedGround.EntityId,
                        3,
                        resolvedGround.MapId,
                        resolvedGround.X,
                        resolvedGround.Y,
                        () => BuildGroundItemSpawnPacket(
                            resolvedGround,
                            isSnapshot: true)),
                    worldSnapshot,
                    out var changes))
                continue;

            state.GroundItems[resolvedGround.EntityId] = resolvedGround;
            PublishWorldAoiChanges(
                changes, skipObserver: state.ConnectionId);
        }

        // The local WorldReady response gets only entities inside AOI from
        // BuildWorldAoiSnapshotPackets. Distant ground rows stay authoritative
        // in SQLite and the AOI index without consuming client bandwidth.
        return [];
    }

    private object BuildGroundItemSpawnPacket(
        GroundItemRuntime ground,
        bool isSnapshot = false)
    {
        var now = DateTimeOffset.UtcNow;
        return new
        {
            Type = "SpawnEntity",
            EntityID = ground.EntityId,
            EntityType = 3,
            IsSnapshot = isSnapshot,
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

    private static bool TryGetTrainTravelSpec(
        string script,
        string function,
        out TrainTravelSpec spec)
    {
        spec = null!;

        var key =
            $"{Path.GetFileName(script)}:{function}";

        spec = key.ToUpperInvariant() switch
        {
            "NQ024_B.TXT:DIA_04A1" =>
                new TrainTravelSpec(
                    "dia_04a6",
                    "train_depart_king.ogv",
                    "train_depart_magic.ogv"),
            "NQ024_B.TXT:DIA_04B1" =>
                new TrainTravelSpec(
                    "dia_04b6",
                    "train_depart_king.ogv",
                    "train_depart_knight.ogv"),
            "NQ024_B.TXT:DIA_04C1" =>
                new TrainTravelSpec(
                    "dia_04c6",
                    "train_depart_king.ogv",
                    "train_depart_robot.ogv"),
            "NQ024_C.TXT:DIA_04A1" =>
                new TrainTravelSpec(
                    "dia_04a6",
                    "train_depart_magic.ogv",
                    "train_depart_king.ogv"),
            "NQ024_D.TXT:DIA_04A1" =>
                new TrainTravelSpec(
                    "dia_04a6",
                    "train_depart_robot.ogv",
                    "train_depart_king.ogv"),
            "NQ024_E.TXT:DIA_04A1" =>
                new TrainTravelSpec(
                    "dia_04a6",
                    "train_depart_knight.ogv",
                    "train_depart_king.ogv"),
            _ => null!
        };

        return spec is not null;
    }

    private static void ClearPendingTrainTravel(
        ClientSession state)
    {
        state.PendingTrainTravelId = "";
        state.PendingTrainNpcEntityId = 0;
        state.PendingTrainScript = "";
        state.PendingTrainContinuation = "";
        state.PendingTrainExpiresAt =
            DateTimeOffset.MinValue;
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

        var px = state.LastX;
        var py = state.LastY;
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
        Dictionary<string, object> character,
        bool suppressLegacyMediaDialogs = false)
    {
        if (TryGetTrainTravelSpec(
                npc.Script,
                function,
                out var trainSpec))
        {
            var travelId = Guid.NewGuid()
                .ToString("N");

            state.PendingTrainTravelId = travelId;
            state.PendingTrainNpcEntityId = npc.EntityId;
            state.PendingTrainScript = npc.Script;
            state.PendingTrainContinuation =
                trainSpec.Continuation;
            state.PendingTrainExpiresAt =
                DateTimeOffset.UtcNow.AddMinutes(3);

            return
            [
                new
                {
                    Type = "TrainTravelStart",
                    TravelID = travelId,
                    SourceVideo = trainSpec.SourceVideo,
                    DestinationVideo =
                        trainSpec.DestinationVideo
                },
                new
                {
                    Type = "NpcDialogResponse",
                    EntityID = npc.EntityId,
                    ScriptName = npc.Script,
                    DialogType = "Close"
                }
            ];
        }

        try
        {
            var assetsChanged = false;
            var skillsChanged = false;
            var careerChanged = false;
            var appearanceChanged = false;
            var moneyChanged = false;
            var questTrackerPackets =
                new List<object>();
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

                        questTrackerPackets.AddRange(
                            _questProgress.OnLegacyFlagChanged(
                                accountId,
                                dbid,
                                name,
                                value));
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
                    },
                    SetData = (key, value) =>
                    {
                        if (dbid <= 0 ||
                            !key.Equals(
                                "Title",
                                StringComparison.OrdinalIgnoreCase))
                            return false;

                        var success = _store.SetCurrentTitle(
                            accountId,
                            dbid,
                            value);
                        appearanceChanged |= success;
                        return success;
                    },
                    ModifyData = (key, delta) =>
                    {
                        if (dbid <= 0 ||
                            !key.Equals(
                                "Money",
                                StringComparison.OrdinalIgnoreCase))
                            return false;

                        var updatedMoney = _store.AdjustMoney(
                            accountId,
                            dbid,
                            delta);

                        if (updatedMoney is null)
                            return false;

                        character["Money"] =
                            updatedMoney["Money"];
                        moneyChanged = true;
                        return true;
                    },
                    SuppressLegacyMediaDialogs =
                        suppressLegacyMediaDialogs
                });

            result.Packets.AddRange(
                questTrackerPackets);

            result.Packets.AddRange(
                _questProgress.OnLegacyInteractionCompleted(
                    accountId,
                    dbid,
                    npc.Script,
                    function,
                    choice,
                    assetsChanged));

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
                _positionWrites?.FenceAndDiscard(
                    accountId, state.CharacterId.Value);
                _store.UpdateLocation(
                    accountId,
                    state.CharacterId.Value,
                    result.NewMapId.Value,
                    result.NewX.Value,
                    result.NewY.Value);

                state.CurrentMapId = result.NewMapId.Value;
                state.LastX = result.NewX.Value;
                state.LastY = result.NewY.Value;
                state.PositionDirty = false;
                state.LastPositionPersistAt = DateTimeOffset.UtcNow;
                state.PendingMapSpawn = true;
                state.WorldReady = false;
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
                    BuildAndBroadcastEffectiveStatusPacket(state, 
                        accountId,
                        state.CharacterId.Value,
                        assets));
                result.Packets.Add(
                    BuildAndBroadcastAppearancePacket(
                        state,
                        accountId,
                        state.CharacterId.Value,
                        assets));

                result.Packets.AddRange(
                    _questProgress.RefreshCollectedItems(
                        accountId,
                        state.CharacterId.Value));
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
                    BuildAndBroadcastEffectiveStatusPacket(state, 
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
            else if (appearanceChanged &&
                     state.CharacterId.HasValue)
            {
                var assets = _store.GetCharacterAssets(
                    accountId,
                    state.CharacterId.Value);

                result.Packets.Add(
                    BuildAndBroadcastAppearancePacket(
                        state,
                        accountId,
                        state.CharacterId.Value,
                        assets));
            }

            if (moneyChanged &&
                state.CharacterId.HasValue)
            {
                var currentCharacter =
                    _store.GetCharacter(
                        accountId,
                        state.CharacterId.Value);

                if (currentCharacter is not null)
                {
                    result.Packets.Add(new
                    {
                        Type = "SyncMoney",
                        Money = currentCharacter["Money"]
                    });
                }
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

    private object BuildFestivalThemeSync()
    {
        var theme = (
            _configuration?["Gameplay:Festival:Theme"] ??
            "normal")
            .Trim()
            .ToLowerInvariant();

        if (theme is not (
                "normal" or
                "mid_autumn" or
                "easter" or
                "halloween" or
                "christmas" or
                "lunar_new_year"))
        {
            theme = "normal";
        }

        var festival = (
            _configuration?["Gameplay:Festival:Activity"] ??
            "")
            .Trim()
            .ToLowerInvariant();

        if (festival is not (
                "" or
                "lunar_new_year" or
                "valentine" or
                "white_valentine" or
                "easter" or
                "dragon_boat" or
                "grand_hunt" or
                "mid_autumn" or
                "halloween" or
                "christmas"))
        {
            festival = "";
        }

        return new
        {
            Type = "FestivalThemeSync",
            Theme = theme,
            Festival = festival
        };
    }

    private object BuildTimedEffectSync(
        long accountId,
        long dbid)
        => new
        {
            Type = "SyncBuffs",
            Buffs = _combat.GetCombinedBuffEntries(
                accountId,
                dbid,
                DateTimeOffset.UtcNow)
        };

    private IReadOnlyList<object> TickAutoSaint(
        ClientSession state,
        long accountId,
        long dbid,
        DateTimeOffset now)
    {
        if (!state.AutoSaintEnabled ||
            !state.WorldReady ||
            now - state.LastAutoSaintActionAt <
                AutoSaintActionInterval)
        {
            return [];
        }

        state.LastAutoSaintActionAt = now;

        var learnedSkills = _store.GetSkills(
            accountId,
            dbid);

        var healLevel = GetLearnedSkillLevel(
            learnedSkills,
            28107);
        var convertLevel = GetLearnedSkillLevel(
            learnedSkills,
            28102);

        if (healLevel <= 0 ||
            convertLevel <= 0)
        {
            state.AutoSaintEnabled = false;
            return
            [
                new
                {
                    Type = "ToggleAutoSaintResponse",
                    IsOn = false,
                    MpThreshold =
                        state.AutoSaintMpThreshold,
                    SpThreshold =
                        state.AutoSaintSpThreshold
                },
                new
                {
                    Type = "SystemMessageFlash",
                    Message =
                        "自動聖靈已停止：治療風暴或心靈轉換不可用"
                }
            ];
        }

        var self = _store.GetCharacter(
            accountId,
            dbid);
        if (self is null ||
            Convert.ToInt32(self["HP"]) <= 0)
        {
            return [];
        }

        var healData = _current.FindSkillLevel(
            28107,
            healLevel);
        var convertData = _current.FindSkillLevel(
            28102,
            convertLevel);

        if (healData is null ||
            convertData is null)
        {
            state.AutoSaintEnabled = false;
            return
            [
                new
                {
                    Type = "ToggleAutoSaintResponse",
                    IsOn = false,
                    MpThreshold =
                        state.AutoSaintMpThreshold,
                    SpThreshold =
                        state.AutoSaintSpThreshold
                },
                new
                {
                    Type = "SystemMessageFlash",
                    Message =
                        "自動聖靈已停止：技能等級資料不存在"
                }
            ];
        }

        var partyMembers = _party.GetMembers(dbid);
        if (partyMembers.Count == 0)
        {
            partyMembers =
            [
                (accountId, dbid)
            ];
        }

        var nearby = new List<(
            long AccountId,
            long CharacterId,
            Dictionary<string, object> Character,
            double DistanceSquared)>();

        foreach (var member in partyMembers)
        {
            double distanceSquared;

            if (member.CharacterId == dbid)
            {
                distanceSquared = 0;
            }
            else
            {
                var presence = _broadcasts.FindCharacter(
                    member.CharacterId);

                if (presence is null ||
                    presence.MapId != state.CurrentMapId)
                {
                    continue;
                }

                var dx = presence.X - state.LastX;
                var dy = presence.Y - state.LastY;
                distanceSquared = dx * dx + dy * dy;
            }

            var character = _store.GetCharacter(
                member.AccountId,
                member.CharacterId);

            if (character is null ||
                Convert.ToInt32(character["HP"]) <= 0)
            {
                continue;
            }

            nearby.Add((
                member.AccountId,
                member.CharacterId,
                character,
                distanceSquared));
        }

        if (nearby.Count == 0)
            return [];

        static double Percent(
            int current,
            int maximum)
            => maximum <= 0
                ? 100.0
                : current * 100.0 / maximum;

        var recoveryThreshold =
            state.AutoSaintMpThreshold;
        var groupRadiusSquared =
            400.0 * 400.0;

        var needsGroupRecovery = nearby.Any(x =>
            x.DistanceSquared <= groupRadiusSquared &&
            (Percent(
                 Convert.ToInt32(x.Character["HP"]),
                 Convert.ToInt32(x.Character["MaxHP"])) <=
                 recoveryThreshold ||
             Percent(
                 Convert.ToInt32(x.Character["MP"]),
                 Convert.ToInt32(x.Character["MaxMP"])) <=
                 recoveryThreshold));

        var selfSp = Convert.ToInt32(self["SP"]);
        var selfMp = Convert.ToInt32(self["MP"]);

        var healReady =
            !state.SkillReadyAt.TryGetValue(
                28107,
                out var healReadyAt) ||
            now >= healReadyAt;

        if (needsGroupRecovery &&
            healReady &&
            selfMp >= healData.CostMp &&
            selfSp >= healData.CostSp &&
            Convert.ToInt32(self["HP"]) >=
                healData.CostHp)
        {
            return _combat.UseSkill(
                state,
                accountId,
                28107,
                checked((int)dbid),
                0,
                checked((int)Math.Round(state.LastX)),
                checked((int)Math.Round(state.LastY)),
                now);
        }

        var convertRange =
            Math.Max(0, convertData.CastRange) + 120.0;
        var convertRangeSquared =
            convertRange * convertRange;

        var spTarget = nearby
            .Where(x =>
                x.DistanceSquared <= convertRangeSquared &&
                Percent(
                    Convert.ToInt32(x.Character["SP"]),
                    Convert.ToInt32(x.Character["MaxSP"])) <=
                    state.AutoSaintSpThreshold)
            .OrderBy(x => Percent(
                Convert.ToInt32(x.Character["SP"]),
                Convert.ToInt32(x.Character["MaxSP"])))
            .FirstOrDefault();

        if (spTarget.Character is null)
            return [];

        var convertReady =
            !state.SkillReadyAt.TryGetValue(
                28102,
                out var convertReadyAt) ||
            now >= convertReadyAt;

        if (!convertReady ||
            selfMp < convertData.CostMp ||
            selfSp < convertData.CostSp ||
            Convert.ToInt32(self["HP"]) <
                convertData.CostHp)
        {
            return [];
        }

        return _combat.UseSkill(
            state,
            accountId,
            28102,
            checked((int)spTarget.CharacterId),
            0,
            checked((int)Math.Round(state.LastX)),
            checked((int)Math.Round(state.LastY)),
            now);
    }

    private static bool HasLearnedSkill(
        IReadOnlyList<Dictionary<string, object>> skills,
        int skillId)
        => GetLearnedSkillLevel(
            skills,
            skillId) > 0;

    private static int GetLearnedSkillLevel(
        IReadOnlyList<Dictionary<string, object>> skills,
        int skillId)
    {
        foreach (var skill in skills)
        {
            var id = skill.TryGetValue(
                    "SkillID",
                    out var upperId)
                ? Convert.ToInt32(upperId)
                : skill.TryGetValue(
                    "skill_id",
                    out var lowerId)
                    ? Convert.ToInt32(lowerId)
                    : 0;

            if (id != skillId)
                continue;

            return Math.Max(
                1,
                skill.TryGetValue(
                    "Level",
                    out var upperLevel)
                    ? Convert.ToInt32(upperLevel)
                    : skill.TryGetValue(
                        "level",
                        out var lowerLevel)
                        ? Convert.ToInt32(lowerLevel)
                        : skill.TryGetValue(
                            "SkillLevel",
                            out var clientLevel)
                            ? Convert.ToInt32(clientLevel)
                            : 1);
        }

        return 0;
    }

    private static bool ResolveSpecialTerrainCapability(
        Dictionary<string, object> character,
        CharacterAssets assets)
    {
        var summonedHorseUid = Convert.ToInt64(
            character.GetValueOrDefault(
                "SummonedHorseUID",
                -1L));

        if (summonedHorseUid <= 0)
            return false;

        foreach (var equip in assets.HorseEquips)
        {
            if (Convert.ToInt64(equip["HorseUID"]) != summonedHorseUid)
                continue;

            if (Convert.ToInt32(equip["Part"]) != 0)
                continue;

            var equipIndex = Convert.ToInt32(
                equip["EquipIdx"]);

            if ((equipIndex >= 50311 && equipIndex <= 50318) ||
                equipIndex == 50344 ||
                (equipIndex >= 50359 && equipIndex <= 50361))
            {
                return true;
            }
        }

        return false;
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
        character["PetSummonMax"] = assets.PetSummonMax;
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
        var activePets = assets.Pets
            .Where(x =>
                Convert.ToInt32(x["State"]) == 1)
            .OrderBy(x =>
                Convert.ToInt64(x["UID"]))
            .ToArray();

        for (var i = 0; i < activePets.Length; i++)
        {
            var pet = activePets[i];
            var petUid = Convert.ToInt64(pet["UID"]);
            var formation = PetFormationOffset(i);
            var spawnX = playerX + formation.X;
            var spawnY = playerY + formation.Y;

            state.PetPositions[petUid] = (spawnX, spawnY);
            state.PetLastAttackAt.Remove(petUid);

            var spawnPacket =
                BuildPetSpawnPacket(
                    dbid,
                    pet,
                    spawnX,
                    spawnY);
            var firstTracked =
                _broadcasts.TrackPlayerCompanionPacket(
                    state.ConnectionId,
                    spawnPacket);

            packets.Add(spawnPacket);

            if (firstTracked)
            {
                _broadcasts.PublishToPlayerObservers(
                    state.ConnectionId,
                    spawnPacket);
            }
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
        var activePets = assets.Pets
            .Where(x =>
                Convert.ToInt32(x["State"]) == 1)
            .OrderBy(x =>
                Convert.ToInt64(x["UID"]))
            .ToArray();

        for (var i = 0; i < activePets.Length; i++)
        {
            var pet = activePets[i];
            var petUid = Convert.ToInt64(pet["UID"]);

            if (!state.PetPositions.TryGetValue(
                    petUid,
                    out var position))
            {
                var formation = PetFormationOffset(i);
                position = (
                    X: playerX + formation.X,
                    Y: playerY + formation.Y);
                state.PetPositions[petUid] = position;
            }

            var spawnPacket =
                BuildPetSpawnPacket(
                    dbid,
                    pet,
                    position.X,
                    position.Y);
            var firstTracked =
                _broadcasts.TrackPlayerCompanionPacket(
                    state.ConnectionId,
                    spawnPacket);

            packets.Add(spawnPacket);

            if (firstTracked)
            {
                _broadcasts.PublishToPlayerObservers(
                    state.ConnectionId,
                    spawnPacket);
            }
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

        // Keep 250ms pet movement and attack semantics intact, but avoid
        // loading inventory, all equipment, every stored pet and the full
        // character on every online player's world tick.
        var activePets = _store.GetActivePetFollowSnapshot(accountId, dbid).ToArray();
        if (activePets.Length == 0)
            return [];

        var hp = _store.GetCharacterHp(accountId, dbid);
        var playerAlive = hp.HasValue && hp.Value > 0;
        var packets = new List<object>();

        for (var petIndex = 0;
             petIndex < activePets.Length;
             petIndex++)
        {
            var pet = activePets[petIndex];
            var formation =
                PetFormationOffset(petIndex);
            var petUid = Convert.ToInt64(pet["UID"]);
            var petEntityId = 4_000_000L + petUid;
            var petSpeechSequence =
                state.PetIdleSpeechSequence.GetValueOrDefault(
                    petUid,
                    0);

            if (!state.PetNextIdleSpeechAt.ContainsKey(petUid))
            {
                state.PetNextIdleSpeechAt[petUid] =
                    now + NextPetIdleSpeechDelay(
                        petUid,
                        petSpeechSequence);
            }

            if (!state.PetPositions.TryGetValue(
                    petUid,
                    out var petPosition))
            {
                petPosition = (
                    playerX + formation.X,
                    playerY + formation.Y);
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
            var ownerFollowX =
                playerX + formation.X;
            var ownerFollowY =
                playerY + formation.Y;
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

            var followX =
                playerX + formation.X;
            var followY =
                playerY + formation.Y;
            var followDx = followX - petPosition.X;
            var followDy = followY - petPosition.Y;
            var followDistance = Math.Sqrt(
                followDx * followDx +
                followDy * followDy);

            if (followDistance <= 10.0)
            {
                if (playerAlive &&
                    now >= state.PetNextIdleSpeechAt[petUid])
                {
                    petSpeechSequence++;
                    state.PetIdleSpeechSequence[petUid] =
                        petSpeechSequence;
                    state.PetNextIdleSpeechAt[petUid] =
                        now + NextPetIdleSpeechDelay(
                            petUid,
                            petSpeechSequence);

                    var speech = _petIdleSpeech.Pick(
                        Convert.ToInt32(pet["PETID"]),
                        petUid,
                        petSpeechSequence);

                    if (!string.IsNullOrWhiteSpace(speech))
                    {
                        packets.Add(new
                        {
                            Type = "PetIdleSpeech",
                            EntityID = petEntityId,
                            Message = speech
                        });
                    }
                }

                continue;
            }

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

    private static (double X, double Y) PetFormationOffset(
        int index)
        => index switch
        {
            0 => (42.0, 24.0),
            1 => (-42.0, 24.0),
            2 => (0.0, -52.0),
            _ => (42.0, 24.0)
        };

    private static TimeSpan NextPetIdleSpeechDelay(
        long petUid,
        int sequence)
    {
        var seed = unchecked(
            petUid * 1103515245L +
            (long)sequence * 12345L +
            0x5F3759DFL);

        var seconds =
            45 + (int)((ulong)seed % 46UL);

        return TimeSpan.FromSeconds(seconds);
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
                    "MoveEntity" or
                    "PetIdleSpeech"))
                continue;

            _broadcasts.PublishToPlayerObservers(
                state.ConnectionId, packet);
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

        _broadcasts.PublishToPlayerObservers(
            state.ConnectionId, packet);

        return packet;
    }

    private object BuildAndBroadcastItemEffect(
        ClientSession state,
        int itemIndex)
    {
        var packet = new
        {
            Type = "PlayItemEffect",
            EntityID = state.CharacterId ?? 0,
            ItemIndex = itemIndex
        };

        if (state.CurrentMapId > 0 &&
            state.CharacterId.HasValue)
        {
            _broadcasts.PublishToPlayerObservers(
                state.ConnectionId, packet);
        }

        return packet;
    }

    private object BuildAndBroadcastEffectiveStatusPacket(
        ClientSession state,
        long accountId,
        long dbid,
        SGO.Server.Domain.Assets.CharacterAssets assets)
    {
        var character = _store.GetCharacter(accountId, dbid);
        if (character is null)
            return new { Type = "PlayerStatusSync" };

        var stats = CharacterStatCalculator.Calculate(
            character,
            assets,
            _current);

        if (state.CurrentMapId > 0 &&
            state.CharacterId == dbid)
        {
            _broadcasts.PublishToPlayerObservers(
                state.ConnectionId,
                new
                {
                    Type = "SyncStatus",
                    EntityID = dbid,
                    Level = character["Level"],
                    HP = Math.Min(
                        Convert.ToInt32(character["HP"]),
                        stats.MaxHP),
                    MP = Math.Min(
                        Convert.ToInt32(character["MP"]),
                        stats.MaxMP),
                    SP = Math.Min(
                        Convert.ToInt32(character["SP"]),
                        stats.MaxSP),
                    MaxHP = stats.MaxHP,
                    MaxMP = stats.MaxMP,
                    MaxSP = stats.MaxSP,
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
                });
        }

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
        ApplyCosmeticOverrides(
            character,
            accountId,
            dbid);

        var packet = BuildPublicPlayerData(character);
        packet["Type"] = "UpdateAppearance";
        packet["EntityID"] = dbid;
        packet["PartyID"] = _party.GetPartyId(dbid);

        return packet;
    }

    // Packets delivered to another player must never inherit the complete
    // persistence/bootstrap dictionary. Keep this explicit allowlist aligned
    // with fields the remote Godot player renderer and public SyncStatus use.
    private static Dictionary<string, object> BuildPublicPlayerData(
        Dictionary<string, object> character)
    {
        var result =
            new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var field in new[]
        {
            "Race",
            "Hair",
            "HairColor",
            "Title",
            "RedMan",
            "RedManKillLeft",
            "PKStatus",
            "Level",
            "HP",
            "MaxHP",
            "MP",
            "MaxMP",
            "SP",
            "MaxSP",
            "Attack",
            "Defense",
            "MagicAttack",
            "MagicDefense",
            "Hit",
            "Dodge",
            "Lucky",
            "MoveSpeed",
            "AttackSpeed",
            "AttackRange",
            "CritRate",
            "CritDamage",
            "SummonedHorseUID",
            "MechaType",
            "MechaBody",
            "MechaWeapon",
            "MechaEngine",
            "ClubID",
            "ClubPosition",
            "ClubName",
            "TransformType",
            "TransformID",
            "TransformColorIndex",
            "Bonnet",
            "Left_hand",
            "Right_hand",
            "Upper_armor",
            "Lower_armor",
            "Weapon",
            "Shield",
            "Back",
            "Amulet",
            "EquipColor0",
            "EquipColor1",
            "EquipColor2",
            "EquipColor3",
            "EquipColor4",
            "EquipColor5",
            "EquipColor6",
            "EquipColor7",
            "EquipColor8"
        })
        {
            if (character.TryGetValue(field, out var value))
                result[field] = value;
        }

        return result;
    }

    private void ApplyCosmeticOverrides(
        Dictionary<string, object> character,
        long accountId,
        long dbid)
    {
        foreach (var cosmetic in
                 _store.GetCosmeticOverrides(
                     accountId,
                     dbid))
        {
            switch (cosmetic.Part)
            {
                case 0:
                    character["Bonnet"] =
                        cosmetic.ItemIndex;
                    break;
                case 1:
                    character["Left_hand"] =
                        cosmetic.ItemIndex;
                    break;
                case 2:
                    character["Right_hand"] =
                        cosmetic.ItemIndex;
                    break;
                case 3:
                    character["Upper_armor"] =
                        cosmetic.ItemIndex;
                    break;
                case 4:
                    character["Lower_armor"] =
                        cosmetic.ItemIndex;
                    break;
                case 5:
                    character["Weapon"] =
                        cosmetic.ItemIndex;
                    break;
                case 6:
                    character["Shield"] =
                        cosmetic.ItemIndex;
                    break;
                case 7:
                    character["Back"] =
                        cosmetic.ItemIndex;
                    break;
                case 8:
                    character["Amulet"] =
                        cosmetic.ItemIndex;
                    break;
            }

            character[$"EquipColor{cosmetic.Part}"] =
                cosmetic.Color;
        }
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

    private static bool RequiresSelectedCharacter(
        string packetType)
        => packetType is not (
            "CreateCharacterRequest" or
            "DeleteCharacterRequest" or
            "ModifyPasswordRequest" or
            "SelectCharacterRequest" or
            "LogoutRequest");

    private static bool IsCraftingMaterialItem(
        CurrentItemDefinition item)
    {
        if (string.IsNullOrWhiteSpace(item.Description))
            return false;

        return item.Description.Contains(
                   "合成",
                   StringComparison.Ordinal) &&
               item.Description.Contains(
                   "材料",
                   StringComparison.Ordinal);
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
