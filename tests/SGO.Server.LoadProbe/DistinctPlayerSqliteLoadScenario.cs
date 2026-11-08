using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using SGO.Server.Infrastructure.Persistence.Sqlite;

internal static class DistinctPlayerSqliteLoadScenario
{
    private sealed record Player(long AccountId, long CharacterId, int Index);

    public static async Task<object> Measure(int playerCount, int operationsPerPlayer)
    {
        var temp = Path.Combine(
            Path.GetTempPath(),
            "sgo-multiplayer-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            var store = new SqliteGameStore(Path.Combine(temp, "game.db"));
            var players = SeedPlayers(store, playerCount);
            var latencies = new ConcurrentBag<double>();
            var failures = 0;
            var started = Stopwatch.GetTimestamp();

            var workers = players.Select(player => Task.Run(() =>
            {
                for (var op = 0; op < operationsPerPlayer; op++)
                {
                    var x = 8_000 + player.Index * 20 + op;
                    var y = 11_000 + player.Index * 20 + op;
                    var opStarted = Stopwatch.GetTimestamp();
                    var ok = store.UpdatePosition(
                        player.AccountId,
                        player.CharacterId,
                        x,
                        y);
                    latencies.Add(
                        Stopwatch.GetElapsedTime(opStarted).TotalMilliseconds);
                    if (!ok)
                        Interlocked.Increment(ref failures);
                }
            })).ToArray();

            await Task.WhenAll(workers);
            var elapsed = Stopwatch.GetElapsedTime(started);
            var samples = latencies.ToArray();
            var operations = playerCount * operationsPerPlayer;
            var finalStateFailures = 0;

            foreach (var player in players)
            {
                var character = store.GetCharacter(
                    player.AccountId,
                    player.CharacterId);
                var expectedX =
                    8_000 + player.Index * 20 + operationsPerPlayer - 1;
                var expectedY =
                    11_000 + player.Index * 20 + operationsPerPlayer - 1;

                if (character is null ||
                    Convert.ToInt32(Convert.ToDouble(character["LocX"])) != expectedX ||
                    Convert.ToInt32(Convert.ToDouble(character["LocY"])) != expectedY)
                {
                    finalStateFailures++;
                }
            }

            if (failures != 0 ||
                finalStateFailures != 0 ||
                samples.Length != operations)
            {
                throw new InvalidOperationException(
                    $"distinct-player SQLite probe failed players={playerCount}: " +
                    $"operations={operations} samples={samples.Length} " +
                    $"writeFailures={failures} finalStateFailures={finalStateFailures}");
            }

            return new
            {
                Players = playerCount,
                OperationsPerPlayer = operationsPerPlayer,
                Operations = operations,
                WriteFailures = failures,
                FinalStateFailures = finalStateFailures,
                ElapsedMs = elapsed.TotalMilliseconds,
                OperationsPerSecond =
                    elapsed.TotalSeconds <= 0
                        ? 0
                        : operations / elapsed.TotalSeconds,
                LatencyMs = Summary(samples)
            };
        }
        finally
        {
            try
            {
                if (Directory.Exists(temp))
                    Directory.Delete(temp, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static Player[] SeedPlayers(SqliteGameStore store, int playerCount)
    {
        var players = new List<Player>(playerCount);
        const string password = "load-test-password";
        var run = Guid.NewGuid().ToString("N")[..8];

        for (var accountIndex = 0;
             players.Count < playerCount;
             accountIndex++)
        {
            var username = $"load-{run}-{accountIndex:D3}";
            var registered = store.Register(username, password, "");
            if (!registered.Success)
            {
                throw new InvalidOperationException(
                    "distinct-player load registration failed: " +
                    registered.Message);
            }

            var accountId = store.Login(username, password)
                ?? throw new InvalidOperationException(
                    "distinct-player load login failed");

            for (var slot = 0;
                 slot < 3 && players.Count < playerCount;
                 slot++)
            {
                var playerIndex = players.Count;
                var name = $"LP{playerIndex:D3}{run[..4]}";
                using var create = JsonDocument.Parse(
                    JsonSerializer.Serialize(new
                    {
                        Name = name,
                        Password = "1234",
                        Race = 1,
                        Hair = 1,
                        HairColor = 1,
                        Str = 5,
                        Dex = 5,
                        Con = 5,
                        Wis = 5
                    }));

                var created = store.Create(accountId, create.RootElement);
                if (!created.Success)
                {
                    throw new InvalidOperationException(
                        "distinct-player load character creation failed: " +
                        created.Message);
                }

                var characterId = store.Characters(accountId)
                    .Where(x =>
                        x.TryGetValue("NAME", out var value) &&
                        value?.ToString() == name)
                    .Select(x => Convert.ToInt64(x["DBID"]))
                    .Single();

                players.Add(new Player(accountId, characterId, playerIndex));
            }
        }

        return players.ToArray();
    }

    private static object Summary(double[] values)
    {
        Array.Sort(values);
        if (values.Length == 0)
        {
            return new
            {
                Count = 0,
                Min = 0.0,
                P50 = 0.0,
                P95 = 0.0,
                P99 = 0.0,
                Max = 0.0,
                Average = 0.0
            };
        }

        return new
        {
            Count = values.Length,
            Min = values[0],
            P50 = Percentile(values, 0.50),
            P95 = Percentile(values, 0.95),
            P99 = Percentile(values, 0.99),
            Max = values[^1],
            Average = values.Average()
        };
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        var index =
            (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
