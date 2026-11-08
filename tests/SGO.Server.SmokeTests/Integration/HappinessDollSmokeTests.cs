using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SGO.Server.Application;
using SGO.Server.Application.Items;
using SGO.Server.Application.Messaging;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class HappinessDollSmokeTests
{
    public static void Run(
        PacketRouter router,
        SqliteGameStore store,
        ClientSession session,
        long accountId,
        long characterId,
        long dollUid,
        CurrentClientDataCatalog current)
    {
        // 2126 is a RANDOM pool, not a guaranteed 50102 battery. Test the
        // router with every legal result instead of relying on random luck.
        var allowedRewardIds = DollRewardCatalog
            .GetRewards(DollService.HappinessDollItemId)
            .Select(x => x.ItemId)
            .ToHashSet();

        var available = allowedRewardIds
            .Select(current.FindItem)
            .Where(x => x is not null)
            .ToArray();

        Check(
            available.Length >= 2 &&
            available.All(x => x!.Category is
                "general" or "recovery" or "booster" or
                "horse" or "horsebattery"),
            "Happiness Doll random fixture contains multiple inventory-only rewards");

        var before = store.GetCharacterAssets(accountId, characterId);
        var beforeCounts = InventoryCounts(before.Inventory);

        using (var request = Json(JsonSerializer.Serialize(new
        {
            Type = "UseItemRequest",
            ItemUID = dollUid
        })))
        {
            var packets = router.Handle(request.RootElement, session);
            var after = store.GetCharacterAssets(accountId, characterId);
            var afterCounts = InventoryCounts(after.Inventory);
            var source = after.Inventory.FirstOrDefault(x =>
                Convert.ToInt64(x["ItemUID"]) == dollUid);

            // Count all changed inventory items, including previously owned
            // stacks, so a reward other than 50102 is still verified.
            var deltas = beforeCounts.Keys
                .Concat(afterCounts.Keys)
                .Distinct()
                .Where(itemId =>
                    itemId != DollService.HappinessDollItemId)
                .Select(itemId => (
                    ItemId: itemId,
                    Delta: afterCounts.GetValueOrDefault(itemId) -
                           beforeCounts.GetValueOrDefault(itemId)))
                .Where(x => x.Delta != 0)
                .ToArray();

            var rewardedItemId = deltas.Length == 1
                ? deltas[0].ItemId
                : 0;
            var rewardedItemName =
                current.FindItem(rewardedItemId)?.Name;

            Check(
                source is not null &&
                Convert.ToInt32(source["Count"]) == 2 &&
                beforeCounts.GetValueOrDefault(
                    DollService.HappinessDollItemId) -
                afterCounts.GetValueOrDefault(
                    DollService.HappinessDollItemId) == 1 &&
                deltas.Length == 1 &&
                deltas[0].Delta == 1 &&
                allowedRewardIds.Contains(rewardedItemId) &&
                !string.IsNullOrWhiteSpace(rewardedItemName) &&
                after.Equips.Count == before.Equips.Count &&
                after.HorseEquips.Count == before.HorseEquips.Count &&
                packets.Any(x =>
                    JsonSerializer.Serialize(x).Contains(
                        "\"Type\":\"InventorySync\"")) &&
                packets.Any(x =>
                    PacketTreeContainsString(
                        x,
                        "幸福娃娃送給你") &&
                    PacketTreeContainsString(
                        x,
                        rewardedItemName!)),
                "UseItemRequest consumes one Happiness Doll and grants exactly one valid random reward" +
                $" (reward={rewardedItemId}, deltas={string.Join(",", deltas.Select(x => $"{x.ItemId}:{x.Delta}"))})");
        }

        // Do not award the main test character an extra 50102 battery here:
        // later HorseChargeRequest tests use its existing battery UID. A
        // separate character and catalog keep this fixture deterministic and
        // prevent accidental inventory-stack pollution.
        var mainInventoryBeforeRare = InventoryCounts(
            store.GetCharacterAssets(accountId, characterId).Inventory);
        var rareUsername =
            "doll-rare-" + Guid.NewGuid().ToString("N")[..16];
        var rareCharacterName =
            "DollRare" + Guid.NewGuid().ToString("N")[..12];
        var registration = store.Register(
            rareUsername,
            "test-password",
            "");
        Check(
            registration.Success,
            $"register isolated rare-doll account (message={registration.Message})");

        var rareAccountId = store.Login(
            rareUsername,
            "test-password");
        Check(
            rareAccountId.HasValue,
            "login isolated rare-doll account");

        using var create = Json(JsonSerializer.Serialize(new
        {
            Name = rareCharacterName,
            Password = "1234",
            Race = 0,
            Hair = 1,
            HairColor = 1,
            Str = 5,
            Dex = 5,
            Con = 5,
            Wis = 5
        }));
        var creation = store.Create(
            rareAccountId!.Value,
            create.RootElement);
        Check(
            creation.Success,
            $"create isolated rare-doll character (message={creation.Message})");
        var rareCharacterId = store.Characters(rareAccountId.Value)
            .Select(x => Convert.ToInt64(x["DBID"]))
            .Single(x => x > 0);

        // The real 2126 pool is random. The fixture exposes only battery
        // 50102, so the announcement outcome is guaranteed.
        var rareCatalog = new CurrentClientDataCatalog(
            new[]
            {
                current.FindItem(DollService.HappinessDollItemId)!,
                current.FindItem(50102)!
            },
            Array.Empty<CurrentSkillDefinition>());
        var rareConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gameplay:Dolls:Happiness:AnnouncementRewardIds"] = "50102",
                ["Gameplay:Dolls:Happiness:AnnouncementMinimumPrice"] = "0"
            })
            .Build();
        var rareBroadcasts = new ServerBroadcastHub();
        var rareListener = rareBroadcasts.Register();
        var rareDolls = new DollService(
            store,
            store,
            rareCatalog,
            rareBroadcasts,
            rareConfig);

        var rareDollUid = store.AddInventoryItem(
            rareAccountId.Value,
            rareCharacterId,
            DollService.HappinessDollItemId,
            1);
        Check(
            rareDollUid > 0,
            "seed isolated rare Happiness Doll reward fixture");

        var rareBefore = store.GetCharacterAssets(
            rareAccountId.Value,
            rareCharacterId);
        var rareBeforeBatteryCount = InventoryCounts(
            rareBefore.Inventory).GetValueOrDefault(50102);

        var rarePackets = rareDolls.Open(
            rareAccountId.Value,
            rareCharacterId,
            rareDollUid,
            DollService.HappinessDollItemId);
        var rareAfter = store.GetCharacterAssets(
            rareAccountId.Value,
            rareCharacterId);
        var rareAfterBatteryCount = InventoryCounts(
            rareAfter.Inventory).GetValueOrDefault(50102);

        Check(
            !rareAfter.Inventory.Any(x =>
                Convert.ToInt64(x["ItemUID"]) == rareDollUid) &&
            rareAfterBatteryCount == rareBeforeBatteryCount + 1 &&
            rarePackets.Any(x =>
                PacketTreeContainsString(x, "魔靈電池")),
            "deterministic rare Happiness Doll consumes source and grants battery");

        Check(
            rareListener.Queue.ToArray().Any(announcement =>
                JsonSerializer.Serialize(announcement).Contains(
                    "\"Type\":\"SystemMessageBroadcast\"") &&
                PacketTreeContainsString(announcement, rareCharacterName) &&
                PacketTreeContainsString(announcement, "魔靈電池")),
            "rare Happiness Doll reward publishes global announcement");

        var mainInventoryAfterRare = InventoryCounts(
            store.GetCharacterAssets(accountId, characterId).Inventory);
        Check(
            mainInventoryBeforeRare.OrderBy(x => x.Key).SequenceEqual(
                mainInventoryAfterRare.OrderBy(x => x.Key)),
            "rare Happiness Doll fixture does not mutate main test character inventory");

        rareBroadcasts.Unregister(rareListener.ConnectionId);
    }

    private static Dictionary<int, int> InventoryCounts(
        IEnumerable<Dictionary<string, object>> inventory)
        => inventory
            .GroupBy(x => Convert.ToInt32(x["ItemIndex"]))
            .ToDictionary(
                x => x.Key,
                x => x.Sum(item => Convert.ToInt32(item["Count"])));
}
