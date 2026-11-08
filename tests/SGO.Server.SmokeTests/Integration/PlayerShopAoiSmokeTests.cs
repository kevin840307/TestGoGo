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
/// Player-shop commerce must follow player visibility. Same-map coordinates
/// alone are not enough: a crafted packet from the other side of a large map
/// cannot inspect or purchase a shop that the client has never spawned.
/// </summary>
internal static class PlayerShopAoiSmokeTests
{
    private sealed record Player(
        long AccountId,
        long CharacterId);

    public static void Run(
        string repoRoot)
    {
        using var temp =
            new TemporaryDirectoryFixture(
                "sgo-player-shop-aoi-");

        var store = new SqliteGameStore(
            Path.Combine(
                temp.Root,
                "game.db"));

        var seller =
            CreatePlayer(
                store,
                "seller");
        var buyer =
            CreatePlayer(
                store,
                "buyer");

        Check(
            store.AdjustMoney(
                buyer.AccountId,
                buyer.CharacterId,
                100) is not null,
            "seed AOI shop buyer money");

        const int itemIndex = 4103;
        var sourceUid =
            store.AddInventoryItem(
                seller.AccountId,
                seller.CharacterId,
                itemIndex,
                1);

        Check(
            sourceUid > 0,
            "seed AOI shop item");

        var broadcasts =
            new ServerBroadcastHub();
        var sellerReg =
            broadcasts.Register();
        var buyerReg =
            broadcasts.Register();

        var sellerSession =
            Session(
                sellerReg.ConnectionId,
                seller,
                15);
        var buyerSession =
            Session(
                buyerReg.ConnectionId,
                buyer,
                15);

        broadcasts.UpdatePresence(
            sellerReg.ConnectionId,
            15,
            seller.CharacterId,
            seller.AccountId,
            1_000,
            1_000);
        broadcasts.UpdatePresence(
            buyerReg.ConnectionId,
            15,
            buyer.CharacterId,
            buyer.AccountId,
            10_000,
            1_000);

        broadcasts.ReconcilePlayerAoi(
            sellerReg.ConnectionId);
        broadcasts.ReconcilePlayerAoi(
            buyerReg.ConnectionId);

        Check(
            !broadcasts.IsPlayerAoiVisible(
                buyerReg.ConnectionId,
                sellerReg.ConnectionId),
            "far same-map shop owner is outside buyer player AOI");

        var service =
            new PlayerShopService(
                store,
                store,
                new CurrentClientDataCatalog(
                    Path.Combine(
                        repoRoot,
                        "client")),
                broadcasts);

        var add =
            service.Handle(
                sellerSession,
                seller.AccountId,
                "AddItem",
                sourceUid,
                0,
                1,
                20,
                0,
                "");

        var listingUid =
            ListingUid(add);
        Check(
            listingUid > 0,
            "AOI shop listing is created");

        service.Handle(
            sellerSession,
            seller.AccountId,
            "OpenShop",
            0,
            0,
            0,
            0,
            0,
            "AOI Shop");

        var moneyBefore =
            Money(
                store,
                buyer);
        var buyerCopiesBefore =
            CountInventory(
                store,
                buyer,
                itemIndex);
        var sellerCopiesBefore =
            CountInventory(
                store,
                seller,
                itemIndex);

        var farView =
            service.Handle(
                buyerSession,
                buyer.AccountId,
                "ViewShop",
                0,
                0,
                0,
                0,
                seller.CharacterId,
                "");

        var farBuy =
            service.Handle(
                buyerSession,
                buyer.AccountId,
                "BuyItem",
                listingUid,
                0,
                1,
                0,
                seller.CharacterId,
                "");

        Check(
            HasFlash(
                farView,
                "不在附近") &&
            HasFlash(
                farBuy,
                "不在附近"),
            "far player cannot view or buy invisible player shop");

        Check(
            Money(store, buyer) ==
                moneyBefore &&
            CountInventory(
                store,
                buyer,
                itemIndex) ==
                buyerCopiesBefore &&
            CountInventory(
                store,
                seller,
                itemIndex) ==
                sellerCopiesBefore,
            "far rejected shop packets cannot mutate money or assets");

        broadcasts.UpdatePosition(
            buyerReg.ConnectionId,
            1_100,
            1_000);
        var enter =
            broadcasts.ReconcilePlayerAoi(
                buyerReg.ConnectionId);

        Check(
            enter.Any(x =>
                x.Appeared &&
                x.Subject.CharacterId ==
                    seller.CharacterId) &&
            broadcasts.IsPlayerAoiVisible(
                buyerReg.ConnectionId,
                sellerReg.ConnectionId),
            "approaching shop owner creates player AOI spawn edge");

        var nearView =
            service.Handle(
                buyerSession,
                buyer.AccountId,
                "ViewShop",
                0,
                0,
                0,
                0,
                seller.CharacterId,
                "");

        Check(
            HasPacket(
                nearView,
                "ShopViewResponse"),
            "nearby player can view shop immediately after AOI enter");

        // Exercise the exit path without consuming the listing first.
        broadcasts.UpdatePosition(
            buyerReg.ConnectionId,
            10_000,
            1_000);
        var exit =
            broadcasts.ReconcilePlayerAoi(
                buyerReg.ConnectionId);

        Check(
            exit.Any(x =>
                !x.Appeared &&
                x.Subject.CharacterId ==
                    seller.CharacterId) &&
            !broadcasts.IsPlayerAoiVisible(
                buyerReg.ConnectionId,
                sellerReg.ConnectionId),
            "leaving shop owner creates player AOI despawn edge");

        var afterExitBuy =
            service.Handle(
                buyerSession,
                buyer.AccountId,
                "BuyItem",
                listingUid,
                0,
                1,
                0,
                seller.CharacterId,
                "");

        Check(
            HasFlash(
                afterExitBuy,
                "不在附近"),
            "shop buy is rejected again after AOI exit");

        broadcasts.UpdatePosition(
            buyerReg.ConnectionId,
            1_100,
            1_000);
        broadcasts.ReconcilePlayerAoi(
            buyerReg.ConnectionId);

        var buy =
            service.Handle(
                buyerSession,
                buyer.AccountId,
                "BuyItem",
                listingUid,
                0,
                1,
                0,
                seller.CharacterId,
                "");

        Check(
            HasFlash(
                buy,
                "購買成功"),
            "nearby player can buy after re-entering AOI");

        Check(
            Money(store, buyer) ==
                moneyBefore - 20 &&
            CountInventory(
                store,
                buyer,
                itemIndex) ==
                buyerCopiesBefore + 1 &&
            CountInventory(
                store,
                seller,
                itemIndex) ==
                sellerCopiesBefore - 1,
            "AOI-authorized purchase transfers money and item exactly once");
    }

    private static ClientSession Session(
        Guid connectionId,
        Player player,
        int mapId)
        => new()
        {
            ConnectionId =
                connectionId,
            AccountId =
                player.AccountId,
            CharacterId =
                player.CharacterId,
            CurrentMapId =
                mapId,
            WorldReady =
                true
        };

    private static Player CreatePlayer(
        SqliteGameStore store,
        string suffix)
    {
        var username =
            $"shop_aoi_{suffix}_{Guid.NewGuid():N}";
        const string password =
            "test-password";

        Check(
            store.Register(
                username,
                password,
                "").Success,
            $"register AOI shop player {suffix}");

        var accountId =
            store.Login(
                username,
                password);
        Check(
            accountId.HasValue,
            $"login AOI shop player {suffix}");

        var name =
            ("A" + suffix);
        if (name.Length > 12)
            name = name[..12];

        using var create =
            Json(
                JsonSerializer.Serialize(
                    new
                    {
                        Name = name,
                        Password = "1234",
                        Race = 0,
                        Hair = 1,
                        HairColor = 1,
                        Str = 5,
                        Dex = 5,
                        Con = 5,
                        Wis = 5
                    }));

        Check(
            store.Create(
                accountId!.Value,
                create.RootElement)
                .Success,
            $"create AOI shop character {suffix}");

        var dbid =
            store.Characters(
                    accountId.Value)
                .Select(x =>
                    Convert.ToInt64(
                        x["DBID"]))
                .First(x => x > 0);

        return new Player(
            accountId.Value,
            dbid);
    }

    private static long ListingUid(
        IEnumerable<object> packets)
    {
        foreach (var packet in packets)
        {
            using var doc =
                JsonDocument.Parse(
                    JsonSerializer.Serialize(
                        packet));

            var root =
                doc.RootElement;
            if (!root.TryGetProperty(
                    "Type",
                    out var type) ||
                type.GetString() !=
                    "ShopSync" ||
                !root.TryGetProperty(
                    "Items",
                    out var items) ||
                items.ValueKind !=
                    JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in
                     items.EnumerateArray())
            {
                if (item.TryGetProperty(
                        "UID",
                        out var uid))
                    return uid.GetInt64();
            }
        }

        return 0;
    }

    private static bool HasPacket(
        IEnumerable<object> packets,
        string type)
        => packets.Any(
            packet =>
            {
                using var doc =
                    JsonDocument.Parse(
                        JsonSerializer.Serialize(
                            packet));
                return doc.RootElement
                    .TryGetProperty(
                        "Type",
                        out var packetType) &&
                    packetType.GetString() ==
                        type;
            });

    private static bool HasFlash(
        IEnumerable<object> packets,
        string text)
        => packets.Any(
            packet =>
            {
                using var doc =
                    JsonDocument.Parse(
                        JsonSerializer.Serialize(
                            packet));
                var root =
                    doc.RootElement;

                return root.TryGetProperty(
                           "Type",
                           out var type) &&
                       type.GetString() ==
                           "SystemMessageFlash" &&
                       root.TryGetProperty(
                           "Message",
                           out var message) &&
                       (message.GetString() ?? "")
                           .Contains(
                               text,
                               StringComparison.Ordinal);
            });

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
                    x["ItemIndex"]) ==
                itemIndex)
            .Sum(x =>
                Convert.ToInt32(
                    x["Count"]));
}
