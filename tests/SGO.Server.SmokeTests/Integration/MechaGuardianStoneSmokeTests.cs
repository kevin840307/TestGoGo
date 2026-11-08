using System.Text.Json;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class MechaGuardianStoneSmokeTests
{
    public static void Run(
        SqliteGameStore store,
        CurrentClientDataCatalog current)
    {
        var guardian =
            current.FindItem(50816) ??
            throw new InvalidOperationException(
                "MA guardian stone 50816 is missing from current client data");

        Check(
            guardian is
            {
                Category: "horseequip",
                HorseType: "MA",
                EquipSlot: 6
            },
            "MA guardian stone 50816 is authoritative mecha slot 6");

        var username =
            $"guardian-slot-{Guid.NewGuid():N}";
        Check(
            store.Register(
                username,
                "test-password",
                "").Success,
            "register isolated guardian-stone account");

        var accountId =
            store.Login(
                username,
                "test-password");
        Check(
            accountId.HasValue,
            "login isolated guardian-stone account");

        using var create = JsonDocument.Parse(
            JsonSerializer.Serialize(new
            {
                Name = "GuardianSlot",
                Password = "1234",
                Race = 0,
                Hair = 1,
                HairColor = 1,
                Str = 5,
                Dex = 5,
                Con = 5,
                Wis = 5
            }));

        Check(
            store.Create(
                accountId!.Value,
                create.RootElement).Success,
            "create isolated guardian-stone character");

        var characterId =
            store.Characters(
                    accountId.Value)
                .Select(x =>
                    Convert.ToInt64(
                        x["DBID"]))
                .First(x => x > 0);

        var horseUid =
            store.AddHorse(
                accountId.Value,
                characterId,
                50012,
                "Guardian MA",
                "MA",
                3600);
        Check(
            horseUid > 0,
            "seed isolated MA mecha for guardian-stone slot test");

        // Fill all three reinforcement-card positions first. The guardian
        // must still use independent slot 6 and must never compete for 3/4/5.
        var cardUids = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            var uid =
                store.AddHorseEquipment(
                    accountId.Value,
                    characterId,
                    70001 + i,
                    -1,
                    3,
                    0);
            Check(
                uid > 0 &&
                store.EquipHorseItem(
                    accountId.Value,
                    characterId,
                    uid,
                    horseUid,
                    3),
                $"seed reinforcement card slot {i + 1}");
            cardUids.Add(uid);
        }

        var cardParts =
            store.GetCharacterAssets(
                    accountId.Value,
                    characterId)
                .HorseEquips
                .Where(x =>
                    cardUids.Contains(
                        Convert.ToInt64(
                            x["EquipUID"])))
                .Select(x =>
                    Convert.ToInt32(
                        x["Part"]))
                .OrderBy(x => x)
                .ToArray();

        Check(
            cardParts.SequenceEqual(
                new[] { 3, 4, 5 }),
            "three reinforcement cards occupy slots 3/4/5 before guardian stone");

        var freshGuardianUid =
            store.AddHorseEquipment(
                accountId.Value,
                characterId,
                50816,
                -1,
                guardian.EquipSlot,
                guardian.Durability);

        var freshGuardian =
            store.GetCharacterAssets(
                    accountId.Value,
                    characterId)
                .HorseEquips
                .Single(x =>
                    Convert.ToInt64(
                        x["EquipUID"]) ==
                    freshGuardianUid);

        Check(
            freshGuardianUid > 0 &&
            Convert.ToInt32(
                freshGuardian["Part"]) == 6,
            "new guardian stone persists slot 6 instead of collapsing to reinforcement slot 3");

        // Simulate a row written by the old <=3 implementation. The router
        // now supplies current GameData EquipSlot=6, allowing EquipHorseItem
        // to repair the row while equipping it.
        var legacyGuardianUid =
            store.AddHorseEquipment(
                accountId.Value,
                characterId,
                50816,
                -1,
                3,
                guardian.Durability);

        Check(
            legacyGuardianUid > 0 &&
            store.EquipHorseItem(
                accountId.Value,
                characterId,
                legacyGuardianUid,
                horseUid,
                guardian.EquipSlot),
            "legacy mis-slotted guardian stone equips successfully with authoritative slot 6");

        var after =
            store.GetCharacterAssets(
                accountId.Value,
                characterId);

        var equippedGuardian =
            after.HorseEquips.Single(x =>
                Convert.ToInt64(
                    x["EquipUID"]) ==
                legacyGuardianUid);

        Check(
            Convert.ToInt64(
                equippedGuardian["HorseUID"]) ==
                horseUid &&
            Convert.ToInt32(
                equippedGuardian["Part"]) == 6,
            "equipping guardian stone repairs legacy part 3 row to slot 6");

        var preservedCardParts =
            after.HorseEquips
                .Where(x =>
                    cardUids.Contains(
                        Convert.ToInt64(
                            x["EquipUID"])))
                .Select(x =>
                    Convert.ToInt32(
                        x["Part"]))
                .OrderBy(x => x)
                .ToArray();

        Check(
            preservedCardParts.SequenceEqual(
                new[] { 3, 4, 5 }),
            "guardian stone slot 6 does not evict or overwrite reinforcement cards");
    }
}
