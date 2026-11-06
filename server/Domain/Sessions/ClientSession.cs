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

    // Runtime entities are intentionally per session for now.
    // This prevents one local player's combat from mutating cached legacy definitions.
    public Dictionary<int, MonsterRuntimeState> Monsters { get; } = new();
}
