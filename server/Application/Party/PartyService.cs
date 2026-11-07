using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Messaging;
using SGO.Server.Domain.Sessions;

namespace SGO.Server.Application.Party;

public sealed class PartyService
{
    private const int MaxMembers = 6;
    private static readonly TimeSpan InviteLifetime =
        TimeSpan.FromSeconds(30);

    private sealed record MemberRef(
        long AccountId,
        long CharacterId);

    private sealed class PartyRuntime
    {
        public required string PartyId { get; init; }
        public long LeaderDbid { get; set; }
        public List<MemberRef> Members { get; } = new();
    }

    private sealed record PendingInvite(
        long InviterDbid,
        string PartyId,
        DateTimeOffset ExpiresAt);

    private readonly object _sync = new();
    private readonly ICharacterStore _characters;
    private readonly ServerBroadcastHub _broadcasts;
    private readonly Dictionary<string, PartyRuntime> _parties =
        new(StringComparer.Ordinal);
    private readonly Dictionary<long, string> _membership = new();
    private readonly Dictionary<long, PendingInvite> _invites = new();

    public PartyService(
        ICharacterStore characters,
        ServerBroadcastHub broadcasts)
    {
        _characters = characters;
        _broadcasts = broadcasts;
    }

    public string GetPartyId(long characterId)
    {
        lock (_sync)
        {
            return _membership.TryGetValue(
                    characterId,
                    out var partyId)
                ? partyId
                : "";
        }
    }

    public IReadOnlyList<(long AccountId, long CharacterId)> GetMembers(
        long characterId)
    {
        lock (_sync)
        {
            if (!_membership.TryGetValue(
                    characterId,
                    out var partyId) ||
                !_parties.TryGetValue(
                    partyId,
                    out var party))
            {
                return Array.Empty<(long AccountId, long CharacterId)>();
            }

            return party.Members
                .Select(x => (
                    x.AccountId,
                    x.CharacterId))
                .ToArray();
        }
    }

    public IReadOnlyList<object> Handle(
        ClientSession session,
        long accountId,
        string action,
        long targetDbid,
        DateTimeOffset now)
    {
        if (!session.CharacterId.HasValue)
            return [];

        var selfDbid = session.CharacterId.Value;
        return action switch
        {
            "Create" => Create(accountId, selfDbid),
            "Invite" => Invite(
                accountId,
                selfDbid,
                targetDbid,
                session.CurrentMapId,
                now),
            "Accept" => Accept(
                accountId,
                selfDbid,
                targetDbid,
                now),
            "Decline" => Decline(
                selfDbid,
                targetDbid),
            "Leave" => Leave(selfDbid),
            "Disband" => Disband(selfDbid),
            "Kick" => Kick(
                selfDbid,
                targetDbid),
            _ =>
            [
                Flash($"隊伍操作尚未支援：{action}")
            ]
        };
    }

    public void Disconnect(long characterId)
    {
        if (characterId <= 0)
            return;

        Leave(characterId);
        lock (_sync)
        {
            _invites.Remove(characterId);

            var pending = _invites
                .Where(x =>
                    x.Value.InviterDbid == characterId)
                .Select(x => x.Key)
                .ToArray();

            foreach (var target in pending)
                _invites.Remove(target);
        }
    }

    public void PublishStat(
        long accountId,
        long characterId)
    {
        string? partyId;
        List<long> targets;

        lock (_sync)
        {
            if (!_membership.TryGetValue(
                    characterId,
                    out partyId) ||
                !_parties.TryGetValue(
                    partyId,
                    out var party))
            {
                return;
            }

            targets = party.Members
                .Where(x =>
                    x.CharacterId != characterId)
                .Select(x => x.CharacterId)
                .ToList();
        }

        var character = _characters.GetCharacter(
            accountId,
            characterId);

        if (character is null)
            return;

        var packet = new
        {
            Type = "PartyStatUpdate",
            DBID = characterId,
            HP = character["HP"],
            MaxHP = character["MaxHP"],
            MP = character["MP"],
            MaxMP = character["MaxMP"],
            SP = character["SP"],
            MaxSP = character["MaxSP"],
            Level = character["Level"]
        };

        foreach (var target in targets)
            _broadcasts.PublishToCharacter(
                target,
                packet);
    }

    private IReadOnlyList<object> Create(
        long accountId,
        long selfDbid)
    {
        PartyRuntime party;

        lock (_sync)
        {
            if (_membership.TryGetValue(
                    selfDbid,
                    out var existingId) &&
                _parties.TryGetValue(
                    existingId,
                    out var existing))
            {
                party = existing;
            }
            else
            {
                party = CreatePartyLocked(
                    accountId,
                    selfDbid);
            }
        }

        return [BuildSyncPacket(party)];
    }

    private IReadOnlyList<object> Invite(
        long accountId,
        long selfDbid,
        long targetDbid,
        int currentMapId,
        DateTimeOffset now)
    {
        if (targetDbid <= 0 ||
            targetDbid == selfDbid)
        {
            return [Flash("邀請目標無效")];
        }

        var targetPresence =
            _broadcasts.FindCharacter(targetDbid);

        if (targetPresence is null ||
            targetPresence.MapId != currentMapId)
        {
            return [Flash("目標玩家不在附近或已離線")];
        }

        PartyRuntime party;
        lock (_sync)
        {
            CleanupInvitesLocked(now);

            if (_membership.ContainsKey(targetDbid))
                return [Flash("目標玩家已經有隊伍")];

            if (_membership.TryGetValue(
                    selfDbid,
                    out var partyId))
            {
                party = _parties[partyId];

                if (party.LeaderDbid != selfDbid)
                    return [Flash("只有隊長可以邀請玩家")];
            }
            else
            {
                party = CreatePartyLocked(
                    accountId,
                    selfDbid);
            }

            if (party.Members.Count >= MaxMembers)
                return [Flash("隊伍已滿")];

            _invites[targetDbid] = new PendingInvite(
                selfDbid,
                party.PartyId,
                now + InviteLifetime);
        }

        var self = _characters.GetCharacter(
            accountId,
            selfDbid);
        var inviterName =
            self?.GetValueOrDefault("NAME")?.ToString()
            ?? $"角色{selfDbid}";

        if (!_broadcasts.PublishToCharacter(
                targetDbid,
                new
                {
                    Type = "PartyInvite",
                    InviterDBID = selfDbid,
                    InviterName = inviterName
                }))
        {
            lock (_sync)
                _invites.Remove(targetDbid);

            return [Flash("目標玩家已離線")];
        }

        return [Flash("隊伍邀請已送出")];
    }

    private IReadOnlyList<object> Accept(
        long accountId,
        long selfDbid,
        long inviterDbid,
        DateTimeOffset now)
    {
        PartyRuntime party;

        lock (_sync)
        {
            CleanupInvitesLocked(now);

            if (_membership.ContainsKey(selfDbid))
                return [Flash("你已經有隊伍")];

            if (!_invites.TryGetValue(
                    selfDbid,
                    out var invite) ||
                invite.InviterDbid != inviterDbid)
            {
                return [Flash("隊伍邀請已失效")];
            }

            if (!_parties.TryGetValue(
                    invite.PartyId,
                    out var foundParty))
            {
                _invites.Remove(selfDbid);
                return [Flash("隊伍已不存在")];
            }

            party = foundParty;

            if (party.Members.Count >= MaxMembers)
            {
                _invites.Remove(selfDbid);
                return [Flash("隊伍已滿")];
            }

            party.Members.Add(new MemberRef(
                accountId,
                selfDbid));
            _membership[selfDbid] = party.PartyId;
            _invites.Remove(selfDbid);
        }

        PublishSyncToOthers(
            party,
            selfDbid);

        return [BuildSyncPacket(party)];
    }

    private IReadOnlyList<object> Decline(
        long selfDbid,
        long inviterDbid)
    {
        var declined = false;

        lock (_sync)
        {
            if (_invites.TryGetValue(
                    selfDbid,
                    out var invite) &&
                invite.InviterDbid == inviterDbid)
            {
                _invites.Remove(selfDbid);
                declined = true;
            }
        }

        if (declined)
        {
            _broadcasts.PublishToCharacter(
                inviterDbid,
                Flash("對方拒絕了隊伍邀請"));
        }

        return [];
    }

    private IReadOnlyList<object> Leave(
        long selfDbid)
    {
        PartyRuntime? remainingParty = null;
        var notifyEmpty = new List<long>();

        lock (_sync)
        {
            if (!_membership.TryGetValue(
                    selfDbid,
                    out var partyId) ||
                !_parties.TryGetValue(
                    partyId,
                    out var party))
            {
                return [EmptySync()];
            }

            party.Members.RemoveAll(x =>
                x.CharacterId == selfDbid);
            _membership.Remove(selfDbid);

            if (party.Members.Count <= 1)
            {
                foreach (var member in party.Members)
                {
                    _membership.Remove(
                        member.CharacterId);
                    notifyEmpty.Add(
                        member.CharacterId);
                }

                _parties.Remove(party.PartyId);
            }
            else
            {
                if (party.LeaderDbid == selfDbid)
                    party.LeaderDbid =
                        party.Members[0].CharacterId;

                remainingParty = party;
            }
        }

        foreach (var target in notifyEmpty)
            _broadcasts.PublishToCharacter(
                target,
                EmptySync());

        if (remainingParty is not null)
            PublishSyncToOthers(
                remainingParty,
                selfDbid);

        return [EmptySync()];
    }

    private IReadOnlyList<object> Disband(
        long selfDbid)
    {
        List<long> targets;

        lock (_sync)
        {
            if (!_membership.TryGetValue(
                    selfDbid,
                    out var partyId) ||
                !_parties.TryGetValue(
                    partyId,
                    out var party))
            {
                return [EmptySync()];
            }

            if (party.LeaderDbid != selfDbid)
                return [Flash("只有隊長可以解散隊伍")];

            targets = party.Members
                .Where(x =>
                    x.CharacterId != selfDbid)
                .Select(x => x.CharacterId)
                .ToList();

            foreach (var member in party.Members)
                _membership.Remove(
                    member.CharacterId);

            _parties.Remove(party.PartyId);
        }

        foreach (var target in targets)
            _broadcasts.PublishToCharacter(
                target,
                EmptySync());

        return [EmptySync()];
    }

    private IReadOnlyList<object> Kick(
        long selfDbid,
        long targetDbid)
    {
        PartyRuntime party;

        lock (_sync)
        {
            if (!_membership.TryGetValue(
                    selfDbid,
                    out var partyId) ||
                !_parties.TryGetValue(
                    partyId,
                    out var foundParty))
            {
                return [Flash("你目前沒有隊伍")];
            }

            party = foundParty;

            if (party.LeaderDbid != selfDbid)
                return [Flash("只有隊長可以踢除成員")];

            if (targetDbid == selfDbid)
                return [Flash("隊長請使用離開或解散隊伍")];

            if (!party.Members.Any(x =>
                    x.CharacterId == targetDbid))
            {
                return [Flash("目標不是隊伍成員")];
            }

            party.Members.RemoveAll(x =>
                x.CharacterId == targetDbid);
            _membership.Remove(targetDbid);
        }

        _broadcasts.PublishToCharacter(
            targetDbid,
            EmptySync());

        PublishSyncToOthers(
            party,
            selfDbid);

        return [BuildSyncPacket(party)];
    }

    private PartyRuntime CreatePartyLocked(
        long accountId,
        long selfDbid)
    {
        var party = new PartyRuntime
        {
            PartyId =
                $"local-{Guid.NewGuid():N}",
            LeaderDbid = selfDbid
        };

        party.Members.Add(new MemberRef(
            accountId,
            selfDbid));

        _parties[party.PartyId] = party;
        _membership[selfDbid] = party.PartyId;
        return party;
    }

    private void PublishSyncToOthers(
        PartyRuntime party,
        long selfDbid)
    {
        var packet = BuildSyncPacket(party);

        foreach (var member in party.Members)
        {
            if (member.CharacterId == selfDbid)
                continue;

            _broadcasts.PublishToCharacter(
                member.CharacterId,
                packet);
        }
    }

    private object BuildSyncPacket(
        PartyRuntime party)
        => new
        {
            Type = "PartySync",
            PartyID = party.PartyId,
            LeaderDBID = party.LeaderDbid,
            Members = party.Members
                .Select(BuildMember)
                .Where(x => x is not null)
                .ToArray()
        };

    private object? BuildMember(MemberRef member)
    {
        var character = _characters.GetCharacter(
            member.AccountId,
            member.CharacterId);

        if (character is null)
            return null;

        return new
        {
            DBID = member.CharacterId,
            Name = character["NAME"],
            Level = character["Level"],
            HP = character["HP"],
            MaxHP = character["MaxHP"],
            MP = character["MP"],
            MaxMP = character["MaxMP"],
            SP = character["SP"],
            MaxSP = character["MaxSP"]
        };
    }

    private static object EmptySync()
        => new
        {
            Type = "PartySync",
            PartyID = "",
            LeaderDBID = 0,
            Members = Array.Empty<object>()
        };

    private static object Flash(string message)
        => new
        {
            Type = "SystemMessageFlash",
            Message = message
        };

    private void CleanupInvitesLocked(
        DateTimeOffset now)
    {
        var expired = _invites
            .Where(x =>
                x.Value.ExpiresAt <= now)
            .Select(x => x.Key)
            .ToArray();

        foreach (var key in expired)
            _invites.Remove(key);
    }
}
