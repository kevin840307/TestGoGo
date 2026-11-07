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
    private readonly WorldDataService _world;
    private readonly ConcurrentDictionary<int, SharedMapRuntime> _maps = new();

    public WorldRuntimeService(WorldDataService world)
    {
        _world = world;
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

        return runtime;
    }
}
