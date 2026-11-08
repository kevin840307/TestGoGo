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

            return Results.Content(AdminHtml, "text/html; charset=utf-8");
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

    private const string AdminHtml = """
<!doctype html>
<html lang="zh-Hant">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>SGO Server Admin</title>
<style>
body{font-family:system-ui,-apple-system,"Segoe UI",sans-serif;margin:0;background:#111827;color:#e5e7eb}
main{max-width:1400px;margin:auto;padding:20px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:12px}
.card{background:#1f2937;border:1px solid #374151;border-radius:10px;padding:14px}h1,h2{margin-top:0}
.metric{font-size:26px;font-weight:700}.muted{color:#9ca3af;font-size:13px}
table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:7px;border-bottom:1px solid #374151;font-size:13px}
input,button{background:#111827;color:#e5e7eb;border:1px solid #4b5563;border-radius:6px;padding:9px}
input{width:min(600px,70%)}button{cursor:pointer}.event-error{color:#fca5a5}.event-warning{color:#fde68a}
section{margin-top:16px}.scroll{max-height:360px;overflow:auto}
</style>
</head>
<body><main>
<h1>SGO Server Admin</h1>
<div id="summary" class="grid"></div>
<section class="card"><h2>全服公告</h2><input id="broadcast" maxlength="500" placeholder="輸入公告內容"><button onclick="sendBroadcast()">送出</button><span id="broadcastResult" class="muted"></span></section>
<section class="card"><h2>地圖分布</h2><div id="maps"></div></section>
<section class="card"><h2>在線玩家</h2><div class="scroll"><table><thead><tr><th>Character</th><th>Account</th><th>Map</th><th>Position</th><th>Connection</th></tr></thead><tbody id="players"></tbody></table></div></section>
<section class="card"><h2>最近診斷事件</h2><div class="scroll"><table><thead><tr><th>時間</th><th>Level</th><th>Category</th><th>Message</th><th>ms</th></tr></thead><tbody id="events"></tbody></table></div></section>
<script>
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
async function refresh(){
 const s=await (await fetch('/admin/api/status')).json();
 document.getElementById('summary').innerHTML=[
 ['在線',s.onlinePlayers],['Working Set',s.runtime.workingSetMb+' MB'],['Private RAM',s.runtime.privateMemoryMb+' MB'],
 ['GC Heap',s.runtime.gcHeapMb+' MB'],['Threads',s.runtime.threads],['Packets',s.counters.packets],
 ['Packet Errors',s.counters.packetErrors],['Slow Packets',s.counters.slowPackets],
 ['AI Slow',s.counters.mapAiSlowTicks],['Map Pressure',s.counters.mapPressureWarnings],
 ['Broadcast Backlog',s.counters.broadcastBacklogWarnings],['Trade Conflicts',s.counters.tradeConflicts],
 ['Pickup Conflicts',s.counters.pickupConflicts],['DB Failures',s.counters.databaseFailures],
 ['World Tick Failures',s.counters.worldTickFailures],['Resource Warnings',s.counters.resourceWarnings]
 ].map(x=>'<div class="card"><div class="muted">'+esc(x[0])+'</div><div class="metric">'+esc(x[1])+'</div></div>').join('');
 document.getElementById('maps').innerHTML=s.maps.length?s.maps.map(x=>'Map '+esc(x.mapId)+': <b>'+esc(x.players)+'</b>').join(' &nbsp; '):'<span class="muted">目前無玩家</span>';
 document.getElementById('players').innerHTML=s.players.map(p=>'<tr><td>'+esc(p.characterId)+'</td><td>'+esc(p.accountId)+'</td><td>'+esc(p.mapId)+'</td><td>'+esc(p.x)+', '+esc(p.y)+'</td><td>'+esc(p.connectionId)+'</td></tr>').join('');
 const e=await (await fetch('/admin/api/events?count=150')).json();
 document.getElementById('events').innerHTML=e.map(x=>'<tr class="event-'+String(x.level).toLowerCase()+'"><td>'+esc(new Date(x.timestamp).toLocaleTimeString())+'</td><td>'+esc(x.level)+'</td><td>'+esc(x.category)+'</td><td>'+esc(x.message)+'</td><td>'+esc(x.elapsedMilliseconds??'')+'</td></tr>').join('');
}
async function sendBroadcast(){
 const el=document.getElementById('broadcast'), out=document.getElementById('broadcastResult');
 const r=await fetch('/admin/api/broadcast',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({message:el.value})});
 out.textContent=r.ok?' 已送出':' 送出失敗'; if(r.ok)el.value='';
 await refresh();
}
refresh();setInterval(refresh,2000);
</script>
</main></body></html>
""";
}
