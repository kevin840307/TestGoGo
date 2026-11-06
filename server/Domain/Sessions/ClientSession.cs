using SGO.Server.Domain.World;

namespace SGO.Server.Domain.Sessions;

public sealed class ClientSession
{
    public long? AccountId { get; set; }
    public long? CharacterId { get; set; }
    public int CurrentMapId { get; set; }
    public bool PendingMapSpawn { get; set; }
    public bool CloseRequested { get; set; }

    // Runtime entities are intentionally per session for now.
    // This prevents one local player's combat from mutating cached legacy definitions.
    public Dictionary<int, MonsterRuntimeState> Monsters { get; } = new();
}
