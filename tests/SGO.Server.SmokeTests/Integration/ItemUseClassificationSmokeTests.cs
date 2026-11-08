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
        CurrentClientDataCatalog current)
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
    }
}
