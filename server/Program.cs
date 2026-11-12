using SGO.Server.Application;
using SGO.Server.Application.Diagnostics;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Quest;
using SGO.Server.Infrastructure.WorldData;
using SGO.Server.Infrastructure.Persistence;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.Transport.WebSockets;
using SGO.Server.Transport.Http;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(
    builder.Configuration["Game:ListenUrl"]
    ?? "http://127.0.0.1:5678");

builder.Services.AddGamePersistence(builder.Configuration);

builder.Services.AddGameData(builder.Configuration);

builder.Services.AddGameFeatures(builder.Configuration);

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var diagnostics =
            ctx.RequestServices.GetRequiredService<ServerDiagnostics>();

        diagnostics.Record(
            "Error",
            "Http",
            $"{ctx.Request.Method} {ctx.Request.Path}: {ex.GetType().Name}");

        app.Logger.LogError(
            ex,
            "[HTTP_UNHANDLED_EXCEPTION] Method={Method} Path={Path}",
            ctx.Request.Method,
            ctx.Request.Path);

        if (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode =
                StatusCodes.Status500InternalServerError;
            await ctx.Response.WriteAsJsonAsync(new
            {
                error = "Internal server error"
            });
        }
    }
});

var currentClientData =
    app.Services.GetRequiredService<CurrentClientDataCatalog>();
var legacyNpcVisuals =
    app.Services.GetRequiredService<LegacyNpcVisualCatalog>();
var shopCatalog =
    app.Services.GetRequiredService<ShopCatalog>();
var sharedMapAiTicker =
    app.Services.GetRequiredService<
        SGO.Server.Application.World.SharedMapAiTicker>();
var broadcastHub =
    app.Services.GetRequiredService<
        SGO.Server.Application.Messaging.ServerBroadcastHub>();
// This diagnostic is read-only and only exists for the SQLite provider.
// Other game-store implementations must not implement instrumentation to boot.
var sqliteStore = app.Services.GetService<SqliteGameStore>();

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
    currentPetCount = currentClientData.Pets.Count,
    sharedMapAi = sharedMapAiTicker.LastDiagnostics,
    // Read-only sub-phase timings for reproducible 20-player combat profiling.
    sharedMapAiProfile = sharedMapAiTicker.LastProfile,
    sharedMapAiRecentTicks = sharedMapAiTicker.RecentTickSamples(),
    sqliteHotWrites = sqliteStore?.HotWriteDiagnostics,
    sqliteBackgroundPosition = sqliteStore?.BackgroundPositionMetrics,
    aoi = new
    {
        playerReady = broadcastHub.AoiReadyPlayers,
        worldObservers = broadcastHub.AoiWorldObservers,
        worldEntities = broadcastHub.AoiWorldEntities
    },
    runtime = new
    {
        workingSetBytes = Environment.WorkingSet,
        gcHeapBytes = GC.GetTotalMemory(false),
        cpuTotalMilliseconds =
            System.Diagnostics.Process
                .GetCurrentProcess()
                .TotalProcessorTime
                .TotalMilliseconds
    }
}));

app.MapMallIntegrationEndpoints();
app.MapAdminEndpoints();
app.MapSgoWebSocket();

app.Run();
