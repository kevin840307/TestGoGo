using SGO.Server.Application.Assets;
using SGO.Server.Application.Combat;
using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.SmokeTests.World;

// Recovered Client.exe (2009) / Paradise.exe (2004) comparison:
// distanceSquared <= reachSquared - 200, with bare-hand reach 45.
// Current SGO keeps the deliberate unarmed == ordinary weapon-120
// compatibility rule while preserving higher numeric item bonuses.
internal static class NormalAttackRangeSmokeTests
{
    public static void Run()
    {
        const double originalUnarmedReach = 45.0;
        const double squaredMargin = 200.0;
        var closeRange = Math.Sqrt(
            originalUnarmedReach * originalUnarmedReach - squaredMargin);

        Check(Near(closeRange, Math.Sqrt(1825.0)),
            "legacy attack threshold uses 45 squared minus 200");
        Check(Near(CombatService.NormalAttackWorldRange(80, 0), closeRange),
            "unarmed normal attack stays at original 45-unit baseline");
        Check(Near(CombatService.NormalAttackWorldRange(80 + 120, 120), closeRange),
            "ordinary numeric-120 weapon and unarmed have the same reach");
        Check(Near(CombatService.NormalAttackWorldRange(80 + 80, 80), closeRange),
            "a weapon below the standard numeric range does not extend reach");
        Check(Near(
            CombatService.NormalAttackWorldRange(80 + 200, 200),
            Math.Sqrt(125.0 * 125.0 - squaredMargin)),
            "special numeric-200 sword gains only its extra range");
        Check(Near(
            CombatService.NormalAttackWorldRange(80 + 400, 400),
            Math.Sqrt(325.0 * 325.0 - squaredMargin)),
            "numeric-400 long-range weapon preserves extended reach");
        Check(Near(
            CombatService.NormalAttackWorldRange(80 + 120 + 50, 120),
            Math.Sqrt(95.0 * 95.0 - squaredMargin)),
            "accessory +50 stacks with the ordinary weapon");
        Check(Near(
            CombatService.NormalAttackWorldRange(80 + 50, 0),
            Math.Sqrt(95.0 * 95.0 - squaredMargin)),
            "accessory +50 extends bare hands too");
        Check(Near(
            CombatService.NormalAttackWorldRange(80 + 200 + 50, 200),
            Math.Sqrt(175.0 * 175.0 - squaredMargin)),
            "special numeric weapon and accessory bonuses stack");
        Check(Near(CombatService.NormalAttackWorldRange(-10, 0), closeRange),
            "negative stat input cannot shrink below default close reach");
        Check(CombatService.NormalAttackWorldRange(80 + 121, 121)
              > CombatService.NormalAttackWorldRange(80 + 120, 120),
            "additional numeric reach above 120 increases real distance");
    }

    public static void CheckCatalog(CurrentClientDataCatalog catalog)
    {
        var basicSword = catalog.FindItem(30001);
        var longRangeSword = catalog.FindItem(30102);
        var gun = catalog.FindItem(30120);
        Check(basicSword is not null && basicSword.AttackRange == 120
            && longRangeSword is not null && longRangeSword.AttackRange == 200
            && gun is not null && gun.AttackRange == 400,
            "the current game's equipment values include numerical 120/200/400");

        var assets = new CharacterAssets();
        Check(CharacterStatCalculator.GetEquippedWeaponAttackRange(assets, catalog) == 0,
            "unarmed equip collection reports no numeric weapon range");

        var equipped = new Dictionary<string, object>
        {
            ["ItemIdx"] = 30001, ["Part"] = 5,
            ["InBank"] = 0, ["InShop"] = 0
        };
        assets.Equips.Add(equipped);
        Check(CharacterStatCalculator.GetEquippedWeaponAttackRange(assets, catalog)
              == basicSword!.AttackRange,
            "active equipped numeric range comes from weapon slot 5, not weapon style");
        Check(Near(
            CombatService.NormalAttackWorldRange(
                80 + basicSword.AttackRange,
                CharacterStatCalculator.GetEquippedWeaponAttackRange(assets, catalog)),
            CombatService.NormalAttackWorldRange(80, 0)),
            "actual standard sword and unarmed both have legacy-close attack distance");

        equipped["ItemIdx"] = 30102;
        Check(CharacterStatCalculator.GetEquippedWeaponAttackRange(assets, catalog)
              == longRangeSword!.AttackRange,
            "special sword retains extended numeric range without name checks");
        equipped["ItemIdx"] = 30120;
        Check(CharacterStatCalculator.GetEquippedWeaponAttackRange(assets, catalog)
              == gun!.AttackRange,
            "gun's large numeric range is preserved");

        equipped["InBank"] = 1;
        Check(CharacterStatCalculator.GetEquippedWeaponAttackRange(assets, catalog) == 0,
            "banked item does not count as an equipped weapon");
        equipped["InBank"] = 0;
        equipped["ItemIdx"] = 30001;
        var withAccessory = CombatService.NormalAttackWorldRange(
            80 + basicSword.AttackRange + 50,
            CharacterStatCalculator.GetEquippedWeaponAttackRange(assets, catalog));
        Check(Near(withAccessory, Math.Sqrt(95.0 * 95.0 - 200.0)),
            "extra numeric range from other equipment remains effective");
    }

    private static bool Near(double actual, double expected)
        => Math.Abs(actual - expected) < 0.0001;

    private static void Check(bool ok, string label)
        => SGO.Server.SmokeTests.SmokeAssert.Check(ok, label);
}
