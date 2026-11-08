using System.Diagnostics;

namespace SGO.Server.Application.Diagnostics;

public sealed class ServerResourceMonitor : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WarningCooldown = TimeSpan.FromMinutes(1);
    private const long WorkingSetWarningBytes = 1024L * 1024 * 1024;
    private const long GrowthWarningBytes = 256L * 1024 * 1024;
    private const int Gen2WarningDelta = 5;

    private readonly ServerDiagnostics _diagnostics;
    private readonly ILogger<ServerResourceMonitor> _log;
    private DateTimeOffset _windowStartedAt = DateTimeOffset.UtcNow;
    private long _windowWorkingSet;
    private int _windowGen2;
    private DateTimeOffset _lastWarningAt = DateTimeOffset.MinValue;

    public ServerResourceMonitor(
        ServerDiagnostics diagnostics,
        ILogger<ServerResourceMonitor> log)
    {
        _diagnostics = diagnostics;
        _log = log;

        using var process = Process.GetCurrentProcess();
        _windowWorkingSet = process.WorkingSet64;
        _windowGen2 = GC.CollectionCount(2);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                Sample(DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal void Sample(DateTimeOffset now)
    {
        using var process = Process.GetCurrentProcess();
        var workingSet = process.WorkingSet64;
        var gen2 = GC.CollectionCount(2);

        if (now - _windowStartedAt >= TimeSpan.FromMinutes(1))
        {
            _windowStartedAt = now;
            _windowWorkingSet = workingSet;
            _windowGen2 = gen2;
            return;
        }

        var growth = workingSet - _windowWorkingSet;
        var gen2Delta = gen2 - _windowGen2;
        var highAbsolute = workingSet >= WorkingSetWarningBytes;
        var suspiciousGrowth = growth >= GrowthWarningBytes;
        var gcPressure = gen2Delta >= Gen2WarningDelta;

        if (!(highAbsolute || suspiciousGrowth || gcPressure) ||
            now - _lastWarningAt < WarningCooldown)
            return;

        _lastWarningAt = now;
        _diagnostics.ResourceWarning();

        var message =
            $"WorkingSetMb={workingSet / 1048576d:F1} " +
            $"GrowthMb={growth / 1048576d:F1} " +
            $"Gen2Delta={gen2Delta} Threads={process.Threads.Count}";

        _diagnostics.Record(
            "Warning",
            "ResourcePressure",
            message);

        _log.LogWarning(
            "[SERVER_RESOURCE_PRESSURE] {Message}",
            message);
    }
}
