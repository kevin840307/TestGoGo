using SGO.Server.Application.Combat;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.SmokeTests.World;

// Numeric range acceptance contracts are separate from monster AI, skill
// cast range and weapon-type classifications.
internal static class NormalAttackRangeSmokeTests
{
    public static void Run()
    {
        Check(CombatService.NormalAttackWorldRange(80) == 40.0,
            "unarmed numeric 80 -> ordinary reach 40");
        Check(CombatService.NormalAttackWorldRange(80 + 120) == 100.0,
            "sword range bonus 120 -> ordinary reach 100");
        Check(CombatService.NormalAttackWorldRange(80 + 200) == 140.0,
            "sword range bonus 200 remains longer than 120");
        Check(CombatService.NormalAttackWorldRange(80 + 400) == 240.0,
            "gun range bonus 400 remains long-distance");
        Check(CombatService.NormalAttackWorldRange(80 + 120 + 50) == 125.0,
            "accessory range bonus 50 increases reach by 25");
        Check(CombatService.NormalAttackWorldRange(-10) == 40.0,
            "negative numeric range clamps to a safe minimum");
    }

    public static void CheckCatalog(CurrentClientDataCatalog catalog)
    {
        var basicSword = catalog.FindItem(30001);
        var longRangeSword = catalog.FindItem(30102);
        var gun = catalog.FindItem(30120);
        Check(basicSword is not null && basicSword.AttackRange == 120
            && longRangeSword is not null && longRangeSword.AttackRange == 200
            && gun is not null && gun.AttackRange == 400,
            "real weapon data uses numeric 120/200/400 range values");
        Check(CombatService.NormalAttackWorldRange(80 + basicSword!.AttackRange)
            < CombatService.NormalAttackWorldRange(80 + longRangeSword!.AttackRange)
            && CombatService.NormalAttackWorldRange(80 + longRangeSword.AttackRange)
            < CombatService.NormalAttackWorldRange(80 + gun!.AttackRange),
            "real weapon reach respects numerical bonuses regardless of style");
    }

    private static void Check(bool ok, string label)
        => SGO.Server.SmokeTests.SmokeAssert.Check(ok, label);
}
