using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

// Measured in the same monotonic process clock for ALL real encrypted bot
// WebSocket clients. Matching submitted (CharacterId, X, Y) to a DIFFERENT
// bot's received MoveEntity deliberately needs no protocol changes.
// Coalesced/blocked/off-AOI moves remain UNMATCHED, never fabricated as 0ms.
internal sealed record BotPeerMoveLatencyReport(
    long SentPositions,
    long MatchedPositions,
    long PeerObservations,
    long UnmatchedPositions,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs,
    string MeasurementMethod);

internal sealed class BotPeerMoveLatencyTracker
{
    private readonly record struct Key(long SenderId, int X, int Y);
    private sealed class Pending(long since)
    {
        public long StartedAt { get; } = since;
        public ConcurrentDictionary<long, byte> SeenBy { get; } = new();
        public int Matched;
    }

    private static readonly double[] BoundsMs =
    [
        5, 10, 20, 40, 60, 80, 100, 150, 200, 300,
        500, 750, 1000, 1500, 2000, 3000, 5000, 10000
    ];
    private const int MaximumTrackedPositions = 8192;
    private static readonly TimeSpan MaximumMatchAge =
        TimeSpan.FromSeconds(10);
    private readonly ConcurrentDictionary<Key, Pending> _pending = new();
    private readonly long[] _histogram = new long[BoundsMs.Length + 1];
    private long _sentPositions;
    private long _matchedPositions;
    private long _peerObservations;
    private long _maxLatencyMicros;

    public bool RecordSent(long senderId, int x, int y)
    {
        if (senderId <= 0)
            return false;
        var now = Stopwatch.GetTimestamp();
        if (_pending.Count >= MaximumTrackedPositions)
            Prune(now);
        var accepted = _pending.TryAdd(
            new Key(senderId, x, y),
            new Pending(now));
        if (accepted)
            Interlocked.Increment(ref _sentPositions);
        return accepted;
    }

    public void CancelSent(long senderId, int x, int y)
    {
        if (_pending.TryRemove(new Key(senderId, x, y), out _))
            Interlocked.Decrement(ref _sentPositions);
    }

    public void ObservePeerMove(long observerCharacterId, JsonElement packet)
    {
        if (observerCharacterId <= 0 ||
            !packet.TryGetProperty("EntityID", out var entity) ||
            !entity.TryGetInt64(out var senderId) ||
            senderId <= 0 ||
            senderId == observerCharacterId ||
            !packet.TryGetProperty("X", out var x) ||
            !packet.TryGetProperty("Y", out var y) ||
            x.ValueKind != JsonValueKind.Number ||
            y.ValueKind != JsonValueKind.Number)
            return;

        var mx = x.GetDouble();
        var my = y.GetDouble();
        if (!double.IsFinite(mx) || !double.IsFinite(my) ||
            mx < int.MinValue || mx > int.MaxValue ||
            my < int.MinValue || my > int.MaxValue)
            return;

        var key = new Key(senderId, (int)Math.Round(mx),
            (int)Math.Round(my));
        if (!_pending.TryGetValue(key, out var pending))
            return;
        var elapsed = Stopwatch.GetElapsedTime(pending.StartedAt);
        if (elapsed < TimeSpan.Zero ||
            elapsed > MaximumMatchAge ||
            !pending.SeenBy.TryAdd(observerCharacterId, 0))
            return;

        if (Interlocked.Exchange(ref pending.Matched, 1) == 0)
            Interlocked.Increment(ref _matchedPositions);

        var latencyMs = elapsed.TotalMilliseconds;
        var bucket = 0;
        while (bucket < BoundsMs.Length && latencyMs > BoundsMs[bucket])
            bucket++;
        Interlocked.Increment(ref _histogram[bucket]);
        Interlocked.Increment(ref _peerObservations);
        var micros = (long)Math.Ceiling(latencyMs * 1000.0);
        var previous = Volatile.Read(ref _maxLatencyMicros);
        while (micros > previous)
        {
            var replaced = Interlocked.CompareExchange(
                ref _maxLatencyMicros, micros, previous);
            if (replaced == previous)
                break;
            previous = replaced;
        }
    }

    private void Prune(long now)
    {
        foreach (var (key, pending) in _pending)
        {
            if (Stopwatch.GetElapsedTime(pending.StartedAt, now) >
                MaximumMatchAge)
                _pending.TryRemove(key, out _);
        }
        if (_pending.Count < MaximumTrackedPositions)
            return;
        // Memory bound for very long running --seconds 0 load bots.
        foreach (var key in _pending.Keys
                     .Take(_pending.Count - MaximumTrackedPositions / 2))
            _pending.TryRemove(key, out _);
    }

    public BotPeerMoveLatencyReport Snapshot()
    {
        var sent = Interlocked.Read(ref _sentPositions);
        var matched = Interlocked.Read(ref _matchedPositions);
        var samples = Interlocked.Read(ref _peerObservations);
        var hist = new long[_histogram.Length];
        for (var i = 0; i < hist.Length; i++)
            hist[i] = Interlocked.Read(ref _histogram[i]);
        var max = Interlocked.Read(ref _maxLatencyMicros) / 1000.0;
        double Quantile(double percentile)
        {
            if (samples == 0)
                return 0;
            var rank = Math.Max(1L,
                (long)Math.Ceiling(samples * percentile));
            long total = 0;
            for (var i = 0; i < hist.Length; i++)
            {
                total += hist[i];
                if (total < rank)
                    continue;
                return Math.Round(Math.Min(max,
                    i < BoundsMs.Length ? BoundsMs[i] : max), 3);
            }
            return Math.Round(max, 3);
        }

        return new BotPeerMoveLatencyReport(
            sent,
            matched,
            samples,
            Math.Max(0, sent - matched),
            Quantile(0.5),
            Quantile(0.95),
            Quantile(0.99),
            Math.Round(max, 3),
            "sender SyncPosition send-start -> DIFFERENT bot encrypted MoveEntity receive/decode; " +
            "matched by source CharacterId and integer X/Y, per observer; " +
            "P50/P95/P99 bucket-upper-bound estimates; unmatched includes " +
            "coalesced, rejected and out-of-AOI movement");
    }

    public static void SelfTest()
    {
        var tracker = new BotPeerMoveLatencyTracker();
        if (!tracker.RecordSent(10, 123, 456) ||
            tracker.RecordSent(10, 123, 456))
            throw new InvalidOperationException(
                "movement tracker must accept one uniquely keyed movement");
        using var packet = JsonDocument.Parse(
            """{"Type":"MoveEntity","EntityID":10,"X":123,"Y":456}""");
        tracker.ObservePeerMove(10, packet.RootElement);
        tracker.ObservePeerMove(20, packet.RootElement);
        tracker.ObservePeerMove(20, packet.RootElement);
        tracker.ObservePeerMove(30, packet.RootElement);
        var result = tracker.Snapshot();
        if (result.SentPositions != 1 ||
            result.MatchedPositions != 1 ||
            result.PeerObservations != 2 ||
            result.UnmatchedPositions != 0 ||
            result.P50Ms > result.P95Ms ||
            result.P95Ms > result.P99Ms ||
            result.MaxMs < 0)
            throw new InvalidOperationException(
                "matched peer movement must exclude self, duplicates and invalid percentiles");

        using var unknown = JsonDocument.Parse(
            """{"Type":"MoveEntity","EntityID":11,"X":123,"Y":456}""");
        tracker.ObservePeerMove(30, unknown.RootElement);
        if (tracker.Snapshot().PeerObservations != 2)
            throw new InvalidOperationException(
                "foreign movement without send timestamp must not count");
        Console.WriteLine(
            "[PEER_MOVE_LATENCY_PASS] send-receive keys, cross-peer deduplication, bounded histogram");
    }
}
