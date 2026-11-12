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

        Check(normal.BaseChanceMultiplier == 0.25,
            "default monster drops reduce recovered item probability to 25%");
        Check(original.BaseChanceMultiplier == 1.0 &&
              zero.BaseChanceMultiplier == 0.0 &&
              invalid.BaseChanceMultiplier == 0.25,
            "drop chance factor is configurable, clamped and NaN-safe");
        Check(normal.LuckRollCount(50) == original.LuckRollCount(50),
            "rare-item luck rerolls stay unchanged");

        long originalDrops = 0;
        long reducedDrops = 0;
        long comparedKills = 0;
        long enemiesWithDrops = 0;
        long mappingMismatches = 0;
        long zeroChanceViolations = 0;
        long subsetViolations = 0;

        for (var enemyId = 1; enemyId <= 300; enemyId++)
        {
            if (original.GetDrops(enemyId).Count == 0)
                continue;
            enemiesWithDrops++;
            if (original.GetDrops(enemyId).Count != normal.GetDrops(enemyId).Count)
                mappingMismatches++;
            for (var sequence = 1; sequence <= 180; sequence++)
            {
                var entityId = 900000 + enemyId;
                // Fixed kill identity/sequence: any 50% result must also
                // appear under the original 100% probability table.
                var baseline = original.RollDrops(enemyId, entityId, sequence, 0, 1.0);
                var balanced = normal.RollDrops(enemyId, entityId, sequence, 0, 1.0);
                var none = zero.RollDrops(enemyId, entityId, sequence, 200, 10.0);
                if (none.Count != 0)
                    zeroChanceViolations++;
                var baseSet = baseline.Select(d => d.ItemId).ToHashSet();
                if (!balanced.All(d => baseSet.Contains(d.ItemId)))
                    subsetViolations++;
                originalDrops += baseline.Count;
                reducedDrops += balanced.Count;
                comparedKills++;
            }
        }

        Check(mappingMismatches == 0,
            "all original enemy item mappings and slot counts remain intact");
        Check(zeroChanceViolations == 0 && subsetViolations == 0,
            "no zero-chance items and no fabricated drops across the real kill sample");
        Check(enemiesWithDrops > 0 && originalDrops > 0,
            "recovered enemy table provides measurable item drop outcomes");
        Check(reducedDrops > 0 && reducedDrops < originalDrops,
            "quarter-rate item chance produces fewer actual loot items over real kills");
        Check((double)reducedDrops / originalDrops < 0.32 &&
              (double)reducedDrops / originalDrops > 0.18,
            "real recovered drop distribution is near 25 percent of original");
        Console.WriteLine(
            "[MONSTER_DROP_BALANCE] enemies={0} kills={1} legacyDrops={2} reducedDrops={3} ratio={4:F3}",
            enemiesWithDrops, comparedKills, originalDrops, reducedDrops,
            (double)reducedDrops / Math.Max(1, originalDrops));
        Console.WriteLine("[MONSTER_DROP_BALANCE] ALL PASS");
    }
}
