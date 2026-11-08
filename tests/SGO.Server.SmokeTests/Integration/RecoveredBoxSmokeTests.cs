using System.Text.Json;
using SGO.Server.Application;
using SGO.Server.Application.Items;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class RecoveredBoxSmokeTests
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
        var exact = RecoveredBoxCatalog.Resolve(
            realCurrent,
            2256);

        Check(
            exact is not null &&
            exact.Rewards.Select(x => x.ItemIndex)
                .OrderBy(x => x)
                .SequenceEqual(new[] { 15230, 15243 }),
            "current client description safely resolves 2256 to exact left/right arm rewards");

        Check(
            RecoveredBoxCatalog.Resolve(
                realCurrent,
                2209) is null,
            "free-choice 2209 box is never auto-resolved to a guessed armor");

        Check(
            RecoveredBoxCatalog.Resolve(
                realCurrent,
                2250) is null,
            "multi-version 2250 griffin-wing box is never auto-resolved without an exact reward");

        var ancientSet =
            RecoveredBoxCatalog.Resolve(
                realCurrent,
                2251);
        Check(
            ancientSet is not null &&
            ancientSet.Rewards
                .Select(x => x.ItemIndex)
                .OrderBy(x => x)
                .SequenceEqual(
                    new[]
                    {
                        15189,
                        15202,
                        15215,
                        15228,
                        15241,
                        15254
                    }),
            "2251 explicit full-set box resolves exactly the six ancient red-sun eagle pieces");

        var replicaSet =
            RecoveredBoxCatalog.Resolve(
                realCurrent,
                2252);
        Check(
            replicaSet is not null &&
            replicaSet.Rewards
                .Select(x => x.ItemIndex)
                .OrderBy(x => x)
                .SequenceEqual(
                    new[]
                    {
                        15190,
                        15203,
                        15216,
                        15229,
                        15242,
                        15255
                    }),
            "2252 explicit full-set box resolves exactly the six replica red-sun eagle pieces");

        var before = store.GetCharacterAssets(
            accountId,
            characterId);
        var beforeLeft = before.Equips.Count(x =>
            Convert.ToInt32(x["ItemIdx"]) == 15230);
        var beforeRight = before.Equips.Count(x =>
            Convert.ToInt32(x["ItemIdx"]) == 15243);

        var boxUid = store.AddInventoryItem(
            accountId,
            characterId,
            2256,
            1);

        Check(
            boxUid > 0,
            "seed recovered two-reward equipment box");

        using (var use = Json(
            JsonSerializer.Serialize(new
            {
                Type = "UseItemRequest",
                ItemUID = boxUid
            })))
        {
            var packets = router.Handle(
                use.RootElement,
                session);
            var json = JsonSerializer.Serialize(packets);

            Check(
                json.Contains("\"Type\":\"InventorySync\"") &&
                json.Contains("\"Type\":\"EquipSync\"") &&
                json.Contains("赤日蒼鷹臂甲(左)") &&
                json.Contains("赤日蒼鷹臂甲(右)"),
                "UseItemRequest opens recovered multi-reward box with inventory/equipment sync");
        }

        var after = store.GetCharacterAssets(
            accountId,
            characterId);

        Check(
            !after.Inventory.Any(x =>
                Convert.ToInt64(x["ItemUID"]) == boxUid) &&
            after.Equips.Count(x =>
                Convert.ToInt32(x["ItemIdx"]) == 15230) ==
                beforeLeft + 1 &&
            after.Equips.Count(x =>
                Convert.ToInt32(x["ItemIdx"]) == 15243) ==
                beforeRight + 1,
            "2256 consumes one source box and atomically grants both exact rewards");

        using (var repeat = Json(
            JsonSerializer.Serialize(new
            {
                Type = "UseItemRequest",
                ItemUID = boxUid
            })))
        {
            var packets = router.Handle(
                repeat.RootElement,
                session);
            var afterRepeat = store.GetCharacterAssets(
                accountId,
                characterId);

            Check(
                packets.Any(x =>
                    JsonSerializer.Serialize(x).Contains(
                        "找不到可使用的寶箱")) &&
                afterRepeat.Equips.Count(x =>
                    Convert.ToInt32(x["ItemIdx"]) == 15230) ==
                    beforeLeft + 1 &&
                afterRepeat.Equips.Count(x =>
                    Convert.ToInt32(x["ItemIdx"]) == 15243) ==
                    beforeRight + 1,
                "replaying consumed box UID cannot duplicate bundle rewards");
        }
    }
}
