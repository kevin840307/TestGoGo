using System.Text.Json;
using SGO.Server.Application.Messaging;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.World;

/// <summary>
/// Companion entities are separate client entities but inherit their owner's
/// PlayerSpatialAoi visibility. The hub keeps one current Spawn snapshot so a
/// newly-visible observer never receives Move/State before Spawn.
/// </summary>
internal static class PlayerCompanionAoiSmokeTests
{
    public static void Run()
    {
        var hub = new ServerBroadcastHub();
        var owner = hub.Register();
        var observer = hub.Register();

        hub.UpdatePresence(
            owner.ConnectionId,
            13,
            101,
            101,
            1_000,
            1_000);
        hub.UpdatePresence(
            observer.ConnectionId,
            13,
            202,
            202,
            1_100,
            1_000);

        hub.ReconcilePlayerAoi(
            owner.ConnectionId);
        hub.ReconcilePlayerAoi(
            observer.ConnectionId);

        Check(
            hub.IsPlayerAoiVisible(
                observer.ConnectionId,
                owner.ConnectionId),
            "companion fixture observer sees owner through player AOI");

        const long petEntityId =
            4_123_456;
        var spawn = new
        {
            Type = "SpawnEntity",
            EntityID = petEntityId,
            EntityType = 4,
            Name = "AOIPet",
            LocX = 1_040.0,
            LocY = 1_020.0,
            ExtraData = new
            {
                OwnerID = 101L,
                Sprite = "EN010A"
            }
        };

        Check(
            hub.TrackPlayerCompanionPacket(
                owner.ConnectionId,
                spawn),
            "first companion spawn creates owner snapshot");

        Check(
            !hub.TrackPlayerCompanionPacket(
                owner.ConnectionId,
                spawn),
            "repeated companion spawn refreshes snapshot without duplicate identity");

        var initial =
            hub.PlayerCompanionSnapshots(
                owner.ConnectionId);
        Check(
            initial.Count == 1 &&
            PacketLong(
                initial[0],
                "EntityID") ==
                petEntityId &&
            Math.Abs(
                PacketDouble(
                    initial[0],
                    "LocX") -
                1_040.0) < 0.01,
            "companion AOI snapshot preserves full spawn identity and position");

        hub.PublishToPlayerObservers(
            owner.ConnectionId,
            new
            {
                Type = "MoveEntity",
                EntityID = petEntityId,
                X = 1_180.0,
                Y = 1_060.0,
                Dir = "1",
                SpeedTime = 0.25
            });

        Check(
            observer.Queue.TryDequeue(
                out var move) &&
            PacketString(
                move,
                "Type") ==
                "MoveEntity",
            "visible observer receives companion movement through owner AOI");

        var moved =
            hub.PlayerCompanionSnapshots(
                owner.ConnectionId);
        Check(
            moved.Count == 1 &&
            Math.Abs(
                PacketDouble(
                    moved[0],
                    "LocX") -
                1_180.0) < 0.01 &&
            Math.Abs(
                PacketDouble(
                    moved[0],
                    "LocY") -
                1_060.0) < 0.01,
            "companion snapshot follows latest authoritative movement");

        hub.PublishToPlayerObservers(
            owner.ConnectionId,
            new Dictionary<string, object>
            {
                ["Type"] = "MoveEntity",
                ["EntityID"] = petEntityId,
                ["X"] = 1_240.0,
                ["Y"] = 1_080.0,
                ["Dir"] = "2",
                ["SpeedTime"] = 0.25
            });

        Check(
            observer.Queue.TryDequeue(out _),
            "dictionary-shaped companion packet still reaches visible observer");

        var dictionaryMoved =
            hub.PlayerCompanionSnapshots(
                owner.ConnectionId);
        Check(
            dictionaryMoved.Count == 1 &&
            Math.Abs(
                PacketDouble(
                    dictionaryMoved[0],
                    "LocX") -
                1_240.0) < 0.01 &&
            Math.Abs(
                PacketDouble(
                    dictionaryMoved[0],
                    "LocY") -
                1_080.0) < 0.01,
            "dictionary-shaped companion movement refreshes AOI snapshot");

        hub.PublishToPlayerObservers(
            owner.ConnectionId,
            new
            {
                Type = "DespawnEntity",
                EntityID = petEntityId
            });

        Check(
            observer.Queue.TryDequeue(
                out var despawn) &&
            PacketString(
                despawn,
                "Type") ==
                "DespawnEntity",
            "visible observer receives companion despawn");

        Check(
            hub.PlayerCompanionSnapshots(
                owner.ConnectionId).Count == 0 &&
            hub.PlayerCompanionEntityIds(
                owner.ConnectionId).Count == 0,
            "companion despawn removes snapshot so future AOI enter cannot resurrect pet");

        hub.TrackPlayerCompanionPacket(
            owner.ConnectionId,
            spawn);
        hub.ClearPlayerCompanions(
            owner.ConnectionId);

        Check(
            hub.PlayerCompanionSnapshots(
                owner.ConnectionId).Count == 0,
            "map/disconnect cleanup clears all owner companion snapshots");
    }

    private static string? PacketString(
        object packet,
        string property)
    {
        using var doc =
            JsonDocument.Parse(
                JsonSerializer.Serialize(
                    packet));

        return doc.RootElement
            .TryGetProperty(
                property,
                out var value)
            ? value.GetString()
            : null;
    }

    private static long PacketLong(
        object packet,
        string property)
    {
        using var doc =
            JsonDocument.Parse(
                JsonSerializer.Serialize(
                    packet));

        return doc.RootElement
            .TryGetProperty(
                property,
                out var value)
            ? value.GetInt64()
            : 0;
    }

    private static double PacketDouble(
        object packet,
        string property)
    {
        using var doc =
            JsonDocument.Parse(
                JsonSerializer.Serialize(
                    packet));

        return doc.RootElement
            .TryGetProperty(
                property,
                out var value)
            ? value.GetDouble()
            : 0;
    }
}
