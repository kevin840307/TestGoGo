using SGO.Server.Domain.World;

namespace SGO.Server.Domain.Sessions;

public sealed class ClientSession
{
    public long? AccountId { get; set; }
    public long? CharacterId { get; set; }
    public int CurrentMapId { get; set; }
    public double LastX { get; set; }
    public double LastY { get; set; }
    public bool PendingMapSpawn { get; set; }
    public int WorldSnapshotRetriesRemaining { get; set; }
    public DateTimeOffset NextWorldSnapshotAt { get; set; } = DateTimeOffset.MinValue;
    public bool CloseRequested { get; set; }
    public DateTimeOffset LastMonsterAiTick { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastPetFollowTick { get; set; } = DateTimeOffset.MinValue;
    public int PetCombatMode { get; set; } = 0;
    public Dictionary<long, (double X, double Y)> PetPositions { get; } = new();
    public Dictionary<long, DateTimeOffset> PetLastAttackAt { get; } = new();
    public Dictionary<int, DateTimeOffset> SkillReadyAt { get; } = new();

    // Runtime entities are per session. Each visited map retains its monster
    // state so leaving and re-entering does not reset positions/HP.
    public Dictionary<int, MonsterRuntimeState> Monsters { get; } = new();
    public Dictionary<int, Dictionary<int, MonsterRuntimeState>> MonstersByMap { get; } = new();
    public int LoadedMonsterMapId { get; set; }
}
