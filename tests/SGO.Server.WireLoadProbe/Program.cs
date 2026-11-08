using System.Text.Json;

var options = Options.Parse(args);
var results = new List<WireLoadScenarioResult>();

foreach (var size in options.Sizes)
{
    Console.Error.WriteLine(
        $"[WIRE_LOAD] players={size} rounds={options.Rounds} mixed_seconds={options.MixedSeconds}");

    results.Add(
        await WireLoadScenario.RunAsync(
            options.Url,
            size,
            options.Rounds,
            options.SetupConcurrency,
            options.MixedSeconds,
            TimeSpan.FromSeconds(
                options.TimeoutSeconds)));
}

var report = new
{
    GeneratedUtc = DateTimeOffset.UtcNow,
    Configuration = new
    {
        options.Url,
        options.Sizes,
        options.Rounds,
        options.SetupConcurrency,
        options.MixedSeconds,
        options.TimeoutSeconds
    },
    Results = results,
    Notes = new[]
    {
        "Uses the same ClientProtocol AES/WebSocket transport as the recovered Godot client.",
        "Movement expects one MoveEntity on every other same-map client for each accepted SyncPosition.",
        "Nearby chat expects one ChatBroadcast on every same-map client, including the sender response.",
        "Round latency is end-to-end fanout completion time, not an in-process router benchmark.",
        "MixedSoak keeps movement/chat/AttackEntity traffic active while SharedMapAiTicker processes seeded monsters.",
        "MixedSoak samples /health for server working set, GC heap, CPU total time and SharedMapAi tick latency.",
        "CPU percent is normalized to the machine processor count; it is an observation, not a hard pass/fail threshold.",
        "Use --mixed-seconds 600 or longer locally for soak; CI intentionally uses a short mixed smoke.",
        "This load probe is destructive only to its uniquely named throwaway test accounts."
    }
};

var json = JsonSerializer.Serialize(
    report,
    new JsonSerializerOptions
    {
        WriteIndented = true
    });

Console.WriteLine(json);

if (!string.IsNullOrWhiteSpace(options.Output))
{
    var output = Path.GetFullPath(options.Output!);
    Directory.CreateDirectory(
        Path.GetDirectoryName(output)!);
    await File.WriteAllTextAsync(
        output,
        json + Environment.NewLine);
}

if (results.Any(x => !x.Success))
    Environment.ExitCode = 1;

internal sealed record Options(
    string Url,
    IReadOnlyList<int> Sizes,
    int Rounds,
    int SetupConcurrency,
    int MixedSeconds,
    int TimeoutSeconds,
    string? Output)
{
    public static Options Parse(string[] args)
    {
        var url = "ws://127.0.0.1:5678/";
        var sizes = new List<int>
        {
            10,
            50,
            100
        };
        var rounds = 3;
        var setupConcurrency = 10;
        var mixedSeconds = 5;
        var timeoutSeconds = 20;
        string? output = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--url" when i + 1 < args.Length:
                    url = args[++i];
                    break;

                case "--sizes" when i + 1 < args.Length:
                    sizes = args[++i]
                        .Split(
                            ',',
                            StringSplitOptions.RemoveEmptyEntries |
                            StringSplitOptions.TrimEntries)
                        .Select(int.Parse)
                        .Where(x => x > 1)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();
                    break;

                case "--rounds" when i + 1 < args.Length:
                    rounds = Math.Max(
                        1,
                        int.Parse(args[++i]));
                    break;

                case "--setup-concurrency" when i + 1 < args.Length:
                    setupConcurrency = Math.Max(
                        1,
                        int.Parse(args[++i]));
                    break;

                case "--mixed-seconds" when i + 1 < args.Length:
                    mixedSeconds = Math.Max(
                        0,
                        int.Parse(args[++i]));
                    break;

                case "--timeout-seconds" when i + 1 < args.Length:
                    timeoutSeconds = Math.Max(
                        5,
                        int.Parse(args[++i]));
                    break;

                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
            }
        }

        if (sizes.Count == 0)
        {
            throw new ArgumentException(
                "--sizes must contain at least one value greater than 1");
        }

        return new Options(
            url,
            sizes,
            rounds,
            setupConcurrency,
            mixedSeconds,
            timeoutSeconds,
            output);
    }
}
