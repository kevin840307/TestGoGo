using SGO.Server.Application.Messaging;
using System.Collections.Concurrent;
using System.Diagnostics;

internal sealed record LatencySummary(
    int Count,
    double Min,
    double P50,
    double P95,
    double P99,
    double Max,
    double Average)
{
    public static LatencySummary From(
        IEnumerable<double> source)
    {
        var values =
            source.OrderBy(x => x)
                .ToArray();

        if (values.Length == 0)
        {
            return new LatencySummary(
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        return new LatencySummary(
            values.Length,
            values[0],
            Percentile(values, 0.50),
            Percentile(values, 0.95),
            Percentile(values, 0.99),
            values[^1],
            values.Average());
    }

    private static double Percentile(
        double[] sorted,
        double percentile)
    {
        var index =
            (int)Math.Ceiling(
                percentile *
                sorted.Length) -
            1;

        return sorted[
            Math.Clamp(
                index,
                0,
                sorted.Length - 1)];
    }
}

internal sealed record WirePhaseResult(
    string Name,
    int Rounds,
    long ExpectedDeliveries,
    long ActualDeliveries,
    long SentBytes,
    long ReceivedBytes,
    double ElapsedMs,
    double DeliveriesPerSecond,
    double ReceivedBytesPerSecond,
    LatencySummary RoundLatencyMs)
{
    public static WirePhaseResult Empty(
        string name)
        => new(
            name,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            LatencySummary.From([]));
}

internal sealed record WireCombatRaceResult(
    int TargetEntityId,
    int AttackWaves,
    long ExpectedDeathDeliveries,
    long ActualDeathDeliveries,
    long SentBytes,
    long ReceivedBytes,
    double ElapsedMs)
{
    public static WireCombatRaceResult Empty()
        => new(
            0,
            0,
            0,
            0,
            0,
            0,
            0);
}

internal sealed record WireMixedSoakResult(
    int DurationSeconds,
    int SeededMonsters,
    int PositionRequests,
    int ChatRequests,
    int AttackRequests,
    long MoveBroadcasts,
    long ChatBroadcasts,
    long CombatEvents,
    long SentBytes,
    long ReceivedBytes,
    double ElapsedMs,
    ServerRuntimeSummary Runtime,
    string[] Errors)
{
    public static WireMixedSoakResult Empty()
        => new(
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            ServerRuntimeSummary.Empty(),
            []);
}

internal sealed record WireWorldAoiResult(
    int NearClients,
    int FarClients,
    int TargetEntityId,
    int ExpectedNearSpawns,
    int ActualNearSpawns,
    int FarSpawnLeaks,
    bool ReentrySpawnReceived,
    long SentBytes,
    long ReceivedBytes,
    double ElapsedMs,
    string[] Errors)
{
    public static WireWorldAoiResult Empty()
        => new(
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            0,
            0,
            0,
            []);
}

internal sealed record WireLoadScenarioResult(
    int Players,
    bool Success,
    int SetupSuccesses,
    int SetupFailures,
    int MapId,
    LatencySummary SetupLatencyMs,
    WirePhaseResult Movement,
    WirePhaseResult NearbyChat,
    WireCombatRaceResult CombatRace,
    WireWorldAoiResult WorldAoi,
    WireMixedSoakResult MixedSoak,
    string[] Errors);

internal static class WireLoadScenario
{
    public static async Task<WireLoadScenarioResult> RunAsync(
        string url,
        int playerCount,
        int rounds,
        int setupConcurrency,
        int mixedSeconds,
        TimeSpan timeout)
    {
        var clients =
            new WireLoadClient?[playerCount];
        var setupLatencies =
            new ConcurrentBag<double>();
        var errors =
            new ConcurrentBag<string>();
        var runId =
            Guid.NewGuid()
                .ToString("N")[..10];

        using var setupGate =
            new SemaphoreSlim(
                Math.Max(
                    1,
                    setupConcurrency));

        try
        {
            var setupTasks =
                Enumerable.Range(
                        0,
                        playerCount)
                    .Select(async index =>
                    {
                        await setupGate.WaitAsync();
                        var started =
                            Stopwatch.GetTimestamp();
                        try
                        {
                            clients[index] =
                                await WireLoadClient
                                    .ConnectAndEnterAsync(
                                        url,
                                        runId,
                                        index,
                                        timeout);

                            setupLatencies.Add(
                                Stopwatch.GetElapsedTime(
                                        started)
                                    .TotalMilliseconds);
                        }
                        catch (Exception ex)
                        {
                            errors.Add(
                                $"client={index}: {ex.Message}");
                        }
                        finally
                        {
                            setupGate.Release();
                        }
                    })
                    .ToArray();

            await Task.WhenAll(setupTasks);

            var active =
                clients
                    .Where(x => x is not null)
                    .Cast<WireLoadClient>()
                    .ToArray();

            if (active.Length != playerCount)
            {
                return Failure(
                    playerCount,
                    active,
                    setupLatencies,
                    errors);
            }

            var mapIds =
                active
                    .Select(x => x.MapId)
                    .Distinct()
                    .ToArray();

            if (mapIds.Length != 1)
            {
                errors.Add(
                    "clients did not enter one shared map: " +
                    string.Join(
                        ",",
                        mapIds));

                return Failure(
                    playerCount,
                    active,
                    setupLatencies,
                    errors);
            }

            // Let late presence/spawn packets drain before measurement starts.
            await Task.Delay(500);
            foreach (var client in active)
                client.ClearInbox();

            var movement =
                await MeasureMovementAsync(
                    active,
                    rounds,
                    timeout);

            await Task.Delay(250);
            foreach (var client in active)
                client.ClearInbox();

            var chat =
                await MeasureChatAsync(
                    active,
                    runId,
                    rounds,
                    timeout);

            await Task.Delay(250);
            foreach (var client in active)
                client.ClearInbox();

            var combat =
                await MeasureCombatRaceAsync(
                    active,
                    timeout);

            await Task.Delay(250);
            foreach (var client in active)
                client.ClearInbox();

            var worldAoi =
                await MeasureWorldAoiAsync(
                    active,
                    timeout);

            await Task.Delay(250);
            foreach (var client in active)
                client.ClearInbox();

            var mixed =
                await MeasureMixedSoakAsync(
                    active,
                    url,
                    runId,
                    mixedSeconds,
                    timeout);

            var success =
                movement.ActualDeliveries ==
                    movement.ExpectedDeliveries &&
                chat.ActualDeliveries ==
                    chat.ExpectedDeliveries &&
                combat.ActualDeathDeliveries ==
                    combat.ExpectedDeathDeliveries &&
                worldAoi.Errors.Length == 0 &&
                mixed.Errors.Length == 0;

            if (!success)
            {
                errors.Add(
                    "wire multiplayer/load validation mismatch");
            }

            return new WireLoadScenarioResult(
                playerCount,
                success,
                active.Length,
                playerCount - active.Length,
                mapIds[0],
                LatencySummary.From(
                    setupLatencies),
                movement,
                chat,
                combat,
                worldAoi,
                mixed,
                errors
                    .Concat(worldAoi.Errors)
                    .Concat(mixed.Errors)
                    .ToArray());
        }
        finally
        {
            var disposals =
                clients
                    .Where(x => x is not null)
                    .Cast<WireLoadClient>()
                    .Select(async x =>
                        await x.DisposeAsync())
                    .ToArray();

            await Task.WhenAll(disposals);
        }
    }

    private static long CountDirectedAoiPeers(WireLoadClient[] clients)
    {
        var count = 0L;
        for (var i = 0; i < clients.Length; i++)
        {
            for (var j = 0; j < clients.Length; j++)
            {
                if (i == j || clients[i].MapId != clients[j].MapId)
                    continue;
                var dx = (double)clients[i].X - clients[j].X;
                var dy = (double)clients[i].Y - clients[j].Y;
                if (dx * dx + dy * dy <=
                    PlayerSpatialAoi.EnterRadius * PlayerSpatialAoi.EnterRadius)
                    count++;
            }
        }
        return count;
    }

    private static async Task<WirePhaseResult> MeasureMovementAsync(
        WireLoadClient[] clients,
        int rounds,
        TimeSpan timeout)
    {
        foreach (var client in clients)
            client.StartMeasurement();

        var roundLatencies =
            new List<double>(rounds);
        var phaseStarted =
            Stopwatch.GetTimestamp();
        // Player movements are delivered only to ready nearby AOI peers.
        // All wire clients have completed WorldReady. Expected deliveries
        // depend on the actual starting positions, not just shared MapID.
        var expectedPerRound =
            CountDirectedAoiPeers(clients);

        try
        {
            for (var round = 0;
                 round < rounds;
                 round++)
            {
                var roundStarted =
                    Stopwatch.GetTimestamp();

                await Task.WhenAll(
                    clients.Select(
                        x => x.SendPositionAsync(
                            timeout)));

                var expected =
                    expectedPerRound *
                    (round + 1);

                await WaitUntilAsync(
                    () => clients.Sum(
                        x => x.MoveBroadcasts),
                    expected,
                    timeout,
                    "MoveEntity");

                roundLatencies.Add(
                    Stopwatch.GetElapsedTime(
                            roundStarted)
                        .TotalMilliseconds);

                // PacketRouter intentionally broadcasts position at <=10 Hz.
                if (round + 1 < rounds)
                    await Task.Delay(120);
            }

            var elapsed =
                Stopwatch.GetElapsedTime(
                    phaseStarted);
            var actual =
                clients.Sum(
                    x => x.MoveBroadcasts);
            var sentBytes =
                clients.Sum(
                    x => x.SentBytes);
            var receivedBytes =
                clients.Sum(
                    x => x.ReceivedBytes);
            var expectedTotal =
                expectedPerRound *
                rounds;

            if (actual != expectedTotal)
            {
                throw new InvalidOperationException(
                    $"movement conservation failed expected={expectedTotal} actual={actual}");
            }

            return BuildPhase(
                "SyncPosition->MoveEntity",
                rounds,
                expectedTotal,
                actual,
                sentBytes,
                receivedBytes,
                elapsed,
                roundLatencies);
        }
        finally
        {
            foreach (var client in clients)
                client.StopMeasurement();
        }
    }

    private static async Task<WirePhaseResult> MeasureChatAsync(
        WireLoadClient[] clients,
        string runId,
        int rounds,
        TimeSpan timeout)
    {
        foreach (var client in clients)
            client.StartMeasurement();

        var roundLatencies =
            new List<double>(rounds);
        var phaseStarted =
            Stopwatch.GetTimestamp();

        // Nearby chat is local AOI only, plus one own-response per sender.
        // World/faction/club channels keep their independent routing rules.
        var expectedPerRound =
            clients.Length + CountDirectedAoiPeers(clients);

        try
        {
            for (var round = 0;
                 round < rounds;
                 round++)
            {
                var roundStarted =
                    Stopwatch.GetTimestamp();

                await Task.WhenAll(
                    clients.Select(
                        x => x.SendNearbyChatAsync(
                            $"wire-load-{runId}-{round:D2}-{x.Index:D3}",
                            timeout)));

                var expected =
                    expectedPerRound *
                    (round + 1);

                await WaitUntilAsync(
                    () => clients.Sum(
                        x => x.ChatBroadcasts),
                    expected,
                    timeout,
                    "ChatBroadcast");

                roundLatencies.Add(
                    Stopwatch.GetElapsedTime(
                            roundStarted)
                        .TotalMilliseconds);
            }

            var elapsed =
                Stopwatch.GetElapsedTime(
                    phaseStarted);
            var actual =
                clients.Sum(
                    x => x.ChatBroadcasts);
            var sentBytes =
                clients.Sum(
                    x => x.SentBytes);
            var receivedBytes =
                clients.Sum(
                    x => x.ReceivedBytes);
            var expectedTotal =
                expectedPerRound *
                rounds;

            if (actual != expectedTotal)
            {
                throw new InvalidOperationException(
                    $"chat conservation failed expected={expectedTotal} actual={actual}");
            }

            return BuildPhase(
                "ChatMessage->ChatBroadcast",
                rounds,
                expectedTotal,
                actual,
                sentBytes,
                receivedBytes,
                elapsed,
                roundLatencies);
        }
        finally
        {
            foreach (var client in clients)
                client.StopMeasurement();
        }
    }

    private static async Task<WireCombatRaceResult> MeasureCombatRaceAsync(
        WireLoadClient[] clients,
        TimeSpan timeout)
    {
        // Test seed only: normal AttackEntity remains the path under race.
        await Task.WhenAll(
            clients.Select(
                x => x.PrepareCombatAsync(
                    timeout)));

        await Task.Delay(150);
        foreach (var client in clients)
            client.ClearInbox();

        var anchor =
            clients[0];
        var targetEntityId =
            await anchor.SpawnMonsterAsync(
                245,
                anchor.X + 20,
                anchor.Y,
                timeout);

        await Task.Delay(150);
        foreach (var client in clients)
            client.ClearInbox();

        foreach (var client in clients)
            client.StartMeasurement(
                targetEntityId);

        var started =
            Stopwatch.GetTimestamp();
        var waves = 0;
        var expectedDeaths =
            (long)clients.Length;

        try
        {
            while (clients.Sum(
                       x => x.CombatDeaths) <
                   expectedDeaths &&
                   waves < 20)
            {
                waves++;

                await Task.WhenAll(
                    clients.Select(
                        x => x.SendAttackAsync(
                            targetEntityId,
                            timeout)));

                await Task.Delay(25);
            }

            await WaitUntilAsync(
                () => clients.Sum(
                    x => x.CombatDeaths),
                expectedDeaths,
                timeout,
                "EntityDeath");

            // Give any duplicate death packet enough time to surface.
            await Task.Delay(100);

            var actualDeaths =
                clients.Sum(
                    x => x.CombatDeaths);
            var elapsed =
                Stopwatch.GetElapsedTime(
                    started);
            var sentBytes =
                clients.Sum(
                    x => x.SentBytes);
            var receivedBytes =
                clients.Sum(
                    x => x.ReceivedBytes);

            if (actualDeaths != expectedDeaths)
            {
                throw new InvalidOperationException(
                    $"combat death conservation failed expected={expectedDeaths} actual={actualDeaths}");
            }

            return new WireCombatRaceResult(
                targetEntityId,
                waves,
                expectedDeaths,
                actualDeaths,
                sentBytes,
                receivedBytes,
                elapsed.TotalMilliseconds);
        }
        finally
        {
            foreach (var client in clients)
                client.StopMeasurement();
        }
    }

    private static async Task<WireWorldAoiResult> MeasureWorldAoiAsync(
        WireLoadClient[] clients,
        TimeSpan timeout)
    {
        var anchor = clients[0];
        var nearCount =
            Math.Max(
                1,
                clients.Length / 2);
        var near =
            clients.Take(nearCount)
                .ToArray();
        var far =
            clients.Skip(nearCount)
                .ToArray();

        var baseX = anchor.X;
        var baseY = anchor.Y;
        var mapId = anchor.MapId;
        var farX =
            baseX >= 6_000
                ? baseX - 5_000
                : baseX + 5_000;

        // Put every client in a deterministic near/far group through the real
        // encrypted GM teleport path. This avoids depending on map collision
        // routing for a synthetic load-test position.
        await Task.WhenAll(
            near.Select(
                (client, index) =>
                    client.TeleportAsync(
                        mapId,
                        baseX +
                            (index % 8) * 20,
                        baseY +
                            (index / 8) * 10,
                        timeout)));

        await Task.WhenAll(
            far.Select(
                (client, index) =>
                    client.TeleportAsync(
                        mapId,
                        farX +
                            (index % 8) * 20,
                        baseY +
                            (index / 8) * 10,
                        timeout)));

        await Task.Delay(200);
        foreach (var client in clients)
        {
            client.ClearInbox();
            client.StartMeasurement();
        }

        var started =
            Stopwatch.GetTimestamp();
        var errors =
            new List<string>();
        var targetEntityId = 0;
        var expectedNearSpawns =
            Math.Max(
                0,
                near.Length - 1);
        var actualNearSpawns = 0;
        var farLeaks = 0;
        var reentrySpawn = false;

        try
        {
            targetEntityId =
                await anchor.SpawnMonsterAsync(
                    245,
                    baseX + 80,
                    baseY,
                    timeout);

            var nearResults =
                await Task.WhenAll(
                    near.Skip(1)
                        .Select(
                            x => x.TryWaitForEntityPacketAsync(
                                "SpawnEntity",
                                targetEntityId,
                                timeout)));

            actualNearSpawns =
                nearResults.Count(x => x);

            var absenceWindow =
                TimeSpan.FromMilliseconds(
                    Math.Clamp(
                        timeout.TotalMilliseconds / 5,
                        500,
                        1_500));

            var farResults =
                await Task.WhenAll(
                    far.Select(
                        x => x.TryWaitForEntityPacketAsync(
                            "SpawnEntity",
                            targetEntityId,
                            absenceWindow)));

            farLeaks =
                farResults.Count(x => x);

            if (actualNearSpawns !=
                expectedNearSpawns)
            {
                errors.Add(
                    $"world AOI near spawn mismatch expected={expectedNearSpawns} actual={actualNearSpawns}");
            }

            if (farLeaks != 0)
            {
                errors.Add(
                    $"world AOI leaked target spawn to {farLeaks}/{far.Length} distant clients");
            }

            if (far.Length > 0)
            {
                var returning =
                    far[0];

                await returning.TeleportAsync(
                    mapId,
                    baseX + 120,
                    baseY,
                    timeout);

                reentrySpawn =
                    await returning.TryWaitForEntityPacketAsync(
                        "SpawnEntity",
                        targetEntityId,
                        timeout);

                if (!reentrySpawn)
                {
                    errors.Add(
                        "world AOI distant client did not receive monster spawn after entering range");
                }
            }

            return new WireWorldAoiResult(
                near.Length,
                far.Length,
                targetEntityId,
                expectedNearSpawns,
                actualNearSpawns,
                farLeaks,
                reentrySpawn,
                clients.Sum(
                    x => x.SentBytes),
                clients.Sum(
                    x => x.ReceivedBytes),
                Stopwatch.GetElapsedTime(
                        started)
                    .TotalMilliseconds,
                errors.ToArray());
        }
        finally
        {
            foreach (var client in clients)
                client.StopMeasurement();

            // Restore one compact group before the normal mixed combat soak so
            // its historical workload remains comparable across AOI changes.
            await Task.WhenAll(
                clients.Select(
                    (client, index) =>
                        client.TeleportAsync(
                            mapId,
                            baseX +
                                (index % 10) * 18,
                            baseY +
                                (index / 10) * 10,
                            timeout)));
        }
    }

    private static async Task<WireMixedSoakResult> MeasureMixedSoakAsync(
        WireLoadClient[] clients,
        string url,
        string runId,
        int mixedSeconds,
        TimeSpan timeout)
    {
        if (mixedSeconds <= 0)
            return WireMixedSoakResult.Empty();

        var anchor = clients[0];
        var monsterCount = Math.Min(
            20,
            Math.Max(
                5,
                clients.Length / 5));
        var monsterIds =
            new List<int>(monsterCount);

        for (var i = 0;
             i < monsterCount;
             i++)
        {
            monsterIds.Add(
                await anchor.SpawnMonsterAsync(
                    245,
                    anchor.X +
                        20 +
                        (i % 5) * 14,
                    anchor.Y +
                        (i / 5) * 14,
                    timeout));
        }

        // Give the 250 ms SharedMapAiTicker at least one full pass so /health
        // captures a real active-session/monster baseline before attacks start.
        await Task.Delay(400);
        foreach (var client in clients)
            client.ClearInbox();

        foreach (var client in clients)
        {
            client.StartMeasurement(
                dropCombatNoise: true);
        }

        var duration =
            TimeSpan.FromSeconds(
                mixedSeconds);
        var healthTask =
            ServerHealthSampler.CaptureAsync(
                url,
                duration,
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromSeconds(
                    Math.Clamp(
                        timeout.TotalSeconds,
                        1,
                        5)));

        var started =
            Stopwatch.GetTimestamp();
        var tick = 0;
        var positionRequests = 0;
        var chatRequests = 0;
        var attackRequests = 0;

        try
        {
            while (Stopwatch.GetElapsedTime(
                       started) <
                   duration)
            {
                await Task.WhenAll(
                    clients.Select(
                        x => x.SendPositionAsync(
                            timeout)));
                positionRequests +=
                    clients.Length;

                if (tick % 4 == 0)
                {
                    var chatters =
                        clients.Take(
                                Math.Min(
                                    5,
                                    clients.Length))
                            .ToArray();
                    await Task.WhenAll(
                        chatters.Select(
                            x => x.SendNearbyChatAsync(
                                $"wire-mixed-{runId}-{tick:D4}-{x.Index:D3}",
                                timeout)));
                    chatRequests +=
                        chatters.Length;
                }

                // Let monsters spend the first second in normal AI mode, then
                // mix real player AttackEntity traffic into the same map.
                if (tick >= 4 &&
                    tick % 2 == 0)
                {
                    var attackers =
                        clients.Take(
                                Math.Min(
                                    3,
                                    clients.Length))
                            .ToArray();
                    var attacks =
                        attackers.Select(
                            (client, index) =>
                                client.SendAttackAsync(
                                    monsterIds[
                                        (tick / 2 + index) %
                                        monsterIds.Count],
                                    timeout))
                            .ToArray();
                    await Task.WhenAll(attacks);
                    attackRequests +=
                        attacks.Length;
                }

                tick++;
                await Task.Delay(250);
            }

            var health =
                await healthTask;
            var elapsed =
                Stopwatch.GetElapsedTime(
                    started);
            var runtimeErrors =
                new List<string>(
                    health.Errors);

            if (health.Summary.Samples < 2)
            {
                runtimeErrors.Add(
                    $"health sampling returned only {health.Summary.Samples} sample(s)");
            }

            if (health.Summary.MaxActiveSessions <
                clients.Length)
            {
                runtimeErrors.Add(
                    $"shared AI observed only {health.Summary.MaxActiveSessions}/{clients.Length} active sessions");
            }

            if (health.Summary.MaxMonsterCount <
                monsterCount)
            {
                runtimeErrors.Add(
                    $"shared AI observed only {health.Summary.MaxMonsterCount}/{monsterCount} seeded monsters");
            }

            if (health.Summary.MaxAoiReadyPlayers <
                clients.Length)
            {
                runtimeErrors.Add(
                    $"player AOI observed only {health.Summary.MaxAoiReadyPlayers}/{clients.Length} ready players");
            }

            if (health.Summary.MaxAoiWorldObservers <
                clients.Length)
            {
                runtimeErrors.Add(
                    $"world AOI observed only {health.Summary.MaxAoiWorldObservers}/{clients.Length} ready observers");
            }

            if (health.Summary.MaxAoiWorldEntities <
                monsterCount)
            {
                runtimeErrors.Add(
                    $"world AOI tracked only {health.Summary.MaxAoiWorldEntities}/{monsterCount} seeded monsters");
            }

            return new WireMixedSoakResult(
                mixedSeconds,
                monsterCount,
                positionRequests,
                chatRequests,
                attackRequests,
                clients.Sum(
                    x => x.MoveBroadcasts),
                clients.Sum(
                    x => x.ChatBroadcasts),
                clients.Sum(
                    x => x.CombatEvents),
                clients.Sum(
                    x => x.SentBytes),
                clients.Sum(
                    x => x.ReceivedBytes),
                elapsed.TotalMilliseconds,
                health.Summary,
                runtimeErrors.ToArray());
        }
        finally
        {
            foreach (var client in clients)
                client.StopMeasurement();
        }
    }

    private static WirePhaseResult BuildPhase(
        string name,
        int rounds,
        long expected,
        long actual,
        long sentBytes,
        long receivedBytes,
        TimeSpan elapsed,
        IReadOnlyCollection<double> latencies)
    {
        var seconds =
            elapsed.TotalSeconds;

        return new WirePhaseResult(
            name,
            rounds,
            expected,
            actual,
            sentBytes,
            receivedBytes,
            elapsed.TotalMilliseconds,
            seconds <= 0
                ? 0
                : actual / seconds,
            seconds <= 0
                ? 0
                : receivedBytes / seconds,
            LatencySummary.From(
                latencies));
    }

    private static async Task WaitUntilAsync(
        Func<long> current,
        long expected,
        TimeSpan timeout,
        string description)
    {
        var started =
            Stopwatch.GetTimestamp();

        while (current() < expected)
        {
            if (Stopwatch.GetElapsedTime(
                    started) >= timeout)
            {
                throw new TimeoutException(
                    $"{description} timed out expected={expected} actual={current()}");
            }

            await Task.Delay(5);
        }
    }

    private static WireLoadScenarioResult Failure(
        int playerCount,
        IReadOnlyCollection<WireLoadClient> active,
        IEnumerable<double> setupLatencies,
        IEnumerable<string> errors)
        => new(
            playerCount,
            false,
            active.Count,
            playerCount - active.Count,
            active
                .Select(x => x.MapId)
                .FirstOrDefault(),
            LatencySummary.From(
                setupLatencies),
            WirePhaseResult.Empty(
                "SyncPosition->MoveEntity"),
            WirePhaseResult.Empty(
                "ChatMessage->ChatBroadcast"),
            WireCombatRaceResult.Empty(),
            WireWorldAoiResult.Empty(),
            WireMixedSoakResult.Empty(),
            errors.ToArray());
}
