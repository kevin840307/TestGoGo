using System.Diagnostics;
using SGO.Server.Application.Assets;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Application.Abstractions;
using SGO.Server.Domain.Sessions;

namespace SGO.Server.Application.Character;

public sealed class ResourceRecoveryService
{
    private readonly ICharacterStore _characters;
    private readonly ICharacterAssetReader _assets;
    private readonly CurrentClientDataCatalog _current;
    private readonly IConfiguration _config;
    private readonly ILogger<ResourceRecoveryService>? _log;
    private long _lastSlowProfileUtcTicks;

    public ResourceRecoveryService(
        ICharacterStore characters,
        ICharacterAssetReader assets,
        CurrentClientDataCatalog current,
        IConfiguration config,
        ILogger<ResourceRecoveryService>? log = null)
    {
        _characters = characters;
        _assets = assets;
        _current = current;
        _config = config;
        _log = log;
    }

    // Called ONLY with the live session monitor held. Reserve the existing
    // recovery clock under that lock, then perform all potentially blocking
    // SQLite work against a detached immutable-in-practice snapshot. A slow
    // WAL writer must not stall inbound position packets or shared-map AI.
    // One pending task per WebSocket prevents duplicate recovery writers.
    public ClientSession? CaptureDueTick(
        ClientSession live,
        DateTimeOffset now)
    {
        if (!live.AccountId.HasValue || !live.CharacterId.HasValue || !Enabled())
            return null;

        var interval = TimeSpan.FromSeconds(Math.Clamp(
            _config.GetValue<int?>(
                "Gameplay:ResourceRecovery:IntervalSeconds") ?? 5, 1, 3600));
        if (now - live.LastResourceRecoveryAt < interval)
            return null;

        var previousTick = live.LastResourceRecoveryAt;
        live.LastResourceRecoveryAt = now;
        return new ClientSession
        {
            ConnectionId = live.ConnectionId,
            AccountId = live.AccountId,
            CharacterId = live.CharacterId,
            CurrentMapId = live.CurrentMapId,
            IsResting = live.IsResting,
            LastResourceRecoveryAt = previousTick
        };
    }

    public IReadOnlyList<object> Tick(
        ClientSession session,
        DateTimeOffset now)
    {
        if (!session.AccountId.HasValue ||
            !session.CharacterId.HasValue)
            return [];

        if (!Enabled())
            return [];

        var interval = TimeSpan.FromSeconds(
            Math.Clamp(
                _config.GetValue<int?>(
                    "Gameplay:ResourceRecovery:IntervalSeconds") ?? 5,
                1,
                3600));

        if (now - session.LastResourceRecoveryAt < interval)
            return [];

        session.LastResourceRecoveryAt = now;

        var totalStarted = Stopwatch.GetTimestamp();
        var characterReadMs = 0.0;
        var assetsReadMs = 0.0;
        var writeMs = 0.0;
        try
        {
        var characterStarted = Stopwatch.GetTimestamp();
        var character = _characters.GetCharacter(
            session.AccountId.Value,
            session.CharacterId.Value);
        characterReadMs = Stopwatch.GetElapsedTime(characterStarted).TotalMilliseconds;

        if (character is null ||
            Convert.ToInt32(character["HP"]) <= 0)
            return [];

        var assetsStarted = Stopwatch.GetTimestamp();
        var effectiveAssets = _assets.GetCharacterAssets(
            session.AccountId.Value,
            session.CharacterId.Value);
        assetsReadMs = Stopwatch.GetElapsedTime(assetsStarted).TotalMilliseconds;
        var effectiveStats = CharacterStatCalculator.Calculate(
            character,
            effectiveAssets,
            _current);

        var maxHp = Math.Max(1, effectiveStats.MaxHP);
        var maxMp = Math.Max(0, effectiveStats.MaxMP);
        var hp = Convert.ToInt32(character["HP"]);
        var mp = Convert.ToInt32(character["MP"]);

        if (hp >= maxHp && mp >= maxMp)
            return [];

        var normalHpPercent = Math.Max(
            0,
            _config.GetValue<int?>(
                "Gameplay:ResourceRecovery:HpPercent") ?? 1);
        var normalMpPercent = Math.Max(
            0,
            _config.GetValue<int?>(
                "Gameplay:ResourceRecovery:MpPercent") ?? 1);
        var restingHpPercent = Math.Max(
            0,
            _config.GetValue<int?>(
                "Gameplay:ResourceRecovery:RestingHpPercent") ?? 20);
        var restingMpPercent = Math.Max(
            0,
            _config.GetValue<int?>(
                "Gameplay:ResourceRecovery:RestingMpPercent") ?? 20);

        var hpPercent = session.IsResting
            ? restingHpPercent
            : normalHpPercent;
        var mpPercent = session.IsResting
            ? restingMpPercent
            : normalMpPercent;

        var minHp = Math.Max(
            0,
            _config.GetValue<int?>(
                "Gameplay:ResourceRecovery:MinimumHp") ?? 5);
        var minMp = Math.Max(
            0,
            _config.GetValue<int?>(
                "Gameplay:ResourceRecovery:MinimumMp") ?? 5);

        var hpAmount = hpPercent <= 0
            ? 0
            : Math.Max(minHp, maxHp * hpPercent / 100);
        var mpAmount = mpPercent <= 0
            ? 0
            : Math.Max(minMp, maxMp * mpPercent / 100);

        if (hpAmount <= 0 && mpAmount <= 0)
            return [];

        var writeStarted = Stopwatch.GetTimestamp();
        var updated = _characters.RecoverResources(
            session.AccountId.Value,
            session.CharacterId.Value,
            hpAmount,
            mpAmount,
            maxHp,
            maxMp);
        writeMs = Stopwatch.GetElapsedTime(writeStarted).TotalMilliseconds;

        if (updated is null)
            return [];

        if (Convert.ToInt32(updated["HP"]) == hp &&
            Convert.ToInt32(updated["MP"]) == mp)
            return [];

        return
        [
            CharacterStatCalculator.ToPlayerStatusPacket(
                updated,
                effectiveAssets,
                _current)
        ];
        }
        finally
        {
            var totalMs = Stopwatch.GetElapsedTime(totalStarted).TotalMilliseconds;
            if (_log is not null && totalMs >= 200.0)
            {
                var previous = Interlocked.Read(ref _lastSlowProfileUtcTicks);
                var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
                if ((previous == 0 || nowTicks - previous >= TimeSpan.FromSeconds(5).Ticks) &&
                    Interlocked.CompareExchange(ref _lastSlowProfileUtcTicks, nowTicks, previous) == previous)
                {
                    _log.LogWarning(
                        "[SLOW_RESOURCE_RECOVERY_PHASE] TotalMs={TotalMs:F1} CharacterReadMs={CharacterReadMs:F1} AssetsReadMs={AssetsReadMs:F1} WriteMs={WriteMs:F1} OtherMs={OtherMs:F1} Character={Character}",
                        totalMs, characterReadMs, assetsReadMs, writeMs,
                        Math.Max(0.0, totalMs - characterReadMs - assetsReadMs - writeMs),
                        session.CharacterId);
                }
            }
        }
    }

    private bool Enabled()
        => _config.GetValue<bool?>(
               "Gameplay:ResourceRecovery:Enabled")
           ?? true;
}
