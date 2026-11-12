using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using SGO.Server.Infrastructure.WorldData;

internal static class InteractiveBots
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var count = 20;
        // -1 selects roughly one quarter of the connected bots as frequent roamers.
        var requestedRoamers = -1;
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
                case "--roamers": requestedRoamers = int.Parse(Next()); break;
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
                    Console.WriteLine("Random walkable monster-area positions; Lv1 moderate-attack, high-HP/DEF bots, normal attacks and rare single-target skills.");
                    Console.WriteLine("--seconds 0 keeps players online until Ctrl+C. --no-combat disables attacking and monster spawning.");
                    Console.WriteLine("--roamers N: choose N frequent walkers (default about 25%, e.g. 5 out of 20; 0 disables).");
                    Console.WriteLine("Connects existing Server/DB only. Default --monsters 0 (use existing monsters); new test accounts persist.");
                    return 0;
                default: throw new ArgumentException("Unknown bot option: " + args[i]);
            }
        }

        if (x.HasValue != y.HasValue)
            throw new ArgumentException("Supply both --x and --y or neither (automatic map placement).");
        if (count < 1 || count > 1000) throw new ArgumentOutOfRangeException(nameof(count), "1..1000");
        if (requestedRoamers < -1 || requestedRoamers > count)
            throw new ArgumentOutOfRangeException(nameof(requestedRoamers),
                "--roamers must be 0..--players");
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
        Console.WriteLine($"[BOTS] Lv1, ATK=60, MATK=45, DEF=1000, HP=20000; single-target skill cap=90; attack cadence={attackIntervalMs}ms.");

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
                        },
                        sendWorldReady: false);
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
            // Assign roles after successful login, so even a partial cohort
            // receives the requested mix. Roles remain fixed for the run.
            var roamerCount = requestedRoamers >= 0
                ? Math.Min(requestedRoamers, live.Length)
                : (int)Math.Clamp(Math.Round(live.Length * 0.25,
                    MidpointRounding.AwayFromZero), 1, live.Length);
            var roamerIds = live.OrderBy(_ => Random.Shared.Next())
                .Take(roamerCount).Select(b => b.Index).ToHashSet();
            Console.WriteLine($"[BOTS] frequent roamers={roamerIds.Count}/{live.Length}; " +
                "80% of their decisions are distant ground clicks, remaining bots=25%.");
            Console.WriteLine("[BOTS] roamer bot indices: " +
                string.Join(", ", roamerIds.OrderBy(i => i).Take(24)) +
                (roamerIds.Count > 24 ? ", ..." : ""));
            long moves = 0, attacks = 0, singleSkills = 0, heals = 0;
            long monsterClicks = 0, groundClicks = 0, targetSwitches = 0;
            long roamerMoves = 0, roamerGroundClicks = 0, roamerAttacks = 0;
            // Simulate independent real players, not a synchronized spike
            // of 20 Attack/SyncState/Move packets every 350ms. Each bot owns
            // its own monotonic action counter and a jittered wall-clock loop.
            async Task RunBotAsync(WireLoadClient bot)
            {
                var isRoamer = roamerIds.Contains(bot.Index);
                var tick = 0;
                var attackCount = 0;
                // Decisions model mouse clicks, not a new nearest-target
                // search every network tick. Each bot independently commits
                // to a target or a distant ground click for several seconds.
                int? selectedMonsterId = null;
                var walkingToGround = false;
                var groundGoal = (X: bot.X, Y: bot.Y);
                var nextDecisionAt = DateTimeOffset.MinValue;
                var lastMonsterId = -1;
                // The attack window starts at the first REAL swing, not when a
                // distant monster is clicked and the bot still has to walk.
                var hasAttackedSelectedMonster = false;
                var triedFinishingTarget = false;
                var nextAttackAt = DateTimeOffset.UtcNow.AddMilliseconds(
                    Random.Shared.Next(0, attackIntervalMs + 1));
                // Every freshly connected bot already received the Lv1
                // weak-profile setup. The old 20-second refresh forced all
                // 20 connections through four SQLite writes while holding
                // their session locks, creating artificial 1–6 second
                // movement/AI stalls. Only combat bots can gain EXP, and
                // stagger their later recovery by tens of seconds.
                var nextProfileRefreshAt = DateTimeOffset.UtcNow.AddSeconds(
                    55 + (bot.Index * 17 % 48));
                try
                {
                    // Spread initial actions across the full base interval.
                    await Task.Delay(
                        Random.Shared.Next(0, interval + 1), stop.Token);
                    while (!stop.IsCancellationRequested)
                    {
                        try
                        {
                            var now = DateTimeOffset.UtcNow;
                            var target = selectedMonsterId.HasValue
                                ? bot.BotWorld.FindTarget(selectedMonsterId.Value)
                                : null;
                            // Sometimes finish a wounded monster instead of
                            // switching exactly when the normal attack window ends.
                            // Only one short extension per target avoids tunnel vision.
                            if (now >= nextDecisionAt && !walkingToGround &&
                                target is not null && hasAttackedSelectedMonster &&
                                !triedFinishingTarget && target.MaxHP > 0 &&
                                (long)target.HP * 100 <= (long)target.MaxHP * 45 &&
                                Random.Shared.NextDouble() < 0.70)
                            {
                                triedFinishingTarget = true;
                                nextDecisionAt = now.AddMilliseconds(
                                    Random.Shared.Next(2500, 5501));
                            }
                            // A short reaction gap before attacking again after
                            // a monster leaves AOI or dies (the new target may
                            // already be within melee range).
                            if (!walkingToGround && selectedMonsterId.HasValue &&
                                target is null)
                                nextAttackAt = now.AddMilliseconds(
                                    Random.Shared.Next(450, 1101));
                            // Reconsider after a human-scale action window,
                            // or if the clicked monster disappears from AOI.
                            if (now >= nextDecisionAt ||
                                (!walkingToGround && target is null))
                            {
                                var candidates = combat
                                    ? bot.BotWorld.VisibleTargets(bot.X, bot.Y)
                                        .OrderBy(m =>
                                            Math.Pow(m.X - bot.X, 2) +
                                            Math.Pow(m.Y - bot.Y, 2))
                                        .Take(10).ToArray()
                                    : Array.Empty<BotWorldState.Monster>();
                                // Frequent roamers click distant ground ~80%
                                // of the time; normal fighters do so ~25%.
                                // With no targets, everyone keeps exploring.
                                walkingToGround = candidates.Length == 0 ||
                                    (isRoamer ? Random.Shared.Next(5) != 0
                                              : Random.Shared.Next(4) == 0);
                                selectedMonsterId = null;
                                hasAttackedSelectedMonster = false;
                                triedFinishingTarget = false;
                                if (walkingToGround)
                                {
                                    // These are long destination clicks (not
                                    // short random hops). The path planner
                                    // performs safe incremental server moves.
                                    Interlocked.Increment(ref groundClicks);
                                    if (isRoamer) Interlocked.Increment(ref roamerGroundClicks);
                                    groundGoal = walks.RandomDistantPointNear(
                                        bot.X, bot.Y, minimumDistance: 220,
                                        maxDistance: Random.Shared.Next(520, 701));
                                    nextDecisionAt = now.AddMilliseconds(
                                        isRoamer ? Random.Shared.Next(4000, 7501)
                                                 : Random.Shared.Next(6500, 12001));
                                }
                                else
                                {
                                    // Most players click a nearby monster,
                                    // but quite often choose a farther one.
                                    // Avoid repeatedly clicking the exact
                                    // previous target when alternatives exist.
                                    // Wounded monsters attract attention, but
                                    // most clicks still use a mixed near/far pool.
                                    var wounded = candidates.Where(m => m.MaxHP > 0 &&
                                        (long)m.HP * 100 <= (long)m.MaxHP * 50).ToArray();
                                    if (wounded.Length > 0 &&
                                        Random.Shared.NextDouble() < 0.55)
                                    {
                                        target = wounded[Random.Shared.Next(wounded.Length)];
                                    }
                                    else
                                    {
                                        var pool = candidates.Length > 1
                                            ? candidates.Where(m =>
                                                m.EntityId != lastMonsterId).ToArray()
                                            : candidates;
                                        var nearCount = Math.Min(3, pool.Length);
                                        var pick = Random.Shared.NextDouble() < 0.55
                                            ? Random.Shared.Next(nearCount)
                                            : Random.Shared.Next(pool.Length);
                                        target = pool[pick];
                                    }
                                    if (lastMonsterId >= 0 && lastMonsterId != target.EntityId)
                                        Interlocked.Increment(ref targetSwitches);
                                    Interlocked.Increment(ref monsterClicks);
                                    selectedMonsterId = target.EntityId;
                                    lastMonsterId = target.EntityId;
                                    // Allow enough travel time for a genuine
                                    // long monster click. Once in range and
                                    // attacking, count 3.5-8.5s of combat.
                                    nextDecisionAt = now.AddSeconds(16);
                                }
                            }

                            var goalX = walkingToGround ? groundGoal.X : target?.X ?? bot.X;
                            var goalY = walkingToGround ? groundGoal.Y : target?.Y ?? bot.Y;
                            var dx = goalX - bot.X;
                            var dy = goalY - bot.Y;
                            var distanceSq = dx * dx + dy * dy;
                            var hasClearPath = !walkingToGround && target is not null &&
                                walks.HasClearPath(bot.X, bot.Y, target.X, target.Y);
                            var shouldMove = walkingToGround
                                ? distanceSq > 32 * 32
                                : target is not null &&
                                  (distanceSq > 92 * 92 || !hasClearPath);
                            if (shouldMove)
                            {
                                // Network position deltas stay bounded by the
                                // validated server movement rules; a long
                                // click does not teleport the character.
                                var stride = Math.Clamp(interval * 0.14, 16.0, 64.0);
                                if (walks.TryStep(bot.X, bot.Y, goalX, goalY,
                                        stride, out var nx, out var ny))
                                {
                                    await bot.SendBotPositionAsync(nx, ny, timeout);
                                    Interlocked.Increment(ref moves);
                                    if (isRoamer) Interlocked.Increment(ref roamerMoves);
                                }
                                else
                                {
                                    // A blocked ground click triggers another
                                    // click elsewhere, not a tight jitter loop.
                                    nextDecisionAt = DateTimeOffset.MinValue;
                                }
                            }
                            else if (walkingToGround)
                            {
                                // Reached the clicked ground coordinate.
                                nextDecisionAt = DateTimeOffset.MinValue;
                            }

                            // Only attack the selected monster after reaching
                            // real melee range and clear line of movement.
                            if (!walkingToGround && target is not null &&
                                distanceSq <= 92 * 92 && hasClearPath &&
                                DateTimeOffset.UtcNow >= nextAttackAt)
                            {
                                attackCount++;
                                if (attackCount % 20 == 0)
                                {
                                    var skill = attackCount % 60 == 0 ? 23102 : 20101;
                                    await bot.SendBotSkillAsync(
                                        skill, target.EntityId, timeout);
                                    Interlocked.Increment(ref singleSkills);
                                }
                                else
                                {
                                    await bot.SendBotAttackWithAnimationAsync(
                                        target.EntityId, target.X, target.Y,
                                        timeout);
                                    Interlocked.Increment(ref attacks);
                                    if (isRoamer) Interlocked.Increment(ref roamerAttacks);
                                }
                                var jitter = Math.Max(1, attackIntervalMs / 5);
                                nextAttackAt = DateTimeOffset.UtcNow.AddMilliseconds(
                                    attackIntervalMs +
                                    Random.Shared.Next(-jitter, jitter + 1));
                                if (!hasAttackedSelectedMonster)
                                {
                                    hasAttackedSelectedMonster = true;
                                    // Most fighters switch after a few seconds,
                                    // while some commit for a longer hunt. This
                                    // gives tougher monsters time to be defeated.
                                    var fightWindowMs = isRoamer
                                        ? Random.Shared.Next(2000, 4201)
                                        : Random.Shared.Next(4) == 0
                                            ? Random.Shared.Next(11000, 19001)
                                            : Random.Shared.Next(3500, 8501);
                                    nextDecisionAt = DateTimeOffset.UtcNow.AddMilliseconds(
                                        fightWindowMs);
                                }
                            }

                            // Idle/movement-only bots do not gain EXP, and
                            // must never cause periodic destructive GM DB
                            // writes. Combat bots still restore the same
                            // Lv1/weak damage profile, on a staggered 90s
                            // interval instead of a synchronized 20s burst.
                            if (combat && DateTimeOffset.UtcNow >= nextProfileRefreshAt)
                            {
                                await bot.RefreshWeakBotProfileAsync(timeout);
                                Interlocked.Increment(ref heals);
                                nextProfileRefreshAt = DateTimeOffset.UtcNow.AddSeconds(90);
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
                    $"monsterClicks={Interlocked.Read(ref monsterClicks)} " +
                    $"groundClicks={Interlocked.Read(ref groundClicks)} " +
                    $"switches={Interlocked.Read(ref targetSwitches)} " +
                    $"roamerMoves={Interlocked.Read(ref roamerMoves)} " +
                    $"roamerGroundClicks={Interlocked.Read(ref roamerGroundClicks)} " +
                    $"attack={Interlocked.Read(ref attacks)} " +
                    $"single={Interlocked.Read(ref singleSkills)} " +
                    $"confirmedHits={damage} confirmedCasts={casts} " +
                    $"confirmedKills={live.Sum(b => b.BotWorld.ConfirmedMonsterKills)} " +
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
                FrequentRoamerCount = roamerIds.Count,
                FrequentRoamerBotIndices = roamerIds.OrderBy(i => i).ToArray(),
                FrequentRoamerMovementPackets = roamerMoves,
                FrequentRoamerGroundClicks = roamerGroundClicks,
                FrequentRoamerAttackPackets = roamerAttacks,
                MonsterClickDecisions = monsterClicks,
                GroundClickDecisions = groundClicks,
                TargetSwitchDecisions = targetSwitches,
                AttackPackets = attacks,
                SingleTargetSkillPackets = singleSkills,
                 TestRecoveryPackets = heals,
                AttackIntervalMs = attackIntervalMs,
                ConfirmedDamageHits = live.Sum(b => b.BotWorld.DamageHits),
                ConfirmedSkillCasts = live.Sum(b => b.BotWorld.SkillCasts),
                ObservedMonsterDeaths = live.Sum(b => b.BotWorld.MonsterDeaths),
                ConfirmedMonsterKills = live.Sum(b => b.BotWorld.ConfirmedMonsterKills),
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
