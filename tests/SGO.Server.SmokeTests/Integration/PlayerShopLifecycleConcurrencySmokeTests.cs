using System.Text.Json;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.Shops;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.SmokeTests.Fixtures;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

/// <summary>
/// Lifecycle races for an open player shop. These tests intentionally race
/// service-level BuyItem against CloseShop and validate both the in-memory
/// shop state and the durable SQLite asset/money state afterwards.
/// </summary>
internal static class PlayerShopLifecycleConcurrencySmokeTests
{
    private sealed record Player(long AccountId, long CharacterId);

    public static void Run(string repoRoot)
    {
        using var temp =
            new TemporaryDirectoryFixture("sgo-player-shop-lifecycle-race-");
        var store = new SqliteGameStore(
            Path.Combine(temp.Root, "game.db"));

        var seller = CreatePlayer(store, "seller");
        var buyer = CreatePlayer(store, "buyer");

        Check(
            store.AdjustMoney(
                buyer.AccountId,
                buyer.CharacterId,
                1000) is not null,
            "seed buyer money for player-shop lifecycle races");

        var broadcasts = new ServerBroadcastHub();
        var sellerReg = broadcasts.Register();
        var buyerReg = broadcasts.Register();

        var sellerSession = Session(
            sellerReg.ConnectionId,
            seller,
            15);
        var buyerSession = Session(
            buyerReg.ConnectionId,
            buyer,
            15);

        broadcasts.UpdatePresence(
            sellerReg.ConnectionId,
            15,
            seller.CharacterId,
            seller.AccountId,
            1000,
            1000);
        broadcasts.UpdatePresence(
            buyerReg.ConnectionId,
            15,
            buyer.CharacterId,
            buyer.AccountId,
            1010,
            1000);

        var service = new PlayerShopService(
            store,
            store,
            new CurrentClientDataCatalog(
                Path.Combine(repoRoot, "client")),
            broadcasts);

        for (var round = 0; round < 8; round++)
        {
            RaceBuyAgainstClose(
                store,
                service,
                seller,
                buyer,
                sellerSession,
                buyerSession,
                round);
        }
    }

    private static void RaceBuyAgainstClose(
        SqliteGameStore store,
        PlayerShopService service,
        Player seller,
        Player buyer,
        ClientSession sellerSession,
        ClientSession buyerSession,
        int round)
    {
        const int itemIndex = 4103;
        const long unitPrice = 20;

        var seededUid = store.AddInventoryItem(
            seller.AccountId,
            seller.CharacterId,
            itemIndex,
            1);
        Check(
            seededUid > 0,
            $"seed player-shop lifecycle item round {round}");

        var sellerCopiesBefore = CountInventory(
            store,
            seller,
            itemIndex);
        var buyerCopiesBefore = CountInventory(
            store,
            buyer,
            itemIndex);
        var sellerMoneyBefore = Money(store, seller);
        var buyerMoneyBefore = Money(store, buyer);

        var add = service.Handle(
            sellerSession,
            seller.AccountId,
            "AddItem",
            seededUid,
            0,
            1,
            unitPrice,
            0,
            "");

        var listingUid = ShopListingUid(add);
        Check(
            listingUid > 0,
            $"player-shop listing created round {round}");

        var open = service.Handle(
            sellerSession,
            seller.AccountId,
            "OpenShop",
            0,
            0,
            0,
            0,
            0,
            $"Race {round}");
        Check(
            HasPacket(open, "SyncShopBoard"),
            $"player shop opens before lifecycle race round {round}");

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);

        var buyTask = Task.Run(() =>
        {
            ready.Signal();
            if (!start.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException(
                    "player-shop buy/close start barrier timed out");

            return service.Handle(
                buyerSession,
                buyer.AccountId,
                "BuyItem",
                listingUid,
                0,
                1,
                0,
                seller.CharacterId,
                "");
        });

        var closeTask = Task.Run(() =>
        {
            ready.Signal();
            if (!start.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException(
                    "player-shop close/buy start barrier timed out");

            return service.Handle(
                sellerSession,
                seller.AccountId,
                "CloseShop",
                0,
                0,
                0,
                0,
                0,
                "");
        });

        try
        {
            Check(
                ready.Wait(TimeSpan.FromSeconds(10)),
                $"buy and close reached barrier round {round}");
        }
        finally
        {
            start.Set();
        }

        Check(
            Task.WaitAll(
                new Task[] { buyTask, closeTask },
                TimeSpan.FromSeconds(20)),
            $"buy versus close terminates round {round}");

        var buySucceeded =
            HasFlash(buyTask.Result, "購買成功");

        Check(
            !service.GetPublicState(
                seller.CharacterId).IsOpen,
            $"shop is closed after buy/close race round {round}");

        var sellerCopiesAfter = CountInventory(
            store,
            seller,
            itemIndex);
        var buyerCopiesAfter = CountInventory(
            store,
            buyer,
            itemIndex);
        var sellerMoneyAfter = Money(store, seller);
        var buyerMoneyAfter = Money(store, buyer);

        Check(
            sellerCopiesAfter + buyerCopiesAfter ==
            sellerCopiesBefore + buyerCopiesBefore,
            $"buy/close race conserves total item count round {round}");

        Check(
            sellerMoneyAfter + buyerMoneyAfter ==
            sellerMoneyBefore + buyerMoneyBefore,
            $"buy/close race conserves total money round {round}");

        if (buySucceeded)
        {
            Check(
                sellerCopiesAfter == sellerCopiesBefore - 1 &&
                buyerCopiesAfter == buyerCopiesBefore + 1 &&
                sellerMoneyAfter == sellerMoneyBefore + unitPrice &&
                buyerMoneyAfter == buyerMoneyBefore - unitPrice,
                $"winning buy commits item and money exactly once round {round}");
        }
        else
        {
            Check(
                sellerCopiesAfter == sellerCopiesBefore &&
                buyerCopiesAfter == buyerCopiesBefore &&
                sellerMoneyAfter == sellerMoneyBefore &&
                buyerMoneyAfter == buyerMoneyBefore,
                $"winning close prevents any partial purchase round {round}");
        }

        var sellerAssets = store.GetCharacterAssets(
            seller.AccountId,
            seller.CharacterId);
        Check(
            !sellerAssets.Inventory.Any(x =>
                Convert.ToInt32(x["ItemIndex"]) == itemIndex &&
                Convert.ToInt32(x["InShop"]) != 0),
            $"closing shop leaves no reserved inventory rows round {round}");
    }

    private static long ShopListingUid(
        IReadOnlyList<object> packets)
    {
        foreach (var packet in packets)
        {
            using var doc = JsonDocument.Parse(
                JsonSerializer.Serialize(packet));
            var root = doc.RootElement;
            if (!root.TryGetProperty(
                    "Type",
                    out var type) ||
                type.GetString() != "ShopSync" ||
                !root.TryGetProperty(
                    "Items",
                    out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty(
                        "UID",
                        out var uid) &&
                    uid.ValueKind == JsonValueKind.Number)
                {
                    return uid.GetInt64();
                }
            }
        }

        return 0;
    }

    private static bool HasPacket(
        IEnumerable<object> packets,
        string type)
        => packets.Any(packet =>
        {
            using var doc = JsonDocument.Parse(
                JsonSerializer.Serialize(packet));
            var root = doc.RootElement;
            return root.TryGetProperty(
                       "Type",
                       out var packetType) &&
                   packetType.GetString() == type;
        });

    private static bool HasFlash(
        IEnumerable<object> packets,
        string text)
        => packets.Any(packet =>
        {
            using var doc = JsonDocument.Parse(
                JsonSerializer.Serialize(packet));
            var root = doc.RootElement;
            return root.TryGetProperty(
                       "Type",
                       out var type) &&
                   type.GetString() == "SystemMessageFlash" &&
                   root.TryGetProperty(
                       "Message",
                       out var message) &&
                   (message.GetString() ?? "")
                       .Contains(
                           text,
                           StringComparison.Ordinal);
        });

    private static ClientSession Session(
        Guid connectionId,
        Player player,
        int mapId)
        => new()
        {
            ConnectionId = connectionId,
            AccountId = player.AccountId,
            CharacterId = player.CharacterId,
            CurrentMapId = mapId,
            WorldReady = true
        };

    private static Player CreatePlayer(
        SqliteGameStore store,
        string suffix)
    {
        var username = $"shoplife_{suffix}";
        const string password = "test-password";

        var registered =
            store.Register(
                username,
                password,
                "");
        Check(
            registered.Success,
            $"register shop-lifecycle player {suffix}");

        var accountId =
            store.Login(
                username,
                password);
        Check(
            accountId.HasValue,
            $"login shop-lifecycle player {suffix}");

        using var create = Json(
            JsonSerializer.Serialize(new
            {
                Name = $"SL{suffix}",
                Password = "1234",
                Race = 0,
                Hair = 1,
                HairColor = 1,
                Str = 5,
                Dex = 5,
                Con = 5,
                Wis = 5
            }));

        var created = store.Create(
            accountId!.Value,
            create.RootElement);
        Check(
            created.Success,
            $"create shop-lifecycle character {suffix}");

        var dbid = store.Characters(
                accountId.Value)
            .Select(x =>
                Convert.ToInt64(x["DBID"]))
            .First(x => x > 0);

        return new Player(
            accountId.Value,
            dbid);
    }

    private static long Money(
        SqliteGameStore store,
        Player player)
        => Convert.ToInt64(
            store.GetCharacter(
                player.AccountId,
                player.CharacterId)!["Money"]);

    private static int CountInventory(
        SqliteGameStore store,
        Player player,
        int itemIndex)
        => store.GetCharacterAssets(
                player.AccountId,
                player.CharacterId)
            .Inventory
            .Where(x =>
                Convert.ToInt32(
                    x["ItemIndex"]) == itemIndex)
            .Sum(x =>
                Convert.ToInt32(
                    x["Count"]));
}
