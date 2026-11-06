using SGO.Server.Application;
using SGO.Server.Application.Combat;
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

builder.Services.AddSingleton(
    new ShopCatalog(gameDataRoot));
builder.Services.AddSingleton(sp =>
    new QuestRuntime(
        Path.Combine(gameDataRoot, "Quest"),
        sp.GetRequiredService<ShopCatalog>()));

builder.Services.AddSingleton<CombatService>();
builder.Services.AddSingleton<ShopService>();
builder.Services.AddSingleton<PacketRouter>();

var app = builder.Build();

app.MapGet("/health", (IConfiguration config) => Results.Ok(new
{
    status = "ok",
    database = config["Database:Provider"] ?? "Sqlite",
    websocket = config["Game:ListenUrl"]
                ?? "http://127.0.0.1:5678"
}));

app.MapSgoWebSocket();

app.Run();
