using System.Diagnostics;
using System.Text.Json;

internal sealed record ServerHealthSample(
    DateTimeOffset SampledUtc,
    long WorkingSetBytes,
    long GcHeapBytes,
    double CpuTotalMilliseconds,
    double AiElapsedMs,
    int AiActiveSessions,
    int AiActiveMaps,
    int AiTickedMaps,
    int AiMonsterCount,
    int AiAssignedMonsterCount);

internal sealed record ServerRuntimeSummary(
    int Samples,
    long StartWorkingSetBytes,
    long EndWorkingSetBytes,
    long MaxWorkingSetBytes,
    long MaxGcHeapBytes,
    double CpuTotalDeltaMs,
    double CpuPercentOfMachine,
    LatencySummary AiTickElapsedMs,
    int MaxActiveSessions,
    int MaxActiveMaps,
    int MaxMonsterCount,
    int MaxAssignedMonsterCount)
{
    public static ServerRuntimeSummary Empty()
        => new(
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            LatencySummary.From([]),
            0,
            0,
            0,
            0);

    public static ServerRuntimeSummary From(
        IReadOnlyList<ServerHealthSample> samples)
    {
        if (samples.Count == 0)
            return Empty();

        var ordered = samples
            .OrderBy(x => x.SampledUtc)
            .ToArray();
        var first = ordered[0];
        var last = ordered[^1];
        var elapsedMs = Math.Max(
            1,
            (last.SampledUtc - first.SampledUtc)
                .TotalMilliseconds);
        var cpuDelta = Math.Max(
            0,
            last.CpuTotalMilliseconds -
            first.CpuTotalMilliseconds);
        var processorCount = Math.Max(
            1,
            Environment.ProcessorCount);

        return new ServerRuntimeSummary(
            ordered.Length,
            first.WorkingSetBytes,
            last.WorkingSetBytes,
            ordered.Max(x => x.WorkingSetBytes),
            ordered.Max(x => x.GcHeapBytes),
            cpuDelta,
            cpuDelta /
                elapsedMs /
                processorCount *
                100.0,
            LatencySummary.From(
                ordered.Select(x => x.AiElapsedMs)),
            ordered.Max(x => x.AiActiveSessions),
            ordered.Max(x => x.AiActiveMaps),
            ordered.Max(x => x.AiMonsterCount),
            ordered.Max(x => x.AiAssignedMonsterCount));
    }
}

internal sealed record ServerHealthCapture(
    ServerRuntimeSummary Summary,
    string[] Errors);

internal static class ServerHealthSampler
{
    public static async Task<ServerHealthCapture> CaptureAsync(
        string webSocketUrl,
        TimeSpan duration,
        TimeSpan interval,
        TimeSpan requestTimeout)
    {
        if (duration <= TimeSpan.Zero)
        {
            return new ServerHealthCapture(
                ServerRuntimeSummary.Empty(),
                []);
        }

        var healthUri = BuildHealthUri(
            webSocketUrl);
        var samples =
            new List<ServerHealthSample>();
        var errors =
            new List<string>();
        using var http =
            new HttpClient();
        var started =
            Stopwatch.GetTimestamp();

        while (Stopwatch.GetElapsedTime(
                   started) <
               duration)
        {
            try
            {
                using var deadline =
                    new CancellationTokenSource(
                        requestTimeout);
                using var response =
                    await http.GetAsync(
                        healthUri,
                        deadline.Token);
                response.EnsureSuccessStatusCode();

                var json =
                    await response.Content
                        .ReadAsStringAsync(
                            deadline.Token);
                using var document =
                    JsonDocument.Parse(json);
                samples.Add(
                    Parse(
                        document.RootElement));
            }
            catch (Exception ex)
            {
                errors.Add(
                    $"{DateTimeOffset.UtcNow:O}: {ex.GetType().Name}: {ex.Message}");
            }

            var remaining =
                duration -
                Stopwatch.GetElapsedTime(
                    started);
            if (remaining <=
                TimeSpan.Zero)
            {
                break;
            }

            var delay =
                remaining < interval
                    ? remaining
                    : interval;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay);
        }

        return new ServerHealthCapture(
            ServerRuntimeSummary.From(samples),
            errors.ToArray());
    }

    private static Uri BuildHealthUri(
        string webSocketUrl)
    {
        var source =
            new Uri(webSocketUrl);
        var builder =
            new UriBuilder(source)
            {
                Scheme =
                    source.Scheme.Equals(
                        "wss",
                        StringComparison.OrdinalIgnoreCase)
                        ? "https"
                        : "http",
                Path = "/health",
                Query = "",
                Fragment = ""
            };

        return builder.Uri;
    }

    private static ServerHealthSample Parse(
        JsonElement root)
    {
        var runtime =
            Property(
                root,
                "runtime");
        var ai =
            Property(
                root,
                "sharedMapAi");

        return new ServerHealthSample(
            DateTimeOffset.UtcNow,
            Number(
                runtime,
                "workingSetBytes")
                .GetInt64(),
            Number(
                runtime,
                "gcHeapBytes")
                .GetInt64(),
            Number(
                runtime,
                "cpuTotalMilliseconds")
                .GetDouble(),
            Number(
                ai,
                "elapsedMs")
                .GetDouble(),
            Number(
                ai,
                "activeSessions")
                .GetInt32(),
            Number(
                ai,
                "activeMaps")
                .GetInt32(),
            Number(
                ai,
                "tickedMaps")
                .GetInt32(),
            Number(
                ai,
                "monsterCount")
                .GetInt32(),
            Number(
                ai,
                "assignedMonsterCount")
                .GetInt32());
    }

    private static JsonElement Number(
        JsonElement root,
        string name)
    {
        var value =
            Property(
                root,
                name);
        if (value.ValueKind !=
            JsonValueKind.Number)
        {
            throw new InvalidOperationException(
                $"health field {name} is not numeric");
        }

        return value;
    }

    private static JsonElement Property(
        JsonElement root,
        string name)
    {
        if (root.TryGetProperty(
                name,
                out var direct))
        {
            return direct;
        }

        foreach (var property in
                 root.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new InvalidOperationException(
            $"health field {name} is missing");
    }
}
