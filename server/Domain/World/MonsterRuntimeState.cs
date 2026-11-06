namespace SGO.Server.Domain.World;

public sealed class MonsterRuntimeState
{
    public int EntityId { get; init; }
    public int LegacyEnemyId { get; init; }
    public int MapId { get; init; }
    public string Name { get; init; } = "";
    public string Sprite { get; init; } = "";
    public int Level { get; init; }
    public int MaxHp { get; init; }
    public int CurrentHp { get; set; }

    public double SpawnX { get; init; }
    public double SpawnY { get; init; }
    public double X { get; set; }
    public double Y { get; set; }

    public bool HasTarget { get; set; }
    public DateTimeOffset LastAttackAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset? RespawnAt { get; set; }

    public bool IsAlive => CurrentHp > 0 && RespawnAt is null;
}
