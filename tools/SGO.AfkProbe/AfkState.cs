using System.Text.Json;

namespace Sgo.AfkProbe;

internal sealed record MonsterSnapshot(long Id, double X, double Y, int Hp, string State);
internal sealed record GroundSnapshot(long Id, double X, double Y);

/// <summary>
/// Only data; no Godot objects, textures, scenes, sprites, GPU or rendering.
/// The server, never this state, owns combat decisions and authoritative HP.
/// </summary>
internal sealed class AfkState
{
    private const int MaxSnapshots = 20000;
    private readonly Dictionary<long, MonsterSnapshot> _monsters = new();
    private readonly Dictionary<long, GroundSnapshot> _groundItems = new();
    private readonly Dictionary<string, long> _types = new(StringComparer.Ordinal);

    internal int MapId { get; private set; }
    internal long CharacterId { get; private set; }
    internal double X { get; private set; }
    internal double Y { get; private set; }
    internal bool AutoSaintEnabled { get; private set; }
    internal long Packets { get; private set; }
    internal long DamageEvents { get; private set; }
    internal long DeathEvents { get; private set; }
    internal long RejectedEntities { get; private set; }
    internal int Monsters => _monsters.Count;
    internal int GroundItems => _groundItems.Count;
    internal long Count(string packetType) => _types.GetValueOrDefault(packetType);

    internal void Apply(JsonElement packet)
    {
        var type = ReadString(packet, "Type");
        if (type.Length == 0) return;
        Packets++;
        _types[type] = Count(type) + 1;

        long id = ReadLong(packet, "EntityID");
        switch (type)
        {
            case "EnterWorldSuccess":
                CharacterId = ReadLong(packet, "DBID", CharacterId);
                SetMap(ReadInt(packet, "MapID", MapId));
                X = ReadDouble(packet, "LocX", X);
                Y = ReadDouble(packet, "LocY", Y);
                break;
            case "ChangeMap":
                SetMap(ReadInt(packet, "MapID", MapId));
                X = ReadDouble(packet, "X", X);
                Y = ReadDouble(packet, "Y", Y);
                break;
            case "PositionCorrection":
                X = ReadDouble(packet, "X", X);
                Y = ReadDouble(packet, "Y", Y);
                break;
            case "ToggleAutoSaintResponse":
                AutoSaintEnabled = ReadBool(packet, "IsOn");
                break;
            case "SpawnEntity":
                if (id == 0 || ReadInt(packet, "EntityType") != 1)
                    break;
                if (!_monsters.ContainsKey(id) && _monsters.Count >= MaxSnapshots)
                {
                    RejectedEntities++;
                    break;
                }
                var data = ReadObject(packet, "ExtraData");
                _monsters[id] = new MonsterSnapshot(
                    id, ReadDouble(packet, "X"), ReadDouble(packet, "Y"),
                    ReadInt(data, "CurrentHP"), ReadString(packet, "State"));
                break;
            case "MoveEntity":
                if (_monsters.TryGetValue(id, out var moving))
                    _monsters[id] = moving with
                    {
                        X = ReadDouble(packet, "X", moving.X),
                        Y = ReadDouble(packet, "Y", moving.Y)
                    };
                break;
            case "SyncState":
                if (_monsters.TryGetValue(id, out var changing))
                    _monsters[id] = changing with
                    {
                        State = ReadString(packet, "State", changing.State)
                    };
                break;
            case "EntityDamage":
                DamageEvents++;
                var targetId = ReadLong(packet, "TargetID", id);
                if (_monsters.TryGetValue(targetId, out var damaged))
                    _monsters[targetId] = damaged with
                    {
                        Hp = ReadInt(packet, "CurrentHP", damaged.Hp)
                    };
                break;
            case "EntityDeath":
                DeathEvents++;
                _monsters.Remove(id);
                break;
            case "DespawnEntity":
            case "RemoveEntity":
                _monsters.Remove(id);
                _groundItems.Remove(id);
                break;
            case "SpawnGroundItem":
            case "GroundItemSpawn":
                if (id == 0) break;
                if (!_groundItems.ContainsKey(id) && _groundItems.Count >= MaxSnapshots)
                {
                    RejectedEntities++;
                    break;
                }
                _groundItems[id] = new GroundSnapshot(
                    id, ReadDouble(packet, "X"), ReadDouble(packet, "Y"));
                break;
            case "RemoveGroundItem":
            case "GroundItemRemoved":
                _groundItems.Remove(id);
                break;
        }
    }

    private void SetMap(int map)
    {
        if (map == MapId) return;
        MapId = map;
        _monsters.Clear();
        _groundItems.Clear();
    }

    internal static JsonElement ReadObject(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty(name, out var e) &&
           e.ValueKind == JsonValueKind.Object ? e : default;

    internal static string ReadString(
        JsonElement root, string name, string fallback = "")
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty(name, out var e) &&
           e.ValueKind == JsonValueKind.String
           ? e.GetString() ?? fallback : fallback;

    internal static int ReadInt(JsonElement root, string name, int fallback = 0)
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty(name, out var e) &&
           e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v)
           ? v : fallback;

    internal static long ReadLong(JsonElement root, string name, long fallback = 0)
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty(name, out var e) &&
           e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var v)
           ? v : fallback;

    internal static double ReadDouble(JsonElement root, string name, double fallback = 0)
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty(name, out var e) &&
           e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var v)
           ? v : fallback;

    internal static bool ReadBool(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty(name, out var e) &&
           e.ValueKind == JsonValueKind.True;
}
