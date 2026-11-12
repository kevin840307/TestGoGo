using System.Diagnostics;
using System.Threading;

namespace SGO.Server.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Cumulative, approximate latency distribution. Percentiles use bounded
/// histogram bucket upper bounds, not interpolated request measurements.
/// Recording never opens SQLite, writes logs, or blocks a gameplay lock.
/// </summary>
public sealed record SqliteLatencyDistribution(
    long Samples, double P50Ms, double P95Ms, double P99Ms, double MaxMs);

public sealed record SqliteHotWriteGateTiming(
    SqliteLatencyDistribution WaitMs,
    SqliteLatencyDistribution HeldMs);

public sealed record SqliteHotWriteDiagnostics(
    SqliteHotWriteGateTiming Damage,
    SqliteHotWriteGateTiming PetDamage,
    SqliteHotWriteGateTiming PositionBatch,
    SqliteHotWriteGateTiming Recovery,
    SqliteLatencyDistribution DamageOpenMs,
    SqliteLatencyDistribution DamageUpdateMs,
    SqliteLatencyDistribution DamageReadMs);

internal enum SqliteHotWriteOperation
{
    Damage,
    PetDamage,
    PositionBatch,
    Recovery
}

internal sealed class SqliteHotWriteTelemetry
{
    private readonly Histogram[] _gateWait =
        Enumerable.Range(0, 4).Select(_ => new Histogram()).ToArray();
    private readonly Histogram[] _gateHeld =
        Enumerable.Range(0, 4).Select(_ => new Histogram()).ToArray();
    private readonly Histogram _damageOpen = new();
    private readonly Histogram _damageUpdate = new();
    private readonly Histogram _damageRead = new();

    public void RecordGate(
        SqliteHotWriteOperation operation, double waitMs, double heldMs)
    {
        var index = (int)operation;
        _gateWait[index].Record(waitMs);
        _gateHeld[index].Record(heldMs);
    }

    public void RecordDamageOpen(double milliseconds)
        => _damageOpen.Record(milliseconds);

    public void RecordDamageUpdate(double milliseconds)
        => _damageUpdate.Record(milliseconds);

    public void RecordDamageRead(double milliseconds)
        => _damageRead.Record(milliseconds);

    // /health observers may call this at any cadence; unlike a "drain",
    // Snapshot does not reset counters or influence other observers.
    public SqliteHotWriteDiagnostics Snapshot() => new(
        Gate(SqliteHotWriteOperation.Damage),
        Gate(SqliteHotWriteOperation.PetDamage),
        Gate(SqliteHotWriteOperation.PositionBatch),
        Gate(SqliteHotWriteOperation.Recovery),
        _damageOpen.Snapshot(),
        _damageUpdate.Snapshot(),
        _damageRead.Snapshot());

    private SqliteHotWriteGateTiming Gate(SqliteHotWriteOperation operation)
    {
        var index = (int)operation;
        return new(_gateWait[index].Snapshot(), _gateHeld[index].Snapshot());
    }

    private sealed class Histogram
    {
        private static readonly double[] BoundsMs =
        [
            0.1, 0.25, 0.5, 1, 2, 5, 10, 25, 50,
            100, 250, 500, 1000, 2000, 5000, 10000, 30000
        ];

        private readonly long[] _buckets = new long[BoundsMs.Length + 1];
        private long _maxMicroseconds;

        public void Record(double milliseconds)
        {
            if (!double.IsFinite(milliseconds))
                return;

            var elapsedMs = Math.Max(0, milliseconds);
            var micros = (long)Math.Min(long.MaxValue,
                Math.Ceiling(elapsedMs * 1000.0));
            var previous = Interlocked.Read(ref _maxMicroseconds);
            while (micros > previous)
            {
                var replaced = Interlocked.CompareExchange(
                    ref _maxMicroseconds, micros, previous);
                if (replaced == previous)
                    break;
                previous = replaced;
            }

            var bucket = 0;
            while (bucket < BoundsMs.Length && elapsedMs > BoundsMs[bucket])
                bucket++;
            Interlocked.Increment(ref _buckets[bucket]);
        }

        public SqliteLatencyDistribution Snapshot()
        {
            var counts = new long[_buckets.Length];
            var count = 0L;
            for (var i = 0; i < counts.Length; i++)
            {
                counts[i] = Interlocked.Read(ref _buckets[i]);
                count += counts[i];
            }
            if (count == 0)
                return new(0, 0, 0, 0, 0);

            var maxMs = Interlocked.Read(ref _maxMicroseconds) / 1000.0;
            return new(
                count,
                Percentile(counts, count, 0.50, maxMs),
                Percentile(counts, count, 0.95, maxMs),
                Percentile(counts, count, 0.99, maxMs),
                Math.Round(maxMs, 3));
        }

        private static double Percentile(
            long[] histogram, long count, double percentile, double maxMs)
        {
            var rank = Math.Max(1, (long)Math.Ceiling(count * percentile));
            long cumulative = 0;
            for (var i = 0; i < histogram.Length; i++)
            {
                cumulative += histogram[i];
                if (cumulative < rank)
                    continue;
                return Math.Round(Math.Min(
                    maxMs,
                    i < BoundsMs.Length ? BoundsMs[i] : maxMs), 3);
            }
            return Math.Round(maxMs, 3);
        }
    }
}
