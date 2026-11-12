using SGO.Server.Infrastructure.GameData;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.GameData;

/// <summary>
/// Real recovered enemy CSV + current item catalog, but no DB or live
/// players. Validate deterministic, monotone drop odds after balance patch.
/// </summary>
internal static class MonsterDropRateSmokeTests
{
    public static void Run(string repoRoot)
    {
        var data = Path.Combine(repoRoot, "Old", "服務端", "Dat");
        var current = new CurrentClientDataCatalog(Path.Combine(repoRoot, "client"));
        var normal = new EnemyDropCatalog(data, current);
        var original = new EnemyDropCatalog(data, current, 1.0);
        var zero = new EnemyDropCatalog(data, current, 0.0);
        var invalid = new EnemyDropCatalog(data, current, double.NaN);

        Check(normal.BaseChanceMultiplier == 0.5,
            "default monster drops reduce recovered item probability to 50%");
        Check(original.BaseChanceMultiplier == 1.0 &&
              zero.BaseChanceMultiplier == 0.0 &&
              invalid.BaseChanceMultiplier == 0.5,
            "drop chance factor is configurable, clamped and NaN-safe");
        Check(normal.LuckRollCount(50) == original.LuckRollCount(50),
            "rare-item luck rerolls stay unchanged");

        long originalDrops = 0;
        long reducedDrops = 0;
        long comparedKills = 0;
        long enemiesWithDrops = 0;

        for (var enemyId = 1; enemyId <= 300; enemyId++)
        {
            if (original.GetDrops(enemyId).Count == 0)
                continue;
            enemiesWithDrops++;
            Check(original.GetDrops(enemyId).Count == normal.GetDrops(enemyId).Count,
                "original enemy item and rarity mappings remain intact");
            for (var sequence = 1; sequence <= 180; sequence++)
            {
                var entityId = 900000 + enemyId;
                // Fixed kill identity/sequence: any 50% result must also
                // appear under the original 100% probability table.
                var baseline = original.RollDrops(enemyId, entityId, sequence, 0, 1.0);
                var balanced = normal.RollDrops(enemyId, entityId, sequence, 0, 1.0);
                var none = zero.RollDrops(enemyId, entityId, sequence, 200, 10.0);
                Check(none.Count == 0, "zero chance prevents item drops at any luck/GM rate");
                var baseSet = baseline.Select(d => d.ItemId).ToHashSet();
                Check(balanced.All(d => baseSet.Contains(d.ItemId)),
                    "lowered drop chance does not fabricate any item for same kill");
                originalDrops += baseline.Count;
                reducedDrops += balanced.Count;
                comparedKills++;
            }
        }

        Check(enemiesWithDrops > 0 && originalDrops > 0,
            "recovered enemy table provides measurable item drop outcomes");
        Check(reducedDrops > 0 && reducedDrops < originalDrops,
            "halving item chance produces fewer actual loot items over real kills");
        Console.WriteLine(
            "[MONSTER_DROP_BALANCE] enemies={0} kills={1} legacyDrops={2} reducedDrops={3} ratio={4:F3}",
            enemiesWithDrops, comparedKills, originalDrops, reducedDrops,
            (double)reducedDrops / Math.Max(1, originalDrops));
        Console.WriteLine("[MONSTER_DROP_BALANCE] ALL PASS");
    }
}
