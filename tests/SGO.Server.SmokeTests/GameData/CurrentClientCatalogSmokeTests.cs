using SGO.Server.Infrastructure.GameData;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.GameData;

internal static class CurrentClientCatalogSmokeTests
{
    public static void Run(CurrentClientDataCatalog catalog)
    {
        var festivalRewards =
            new FestivalRewardCatalog(catalog.Root);

        Check(
            festivalRewards.Supports(2133) &&
            festivalRewards.GetRewards(2133).Any(x =>
                x.ItemId == 2014) &&
            festivalRewards.Supports(2837) &&
            festivalRewards.GetRewards(2837)
                .Select(x => x.ItemId)
                .OrderBy(x => x)
                .SequenceEqual(new[] { 2689, 2690, 2691, 2784 }),
            "real client Festival_Data random reward boxes are available to server");

        Check(
            catalog.FindSkillLevel(31112, 1) is not null,
            "real client loader keeps tick-only 隨身修理店 skill level");

        Check(
            catalog.FindSkillLevel(23108, 1) is
                { AtkBonusPercent: 50, DurationSeconds: 60 },
            "real Skill.csv enriches 狂戰士體 with +50% attack");

        Check(
            catalog.FindSkillLevel(26110, 1) is
                { MaxMpBonusPercent: 100, DurationSeconds: 600 },
            "real Skill.csv enriches 魔法之泉 with +100% MaxMP");

        Check(
            catalog.FindSkillLevel(27104, 1) is
                { DeathExpReductionPercent: 10 } &&
            catalog.FindSkillLevel(27104, 3) is
                { DeathExpReductionPercent: 30 } &&
            catalog.FindSkillLevel(27111, 1) is
                { DeathExpReductionPercent: 100 },
            "real Skill.csv exposes revive death-EXP reduction percentages");

        Check(
            catalog.ResolveSkillBook(3701) is
                { SkillId: 27103, SkillLevel: 1 },
            "general 加速術一級 book resolves to current skill 27103 Lv1");

        Check(
            catalog.ResolveSkillBook(3797) is
                { SkillId: 29106, SkillLevel: 2 },
            "general 雷之閃影二級 book resolves to current skill 29106 Lv2");

        Check(
            catalog.ResolveSkillBook(3239) is null,
            "crafting blueprint is not misclassified as a skill book");

        Check(
            catalog.ResolveExperienceCard(2460) is
                { MultiplierPercent: 200, DurationSeconds: 3600 } &&
            catalog.ResolveExperienceCard(2461) is
                { MultiplierPercent: 200, DurationSeconds: 10800 },
            "double-EXP cards resolve 2x multiplier and original 1h/3h durations");

        Check(
            catalog.FindItem(30371) is { WeaponStyle: "Sword" } &&
            catalog.FindItem(31139) is { WeaponStyle: "Gun" } &&
            catalog.FindItem(31214) is { WeaponStyle: "TwinGun" },
            "real weapon catalog exposes melee single-gun and twin-gun animation styles");

        Check(
            catalog.FindItem(2459) is { ScriptName: "無罪之書" },
            "real innocence book exposes its authoritative client script name");

        Check(
            catalog.FindItem(50816) is
            {
                Category: "horseequip",
                HorseType: "MA",
                EquipSlot: 6,
                Atk: 150,
                Def: 150,
                MAtk: 150,
                MDef: 150,
                Aspd: 50,
                MoveSpeed: 50
            },
            "MA guardian stone 50816 exposes +50 ASPD and +50 move speed to server");
    }
}
