using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

// External, read-only Windows memory audit. Never run any working-set trim,
// GC or renderer synchronization in the target Godot process.
if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
{
    Console.Error.WriteLine("Windows x64 required");
    return 3;
}

try
{
    if (args.Length == 1 && args[0] == "--self-test")
    {
        var before = MemoryAudit.Snapshot(Environment.ProcessId, "before");
        const int bytes = 64 * 1024 * 1024;
        var block = Native.VirtualAlloc(
            IntPtr.Zero, (nuint)bytes,
            Native.MEM_RESERVE | Native.MEM_COMMIT, Native.PAGE_READWRITE);
        if (block == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            // Touch the pages to exercise resident WS as well as committed VA.
            for (var offset = 0; offset < bytes; offset += 4096)
                Marshal.WriteByte(block, offset, 1);
            var during = MemoryAudit.Snapshot(Environment.ProcessId, "during");
            var committedIncrease = during.PrivateCommittedMB - before.PrivateCommittedMB;
            var privateIncrease = during.PrivateBytesMB - before.PrivateBytesMB;
            if (committedIncrease < 48 || privateIncrease < 48)
                throw new InvalidOperationException(
                    $"Private commit did not capture 64 MiB allocation: VQ={committedIncrease:F2}, PrivateBytes={privateIncrease:F2}");
            Console.WriteLine(
                $"WINDOWS_MEMORY_AUDIT_SELF_TEST_PASS private_committed_delta_mb={committedIncrease:F2} private_bytes_delta_mb={privateIncrease:F2}");
        }
        finally
        {
            if (!Native.VirtualFree(block, 0, Native.MEM_RELEASE))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        return 0;
    }

    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] is not ("--pid" or "--stage" or "--output") ||
            i + 1 >= args.Length ||
            args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
            !options.TryAdd(args[i], args[++i]))
            throw new ArgumentException("Usage: --pid <pid> --stage <Login|Game|AFK|Logout> --output <file.json> | --self-test");
    }
    if (!options.TryGetValue("--pid", out var rawPid) ||
        !int.TryParse(rawPid, out var pid) || pid <= 0 ||
        !options.TryGetValue("--stage", out var stage) ||
        stage is not ("Login" or "Game" or "AFK" or "Logout") ||
        !options.TryGetValue("--output", out var output) ||
        string.IsNullOrWhiteSpace(output))
        throw new ArgumentException("Required: --pid <pid> --stage <Login|Game|AFK|Logout> --output <file.json>");

    var snapshot = MemoryAudit.Snapshot(pid, stage);
    var path = Path.GetFullPath(output);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var json = JsonSerializer.Serialize(snapshot,
        new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(path, json);
    Console.WriteLine(
        $"[WINDOWS_MEMORY_AUDIT] stage={snapshot.Stage} pid={snapshot.ProcessId} " +
        $"working_set_mb={snapshot.WorkingSetMB:F2} private_bytes_mb={snapshot.PrivateBytesMB:F2} " +
        $"private_commit_mb={snapshot.PrivateCommittedMB:F2} mapped_commit_mb={snapshot.MappedCommittedMB:F2} " +
        $"image_commit_mb={snapshot.ImageCommittedMB:F2} output={path}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        "WINDOWS_MEMORY_AUDIT_FAILED: " + ex.GetType().Name + ": " + ex.Message);
    return 1;
}

internal sealed record PrivateAllocation(
    string AllocationBase, double CommittedMB, int Regions);

internal sealed record MemorySnapshot(
    string Stage,
    DateTimeOffset TimestampUtc,
    int ProcessId,
    string ProcessName,
    double WorkingSetMB,
    double PrivateBytesMB,
    double PeakWorkingSetMB,
    double PrivateCommittedMB,
    double MappedCommittedMB,
    double ImageCommittedMB,
    double UnknownCommittedMB,
    int RegionsVisited,
    int CommittedRegions,
    PrivateAllocation[] TopPrivateAllocations,
    string MetricNotes);

internal static class MemoryAudit
{
    private const uint MEM_PRIVATE = 0x20000;
    private const uint MEM_MAPPED = 0x40000;
    private const uint MEM_IMAGE = 0x1000000;
    private const ulong MAX_USER_ADDRESS = 0x00007FFFFFFFFFFFUL;
    private const double MIB = 1024.0 * 1024.0;

    public static MemorySnapshot Snapshot(int pid, string stage)
    {
        using var process = Process.GetProcessById(pid);
        process.Refresh();
        var workingSet = process.WorkingSet64;
        var privateBytes = process.PrivateMemorySize64;
        var peakWorkingSet = process.PeakWorkingSet64;
        var processName = process.ProcessName;

        using var handle = new SafeProcessHandle(
            Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION, false, pid));
        if (handle.IsInvalid)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        ulong privateCommit = 0, mappedCommit = 0, imageCommit = 0, otherCommit = 0;
        var privateAllocations = new Dictionary<ulong, (ulong Bytes, int Regions)>();
        var committedRegions = 0;
        var regionsVisited = 0;
        var mbiSize = (nuint)Marshal.SizeOf<Native.MemoryBasicInformation64>();

        for (ulong address = 0; address < MAX_USER_ADDRESS;)
        {
            var read = Native.VirtualQueryEx(
                handle.DangerousGetHandle(), new IntPtr(checked((long)address)),
                out var region, mbiSize);
            if (read == 0)
            {
                var error = Marshal.GetLastWin32Error();
                // ERROR_INVALID_PARAMETER means the last accessible VA was passed.
                if (error == 87) break;
                throw new System.ComponentModel.Win32Exception(error);
            }
            if (region.RegionSize == 0)
                throw new InvalidDataException("VirtualQueryEx returned an empty region");
            regionsVisited++;

            if (region.State == Native.MEM_COMMIT)
            {
                committedRegions++;
                switch (region.Type)
                {
                    case MEM_PRIVATE:
                        privateCommit += region.RegionSize;
                        var key = region.AllocationBase;
                        var old = privateAllocations.GetValueOrDefault(key);
                        privateAllocations[key] =
                            (old.Bytes + region.RegionSize, old.Regions + 1);
                        break;
                    case MEM_MAPPED:
                        mappedCommit += region.RegionSize;
                        break;
                    case MEM_IMAGE:
                        imageCommit += region.RegionSize;
                        break;
                    default:
                        otherCommit += region.RegionSize;
                        break;
                }
            }

            if (region.BaseAddress > ulong.MaxValue - region.RegionSize)
                break;
            var next = region.BaseAddress + region.RegionSize;
            if (next <= address) break;
            address = next;
        }

        var top = privateAllocations
            .OrderByDescending(x => x.Value.Bytes)
            .Take(16)
            .Select(x => new PrivateAllocation(
                "0x" + x.Key.ToString("X16"),
                Math.Round(x.Value.Bytes / MIB, 2), x.Value.Regions))
            .ToArray();

        return new MemorySnapshot(stage, DateTimeOffset.UtcNow, pid,
            processName,
            Math.Round(workingSet / MIB, 2),
            Math.Round(privateBytes / MIB, 2),
            Math.Round(peakWorkingSet / MIB, 2),
            Math.Round(privateCommit / MIB, 2),
            Math.Round(mappedCommit / MIB, 2),
            Math.Round(imageCommit / MIB, 2),
            Math.Round(otherCommit / MIB, 2),
            regionsVisited, committedRegions, top,
            "Windows process WorkingSet and PrivateBytes are OS counters. " +
            "VirtualQueryEx buckets report committed virtual-address regions by MEM_PRIVATE/MEM_MAPPED/MEM_IMAGE; " +
            "they do not sum to physical RAM, do not identify call stacks, and cannot prove GPU/driver attribution. " +
            "This tool never trims memory or triggers a GC in the target.");
    }
}

internal sealed class SafeProcessHandle : IDisposable
{
    private IntPtr _handle;
    public SafeProcessHandle(IntPtr handle) => _handle = handle;
    public bool IsInvalid => _handle == IntPtr.Zero || _handle == new IntPtr(-1);
    public IntPtr DangerousGetHandle() => _handle;
    public void Dispose()
    {
        if (!IsInvalid) Native.CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }
}

internal static class Native
{
    internal const uint PROCESS_QUERY_INFORMATION = 0x0400;
    internal const uint MEM_COMMIT = 0x1000;
    internal const uint MEM_RESERVE = 0x2000;
    internal const uint MEM_RELEASE = 0x8000;
    internal const uint PAGE_READWRITE = 0x04;

    // Explicit x64 layout of MEMORY_BASIC_INFORMATION (48 bytes). The
    // separate x86 structure is intentionally not supported.
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    internal struct MemoryBasicInformation64
    {
        [FieldOffset(0)] public ulong BaseAddress;
        [FieldOffset(8)] public ulong AllocationBase;
        [FieldOffset(16)] public uint AllocationProtect;
        [FieldOffset(20)] public ushort PartitionId;
        [FieldOffset(24)] public ulong RegionSize;
        [FieldOffset(32)] public uint State;
        [FieldOffset(36)] public uint Protect;
        [FieldOffset(40)] public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(
        uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nuint VirtualQueryEx(
        IntPtr process, IntPtr address,
        out MemoryBasicInformation64 memoryInformation, nuint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr VirtualAlloc(
        IntPtr address, nuint size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool VirtualFree(
        IntPtr address, nuint size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);
}
