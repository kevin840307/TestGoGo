using SGO.Server.Application.Abstractions;
using SGO.Server.Domain.Sessions;

namespace SGO.Server.Application.Character;

public sealed class ResourceRecoveryService
{
    private readonly ICharacterStore _characters;
    private readonly IConfiguration _config;

    public ResourceRecoveryService(
        ICharacterStore characters,
        IConfiguration config)
    {
        _characters = characters;
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

        var maxHp = Math.Max(1, Convert.ToInt32(character["MaxHP"]));
        var maxMp = Math.Max(0, Convert.ToInt32(character["MaxMP"]));
        var hp = Convert.ToInt32(character["HP"]);
        var mp = Convert.ToInt32(character["MP"]);

        if (hp >= maxHp && mp >= maxMp)
            return [];

        var hpPercent = Math.Max(
            0,
            _config.GetValue<int?>(
                "Gameplay:ResourceRecovery:HpPercent") ?? 1);
        var mpPercent = Math.Max(
            0,
            _config.GetValue<int?>(
                "Gameplay:ResourceRecovery:MpPercent") ?? 1);

        var minHp = Math.Max(
            0,
            _config.GetValue<int?>(
                "Gameplay:ResourceRecovery:MinimumHp") ?? 1);
        var minMp = Math.Max(
            0,
            _config.GetValue<int?>(
                "Gameplay:ResourceRecovery:MinimumMp") ?? 1);

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
            mpAmount);

        if (updated is null)
            return [];

        if (Convert.ToInt32(updated["HP"]) == hp &&
            Convert.ToInt32(updated["MP"]) == mp)
            return [];

        return
        [
            new
            {
                Type = "PlayerStatusSync",
                HP = updated["HP"],
                MP = updated["MP"],
                SP = updated["SP"],
                MaxHP = updated["MaxHP"],
                MaxMP = updated["MaxMP"],
                MaxSP = updated["MaxSP"]
            }
        ];
    }

    private bool Enabled()
        => _config.GetValue<bool?>(
               "Gameplay:ResourceRecovery:Enabled")
           ?? true;
}
