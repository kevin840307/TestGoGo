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
        var duration = 0;
        var spawn = -1;
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
                    return 0;
                default: throw new ArgumentException("Unknown bot option: " + args[i]);
            }
        }

        if (spawn == -1)
            spawn = Math.Clamp(count, 5, 100);
        if (x.HasValue != y.HasValue)
            throw new ArgumentException("Supply both --x and --y or neither (automatic map placement).");
        if (count < 1 || count > 1000) throw new ArgumentOutOfRangeException(nameof(count), "1..1000");
        if (interval < 150 || interval > 60000) throw new ArgumentOutOfRangeException(nameof(interval), "150..60000");
        if (radius < 32 || radius > 700 || level != 1 ||
            duration < 0 || spawn < 0 || spawn > 100 ||
            concurrency < 1 || concurrency > 50)
            throw new ArgumentException("Invalid radius/level/seconds/monsters/setup-concurrency");
        if (combat && spawn == 0) throw new ArgumentException("Combat needs --monsters > 0");
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
        if (combat && mapEnemy is null)
            throw new InvalidOperationException(
                "The map contains no valid Enemy.dat entries; cannot spawn test monsters.");
        var locations = Enumerable.Range(0, count)
            .Select(_ => walks.RandomPoint())
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

        Console.WriteLine($"[BOTS] run={run} count={count} map={map} center=({walks.Center.X},{walks.Center.Y}) radius={radius}");
        Console.WriteLine("[BOTS] Requires disposable DB + Game__GmCommandsEnabled=true + Game__TestBotCommandsEnabled=true.");
        Console.WriteLine("[BOTS] Lv1, ATK=1, DEF=1000, HP=20000; normal attacks + rare simple single-target skills.");

        try
        {
            using var slots = new SemaphoreSlim(concurrency);
            await Task.WhenAll(Enumerable.Range(0, count).Select(async n =>
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
                    var bot = await WireLoadClient.ConnectAndEnterAsync(url, run, n, timeout);
                    try
                    {
                        var location = locations[n];
                        await bot.TeleportAsync(map, location.X, location.Y, timeout);
                        // The profile is restricted to an explicitly enabled
                        // test server. Ordinary GM level/maxstats would make
                        // attacks too powerful to test sustained combat.
                        await bot.PrepareWeakBotProfileAsync(timeout);
                        if (combat)
                        {
                            await bot.LearnBotSkillAsync(20101, timeout); // 赤色光彈
                            await bot.LearnBotSkillAsync(23102, timeout); // 烈火爆劍
                         }
                        if (n != 0) bot.EnableInteractiveBots();
                        clients[n] = bot;
                        var ready = Interlocked.Increment(ref launched);
                        if (ready % 10 == 0 || ready == count)
                            Console.WriteLine($"[BOTS] prepared {ready}/{count}");
                    }
                    catch
                    {
                        await bot.DisposeAsync();
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue($"setup #{n}: {ex.Message}");
                }
                finally
                {
                    slots.Release();
                }
            }));

            var live = clients.OfType<WireLoadClient>().ToArray();
            if (live.Length == 0)
                throw new InvalidOperationException(
                    "No bot entered: check local server, GM permission and character creation.");

            if (combat)
            {
                // Spawn real server monsters in verified walkable coordinates.
                // They also respawn through the normal shared world AI.
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
                goals[i] = walks.RandomPoint();

            var cycle = 0;
            while (!stop.IsCancellationRequested)
            {
                var tick = cycle;
                await Task.WhenAll(live.Select(async bot =>
                {
                    try
                    {
                        var nearest = combat
                            ? bot.BotWorld.Nearest(bot.X, bot.Y, 600)
                            : null;

                        var distanceSq = nearest is null
                            ? double.PositiveInfinity
                            : Math.Pow(nearest.X - bot.X, 2) +
                              Math.Pow(nearest.Y - bot.Y, 2);

                        // Use real A* navigation toward a living AOI monster.
                        // When nothing is nearby, wander toward random reachable points.
                        if (nearest is null || distanceSq > 150 * 150)
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
                                goals[bot.Index] = walks.RandomPoint();
                            }
                        }

                        // Never attack by a cached fixed entity ID. Pick a living
                        // monster from this bot's actual AOI snapshot every tick.
                        if (nearest is not null && distanceSq <= 185 * 185)
                        {
                            var phase = (tick + bot.Index * 7);
                            if (phase % 60 == 0 || phase % 20 == 0)
                            {
                                // Only normal single-target skills. Never cast
                                // 99001/23103 or any AOE during load tests.
                                var skill = phase % 60 == 0 ? 23102 : 20101;
                                await bot.SendBotSkillAsync(skill, nearest.EntityId, timeout);
                                Interlocked.Increment(ref singleSkills);
                            }
                            else
                            {
                                await bot.SendAttackAsync(nearest.EntityId, timeout);
                                Interlocked.Increment(ref attacks);
                            }
                        }

                        // Keep HP and mana for sustained observation; the heal
                        // command is enabled only on this isolated test server.
                        if ((tick + bot.Index) % Math.Max(1, 20000 / interval) == 0)
                        {
                            await bot.SendBotHealAsync(timeout);
                            Interlocked.Increment(ref heals);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (errors.Count < 100)
                            errors.Enqueue($"bot #{bot.Index}: {ex.Message}");
                    }
                }));

                cycle++;
                if (cycle % Math.Max(1, 10000 / interval) == 0)
                {
                    var damage = live.Sum(b => b.BotWorld.DamageHits);
                    var casts = live.Sum(b => b.BotWorld.SkillCasts);
                    Console.WriteLine(
                        $"[BOTS] {start.Elapsed.TotalSeconds:F0}s online={live.Length} move={moves} attack={attacks} single={singleSkills} confirmedHits={damage} confirmedCasts={casts} corrections={live.Sum(b => b.PositionCorrections)} errors={errors.Count}");
                }

                try
                {
                    await Task.Delay(interval, stop.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            var report = new
            {
                RunId = run,
                Requested = count,
                Connected = live.Length,
                ElapsedSeconds = start.Elapsed.TotalSeconds,
                MapId = map,
                RandomSpawnRadius = radius,
                Level = 1,
                MovementPackets = moves,
                AttackPackets = attacks,
                SingleTargetSkillPackets = singleSkills,
                 TestRecoveryPackets = heals,
                ConfirmedDamageHits = live.Sum(b => b.BotWorld.DamageHits),
                ConfirmedSkillCasts = live.Sum(b => b.BotWorld.SkillCasts),
                ObservedMonsterDeaths = live.Sum(b => b.BotWorld.MonsterDeaths),
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
