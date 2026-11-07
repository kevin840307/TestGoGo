namespace SGO.Server.Domain.Sessions;

public sealed class SkillBuffRuntime
{
    public int SkillId { get; init; }
    public string Name { get; init; } = "";
    public int Level { get; init; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int DurationSeconds { get; init; }

    public int AtkBonus { get; init; }
    public int DefBonus { get; init; }
    public int MAtkBonus { get; init; }
    public int MDefBonus { get; init; }
    public int HitBonus { get; init; }
    public int FleeBonus { get; init; }
    public int LukBonus { get; init; }
    public int AspdBonus { get; init; }
    public int MoveSpeedBonus { get; init; }
    public int CritRateBonus { get; init; }
    public int CritDamageBonus { get; init; }
}
