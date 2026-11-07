namespace SGO.Server.Domain.World;

public enum MonsterAiState
{
    Idle,
    Wander,
    Chase,
    Attack,
    Dead
}

public sealed class MonsterRuntimeState
{
    public int EntityId { get; init; }
    public int EnemyId { get; init; }
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
    public double WanderCenterX { get; set; }
    public double WanderCenterY { get; set; }

    public bool HasTarget { get; set; }
    public long TargetCharacterId { get; set; }
    // 0 means the monster targets the character directly. A positive value
    // means it targets that character's summoned pet while ownership stays
    // bound to TargetCharacterId for multiplayer tick arbitration.
    public long TargetPetUid { get; set; }
    public DateTimeOffset LastAiAt { get; set; } = DateTimeOffset.MinValue;
    public MonsterAiState AiState { get; set; } = MonsterAiState.Idle;

    // Compatibility mirrors while older tests/features migrate to AiState.
    // CombatService keeps these synchronized through SetAiState().
    public bool IsMoving { get; set; }
    public bool HoldingAttackRange { get; set; }
    public string CurrentDir { get; set; } = "1";
    public DateTimeOffset LastAttackAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset NextWanderAt { get; set; } = DateTimeOffset.MinValue;
    public int WanderSequence { get; set; }
    public int KillSequence { get; set; }
    public DateTimeOffset? RespawnAt { get; set; }

    public bool IsAlive => CurrentHp > 0 && RespawnAt is null;
}
