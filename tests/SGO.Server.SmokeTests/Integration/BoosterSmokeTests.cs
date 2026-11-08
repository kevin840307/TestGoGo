using System.Text.Json;
using SGO.Server.Application;
using SGO.Server.Application.Assets;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class BoosterSmokeTests
{
    public static void Run(
        PacketRouter router,
        SqliteGameStore store,
        ClientSession session,
        long accountId,
        long characterId,
        CurrentClientDataCatalog current)
    {
        var boosters = current.Items
            .Where(x => x.Category.Equals(
                "booster",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ItemId)
            .ToArray();

        Check(
            boosters.Length == 20 &&
            boosters[0].ItemId == 1 &&
            boosters[^1].ItemId == 20,
            "current client exposes all twenty booster consumables");

        var booster = current.FindItem(14);
        Check(
            booster is
            {
                Category: "booster",
                MaxHpBonus: 1000,
                Atk: 100,
                Luk: 20
            },
            "booster 14 uses authoritative current-client HP/ATK/LUK values");

        var beforeCharacter = store.GetCharacter(
            accountId,
            characterId)!;
        var beforeAssets = store.GetCharacterAssets(
            accountId,
            characterId);
        var before = CharacterStatCalculator.Calculate(
            beforeCharacter,
            beforeAssets,
            current);

        var boosterUid = store.AddInventoryItem(
            accountId,
            characterId,
            14,
            2);
        Check(
            boosterUid > 0,
            "seed stack of booster 14");

        using (var use = Json(
            JsonSerializer.Serialize(new
            {
                Type = "UseItemRequest",
                ItemUID = boosterUid
            })))
        {
            var responses = router.Handle(
                use.RootElement,
                session);
            var json = JsonSerializer.Serialize(responses);

            Check(
                json.Contains("\"Type\":\"InventorySync\"") &&
                json.Contains("\"Type\":\"PlayerStatusSync\"") &&
                json.Contains("\"Type\":\"SyncBuffs\"") &&
                json.Contains("\"BuffName\":\"ItemEffect_14\"") &&
                json.Contains("\"SourceType\":1") &&
                json.Contains("\"Value\":14"),
                "UseItemRequest activates booster and returns inventory/status/single HUD buff sync");
        }

        var remaining = store.GetCharacterAssets(
                accountId,
                characterId)
            .Inventory
            .Where(x =>
                Convert.ToInt64(x["ItemUID"]) ==
                boosterUid)
            .Sum(x => Convert.ToInt32(x["Count"]));

        var active = store.GetActiveTimedEffects(
            accountId,
            characterId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        Check(
            remaining == 1 &&
            active.Count(x =>
                x.SourceType == 1 &&
                x.SourceValue == 14) == 3 &&
            active.Any(x =>
                x.EffectKey == "stat_max_hp" &&
                x.EffectValue == 1000) &&
            active.Any(x =>
                x.EffectKey == "stat_attack" &&
                x.EffectValue == 100) &&
            active.Any(x =>
                x.EffectKey == "stat_lucky" &&
                x.EffectValue == 20),
            "one booster is consumed once while all three timed stat rows persist");

        var boostedCharacter = store.GetCharacter(
            accountId,
            characterId)!;
        var boosted = CharacterStatCalculator.Calculate(
            boostedCharacter,
            store.GetCharacterAssets(
                accountId,
                characterId),
            current);

        Check(
            boosted.MaxHP == before.MaxHP + 1000 &&
            boosted.Attack == before.Attack + 100 &&
            boosted.Lucky == before.Lucky + 20,
            "persisted booster rows participate in effective stat calculation");

        using (var reenter = Json(
            JsonSerializer.Serialize(new
            {
                Type = "SelectCharacterRequest",
                DBID = characterId
            })))
        {
            var responses = router.Handle(
                reenter.RootElement,
                session);
            var json = JsonSerializer.Serialize(responses);

            Check(
                json.Contains("\"Type\":\"SyncBuffs\"") &&
                json.Contains("\"BuffName\":\"ItemEffect_14\"") &&
                json.Contains("\"Value\":14"),
                "character bootstrap restores booster HUD state after re-entry");
        }

        using (var duplicate = Json(
            JsonSerializer.Serialize(new
            {
                Type = "UseItemRequest",
                ItemUID = boosterUid
            })))
        {
            var responses = router.Handle(
                duplicate.RootElement,
                session);

            var remainingAfterReject =
                store.GetCharacterAssets(
                        accountId,
                        characterId)
                    .Inventory
                    .Where(x =>
                        Convert.ToInt64(x["ItemUID"]) ==
                        boosterUid)
                    .Sum(x =>
                        Convert.ToInt32(x["Count"]));

            var hasDuplicateEffectMessage =
                responses.Any(packet =>
                {
                    using var doc =
                        JsonDocument.Parse(
                            JsonSerializer.Serialize(packet));
                    return doc.RootElement.TryGetProperty(
                               "Message",
                               out var message) &&
                           message.ValueKind ==
                               JsonValueKind.String &&
                           message.GetString() ==
                               "相同類型的能力增益仍在持續中";
                });

            Check(
                remainingAfterReject == 1 &&
                hasDuplicateEffectMessage,
                "overlapping booster effect is rejected without consuming the second item");
        }

        // Drive cleanup through the same persistence API with a future clock.
        store.GetActiveTimedEffects(
            accountId,
            characterId,
            DateTimeOffset.UtcNow
                .AddHours(1)
                .ToUnixTimeMilliseconds());

        var afterCharacter = store.GetCharacter(
            accountId,
            characterId)!;
        var after = CharacterStatCalculator.Calculate(
            afterCharacter,
            store.GetCharacterAssets(
                accountId,
                characterId),
            current);

        Check(
            after.MaxHP == before.MaxHP &&
            after.Attack == before.Attack &&
            after.Lucky == before.Lucky,
            "expired booster rows are removed and effective stats return to baseline");
    }
}
