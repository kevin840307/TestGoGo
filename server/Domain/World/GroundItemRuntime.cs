namespace SGO.Server.Domain.World;

public sealed class GroundItemRuntime
{
    public long GroundItemId { get; init; }
    public int EntityId { get; init; }
    public int MapId { get; init; }
    public int TabType { get; init; }
    public int ItemIndex { get; init; }
    public int Count { get; init; }
    public int Damage { get; init; }
    public int Color { get; init; }
    public int Part { get; init; } = 255;
    public double X { get; init; }
    public double Y { get; init; }
    public long OwnerDbid { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProtectUntil { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    public bool IsProtectedFor(long dbid, DateTimeOffset now)
        => ProtectUntil.HasValue &&
           ProtectUntil.Value > now &&
           OwnerDbid > 0 &&
           OwnerDbid != dbid;

    public bool IsExpired(DateTimeOffset now)
        => ExpiresAt.HasValue &&
           ExpiresAt.Value <= now;

    public double RemainingSeconds(DateTimeOffset now)
        => ExpiresAt.HasValue
            ? Math.Max(0.0, (ExpiresAt.Value - now).TotalSeconds)
            : 0.0;

    public double ProtectSeconds(DateTimeOffset now)
        => ProtectUntil.HasValue
            ? Math.Max(0.0, (ProtectUntil.Value - now).TotalSeconds)
            : 0.0;
}
