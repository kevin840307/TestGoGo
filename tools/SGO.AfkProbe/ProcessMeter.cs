using System.Diagnostics;

namespace Sgo.AfkProbe;

internal sealed record MemorySample(
    DateTimeOffset AtUtc,
    double WorkingSetMB,
    double PrivateBytesMB,
    double GcHeapMB,
    int Threads);

internal sealed class ProcessMeter
{
    private readonly List<MemorySample> _samples = new();
    internal IReadOnlyList<MemorySample> Samples => _samples;

    internal void Sample()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        _samples.Add(new MemorySample(
            DateTimeOffset.UtcNow,
            Math.Round(process.WorkingSet64 / 1048576.0, 2),
            Math.Round(process.PrivateMemorySize64 / 1048576.0, 2),
            Math.Round(GC.GetTotalMemory(false) / 1048576.0, 2),
            process.Threads.Count));
    }

    internal object Report() => new
    {
        ProcessId = Environment.ProcessId,
        Samples = _samples,
        PeakWorkingSetMB = _samples.Count == 0 ? 0 : _samples.Max(x => x.WorkingSetMB),
        PeakPrivateBytesMB = _samples.Count == 0 ? 0 : _samples.Max(x => x.PrivateBytesMB),
        FinalWorkingSetMB = _samples.LastOrDefault()?.WorkingSetMB ?? 0,
        FinalPrivateBytesMB = _samples.LastOrDefault()?.PrivateBytesMB ?? 0
    };
}
