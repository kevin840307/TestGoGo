using System.Text.Json;
using SGO.Server.Application;
using SGO.Server.Application.Items;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class DollRewardSmokeTests
{
    public static void Run(
        PacketRouter router,
        SqliteGameStore store,
        ClientSession session,
        long accountId,
        long characterId,
        CurrentClientDataCatalog current,
        CurrentClientDataCatalog realCurrent)
    {
        Check(
            DollRewardCatalog.SourceItemIds
                .OrderBy(x => x)
                .SequenceEqual(
                    Enumerable.Range(
                        2126,
                        9)),
            "all nine recovered doll source IDs 2126..2134 are catalogued");

        foreach (var sourceId in
                 DollRewardCatalog.SourceItemIds)
        {
            var rewards =
                DollRewardCatalog.GetRewards(
                    sourceId);

            Check(
                rewards.Count > 0 &&
                rewards.All(x =>
                    realCurrent.FindItem(
                        x.ItemId) is not null),
                $"doll {sourceId} reward pool contains only current-client items");
        }

        Check(
            DollRewardCatalog.GetRewards(
                    2130)
                .Select(x => x.ItemId)
                .SequenceEqual(
                    Enumerable.Range(
                        18089,
                        12)),
            "zodiac doll reward pool is exactly 18089..18100");

        Check(
            DollRewardCatalog.GetRewards(
                    2133)
                .Where(x =>
                    x.ItemId is >= 2006 and <= 2027)
                .All(x =>
                    x.MinCount == 30 &&
                    x.MaxCount == 30),
            "valentine doll grants thirty uses for every documented dress transformer");

        var before =
            store.GetCharacterAssets(
                accountId,
                characterId);
        var beforeZodiac =
            before.Equips.Count(x =>
                Convert.ToInt32(
                    x["ItemIdx"]) is >= 18089 and <= 18100);

        var dollUid =
            store.AddInventoryItem(
                accountId,
                characterId,
                2130,
                1);

        Check(
            dollUid > 0 &&
            current.FindItem(
                2130) is not null,
            "seed zodiac doll for real router open");

        using (var use = Json(
            JsonSerializer.Serialize(new
            {
                Type = "UseItemRequest",
                ItemUID = dollUid
            })))
        {
            var packets =
                router.Handle(
                    use.RootElement,
                    session);
            var packetDocuments =
                packets
                    .Select(packet =>
                        JsonDocument.Parse(
                            JsonSerializer.Serialize(packet)))
                    .ToArray();

            try
            {
                var types =
                    packetDocuments
                        .Select(x => x.RootElement)
                        .Where(x =>
                            x.TryGetProperty(
                                "Type",
                                out _))
                        .Select(x =>
                            x.GetProperty("Type").GetString())
                        .Where(x => x is not null)
                        .ToHashSet(StringComparer.Ordinal);

                var message =
                    packetDocuments
                        .Select(x => x.RootElement)
                        .Where(x =>
                            x.TryGetProperty(
                                "Type",
                                out var type) &&
                            type.GetString() ==
                                "SystemMessageFlash")
                        .Select(x =>
                            x.TryGetProperty(
                                "Message",
                                out var value)
                                ? value.GetString() ?? ""
                                : "")
                        .FirstOrDefault(x =>
                            x.StartsWith(
                                "十二生肖帽娃娃送給你：",
                                StringComparison.Ordinal));

                Check(
                    types.Contains("InventorySync") &&
                    types.Contains("EquipSync") &&
                    types.Contains("PlayItemEffect") &&
                    !string.IsNullOrWhiteSpace(message),
                    "zodiac doll opens through the standard UseItemRequest path");
            }
            finally
            {
                foreach (var document in packetDocuments)
                    document.Dispose();
            }
        }

        var after =
            store.GetCharacterAssets(
                accountId,
                characterId);
        var afterZodiac =
            after.Equips
                .Where(x =>
                    Convert.ToInt32(
                        x["ItemIdx"]) is >= 18089 and <= 18100)
                .ToArray();

        Check(
            !after.Inventory.Any(x =>
                Convert.ToInt64(
                    x["ItemUID"]) ==
                dollUid) &&
            afterZodiac.Length ==
                beforeZodiac + 1,
            "zodiac doll atomically consumes one source and grants exactly one zodiac headpiece");

        using (var replay = Json(
            JsonSerializer.Serialize(new
            {
                Type = "UseItemRequest",
                ItemUID = dollUid
            })))
        {
            var packets =
                router.Handle(
                    replay.RootElement,
                    session);
            var afterReplay =
                store.GetCharacterAssets(
                    accountId,
                    characterId);

            var missingItemMessage =
                packets
                    .Select(packet =>
                        JsonDocument.Parse(
                            JsonSerializer.Serialize(packet)))
                    .ToArray();

            try
            {
                var hasMissingItemMessage =
                    missingItemMessage
                        .Select(x => x.RootElement)
                        .Any(x =>
                            x.TryGetProperty(
                                "Message",
                                out var value) &&
                            value.ValueKind ==
                                JsonValueKind.String &&
                            value.GetString() ==
                                "找不到物品");

                Check(
                    hasMissingItemMessage &&
                    afterReplay.Equips.Count(x =>
                        Convert.ToInt32(
                            x["ItemIdx"]) is >= 18089 and <= 18100) ==
                        afterZodiac.Length,
                    "replaying a consumed doll UID cannot grant a second random reward");
            }
            finally
            {
                foreach (var document in missingItemMessage)
                    document.Dispose();
            }
        }
    }
}
