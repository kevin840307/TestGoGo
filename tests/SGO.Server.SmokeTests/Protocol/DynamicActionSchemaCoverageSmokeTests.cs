using System.Collections;
using System.Reflection;
using System.Text.Json;
using SGO.Server.Protocol;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Protocol;

internal static class DynamicActionSchemaCoverageSmokeTests
{
    private sealed record Descriptor(
        string Name,
        string Type,
        bool Required);

    public static void Run()
    {
        var actionRulesField =
            typeof(PacketSchemaValidator).GetField(
                "ActionRules",
                BindingFlags.NonPublic |
                BindingFlags.Static);

        Check(
            actionRulesField is not null,
            "dynamic action schema coverage can locate ActionRules");

        var rawRules =
            actionRulesField!.GetValue(null)
            as IEnumerable;

        Check(
            rawRules is not null,
            "dynamic action schema coverage can enumerate ActionRules");

        var actionCount = 0;
        var requiredFieldChecks = 0;

        foreach (var entry in rawRules!)
        {
            var entryType =
                entry!.GetType();
            var key =
                entryType.GetProperty("Key")!
                    .GetValue(entry)!
                    .ToString()!;
            var fields =
                (IEnumerable)entryType
                    .GetProperty("Value")!
                    .GetValue(entry)!;

            var split =
                key.Split(
                    ':',
                    2);

            Check(
                split.Length == 2,
                $"action schema key is PacketType:Action ({key})");

            var packetType =
                split[0];
            var action =
                split[1];
            var descriptors =
                fields.Cast<object>()
                    .Select(ReadDescriptor)
                    .ToArray();

            var validPayload =
                new Dictionary<string, object?>
                {
                    ["Type"] = packetType,
                    ["Action"] = action
                };

            foreach (var descriptor in descriptors)
            {
                validPayload[descriptor.Name] =
                    ValidValue(descriptor.Type);
            }

            using (var valid =
                   JsonDocument.Parse(
                       JsonSerializer.Serialize(
                           validPayload)))
            {
                Check(
                    PacketSchemaValidator.TryValidate(
                        valid.RootElement,
                        packetType,
                        out var validError),
                    $"dynamic action schema accepts valid {key}: {validError}");
            }

            foreach (var descriptor in descriptors.Where(
                         x => x.Required))
            {
                var invalidPayload =
                    new Dictionary<string, object?>(
                        validPayload)
                    {
                        [descriptor.Name] =
                            InvalidValue(
                                descriptor.Type)
                    };

                using var invalid =
                    JsonDocument.Parse(
                        JsonSerializer.Serialize(
                            invalidPayload));

                Check(
                    !PacketSchemaValidator.TryValidate(
                        invalid.RootElement,
                        packetType,
                        out var invalidError) &&
                    invalidError.Contains(
                        descriptor.Name,
                        StringComparison.Ordinal),
                    $"dynamic action schema rejects wrong type for {key}.{descriptor.Name}");

                requiredFieldChecks++;
            }

            actionCount++;
        }

        Check(
            actionCount >= 25,
            "runtime dynamic action coverage includes the complete current action-rule set");
        Check(
            requiredFieldChecks >= actionCount,
            "every dynamic action rule has at least one runtime required-field rejection");
    }

    private static Descriptor ReadDescriptor(
        object raw)
    {
        var type =
            raw.GetType();

        return new Descriptor(
            type.GetProperty("Name")!
                .GetValue(raw)!
                .ToString()!,
            type.GetProperty("Type")!
                .GetValue(raw)!
                .ToString()!,
            Convert.ToBoolean(
                type.GetProperty("Required")!
                    .GetValue(raw)));
    }

    private static object ValidValue(
        string type)
        => type switch
        {
            "Integer" => 7,
            "LongInteger" => 7L,
            "Text" => "text",
            "NonEmptyText" => "value",
            "Boolean" => true,
            "LongIntegerArray" =>
                new long[] { 1, 2 },
            _ => throw new InvalidOperationException(
                "Unknown schema field type: " +
                type)
        };

    private static object InvalidValue(
        string type)
        => type switch
        {
            "Integer" or
            "LongInteger" =>
                "not-a-number",
            "Text" or
            "NonEmptyText" =>
                123,
            "Boolean" =>
                "true",
            "LongIntegerArray" =>
                new object[]
                {
                    1L,
                    "bad"
                },
            _ => throw new InvalidOperationException(
                "Unknown schema field type: " +
                type)
        };
}
