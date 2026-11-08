using System.Diagnostics;
using SGO.Server.Application.Messaging;

/// <summary>
/// In-process PlayerSpatialAoi load benchmark with MOVING coordinates.
/// Contrasts dense and spatially distributed populations on the same map.
/// This does NOT exercise WebSocket, SQLite, collision maps or game rendering.
/// </summary>
internal static class PlayerAoiMovingLoadScenario
{
    public static object Measure(int playerCount, int steps, bool dense)
    {
        if (playerCount < 2 || steps <= 0)
            throw new ArgumentOutOfRangeException(nameof(playerCount));

        var aoi = new PlayerSpatialAoi();
        var players = new OnlinePlayerPresence[playerCount];
        const int mapId = 15;
        // Two special players start outside AOI and repeatedly cross its
        // enter/exit boundaries. They are far from all other test groups.
        // Their jumps intentionally emulate teleport/fast movement; other
        // players take small repeated steps each update.
        for (var i = 0; i < playerCount; i++)
        {
            double x;
            double y;
            if (i == 0)
            {
                x = 500;
                y = 500;
            }
            else if (i == 1)
            {
                x = 3_000;
                y = 500;
            }
            else
            {
                var index = i - 2;
                var group = dense ? 0 : index / 20;
                var member = dense ? index : index % 20;
                var columns = dense ? 32 : 10;
                x = 8_000 + group * 6_000 + member % columns * 24;
                y = 11_000 + member / columns * 24;
            }

            players[i] = new OnlinePlayerPresence(
                Guid.NewGuid(), i + 1L, i + 1L, mapId, x, y,
                "AOI_" + i);
            aoi.Upsert(players[i]);
        }

        var initialPeerCount = players.Sum(
            p => (long)aoi.ObserversOf(p.ConnectionId).Count);
        var totalUpdates = checked((long)playerCount * steps);
        var allMapDeliveries = checked(
            totalUpdates * (playerCount - 1L));
        var samples = new double[checked(playerCount * steps)];
        var sampleIndex = 0;
        long deliveryCount = 0;
        long enters = 0;
        long exits = 0;
        var boundaryFailures = 0;
        var movementFailures = 0;
        var memoryBefore = GC.GetAllocatedBytesForCurrentThread();
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var runStart = Stopwatch.GetTimestamp();

        for (var step = 0; step < steps; step++)
        {
            for (var i = 0; i < players.Length; i++)
            {
                var old = players[i];
                // For regular players, alternate +/- 8 px about the
                // original coordinate: each request has a real delta.
                // Boundary peer alternates between 1500 and 2500 distance.
                var nextX = i == 0
                    ? old.X
                    : i == 1
                        ? (step % 2 == 0 ? 2_000 : 3_000)
                        : old.X + (step % 2 == 0 ? 8 : -8);
                var nextY = i == 0 ? old.Y + (step % 2 == 0 ? 4 : -4) : old.Y;
                if (nextX == old.X && nextY == old.Y)
                    movementFailures++;
                var updated = old with { X = nextX, Y = nextY };
                var updateStart = Stopwatch.GetTimestamp();
                var changes = aoi.Upsert(updated);
                samples[sampleIndex++] =
                    Stopwatch.GetElapsedTime(updateStart).TotalMilliseconds;
                enters += changes.Count(change => change.Appeared);
                exits += changes.Count(change => !change.Appeared);
                players[i] = updated;
                // Exactly how many recipients a real AOI-gated MoveEntity
                // would reach; no payload serialization in this benchmark.
                deliveryCount += aoi.ObserversOf(updated.ConnectionId).Count;
            }
            var boundaryVisible = aoi.IsVisible(
                players[0].ConnectionId, players[1].ConnectionId);
            if (boundaryVisible != (step % 2 == 0) ||
                boundaryVisible != aoi.IsVisible(
                    players[1].ConnectionId, players[0].ConnectionId))
                boundaryFailures++;
        }

        var elapsed = Stopwatch.GetElapsedTime(runStart);
        var cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        var allocation = GC.GetAllocatedBytesForCurrentThread() - memoryBefore;
        Array.Sort(samples);
        var finalPeerCount = players.Sum(
            p => (long)aoi.ObserversOf(p.ConnectionId).Count);
        var percentAvoided = allMapDeliveries == 0 ? 0 :
            (1.0 - (double)deliveryCount / allMapDeliveries) * 100;

        // One map; no duplicate/unknown entity IDs in the same observer set.
        var ready = aoi.ReadyPlayerCount == playerCount;
        var passed = ready && boundaryFailures == 0 &&
            movementFailures == 0 && enters >= steps - 1 &&
            exits >= steps - 1;
        if (!passed)
            throw new InvalidOperationException(
                $"moving AOI failed players={playerCount} dense={dense} " +
                $"ready={ready} boundaryFailures={boundaryFailures} " +
                $"stationaryUpdates={movementFailures} enters={enters} exits={exits}");

        return new
        {
            Players = playerCount,
            MapId = mapId,
            Layout = dense ? "dense" : "groups-of-20",
            StepsPerPlayer = steps,
            Updates = totalUpdates,
            InitiallyKnownDirectedPeers = initialPeerCount,
            FinallyKnownDirectedPeers = finalPeerCount,
            WouldDeliverWithoutAoi = allMapDeliveries,
            AoiMoveDeliveries = deliveryCount,
            AvoidedDeliveryPercent = percentAvoided,
            EnterNotifications = enters,
            ExitNotifications = exits,
            BoundaryFailures = boundaryFailures,
            ElapsedMs = elapsed.TotalMilliseconds,
            CpuMs = cpuMs,
            AllocatedBytes = allocation,
            UpdatesPerSecond = totalUpdates / Math.Max(0.001, elapsed.TotalSeconds),
            UpdateLatencyMs = new
            {
                P50 = At(samples, 0.5),
                P95 = At(samples, 0.95),
                P99 = At(samples, 0.99),
                Max = samples[^1]
            },
            Success = true,
            Notes = "In-process synthetic moving AOI only; not live WebSocket, DB, client rendering or collision-valid moves."
        };
    }

    private static double At(double[] values, double rank)
        => values[Math.Clamp(
            (int)Math.Ceiling(rank * values.Length) - 1,
            0, values.Length - 1)];
}
