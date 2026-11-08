using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.Trade;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

/// <summary>
/// Deterministically pause the durable trade commit after the in-memory
/// single-owner flag is set. A cancel/disconnect at that point must not
/// publish TradeCancel followed by TradeSuccess.
/// </summary>
internal static class TradeTerminalRaceSmokeTests
{
    public static void Run(
        string repoRoot,
        SqliteGameStore store,
        IReadOnlyList<long> accounts,
        IReadOnlyList<long> characters)
    {
        const int itemIndex = 4103;
        var firstBefore = Count(store, accounts[0], characters[0]);
        var secondBefore = Count(store, accounts[1], characters[1]);
        var uid = store.AddInventoryItem(
            accounts[0], characters[0], itemIndex, 1);
        Check(uid > 0, "seed item for deterministic trade cancel/commit race");

        using var blocker = new BlockingTradeCommitProxyState();
        var proxy = DispatchProxy.Create<IGameStore, BlockingTradeCommitProxy>();
        var interception = (BlockingTradeCommitProxy)(object)proxy;
        interception.Configure(store, blocker);

        var hub = new ServerBroadcastHub();
        var first = hub.Register();
        var second = hub.Register();
        hub.UpdatePresence(
            first.ConnectionId, 15, characters[0], accounts[0], 1000, 1000);
        hub.UpdatePresence(
            second.ConnectionId, 15, characters[1], accounts[1], 1010, 1000);

        var firstSession = new ClientSession
        {
            ConnectionId = first.ConnectionId,
            AccountId = accounts[0],
            CharacterId = characters[0],
            CurrentMapId = 15
        };
        var secondSession = new ClientSession
        {
            ConnectionId = second.ConnectionId,
            AccountId = accounts[1],
            CharacterId = characters[1],
            CurrentMapId = 15
        };

        var trade = new TradeService(
            proxy,
            new CurrentClientDataCatalog(Path.Combine(repoRoot, "client")),
            hub);

        trade.Request(
            firstSession, accounts[0], characters[1], DateTimeOffset.UtcNow);
        trade.Reply(
            secondSession, accounts[1], characters[0], true, DateTimeOffset.UtcNow);

        using (var add = Json(JsonSerializer.Serialize(new
        {
            TabType = 0,
            UID = uid,
            Count = 1
        })))
        {
            trade.Action(firstSession, "TradeAddItem", add.RootElement);
        }

        using (var empty = Json("{}"))
        {
            trade.Action(firstSession, "TradeLock", empty.RootElement);
            trade.Action(secondSession, "TradeLock", empty.RootElement);
            trade.Action(firstSession, "TradeFinalConfirm", empty.RootElement);
        }

        while (first.Queue.TryDequeue(out _)) { }
        while (second.Queue.TryDequeue(out _)) { }

        var commitTask = Task.Run(() =>
        {
            using var empty = Json("{}");
            trade.Action(secondSession, "TradeFinalConfirm", empty.RootElement);
        });

        try
        {
            Check(
                blocker.CommitEntered.Wait(TimeSpan.FromSeconds(10)),
                "final confirm reached durable commit after marking trade as committing");

            using (var cancel = Json("{}"))
                trade.Action(firstSession, "TradeCancel", cancel.RootElement);
            trade.Disconnect(characters[1]);

            Check(
                !first.Queue.Any(x => JsonSerializer.Serialize(x)
                    .Contains("\"Type\":\"TradeCancel\"")) &&
                !second.Queue.Any(x => JsonSerializer.Serialize(x)
                    .Contains("\"Type\":\"TradeCancel\"")),
                "cancel and disconnect during commit do not emit a false TradeCancel");
        }
        finally
        {
            blocker.AllowCommit.Set();
        }

        Check(
            commitTask.Wait(TimeSpan.FromSeconds(15)),
            "durable trade commit completes after cancellation race");

        var firstPackets = first.Queue.Select(x => JsonSerializer.Serialize(x)).ToArray();
        var secondPackets = second.Queue.Select(x => JsonSerializer.Serialize(x)).ToArray();

        Check(
            firstPackets.Count(x => x.Contains("\"Type\":\"TradeSuccess\"")) == 1 &&
            secondPackets.Count(x => x.Contains("\"Type\":\"TradeSuccess\"")) == 1 &&
            !firstPackets.Any(x => x.Contains("\"Type\":\"TradeCancel\"")) &&
            !secondPackets.Any(x => x.Contains("\"Type\":\"TradeCancel\"")),
            "commit/cancel race emits exactly one successful terminal event and no cancel");

        Check(
            Count(store, accounts[0], characters[0]) == firstBefore &&
            Count(store, accounts[1], characters[1]) == secondBefore + 1,
            "cancel during commit cannot roll back or duplicate the already committed item");

        using (var cancel = Json("{}"))
            trade.Action(firstSession, "TradeCancel", cancel.RootElement);

        Check(
            !first.Queue.Any(x => JsonSerializer.Serialize(x)
                .Contains("\"Type\":\"TradeCancel\"")) &&
            !second.Queue.Any(x => JsonSerializer.Serialize(x)
                .Contains("\"Type\":\"TradeCancel\"")),
            "cancel after successful commit also cannot emit contradictory terminal event");

        hub.Unregister(first.ConnectionId);
        hub.Unregister(second.ConnectionId);
    }

    private static int Count(SqliteGameStore store, long accountId, long characterId)
        => store.GetCharacterAssets(accountId, characterId)
            .Inventory
            .Where(x => Convert.ToInt32(x["ItemIndex"]) == 4103)
            .Sum(x => Convert.ToInt32(x["Count"]));
}

public sealed class BlockingTradeCommitProxyState : IDisposable
{
    public ManualResetEventSlim CommitEntered { get; } = new(false);
    public ManualResetEventSlim AllowCommit { get; } = new(false);

    public void Dispose()
    {
        CommitEntered.Dispose();
        AllowCommit.Dispose();
    }
}

public class BlockingTradeCommitProxy : DispatchProxy
{
    private IGameStore? _inner;
    private BlockingTradeCommitProxyState? _state;

    public void Configure(IGameStore inner, BlockingTradeCommitProxyState state)
    {
        _inner = inner;
        _state = state;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (_inner is null || _state is null || targetMethod is null)
            throw new InvalidOperationException("Store proxy is not configured");

        if (targetMethod.Name == "CommitTrade")
        {
            _state.CommitEntered.Set();
            if (!_state.AllowCommit.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("Trade commit test barrier was never released");
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
