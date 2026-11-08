using System.Text.Json;
using SGO.Server.Application.Diagnostics;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class ServerDiagnosticsSmokeTests
{
    public static void Run()
    {
        var diagnostics = new ServerDiagnostics();

        diagnostics.PacketHandled();
        diagnostics.PacketError();
        diagnostics.SlowPacket();
        diagnostics.WebSocketConnected();
        diagnostics.WebSocketDisconnected();
        diagnostics.MapAiSlowTick();
        diagnostics.MapPressureWarning();
        diagnostics.BroadcastBacklogWarning();
        diagnostics.TradeConflict();
        diagnostics.PickupConflict();
        diagnostics.DatabaseFailure();
        diagnostics.WorldTickFailure();
        diagnostics.ResourceWarning();

        using var counters = JsonDocument.Parse(
            JsonSerializer.Serialize(diagnostics.SnapshotCounters()));

        var root = counters.RootElement;
        Check(root.GetProperty("packets").GetInt64() == 1 &&
              root.GetProperty("packetErrors").GetInt64() == 1 &&
              root.GetProperty("slowPackets").GetInt64() == 1 &&
              root.GetProperty("webSocketConnects").GetInt64() == 1 &&
              root.GetProperty("webSocketDisconnects").GetInt64() == 1 &&
              root.GetProperty("mapAiSlowTicks").GetInt64() == 1 &&
              root.GetProperty("mapPressureWarnings").GetInt64() == 1 &&
              root.GetProperty("broadcastBacklogWarnings").GetInt64() == 1 &&
              root.GetProperty("tradeConflicts").GetInt64() == 1 &&
              root.GetProperty("pickupConflicts").GetInt64() == 1 &&
              root.GetProperty("databaseFailures").GetInt64() == 1 &&
              root.GetProperty("worldTickFailures").GetInt64() == 1 &&
              root.GetProperty("resourceWarnings").GetInt64() == 1,
            "server diagnostics exposes every operational counter");

        for (var i = 0; i < 510; i++)
        {
            diagnostics.Record(
                i % 2 == 0 ? "Warning" : "Information",
                "Smoke",
                $"event-{i}",
                elapsedMilliseconds: i);
        }

        var recent = diagnostics.Recent(500);
        Check(
            recent.Count == 500 &&
            recent[0].Message == "event-509" &&
            recent[^1].Message == "event-10",
            "diagnostic ring buffer stays bounded and returns newest events first");

        Check(
            diagnostics.Recent(5).Select(x => x.Message).SequenceEqual(
                new[]
                {
                    "event-509",
                    "event-508",
                    "event-507",
                    "event-506",
                    "event-505"
                }),
            "diagnostic recent count preserves newest-first ordering");
    }
}
