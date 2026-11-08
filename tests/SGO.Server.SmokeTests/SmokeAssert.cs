using System.Text.Json;

namespace SGO.Server.SmokeTests;

/// <summary>Shared minimal assertion helpers; no external test framework dependency.</summary>
internal static class SmokeAssert
{
    public static void Check(bool ok, string message)
    {
        if (!ok)
            throw new InvalidOperationException("FAILED: " + message);
        Console.WriteLine("PASS: " + message);
    }

    public static JsonDocument Json(string value) => JsonDocument.Parse(value);
}
