using System.Diagnostics;
using System.Text.Json;

internal static class InteractiveBots
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var count = 20;
        var url = "ws://127.0.0.1:5678/";
        var map = 13;
        var x = 4331;
        var y = 5346;
        var interval = 350;
        var duration = 0;
        var spawn = 5;
        var concurrency = 5;
        var combat = true;
        var timeout = TimeSpan.FromSeconds(30);
        string? output = null;

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length
                ? args[++i] : throw new ArgumentException("Missing value after " + args[i]);
            switch (args[i])
            {
                case "--players": count = int.Parse(Next()); break;
                case "--url": url = Next(); break;
                case "--map": map = int.Parse(Next()); break;
                case "--x": x = int.Parse(Next()); break;
                case "--y": y = int.Parse(Next()); break;
                case "--interval-ms": interval = int.Parse(Next()); break;
                case "--seconds": duration = int.Parse(Next()); break;
                case "--monsters": spawn = int.Parse(Next()); break;
                case "--setup-concurrency": concurrency = int.Parse(Next()); break;
                case "--timeout-seconds": timeout = TimeSpan.FromSeconds(int.Parse(Next())); break;
                case "--output": output = Next(); break;
                case "--no-combat": combat = false; break;
                case "--help":
                    Console.WriteLine("dotnet run -c Release --project tests/SGO.Server.WireLoadProbe -- --bots --players 20 --map 13 --x 4331 --y 5346 --seconds 0");
                    Console.WriteLine("--seconds 0 runs until Ctrl+C; --no-combat disables monster spawn/attacks.");
                    return 0;
                default: throw new ArgumentException("Unknown bot option: " + args[i]);
            }
        }

        if (count < 1 || count > 1000) throw new ArgumentOutOfRangeException(nameof(count), "1..1000");
        if (interval < 150 || interval > 60000) throw new ArgumentOutOfRangeException(nameof(interval), "150..60000");
        if (duration < 0 || spawn < 0 || spawn > 100 || concurrency < 1 || concurrency > 50)
            throw new ArgumentException("Invalid seconds/monsters/setup-concurrency");
        if (combat && spawn == 0) throw new ArgumentException("Combat needs --monsters > 0");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("ws" or "wss"))
            throw new ArgumentException("--url must be ws:// or wss://");
        // Run only against a local/private test server. Never create accounts on an arbitrary public host.
        if (!(uri.IsLoopback || uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Bots require a loopback or .internal test server");

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        if (duration > 0) stop.CancelAfter(TimeSpan.FromSeconds(duration));

        var run = Guid.NewGuid().ToString("N")[..10];
        var clients = new WireLoadClient?[count];
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var launched = 0;
        var start = Stopwatch.StartNew();
        Console.WriteLine($"[BOTS] run={run} players={count} map={map} center=({x},{y}) interval={interval}ms");
        Console.WriteLine("[BOTS] Use a disposable database and enable Game__GmCommandsEnabled=true.");
        try
        {
            using var slots = new SemaphoreSlim(concurrency);
            await Task.WhenAll(Enumerable.Range(0, count).Select(async n =>
            {
                await slots.WaitAsync(stop.Token);
                try
                {
                    if (stop.IsCancellationRequested) return;
                    var client = await WireLoadClient.ConnectAndEnterAsync(url, run, n, timeout);
                    try
                    {
                        // Keep the actors close enough for a real observer to see them.
                        await client.TeleportAsync(map, x + (n % 8) * 12, y + (n / 8) * 12, timeout);
                        // Non-anchor bots can discard unsolicited AOI packets immediately.
                        if (n != 0) client.EnableInteractiveBots();
                        clients[n] = client;
                        var ready = Interlocked.Increment(ref launched);
                        if (ready % 10 == 0 || ready == count)
                            Console.WriteLine($"[BOTS] entered {ready}/{count}");
                    }
                    catch
                    {
                        await client.DisposeAsync();
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue($"setup #{n}: {ex.Message}");
                }
                finally { slots.Release(); }
            }));

            var live = clients.OfType<WireLoadClient>().ToArray();
            if (live.Length == 0)
                throw new InvalidOperationException("No bots entered. Check GM enabled, WebSocket URL and account creation.");
            var monsters = new List<int>();
            if (combat)
            {
                for (var i = 0; i < spawn; i++)
                {
                    try
                    {
                        var target = await live[0].SpawnMonsterAsync(
                            245, x + 24 + (i % 5) * 14, y + (i / 5) * 14, timeout);
                        monsters.Add(target);
                    }
                    catch (Exception ex) { errors.Enqueue($"spawn #{i}: {ex.Message}"); }
                }
                if (monsters.Count == 0)
                    Console.WriteLine("[BOTS] No test monsters spawned; movement still runs.");
            }

            foreach (var client in live) client.EnableInteractiveBots();
            Console.WriteLine($"[BOTS] Active={live.Length}, spawned={monsters.Count}. Open your regular game client now. Ctrl+C stops.");
            long moves = 0, attacks = 0;
            var cycle = 0;
            while (!stop.IsCancellationRequested)
            {
                var jobs = live.Select(async (bot, i) =>
                {
                    try
                    {
                        // Alternating small movements keep server-side position validation active.
                        // The authoritative position stays near the safe GM-provided spawn point.
                        var offset = (cycle + i) % 2 == 0 ? 4 : -4;
                        await bot.SendPositionAsync(timeout, offset);
                        Interlocked.Increment(ref moves);
                        if (monsters.Count > 0 && (cycle + i) % 2 == 0)
                        {
                            await bot.SendAttackAsync(monsters[(cycle / 2 + i) % monsters.Count], timeout);
                            Interlocked.Increment(ref attacks);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (errors.Count < 100) errors.Enqueue($"bot #{bot.Index}: {ex.Message}");
                    }
                });
                await Task.WhenAll(jobs);
                cycle++;
                if (cycle % Math.Max(1, 10000 / interval) == 0)
                {
                    Console.WriteLine($"[BOTS] elapsed={start.Elapsed:hh\\:mm\\:ss} online={live.Length} move={moves} attack={attacks} corrections={live.Sum(b => b.PositionCorrections)} errors={errors.Count}");
                }
                try { await Task.Delay(interval, stop.Token); }
                catch (OperationCanceledException) { break; }
            }

            var report = new
            {
                RunId = run, Requested = count, Connected = live.Length,
                ElapsedSeconds = start.Elapsed.TotalSeconds, MapId = map,
                MovementPackets = moves, AttackPackets = attacks,
                PositionCorrections = live.Sum(b => b.PositionCorrections),
                SentBytes = live.Sum(b => b.SentBytes),
                ReceivedBytes = live.Sum(b => b.ReceivedBytes),
                Errors = errors.ToArray()
            };
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            if (output != null)
            {
                var path = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }
            return errors.IsEmpty && live.Length == count ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[BOTS] " + ex);
            return 1;
        }
        finally
        {
            foreach (var client in clients)
                if (client != null) await client.DisposeAsync();
        }
    }
}
