using System.Text.Json;
using SGO.Server.Application;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.Shops;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class SessionLifecycleSmokeTests
{
    public static void Run(
        PacketRouter router,
        SqliteGameStore store,
        ServerBroadcastHub broadcasts,
        PlayerShopService playerShop)
    {
        var suffix =
            Guid.NewGuid()
                .ToString("N")[..10];
        var username =
            "lifecycle-" + suffix;
        const string password =
            "test-password";

        Check(
            store.Register(
                username,
                password,
                "").Success,
            "register isolated lifecycle account");

        var accountId =
            store.Login(
                username,
                password);
        Check(
            accountId.HasValue,
            "login isolated lifecycle fixture");

        using (var create = Json(
            JsonSerializer.Serialize(new
            {
                Name = "Life" + suffix[..6],
                Password = "1234",
                Race = 1,
                Hair = 1,
                HairColor = 1,
                Str = 5,
                Dex = 5,
                Con = 5,
                Wis = 5
            })))
        {
            Check(
                store.Create(
                    accountId!.Value,
                    create.RootElement).Success,
                "create isolated lifecycle character");
        }

        var characterId =
            store.Characters(
                    accountId!.Value)
                .Select(x =>
                    Convert.ToInt64(
                        x["DBID"]))
                .First(x => x > 0);

        var firstRegistration =
            broadcasts.Register();
        var session =
            new ClientSession
            {
                ConnectionId =
                    firstRegistration.ConnectionId
            };

        LoginAndSelect(
            router,
            session,
            username,
            password,
            characterId);

        router.SyncPresence(
            session);

        var initialMap =
            session.CurrentMapId;

        var oldMapObserver =
            broadcasts.Register();
        broadcasts.UpdatePresence(
            oldMapObserver.ConnectionId,
            initialMap,
            900_001,
            900_001);

        var finalX =
            session.LastX + 17;
        var finalY =
            session.LastY + 23;
        session.LastX = finalX;
        session.LastY = finalY;
        session.PositionDirty = true;

        // Open a real player shop through PacketRouter before transport
        // disconnect. DisconnectPresence must release durable InShop rows
        // while the old account/character identity is still available.
        const int lifecycleShopItemIndex = 4103;
        var lifecycleShopUid = store.AddInventoryItem(
            accountId.Value,
            characterId,
            lifecycleShopItemIndex,
            1);
        Check(
            lifecycleShopUid > 0,
            "seed player-shop item before lifecycle disconnect");

        using (var addShopItem = Json(
            JsonSerializer.Serialize(new
            {
                Type = "ShopAction",
                Action = "AddItem",
                UID = lifecycleShopUid,
                TabType = 0,
                Count = 1,
                Price = 20
            })))
        {
            var addResponses = router.Handle(
                addShopItem.RootElement,
                session);
            Check(
                JsonSerializer.Serialize(addResponses)
                    .Contains("\"Type\":\"ShopSync\""),
                "router player-shop AddItem succeeds before disconnect");
        }

        using (var openShop = Json(
            JsonSerializer.Serialize(new
            {
                Type = "ShopAction",
                Action = "OpenShop",
                ShopName = "LifecycleShop"
            })))
        {
            var openResponses = router.Handle(
                openShop.RootElement,
                session);
            Check(
                JsonSerializer.Serialize(openResponses)
                    .Contains("\"Type\":\"SyncShopBoard\""),
                "router player-shop OpenShop succeeds before disconnect");
        }

        Check(
            playerShop.GetPublicState(characterId).IsOpen &&
            store.GetCharacterAssets(accountId.Value, characterId)
                .Inventory.Any(x =>
                    Convert.ToInt32(x["ItemIndex"]) ==
                        lifecycleShopItemIndex &&
                    Convert.ToInt32(x["InShop"]) == 1),
            "open player shop is published and durable before disconnect");

        router.DisconnectPresence(
            session);

        Check(
            !playerShop.GetPublicState(characterId).IsOpen &&
            !store.GetCharacterAssets(accountId.Value, characterId)
                .Inventory.Any(x =>
                    Convert.ToInt32(x["ItemIndex"]) ==
                        lifecycleShopItemIndex &&
                    Convert.ToInt32(x["InShop"]) != 0),
            "transport disconnect closes player shop and releases all reserved rows");

        var persisted =
            store.GetCharacter(
                accountId.Value,
                characterId)!;

        Check(
            Convert.ToInt32(
                Math.Round(
                    Convert.ToDouble(
                        persisted["LocX"]))) ==
                (int)Math.Round(finalX) &&
            Convert.ToInt32(
                Math.Round(
                    Convert.ToDouble(
                        persisted["LocY"]))) ==
                (int)Math.Round(finalY),
            "disconnect persists the final dirty in-memory position");

        Check(
            !DrainJson(oldMapObserver.Queue)
                .Any(json =>
                    json.Contains("\"Type\":\"DespawnEntity\"") &&
                    json.Contains($"\"EntityID\":{characterId}")),
            "not-WorldReady map observer has no AOI edge and receives no stray despawn");

        Check(
            broadcasts.FindCharacter(
                characterId) is null,
            "disconnect removes character from online presence");

        broadcasts.Unregister(
            firstRegistration.ConnectionId);

        var reconnectRegistration =
            broadcasts.Register();
        var reconnect =
            new ClientSession
            {
                ConnectionId =
                    reconnectRegistration.ConnectionId
            };

        var enter =
            LoginAndSelect(
                router,
                reconnect,
                username,
                password,
                characterId);

        Check(
            Math.Abs(
                enter.GetProperty(
                        "LocX")
                    .GetDouble() -
                finalX) < 0.5 &&
            Math.Abs(
                enter.GetProperty(
                        "LocY")
                    .GetDouble() -
                finalY) < 0.5 &&
            Math.Abs(
                reconnect.LastX -
                finalX) < 0.5 &&
            Math.Abs(
                reconnect.LastY -
                finalY) < 0.5,
            "reconnect restores the final persisted position without rollback");

        router.SyncPresence(
            reconnect);

        var oldMapObserver2 =
            broadcasts.Register();
        broadcasts.UpdatePresence(
            oldMapObserver2.ConnectionId,
            initialMap,
            900_002,
            900_002);

        var newMap =
            initialMap == 15
                ? 7
                : 15;
        const int newX = 4321;
        const int newY = 5432;

        var newMapObserver =
            broadcasts.Register();
        broadcasts.UpdatePresence(
            newMapObserver.ConnectionId,
            newMap,
            900_003,
            900_003);

        // Simulate a server-driven map transition (quest/train/teleport)
        // racing with an old dirty client position.
        reconnect.PositionDirty = true;
        reconnect.LastX = finalX - 100;
        reconnect.LastY = finalY - 100;
        reconnect.LastLocationReconcileAt =
            DateTimeOffset.MinValue;

        Check(
            store.UpdateLocation(
                accountId.Value,
                characterId,
                newMap,
                newX,
                newY),
            "persist server-side destination before reconcile");

        router.Tick(
            reconnect,
            DateTimeOffset.UtcNow);

        Check(
            reconnect.CurrentMapId ==
                newMap &&
            Math.Abs(
                reconnect.LastX -
                newX) < 0.1 &&
            Math.Abs(
                reconnect.LastY -
                newY) < 0.1 &&
            !reconnect.PositionDirty,
            "map reconcile replaces stale dirty client coordinates with authoritative destination");

        var reconciledPresence =
            broadcasts.FindCharacter(
                characterId);
        Check(
            reconciledPresence is not null &&
            reconciledPresence.MapId == newMap &&
            !broadcasts.VisiblePlayerPeers(
                    reconnect.ConnectionId)
                .Any(),
            "server map reconcile clears old AOI immediately while destination waits for WorldReady");

        router.SyncPresence(
            reconnect);

        var oldPackets =
            DrainJson(
                oldMapObserver2.Queue);
        var newPackets =
            DrainJson(
                newMapObserver.Queue);

        Check(
            !oldPackets.Any(json =>
                json.Contains("\"Type\":\"DespawnEntity\"") &&
                json.Contains($"\"EntityID\":{characterId}")),
            "map transition does not send despawn to an observer outside AOI/not WorldReady");

        Check(
            !newPackets.Any(json =>
                json.Contains("\"Type\":\"SpawnEntity\"") &&
                json.Contains($"\"EntityID\":{characterId}")),
            "map transition does not expose player before nearby observer WorldReady");

        var afterTransition =
            store.GetCharacter(
                accountId.Value,
                characterId)!;

        Check(
            Convert.ToInt32(
                afterTransition["MapID"]) ==
                newMap &&
            Convert.ToInt32(
                Math.Round(
                    Convert.ToDouble(
                        afterTransition["LocX"]))) ==
                newX &&
            Convert.ToInt32(
                Math.Round(
                    Convert.ToDouble(
                        afterTransition["LocY"]))) ==
                newY,
            "stale dirty position never overwrites the server-side map destination");

        router.DisconnectPresence(
            reconnect);

        var afterFinalDisconnect =
            store.GetCharacter(
                accountId.Value,
                characterId)!;

        Check(
            Convert.ToInt32(
                afterFinalDisconnect["MapID"]) ==
                newMap &&
            Convert.ToInt32(
                Math.Round(
                    Convert.ToDouble(
                        afterFinalDisconnect["LocX"]))) ==
                newX &&
            Convert.ToInt32(
                Math.Round(
                    Convert.ToDouble(
                        afterFinalDisconnect["LocY"]))) ==
                newY,
            "disconnect after reconciled transition preserves authoritative destination");

        broadcasts.Unregister(
            reconnectRegistration.ConnectionId);
        broadcasts.Unregister(
            oldMapObserver.ConnectionId);
        broadcasts.Unregister(
            oldMapObserver2.ConnectionId);
        broadcasts.Unregister(
            newMapObserver.ConnectionId);
    }

    private static JsonElement LoginAndSelect(
        PacketRouter router,
        ClientSession session,
        string username,
        string password,
        long characterId)
    {
        using (var login = Json(
            JsonSerializer.Serialize(new
            {
                Type = "LoginRequest",
                User = username,
                Pass = password
            })))
        {
            var packets =
                router.Handle(
                    login.RootElement,
                    session);

            Check(
                packets.Any(x =>
                    JsonSerializer.Serialize(x)
                        .Contains(
                            "\"Success\":true")),
                "lifecycle fixture login succeeds through router");
        }

        var preWorldRequests =
            new object[]
            {
                new
                {
                    Type = "UseItemRequest",
                    ItemUID = 1L
                },
                new
                {
                    Type = "BankTransfer",
                    UID = 1L,
                    TabType = 0,
                    Amount = 1,
                    ToBank = true
                },
                new
                {
                    Type = "MallAction",
                    Action = "OpenMall"
                },
                new
                {
                    Type = "TradeLock"
                },
                new
                {
                    Type = "PetAction",
                    Action = "Summon",
                    PetUID = 1L
                },
                new
                {
                    Type = "PartyAction",
                    Action = "Create"
                }
            };

        foreach (var request in preWorldRequests)
        {
            using var packet = Json(
                JsonSerializer.Serialize(
                    request));
            var responses =
                router.Handle(
                    packet.RootElement,
                    session);
            var json =
                JsonSerializer.Serialize(
                    responses);

            Check(
                json.Contains(
                    "\"Type\":\"PacketError\"") &&
                json.Contains(
                    "\"Code\":\"WORLD_REQUIRED\""),
                "authenticated gameplay request before character selection returns WORLD_REQUIRED");
        }

        using var select = Json(
            JsonSerializer.Serialize(new
            {
                Type = "SelectCharacterRequest",
                DBID = characterId
            }));

        var selected =
            router.Handle(
                select.RootElement,
                session);
        var enter =
            JsonSerializer.SerializeToElement(
                selected.First(x =>
                    JsonSerializer.Serialize(x)
                        .Contains(
                            "\"Type\":\"EnterWorldSuccess\"")));

        return enter;
    }

    private static string[] DrainJson(
        System.Collections.Concurrent.ConcurrentQueue<object> queue)
    {
        var packets =
            new List<string>();
        while (queue.TryDequeue(
                   out var packet))
        {
            packets.Add(
                JsonSerializer.Serialize(
                    packet));
        }

        return packets.ToArray();
    }
}
