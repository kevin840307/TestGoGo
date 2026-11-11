using SGO.Server.Application.Messaging;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.World;

/// <summary>
/// Dependency-free AOI invariants for dynamic world entities. This suite is
/// deliberately separate from PlayerSpatialAoiSmokeTests because monster and
/// ground-item known sets have different radii and lifecycle semantics.
/// </summary>
internal static class WorldEntitySpatialAoiSmokeTests
{
    private static OnlinePlayerPresence Observer(
        long id,
        int mapId,
        double x,
        double y)
        => new(
            Guid.NewGuid(),
            id,
            id,
            mapId,
            x,
            y);

    private static WorldAoiEntity Entity(
        long id,
        int type,
        int mapId,
        double x,
        double y,
        int revision = 1)
        => new(
            id,
            type,
            mapId,
            x,
            y,
            () => new
            {
                Type = "SpawnEntity",
                EntityID = id,
                EntityType = type,
                Revision = revision,
                LocX = x,
                LocY = y
            });

    public static void Run()
    {
        ObserverAndEntityMovementUseHysteresis();
        ViewportResizeReconcilesStationaryAndMovingMonsters();
        EntityRemovalNeverLeavesGhostKnownSets();
        AuthoritativeMapReconciliationPurgesStaleGround();
        LatestSpawnFactoryWinsForLateObserver();
        SpatialIndexHandlesLargeSparseWorld();
        ConcurrentEntityMovementSettlesSymmetrically();
        BroadcastHubRoutesOnlyKnownWorldObservers();
    }

    private static void ObserverAndEntityMovementUseHysteresis()
    {
        var aoi = new WorldEntitySpatialAoi();
        var viewer = Observer(1, 13, 0, 0);
        var monster = Entity(9_000_001, 1, 13, 9_000, 0);

        Check(
            aoi.UpsertObserver(viewer).Count == 0,
            "world AOI first observer has no dynamic entities");

        Check(
            aoi.UpsertEntity(monster).Count == 0 &&
            !aoi.IsVisible(viewer.ConnectionId, monster.EntityId),
            "world AOI suppresses far monster spawn");

        monster = Entity(
            monster.EntityId,
            1,
            13,
            WorldEntitySpatialAoi.EnterRadius - 1,
            0,
            2);

        var appeared = aoi.UpsertEntity(monster);
        Check(
            appeared.Count == 1 &&
            appeared[0].Appeared &&
            appeared[0].ObserverConnectionId == viewer.ConnectionId &&
            aoi.IsVisible(viewer.ConnectionId, monster.EntityId),
            "world AOI monster entering radius produces one spawn edge");

        monster = Entity(
            monster.EntityId,
            1,
            13,
            WorldEntitySpatialAoi.ExitRadius - 10,
            0,
            3);

        Check(
            aoi.UpsertEntity(monster).Count == 0 &&
            aoi.IsVisible(viewer.ConnectionId, monster.EntityId),
            "world AOI exit hysteresis avoids edge flicker");

        monster = Entity(
            monster.EntityId,
            1,
            13,
            WorldEntitySpatialAoi.ExitRadius + 1,
            0,
            4);

        var disappeared = aoi.UpsertEntity(monster);
        Check(
            disappeared.Count == 1 &&
            !disappeared[0].Appeared &&
            !aoi.IsVisible(viewer.ConnectionId, monster.EntityId),
            "world AOI moving monster outside exit radius despawns once");

        var ground = Entity(6_000_001, 3, 13, 100, 100);
        Check(
            aoi.UpsertEntity(ground).Count == 1 &&
            aoi.IsVisible(viewer.ConnectionId, ground.EntityId),
            "world AOI nearby persisted ground item appears");

        viewer = viewer with
        {
            X = 20_000,
            Y = 20_000
        };

        Check(
            aoi.UpsertObserver(viewer)
                .Any(x =>
                    x.Entity.EntityId == ground.EntityId &&
                    !x.Appeared) &&
            !aoi.IsVisible(viewer.ConnectionId, ground.EntityId),
            "world AOI player moving away despawns known ground item");

        viewer = viewer with
        {
            X = 100,
            Y = 100
        };

        Check(
            aoi.UpsertObserver(viewer)
                .Any(x =>
                    x.Entity.EntityId == ground.EntityId &&
                    x.Appeared) &&
            aoi.IsVisible(viewer.ConnectionId, ground.EntityId),
            "world AOI returning player receives ground item spawn again");

        viewer = viewer with { MapId = 15 };
        Check(
            aoi.UpsertObserver(viewer)
                .Any(x =>
                    x.Entity.EntityId == ground.EntityId &&
                    !x.Appeared),
            "world AOI map change removes old-map entity knowledge");

        Check(
            !aoi.IsVisible(
                viewer.ConnectionId,
                ground.EntityId) &&
            !aoi.ObserversOf(
                ground.EntityId)
                .Contains(viewer.ConnectionId),
            "world AOI map change clears both observer and entity visibility indexes");

        var identityGround = Entity(
            6_000_002,
            3,
            15,
            100,
            100);
        aoi.UpsertEntity(identityGround);
        viewer = viewer with
        {
            X = 100,
            Y = 100
        };
        aoi.UpsertObserver(viewer);

        Check(
            aoi.IsVisible(
                viewer.ConnectionId,
                identityGround.EntityId),
            "world AOI replacement identity starts from a visible entity");

        viewer = viewer with
        {
            CharacterId = viewer.CharacterId + 1000
        };

        var identityChanges =
            aoi.UpsertObserver(viewer);

        Check(
            identityChanges.Count(x =>
                x.Entity.EntityId ==
                    identityGround.EntityId &&
                !x.Appeared) == 1 &&
            identityChanges.Count(x =>
                x.Entity.EntityId ==
                    identityGround.EntityId &&
                x.Appeared) == 1 &&
            aoi.IsVisible(
                viewer.ConnectionId,
                identityGround.EntityId) &&
            aoi.ObserversOf(
                identityGround.EntityId)
                .Count(x =>
                    x == viewer.ConnectionId) == 1,
            "world AOI identity change despawns old knowledge then respawns nearby entity for new identity");
    }

    private static void ViewportResizeReconcilesStationaryAndMovingMonsters()
    {
        var aoi = new WorldEntitySpatialAoi();
        var viewer = Observer(800, 13, 0, 0);
        var edge = Entity(9_999_800, 1, 13, 3400, 0);
        aoi.UpsertObserver(viewer);
        Check(aoi.UpsertEntity(edge).Count == 0,
            "legacy world AOI excludes offscreen monsters");
        var entered = aoi.UpsertObserver(viewer, 3800);
        Check(entered.Count == 1 && entered[0].Appeared &&
              aoi.IsVisible(viewer.ConnectionId, edge.EntityId),
            "stationary viewport expansion spawns edge monster");
        Check(aoi.UpsertObserver(viewer, 3800).Count == 0,
            "unchanged viewport demand avoids duplicate spawns");
        edge = Entity(edge.EntityId, 1, 13, 4100, 0, 2);
        Check(aoi.UpsertEntity(edge).Count == 0 &&
              aoi.IsVisible(viewer.ConnectionId, edge.EntityId),
            "moving monster keeps per-viewer exit hysteresis");
        edge = Entity(edge.EntityId, 1, 13, 4300, 0, 3);
        Check(aoi.UpsertEntity(edge).Count == 1 &&
              !aoi.IsVisible(viewer.ConnectionId, edge.EntityId),
            "moving monster leaves camera-expanded AOI at 4200");
        edge = Entity(edge.EntityId, 1, 13, 3400, 0, 4);
        Check(aoi.UpsertEntity(edge).Count == 1,
            "moving monster reenters camera-expanded AOI");
        var left = aoi.UpsertObserver(viewer, 1800);
        Check(left.Count == 1 && !left[0].Appeared &&
              !aoi.IsVisible(viewer.ConnectionId, edge.EntityId),
            "shrunken camera drops unnecessary outer monster traffic");
        // Viewport >5000 world pixels must still see edge monsters.
        var ultraMonster = Entity(9_999_801, 1, 13, 6200, 0);
        Check(aoi.UpsertEntity(ultraMonster).Count == 0,
            "standard viewport does not stream distant 6200px monster");
        var newOuter = aoi.UpsertObserver(viewer, 6500);
        Check(newOuter.Any(x => x.Appeared && x.Entity.EntityId == ultraMonster.EntityId) &&
              aoi.IsVisible(viewer.ConnectionId, ultraMonster.EntityId),
            "actual large Canvas streams monsters beyond old 5000 cap");
        var removedOuter = aoi.UpsertObserver(viewer, 2600);
        Check(removedOuter.Any(x => !x.Appeared && x.Entity.EntityId == ultraMonster.EntityId) &&
              !aoi.IsVisible(viewer.ConnectionId, ultraMonster.EntityId),
            "shrinking large Canvas despawns distant monster");
    }

    private static void EntityRemovalNeverLeavesGhostKnownSets()
    {
        var aoi = new WorldEntitySpatialAoi();
        var first = Observer(10, 13, 0, 0);
        var second = Observer(11, 13, 100, 0);
        var ground = Entity(6_000_020, 3, 13, 50, 0);

        aoi.UpsertObserver(first);
        aoi.UpsertObserver(second);
        var appeared = aoi.UpsertEntity(ground);

        Check(
            appeared.Count == 2 &&
            aoi.ObserversOf(ground.EntityId).Count == 2,
            "world AOI one ground entity can be known by two nearby observers");

        var removed = aoi.RemoveEntity(
            ground.EntityId,
            notifyObservers: true);

        Check(
            removed.Count == 2 &&
            removed.All(x => !x.Appeared) &&
            !aoi.IsVisible(first.ConnectionId, ground.EntityId) &&
            !aoi.IsVisible(second.ConnectionId, ground.EntityId) &&
            aoi.ObserversOf(ground.EntityId).Count == 0,
            "world AOI pickup/expiry removal clears every observer known set");

        aoi.UpsertEntity(ground);
        var silent = aoi.RemoveEntity(
            ground.EntityId,
            notifyObservers: false);

        Check(
            silent.Count == 0 &&
            !aoi.IsVisible(first.ConnectionId, ground.EntityId),
            "world AOI terminal death can remove entity silently after death packet");
    }

    private static void AuthoritativeMapReconciliationPurgesStaleGround()
    {
        var aoi = new WorldEntitySpatialAoi();
        var viewer = Observer(15, 13, 0, 0);
        var keep = Entity(6_000_101, 3, 13, 100, 0);
        var expired = Entity(6_000_102, 3, 13, 120, 0);

        aoi.UpsertObserver(viewer);
        aoi.UpsertEntity(keep);
        aoi.UpsertEntity(expired);

        Check(
            aoi.VisibleEntities(viewer.ConnectionId).Count == 2,
            "world AOI observer initially knows both persisted ground rows");

        var removed = aoi.RemoveMissingEntities(
            13,
            3,
            new HashSet<long>
            {
                keep.EntityId
            });

        Check(
            removed.Count == 1 &&
            removed[0].Entity.EntityId == expired.EntityId &&
            !removed[0].Appeared,
            "authoritative ground snapshot emits despawn for stale AOI row");

        Check(
            aoi.IsVisible(viewer.ConnectionId, keep.EntityId) &&
            !aoi.IsVisible(viewer.ConnectionId, expired.EntityId) &&
            aoi.VisibleEntities(viewer.ConnectionId).Count == 1,
            "authoritative ground snapshot cannot resurrect expired ghost item");
    }

    private static void LatestSpawnFactoryWinsForLateObserver()
    {
        var aoi = new WorldEntitySpatialAoi();
        var first = Observer(20, 13, 0, 0);
        aoi.UpsertObserver(first);

        var monster = Entity(9_000_030, 1, 13, 10, 0, 1);
        aoi.UpsertEntity(monster);

        // Same position but fresher state/factory. Existing viewer needs no
        // duplicate spawn, while a later viewer must receive current state.
        monster = Entity(9_000_030, 1, 13, 10, 0, 7);
        Check(
            aoi.UpsertEntity(monster).Count == 0,
            "world AOI unchanged entity position does not duplicate spawn");

        var second = Observer(21, 13, 20, 0);
        var changes = aoi.UpsertObserver(second);
        var spawn = changes
            .Single(x =>
                x.Entity.EntityId == monster.EntityId &&
                x.Appeared)
            .Entity
            .SpawnPacketFactory();

        var revision = Convert.ToInt32(
            spawn.GetType()
                .GetProperty("Revision")!
                .GetValue(spawn));

        Check(
            revision == 7,
            "world AOI late observer spawn uses newest monster state factory");
    }

    private static void SpatialIndexHandlesLargeSparseWorld()
    {
        var aoi = new WorldEntitySpatialAoi();
        var viewer = Observer(30, 13, 0, 0);
        aoi.UpsertObserver(viewer);

        for (var i = 0; i < 1_000; i++)
        {
            var entity = Entity(
                7_000_000 + i,
                i % 2 == 0 ? 1 : 3,
                13,
                10_000 + i * 8_192,
                0);

            Check(
                aoi.UpsertEntity(entity).Count == 0,
                "world AOI sparse entity does not leak distant spawn " + i);
        }

        Check(
            aoi.EntityCount == 1_000 &&
            aoi.VisibleEntities(viewer.ConnectionId).Count == 0,
            "world AOI indexes 1000 distant dynamic entities without visibility edges");

        var nearbyId = 7_000_500L;
        Check(
            aoi.UpsertEntity(
                    Entity(nearbyId, 1, 13, 100, 0))
                .Count == 1 &&
            aoi.VisibleEntities(viewer.ConnectionId)
                .Single()
                .EntityId == nearbyId,
            "world AOI moving one of 1000 entities nearby affects only that edge");

        var observerIndex = new WorldEntitySpatialAoi();
        var observers = new List<OnlinePlayerPresence>();
        for (var i = 0; i < 1_000; i++)
        {
            var observer = Observer(
                10_000 + i,
                13,
                i * 8_192,
                0);
            observers.Add(observer);
            observerIndex.UpsertObserver(observer);
        }

        var oneMonster = Entity(
            9_500_000,
            1,
            13,
            observers[500].X + 10,
            0);

        var oneEdge =
            observerIndex.UpsertEntity(oneMonster);

        Check(
            oneEdge.Count == 1 &&
            oneEdge[0].ObserverConnectionId ==
                observers[500].ConnectionId,
            "world AOI 1000-observer grid discovers only nearby viewer");
    }

    private static void ConcurrentEntityMovementSettlesSymmetrically()
    {
        var aoi = new WorldEntitySpatialAoi();
        var viewer = Observer(40, 13, 0, 0);
        aoi.UpsertObserver(viewer);

        var entities = Enumerable.Range(0, 32)
            .Select(i =>
                Entity(
                    9_600_000 + i,
                    1,
                    13,
                    5_000,
                    i * 5))
            .ToArray();

        foreach (var entity in entities)
            aoi.UpsertEntity(entity);

        Parallel.For(
            0,
            512,
            i =>
            {
                var index = i % entities.Length;
                var source = entities[index];
                aoi.UpsertEntity(
                    Entity(
                        source.EntityId,
                        1,
                        13,
                        i % 2 == 0
                            ? 100
                            : 5_000,
                        index * 5,
                        i));
            });

        for (var i = 0; i < entities.Length; i++)
        {
            aoi.UpsertEntity(
                Entity(
                    entities[i].EntityId,
                    1,
                    13,
                    100,
                    i * 5,
                    999));
        }

        Check(
            aoi.VisibleEntities(viewer.ConnectionId).Count ==
                entities.Length,
            "world AOI concurrent monster movement settles all nearby known entities");

        foreach (var entity in entities)
        {
            Check(
                aoi.ObserversOf(entity.EntityId)
                    .Contains(viewer.ConnectionId),
                "world AOI concurrent settle keeps reciprocal observer edge");
        }
    }

    private static void BroadcastHubRoutesOnlyKnownWorldObservers()
    {
        var hub = new ServerBroadcastHub();
        var near = hub.Register();
        var far = hub.Register();

        hub.UpdatePresence(
            near.ConnectionId,
            13,
            101,
            101,
            0,
            0);
        hub.UpdatePresence(
            far.ConnectionId,
            13,
            102,
            102,
            20_000,
            20_000);

        hub.ReconcileWorldAoi(
            near.ConnectionId);
        hub.ReconcileWorldAoi(
            far.ConnectionId);

        const long monsterId = 9_700_000;
        hub.UpsertWorldEntity(
            Entity(
                monsterId,
                1,
                13,
                100,
                100));

        hub.PublishToWorldEntityObservers(
            monsterId,
            new
            {
                Type = "MoveEntity",
                EntityID = monsterId,
                X = 120,
                Y = 100
            });

        Check(
            near.Queue.TryDequeue(out _),
            "world AOI hub delivers monster movement to nearby known observer");
        Check(
            !far.Queue.TryDequeue(out _),
            "world AOI hub suppresses monster movement for distant player");

        hub.UpdatePosition(
            far.ConnectionId,
            150,
            100);
        var appeared = hub.ReconcileWorldAoi(
            far.ConnectionId);

        Check(
            appeared.Count == 1 &&
            appeared[0].Appeared &&
            appeared[0].Entity.EntityId == monsterId,
            "world AOI hub player entering range receives spawn edge");

        // Reconcile returns the edge to the caller; after it is considered
        // delivered, incremental traffic reaches this observer too.
        hub.PublishToWorldEntityObservers(
            monsterId,
            new
            {
                Type = "SyncState",
                EntityID = monsterId,
                State = "idle"
            });

        Check(
            far.Queue.TryDequeue(out _),
            "world AOI newly visible observer receives later monster state");
    }
}
