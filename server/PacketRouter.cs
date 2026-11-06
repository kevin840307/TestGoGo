using System.Text.Json;

namespace SGO.Server;

public sealed class ClientSession
{
    public long? AccountId { get; set; }
    public long? CharacterId { get; set; }
    public bool CloseRequested { get; set; }
}

public sealed class PacketRouter
{
    private readonly SqliteGameStore _store;
    private readonly ILogger<PacketRouter> _log;

    public PacketRouter(SqliteGameStore store, ILogger<PacketRouter> log)
    {
        _store = store;
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

        // Announcement UI is harmless and may be opened before world selection.
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
                if (state.CharacterId == dbid) state.CharacterId = null;
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
                if (state.CharacterId.HasValue)
                    _store.UpdatePosition(
                        accountId, state.CharacterId.Value,
                        req.Int("X"), req.Int("Y"));
                return [];

            case "SyncState":
                if (state.CharacterId.HasValue)
                    _store.UpdateState(
                        accountId, state.CharacterId.Value,
                        req.Str("State", "idle"));
                return [];

            case "ChatMessage":
            {
                if (!state.CharacterId.HasValue) return [];
                var character = _store.GetCharacter(accountId, state.CharacterId.Value);
                if (character is null) return [];
                var message = req.Str("Message");
                if (message.Length > 200) message = message[..200];
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
                state.CloseRequested = true;
                return [new
                {
                    Type = "LogoutResponse",
                    IsBackToLogin = req.TryGetProperty("IsBackToLogin", out var back) && back.ValueKind == JsonValueKind.True
                }];

            case "Pong":
                return [];

            default:
                _log.LogDebug("Unimplemented client packet: {Type}", type);
                return [];
        }
    }
}
