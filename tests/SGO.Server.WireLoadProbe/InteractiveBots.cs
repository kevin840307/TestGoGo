using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using SGO.Server.Infrastructure.WorldData;

internal static class InteractiveBots
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var count = 20;
        var url = "ws://127.0.0.1:5678/";
        var map = 13;
        int? x = null;
        int? y = null;
        var radius = 280;
        var interval = 350;
        // Combat cadence is deliberately independent of fast movement
        // updates. Normal bots should not swing 3 times per second.
        var attackIntervalMs = 1000;
        var duration = 0;
        // Use existing map monsters by default; --monsters may add test spawns.
        var spawn = 0;
        var concurrency = 5;
        var level = 1;
        var combat = true;
        var timeout = TimeSpan.FromSeconds(30);
        string? output = null;

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length
                ? args[++i]
                : throw new ArgumentException("Missing value after " + args[i]);
            switch (args[i])
            {
                case "--players": count = int.Parse(Next()); break;
                case "--url": url = Next(); break;
                case "--map": map = int.Parse(Next()); break;
                case "--x": x = int.Parse(Next()); break;
                case "--y": y = int.Parse(Next()); break;
                case "--radius": radius = int.Parse(Next()); break;
                case "--interval-ms": interval = int.Parse(Next()); break;
                case "--attack-ms": attackIntervalMs = int.Parse(Next()); break;
                case "--seconds": duration = int.Parse(Next()); break;
                case "--monsters": spawn = int.Parse(Next()); break;
                case "--level": level = int.Parse(Next()); break;
                case "--setup-concurrency": concurrency = int.Parse(Next()); break;
                case "--timeout-seconds":
                    timeout = TimeSpan.FromSeconds(int.Parse(Next()));
                    break;
                case "--output": output = Next(); break;
                case "--no-combat": combat = false; break;
                case "--help":
                    Console.WriteLine("dotnet run -c Release --project tests/SGO.Server.WireLoadProbe -- --bots --players 20 --map 13");
                    Console.WriteLine("Random walkable monster-area positions; Lv1 low-attack, high-HP/DEF bots, normal attacks and rare single-target skills.");
                    Console.WriteLine("--seconds 0 keeps players online until Ctrl+C. --no-combat disables attacking and monster spawning.");
                    Console.WriteLine("Connects existing Server/DB only. Default --monsters 0 (use existing monsters); new test accounts persist.");
                    return 0;
                default: throw new ArgumentException("Unknown bot option: " + args[i]);
            }
        }

        if (x.HasValue != y.HasValue)
            throw new ArgumentException("Supply both --x and --y or neither (automatic map placement).");
        if (count < 1 || count > 1000) throw new ArgumentOutOfRangeException(nameof(count), "1..1000");
        if (interval < 150 || interval > 60000) throw new ArgumentOutOfRangeException(nameof(interval), "150..60000");
        if (attackIntervalMs < 300 || attackIntervalMs > 30000)
            throw new ArgumentOutOfRangeException(nameof(attackIntervalMs), "300..30000");
        if (radius < 32 || radius > 700 || level != 1 ||
            duration < 0 || spawn < 0 || spawn > 100 ||
            concurrency < 1 || concurrency > 50)
            throw new ArgumentException("Invalid radius/level/seconds/monsters/setup-concurrency");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("ws" or "wss"))
            throw new ArgumentException("--url must be ws:// or wss://");
        if (!(uri.IsLoopback ||
              uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Bots require a loopback or .internal test server");

        // Fail before registering any test accounts if the collision data is unavailable.
        var walks = x.HasValue
            ? new BotWalkPlanner(map, x.Value, y!.Value, radius)
            : BotWalkPlanner.ForRandomMapLocation(map, radius);
        // Use a reasonably sturdy map enemy, so low-damage bots can
        // demonstrate many hits instead of instantly one-shotting mobs.
        var enemyOptions = new WorldDataService(walks.GameDataRoot)
            .GetEnemies(map)
            .Where(enemy => enemy.Level < 65535)
            .OrderBy(enemy => enemy.MaxHp)
            .ToArray();
        var mapEnemy = enemyOptions.Length > 0
            ? enemyOptions[(enemyOptions.Length * 3) / 4]
            : null;
        if (combat && spawn > 0 && mapEnemy is null)
            throw new InvalidOperationException(
                "The map contains no valid Enemy.dat entries; cannot spawn test monsters.");
        var locations = Enumerable.Range(0, count)
            .Select(_ => x.HasValue ? walks.RandomPoint() : walks.RandomMapPoint())
            .ToArray();

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        if (duration > 0) stop.CancelAfter(TimeSpan.FromSeconds(duration));

        var run = Guid.NewGuid().ToString("N")[..10];
        var clients = new WireLoadClient?[count];
        var errors = new ConcurrentQueue<string>();
        var launched = 0;
        var start = Stopwatch.StartNew();

        Console.WriteLine($"[BOTS] run={run} count={count} map={map} " +
            (x.HasValue ? $"center=({walks.Center.X},{walks.Center.Y}) radius={radius}"
                : "placement=all walkable enemy zones (no fixed patrol radius)"));
        Console.WriteLine($"[BOTS] Connecting to existing Server at {url} (no separate database or second Server).");
        Console.WriteLine($"[BOTS] Extra test monster spawns: {spawn} (0 = use existing map monsters).");
        Console.WriteLine("[BOTS] New bot accounts persist in the existing Server database.");
        Console.WriteLine("[BOTS] Requires Game__GmCommandsEnabled=true and Game__TestBotCommandsEnabled=true on Server.");
        Console.WriteLine($"[BOTS] Lv1, ATK=1, DEF=1000, HP=20000; single-target damage capped at 1; attack cadence={attackIntervalMs}ms.");

        try
        {
            using var slots = new SemaphoreSlim(concurrency);
            // A live Server persists bot accounts. Validate GM permission
            // using only ONE bot before creating the rest of the cohort.
            async Task PrepareBotAsync(int n)
            {
                try
                {
                    await slots.WaitAsync(stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    if (stop.IsCancellationRequested) return;
                    var bot = await WireLoadClient.ConnectAndEnterAsync(
                        url, run, n, timeout,
                        (idx, next) =>
                        {
                            if (idx < 3 || idx % 10 == 0)
                                Console.WriteLine($"[BOTS] #{idx:D3} stage={next}");
                        });
                    try
                    {
                        var location = locations[n];
                        Console.WriteLine($"[BOTS] #{n:D3} enter={bot.CharacterId} " +
                            $"destination=({location.X},{location.Y})");
                        await bot.TeleportAsync(map, location.X, location.Y, timeout);
                        if (n < 3)
                            Console.WriteLine($"[BOTS] #{n:D3} stage=test-profile");
                        // The profile is restricted to an explicitly enabled
                        // test server. Ordinary GM level/maxstats would make
                        // attacks too powerful to test sustained combat.
                        await bot.PrepareWeakBotProfileAsync(timeout);
                        if (combat)
                        {
                            if (n < 3)
                                Console.WriteLine($"[BOTS] #{n:D3} stage=learn-single-target-skills");
                            await bot.LearnBotSkillAsync(20101, timeout); // 赤色光彈
                            await bot.LearnBotSkillAsync(23102, timeout); // 烈火爆劍
                         }
                        if (n != 0) bot.EnableInteractiveBots();
                        clients[n] = bot;
                        var ready = Interlocked.Increment(ref launched);
                        Console.WriteLine($"[BOTS] ready #{n:D3} ({ready}/{count}) " +
                            $"position=({bot.X},{bot.Y})");
                    }
                    catch
                    {
                        await bot.DisposeAsync();
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue($"setup #{n}: {ex.GetType().Name}: {ex.Message}");
                    Console.Error.WriteLine($"[BOTS] SETUP FAILED #{n:D3}: " +
                        $"{ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    slots.Release();
                }
            }

            Console.WriteLine(
                "[BOTS] Preflight: validating one test bot before creating the remaining accounts.");
            await PrepareBotAsync(0);
            if (clients[0] is null)
            {
                Console.Error.WriteLine(
                    "[BOTS] PRECHECK FAILED: stopped after the first bot; no further " +
                    "test accounts were created. Check that the running Server was " +
                    "rebuilt/restarted with Game__TestBotCommandsEnabled=true.");
                return 1;
            }

            if (count > 1 && !stop.IsCancellationRequested)
                await Task.WhenAll(
                    Enumerable.Range(1, count - 1).Select(PrepareBotAsync));

            var live = clients.OfType<WireLoadClient>().ToArray();
            if (live.Length == 0)
                throw new InvalidOperationException(
                    "No bot entered: check local server, GM permission and character creation.");

            if (combat)
            {
                // By default use normal map monsters already on this Server.
                // Explicit --monsters N uses the existing GM spawn path.
                for (var i = 0; i < spawn; i++)
                {
                    try
                    {
                        // Use an actual enemy listed for the chosen map,
                        // not a hardcoded ID from a different zone.
                        var loc = locations[i % locations.Length];
                        await live[0].SpawnMonsterAsync(
                            mapEnemy!.EnemyId, loc.X, loc.Y, timeout);
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue($"spawn #{i}: {ex.Message}");
                    }
                }
            }

            foreach (var bot in live) bot.EnableInteractiveBots();
            Console.WriteLine($"[BOTS] online={live.Length}; open Godot on the same map/server. Ctrl+C to stop.");
            long moves = 0, attacks = 0, singleSkills = 0, heals = 0;
            var goals = new (int X, int Y)[count];
            for (var i = 0; i < count; i++)
                goals[i] = walks.RandomPointNear(locations[i].X, locations[i].Y, radius);

            // Simulate independent real players, not a synchronized spike
            // of 20 Attack/SyncState/Move packets every 350ms. Each bot owns
            // its own monotonic action counter and a jittered wall-clock loop.
            async Task RunBotAsync(WireLoadClient bot)
            {
                var tick = 0;
                var attackCount = 0;
                var nextAttackAt = DateTimeOffset.UtcNow.AddMilliseconds(
                    Random.Shared.Next(0, attackIntervalMs + 1));
                try
                {
                    // Spread initial actions across the full base interval.
                    await Task.Delay(
                        Random.Shared.Next(0, interval + 1), stop.Token);
                    while (!stop.IsCancellationRequested)
                    {
                        try
                        {
                            var nearest = combat
                                ? bot.BotWorld.Nearest(
                                    bot.X, bot.Y, double.PositiveInfinity)
                                : null;
                            var distanceSq = nearest is null
                                ? double.PositiveInfinity
                                : Math.Pow(nearest.X - bot.X, 2) +
                                  Math.Pow(nearest.Y - bot.Y, 2);

                            // Movement and attack are separate cadences.
                            // A melee bot must actually approach a target, and
                            // wall-separated targets require pathfinding.
                            var hasClearPath = nearest is not null &&
                                walks.HasClearPath(
                                    bot.X, bot.Y, nearest.X, nearest.Y);
                            if (nearest is null ||
                                distanceSq > 92 * 92 ||
                                !hasClearPath)
                            {
                                var goal = nearest is not null
                                    ? (X: nearest.X, Y: nearest.Y)
                                    : (X: (double)goals[bot.Index].X,
                                       Y: (double)goals[bot.Index].Y);
                                if (walks.TryStep(bot.X, bot.Y, goal.X, goal.Y,
                                        out var nx, out var ny))
                                {
                                    await bot.SendBotPositionAsync(nx, ny, timeout);
                                    Interlocked.Increment(ref moves);
                                }
                                else
                                {
                                    goals[bot.Index] = walks.RandomPointNear(
                                        bot.X, bot.Y, radius);
                                }
                            }

                            // Server still validates real combat range. Never
                            // alter authoritative damage or simulate a fake hit.
                            if (nearest is not null &&
                                distanceSq <= 92 * 92 &&
                                hasClearPath &&
                                DateTimeOffset.UtcNow >= nextAttackAt)
                            {
                                attackCount++;
                                // One low-damage single-target skill per 20
                                // attacks; no AOEs or simulated distance hits.
                                if (attackCount % 20 == 0)
                                {
                                    var skill = attackCount % 60 == 0 ? 23102 : 20101;
                                    await bot.SendBotSkillAsync(
                                        skill, nearest.EntityId, timeout);
                                    Interlocked.Increment(ref singleSkills);
                                }
                                else
                                {
                                    await bot.SendBotAttackWithAnimationAsync(
                                        nearest.EntityId, nearest.X, nearest.Y,
                                        timeout);
                                    Interlocked.Increment(ref attacks);
                                }
                                var jitter = Math.Max(1, attackIntervalMs / 5);
                                nextAttackAt = DateTimeOffset.UtcNow.AddMilliseconds(
                                    attackIntervalMs +
                                    Random.Shared.Next(-jitter, jitter + 1));
                            }

                            // Retain weak Lv1 bot stats even after kill rewards.
                            if ((tick + bot.Index) %
                                Math.Max(1, 20000 / interval) == 0)
                            {
                                await bot.RefreshWeakBotProfileAsync(timeout);
                                Interlocked.Increment(ref heals);
                            }
                        }
                        catch (Exception ex)
                        {
                            if (errors.Count < 100)
                                errors.Enqueue(
                                    $"bot #{bot.Index}: {ex.GetType().Name}: {ex.Message}");
                        }

                        tick++;
                        // 75%-125% of base cadence; no global barrier.
                        var swing = Math.Max(1, interval / 4);
                        var actionDelay = Math.Max(
                            150, interval + Random.Shared.Next(-swing, swing + 1));
                        await Task.Delay(actionDelay, stop.Token);
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    // Expected Ctrl+C or timed-run cancellation.
                }
            }

            var workers = live.Select(RunBotAsync).ToArray();
            Console.WriteLine(
                $"[BOTS] independent action loops started: {workers.Length}; " +
                $"base={interval}ms, randomized 75%-125% per player.");

            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(10000, stop.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var damage = live.Sum(b => b.BotWorld.DamageHits);
                var casts = live.Sum(b => b.BotWorld.SkillCasts);
                var visible = live.Sum(b => b.BotWorld.VisibleMonsters);
                var rejected = live.Sum(b => b.BotWorld.RejectedAttackMessages);
                Console.WriteLine(
                    $"[BOTS] {start.Elapsed.TotalSeconds:F0}s online={live.Length} " +
                    $"visibleTargets={visible} move={Interlocked.Read(ref moves)} " +
                    $"attack={Interlocked.Read(ref attacks)} " +
                    $"single={Interlocked.Read(ref singleSkills)} " +
                    $"confirmedHits={damage} confirmedCasts={casts} " +
                    $"rejected={rejected} " +
                    $"corrections={live.Sum(b => b.PositionCorrections)} " +
                    $"errors={errors.Count}");

                if (start.Elapsed.TotalSeconds > 10 && damage == 0)
                {
                    var sample = live[0];
                    var target = sample.BotWorld.Nearest(
                        sample.X, sample.Y, double.PositiveInfinity);
                    var range = target is null
                        ? "none"
                        : Math.Sqrt(Math.Pow(target.X - sample.X, 2) +
                            Math.Pow(target.Y - sample.Y, 2)).ToString("F0");
                    Console.WriteLine(
                        $"[BOTS] NO CONFIRMED HITS: targets={visible} " +
                        $"firstBot=({sample.X},{sample.Y}) nearestRange={range}px " +
                        $"lastServerFlash='{sample.BotWorld.LastFlash}' " +
                        $"attempts={Interlocked.Read(ref attacks)}");
                }
            }

            // Finish in-flight sends before disposing WebSocket sessions.
            await Task.WhenAll(workers);

            var report = new
            {
                RunId = run,
                Requested = count,
                Connected = live.Length,
                ElapsedSeconds = start.Elapsed.TotalSeconds,
                MapId = map,
                ServerUrl = url,
                ExtraMonsterSpawnsRequested = spawn,
                PatrolRadius = radius,
                Placement = x.HasValue ? "near-user-point" : "map-wide-enemy-spawn-zones",
                Level = 1,
                MovementPackets = moves,
                AttackPackets = attacks,
                SingleTargetSkillPackets = singleSkills,
                 TestRecoveryPackets = heals,
                AttackIntervalMs = attackIntervalMs,
                ConfirmedDamageHits = live.Sum(b => b.BotWorld.DamageHits),
                ConfirmedSkillCasts = live.Sum(b => b.BotWorld.SkillCasts),
                ObservedMonsterDeaths = live.Sum(b => b.BotWorld.MonsterDeaths),
                VisibleTargets = live.Sum(b => b.BotWorld.VisibleMonsters),
                RejectedAttackMessages = live.Sum(b => b.BotWorld.RejectedAttackMessages),
                PositionCorrections = live.Sum(b => b.PositionCorrections),
                SentBytes = live.Sum(b => b.SentBytes),
                ReceivedBytes = live.Sum(b => b.ReceivedBytes),
                Errors = errors.ToArray()
            };

            var json = JsonSerializer.Serialize(report,
                new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);

            if (output is not null)
            {
                var fullPath = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, json + Environment.NewLine);
            }

            return errors.IsEmpty && live.Length == count &&
                (!combat || (report.ConfirmedDamageHits > 0 &&
                             report.ConfirmedSkillCasts > 0))
                ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[BOTS] " + ex);
            return 1;
        }
        finally
        {
            foreach (var bot in clients)
                if (bot is not null)
                    await bot.DisposeAsync();
        }
    }
}
