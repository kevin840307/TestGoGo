namespace SGO.Server.Application.Gm;

public sealed record GlobalRateSnapshot(
    double ExperienceMultiplier,
    double MoneyMultiplier,
    double DropMultiplier,
    DateTimeOffset StartsAt,
    DateTimeOffset ExpiresAt)
{
    public bool IsActive(DateTimeOffset now)
        => now >= StartsAt && now < ExpiresAt;

    public double EffectiveExperience(DateTimeOffset now)
        => IsActive(now) ? ExperienceMultiplier : 1.0;

    public double EffectiveMoney(DateTimeOffset now)
        => IsActive(now) ? MoneyMultiplier : 1.0;

    public double EffectiveDrop(DateTimeOffset now)
        => IsActive(now) ? DropMultiplier : 1.0;
}

public sealed class GlobalRateService
{
    private static readonly GlobalRateSnapshot Default = new(
        1.0,
        1.0,
        1.0,
        DateTimeOffset.MinValue,
        DateTimeOffset.MinValue);

    private GlobalRateSnapshot _state = Default;

    public GlobalRateSnapshot Snapshot()
        => Volatile.Read(ref _state);

    public GlobalRateSnapshot Set(
        double experienceMultiplier,
        double moneyMultiplier,
        double dropMultiplier,
        DateTimeOffset startsAt,
        TimeSpan duration)
    {
        var safeDuration = duration <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(1)
            : duration > TimeSpan.FromDays(7)
                ? TimeSpan.FromDays(7)
                : duration;

        var next = new GlobalRateSnapshot(
            ClampMultiplier(experienceMultiplier),
            ClampMultiplier(moneyMultiplier),
            ClampMultiplier(dropMultiplier),
            startsAt,
            startsAt.Add(safeDuration));

        Volatile.Write(ref _state, next);
        return next;
    }

    public GlobalRateSnapshot Cancel()
    {
        Volatile.Write(ref _state, Default);
        return Default;
    }

    private static double ClampMultiplier(double value)
        => double.IsFinite(value)
            ? Math.Clamp(value, 1.0, 10.0)
            : 1.0;
}
