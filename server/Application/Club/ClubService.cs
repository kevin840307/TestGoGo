using System.Collections.Concurrent;
using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Messaging;

namespace SGO.Server.Application.Club;

public sealed class ClubService
{
    private sealed record PendingInvite(
        long ClubId,
        long InviterDbId,
        long ExpiresUtcMs);

    private readonly IGameStore _store;
    private readonly ServerBroadcastHub _broadcasts;
    private readonly ConcurrentDictionary<long, PendingInvite> _invites = new();

    public ClubService(
        IGameStore store,
        ServerBroadcastHub broadcasts)
    {
        _store = store;
        _broadcasts = broadcasts;
    }

    public IReadOnlyList<object> Handle(
        long accountId,
        long dbid,
        string action,
        long targetDbid,
        string clubName,
        string clubMessage,
        long amount,
        int researchId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        action = action.Trim();

        switch (action)
        {
            case "GetInfo":
                return [BuildSync(accountId, dbid)];

            case "Create":
            {
                var result = _store.CreateClub(
                    accountId,
                    dbid,
                    clubName,
                    now);

                if (result.Success)
                    BroadcastClubSync(dbid);

                return BuildMutationResponse(
                    accountId,
                    dbid,
                    result);
            }

            case "Invite":
                return Invite(
                    accountId,
                    dbid,
                    targetDbid,
                    now);

            case "Accept":
                return Accept(
                    accountId,
                    dbid,
                    targetDbid,
                    now);

            case "UpdateMessage":
            {
                var result = _store.UpdateClubMessage(
                    accountId,
                    dbid,
                    clubMessage);

                if (result.Success)
                    BroadcastClubSync(dbid);

                return BuildMutationResponse(
                    accountId,
                    dbid,
                    result);
            }

            case "DonateMoney":
            {
                var result = _store.DonateClubMoney(
                    accountId,
                    dbid,
                    amount);

                if (result.Success)
                    BroadcastClubSync(dbid);

                return BuildMutationResponse(
                    accountId,
                    dbid,
                    result);
            }

            case "DonateExp":
            {
                var result = _store.DonateClubExp(
                    accountId,
                    dbid,
                    amount);

                if (result.Success)
                    BroadcastClubSync(dbid);

                return BuildMutationResponse(
                    accountId,
                    dbid,
                    result);
            }

            case "Research":
            {
                var result = _store.StartClubResearch(
                    accountId,
                    dbid,
                    researchId,
                    now);

                if (result.Success)
                    BroadcastClubSync(dbid);

                return BuildMutationResponse(
                    accountId,
                    dbid,
                    result);
            }

            default:
                return [new
                {
                    Type = "SystemMessageFlash",
                    Message = "不支援的戰團操作"
                }];
        }
    }

    public object BuildSync(
        long accountId,
        long dbid)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var snapshot = _store.GetClubSnapshot(
            accountId,
            dbid,
            now);

        if (snapshot is null)
        {
            return new
            {
                Type = "ClubSync",
                ClubID = 0,
                ClubName = "",
                ClubLevel = 0,
                ClubExp = 0L,
                ClubSaving = 0L,
                ClubMsg = "",
                ClubMaxMember = 10,
                MyPosition = 0,
                MyDonation = 0L,
                ActiveResearchID = 0,
                ActiveResearchRemainingSec = 0,
                Members = Array.Empty<object>(),
                Researches = Array.Empty<object>()
            };
        }

        var remainingSec = snapshot.ActiveResearchId > 0
            ? Math.Max(
                0L,
                (snapshot.ActiveResearchEndUtcMs - now + 999L) / 1000L)
            : 0L;

        var members = snapshot.Members
            .Select(x => new
            {
                DBID = x.DbId,
                Name = x.Name,
                Level = x.Level,
                Career = x.Career,
                Job = x.Job,
                Position = x.Position,
                Donation = x.Donation,
                IsOnline = _broadcasts.FindCharacter(x.DbId) is not null,
                LastLoginDate = "未知"
            })
            .Cast<object>()
            .ToArray();

        var researches = snapshot.Researches
            .Select(x => new
            {
                ResearchID = x.ResearchId,
                Level = x.Level
            })
            .Cast<object>()
            .ToArray();

        return new
        {
            Type = "ClubSync",
            ClubID = snapshot.ClubId,
            ClubName = snapshot.ClubName,
            ClubLevel = snapshot.ClubLevel,
            ClubExp = snapshot.ClubExp,
            ClubSaving = snapshot.ClubSaving,
            ClubMsg = snapshot.ClubMessage,
            ClubMaxMember = Math.Min(
                50,
                Math.Max(1, snapshot.ClubLevel) * 10),
            MyPosition = snapshot.MyPosition,
            MyDonation = snapshot.MyDonation,
            ActiveResearchID = snapshot.ActiveResearchId,
            ActiveResearchRemainingSec = remainingSec,
            Members = members,
            Researches = researches
        };
    }

    private IReadOnlyList<object> Invite(
        long accountId,
        long dbid,
        long targetDbid,
        long now)
    {
        if (targetDbid <= 0 || targetDbid == dbid)
        {
            return [new
            {
                Type = "SystemMessageFlash",
                Message = "邀請目標無效"
            }];
        }

        var snapshot = _store.GetClubSnapshot(
            accountId,
            dbid,
            now);

        if (snapshot is null)
        {
            return [new
            {
                Type = "SystemMessageFlash",
                Message = "你尚未加入戰團"
            }];
        }

        if (snapshot.MyPosition < 3)
        {
            return [new
            {
                Type = "SystemMessageFlash",
                Message = "只有團長可以邀請成員"
            }];
        }

        if (_store.GetClubIdForCharacter(targetDbid) > 0)
        {
            return [new
            {
                Type = "SystemMessageFlash",
                Message = "對方已經加入戰團"
            }];
        }

        if (_broadcasts.FindCharacter(targetDbid) is null)
        {
            return [new
            {
                Type = "SystemMessageFlash",
                Message = "對方目前不在線"
            }];
        }

        var inviter = snapshot.Members
            .FirstOrDefault(x => x.DbId == dbid);

        _invites[targetDbid] = new PendingInvite(
            snapshot.ClubId,
            dbid,
            now + 60_000L);

        _broadcasts.PublishToCharacter(
            targetDbid,
            new
            {
                Type = "ClubInvite",
                InviterDBID = dbid,
                InviterName = inviter?.Name ?? "玩家",
                ClubName = snapshot.ClubName
            });

        return [new
        {
            Type = "SystemMessageFlash",
            Message = "戰團邀請已送出"
        }];
    }

    private IReadOnlyList<object> Accept(
        long accountId,
        long dbid,
        long inviterDbid,
        long now)
    {
        if (!_invites.TryGetValue(dbid, out var invite) ||
            invite.InviterDbId != inviterDbid ||
            invite.ExpiresUtcMs < now)
        {
            _invites.TryRemove(dbid, out _);
            return [new
            {
                Type = "SystemMessageFlash",
                Message = "戰團邀請已失效"
            }];
        }

        var result = _store.JoinClub(
            accountId,
            dbid,
            invite.ClubId,
            now);

        if (result.Success)
        {
            _invites.TryRemove(dbid, out _);
            BroadcastClubSync(dbid);
        }

        return BuildMutationResponse(
            accountId,
            dbid,
            result);
    }

    private IReadOnlyList<object> BuildMutationResponse(
        long accountId,
        long dbid,
        ClubMutationResult result)
    {
        var packets = new List<object>
        {
            new
            {
                Type = "SystemMessageFlash",
                Message = result.Message
            }
        };

        if (result.Success)
            packets.Add(BuildSync(accountId, dbid));

        return packets;
    }

    private void BroadcastClubSync(long memberDbid)
    {
        var origin = _broadcasts.FindCharacter(memberDbid);
        if (origin is null)
            return;

        var snapshot = _store.GetClubSnapshot(
            origin.AccountId,
            origin.CharacterId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        if (snapshot is null)
            return;

        foreach (var member in snapshot.Members)
        {
            if (member.DbId == memberDbid)
                continue;

            var presence = _broadcasts.FindCharacter(member.DbId);
            if (presence is null)
                continue;

            _broadcasts.PublishToCharacter(
                presence.CharacterId,
                BuildSync(
                    presence.AccountId,
                    presence.CharacterId));
        }
    }
}
