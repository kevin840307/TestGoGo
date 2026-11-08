using System.Text.Json;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.SmokeTests.Fixtures;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

/// <summary>
/// Input validation must occur before the inventory-to-ground transaction.
/// This guards against accidental disappearance when clients submit zero,
/// negative or multi-unit equipment drop requests.
/// </summary>
internal static class GroundDropRequestGuardSmokeTests
{
    public static void Run()
    {
        using var temp =
            new TemporaryDirectoryFixture("sgo-ground-drop-guards-");
        var store = new SqliteGameStore(
            Path.Combine(temp.Root, "game.db"));
        var name = $"drop_{Guid.NewGuid():N}";
        const string password = "test-password";
        Check(store.Register(name, password, "").Success,
            "register ground-drop guard player");
        var accountId = store.Login(name, password);
        Check(accountId.HasValue,
            "login ground-drop guard player");

        using var create = JsonSerializer.SerializeToDocument(new
        {
            Name = "DropGuard",
            Password = "1234",
            Race = 0,
            Hair = 1,
            HairColor = 1,
            Str = 5,
            Dex = 5,
            Con = 5,
            Wis = 5
        });
        Check(store.Create(accountId!.Value, create.RootElement).Success,
            "create ground-drop guard character");
        var characterId = store.Characters(accountId.Value)
            .Select(x => Convert.ToInt64(x["DBID"]))
            .First(x => x > 0);

        const int mapId = 13;
        const int stackItem = 999_991;
        const int equipmentItem = 999_992;
        int StackCount() => store.GetCharacterAssets(
                accountId.Value, characterId)
            .Inventory
            .Where(x => Convert.ToInt32(x["ItemIndex"]) == stackItem)
            .Sum(x => Convert.ToInt32(x["Count"]));
        int EquipmentCount() => store.GetCharacterAssets(
                accountId.Value, characterId)
            .Equips
            .Count(x => Convert.ToInt32(x["ItemIdx"]) == equipmentItem);
        int GroundCount() => store.GetGroundItems(mapId)
            .Sum(x => x.Count);

        var stackUid = store.AddInventoryItem(
            accountId.Value, characterId, stackItem, 8);
        var equipUid = store.AddEquipment(
            accountId.Value, characterId, equipmentItem);
        Check(stackUid > 0 && equipUid > 0 &&
              StackCount() == 8 && EquipmentCount() == 1,
            "seed stack and one indivisible equipment item");

        foreach (var invalidCount in new[] { 0, -5, 9 })
        {
            var result = store.DropAssetToGround(
                accountId.Value, characterId, stackUid, 0,
                invalidCount, mapId, 100, 100);
            Check(result is null &&
                  StackCount() == 8 && GroundCount() == 0,
                $"invalid stack drop count {invalidCount} preserves items");
        }

        Check(store.DropAssetToGround(
                  accountId.Value, characterId,
                  stackUid, 0, 1, mapId, double.NaN, 100) is null &&
              StackCount() == 8 && GroundCount() == 0,
            "invalid non-finite drop location cannot remove inventory");

        Check(store.DropAssetToGround(
                  accountId.Value, characterId, equipUid, 1,
                  2, mapId, 100, 100) is null &&
              store.DropAssetToGround(
                  accountId.Value, characterId, equipUid, 1,
                  0, mapId, 100, 100) is null &&
              EquipmentCount() == 1 && GroundCount() == 0,
            "equipment cannot silently convert a multi/zero-unit request into one discarded item");

        var dropped = store.DropAssetToGround(
            accountId.Value, characterId,
            stackUid, 0, 3, mapId, 100, 100);
        Check(dropped is not null &&
              dropped.Count == 3 &&
              StackCount() == 5 && GroundCount() == 3,
            "valid drop commits exactly three units, preserving total eight");

        var pickup = store.PickUpGroundItem(
            accountId.Value, characterId, dropped!.GroundItemId);
        Check(pickup.Success &&
              StackCount() == 8 &&
              GroundCount() == 0,
            "pickup restores exactly three units without minting or losing any");

        Check(!store.PickUpGroundItem(
                  accountId.Value, characterId, dropped.GroundItemId).Success &&
              StackCount() == 8 &&
              GroundCount() == 0,
            "duplicate pickup cannot create a second copy");

        var equipGround = store.DropAssetToGround(
            accountId.Value, characterId,
            equipUid, 1, 1, mapId, 100, 100);
        Check(equipGround is not null &&
              equipGround.Count == 1 &&
              EquipmentCount() == 0 && GroundCount() == 1,
            "valid indivisible equipment drop transfers exactly one asset");
        Check(store.PickUpGroundItem(
                  accountId.Value, characterId,
                  equipGround!.GroundItemId).Success &&
              EquipmentCount() == 1 && GroundCount() == 0,
            "equipment pickup restores the original asset count");

        Console.WriteLine("Ground drop request guard smoke passed.");
    }
}
