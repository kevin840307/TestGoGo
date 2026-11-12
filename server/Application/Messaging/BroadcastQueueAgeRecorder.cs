using System.Diagnostics;
using System.Threading;

namespace SGO.Server.Application.Messaging;

/// <summary>
/// Time spent inside one WebSocket broadcast lane BEFORE dequeuing for send.
/// This excludes sendGate wait, encoding, socket write and client processing.
/// Samples are per delivered queue slot, not per coalesced historical MoveEntity.
/// </summary>
public sealed record BroadcastQueueAgeLane(
    long Samples, double P50Ms, double P95Ms, double P99Ms, double MaxMs);

public sealed record BroadcastQueueAgeReport(
    BroadcastQueueAgeLane Player,
    BroadcastQueueAgeLane World);

internal sealed class BroadcastQueueAgeRecorder
{
    // Bounded memory and zero allocations on the hot sampling path.
    // Quantiles are bucket upper bounds, not exact individual measurements.
    private static readonly double[] BucketBoundsMs =
        [5, 10, 25, 50, 100, 200, 350, 500, 1000, 2000, 5000, 10000];

    private readonly Lane _player = new();
    private readonly Lane _world = new();

    public void RecordPlayer(long queuedAt)
        => _player.Record(queuedAt);

    public void RecordWorld(long queuedAt)
        => _world.Record(queuedAt);

    public BroadcastQueueAgeReport Drain()
        => new(_player.Drain(), _world.Drain());

    // Cumulative read-only snapshot for /health. Separate from the
    // WebSocket sender's 10s Drain(), so repeated health polls neither
    // double-count nor erase per-connection diagnostic intervals.
    public BroadcastQueueAgeReport Snapshot()
        => new(_player.Snapshot(), _world.Snapshot());

    private sealed class Lane
    {
        private readonly long[] _counts = new long[BucketBoundsMs.Length + 1];
        private long _maxAgeUs;

        public void Record(long queuedAt)
        {
            if (queuedAt <= 0)
                return;

            var ageMs = Math.Max(0.0,
                Stopwatch.GetElapsedTime(queuedAt).TotalMilliseconds);
            var bucket = 0;
            while (bucket < BucketBoundsMs.Length &&
                   ageMs > BucketBoundsMs[bucket])
                bucket++;
            Interlocked.Increment(ref _counts[bucket]);

            var micros = (long)Math.Ceiling(ageMs * 1000.0);
            var observed = Volatile.Read(ref _maxAgeUs);
            while (micros > observed)
            {
                var previous = Interlocked.CompareExchange(
                    ref _maxAgeUs, micros, observed);
                if (previous == observed)
                    break;
                observed = previous;
            }
        }

        public BroadcastQueueAgeLane Snapshot()
        {
            var samples = 0L;
            var histogram = new long[_counts.Length];
            for (var i = 0; i < histogram.Length; i++)
            {
                histogram[i] = Interlocked.Read(ref _counts[i]);
                samples += histogram[i];
            }
            var maxAgeMs = Interlocked.Read(ref _maxAgeUs) / 1000.0;
            if (samples == 0)
                return new BroadcastQueueAgeLane(0, 0, 0, 0, 0);

            return new BroadcastQueueAgeLane(
                samples,
                Percentile(histogram, samples, 0.50, maxAgeMs),
                Percentile(histogram, samples, 0.95, maxAgeMs),
                Percentile(histogram, samples, 0.99, maxAgeMs),
                Math.Round(maxAgeMs, 2));
        }

        public BroadcastQueueAgeLane Drain()
        {
            var samples = 0L;
            var histogram = new long[_counts.Length];
            for (var i = 0; i < histogram.Length; i++)
            {
                histogram[i] = Interlocked.Exchange(ref _counts[i], 0);
                samples += histogram[i];
            }

            var maxAgeMs = Interlocked.Exchange(ref _maxAgeUs, 0) / 1000.0;
            if (samples == 0)
                return new BroadcastQueueAgeLane(0, 0, 0, 0, 0);

            return new BroadcastQueueAgeLane(
                samples,
                Percentile(histogram, samples, 0.50, maxAgeMs),
                Percentile(histogram, samples, 0.95, maxAgeMs),
                Percentile(histogram, samples, 0.99, maxAgeMs),
                Math.Round(maxAgeMs, 2));
        }

        private static double Percentile(
            long[] histogram, long samples, double fraction, double maxAgeMs)
        {
            var rank = Math.Max(1L, (long)Math.Ceiling(samples * fraction));
            var count = 0L;
            for (var i = 0; i < histogram.Length; i++)
            {
                count += histogram[i];
                if (count < rank)
                    continue;
                var upper = i < BucketBoundsMs.Length
                    ? BucketBoundsMs[i]
                    : maxAgeMs;
                return Math.Round(Math.Min(maxAgeMs, upper), 2);
            }
            return Math.Round(maxAgeMs, 2);
        }
    }
}
