using SGO.Server.Application.Abstractions;

namespace SGO.Server.Application.Combat;

// Old/服務端/map.cfg [NonPKMap]; server-side map authority.
// The original client's 800x600 viewport plays no role in PvP permission.
public static class PvpRules
{
    public const long PinkDurationMilliseconds = 60_000;
    public const long PinkFlashMilliseconds = 3_000;
    private static readonly HashSet<int> NonPvpMaps =
        [7, 9, 15, 17, 22, 25, 38, 40];

    public static bool IsPvpMap(int mapId)
        => mapId > 0 && !NonPvpMaps.Contains(mapId);

    public static PvpNameState FromCharacter(
        Dictionary<string, object> character)
        => new(
            Convert.ToInt32(character.GetValueOrDefault("PKStatus", 0)),
            Convert.ToInt32(character.GetValueOrDefault("RedMan", 0)),
            Convert.ToInt64(character.GetValueOrDefault("PinkUntilUtcMs", 0L)));

    public static int NameColorState(PvpNameState state, long nowUtcMs)
    {
        if (state.RedMan > 0)
            return 1; // red
        var remaining = state.PinkUntilUtcMs - nowUtcMs;
        if (remaining > 0)
            return remaining <= PinkFlashMilliseconds ? 3 : 2;
        return 0; // PKStatus decides blue (0) / white (1)
    }

    public static int PinkRemainingMs(PvpNameState state, long nowUtcMs)
        => (int)Math.Clamp(state.PinkUntilUtcMs - nowUtcMs, 0L, int.MaxValue);

    public static object NameColorPacket(
        long entityId, PvpNameState state, long nowUtcMs)
        => new
        {
            Type = "UpdateNameColor",
            EntityID = entityId,
            ColorState = NameColorState(state, nowUtcMs),
            PKStatus = state.PkStatus,
            RedManKillLeft = 0,
            PartyID = "",
            PinkRemainingMs = PinkRemainingMs(state, nowUtcMs)
        };
}
