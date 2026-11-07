using SGO.Server.Domain.World;

namespace SGO.Server.Domain.Sessions;

public sealed class ClientSession
{
    public Guid ConnectionId { get; set; }
    public int PublishedMapId { get; set; }
    public long PublishedCharacterId { get; set; }
    public long? AccountId { get; set; }
    public long? CharacterId { get; set; }
    public int CurrentMapId { get; set; }
    public double LastX { get; set; }
    public double LastY { get; set; }
    public bool PendingMapSpawn { get; set; }
    public bool WorldReady { get; set; }
    public int WorldSnapshotRetriesRemaining { get; set; }
    public DateTimeOffset NextWorldSnapshotAt { get; set; } = DateTimeOffset.MinValue;
    public bool CloseRequested { get; set; }
    public DateTimeOffset LastMonsterAiTick { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastPetFollowTick { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastResourceRecoveryAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastGroundItemCleanupAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastPartyStatAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastQuestProgressAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastCompanionRuntimeAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastCompanionClientSyncAt { get; set; } = DateTimeOffset.MinValue;
    public int PetCombatMode { get; set; } = 0;
    public bool AutoSaintEnabled { get; set; }
    public bool IsResting { get; set; }
    public bool CanCrossSpecialTerrain { get; set; }
    public bool PositionDirty { get; set; }
    public DateTimeOffset LastPositionPersistAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastPositionBroadcastAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastLocationReconcileAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastPingAt { get; set; } = DateTimeOffset.MinValue;
    public DateTimeOffset LastPongAt { get; set; } = DateTimeOffset.UtcNow;
    public int AutoSaintMpThreshold { get; set; } = 30;
    public int AutoSaintSpThreshold { get; set; } = 30;

    public string PendingTrainTravelId { get; set; } = "";
    public int PendingTrainNpcEntityId { get; set; }
    public string PendingTrainScript { get; set; } = "";
    public string PendingTrainContinuation { get; set; } = "";
    public DateTimeOffset PendingTrainExpiresAt { get; set; } =
        DateTimeOffset.MinValue;
    public Dictionary<long, (double X, double Y)> PetPositions { get; } = new();
    public Dictionary<long, DateTimeOffset> PetLastAttackAt { get; } = new();
    public Dictionary<long, int> PetTargets { get; } = new();
    public Dictionary<long, DateTimeOffset> PetOutOfRangeSince { get; } = new();
    public Dictionary<long, DateTimeOffset> PetNextIdleSpeechAt { get; } = new();
    public Dictionary<long, int> PetIdleSpeechSequence { get; } = new();

    public string PartyId { get; set; } = "";
    public long PartyLeaderDbid { get; set; }
    public List<Dictionary<string, object>> PartyMembers { get; } = new();

    public bool IsPlayerShopOpen { get; set; }
    public string PlayerShopName { get; set; } = "";
    public List<Dictionary<string, object>> PlayerShopItems { get; } = new();

    public Dictionary<int, DateTimeOffset> SkillReadyAt { get; } = new();
    public Dictionary<int, SkillBuffRuntime> ActiveSkillBuffs { get; } = new();

    public Dictionary<int, GroundItemRuntime> GroundItems { get; } = new();
    public int NextGroundItemEntityId { get; set; } = 6_000_000;

    // Runtime entities are per session. Each visited map retains its monster
    // state so leaving and re-entering does not reset positions/HP.
    public Dictionary<int, MonsterRuntimeState> Monsters { get; } = new();
    public Dictionary<int, Dictionary<int, MonsterRuntimeState>> MonstersByMap { get; } = new();
    public int LoadedMonsterMapId { get; set; }
}
