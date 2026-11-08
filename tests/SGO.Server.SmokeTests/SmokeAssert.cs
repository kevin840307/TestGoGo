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

    public static bool PacketTreeContainsString(
        object? packet,
        string expected)
        => JsonTreeContainsString(
            JsonSerializer.Serialize(packet),
            expected);

    public static bool JsonTreeContainsString(
        string json,
        string expected)
    {
        using var document =
            JsonDocument.Parse(json);

        static bool Contains(
            JsonElement element,
            string expectedText)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString()?.Contains(
                        expectedText,
                        StringComparison.Ordinal) == true;

                case JsonValueKind.Object:
                    foreach (var property in
                             element.EnumerateObject())
                    {
                        if (Contains(
                                property.Value,
                                expectedText))
                        {
                            return true;
                        }
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (var item in
                             element.EnumerateArray())
                    {
                        if (Contains(
                                item,
                                expectedText))
                        {
                            return true;
                        }
                    }

                    break;
            }

            return false;
        }

        return Contains(
            document.RootElement,
            expected);
    }

    public static JsonDocument Json(string value) => JsonDocument.Parse(value);
}
