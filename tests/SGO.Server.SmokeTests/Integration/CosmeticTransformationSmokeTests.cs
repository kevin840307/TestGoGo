using System.Text.Json;
using SGO.Server.Application;
using SGO.Server.Application.Assets;
using SGO.Server.Application.Items;
using SGO.Server.Domain.Sessions;
using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class CosmeticTransformationSmokeTests
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
        var expectedWafuu =
            new Dictionary<int, int[]>
            {
                [0] = [19272, 19276, 19280, 19284],
                [1] = [19273, 19277, 19281, 19285],
                [2] = [19274, 19278, 19282, 19286],
                [3] = [19275, 19279, 19283, 19287]
            };

        foreach (var pair in expectedWafuu)
        {
            var resolved =
                CosmeticTransformationCatalog.Resolve(
                    realCurrent,
                    2003,
                    pair.Key);

            Check(
                resolved is not null &&
                resolved.Overrides
                    .Select(x => x.ItemIndex)
                    .OrderBy(x => x)
                    .SequenceEqual(
                        pair.Value.OrderBy(x => x)) &&
                resolved.Overrides
                    .Select(x => x.Part)
                    .OrderBy(x => x)
                    .SequenceEqual(
                        new[] { 1, 2, 3, 4 }),
                $"Wafuu transformation resolves exact four-piece appearance for race {pair.Key}");
        }

        for (var itemId = 2004;
             itemId <= 2036;
             itemId++)
        {
            var requiredRace =
                CosmeticTransformationCatalog.RequiredRace(
                    itemId);
            var race =
                requiredRace ?? 0;
            var resolved =
                CosmeticTransformationCatalog.Resolve(
                    realCurrent,
                    itemId,
                    race);

            Check(
                resolved is not null &&
                resolved.Overrides.Count > 0 &&
                resolved.Overrides
                    .Select(x => x.Part)
                    .Distinct()
                    .Count() ==
                resolved.Overrides.Count,
                $"cosmetic transformation {itemId} resolves to unique current-client appearance slots");
        }

        Check(
            !CosmeticTransformationCatalog.Supports(2001) &&
            !CosmeticTransformationCatalog.Supports(2002) &&
            CosmeticTransformationCatalog.Supports(2003),
            "flower-ball remains unresolved while Wafuu is mapped and lantern uses its dedicated resolver");

        CharacterAssets AssetsWithWeapon(
            int itemId)
        {
            var assets =
                new CharacterAssets();
            assets.Equips.Add(
                new Dictionary<string, object>
                {
                    ["ItemID"] = 1L,
                    ["ItemIdx"] = itemId,
                    ["Damage"] = 0,
                    ["Color"] = 0,
                    ["Part"] = 5,
                    ["InBank"] = 0,
                    ["InShop"] = 0
                });
            return assets;
        }

        var lanternMelee =
            LanternTransformationCatalog.Resolve(
                realCurrent,
                AssetsWithWeapon(30371));
        var lanternTwin =
            LanternTransformationCatalog.Resolve(
                realCurrent,
                AssetsWithWeapon(31214));
        var lanternGun =
            LanternTransformationCatalog.Resolve(
                realCurrent,
                AssetsWithWeapon(31139));

        Check(
            lanternMelee?.Overrides.Single().ItemIndex ==
                LanternTransformationCatalog.MeleeLanternItemId &&
            lanternTwin?.Overrides.Single().ItemIndex ==
                LanternTransformationCatalog.TwinGunLanternItemId &&
            lanternGun?.Overrides.Single().ItemIndex ==
                LanternTransformationCatalog.GunLanternItemId,
            "lantern transformer maps melee twin-gun and single-gun weapon styles to exact current-client visuals");

        Check(
            LanternTransformationCatalog.Resolve(
                realCurrent,
                new CharacterAssets()) is null,
            "lantern transformer refuses use when no weapon is equipped");

        var beforeCharacter =
            store.GetCharacter(
                accountId,
                characterId)!;
        var beforeAssets =
            store.GetCharacterAssets(
                accountId,
                characterId);
        var beforeStats =
            CharacterStatCalculator.Calculate(
                beforeCharacter,
                beforeAssets,
                current);
        var beforeEquipIds =
            beforeAssets.Equips
                .Select(x =>
                    Convert.ToInt64(
                        x["ItemID"]))
                .OrderBy(x => x)
                .ToArray();

        var romanticUid =
            store.AddInventoryItem(
                accountId,
                characterId,
                2034,
                1);

        using (var use = Json(
            JsonSerializer.Serialize(new
            {
                Type = "UseItemRequest",
                ItemUID = romanticUid
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
                json.Contains("\"Type\":\"UpdateAppearance\"") &&
                json.Contains("\"Bonnet\":19334") &&
                json.Contains("\"Upper_armor\":19335") &&
                json.Contains("\"Lower_armor\":19336") &&
                json.Contains("\"Left_hand\":19337") &&
                json.Contains("\"Right_hand\":19338"),
                "2034 one-use romantic formal device broadcasts all five exact appearance slots");
        }

        var afterAssets =
            store.GetCharacterAssets(
                accountId,
                characterId);
        var afterEquipIds =
            afterAssets.Equips
                .Select(x =>
                    Convert.ToInt64(
                        x["ItemID"]))
                .OrderBy(x => x)
                .ToArray();
        var overrides =
            store.GetCosmeticOverrides(
                accountId,
                characterId);

        Check(
            !afterAssets.Inventory.Any(x =>
                Convert.ToInt64(
                    x["ItemUID"]) ==
                romanticUid) &&
            overrides.Count == 5 &&
            afterEquipIds.SequenceEqual(
                beforeEquipIds),
            "one-use cosmetic consumes source while leaving real equipment unchanged");

        var afterCharacter =
            store.GetCharacter(
                accountId,
                characterId)!;
        var afterStats =
            CharacterStatCalculator.Calculate(
                afterCharacter,
                afterAssets,
                current);

        Check(
            afterStats == beforeStats,
            "cosmetic transformation does not change effective combat stats");

        using (var reenter = Json(
            JsonSerializer.Serialize(new
            {
                Type = "SelectCharacterRequest",
                DBID = characterId
            })))
        {
            var packets =
                router.Handle(
                    reenter.RootElement,
                    session);
            var enter =
                JsonSerializer.Serialize(
                    packets.First());

            Check(
                enter.Contains("\"Bonnet\":19334") &&
                enter.Contains("\"Upper_armor\":19335") &&
                enter.Contains("\"Right_hand\":19338"),
                "cosmetic overrides survive character re-entry");
        }

        var pirateUid =
            store.AddInventoryItem(
                accountId,
                characterId,
                2031,
                1);

        using (var permanent = Json(
            JsonSerializer.Serialize(new
            {
                Type = "UseItemRequest",
                ItemUID = pirateUid
            })))
        {
            router.Handle(
                permanent.RootElement,
                session);
        }

        Check(
            store.GetCharacterAssets(
                    accountId,
                    characterId)
                .Inventory.Any(x =>
                    Convert.ToInt64(
                        x["ItemUID"]) ==
                    pirateUid),
            "permanent pirate appearance device is not consumed");

        var wrongRaceUid =
            store.AddInventoryItem(
                accountId,
                characterId,
                2006,
                1);

        using (var wrongRace = Json(
            JsonSerializer.Serialize(new
            {
                Type = "UseItemRequest",
                ItemUID = wrongRaceUid
            })))
        {
            var packets =
                router.Handle(
                    wrongRace.RootElement,
                    session);

            Check(
                packets.Any(x =>
                    PacketTreeContainsString(
                        x,
                        "不適用於目前角色種族")) &&
                store.GetCharacterAssets(
                        accountId,
                        characterId)
                    .Inventory.Any(x =>
                        Convert.ToInt64(
                            x["ItemUID"]) ==
                        wrongRaceUid),
                "race-incompatible cosmetic is rejected without consuming source");
        }
    }
}
