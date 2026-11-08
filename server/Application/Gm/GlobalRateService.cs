using System.Text.Json;

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

    private readonly string? _persistencePath;
    private GlobalRateSnapshot _state;

    public GlobalRateService(string? persistencePath = null)
    {
        _persistencePath = persistencePath;
        _state = Load(persistencePath) ?? Default;
    }

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
        Persist(next);
        return next;
    }

    public GlobalRateSnapshot Cancel()
    {
        Volatile.Write(ref _state, Default);
        Persist(Default);
        return Default;
    }

    private void Persist(GlobalRateSnapshot value)
    {
        if (string.IsNullOrWhiteSpace(_persistencePath))
            return;

        var directory = Path.GetDirectoryName(_persistencePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = _persistencePath + ".tmp";
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(value));
        File.Move(temp, _persistencePath, overwrite: true);
    }

    private static GlobalRateSnapshot? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<GlobalRateSnapshot>(
                File.ReadAllText(path));
        }
        catch
        {
            // A broken optional admin-state file must never prevent the
            // game server from starting. The admin UI can simply recreate it.
            return null;
        }
    }

    private static double ClampMultiplier(double value)
        => double.IsFinite(value)
            ? Math.Clamp(value, 1.0, 10.0)
            : 1.0;
}
