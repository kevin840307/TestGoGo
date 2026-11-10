using SGO.Server.Application.Assets;
using SGO.Server.Application.Combat;
using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.SmokeTests.World;

// Pure numeric reach, not a weapon type/name classification.
internal static class NormalAttackRangeSmokeTests
{
    public static void Run()
    {
        Check(CombatService.NormalAttackWorldRange(80, 0) == 40.0,
            "unarmed base 80 uses 40 world units");
        Check(CombatService.NormalAttackWorldRange(80 + 120, 120) == 40.0,
            "ordinary 120-range sword and unarmed have identical reach");
        Check(CombatService.NormalAttackWorldRange(80 + 80, 80) == 40.0,
            "80-range weapon does not extend ordinary reach");
        Check(CombatService.NormalAttackWorldRange(80 + 200, 200) == 80.0,
            "200-range sword gains extra reach according to numeric stat");
        Check(CombatService.NormalAttackWorldRange(80 + 400, 400) == 180.0,
            "400-range weapon remains longer according to numeric stat");
        Check(CombatService.NormalAttackWorldRange(80 + 120 + 50, 120) == 65.0,
            "accessory +50 preserves extra range with ordinary sword");
        Check(CombatService.NormalAttackWorldRange(80 + 50, 0) == 65.0,
            "accessory +50 preserves extra range while unarmed");
        Check(CombatService.NormalAttackWorldRange(80 + 200 + 50, 200) == 105.0,
            "special weapon and accessory bonuses stack");
        Check(CombatService.NormalAttackWorldRange(-10, 0) == 40.0,
            "negative numeric range clamps to safe minimum");
        Check(CombatService.NormalAttackWorldRange(80 + 121, 121)
              > CombatService.NormalAttackWorldRange(80 + 120, 120),
            "numeric weapon range above standard 120 increases reach");
    }

    public static void CheckCatalog(CurrentClientDataCatalog catalog)
    {
        var basicSword = catalog.FindItem(30001);
        var longRangeSword = catalog.FindItem(30102);
        var gun = catalog.FindItem(30120);
        Check(basicSword is not null && basicSword.AttackRange == 120
            && longRangeSword is not null && longRangeSword.AttackRange == 200
            && gun is not null && gun.AttackRange == 400,
            "real equipment data has numerical range 120/200/400");

        var assets = new CharacterAssets();
        Check(CharacterStatCalculator.GetEquippedWeaponAttackRange(assets, catalog) == 0,
            "unarmed equip collection reports no weapon range");

        // Weapon slot 5, not any Sword/Gun/Dagger style string.
        var equipped = new Dictionary<string, object>
        {
            ["ItemIdx"] = 30001, ["Part"] = 5,
            ["InBank"] = 0, ["InShop"] = 0
        };
        assets.Equips.Add(equipped);
        Check(CharacterStatCalculator.GetEquippedWeaponAttackRange(assets, catalog)
              == basicSword!.AttackRange,
            "active equipped numeric range comes from weapon slot only");
        Check(
            CombatService.NormalAttackWorldRange(80 + basicSword.AttackRange,
                CharacterStatCalculator.GetEquippedWeaponAttackRange(assets, catalog))
            == CombatService.NormalAttackWorldRange(80, 0),
            "ordinary equipped sword and unarmed actually compute equal reach");

        equipped["ItemIdx"] = 30102;
        Check(CharacterStatCalculator.GetEquippedWeaponAttackRange(assets, catalog)
              == longRangeSword!.AttackRange,
            "special sword retains extra attack range by numerical value");
        equipped["ItemIdx"] = 30120;
        Check(CharacterStatCalculator.GetEquippedWeaponAttackRange(assets, catalog)
              == gun!.AttackRange,
            "long-range numerical weapon retains its range");

        equipped["InBank"] = 1;
        Check(CharacterStatCalculator.GetEquippedWeaponAttackRange(assets, catalog) == 0,
            "weapon in bank does not count as equipped");

        // Numeric +50 on accessory must not be subtracted as a weapon baseline.
        equipped["InBank"] = 0;
        equipped["ItemIdx"] = 30001;
        var withAccessory = CombatService.NormalAttackWorldRange(
            80 + 120 + 50,
            CharacterStatCalculator.GetEquippedWeaponAttackRange(assets, catalog));
        Check(withAccessory == 65.0,
            "other equipment's attack range bonus still extends ordinary weapon reach");
    }

    private static void Check(bool ok, string label)
        => SGO.Server.SmokeTests.SmokeAssert.Check(ok, label);
}
