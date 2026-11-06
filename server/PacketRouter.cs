using System.Text.Json;
using SGO.Server.Legacy;

namespace SGO.Server;

public sealed class ClientSession
{
    public long? AccountId { get; set; }
    public long? CharacterId { get; set; }
    public int CurrentMapId { get; set; }
    public bool PendingMapSpawn { get; set; }
    public bool CloseRequested { get; set; }
}

public sealed class PacketRouter
{
    private const int NpcInteractRange = 500;

    private readonly SqliteGameStore _store;
    private readonly LegacyWorldService _world;
    private readonly LegacyQuestRuntime _quests;
    private readonly ILogger<PacketRouter> _log;

    public PacketRouter(
        SqliteGameStore store,
        LegacyWorldService world,
        LegacyQuestRuntime quests,
        ILogger<PacketRouter> log)
    {
        _store = store;
        _world = world;
        _quests = quests;
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

                return
                [
                    character,
                    new { Type = "InventorySync", Items = Array.Empty<object>(), MaxSize = 200 },
                    new { Type = "EquipSync", Equips = Array.Empty<object>(), MaxSize = 200 },
                    new { Type = "PetSync", Pets = Array.Empty<object>(), MaxSize = 50 },
                    new
                    {
                        Type = "HorseSync",
                        Horses = Array.Empty<object>(),
                        HorseEquips = Array.Empty<object>(),
                        MaxHorseEquipSize = 20
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

                if (!state.PendingMapSpawn)
                    return [];

                state.PendingMapSpawn = false;
                return _world.BuildMapSpawnPackets(state.CurrentMapId);
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

                return
                [
                    new
                    {
                        Type = "SyncStatus",
                        EntityID = state.CharacterId.Value,
                        HP = character["HP"],
                        MP = character["MP"],
                        SP = character["SP"],
                        MaxHP = character["MaxHP"],
                        MaxMP = character["MaxMP"],
                        MaxSP = character["MaxSP"]
                    },
                    new
                    {
                        Type = "PlayerStatusSync",
                        HP = character["HP"],
                        MP = character["MP"],
                        SP = character["SP"]
                    }
                ];
            }

            case "LogoutRequest":
                state.AccountId = null;
                state.CharacterId = null;
                state.CurrentMapId = 0;
                state.PendingMapSpawn = false;
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
        LegacyWorldNpc npc,
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
                name => GetLegacyCharacterData(character, name));

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

    private static object? GetLegacyCharacterData(
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
