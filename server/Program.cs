using SGO.Server.Application;
using SGO.Server.Application.Combat;
using SGO.Server.Infrastructure.Legacy.Quest;
using SGO.Server.Infrastructure.Legacy.World;
using SGO.Server.Infrastructure.Persistence;
using SGO.Server.Transport.WebSockets;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(
    builder.Configuration["Game:ListenUrl"]
    ?? "http://127.0.0.1:5678");

builder.Services.AddGamePersistence(builder.Configuration);

var legacyDataRoot = Path.Combine(
    AppContext.BaseDirectory,
    "LegacyData");

builder.Services.AddSingleton(
    new LegacyWorldService(legacyDataRoot));

builder.Services.AddSingleton(
    new LegacyQuestRuntime(
        Path.Combine(legacyDataRoot, "Quest")));

builder.Services.AddSingleton<CombatService>();
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
