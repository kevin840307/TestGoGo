using System.Text.Json;
using SGO.Server.Application;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class ItemUseClassificationSmokeTests
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
        var blueprint = current.FindItem(3239);
        Check(
            blueprint is not null &&
            blueprint.Description.Contains("合成") &&
            blueprint.Description.Contains("材料"),
            "current client crafting blueprint exposes non-consumable description semantics");

        var uid = store.AddInventoryItem(
            accountId,
            characterId,
            3239,
            1);
        Check(uid > 0, "seed crafting blueprint");

        using var use = Json(
            JsonSerializer.Serialize(new
            {
                Type = "UseItemRequest",
                ItemUID = uid
            }));

        var packets = router.Handle(
            use.RootElement,
            session);
        var remaining = store.GetCharacterAssets(
                accountId,
                characterId)
            .Inventory
            .Where(x =>
                Convert.ToInt64(x["ItemUID"]) == uid)
            .Sum(x => Convert.ToInt32(x["Count"]));

        Check(
            remaining == 1 &&
            packets.Any(x =>
                JsonSerializer.Serialize(x).Contains(
                    "製作材料／藍圖")),
            "double-clicking crafting blueprint gives explicit crafting guidance without consuming it");


        var realCraftingCards =
            new Dictionary<int, int>
            {
                [3128] = 19263,
                [3129] = 19264,
                [3131] = 19266
            };

        foreach (var pair in realCraftingCards)
        {
            var resolved =
                SGO.Server.Application.Items.CraftingArtifactCatalog.Resolve(
                    realCurrent,
                    pair.Key);
            Check(
                resolved is not null &&
                resolved.OutputItemId ==
                    pair.Value,
                $"legacy crafting card {pair.Key} resolves one exact current-client output");
        }

        for (var chestId = 2117;
             chestId <= 2125;
             chestId++)
        {
            var resolved =
                SGO.Server.Application.Items.LegacyKeyedChestCatalog.Resolve(
                    realCurrent,
                    chestId);
            Check(
                resolved is not null &&
                resolved.KeyItemId ==
                    chestId - 9,
                $"legacy keyed chest {chestId} resolves its exact matching key");
        }

        var cardUid =
            store.AddInventoryItem(
                accountId,
                characterId,
                3131,
                1);

        using (var useCard = Json(
            JsonSerializer.Serialize(new
            {
                Type = "UseItemRequest",
                ItemUID = cardUid
            })))
        {
            var cardPackets =
                router.Handle(
                    useCard.RootElement,
                    session);
            var cardRemaining =
                store.GetCharacterAssets(
                        accountId,
                        characterId)
                    .Inventory
                    .Where(x =>
                        Convert.ToInt64(
                            x["ItemUID"]) ==
                        cardUid)
                    .Sum(x =>
                        Convert.ToInt32(
                            x["Count"]));

            Check(
                cardRemaining == 1 &&
                cardPackets.Any(x =>
                    JsonSerializer.Serialize(x)
                        .Contains(
                            "製作材料／藍圖")),
                "legacy crafting card is not consumed when double-clicked");
        }

        var chestUid =
            store.AddInventoryItem(
                accountId,
                characterId,
                2118,
                1);

        using (var useWithoutKey = Json(
            JsonSerializer.Serialize(new
            {
                Type = "UseItemRequest",
                ItemUID = chestUid
            })))
        {
            var chestPackets =
                router.Handle(
                    useWithoutKey.RootElement,
                    session);

            Check(
                chestPackets.Any(x =>
                    JsonSerializer.Serialize(x)
                        .Contains(
                            "需要堅固的鑰匙")) &&
                store.GetCharacterAssets(
                        accountId,
                        characterId)
                    .Inventory.Any(x =>
                        Convert.ToInt64(
                            x["ItemUID"]) ==
                        chestUid),
                "keyed chest refuses opening without its exact key and preserves the chest");
        }

        var keyUid =
            store.AddInventoryItem(
                accountId,
                characterId,
                2109,
                1);

        using (var useWithKey = Json(
            JsonSerializer.Serialize(new
            {
                Type = "UseItemRequest",
                ItemUID = chestUid
            })))
        {
            var chestPackets =
                router.Handle(
                    useWithKey.RootElement,
                    session);
            var after =
                store.GetCharacterAssets(
                    accountId,
                    characterId);

            Check(
                chestPackets.Any(x =>
                    JsonSerializer.Serialize(x)
                        .Contains(
                            "原始寶箱獎勵表尚未恢復")) &&
                after.Inventory.Any(x =>
                    Convert.ToInt64(
                        x["ItemUID"]) ==
                    chestUid) &&
                after.Inventory.Any(x =>
                    Convert.ToInt64(
                        x["ItemUID"]) ==
                    keyUid),
                "known keyed chest with matching key preserves both items until authoritative rewards are recovered");
        }
    }
}
