using System.Net;
using SGO.Server.Application.Diagnostics;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.World;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Transport.Http;

public static class AdminEndpoints
{
    public static WebApplication MapAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/admin", (HttpContext ctx) =>
        {
            if (!IsLocal(ctx))
                return Results.NotFound();

            var uiPath = Path.Combine(
                AppContext.BaseDirectory,
                "AdminUi",
                "index.html");

            return File.Exists(uiPath)
                ? Results.File(uiPath, "text/html; charset=utf-8")
                : Results.NotFound();
        });

        app.MapGet("/admin/api/status", (
            HttpContext ctx,
            ServerBroadcastHub broadcasts,
            ServerDiagnostics diagnostics,
            CurrentClientDataCatalog gameData,
            SharedMapAiTicker mapAi) =>
        {
            if (!IsLocal(ctx))
                return Results.NotFound();

            var players = broadcasts.OnlinePlayers()
                .OrderBy(x => x.MapId)
                .ThenBy(x => x.CharacterId)
                .ToArray();

            var maps = players
                .GroupBy(x => x.MapId)
                .OrderBy(x => x.Key)
                .Select(x => new
                {
                    mapId = x.Key,
                    players = x.Count()
                })
                .ToArray();

            using var process = System.Diagnostics.Process.GetCurrentProcess();

            return Results.Ok(new
            {
                serverTime = DateTimeOffset.Now,
                uptimeSeconds =
                    (DateTimeOffset.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds,
                onlinePlayers = players.Length,
                maps,
                players = players.Select(x => new
                {
                    connectionId = x.ConnectionId,
                    accountId = x.AccountId,
                    characterId = x.CharacterId,
                    mapId = x.MapId,
                    x = Math.Round(x.X, 1),
                    y = Math.Round(x.Y, 1)
                }),
                runtime = new
                {
                    workingSetMb = Math.Round(process.WorkingSet64 / 1048576d, 1),
                    privateMemoryMb = Math.Round(process.PrivateMemorySize64 / 1048576d, 1),
                    gcHeapMb = Math.Round(GC.GetTotalMemory(false) / 1048576d, 1),
                    gcGen0 = GC.CollectionCount(0),
                    gcGen1 = GC.CollectionCount(1),
                    gcGen2 = GC.CollectionCount(2),
                    threads = process.Threads.Count,
                    cpuTotalMilliseconds = Math.Round(process.TotalProcessorTime.TotalMilliseconds, 0)
                },
                gameData = new
                {
                    available = gameData.IsAvailable,
                    root = gameData.Root,
                    items = gameData.Items.Count,
                    skills = gameData.Skills.Count,
                    pets = gameData.Pets.Count
                },
                counters = diagnostics.SnapshotCounters(),
                sharedMapAi = mapAi.LastDiagnostics
            });
        });

        app.MapGet("/admin/api/events", (
            HttpContext ctx,
            ServerDiagnostics diagnostics,
            int? count) =>
        {
            if (!IsLocal(ctx))
                return Results.NotFound();

            return Results.Ok(diagnostics.Recent(count ?? 100));
        });

        app.MapPost("/admin/api/broadcast", async (
            HttpContext ctx,
            ServerBroadcastHub broadcasts,
            ServerDiagnostics diagnostics) =>
        {
            if (!IsLocal(ctx))
                return Results.NotFound();

            var body = await ctx.Request.ReadFromJsonAsync<BroadcastRequest>();
            var message = body?.Message?.Trim() ?? "";
            if (message.Length is < 1 or > 500)
                return Results.BadRequest(new { error = "公告內容需為 1~500 字元" });

            broadcasts.Publish(new
            {
                Type = "SystemMessageBroadcast",
                Message = message
            });

            diagnostics.Record(
                "Information",
                "Admin",
                $"Broadcast sent: {message}");

            return Results.Ok(new { success = true });
        });

        return app;
    }

    private static bool IsLocal(HttpContext ctx)
    {
        var address = ctx.Connection.RemoteIpAddress;
        return address is not null && IPAddress.IsLoopback(address);
    }

    private sealed record BroadcastRequest(string Message);


}
