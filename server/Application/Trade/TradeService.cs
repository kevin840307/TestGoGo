using System.Text.Json;
using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Diagnostics;
using SGO.Server.Application.Messaging;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Protocol;

namespace SGO.Server.Application.Trade;

public sealed class TradeService
{
    private static readonly TimeSpan InviteLifetime =
        TimeSpan.FromSeconds(30);

    private sealed record PendingInvite(
        long RequesterAccountId,
        long RequesterDbid,
        string RequesterName,
        long TargetAccountId,
        DateTimeOffset ExpiresAt);

    private sealed class Participant
    {
        public required long AccountId { get; init; }
        public required long CharacterId { get; init; }
        public required string Name { get; init; }
        public long Money { get; set; }
        public bool Locked { get; set; }
        public bool Confirmed { get; set; }
        public List<Offer> Offers { get; } = new();
    }

    private sealed class Offer
    {
        public required TradeAssetOffer Persisted { get; set; }
        public required Dictionary<string, object> View { get; set; }
    }

    private sealed class TradeRuntime
    {
        public required string SessionId { get; init; }
        public required Participant First { get; init; }
        public required Participant Second { get; init; }
        public bool CommitStarted { get; set; }

        public Participant? Find(long characterId)
            => First.CharacterId == characterId
                ? First
                : Second.CharacterId == characterId
                    ? Second
                    : null;

        public Participant Other(long characterId)
            => First.CharacterId == characterId
                ? Second
                : First;
    }

    private readonly object _sync = new();
    private readonly IGameStore _store;
    private readonly CurrentClientDataCatalog _current;
    private readonly ServerBroadcastHub _broadcasts;
    private readonly ServerDiagnostics? _diagnostics;
    private readonly Dictionary<long, PendingInvite> _invites = new();
    private readonly Dictionary<string, TradeRuntime> _sessions =
        new(StringComparer.Ordinal);
    private readonly Dictionary<long, string> _byCharacter = new();

    public TradeService(
        IGameStore store,
        CurrentClientDataCatalog current,
        ServerBroadcastHub broadcasts,
        ServerDiagnostics? diagnostics = null)
    {
        _store = store;
        _current = current;
        _broadcasts = broadcasts;
        _diagnostics = diagnostics;
    }

    public IReadOnlyList<object> Request(
        ClientSession session,
        long accountId,
        long targetDbid,
        DateTimeOffset now)
    {
        if (!session.CharacterId.HasValue ||
            targetDbid <= 0 ||
            targetDbid == session.CharacterId.Value)
        {
            return [Flash("交易目標無效")];
        }

        var selfDbid = session.CharacterId.Value;
        var target = _broadcasts.FindCharacter(targetDbid);

        if (target is null ||
            target.MapId != session.CurrentMapId)
        {
            return [Flash("目標玩家不在附近或已離線")];
        }

        var self = _store.GetCharacter(
            accountId,
            selfDbid);

        if (self is null)
            return [];

        lock (_sync)
        {
            CleanupInvitesLocked(now);

            if (_byCharacter.ContainsKey(selfDbid) ||
                _byCharacter.ContainsKey(targetDbid))
            {
                RecordConflict(
                    "Request",
                    selfDbid,
                    targetDbid,
                    "participant already trading");
                return [Flash("其中一方正在交易中")];
            }

            _invites[targetDbid] = new PendingInvite(
                accountId,
                selfDbid,
                self["NAME"]?.ToString() ??
                    $"角色{selfDbid}",
                target.AccountId,
                now + InviteLifetime);
        }

        if (!_broadcasts.PublishToCharacter(
                targetDbid,
                new
                {
                    Type = "TradeInvite",
                    RequesterDBID = selfDbid,
                    RequesterName =
                        self["NAME"]?.ToString() ??
                        $"角色{selfDbid}"
                }))
        {
            lock (_sync)
                _invites.Remove(targetDbid);

            return [Flash("目標玩家已離線")];
        }

        return [Flash("交易邀請已送出")];
    }

    public IReadOnlyList<object> Reply(
        ClientSession session,
        long accountId,
        long requesterDbid,
        bool accept,
        DateTimeOffset now)
    {
        if (!session.CharacterId.HasValue)
            return [];

        var selfDbid = session.CharacterId.Value;
        PendingInvite invite;

        lock (_sync)
        {
            CleanupInvitesLocked(now);

            if (!_invites.TryGetValue(
                    selfDbid,
                    out var foundInvite) ||
                foundInvite.RequesterDbid != requesterDbid)
            {
                return [Flash("交易邀請已失效")];
            }

            invite = foundInvite;
            _invites.Remove(selfDbid);
        }

        if (!accept)
        {
            _broadcasts.PublishToCharacter(
                requesterDbid,
                new
                {
                    Type = "TradeCancel",
                    Message = "對方拒絕了交易邀請"
                });
            return [];
        }

        if (_broadcasts.FindCharacter(
                requesterDbid) is null)
        {
            return [Flash("邀請者已離線")];
        }

        var targetCharacter = _store.GetCharacter(
            accountId,
            selfDbid);
        var requesterCharacter = _store.GetCharacter(
            invite.RequesterAccountId,
            requesterDbid);

        if (targetCharacter is null ||
            requesterCharacter is null)
        {
            return [Flash("交易角色資料不存在")];
        }

        TradeRuntime trade;
        lock (_sync)
        {
            if (_byCharacter.ContainsKey(selfDbid) ||
                _byCharacter.ContainsKey(requesterDbid))
            {
                RecordConflict(
                    "Reply",
                    selfDbid,
                    requesterDbid,
                    "participant already trading");
                return [Flash("其中一方正在交易中")];
            }

            trade = new TradeRuntime
            {
                SessionId = Guid.NewGuid().ToString("N"),
                First = new Participant
                {
                    AccountId = invite.RequesterAccountId,
                    CharacterId = requesterDbid,
                    Name =
                        requesterCharacter["NAME"]?.ToString() ??
                        $"角色{requesterDbid}"
                },
                Second = new Participant
                {
                    AccountId = accountId,
                    CharacterId = selfDbid,
                    Name =
                        targetCharacter["NAME"]?.ToString() ??
                        $"角色{selfDbid}"
                }
            };

            _sessions[trade.SessionId] = trade;
            _byCharacter[requesterDbid] =
                trade.SessionId;
            _byCharacter[selfDbid] =
                trade.SessionId;
        }

        PublishBoth(
            trade,
            new
            {
                Type = "TradeStart",
                SessionID = trade.SessionId,
                TargetName1 = trade.First.Name,
                TargetName2 = trade.Second.Name
            });

        PublishSync(trade);
        return [];
    }

    public IReadOnlyList<object> Action(
        ClientSession session,
        string action,
        JsonElement request)
    {
        if (!session.CharacterId.HasValue)
            return [];

        var selfDbid = session.CharacterId.Value;
        TradeRuntime trade;
        Participant self;
        Participant other;

        lock (_sync)
        {
            if (!_byCharacter.TryGetValue(
                    selfDbid,
                    out var sessionId) ||
                !_sessions.TryGetValue(
                    sessionId,
                    out var foundTrade))
            {
                return [Flash("目前沒有進行中的交易")];
            }

            trade = foundTrade;
            self = trade.Find(selfDbid)!;
            other = trade.Other(selfDbid);
        }

        switch (action)
        {
            case "TradeAddItem":
                return AddItem(
                    trade,
                    self,
                    other,
                    request);

            case "TradeRemoveItem":
                return RemoveItem(
                    trade,
                    self,
                    other,
                    request);

            case "TradeSetMoney":
                return SetMoney(
                    trade,
                    self,
                    other,
                    request.Long("Money"));

            case "TradeLock":
                return Lock(
                    trade,
                    self,
                    other);

            case "TradeFinalConfirm":
                return Confirm(
                    trade,
                    self,
                    other);

            case "TradeCancel":
                Cancel(
                    trade,
                    "交易已取消");
                return [];

            default:
                return [Flash(
                    $"交易操作尚未支援：{action}")];
        }
    }

    public void Disconnect(long characterId)
    {
        TradeRuntime? trade = null;

        lock (_sync)
        {
            _invites.Remove(characterId);

            var inviteTargets = _invites
                .Where(x =>
                    x.Value.RequesterDbid ==
                    characterId)
                .Select(x => x.Key)
                .ToArray();

            foreach (var target in inviteTargets)
                _invites.Remove(target);

            if (_byCharacter.TryGetValue(
                    characterId,
                    out var sessionId))
            {
                _sessions.TryGetValue(
                    sessionId,
                    out trade);
            }
        }

        if (trade is not null)
            Cancel(
                trade,
                "對方已離線，交易取消");
    }

    private IReadOnlyList<object> AddItem(
        TradeRuntime trade,
        Participant self,
        Participant other,
        JsonElement request)
    {
        lock (_sync)
        {
            if (!IsActiveLocked(trade))
                return [Flash("交易已結束")];

            if (self.Locked)
                return [Flash("已鎖定交易，不能再修改物品")];

            var tabType = request.Int("TabType", -1);
            var uid = request.Long("UID");
            var requestedCount = Math.Max(
                1,
                request.Int("Count", 1));

            var resolved = ResolveOffer(
                self,
                tabType,
                uid,
                requestedCount,
                out var error);

            if (resolved is null)
                return [Flash(error)];

            if (tabType == 0)
            {
                var existing = self.Offers.FirstOrDefault(x =>
                    x.Persisted.TabType == 0 &&
                    x.Persisted.Uid == uid);

                if (existing is not null)
                {
                    var total = checked(
                        existing.Persisted.Count +
                        requestedCount);

                    resolved = ResolveOffer(
                        self,
                        tabType,
                        uid,
                        total,
                        out error);

                    if (resolved is null)
                        return [Flash(error)];

                    existing.Persisted =
                        resolved.Persisted;
                    existing.View =
                        resolved.View;
                }
                else
                {
                    self.Offers.Add(resolved);
                }
            }
            else
            {
                if (self.Offers.Any(x =>
                        x.Persisted.TabType == tabType &&
                        x.Persisted.Uid == uid))
                {
                    return [Flash("此物品已放入交易")];
                }

                self.Offers.Add(resolved);
            }

            ResetLocks(trade);
        }

        PublishSync(trade);
        return [];
    }

    private IReadOnlyList<object> RemoveItem(
        TradeRuntime trade,
        Participant self,
        Participant other,
        JsonElement request)
    {
        lock (_sync)
        {
            if (!IsActiveLocked(trade))
                return [Flash("交易已結束")];

            if (self.Locked)
                return [Flash("已鎖定交易，不能再修改物品")];

            var tabType = request.Int("TabType", -1);
            var uid = request.Long("UID");

            self.Offers.RemoveAll(x =>
                x.Persisted.TabType == tabType &&
                x.Persisted.Uid == uid);

            ResetLocks(trade);
        }

        PublishSync(trade);
        return [];
    }

    private IReadOnlyList<object> SetMoney(
        TradeRuntime trade,
        Participant self,
        Participant other,
        long money)
    {
        if (money < 0 ||
            money > 9_999_999_999L)
        {
            return [Flash("交易金額無效")];
        }

        var character = _store.GetCharacter(
            self.AccountId,
            self.CharacterId);

        if (character is null)
            return [Flash("角色資料不存在")];

        if (money >
            Convert.ToInt64(character["Money"]))
        {
            return [Flash("交易金額超過目前持有金錢")];
        }

        lock (_sync)
        {
            if (!IsActiveLocked(trade))
                return [Flash("交易已結束")];

            if (self.Locked)
                return [Flash("已鎖定交易，不能再修改金額")];

            self.Money = money;
            ResetLocks(trade);
        }

        PublishSync(trade);
        return [];
    }

    private IReadOnlyList<object> Lock(
        TradeRuntime trade,
        Participant self,
        Participant other)
    {
        var bothLocked = false;

        lock (_sync)
        {
            if (!IsActiveLocked(trade))
                return [Flash("交易已結束")];

            // Repeated Lock is idempotent. It must not clear a previously
            // received FinalConfirm, and it cannot change a committing trade.
            if (trade.CommitStarted || self.Locked)
                return [];

            self.Locked = true;
            self.Confirmed = false;
            bothLocked =
                self.Locked &&
                other.Locked;
        }

        PublishSync(trade);

        if (bothLocked)
        {
            PublishBoth(
                trade,
                new
                {
                    Type = "TradeFinalConfirmReq"
                });
        }

        return [];
    }

    private IReadOnlyList<object> Confirm(
        TradeRuntime trade,
        Participant self,
        Participant other)
    {
        lock (_sync)
        {
            if (!IsActiveLocked(trade))
                return [Flash("交易已結束")];

            if (!self.Locked ||
                !other.Locked)
            {
                return [Flash("雙方尚未鎖定交易")];
            }

            self.Confirmed = true;
            if (!self.Confirmed ||
                !other.Confirmed)
                return [];

            // FinalConfirm may arrive more than once or concurrently from a
            // client. Exactly one caller owns the durable commit.
            if (trade.CommitStarted)
            {
                RecordConflict(
                    "FinalConfirm",
                    self.CharacterId,
                    other.CharacterId,
                    "duplicate concurrent commit attempt");
                return [];
            }

            trade.CommitStarted = true;
        }

        var result = _store.CommitTrade(
            trade.First.AccountId,
            trade.First.CharacterId,
            trade.First.Money,
            trade.First.Offers
                .Select(x => x.Persisted)
                .ToArray(),
            trade.Second.AccountId,
            trade.Second.CharacterId,
            trade.Second.Money,
            trade.Second.Offers
                .Select(x => x.Persisted)
                .ToArray());

        if (!result.Success)
        {
            _diagnostics?.DatabaseFailure();
            _diagnostics?.Record(
                "Warning",
                "TradeCommit",
                $"Session={trade.SessionId} Failed={result.Message} First={trade.First.CharacterId} Second={trade.Second.CharacterId}");
            Cancel(
                trade,
                result.Message,
                force: true);
            return [];
        }

        lock (_sync)
            RemoveTradeLocked(trade);

        PublishTradeAssets(
            trade.First,
            result.FirstMoney);
        PublishTradeAssets(
            trade.Second,
            result.SecondMoney);

        PublishBoth(
            trade,
            new
            {
                Type = "TradeSuccess",
                Message = "交易完成"
            });

        return [];
    }

    private Offer? ResolveOffer(
        Participant participant,
        int tabType,
        long uid,
        int count,
        out string error)
    {
        error = "";
        var assets = _store.GetCharacterAssets(
            participant.AccountId,
            participant.CharacterId);

        switch (tabType)
        {
            case 0:
            {
                var item = assets.Inventory.FirstOrDefault(x =>
                    Convert.ToInt64(x["ItemUID"]) == uid &&
                    Convert.ToInt32(x["InBank"]) == 0 &&
                    Convert.ToInt32(x["InShop"]) == 0);

                if (item is null ||
                    count >
                    Convert.ToInt32(item["Count"]))
                {
                    error = "物品數量不足";
                    return null;
                }

                var itemIndex =
                    Convert.ToInt32(item["ItemIndex"]);

                if (!CanTradeItem(itemIndex))
                {
                    error = "此物品無法交易";
                    return null;
                }

                return new Offer
                {
                    Persisted = new TradeAssetOffer(
                        0,
                        uid,
                        itemIndex,
                        count),
                    View = new Dictionary<string, object>
                    {
                        ["TabType"] = 0,
                        ["UID"] = uid,
                        ["ItemIndex"] = itemIndex,
                        ["Count"] = count
                    }
                };
            }

            case 1:
            {
                var item = assets.Equips.FirstOrDefault(x =>
                    Convert.ToInt64(x["ItemID"]) == uid &&
                    Convert.ToInt32(x["Part"]) == 255 &&
                    Convert.ToInt32(x["InBank"]) == 0 &&
                    Convert.ToInt32(x["InShop"]) == 0);

                if (item is null)
                {
                    error = "裝備需卸下後才能交易";
                    return null;
                }

                var itemIndex =
                    Convert.ToInt32(item["ItemIdx"]);

                if (!CanTradeItem(itemIndex))
                {
                    error = "此裝備無法交易";
                    return null;
                }

                var view =
                    new Dictionary<string, object>(item)
                    {
                        ["TabType"] = 1,
                        ["UID"] = uid,
                        ["ItemIndex"] = itemIndex,
                        ["Count"] = 1
                    };

                return new Offer
                {
                    Persisted = new TradeAssetOffer(
                        1,
                        uid,
                        itemIndex,
                        1),
                    View = view
                };
            }

            case 2:
            {
                var pet = assets.Pets.FirstOrDefault(x =>
                    Convert.ToInt64(x["UID"]) == uid &&
                    Convert.ToInt32(x["State"]) == 0 &&
                    Convert.ToInt32(x["InShop"]) == 0);

                if (pet is null ||
                    Convert.ToInt64(pet["Equip0"]) > 0 ||
                    Convert.ToInt64(pet["Equip1"]) > 0 ||
                    Convert.ToInt64(pet["Equip2"]) > 0)
                {
                    error = "寵物需收回並卸下裝備後才能交易";
                    return null;
                }

                var petId =
                    Convert.ToInt32(pet["PETID"]);

                return new Offer
                {
                    Persisted = new TradeAssetOffer(
                        2,
                        uid,
                        petId,
                        1),
                    View = new Dictionary<string, object>
                    {
                        ["TabType"] = 2,
                        ["UID"] = uid,
                        ["ItemIndex"] = petId,
                        ["Count"] = 1,
                        ["ExtraData"] =
                            new Dictionary<string, object>(pet)
                    }
                };
            }

            case 3:
            {
                var item = assets.HorseEquips.FirstOrDefault(x =>
                    Convert.ToInt64(x["EquipUID"]) == uid &&
                    Convert.ToInt64(x["HorseUID"]) == -1 &&
                    Convert.ToInt32(x["InBank"]) == 0 &&
                    Convert.ToInt32(x["InShop"]) == 0);

                if (item is null)
                {
                    error = "機甲裝備需卸下後才能交易";
                    return null;
                }

                var itemIndex =
                    Convert.ToInt32(item["EquipIdx"]);

                if (!CanTradeItem(itemIndex))
                {
                    error = "此機甲裝備無法交易";
                    return null;
                }

                var view =
                    new Dictionary<string, object>(item)
                    {
                        ["TabType"] = 3,
                        ["UID"] = uid,
                        ["ItemIndex"] = itemIndex,
                        ["Count"] = 1
                    };

                return new Offer
                {
                    Persisted = new TradeAssetOffer(
                        3,
                        uid,
                        itemIndex,
                        1),
                    View = view
                };
            }

            default:
                error = "不支援的交易物品類型";
                return null;
        }
    }

    private bool CanTradeItem(int itemIndex)
    {
        var item = _current.FindItem(itemIndex);
        return item is not null &&
               item.CanTrade == 1;
    }

    private void ResetLocks(
        TradeRuntime trade)
    {
        trade.First.Locked = false;
        trade.First.Confirmed = false;
        trade.Second.Locked = false;
        trade.Second.Confirmed = false;
        trade.CommitStarted = false;
    }

    private void PublishSync(
        TradeRuntime trade)
    {
        PublishSyncFor(
            trade,
            trade.First);
        PublishSyncFor(
            trade,
            trade.Second);
    }

    private void PublishSyncFor(
        TradeRuntime trade,
        Participant self)
    {
        var other = trade.Other(
            self.CharacterId);

        _broadcasts.PublishToCharacter(
            self.CharacterId,
            new
            {
                Type = "TradeSync",
                MyMoney = self.Money,
                OtherMoney = other.Money,
                MyItems = self.Offers
                    .Select(x => x.View)
                    .ToArray(),
                OtherItems = other.Offers
                    .Select(x => x.View)
                    .ToArray(),
                MyLocked = self.Locked,
                OtherLocked = other.Locked
            });
    }

    private void PublishTradeAssets(
        Participant participant,
        long money)
    {
        var assets = _store.GetCharacterAssets(
            participant.AccountId,
            participant.CharacterId);

        _broadcasts.PublishToCharacter(
            participant.CharacterId,
            new
            {
                Type = "SyncMoney",
                Money = money
            });
        _broadcasts.PublishToCharacter(
            participant.CharacterId,
            new
            {
                Type = "InventorySync",
                Items = assets.Inventory,
                MaxSize = assets.InventoryMaxSize
            });
        _broadcasts.PublishToCharacter(
            participant.CharacterId,
            new
            {
                Type = "EquipSync",
                Equips = assets.Equips,
                MaxSize = assets.EquipMaxSize
            });
        _broadcasts.PublishToCharacter(
            participant.CharacterId,
            new
            {
                Type = "PetSync",
                Pets = assets.Pets,
                MaxSize = assets.PetMaxSize
            });
        _broadcasts.PublishToCharacter(
            participant.CharacterId,
            new
            {
                Type = "HorseSync",
                Horses = assets.Horses,
                HorseEquips = assets.HorseEquips,
                MaxHorseEquipSize =
                    assets.HorseEquipMaxSize
            });
    }

    private void Cancel(
        TradeRuntime trade,
        string message,
        bool force = false)
    {
        lock (_sync)
        {
            // A completed trade may already have been removed from the
            // registry. Repeated cancel/disconnect must not emit a second
            // terminal event. Once a durable commit starts, ordinary
            // cancellation cannot undo it.
            if (!IsActiveLocked(trade) ||
                (trade.CommitStarted && !force))
                return;

            RemoveTradeLocked(trade);
        }

        PublishBoth(
            trade,
            new
            {
                Type = "TradeCancel",
                Message = message
            });
    }

    private bool IsActiveLocked(TradeRuntime trade)
        => _sessions.TryGetValue(
               trade.SessionId, out var active) &&
           ReferenceEquals(active, trade);

    private void RemoveTradeLocked(
        TradeRuntime trade)
    {
        _sessions.Remove(
            trade.SessionId);
        _byCharacter.Remove(
            trade.First.CharacterId);
        _byCharacter.Remove(
            trade.Second.CharacterId);
    }

    private void PublishBoth(
        TradeRuntime trade,
        object packet)
    {
        _broadcasts.PublishToCharacter(
            trade.First.CharacterId,
            packet);
        _broadcasts.PublishToCharacter(
            trade.Second.CharacterId,
            packet);
    }

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

    private void RecordConflict(
        string action,
        long firstCharacterId,
        long secondCharacterId,
        string reason)
    {
        _diagnostics?.TradeConflict();
        _diagnostics?.Record(
            "Warning",
            "TradeConflict",
            $"Action={action} First={firstCharacterId} Second={secondCharacterId} Reason={reason}");
    }

    private static object Flash(string message)
        => new
        {
            Type = "SystemMessageFlash",
            Message = message
        };
}
