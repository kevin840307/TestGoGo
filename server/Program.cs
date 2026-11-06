using System.Security.Cryptography;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SGO.Server;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5678");
var dbPath = builder.Configuration["Game:Database"] ?? Path.Combine(AppContext.BaseDirectory, "Data", "game.db");
builder.Services.AddSingleton(new SqliteGameStore(dbPath));

var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.MapGet("/health", () => Results.Ok(new { status = "ok", database = "sqlite" }));
app.Map("/", async (HttpContext ctx, SqliteGameStore store, ILogger<Program> log) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }
    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    var state = new ClientSession();
    var buffer = new byte[64 * 1024];
    using var message = new MemoryStream();
    try
    {
        while (socket.State == WebSocketState.Open && !ctx.RequestAborted.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), ctx.RequestAborted);
            if (result.MessageType == WebSocketMessageType.Close) break;
            if (result.MessageType != WebSocketMessageType.Text)
            {
                await socket.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "Text only", CancellationToken.None);
                break;
            }
            message.Write(buffer, 0, result.Count);
            if (message.Length > 2_097_152)
            {
                await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Packet too large", CancellationToken.None);
                break;
            }
            if (!result.EndOfMessage) continue;
            var encoded = Encoding.UTF8.GetString(message.ToArray());
            message.SetLength(0);
            try
            {
                using var packet = Protocol.Decode(encoded);
                var responses = HandlePacket(packet.RootElement, state, store, log);
                foreach (var response in responses)
                {
                    var output = Encoding.UTF8.GetBytes(Protocol.Encode(response));
                    await socket.SendAsync(output.AsMemory(), WebSocketMessageType.Text, true, ctx.RequestAborted);
                }
            }
            catch (Exception e) when (e is FormatException or CryptographicException or JsonException)
            {
                log.LogWarning("Rejected invalid encrypted packet: {Error}", e.GetType().Name);
                await socket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "Bad packet", CancellationToken.None);
                break;
            }
        }
    }
    catch (OperationCanceledException) { }
    catch (WebSocketException ex) { log.LogInformation("WebSocket disconnected: {Message}", ex.Message); }
    finally
    {
        if (socket.State == WebSocketState.Open)
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
    }
});
app.Run();

static IEnumerable<object> HandlePacket(JsonElement req, ClientSession state, SqliteGameStore store, ILogger log)
{
    var type = req.Str("Type");
    if (type == "RegisterAccountRequest")
    {
        var (ok, msg) = store.Register(req.Str("LoginName"), req.Str("Password"), req.Str("EMAIL"));
        return [new { Type = "RegisterAccountResponse", Success = ok, Message = msg }];
    }
    if (type == "LoginRequest")
    {
        var id = store.Login(req.Str("User"), req.Str("Pass"));
        state.AccountId = id;
        state.CharacterId = null;
        return [new {
            Type = "LoginResponse",
            Success = id.HasValue,
            Message = id.HasValue ? "登入成功" : "帳號或密碼錯誤",
            Characters = id.HasValue ? store.Characters(id.Value) : []
        }];
    }
    if (!state.AccountId.HasValue)
    {
        log.LogWarning("Unauthenticated request: {Type}", type);
        return [];
    }
    var acc = state.AccountId.Value;
    switch (type)
    {
        case "CreateCharacterRequest":
        {
            var (ok, msg) = store.Create(acc, req);
            return [new { Type = "CreateCharacterResponse", Success = ok, Message = msg }];
        }
        case "DeleteCharacterRequest":
        {
            var dbid = req.Int("DBID");
            var ok = store.Delete(acc, dbid, req.Str("Password"));
            return [new {
                Type = "DeleteCharacterResponse", Success = ok,
                Message = ok ? "角色已刪除" : "無法刪除角色，請確認密碼",
                CharSlot = req.Int("CharSlot", -1), DeletedDBID = ok ? dbid : -1
            }];
        }
        case "ModifyPasswordRequest":
        {
            var newPwd = req.Str("NewPwd");
            var ok = newPwd.Length is >= 1 and <= 100 &&
                     store.ChangeCharacterPassword(acc, req.Int("DBID"), newPwd);
            return [new { Type = "ModifyPasswordResponse", Success = ok,
                Message = ok ? "修改成功" : "修改失敗",
                CharSlot = req.Int("CharSlot", -1), NewPwd = newPwd }];
        }
        case "SelectCharacterRequest":
        {
            var dbid = req.Int("DBID");
            var selected = store.Select(acc, dbid);
            if (selected is null)
                return [new { Type = "SystemMessageFlash", Message = "角色不存在" }];
            state.CharacterId = dbid;
            var world = new Dictionary<string, object>(selected) { ["Type"] = "EnterWorldSuccess" };
            // This is a minimal response and may need additional fields for full world initialization.
            return [world,
                new { Type = "InventorySync", Items = Array.Empty<object>(), MaxSize = 200 },
                new { Type = "EquipSync", Equips = Array.Empty<object>(), MaxSize = 200 },
                new { Type = "PetSync", Pets = Array.Empty<object>(), MaxSize = 50 },
                new { Type = "HorseSync", Horses = Array.Empty<object>(), HorseEquips = Array.Empty<object>(), MaxHorseEquipSize = 20 },
                new { Type = "SkillSync", Skills = Array.Empty<object>() },
                new { Type = "QuestSync", Quests = Array.Empty<object>() }
            ];
        }
        case "LogoutRequest":
            state.AccountId = null;
            state.CharacterId = null;
            return [new { Type = "LogoutResponse", IsBackToLogin = true }];
        default:
            // Do not fabricate gameplay results. Log missing packet handlers for incremental development.
            log.LogDebug("Unimplemented client packet: {Type}", type);
            return [];
    }
}

public sealed class ClientSession
{
    public long? AccountId { get; set; }
    public long? CharacterId { get; set; }
}
