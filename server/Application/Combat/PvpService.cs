using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.Party;
using SGO.Server.Domain.Sessions;

namespace SGO.Server.Application.Combat;

/// <summary>
/// Server-authoritative player-vs-player rules reconstructed from the legacy
/// MapPK/CanPK split:
/// - map permission and player name state are separate checks;
/// - blue players cannot initiate or receive PvP;
/// - white players can fight only on PvP maps;
/// - attacking a normal white player turns the attacker pink temporarily;
/// - killing a normal white player turns the killer red;
/// - attacking/killing pink or red players does not escalate the attacker.
/// Name-color animation timing is sent to clients, but attack legality always
/// remains authoritative here.
/// </summary>
public sealed class PvpService
{
    public const int PinkDurationSeconds = 30;
    public const int PinkFlashSeconds = 3;

    // Legacy Old/服務端/map.cfg [NonPKMap]:
    // 15, 9, 17, 22, 25, 38, 7, 40.
    // Keep the current recovered-client special/safe maps protected as well;
    // none of them are part of the legacy normal PK map set.
    private static readonly HashSet<int> NonPvpMapIds =
    [
        4, 7, 9, 15, 17, 22, 25, 38, 39, 40, 53, 75, 93
    ];

    private readonly IGameStore _store;
    private readonly ServerBroadcastHub _broadcasts;
    private readonly PartyService _party;

    public PvpService(
        IGameStore store,
        ServerBroadcastHub broadcasts,
        PartyService party)
    {
        _store = store;
        _broadcasts = broadcasts;
        _party = party;
    }

    public bool IsPvpMap(int mapId)
        => IsPvpMapId(mapId);

    public static bool IsPvpMapId(int mapId)
        => mapId > 0 && !NonPvpMapIds.Contains(mapId);

    public static bool IsPink(
        Dictionary<string, object> character,
        DateTimeOffset now)
        => Convert.ToInt64(
               character.GetValueOrDefault(
                   "PKPinkUntilUtcMs",
                   0L)) >
           now.ToUnixTimeMilliseconds();

    public static bool IsNormalWhite(
        Dictionary<string, object> character,
        DateTimeOffset now)
        => Convert.ToInt32(
               character.GetValueOrDefault(
                   "PKStatus",
                   1)) == 1 &&
           Convert.ToInt32(
               character.GetValueOrDefault(
                   "RedMan",
                   0)) <= 0 &&
           !IsPink(character, now);

    public static int NameColorState(
        Dictionary<string, object> character,
        DateTimeOffset now)
    {
        if (Convert.ToInt32(
                character.GetValueOrDefault(
                    "RedMan",
                    0)) > 0)
            return 1;

        var remainingMs =
            Convert.ToInt64(
                character.GetValueOrDefault(
                    "PKPinkUntilUtcMs",
                    0L)) -
            now.ToUnixTimeMilliseconds();

        if (remainingMs <= 0)
            return 0;

        return remainingMs <= PinkFlashSeconds * 1000L
            ? 3
            : 2;
    }

    public static double PinkRemainingSeconds(
        Dictionary<string, object> character,
        DateTimeOffset now)
        => Math.Max(
            0.0,
            (Convert.ToInt64(
                 character.GetValueOrDefault(
                     "PKPinkUntilUtcMs",
                     0L)) -
             now.ToUnixTimeMilliseconds()) /
            1000.0);

    public bool CanAttack(
        ClientSession attackerSession,
        Dictionary<string, object> attacker,
        OnlinePlayerPresence targetPresence,
        Dictionary<string, object> target,
        DateTimeOffset now,
        out string reason)
    {
        reason = "";

        if (!attackerSession.CharacterId.HasValue ||
            attackerSession.CharacterId.Value ==
            targetPresence.CharacterId)
        {
            reason = "不能攻擊自己";
            return false;
        }

        if (targetPresence.MapId != attackerSession.CurrentMapId)
        {
            reason = "攻擊目標不在目前地圖";
            return false;
        }

        if (!IsPvpMap(attackerSession.CurrentMapId))
        {
            reason = "目前地圖禁止玩家對戰";
            return false;
        }

        if (Convert.ToInt32(
                attacker.GetValueOrDefault(
                    "HP",
                    0)) <= 0)
        {
            reason = "死亡狀態無法攻擊";
            return false;
        }

        if (Convert.ToInt32(
                target.GetValueOrDefault(
                    "HP",
                    0)) <= 0)
        {
            reason = "目標已死亡";
            return false;
        }

        if (!IsCombatEligible(attacker, now))
        {
            reason = "藍名狀態無法攻擊玩家";
            return false;
        }

        if (!IsCombatEligible(target, now))
        {
            reason = "藍名玩家不可被攻擊";
            return false;
        }

        var attackerParty =
            _party.GetPartyId(
                attackerSession.CharacterId.Value);
        var targetParty =
            _party.GetPartyId(
                targetPresence.CharacterId);

        if (!string.IsNullOrEmpty(attackerParty) &&
            attackerParty == targetParty)
        {
            reason = "無法攻擊同隊玩家";
            return false;
        }

        return true;
    }

    public Dictionary<string, object>? MarkHostileAttack(
        long accountId,
        long characterId,
        Dictionary<string, object> attacker,
        bool targetWasNormalWhite,
        DateTimeOffset now)
    {
        if (!targetWasNormalWhite ||
            Convert.ToInt32(
                attacker.GetValueOrDefault(
                    "RedMan",
                    0)) > 0)
            return attacker;

        var currentUntil =
            Convert.ToInt64(
                attacker.GetValueOrDefault(
                    "PKPinkUntilUtcMs",
                    0L));
        var refreshedUntil = Math.Max(
            currentUntil,
            now.AddSeconds(PinkDurationSeconds)
                .ToUnixTimeMilliseconds());

        var updated = _store.SetPvpState(
            accountId,
            characterId,
            pkStatus: 1,
            redMan: 0,
            redManKillLeft: Convert.ToInt32(
                attacker.GetValueOrDefault(
                    "RedManKillLeft",
                    0)),
            pinkUntilUtcMs: refreshedUntil);

        if (updated is not null)
            PublishNameColor(updated, now);

        return updated;
    }

    public Dictionary<string, object>? MarkWhitePlayerKill(
        long accountId,
        long characterId,
        Dictionary<string, object> attacker,
        bool victimWasNormalWhite,
        DateTimeOffset now)
    {
        if (!victimWasNormalWhite)
            return attacker;

        var updated = _store.SetPvpState(
            accountId,
            characterId,
            pkStatus: 1,
            redMan: 1,
            redManKillLeft: Convert.ToInt32(
                attacker.GetValueOrDefault(
                    "RedManKillLeft",
                    0)),
            pinkUntilUtcMs: 0);

        if (updated is not null)
            PublishNameColor(updated, now);

        return updated;
    }

    public Dictionary<string, object>? SetNameColor(
        long accountId,
        long characterId,
        string requestedColor,
        DateTimeOffset now)
    {
        var current = _store.GetCharacter(
            accountId,
            characterId);
        if (current is null)
            return null;

        var normalized = requestedColor
            .Trim()
            .ToLowerInvariant();

        var pkStatus = 1;
        var redMan = 0;
        var pinkUntil = 0L;

        switch (normalized)
        {
            case "blue":
            case "藍":
            case "藍名":
                pkStatus = 0;
                break;

            case "white":
            case "白":
            case "白名":
                pkStatus = 1;
                break;

            case "pink":
            case "purple":
            case "粉":
            case "粉紅":
            case "粉紅名":
                pkStatus = 1;
                pinkUntil = now
                    .AddSeconds(PinkDurationSeconds)
                    .ToUnixTimeMilliseconds();
                break;

            case "flash":
            case "flashing":
            case "閃粉":
            case "閃爍粉紅":
                pkStatus = 1;
                pinkUntil = now
                    .AddSeconds(PinkFlashSeconds)
                    .ToUnixTimeMilliseconds();
                break;

            case "red":
            case "紅":
            case "紅名":
                pkStatus = 1;
                redMan = 1;
                break;

            default:
                return null;
        }

        var updated = _store.SetPvpState(
            accountId,
            characterId,
            pkStatus,
            redMan,
            Convert.ToInt32(
                current.GetValueOrDefault(
                    "RedManKillLeft",
                    0)),
            pinkUntil);

        if (updated is not null)
            PublishNameColor(updated, now);

        return updated;
    }

    public object BuildNameColorPacket(
        Dictionary<string, object> character,
        DateTimeOffset now)
        => new
        {
            Type = "UpdateNameColor",
            EntityID = Convert.ToInt64(
                character["DBID"]),
            ColorState =
                NameColorState(character, now),
            PKStatus = Convert.ToInt32(
                character.GetValueOrDefault(
                    "PKStatus",
                    1)),
            RedManKillLeft = Convert.ToInt32(
                character.GetValueOrDefault(
                    "RedManKillLeft",
                    0)),
            PinkRemainingSeconds =
                PinkRemainingSeconds(character, now)
        };

    public void PublishNameColor(
        Dictionary<string, object> character,
        DateTimeOffset now)
    {
        var characterId =
            Convert.ToInt64(
                character.GetValueOrDefault(
                    "DBID",
                    0L));
        if (characterId <= 0)
            return;

        var packet =
            BuildNameColorPacket(
                character,
                now);
        var presence =
            _broadcasts.FindCharacter(
                characterId);

        if (presence is null)
            return;

        _broadcasts.PublishToCharacter(
            characterId,
            packet);
        _broadcasts.PublishToPlayerObservers(
            presence.ConnectionId,
            packet);
    }

    private static bool IsCombatEligible(
        Dictionary<string, object> character,
        DateTimeOffset now)
        => Convert.ToInt32(
               character.GetValueOrDefault(
                   "PKStatus",
                   1)) == 1 ||
           Convert.ToInt32(
               character.GetValueOrDefault(
                   "RedMan",
                   0)) > 0 ||
           IsPink(character, now);
}
