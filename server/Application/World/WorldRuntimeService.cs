using System.Collections.Concurrent;
using SGO.Server.Domain.World;
using SGO.Server.Infrastructure.WorldData;

namespace SGO.Server.Application.World;

public sealed class SharedMapRuntime
{
    public SharedMapRuntime(int mapId)
    {
        MapId = mapId;
    }

    public int MapId { get; }
    public object SyncRoot { get; } = new();
    public Dictionary<int, MonsterRuntimeState> Monsters { get; } = new();
}

public sealed class WorldRuntimeService
{
    private const int NiciaPlateauMapId = 13;
    private const double SupplementalSpawnRadius = 120.0;

    private readonly WorldDataService _world;
    private readonly MonsterNavigationService _navigation;
    private readonly ConcurrentDictionary<int, SharedMapRuntime> _maps = new();

    public WorldRuntimeService(
        WorldDataService world,
        BlockingMapService blocking)
    {
        _world = world;
        _navigation = new MonsterNavigationService(blocking);
    }

    public SharedMapRuntime GetMap(int mapId)
        => _maps.GetOrAdd(mapId, CreateMap);

    private SharedMapRuntime CreateMap(int mapId)
    {
        var runtime = new SharedMapRuntime(mapId);

        if (_world.IsCityMap(mapId))
            return runtime;

        foreach (var enemy in _world.GetEnemies(mapId))
        {
            if (enemy.Level >= 65535)
                continue;

            var monster = new MonsterRuntimeState
            {
                EntityId = enemy.EntityId,
                EnemyId = enemy.EnemyId,
                MapId = enemy.MapId,
                Name = enemy.Name,
                Sprite = enemy.Sprite,
                Level = Math.Max(1, enemy.Level),
                MaxHp = Math.Max(1, enemy.MaxHp),
                CurrentHp = Math.Max(1, enemy.MaxHp),
                SpawnX = enemy.X,
                SpawnY = enemy.Y,
                X = enemy.X,
                Y = enemy.Y,
                WanderCenterX = enemy.X,
                WanderCenterY = enemy.Y
            };

            runtime.Monsters[monster.EntityId] = monster;
        }

        if (mapId == NiciaPlateauMapId &&
            runtime.Monsters.Count > 0)
        {
            AddNiciaSupplementalMonsters(runtime);
        }

        return runtime;
    }

    private void AddNiciaSupplementalMonsters(
        SharedMapRuntime runtime)
    {
        var sources = runtime.Monsters.Values
            .OrderBy(x => x.EntityId)
            .ToArray();

        var extraCount = Math.Clamp(
            sources.Length,
            20,
            80);

        for (var index = 0; index < extraCount; index++)
        {
            var source = sources[index % sources.Length];
            var (x, y) = ResolveSupplementalSpawn(
                source,
                index);

            var entityId =
                8_000_000 +
                runtime.MapId * 10_000 +
                index;

            while (runtime.Monsters.ContainsKey(entityId))
                entityId++;

            runtime.Monsters[entityId] =
                new MonsterRuntimeState
                {
                    EntityId = entityId,
                    EnemyId = source.EnemyId,
                    MapId = source.MapId,
                    Name = source.Name,
                    Sprite = source.Sprite,
                    Level = source.Level,
                    MaxHp = source.MaxHp,
                    CurrentHp = source.MaxHp,
                    SpawnX = x,
                    SpawnY = y,
                    X = x,
                    Y = y,
                    WanderCenterX = x,
                    WanderCenterY = y
                };
        }
    }

    private (double X, double Y) ResolveSupplementalSpawn(
        MonsterRuntimeState source,
        int sequence)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var seed = unchecked(
                source.EntityId * 1103515245 +
                (sequence + 1) * 12345 +
                attempt * 265443576);
            var angle =
                ((uint)seed % 360) *
                Math.PI / 180.0;
            var radiusSeed = unchecked(
                seed * 214013 + 2531011);
            var radius =
                28.0 +
                ((uint)radiusSeed % 10_001) /
                10_000.0 *
                (SupplementalSpawnRadius - 28.0);

            var x =
                source.SpawnX +
                Math.Cos(angle) * radius;
            var y =
                source.SpawnY +
                Math.Sin(angle) * radius;

            if (_navigation.IsDirectClear(
                    source.MapId,
                    source.SpawnX,
                    source.SpawnY,
                    x,
                    y))
            {
                return (x, y);
            }
        }

        return (source.SpawnX, source.SpawnY);
    }
}
