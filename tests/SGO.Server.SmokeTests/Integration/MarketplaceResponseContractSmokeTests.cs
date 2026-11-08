using System.Collections.Concurrent;
using System.Text.Json;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.Shops;
using SGO.Server.Application.Trade;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Persistence.Sqlite;
using SGO.Server.SmokeTests.Fixtures;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

/// <summary>
/// Runtime contracts for dynamic multiplayer commerce payloads. These tests use
/// real services, SQLite and ServerBroadcastHub queues instead of source grep.
/// </summary>
internal static class MarketplaceResponseContractSmokeTests
{
    private sealed record Player(long AccountId, long CharacterId);

    public static void Run(string repoRoot)
    {
        TradeSyncUsesSymmetricRuntimeViews(repoRoot);
        PlayerShopViewsPreserveDynamicListings(repoRoot);
        TenBuyerPlayerShopRaceConservesAssetsAndMoney(repoRoot);
    }

    private static void TradeSyncUsesSymmetricRuntimeViews(string repoRoot)
    {
        using var temp = new TemporaryDirectoryFixture(
            "sgo-trade-response-contract-");
        var store = new SqliteGameStore(Path.Combine(temp.Root, "game.db"));
        var first = CreatePlayer(store, "tradea");
        var second = CreatePlayer(store, "tradeb");

        Check(store.AdjustMoney(first.AccountId, first.CharacterId, 100) is not null,
            "seed trade-response money");

        const int generalItemIndex = 4103;
        var generalUid = store.AddInventoryItem(
            first.AccountId, first.CharacterId, generalItemIndex, 2);
        var petUid = store.AddPet(
            first.AccountId, first.CharacterId, 45001, "ContractPet");
        Check(generalUid > 0 && petUid > 0,
            "seed general item and pet for TradeSync dynamic views");

        var broadcasts = new ServerBroadcastHub();
        var firstReg = broadcasts.Register();
        var secondReg = broadcasts.Register();
        var firstSession = Session(firstReg.ConnectionId, first, 15);
        var secondSession = Session(secondReg.ConnectionId, second, 15);
        broadcasts.UpdatePresence(
            firstReg.ConnectionId, 15, first.CharacterId, first.AccountId, 1000, 1000);
        broadcasts.UpdatePresence(
            secondReg.ConnectionId, 15, second.CharacterId, second.AccountId, 1010, 1000);

        var trade = new TradeService(
            store,
            new CurrentClientDataCatalog(Path.Combine(repoRoot, "client")),
            broadcasts);

        trade.Request(
            firstSession, first.AccountId, second.CharacterId, DateTimeOffset.UtcNow);
        trade.Reply(
            secondSession, second.AccountId, first.CharacterId, true, DateTimeOffset.UtcNow);
        Drain(firstReg.Queue);
        Drain(secondReg.Queue);

        using (var addGeneral = Json(JsonSerializer.Serialize(new
        {
            TabType = 0,
            UID = generalUid,
            Count = 2
        })))
        {
            trade.Action(firstSession, "TradeAddItem", addGeneral.RootElement);
        }

        using (var addPet = Json(JsonSerializer.Serialize(new
        {
            TabType = 2,
            UID = petUid,
            Count = 1
        })))
        {
            trade.Action(firstSession, "TradeAddItem", addPet.RootElement);
        }

        var firstSync = LastPacket(Drain(firstReg.Queue), "TradeSync");
        var secondSync = LastPacket(Drain(secondReg.Queue), "TradeSync");
        ValidateTradeSyncHeader(firstSync, 0, 0, false, false);
        ValidateTradeSyncHeader(secondSync, 0, 0, false, false);

        var firstMine = RequireArray(firstSync, "MyItems");
        var firstOther = RequireArray(firstSync, "OtherItems");
        var secondMine = RequireArray(secondSync, "MyItems");
        var secondOther = RequireArray(secondSync, "OtherItems");
        Check(
            firstMine.GetArrayLength() == 2 &&
            firstOther.GetArrayLength() == 0 &&
            secondMine.GetArrayLength() == 0 &&
            secondOther.GetArrayLength() == 2,
            "TradeSync exposes the same two offers from opposite My/Other perspectives");

        ValidateGeneralListing(
            Listing(firstMine, 0), generalUid, generalItemIndex, 2, false);
        ValidateGeneralListing(
            Listing(secondOther, 0), generalUid, generalItemIndex, 2, false);
        ValidatePetListing(
            Listing(firstMine, 2), petUid, 45001, false);
        ValidatePetListing(
            Listing(secondOther, 2), petUid, 45001, false);

        Drain(firstReg.Queue);
        Drain(secondReg.Queue);
        using (var money = Json("""{"Money":35}"""))
            trade.Action(firstSession, "TradeSetMoney", money.RootElement);

        firstSync = LastPacket(Drain(firstReg.Queue), "TradeSync");
        secondSync = LastPacket(Drain(secondReg.Queue), "TradeSync");
        ValidateTradeSyncHeader(firstSync, 35, 0, false, false);
        ValidateTradeSyncHeader(secondSync, 0, 35, false, false);

        using (var empty = Json("{}"))
            trade.Action(firstSession, "TradeLock", empty.RootElement);

        firstSync = LastPacket(Drain(firstReg.Queue), "TradeSync");
        secondSync = LastPacket(Drain(secondReg.Queue), "TradeSync");
        ValidateTradeSyncHeader(firstSync, 35, 0, true, false);
        ValidateTradeSyncHeader(secondSync, 0, 35, false, true);

        using (var cancel = Json("{}"))
            trade.Action(secondSession, "TradeCancel", cancel.RootElement);

        Console.WriteLine("TradeSync dynamic multiplayer response contract passed.");
    }

    private static void PlayerShopViewsPreserveDynamicListings(string repoRoot)
    {
        using var temp = new TemporaryDirectoryFixture(
            "sgo-shop-response-contract-");
        var store = new SqliteGameStore(Path.Combine(temp.Root, "game.db"));
        var seller = CreatePlayer(store, "viewseller");
        var buyer = CreatePlayer(store, "viewbuyer");

        const int generalItemIndex = 4103;
        var generalUid = store.AddInventoryItem(
            seller.AccountId, seller.CharacterId, generalItemIndex, 2);
        var petUid = store.AddPet(
            seller.AccountId, seller.CharacterId, 45001, "ShopPet");
        Check(generalUid > 0 && petUid > 0,
            "seed player-shop general and pet dynamic listings");

        var broadcasts = new ServerBroadcastHub();
        var sellerReg = broadcasts.Register();
        var buyerReg = broadcasts.Register();
        var sellerSession = Session(sellerReg.ConnectionId, seller, 15);
        var buyerSession = Session(buyerReg.ConnectionId, buyer, 15);
        broadcasts.UpdatePresence(
            sellerReg.ConnectionId, 15, seller.CharacterId, seller.AccountId, 1000, 1000);
        broadcasts.UpdatePresence(
            buyerReg.ConnectionId, 15, buyer.CharacterId, buyer.AccountId, 1010, 1000);
        broadcasts.ReconcilePlayerAoi(sellerReg.ConnectionId);
        broadcasts.ReconcilePlayerAoi(buyerReg.ConnectionId);

        var service = new PlayerShopService(
            store,
            store,
            new CurrentClientDataCatalog(Path.Combine(repoRoot, "client")),
            broadcasts);

        var addGeneral = service.Handle(
            sellerSession, seller.AccountId, "AddItem",
            generalUid, 0, 2, 25, 0, "");
        var firstShopSync = LastPacket(addGeneral, "ShopSync");
        var firstItems = RequireArray(firstShopSync, "Items");
        Check(firstItems.GetArrayLength() == 1,
            "ShopSync contains first dynamic listing");
        var generalListing = Listing(firstItems, 0);
        var reservedGeneralUid = RequireNumber(generalListing, "UID").GetInt64();
        ValidateGeneralListing(
            generalListing, reservedGeneralUid, generalItemIndex, 2, true, 25);
        RequireString(generalListing, "Name");
        Check(RequireNumber(generalListing, "InShop").GetInt32() == 1,
            "ShopSync source dictionary keeps InShop=1");

        var addPet = service.Handle(
            sellerSession, seller.AccountId, "AddItem",
            petUid, 2, 1, 40, 0, "");
        var secondShopSync = LastPacket(addPet, "ShopSync");
        var secondItems = RequireArray(secondShopSync, "Items");
        Check(secondItems.GetArrayLength() == 2,
            "ShopSync preserves multiple heterogeneous dynamic listings");

        var petListing = Listing(secondItems, 2);
        ValidatePetListing(petListing, petUid, 45001, true, 40);
        RequireString(petListing, "Name");

        var open = service.Handle(
            sellerSession, seller.AccountId, "OpenShop",
            0, 0, 0, 0, 0, "Contract Shop");
        var board = LastPacket(open, "SyncShopBoard");
        Check(
            RequireBoolean(board, "IsOpen") &&
            RequireString(board, "ShopName").GetString() == "Contract Shop",
            "opening player shop emits typed public board state");

        var view = service.Handle(
            buyerSession, buyer.AccountId, "ViewShop",
            0, 0, 0, 0, seller.CharacterId, "");
        var viewPacket = LastPacket(view, "ShopViewResponse");
        Check(
            RequireString(viewPacket, "ShopName").GetString() == "Contract Shop" &&
            RequireNumber(viewPacket, "OwnerDBID").GetInt64() == seller.CharacterId,
            "ShopViewResponse identifies the authoritative seller");

        var viewedItems = RequireArray(viewPacket, "Items");
        Check(viewedItems.GetArrayLength() == 2,
            "ShopViewResponse exposes both server-owned listings");
        ValidateGeneralListing(
            Listing(viewedItems, 0),
            reservedGeneralUid, generalItemIndex, 2, true, 25);
        ValidatePetListing(
            Listing(viewedItems, 2),
            petUid, 45001, true, 40);

        service.Handle(
            sellerSession, seller.AccountId, "CloseShop",
            0, 0, 0, 0, 0, "");

        Console.WriteLine("Player-shop dynamic listing response contract passed.");
    }

    private static void TenBuyerPlayerShopRaceConservesAssetsAndMoney(
        string repoRoot)
    {
        using var temp = new TemporaryDirectoryFixture(
            "sgo-shop-service-race-");
        var store = new SqliteGameStore(Path.Combine(temp.Root, "game.db"));
        var seller = CreatePlayer(store, "raceseller");
        var buyers = Enumerable.Range(0, 10)
            .Select(i => CreatePlayer(store, $"race{i:D2}"))
            .ToArray();

        foreach (var buyer in buyers)
        {
            Check(store.AdjustMoney(
                    buyer.AccountId, buyer.CharacterId, 100) is not null,
                "seed service-level player-shop buyer money");
        }

        const int itemIndex = 4103;
        var sourceUid = store.AddInventoryItem(
            seller.AccountId, seller.CharacterId, itemIndex, 5);
        Check(sourceUid > 0,
            "seed five-count service-level player-shop listing");

        var buyerCopiesBefore = buyers.Sum(
            x => CountInventory(store, x, itemIndex));
        var sellerCopiesBefore = CountInventory(
            store, seller, itemIndex);
        var sellerMoneyBefore = Money(store, seller);
        var buyerMoneyBefore = buyers.Sum(x => Money(store, x));

        var broadcasts = new ServerBroadcastHub();
        var sellerReg = broadcasts.Register();
        var sellerSession = Session(sellerReg.ConnectionId, seller, 15);
        broadcasts.UpdatePresence(
            sellerReg.ConnectionId, 15, seller.CharacterId, seller.AccountId, 1000, 1000);

        var buyerSessions = buyers.Select((buyer, index) =>
        {
            var reg = broadcasts.Register();
            broadcasts.UpdatePresence(
                reg.ConnectionId, 15, buyer.CharacterId, buyer.AccountId,
                1010 + index, 1000);
            return Session(reg.ConnectionId, buyer, 15);
        }).ToArray();

        broadcasts.ReconcilePlayerAoi(sellerReg.ConnectionId);
        foreach (var buyerSession in buyerSessions)
            broadcasts.ReconcilePlayerAoi(buyerSession.ConnectionId);

        var service = new PlayerShopService(
            store,
            store,
            new CurrentClientDataCatalog(Path.Combine(repoRoot, "client")),
            broadcasts);

        var add = service.Handle(
            sellerSession, seller.AccountId, "AddItem",
            sourceUid, 0, 5, 20, 0, "");
        var shopSync = LastPacket(add, "ShopSync");
        var listing = Listing(RequireArray(shopSync, "Items"), 0);
        var listingUid = RequireNumber(listing, "UID").GetInt64();

        var open = service.Handle(
            sellerSession, seller.AccountId, "OpenShop",
            0, 0, 0, 0, 0, "Race Shop");
        Check(RequireBoolean(LastPacket(open, "SyncShopBoard"), "IsOpen"),
            "service-level race shop opens before buyers run");
        Drain(sellerReg.Queue);

        var attempts = buyers.Select((buyer, index) =>
            Task.Run(() => service.Handle(
                buyerSessions[index],
                buyer.AccountId,
                "BuyItem",
                listingUid,
                0,
                1,
                0,
                seller.CharacterId,
                ""))).ToArray();
        Task.WaitAll(attempts);

        var responses = attempts.Select(x => x.Result).ToArray();
        var successes = responses.Where(x => HasFlash(x, "購買成功")).ToArray();
        Check(successes.Length == 5,
            "ten buyers racing five player-shop items produce exactly five service-level successes");
        Check(successes.All(x =>
                HasPacket(x, "SyncMoney") &&
                HasPacket(x, "InventorySync")),
            "every successful player-shop race response includes money and inventory synchronization");

        var totalBuyerCopies = buyers.Sum(x => CountInventory(store, x, itemIndex));
        var sellerCopies = CountInventory(store, seller, itemIndex);
        var sellerMoney = Money(store, seller);
        var buyerMoney = buyers.Sum(x => Money(store, x));
        Check(
            totalBuyerCopies == buyerCopiesBefore + 5 &&
            sellerCopies == sellerCopiesBefore - 5,
            "service-level player-shop race transfers exactly five items without assuming empty starter inventory");
        Check(
            sellerMoney == sellerMoneyBefore + 100 &&
            buyerMoney == buyerMoneyBefore - 100 &&
            sellerMoney + buyerMoney ==
                sellerMoneyBefore + buyerMoneyBefore,
            "service-level player-shop race conserves total money without assuming zero starter balance");

        var publicState = service.GetPublicState(seller.CharacterId);
        Check(!publicState.IsOpen,
            "sold-out player shop closes atomically after final concurrent purchase");

        var sellerPackets = Drain(sellerReg.Queue);
        var finalSync = LastPacket(sellerPackets, "ShopSync");
        Check(RequireArray(finalSync, "Items").GetArrayLength() == 0,
            "seller receives an empty ShopSync after the final item is sold");
        var closedBoard = LastPacket(sellerPackets, "SyncShopBoard");
        Check(!RequireBoolean(closedBoard, "IsOpen"),
            "seller observes the sold-out public board close");

        Console.WriteLine("Ten-buyer PlayerShopService race contract passed.");
    }

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

    private static Player CreatePlayer(SqliteGameStore store, string suffix)
    {
        var username = $"market_{suffix}";
        const string password = "test-password";
        var registered = store.Register(username, password, "");
        Check(registered.Success, $"register marketplace player {suffix}");

        var accountId = store.Login(username, password);
        Check(accountId.HasValue, $"login marketplace player {suffix}");

        var name = $"M{suffix}";
        if (name.Length > 12)
            name = name[..12];

        using var create = Json(JsonSerializer.Serialize(new
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

        var created = store.Create(accountId!.Value, create.RootElement);
        Check(created.Success, $"create marketplace character {suffix}");

        var characterId = store.Characters(accountId.Value)
            .Select(x => Convert.ToInt64(x["DBID"]))
            .First(x => x > 0);
        return new Player(accountId.Value, characterId);
    }

    private static object[] Drain(ConcurrentQueue<object> queue)
    {
        var packets = new List<object>();
        while (queue.TryDequeue(out var packet))
            packets.Add(packet);
        return packets.ToArray();
    }

    private static JsonElement LastPacket(
        IEnumerable<object> packets,
        string type)
    {
        JsonElement result = default;
        var found = false;
        foreach (var packet in packets)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(packet));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("Type", out var packetType) ||
                packetType.ValueKind != JsonValueKind.String ||
                packetType.GetString() != type)
                continue;

            result = root.Clone();
            found = true;
        }

        if (!found)
            throw new InvalidOperationException(
                $"FAILED: expected runtime packet {type}");
        return result;
    }

    private static bool HasPacket(IEnumerable<object> packets, string type)
    {
        foreach (var packet in packets)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(packet));
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("Type", out var packetType) &&
                packetType.ValueKind == JsonValueKind.String &&
                packetType.GetString() == type)
                return true;
        }

        return false;
    }

    private static bool HasFlash(IEnumerable<object> packets, string message)
    {
        foreach (var packet in packets)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(packet));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("Type", out var type) ||
                type.GetString() != "SystemMessageFlash" ||
                !root.TryGetProperty("Message", out var actual))
                continue;
            if (actual.GetString() == message)
                return true;
        }

        return false;
    }

    private static void ValidateTradeSyncHeader(
        JsonElement packet,
        long myMoney,
        long otherMoney,
        bool myLocked,
        bool otherLocked)
    {
        Check(RequireNumber(packet, "MyMoney").GetInt64() == myMoney,
            "TradeSync MyMoney matches authoritative participant view");
        Check(RequireNumber(packet, "OtherMoney").GetInt64() == otherMoney,
            "TradeSync OtherMoney matches opposite participant view");
        Check(RequireBoolean(packet, "MyLocked") == myLocked,
            "TradeSync MyLocked matches authoritative participant view");
        Check(RequireBoolean(packet, "OtherLocked") == otherLocked,
            "TradeSync OtherLocked matches opposite participant view");
        RequireArray(packet, "MyItems");
        RequireArray(packet, "OtherItems");
    }

    private static JsonElement Listing(JsonElement items, int tabType)
    {
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("TabType", out var type) &&
                type.ValueKind == JsonValueKind.Number &&
                type.GetInt32() == tabType)
                return item.Clone();
        }

        throw new InvalidOperationException(
            $"FAILED: dynamic listing TabType={tabType} not found");
    }

    private static void ValidateGeneralListing(
        JsonElement listing,
        long uid,
        int itemIndex,
        int count,
        bool includePrice,
        long expectedPrice = 0)
    {
        Check(
            RequireNumber(listing, "TabType").GetInt32() == 0 &&
            RequireNumber(listing, "UID").GetInt64() == uid &&
            RequireNumber(listing, "ItemIndex").GetInt32() == itemIndex &&
            RequireNumber(listing, "Count").GetInt32() == count,
            "general dynamic listing keeps numeric TabType/UID/ItemIndex/Count values");
        if (includePrice)
        {
            Check(RequireNumber(listing, "Price").GetInt64() == expectedPrice,
                "player-shop general listing keeps numeric unit price");
        }
    }

    private static void ValidatePetListing(
        JsonElement listing,
        long uid,
        int petId,
        bool includePrice,
        long expectedPrice = 0)
    {
        Check(
            RequireNumber(listing, "TabType").GetInt32() == 2 &&
            RequireNumber(listing, "UID").GetInt64() == uid &&
            RequireNumber(listing, "ItemIndex").GetInt32() == petId &&
            RequireNumber(listing, "Count").GetInt32() == 1,
            "pet dynamic listing keeps numeric identity fields");

        var extra = RequireObject(listing, "ExtraData");
        Check(
            RequireNumber(extra, "UID").GetInt64() == uid &&
            RequireNumber(extra, "PETID").GetInt32() == petId &&
            !string.IsNullOrWhiteSpace(RequireString(extra, "Name").GetString()),
            "pet dynamic listing keeps nested authoritative ExtraData");
        if (includePrice)
        {
            Check(RequireNumber(listing, "Price").GetInt64() == expectedPrice,
                "player-shop pet listing keeps numeric unit price");
        }
    }

    private static JsonElement RequireArray(JsonElement packet, string name)
        => RequireKind(packet, name, JsonValueKind.Array);

    private static JsonElement RequireObject(JsonElement packet, string name)
        => RequireKind(packet, name, JsonValueKind.Object);

    private static JsonElement RequireNumber(JsonElement packet, string name)
        => RequireKind(packet, name, JsonValueKind.Number);

    private static JsonElement RequireString(JsonElement packet, string name)
        => RequireKind(packet, name, JsonValueKind.String);

    private static bool RequireBoolean(JsonElement packet, string name)
    {
        Check(packet.TryGetProperty(name, out var value),
            $"runtime marketplace packet contains {name}");
        Check(value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            $"runtime marketplace {name} is boolean");
        return value.GetBoolean();
    }

    private static JsonElement RequireKind(
        JsonElement packet,
        string name,
        JsonValueKind kind)
    {
        Check(packet.TryGetProperty(name, out var value),
            $"runtime marketplace packet contains {name}");
        Check(value.ValueKind == kind,
            $"runtime marketplace {name} is {kind}");
        return value;
    }

    private static int CountInventory(
        SqliteGameStore store,
        Player player,
        int itemIndex)
        => store.GetCharacterAssets(player.AccountId, player.CharacterId)
            .Inventory
            .Where(x => Convert.ToInt32(x["ItemIndex"]) == itemIndex)
            .Sum(x => Convert.ToInt32(x["Count"]));

    private static long Money(SqliteGameStore store, Player player)
        => Convert.ToInt64(
            store.GetCharacter(player.AccountId, player.CharacterId)!["Money"]);
}
