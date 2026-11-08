using System.Text.Json;
using SGO.Server.Application;
using SGO.Server.Application.Items;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class ChoiceRewardSmokeTests
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
        var expectedRealChoiceCounts =
            new Dictionary<int, int>
            {
                [2208] = 3,
                [2209] = 3,
                [2210] = 3,
                [2211] = 6,
                [2212] = 3
            };

        foreach (var expected in
                 expectedRealChoiceCounts)
        {
            var resolved =
                ChoiceRewardCatalog.Resolve(
                    realCurrent,
                    expected.Key);

            Check(
                resolved is not null &&
                resolved.Options.Count ==
                    expected.Value &&
                resolved.Options
                    .Select(x => x.ItemIndex)
                    .Distinct()
                    .Count() ==
                    expected.Value,
                $"real explicit choice box {expected.Key} resolves exactly {expected.Value} selectable rewards");
        }

        var realChoice =
            ChoiceRewardCatalog.Resolve(
                realCurrent,
                2209);

        Check(
            realChoice is not null &&
            realChoice.Options
                .Select(x => x.ItemIndex)
                .OrderBy(x => x)
                .SequenceEqual(
                    new[]
                    {
                        15112,
                        15113,
                        15114
                    }),
            "real 2209 description resolves exactly the 50/70/95 temple knight armor choices");

        var syntheticChoice =
            ChoiceRewardCatalog.Resolve(
                current,
                2209);

        Check(
            syntheticChoice is not null &&
            syntheticChoice.Options.Count == 3,
            "test catalog resolves the same three explicit choice rewards");

        var before =
            store.GetCharacterAssets(
                accountId,
                characterId);
        var beforeRewardCount =
            before.Equips.Count(x =>
                Convert.ToInt32(
                    x["ItemIdx"]) ==
                15113);

        var boxUid =
            store.AddInventoryItem(
                accountId,
                characterId,
                2209,
                1);

        Check(
            boxUid > 0,
            "seed explicit choice reward box");

        using (var use = Json(
            JsonSerializer.Serialize(
                new
                {
                    Type =
                        "UseItemRequest",
                    ItemUID =
                        boxUid
                })))
        {
            var packets =
                router.Handle(
                    use.RootElement,
                    session);
            var json =
                JsonSerializer.Serialize(
                    packets);

            Check(
                json.Contains(
                    "\"Type\":\"ItemChoiceRequired\"") &&
                json.Contains(
                    "\"ItemIndex\":15112") &&
                json.Contains(
                    "\"ItemIndex\":15113") &&
                json.Contains(
                    "\"ItemIndex\":15114"),
                "using a choice box returns only its authoritative reward options");
        }

        var afterPrompt =
            store.GetCharacterAssets(
                accountId,
                characterId);

        Check(
            afterPrompt.Inventory.Any(x =>
                Convert.ToInt64(
                    x["ItemUID"]) ==
                boxUid) &&
            afterPrompt.Equips.Count(x =>
                Convert.ToInt32(
                    x["ItemIdx"]) ==
                15113) ==
                beforeRewardCount,
            "opening the choice UI does not consume the source or grant a reward");

        using (var spoof = Json(
            JsonSerializer.Serialize(
                new
                {
                    Type =
                        "UseItemChoiceRequest",
                    ItemUID =
                        boxUid,
                    ItemIndex =
                        999999
                })))
        {
            var packets =
                router.Handle(
                    spoof.RootElement,
                    session);
            Check(
                SGO.Server.SmokeTests.SmokeAssert.PacketTreeContainsString(
                    packets,
                    "獎勵選項已失效"),
                "forged choice reward IDs are rejected");
        }

        var afterSpoof =
            store.GetCharacterAssets(
                accountId,
                characterId);

        Check(
            afterSpoof.Inventory.Any(x =>
                Convert.ToInt64(
                    x["ItemUID"]) ==
                boxUid) &&
            afterSpoof.Equips.Count(x =>
                Convert.ToInt32(
                    x["ItemIdx"]) ==
                15113) ==
                beforeRewardCount,
            "forged choice request cannot consume source or grant inventory");

        using (var choose = Json(
            JsonSerializer.Serialize(
                new
                {
                    Type =
                        "UseItemChoiceRequest",
                    ItemUID =
                        boxUid,
                    ItemIndex =
                        15113
                })))
        {
            var packets =
                router.Handle(
                    choose.RootElement,
                    session);
            var json =
                JsonSerializer.Serialize(
                    packets);

            Check(
                json.Contains(
                    "\"Type\":\"InventorySync\"") &&
                json.Contains(
                    "\"Type\":\"EquipSync\"") &&
                SGO.Server.SmokeTests.SmokeAssert.PacketTreeContainsString(
                    packets,
                    "已選擇獎勵：聖殿騎士鎧甲．復仇"),
                "valid choice consumes source and synchronizes the granted equipment");
        }

        var afterChoose =
            store.GetCharacterAssets(
                accountId,
                characterId);

        Check(
            !afterChoose.Inventory.Any(x =>
                Convert.ToInt64(
                    x["ItemUID"]) ==
                boxUid) &&
            afterChoose.Equips.Count(x =>
                Convert.ToInt32(
                    x["ItemIdx"]) ==
                15113) ==
                beforeRewardCount + 1,
            "valid choice atomically consumes one source and grants exactly one selected reward");

        using (var replay = Json(
            JsonSerializer.Serialize(
                new
                {
                    Type =
                        "UseItemChoiceRequest",
                    ItemUID =
                        boxUid,
                    ItemIndex =
                        15113
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

            Check(
                packets.Any(x =>
                    SGO.Server.SmokeTests.SmokeAssert.PacketTreeContainsString(
                        x,
                        "找不到可使用的選擇型道具")) &&
                afterReplay.Equips.Count(x =>
                    Convert.ToInt32(
                        x["ItemIdx"]) ==
                    15113) ==
                    beforeRewardCount + 1,
                "replaying a consumed choice UID cannot duplicate the selected reward");
        }
        // Concurrent duplicate submissions are a separate failure mode from
        // replaying an already consumed UID in sequence.
        var racingUid = store.AddInventoryItem(
            accountId, characterId, 2209, 1);
        Check(racingUid > 0, "seed choice box for ten concurrent claims");

        var beforeRacing = store.GetCharacterAssets(accountId, characterId)
            .Equips.Count(x => Convert.ToInt32(x["ItemIdx"]) == 15113);

        var racingAttempts = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() =>
            {
                using var request = Json(JsonSerializer.Serialize(new
                {
                    Type = "UseItemChoiceRequest",
                    ItemUID = racingUid,
                    ItemIndex = 15113
                }));
                return JsonSerializer.Serialize(
                    router.Handle(request.RootElement, session));
            }))
            .ToArray();

        Task.WaitAll(racingAttempts);

        var racingResults = racingAttempts.Select(x => x.Result).ToArray();
        var afterRacing = store.GetCharacterAssets(accountId, characterId);

        Check(
            racingResults.Count(x =>
                JsonTreeContainsString(
                    x,
                    "已選擇獎勵")) == 1 &&
            racingResults.Count(x =>
                JsonTreeContainsString(
                    x,
                    "找不到可使用的選擇型道具") ||
                JsonTreeContainsString(
                    x,
                    "找不到可使用的寶箱")) == 9 &&
            !afterRacing.Inventory.Any(x =>
                Convert.ToInt64(x["ItemUID"]) == racingUid) &&
            afterRacing.Equips.Count(x =>
                Convert.ToInt32(x["ItemIdx"]) == 15113) == beforeRacing + 1,
            "ten concurrent choice requests consume one box and grant one reward only");

    }
}
