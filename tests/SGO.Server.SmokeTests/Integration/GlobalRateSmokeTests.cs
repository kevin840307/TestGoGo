using SGO.Server.Application.Gm;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class GlobalRateSmokeTests
{
    public static void Run()
    {
        Check(
            GlobalRateMath.ApplyReward(100, 1.0) == 100 &&
            GlobalRateMath.ApplyReward(100, 2.0) == 200 &&
            GlobalRateMath.ApplyReward(3, 1.5) == 5 &&
            GlobalRateMath.ApplyReward(0, 10.0) == 0 &&
            GlobalRateMath.ApplyReward(long.MaxValue, 10.0) == int.MaxValue,
            "global reward math is single-application, rounded and overflow-safe");

        Check(
            Math.Abs(GlobalRateMath.ApplyDropChance(0.05, 2.0) - 0.10) < 0.000001 &&
            GlobalRateMath.ApplyDropChance(0.8, 2.0) == 1.0 &&
            GlobalRateMath.ApplyDropChance(0.05, double.NaN) == 0.05,
            "global drop math multiplies once and clamps to valid probability");

        var service = new GlobalRateService();
        var start = DateTimeOffset.UtcNow.AddMinutes(10);

        var scheduled = service.Set(
            experienceMultiplier: 2.0,
            moneyMultiplier: 1.5,
            dropMultiplier: 3.0,
            startsAt: start,
            duration: TimeSpan.FromMinutes(60));

        Check(
            scheduled.EffectiveExperience(start.AddSeconds(-1)) == 1.0 &&
            scheduled.EffectiveMoney(start.AddSeconds(-1)) == 1.0 &&
            scheduled.EffectiveDrop(start.AddSeconds(-1)) == 1.0,
            "global rates stay at 1x before scheduled start");

        var activeAt = start.AddMinutes(30);
        Check(
            scheduled.EffectiveExperience(activeAt) == 2.0 &&
            scheduled.EffectiveMoney(activeAt) == 1.5 &&
            scheduled.EffectiveDrop(activeAt) == 3.0,
            "global rates apply during scheduled window");

        Check(
            scheduled.EffectiveExperience(scheduled.ExpiresAt) == 1.0 &&
            scheduled.EffectiveMoney(scheduled.ExpiresAt) == 1.0 &&
            scheduled.EffectiveDrop(scheduled.ExpiresAt) == 1.0,
            "global rates automatically expire without a timer");

        var clamped = service.Set(
            99,
            double.PositiveInfinity,
            0,
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(30));

        Check(
            clamped.ExperienceMultiplier == 10.0 &&
            clamped.MoneyMultiplier == 1.0 &&
            clamped.DropMultiplier == 1.0 &&
            clamped.ExpiresAt - clamped.StartsAt == TimeSpan.FromDays(7),
            "global rate input is bounded for safe administration");

        var persistedPath = Path.Combine(
            Path.GetTempPath(),
            "sgo-global-rate-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var persistent = new GlobalRateService(persistedPath);
            var persisted = persistent.Set(
                2.5,
                2.0,
                1.5,
                DateTimeOffset.UtcNow.AddMinutes(5),
                TimeSpan.FromMinutes(45));

            var reloaded = new GlobalRateService(persistedPath).Snapshot();
            Check(
                reloaded == persisted,
                "global rate schedule survives service restart");
        }
        finally
        {
            if (File.Exists(persistedPath))
                File.Delete(persistedPath);
            if (File.Exists(persistedPath + ".tmp"))
                File.Delete(persistedPath + ".tmp");
        }

        var concurrent = new GlobalRateService();
        var writerA = Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
                concurrent.Set(2.0, 3.0, 4.0, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        });
        var writerB = Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
                concurrent.Set(5.0, 6.0, 7.0, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        });
        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 3000; i++)
            {
                var snap = concurrent.Snapshot();
                var first =
                    snap.ExperienceMultiplier == 2.0 &&
                    snap.MoneyMultiplier == 3.0 &&
                    snap.DropMultiplier == 4.0;
                var second =
                    snap.ExperienceMultiplier == 5.0 &&
                    snap.MoneyMultiplier == 6.0 &&
                    snap.DropMultiplier == 7.0;
                var baseline =
                    snap.ExperienceMultiplier == 1.0 &&
                    snap.MoneyMultiplier == 1.0 &&
                    snap.DropMultiplier == 1.0;
                Check(
                    first || second || baseline,
                    "concurrent readers never observe a torn global-rate snapshot");
            }
        });
        Task.WaitAll(writerA, writerB, reader);

        service.Cancel();
        var cancelled = service.Snapshot();
        Check(
            !cancelled.IsActive(DateTimeOffset.UtcNow) &&
            cancelled.EffectiveExperience(DateTimeOffset.UtcNow) == 1.0,
            "cancel restores normal global rates");
    }
}
