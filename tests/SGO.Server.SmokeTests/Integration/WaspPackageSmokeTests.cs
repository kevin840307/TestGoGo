using System.Text.Json;
using SGO.Server.Application;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class WaspPackageSmokeTests
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
        var realCatalog =
            new HorseCardCatalog(
                realCurrent);

        for (var levelIndex = 1;
             levelIndex <= 7;
             levelIndex++)
        {
            var heavySource =
                2399 + levelIndex * 2;
            var magicSource =
                heavySource + 1;

            var heavy =
                realCatalog.Find(
                    heavySource);
            var magic =
                realCatalog.Find(
                    magicSource);

            Check(
                heavy is not null &&
                heavy.HorseIndex == 50011 &&
                heavy.BodyId == 50311 + levelIndex &&
                heavy.WeaponId == 50319 + levelIndex * 2 &&
                heavy.EngineId == 50335 + levelIndex,
                $"heavy wasp package {heavySource} maps exact current-client variant parts");

            Check(
                magic is not null &&
                magic.HorseIndex == 50012 &&
                magic.BodyId == 50311 + levelIndex &&
                magic.WeaponId == 50320 + levelIndex * 2 &&
                magic.EngineId == 50335 + levelIndex,
                $"magic wasp package {magicSource} maps exact current-client variant parts");
        }

        Check(
            realCatalog.Find(2422) is
            {
                HorseIndex: 50011,
                BodyId: 50311,
                WeaponId: 50319,
                EngineId: 50335
            } &&
            realCatalog.Find(2423) is
            {
                HorseIndex: 50012,
                BodyId: 50311,
                WeaponId: 50320,
                EngineId: 50335
            },
            "base wasp packages 2422/2423 map exact 10-level presets");

        var sourceUid =
            store.AddInventoryItem(
                accountId,
                characterId,
                2410,
                1);

        Check(
            sourceUid > 0,
            "seed magic snow wasp package");

        var before =
            store.GetCharacterAssets(
                accountId,
                characterId);
        var beforeHorseCount =
            before.Horses.Count;

        using (var use = Json(
            JsonSerializer.Serialize(new
            {
                Type = "UseItemRequest",
                ItemUID = sourceUid
            })))
        {
            var packets =
                router.Handle(
                    use.RootElement,
                    session);

            var hasHorseSync =
                packets.Any(packet =>
                {
                    using var document =
                        JsonDocument.Parse(
                            JsonSerializer.Serialize(packet));
                    return document.RootElement.TryGetProperty(
                               "Type",
                               out var type) &&
                           type.GetString() == "HorseSync";
                });

            Check(
                hasHorseSync &&
                PacketTreeContainsString(
                    packets,
                    "魔裝黃蜂戰鬥機"),
                "UseItemRequest activates magic snow wasp package");
        }

        var after =
            store.GetCharacterAssets(
                accountId,
                characterId);

        Check(
            after.Horses.Count ==
                beforeHorseCount + 1 &&
            !after.Inventory.Any(x =>
                Convert.ToInt64(
                    x["ItemUID"]) ==
                sourceUid),
            "wasp package consumes source general item and creates one mecha");

        var createdHorse =
            after.Horses
                .OrderByDescending(x =>
                    Convert.ToInt64(
                        x["HorseUID"]))
                .First();

        var horseUid =
            Convert.ToInt64(
                createdHorse["HorseUID"]);

        Check(
            Convert.ToInt32(
                createdHorse["HorseIdx"]) ==
                50012,
            "2410 creates magic wasp horse 50012");

        var equippedParts =
            after.HorseEquips
                .Where(x =>
                    Convert.ToInt64(
                        x["HorseUID"]) ==
                    horseUid)
                .Select(x =>
                    Convert.ToInt32(
                        x["EquipIdx"]))
                .OrderBy(x => x)
                .ToArray();

        Check(
            equippedParts.SequenceEqual(
                new[]
                {
                    50316,
                    50330,
                    50340
                }),
            "2410 persists exact level-60 body/magic-weapon/engine package");
    }
}
