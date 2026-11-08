using System.Text.Json;

namespace SGO.Server.SmokeTests;

/// <summary>Shared minimal assertion helpers; no external test framework dependency.</summary>
internal static class SmokeAssert
{
    private static int _testNumber;

    public static void Check(bool ok, string message)
    {
        var testNumber =
            Interlocked.Increment(ref _testNumber);
        var testId = $"T{testNumber:D5}";

        if (!ok)
        {
            throw new InvalidOperationException(
                $"[{testId}] FAILED: {message}");
        }

        Console.WriteLine(
            $"[{testId}] PASS: {message}");
    }

    public static JsonDocument Json(string value) => JsonDocument.Parse(value);
}
