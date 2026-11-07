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
}
