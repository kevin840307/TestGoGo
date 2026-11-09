using System.Text.Json;
using SGO.Server.Protocol;
using Sgo.AfkProbe;

if (args.Length == 1 && args[0] == "--self-test")
{
    var input = new
    {
        Type = "SpawnEntity",
        EntityType = 1,
        EntityID = 15,
        LocX = 12,
        LocY = 34,
        ExtraData = new { CurrentHP = 90 }
    };
    using var roundTrip = ClientProtocol.Decode(ClientProtocol.Encode(input));
    var state = new AfkState();
    state.Apply(roundTrip.RootElement);
    if (state.Monsters != 1)
        throw new Exception("Spawn did not create data-only monster");
    using var ground = JsonDocument.Parse(
        "{\"Type\":\"SpawnEntity\",\"EntityType\":3,\"EntityID\":701,\"LocX\":22,\"LocY\":11}");
    state.Apply(ground.RootElement);
    if (state.GroundItems != 1)
        throw new Exception("Ground spawn was not retained");

    using var hit = JsonDocument.Parse(
        "{\"Type\":\"EntityDamage\",\"TargetID\":15,\"CurrentHP\":25}");
    state.Apply(hit.RootElement);
    using var change = JsonDocument.Parse(
        "{\"Type\":\"ChangeMap\",\"MapID\":15,\"X\":1,\"Y\":2}");
    state.Apply(change.RootElement);
    if (state.Monsters != 0 || state.MapId != 15 ||
        state.DamageEvents != 1)
        throw new Exception("AFK data-only model regression");
    new ProcessMeter().Sample();
    Console.WriteLine("AFK_PROBE_SELF_TEST_PASS");
    return 0;
}

try
{
    var options = ProbeArguments.Parse(args);
    using var cancelled = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cancelled.Cancel();
    };
    var meter = new ProcessMeter();
    if (options.Synthetic)
    {
        var state = new AfkState();
        var end = DateTimeOffset.UtcNow.AddSeconds(options.Seconds);
        var random = new Random(17);
        while (DateTimeOffset.UtcNow < end &&
               !cancelled.Token.IsCancellationRequested)
        {
            for (var i = 0; i < 300; i++)
            {
                // This only benchmarks data-only state. No Server and no Godot.
                using var packet = JsonDocument.Parse(
                    "{\"Type\":\"SpawnEntity\",\"EntityType\":1,\"EntityID\":" +
                    (1000 + i) + ",\"X\":" + random.Next(0, 500) +
                    ",\"Y\":" + random.Next(0, 500) + "}");
                state.Apply(packet.RootElement);
            }
            meter.Sample();
            await Task.Delay(1000, cancelled.Token);
        }
        meter.Sample();
        await SaveReportAsync(options, meter, new
        {
            Mode = "synthetic_data_only",
            state.Packets,
            state.Monsters,
            state.GroundItems,
            state.RejectedEntities
        });
        return 0;
    }

    await using var session = new WireSession();
    using (var connectTimeout = new CancellationTokenSource(
        TimeSpan.FromSeconds(15)))
    {
        await session.ConnectAsync(options.Endpoint!, connectTimeout.Token);
    }
    await session.EnterWorldAsync(
        options.Login!, options.Password!,
        options.CharacterId, options.DisposableTestAccount,
        cancelled.Token);
    if (options.TestMap.HasValue)
        await session.EnterDisposableTestMapAsync(
            options.TestMap.Value, cancelled.Token);
    if (options.EnableAutoSaint)
    {
        var enabled = await session.EnableAutoSaintAsync(
            options.MpThreshold, options.SpThreshold, cancelled.Token);
        if (!enabled)
            throw new InvalidOperationException(
                "Server refused AutoSaint: required skills may not be learned");
    }
    await session.RunAsync(
        TimeSpan.FromSeconds(options.Seconds), meter, cancelled.Token);
    await SaveReportAsync(options, meter, new
    {
        Mode = "real_encrypted_wire",
        session.State.Packets,
        session.State.Monsters,
        session.State.GroundItems,
        session.State.DamageEvents,
        session.State.DeathEvents,
        session.State.RejectedEntities,
        session.State.MapId,
        session.State.AutoSaintEnabled,
        session.SentBytes,
        session.ReceivedBytes
    });
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("AFK_PROBE_CANCELLED");
    return 130;
}
catch (Exception ex)
{
    // Deliberately avoid logging passwords, usernames, or raw packet payloads.
    Console.Error.WriteLine("AFK_PROBE_FAILED " +
        ex.GetType().Name + ": " + ex.Message);
    return 1;
}

static async Task SaveReportAsync(
    ProbeArguments options, ProcessMeter meter, object state)
{
    var report = new
    {
        GeneratedUtc = DateTimeOffset.UtcNow,
        Runtime = Environment.Version.ToString(),
        OperatingSystem = Environment.OSVersion.Platform.ToString(),
        State = state,
        Memory = meter.Report(),
        Note = "Windows Process Working Set/Private Bytes; NOT Godot Static. " +
            "Synthetic replay does not establish actual gameplay correctness."
    };
    var json = JsonSerializer.Serialize(
        report, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    if (!string.IsNullOrWhiteSpace(options.Output))
    {
        var path = Path.GetFullPath(options.Output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, json);
    }
}
