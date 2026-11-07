using SGO.Server.Application;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Quest;
using SGO.Server.Infrastructure.WorldData;
using SGO.Server.Infrastructure.Persistence;
using SGO.Server.Transport.WebSockets;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(
    builder.Configuration["Game:ListenUrl"]
    ?? "http://127.0.0.1:5678");

builder.Services.AddGamePersistence(builder.Configuration);

builder.Services.AddGameData(builder.Configuration);

builder.Services.AddGameFeatures(builder.Configuration);

var app = builder.Build();

var currentClientData =
    app.Services.GetRequiredService<CurrentClientDataCatalog>();
var legacyNpcVisuals =
    app.Services.GetRequiredService<LegacyNpcVisualCatalog>();
var shopCatalog =
    app.Services.GetRequiredService<ShopCatalog>();

if (!currentClientData.IsAvailable)
{
    app.Logger.LogWarning(
        "[CURRENT_GAMEDATA_MISSING] 無法找到新版 recovered client 的 Items/Skills。設定 Game:ClientRoot 或 SGO_CLIENT_ROOT；舊 ClientData 不會當正式 runtime GameData 使用。");
}
else
{
    app.Logger.LogInformation(
        "[CURRENT_GAMEDATA] Root={Root} Items={ItemCount} Skills={SkillCount} Pets={PetCount}",
        currentClientData.Root,
        currentClientData.Items.Count,
        currentClientData.Skills.Count,
        currentClientData.Pets.Count);

    app.Logger.LogInformation(
        "[LEGACY_NPC_VISUALS] Mapped={Count}",
        legacyNpcVisuals.Count);

    foreach (var info in shopCatalog.MappingInfo)
        app.Logger.LogInformation("{MappingInfo}", info);

    foreach (var warning in shopCatalog.MappingWarnings)
        app.Logger.LogWarning("{MappingWarning}", warning);
}

app.MapGet("/health", (IConfiguration config) => Results.Ok(new
{
    status = "ok",
    database = config["Database:Provider"] ?? "Sqlite",
    websocket = config["Game:ListenUrl"]
                ?? "http://127.0.0.1:5678",
    currentGameData = currentClientData.IsAvailable,
    currentClientRoot = currentClientData.Root,
    currentItemCount = currentClientData.Items.Count,
    currentSkillCount = currentClientData.Skills.Count,
    currentPetCount = currentClientData.Pets.Count
}));

app.MapSgoWebSocket();

app.Run();
