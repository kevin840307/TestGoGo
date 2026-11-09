using System.Diagnostics;
using System.Runtime.InteropServices;

const uint PROCESS_SET_QUOTA = 0x0100;
const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Windows only.");
    return 3;
}

var selfTest = args.Length == 1 && args[0].Equals("--self-test", StringComparison.OrdinalIgnoreCase);
if ((!selfTest && args.Length != 2) ||
    (!selfTest && !int.TryParse(args[0], out _)))
{
    Console.Error.WriteLine("Usage: SGO.WorkingSetTrimmer <pid> <result-file> | --self-test");
    return 2;
}

var targetPid = selfTest ? Environment.ProcessId : int.Parse(args[0]);
var resultPath = selfTest
    ? Path.Combine(Path.GetTempPath(), $"sgo-working-set-trim-{Environment.ProcessId}.txt")
    : Path.GetFullPath(args[1]);

long before = 0;
long after = 0;
var ok = false;
var error = string.Empty;
IntPtr handle = IntPtr.Zero;

try
{
    using var target = Process.GetProcessById(targetPid);
    before = target.WorkingSet64;
    handle = Native.OpenProcess(
        PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION,
        false,
        targetPid);
    if (handle == IntPtr.Zero)
        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

    ok = Native.SetProcessWorkingSetSize(handle, new IntPtr(-1), new IntPtr(-1));
    if (!ok)
        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

    Thread.Sleep(120);
    target.Refresh();
    after = target.WorkingSet64;
}
catch (Exception ex)
{
    error = ex.Message.Replace('|', '/').ReplaceLineEndings(" ");
}
finally
{
    if (handle != IntPtr.Zero)
        Native.CloseHandle(handle);
}

Directory.CreateDirectory(Path.GetDirectoryName(resultPath) ?? ".");
File.WriteAllText(resultPath, $"{ok}|{before}|{after}|{error}");
Console.WriteLine($"success={ok} before_mb={before / 1048576.0:F2} after_mb={after / 1048576.0:F2}");
if (selfTest)
{
    try { File.Delete(resultPath); } catch { }
}
return ok ? 0 : 4;

internal static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);
}
