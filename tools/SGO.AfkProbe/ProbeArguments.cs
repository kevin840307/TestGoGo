namespace Sgo.AfkProbe;

internal sealed record ProbeArguments(
    Uri? Endpoint,
    string? Login,
    string? Password,
    long? CharacterId,
    bool DisposableTestAccount,
    bool Synthetic,
    bool EnableAutoSaint,
    int MpThreshold,
    int SpThreshold,
    int Seconds,
    string? Output)
{
    internal static ProbeArguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--synthetic" or "--smoke-account" or
                "--allow-session-replacement" or "--enable-auto-saint")
            {
                if (!flags.Add(args[i]))
                    throw new ArgumentException("Duplicate flag");
                continue;
            }

            if (args[i] is not (
                "--url" or "--user" or "--password-env" or
                "--character-id" or "--seconds" or "--output" or
                "--mp-threshold" or "--sp-threshold"))
                throw new ArgumentException("Unknown option: " + args[i]);
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
                throw new ArgumentException("Missing value for " + args[i]);
            if (!values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Duplicate option");
        }

        int seconds = values.TryGetValue("--seconds", out var s)
            ? int.Parse(s) : 15;
        if (seconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(seconds), "1..3600");
        bool synthetic = flags.Contains("--synthetic");
        bool smoke = flags.Contains("--smoke-account");
        bool autoSaint = flags.Contains("--enable-auto-saint");

        if (synthetic && (smoke || autoSaint || values.ContainsKey("--user")))
            throw new ArgumentException("Synthetic mode cannot connect or log in");
        if (smoke && autoSaint)
            throw new ArgumentException("Disposable character has no learned AutoSaint skills");

        Uri? endpoint = null;
        string? login = null;
        string? password = null;
        long? characterId = null;
        if (!synthetic)
        {
            if (!values.TryGetValue("--url", out var url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out endpoint) ||
                endpoint.Scheme is not ("ws" or "wss"))
                throw new ArgumentException("--url must be ws:// or wss://");
            if (smoke)
            {
                if (!endpoint.IsLoopback)
                    throw new ArgumentException("--smoke-account requires localhost");
                login = "afk-probe-" + Guid.NewGuid().ToString("N")[..12];
                password = Guid.NewGuid().ToString("N") + "-Test";
            }
            else
            {
                if (!flags.Contains("--allow-session-replacement"))
                    throw new ArgumentException(
                        "Login supersedes any existing Godot session. " +
                        "Use --allow-session-replacement only with a dedicated test account.");
                if (!values.TryGetValue("--user", out login) ||
                    !values.TryGetValue("--password-env", out var key) ||
                    string.IsNullOrWhiteSpace(key))
                    throw new ArgumentException(
                        "Supply --user and --password-env (not a password argument)");
                password = Environment.GetEnvironmentVariable(key);
                if (string.IsNullOrEmpty(password))
                    throw new ArgumentException("Missing password environment variable");
                if (!values.TryGetValue("--character-id", out var id) ||
                    !long.TryParse(id, out var parsed) || parsed <= 0)
                    throw new ArgumentException("Invalid --character-id");
                characterId = parsed;
            }
        }

        int mp = values.TryGetValue("--mp-threshold", out var mpArg)
            ? int.Parse(mpArg) : 30;
        int sp = values.TryGetValue("--sp-threshold", out var spArg)
            ? int.Parse(spArg) : 30;
        if (mp is < 0 or > 100 || sp is < 0 or > 100)
            throw new ArgumentException("Thresholds must be 0..100");
        return new ProbeArguments(
            endpoint, login, password, characterId, smoke,
            synthetic, autoSaint, mp, sp, seconds,
            values.GetValueOrDefault("--output"));
    }
}
