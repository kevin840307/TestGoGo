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

    public ResourceRecoveryService(
        ICharacterStore characters,
        ICharacterAssetReader assets,
        CurrentClientDataCatalog current,
        IConfiguration config)
    {
        _characters = characters;
        _assets = assets;
        _current = current;
        _config = config;
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

        var character = _characters.GetCharacter(
            session.AccountId.Value,
            session.CharacterId.Value);

        if (character is null ||
            Convert.ToInt32(character["HP"]) <= 0)
            return [];

        var effectiveAssets = _assets.GetCharacterAssets(
            session.AccountId.Value,
            session.CharacterId.Value);
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

        var updated = _characters.RecoverResources(
            session.AccountId.Value,
            session.CharacterId.Value,
            hpAmount,
            mpAmount,
            maxHp,
            maxMp);

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

    private bool Enabled()
        => _config.GetValue<bool?>(
               "Gameplay:ResourceRecovery:Enabled")
           ?? true;
}
