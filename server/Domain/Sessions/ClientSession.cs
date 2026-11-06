namespace SGO.Server.Domain.Sessions;

public sealed class ClientSession
{
    public long? AccountId { get; set; }
    public long? CharacterId { get; set; }
    public int CurrentMapId { get; set; }
    public bool PendingMapSpawn { get; set; }
    public bool CloseRequested { get; set; }
}
