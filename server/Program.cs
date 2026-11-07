using SGO.Server.Application;
using SGO.Server.Application.Combat;
using SGO.Server.Application.Gm;
using SGO.Server.Application.Shops;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Quest;
using SGO.Server.Infrastructure.World;
using SGO.Server.Infrastructure.Persistence;
using SGO.Server.Transport.WebSockets;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(
    builder.Configuration["Game:ListenUrl"]
    ?? "http://127.0.0.1:5678");

builder.Services.AddGamePersistence(builder.Configuration);

var gameDataRoot = Path.Combine(
    AppContext.BaseDirectory,
    "GameData");

builder.Services.AddSingleton(
    new WorldDataService(gameDataRoot));

var configuredClientRoot =
    builder.Configuration["Game:ClientRoot"]
    ?? Environment.GetEnvironmentVariable("SGO_CLIENT_ROOT");

var currentClientData =
    new CurrentClientDataCatalog(configuredClientRoot);

var configuredLegacyClientRoot =
    builder.Configuration["Game:LegacyClientDataRoot"]
    ?? Environment.GetEnvironmentVariable("SGO_LEGACY_CLIENT_ROOT")
    ?? @"C:\SGO\Recovered\Old\ClientData";

var legacyClientData =
    new LegacyClientDataCatalog(configuredLegacyClientRoot);

builder.Services.AddSingleton(currentClientData);
builder.Services.AddSingleton(legacyClientData);

var shopCatalog = new ShopCatalog(
    gameDataRoot,
    currentClientData,
    legacyClientData);

builder.Services.AddSingleton(shopCatalog);
builder.Services.AddSingleton(
    new HorseBatteryCatalog(currentClientData));
builder.Services.AddSingleton(
    new RecoveryItemCatalog(currentClientData));
builder.Services.AddSingleton(
    new HorseCardCatalog(currentClientData));
builder.Services.AddSingleton(
    new SkillCatalog(currentClientData));
builder.Services.AddSingleton(sp =>
    new QuestRuntime(
        Path.Combine(gameDataRoot, "Quest"),
        sp.GetRequiredService<ShopCatalog>()));

builder.Services.AddSingleton<CombatService>();
builder.Services.AddSingleton<ShopService>();
builder.Services.AddSingleton<GmCommandService>();
builder.Services.AddSingleton<PacketRouter>();

var app = builder.Build();

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
