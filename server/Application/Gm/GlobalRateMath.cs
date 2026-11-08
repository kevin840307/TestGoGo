namespace SGO.Server.Application.Gm;

public static class GlobalRateMath
{
    public static int ApplyReward(
        long baseValue,
        double multiplier)
    {
        if (baseValue <= 0)
            return 0;

        var safeMultiplier =
            double.IsFinite(multiplier)
                ? Math.Clamp(multiplier, 1.0, 10.0)
                : 1.0;

        return (int)Math.Clamp(
            Math.Round(
                baseValue * safeMultiplier,
                MidpointRounding.AwayFromZero),
            0d,
            int.MaxValue);
    }

    public static double ApplyDropChance(
        double baseChance,
        double multiplier)
    {
        var chance = Math.Clamp(baseChance, 0.0, 1.0);
        var safeMultiplier =
            double.IsFinite(multiplier)
                ? Math.Clamp(multiplier, 1.0, 10.0)
                : 1.0;

        return Math.Clamp(
            chance * safeMultiplier,
            0.0,
            1.0);
    }
}
