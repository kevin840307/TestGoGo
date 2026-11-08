using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SGO.Server.Application.Messaging;
using SGO.Server.Infrastructure.Persistence.Sqlite;

var options = Options.Parse(args);
var sizes = options.Sizes.Count == 0
    ? new[] { 10, 50, 100 }
    : options.Sizes.ToArray();

// --aoi-only avoids account creation/PBKDF2 and SQLite contention. It
// exercises moving PlayerSpatialAoi state independently of the transport,
// then the real WireLoadProbe can measure encrypted socket throughput.
var movingAoiRows = new List<object>();
if (options.AoiSteps > 0)
{
    foreach (var size in sizes)
    {
        foreach (var dense in new[] { false, true })
        {
            Console.Error.WriteLine(
                $"[AOI_MOVING] players={size} layout={(dense ? "dense" : "groups-of-20")} steps={options.AoiSteps}");
            movingAoiRows.Add(
                PlayerAoiMovingLoadScenario.Measure(
                    size, options.AoiSteps, dense));
        }
    }
}

var fanoutRows = new List<object>();
var dbRows = new List<object>();
var distinctPlayerRows = new List<object>();
if (!options.AoiOnly)
{
    foreach (var size in sizes)
        fanoutRows.Add(MeasureFanout(size, options.FanoutIterations));

    foreach (var size in sizes)
        dbRows.Add(await MeasureSqliteWrites(size, options.DbOpsPerWorker));

    foreach (var size in sizes)
        distinctPlayerRows.Add(
            await DistinctPlayerSqliteLoadScenario.Measure(
                size, options.DbOpsPerWorker));
}

object? healthSamples = null;
if (!string.IsNullOrWhiteSpace(
        options.HealthUrl))
{
    healthSamples =
        await SampleHealth(
            options.HealthUrl!,
            options.HealthSamples,
            options.HealthIntervalMs);
}

var report = new
{
    GeneratedUtc = DateTimeOffset.UtcNow,
    Machine = new
    {
        ProcessorCount =
            Environment.ProcessorCount,
        Framework =
            System.Runtime.InteropServices.RuntimeInformation
                .FrameworkDescription,
        Os =
            System.Runtime.InteropServices.RuntimeInformation
                .OSDescription
    },
    Configuration = new
    {
        Sizes = sizes,
        options.FanoutIterations,
        options.DbOpsPerWorker,
        options.AoiOnly,
        options.AoiSteps,
        options.HealthUrl,
        options.HealthSamples,
        options.HealthIntervalMs
    },
    Fanout = fanoutRows,
    AoiMovingPlayers = movingAoiRows,
    SqliteWriteContention = dbRows,
    SqliteDistinctPlayerWrites = distinctPlayerRows,
    LiveHealth = healthSamples,
    Notes = new[]
    {
        "Fanout measures ServerBroadcastHub same-map delivery cost without WebSocket serialization.",
        "SQLite contention intentionally writes one character row from N workers; it is a worst-case lock-contention probe, not a normal session pattern.",
        "SqliteDistinctPlayerWrites uses N different characters so normal 10/50/100-player persistence can be compared with the same-row worst case.",
        "AoiMovingPlayers changes every synthetic player's coordinates in one map and checks enter/exit boundary, per-move fanout, P95/P99, CPU and allocations for dense/spread layouts.",
        "AoiMovingPlayers measures the in-process PlayerSpatialAoi index only, not real movement validation, WebSocket bandwidth, network queue, SQLite or Godot.",
        "Use --aoi-only --aoi-steps 50 to measure 100-1000 moving virtual AOI players without PBKDF2/SQLite setup.",
        "SharedMapAI correctness is covered separately by SharedMapAiConcurrencySmokeTests. Live --health sampling reports real tick elapsed time under an externally generated load.",
        "This is a capacity measurement, not a correctness certificate or a fixed supported-player guarantee."
    }
};

var json =
    JsonSerializer.Serialize(
        report,
        new JsonSerializerOptions
        {
            WriteIndented = true
        });

Console.WriteLine(json);
if (!string.IsNullOrWhiteSpace(
        options.Output))
{
    var output =
        Path.GetFullPath(
            options.Output!);
    Directory.CreateDirectory(
        Path.GetDirectoryName(output)!);
    await File.WriteAllTextAsync(
        output,
        json + Environment.NewLine);
}

static object MeasureFanout(
    int playerCount,
    int iterations)
{
    var hub =
        new ServerBroadcastHub();
    var registrations =
        Enumerable.Range(0, playerCount)
            .Select(_ =>
                hub.Register())
            .ToArray();

    for (var i = 0;
         i < registrations.Length;
         i++)
    {
        hub.UpdatePresence(
            registrations[i].ConnectionId,
            15,
            10_000 + i,
            20_000 + i,
            8_000 + i,
            11_000);
    }

    var packet = new
    {
        Type = "MoveEntity",
        EntityID = 12345,
        X = 8000,
        Y = 11000,
        Dir = "1",
        SpeedTime = 0.10
    };
    var packetBytes =
        Encoding.UTF8.GetByteCount(
            JsonSerializer.Serialize(packet));

    for (var i = 0; i < 20; i++)
    {
        hub.PublishToMap(
            15,
            packet,
            registrations[0].ConnectionId);
    }
    foreach (var registration in registrations)
    {
        while (registration.Queue.TryDequeue(
                   out _))
        {
        }
    }

    var latencies =
        new double[iterations];
    var totalStart =
        Stopwatch.GetTimestamp();

    for (var i = 0;
         i < iterations;
         i++)
    {
        var start =
            Stopwatch.GetTimestamp();
        hub.PublishToMap(
            15,
            packet,
            registrations[0].ConnectionId);
        latencies[i] =
            Stopwatch.GetElapsedTime(start)
                .TotalMilliseconds;
    }

    var elapsed =
        Stopwatch.GetElapsedTime(totalStart);
    var expectedDeliveries =
        (long)iterations *
        Math.Max(
            0,
            playerCount - 1);
    var actualDeliveries =
        registrations
            .Skip(1)
            .Sum(x =>
                (long)x.Queue.Count);
    var senderDeliveries =
        registrations[0].Queue.Count;

    if (actualDeliveries !=
            expectedDeliveries ||
        senderDeliveries != 0)
    {
        throw new InvalidOperationException(
            $"fanout conservation failed players={playerCount}: " +
            $"expected={expectedDeliveries} actual={actualDeliveries} " +
            $"sender={senderDeliveries}");
    }

    return new
    {
        Players = playerCount,
        Iterations = iterations,
        RecipientsPerPublish =
            Math.Max(
                0,
                playerCount - 1),
        TotalDeliveries =
            actualDeliveries,
        ApproxPayloadBytes =
            actualDeliveries *
            packetBytes,
        ElapsedMs =
            elapsed.TotalMilliseconds,
        PublishesPerSecond =
            Rate(
                iterations,
                elapsed.TotalSeconds),
        DeliveriesPerSecond =
            Rate(
                actualDeliveries,
                elapsed.TotalSeconds),
        LatencyMs = Summary(latencies)
    };
}

static async Task<object> MeasureSqliteWrites(
    int concurrency,
    int operationsPerWorker)
{
    var temp =
        Path.Combine(
            Path.GetTempPath(),
            "sgo-load-" +
            Guid.NewGuid()
                .ToString("N"));
    Directory.CreateDirectory(temp);

    try
    {
        var store =
            new SqliteGameStore(
                Path.Combine(
                    temp,
                    "game.db"));

        var username =
            "load-" +
            Guid.NewGuid()
                .ToString("N");
        var password =
            "load-test-password";

        var registered =
            store.Register(
                username,
                password,
                "");
        if (!registered.Success)
        {
            throw new InvalidOperationException(
                "load probe account registration failed: " +
                registered.Message);
        }

        var accountId =
            store.Login(
                username,
                password)
            ?? throw new InvalidOperationException(
                "load probe login failed");

        using var create =
            JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new
                    {
                        Name =
                            "Load" +
                            Guid.NewGuid()
                                .ToString("N")[..8],
                        Password = "1234",
                        Race = 1,
                        Hair = 1,
                        HairColor = 1,
                        Str = 5,
                        Dex = 5,
                        Con = 5,
                        Wis = 5
                    }));

        var created =
            store.Create(
                accountId,
                create.RootElement);
        if (!created.Success)
        {
            throw new InvalidOperationException(
                "load probe character creation failed: " +
                created.Message);
        }

        var characterId =
            store.Characters(accountId)
                .Select(x =>
                    Convert.ToInt64(
                        x["DBID"]))
                .First(x => x > 0);

        var latencies =
            new ConcurrentBag<double>();
        var failures = 0;
        var totalStart =
            Stopwatch.GetTimestamp();

        var workers =
            Enumerable.Range(
                    0,
                    concurrency)
                .Select(worker =>
                    Task.Run(() =>
                    {
                        for (var op = 0;
                             op < operationsPerWorker;
                             op++)
                        {
                            var started =
                                Stopwatch.GetTimestamp();
                            var ok =
                                store.UpdatePosition(
                                    accountId,
                                    characterId,
                                    8000 +
                                    worker *
                                    10 +
                                    op,
                                    11000 +
                                    worker *
                                    10 +
                                    op);
                            latencies.Add(
                                Stopwatch.GetElapsedTime(
                                        started)
                                    .TotalMilliseconds);
                            if (!ok)
                                Interlocked.Increment(
                                    ref failures);
                        }
                    }))
                .ToArray();

        await Task.WhenAll(workers);
        var elapsed =
            Stopwatch.GetElapsedTime(totalStart);
        var samples =
            latencies.ToArray();
        var operations =
            concurrency *
            operationsPerWorker;

        if (failures != 0 ||
            samples.Length != operations)
        {
            throw new InvalidOperationException(
                $"SQLite contention probe failed concurrency={concurrency}: " +
                $"operations={operations} samples={samples.Length} failures={failures}");
        }

        return new
        {
            Concurrency =
                concurrency,
            OperationsPerWorker =
                operationsPerWorker,
            Operations =
                operations,
            Failures =
                failures,
            ElapsedMs =
                elapsed.TotalMilliseconds,
            OperationsPerSecond =
                Rate(
                    operations,
                    elapsed.TotalSeconds),
            LatencyMs =
                Summary(samples)
        };
    }
    finally
    {
        try
        {
            if (Directory.Exists(temp))
                Directory.Delete(
                    temp,
                    recursive: true);
        }
        catch
        {
        }
    }
}

static async Task<object> SampleHealth(
    string healthUrl,
    int sampleCount,
    int intervalMs)
{
    using var http =
        new HttpClient
        {
            Timeout =
                TimeSpan.FromSeconds(5)
        };

    var aiElapsed =
        new List<double>();
    var workingSet =
        new List<double>();
    var gcHeap =
        new List<double>();
    var cpu =
        new List<double>();

    for (var i = 0;
         i < sampleCount;
         i++)
    {
        using var response =
            await http.GetAsync(
                healthUrl);
        response.EnsureSuccessStatusCode();

        using var doc =
            JsonDocument.Parse(
                await response.Content
                    .ReadAsStringAsync());
        var root =
            doc.RootElement;

        if (root.TryGetProperty(
                "sharedAi",
                out var ai) &&
            ai.TryGetProperty(
                "elapsedMs",
                out var elapsed))
        {
            aiElapsed.Add(
                elapsed.GetDouble());
        }

        if (root.TryGetProperty(
                "runtime",
                out var runtime))
        {
            if (runtime.TryGetProperty(
                    "workingSetBytes",
                    out var ws))
            {
                workingSet.Add(
                    ws.GetDouble());
            }

            if (runtime.TryGetProperty(
                    "gcHeapBytes",
                    out var heap))
            {
                gcHeap.Add(
                    heap.GetDouble());
            }

            if (runtime.TryGetProperty(
                    "cpuTotalMilliseconds",
                    out var totalCpu))
            {
                cpu.Add(
                    totalCpu.GetDouble());
            }
        }

        if (i + 1 < sampleCount)
        {
            await Task.Delay(
                Math.Max(
                    1,
                    intervalMs));
        }
    }

    double? cpuUtilizationPercent = null;
    if (cpu.Count >= 2 &&
        sampleCount > 1)
    {
        var wallMs =
            (sampleCount - 1) *
            Math.Max(
                1,
                intervalMs);
        var cpuDelta =
            cpu[^1] -
            cpu[0];
        cpuUtilizationPercent =
            wallMs <= 0
                ? null
                : cpuDelta /
                  wallMs /
                  Math.Max(
                      1,
                      Environment.ProcessorCount) *
                  100.0;
    }

    return new
    {
        Url = healthUrl,
        Samples = sampleCount,
        SharedAiElapsedMs =
            Summary(
                aiElapsed.ToArray()),
        WorkingSetBytes =
            Summary(
                workingSet.ToArray()),
        GcHeapBytes =
            Summary(
                gcHeap.ToArray()),
        CpuUtilizationPercent =
            cpuUtilizationPercent
    };
}

static object Summary(
    double[] values)
{
    if (values.Length == 0)
    {
        return new
        {
            Count = 0,
            Min = 0.0,
            P50 = 0.0,
            P95 = 0.0,
            P99 = 0.0,
            Max = 0.0,
            Average = 0.0
        };
    }

    Array.Sort(values);
    return new
    {
        Count = values.Length,
        Min = values[0],
        P50 = Percentile(values, 0.50),
        P95 = Percentile(values, 0.95),
        P99 = Percentile(values, 0.99),
        Max = values[^1],
        Average = values.Average()
    };
}

static double Percentile(
    double[] sorted,
    double percentile)
{
    if (sorted.Length == 0)
        return 0;

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

static double Rate(
    long count,
    double seconds)
    => seconds <= 0
        ? 0
        : count /
          seconds;

internal sealed record Options(
    IReadOnlyList<int> Sizes,
    int FanoutIterations,
    int DbOpsPerWorker,
    bool AoiOnly,
    int AoiSteps,
    string? Output,
    string? HealthUrl,
    int HealthSamples,
    int HealthIntervalMs)
{
    public static Options Parse(
        string[] args)
    {
        var sizes =
            new List<int>();
        var fanoutIterations = 500;
        var dbOps = 10;
        var aoiOnly = false;
        var aoiSteps = 0;
        string? output = null;
        string? health = null;
        var healthSamples = 20;
        var healthIntervalMs = 250;

        for (var i = 0;
             i < args.Length;
             i++)
        {
            switch (args[i])
            {
                case "--sizes"
                    when i + 1 < args.Length:
                    sizes = args[++i]
                        .Split(
                            ',',
                            StringSplitOptions.RemoveEmptyEntries |
                            StringSplitOptions.TrimEntries)
                        .Select(int.Parse)
                        .Where(x => x > 0)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();
                    break;

                case "--fanout-iterations"
                    when i + 1 < args.Length:
                    fanoutIterations =
                        Math.Max(
                            1,
                            int.Parse(
                                args[++i]));
                    break;

                case "--db-ops"
                    when i + 1 < args.Length:
                    dbOps =
                        Math.Max(
                            1,
                            int.Parse(
                                args[++i]));
                    break;
                case "--aoi-only":
                    aoiOnly = true;
                    break;
                case "--aoi-steps"
                    when i + 1 < args.Length:
                    aoiSteps = Math.Clamp(int.Parse(args[++i]), 1, 1000);
                    break;

                case "--output"
                    when i + 1 < args.Length:
                    output =
                        args[++i];
                    break;

                case "--health"
                    when i + 1 < args.Length:
                    health =
                        args[++i];
                    break;

                case "--health-samples"
                    when i + 1 < args.Length:
                    healthSamples =
                        Math.Max(
                            2,
                            int.Parse(
                                args[++i]));
                    break;

                case "--health-interval-ms"
                    when i + 1 < args.Length:
                    healthIntervalMs =
                        Math.Max(
                            1,
                            int.Parse(
                                args[++i]));
                    break;
            }
        }

        if (aoiOnly && aoiSteps == 0)
            aoiSteps = 20;

        return new Options(
            sizes,
            fanoutIterations,
            dbOps,
            aoiOnly,
            aoiSteps,
            output,
            health,
            healthSamples,
            healthIntervalMs);
    }
}
