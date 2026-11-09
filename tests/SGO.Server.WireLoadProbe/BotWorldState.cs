using System.Text.Json;

// Small, bounded observation model for a headless bot. All data comes from
// the same encrypted packets that a normal Godot client receives.
internal sealed class BotWorldState
{
    internal sealed record Monster(int EntityId, double X, double Y, int HP);

    private readonly object _sync = new();
    private readonly Dictionary<int, Monster> _monsters = new();
    private const int MaxTrackedMonsters = 2048;
    private long _damageHits;
    private long _skillCasts;
    private long _monsterDeaths;
    private int _hp = -1;
    private int _maxHp = -1;
    private string _lastFlash = "";

    public long DamageHits => Interlocked.Read(ref _damageHits);
    public long SkillCasts => Interlocked.Read(ref _skillCasts);
    public long MonsterDeaths => Interlocked.Read(ref _monsterDeaths);
    public int HP { get { lock (_sync) return _hp; } }
    public int MaxHP { get { lock (_sync) return _maxHp; } }
    public string LastFlash { get { lock (_sync) return _lastFlash; } }

    public Monster? Nearest(int x, int y, double maxRange = 450)
    {
        lock (_sync)
        {
            Monster? best = null;
            var bestSquared = maxRange * maxRange;
            foreach (var monster in _monsters.Values)
            {
                if (monster.HP <= 0) continue;
                var dx = monster.X - x;
                var dy = monster.Y - y;
                var squared = dx * dx + dy * dy;
                if (squared > bestSquared) continue;
                best = monster;
                bestSquared = squared;
            }
            return best;
        }
    }

    public void Observe(JsonElement packet, long characterId)
    {
        if (!packet.TryGetProperty("Type", out var element) ||
            element.ValueKind != JsonValueKind.String)
            return;

        var type = element.GetString();
        var entityId = Number(packet, "EntityID");
        lock (_sync)
        {
            switch (type)
            {
                case "SpawnEntity" when Number(packet, "EntityType") == 1:
                {
                    if (entityId <= 0 || entityId > int.MaxValue) break;
                    if (!packet.TryGetProperty("ExtraData", out var extra) ||
                        extra.ValueKind != JsonValueKind.Object) break;
                    var hp = (int)Number(extra, "CurrentHP",
                        Number(extra, "MaxHP", 1));
                    if (_monsters.Count >= MaxTrackedMonsters &&
                        !_monsters.ContainsKey((int)entityId))
                        break;
                    _monsters[(int)entityId] = new Monster(
                        (int)entityId, Number(packet, "LocX"),
                        Number(packet, "LocY"), hp);
                    break;
                }
                case "MoveEntity":
                    if (entityId > 0 && entityId <= int.MaxValue &&
                        _monsters.TryGetValue((int)entityId, out var moving))
                        _monsters[(int)entityId] = moving with
                        {
                            X = Number(packet, "X", moving.X),
                            Y = Number(packet, "Y", moving.Y)
                        };
                    break;
                case "EntityDamage":
                {
                    var targetId = Number(packet, "TargetID");
                    var targetType = Number(packet, "TargetType");
                    if (targetType == 1)
                    {
                        if (targetId > 0 && targetId <= int.MaxValue &&
                            _monsters.TryGetValue((int)targetId, out var hit))
                            _monsters[(int)targetId] = hit with
                            {
                                HP = (int)Number(packet, "CurrentHP", hit.HP)
                            };
                        if (Number(packet, "AttackerID") == characterId)
                            Interlocked.Increment(ref _damageHits);
                    }
                    else if (targetType == 0 && targetId == characterId)
                    {
                        _hp = (int)Number(packet, "CurrentHP", _hp);
                    }
                    break;
                }
                case "EntityHeal":
                    if (Number(packet, "TargetType") == 0 &&
                        Number(packet, "TargetID") == characterId)
                        _hp = (int)Number(packet, "CurrentHP", _hp);
                    break;
                case "EntityDeath":
                    if (Number(packet, "EntityType") == 1 && entityId > 0 &&
                        entityId <= int.MaxValue)
                    {
                        _monsters.Remove((int)entityId);
                        Interlocked.Increment(ref _monsterDeaths);
                    }
                    else if (entityId == characterId)
                        _hp = 0;
                    break;
                case "DespawnEntity":
                    if (entityId > 0 && entityId <= int.MaxValue)
                        _monsters.Remove((int)entityId);
                    break;
                case "SkillCast":
                    if (Number(packet, "CasterID") == characterId)
                        Interlocked.Increment(ref _skillCasts);
                    break;
                case "PlayerStatusSync":
                    _hp = (int)Number(packet, "HP", _hp);
                    _maxHp = (int)Number(packet, "MaxHP", _maxHp);
                    break;
                case "SystemMessageFlash":
                    if (packet.TryGetProperty("Message", out var msg) &&
                        msg.ValueKind == JsonValueKind.String)
                        _lastFlash = msg.GetString() ?? "";
                    break;
            }
        }
    }

    private static double Number(JsonElement packet, string name,
        double fallback = 0)
        => packet.ValueKind == JsonValueKind.Object &&
           packet.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.Number &&
           element.TryGetDouble(out var value)
            ? value : fallback;
}
