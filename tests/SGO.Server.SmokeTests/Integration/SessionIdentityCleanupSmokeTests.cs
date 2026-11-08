using System.Text.Json;
using SGO.Server.Application;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.Shops;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

/// <summary>
/// Identity-transition cleanup must run while the old account/character IDs
/// are still available. This protects player-shop reservations when a client
/// logs into another account on the same socket or sends LogoutRequest.
/// </summary>
internal static class SessionIdentityCleanupSmokeTests
{
    private sealed record Player(
        string Username,
        string Password,
        long AccountId,
        long CharacterId);

    public static void Run(
        PacketRouter router,
        SqliteGameStore store,
        ServerBroadcastHub broadcasts,
        PlayerShopService playerShop)
    {
        VerifyIdentityFixtureUsernames();
        var first = CreatePlayer(store, "identity_a");
        var second = CreatePlayer(store, "identity_b");

        var registration = broadcasts.Register();
        var session = new ClientSession
        {
            ConnectionId = registration.ConnectionId
        };

        LoginAndSelect(
            router,
            session,
            first);
        router.SyncPresence(session);

        OpenShop(
            router,
            store,
            playerShop,
            session,
            first,
            "ReloginShop");

        Check(
            broadcasts.FindCharacter(
                first.CharacterId) is not null,
            "first character is online before same-socket relogin");

        using (var relogin = Json(
            JsonSerializer.Serialize(new
            {
                Type = "LoginRequest",
                User = second.Username,
                Pass = second.Password
            })))
        {
            var responses = router.Handle(
                relogin.RootElement,
                session);
            Check(
                JsonSerializer.Serialize(responses)
                    .Contains("\"Type\":\"LoginResponse\"") &&
                JsonSerializer.Serialize(responses)
                    .Contains("\"Success\":true"),
                "same socket can authenticate second account after cleanup");
        }

        Check(
            session.AccountId == second.AccountId &&
            !session.CharacterId.HasValue &&
            !playerShop.GetPublicState(
                first.CharacterId).IsOpen &&
            broadcasts.FindCharacter(
                first.CharacterId) is null,
            "relogin removes old character presence and public shop state");

        Check(
            !HasReservedShopRows(
                store,
                first),
            "relogin releases old account player-shop reservations");

        Select(
            router,
            session,
            second.CharacterId);
        router.SyncPresence(session);

        OpenShop(
            router,
            store,
            playerShop,
            session,
            second,
            "LogoutShop");

        using (var logout = Json(
            JsonSerializer.Serialize(new
            {
                Type = "LogoutRequest",
                IsBackToLogin = true
            })))
        {
            var responses = router.Handle(
                logout.RootElement,
                session);
            Check(
                JsonSerializer.Serialize(responses)
                    .Contains("\"Type\":\"LogoutResponse\""),
                "LogoutRequest returns normal response after lifecycle cleanup");
        }

        Check(
            !session.AccountId.HasValue &&
            !session.CharacterId.HasValue &&
            !playerShop.GetPublicState(
                second.CharacterId).IsOpen &&
            broadcasts.FindCharacter(
                second.CharacterId) is null,
            "LogoutRequest clears identity, presence and public shop state");

        Check(
            !HasReservedShopRows(
                store,
                second),
            "LogoutRequest releases player-shop reservations before identity reset");

        // Simulate persistence left behind by a process crash: the in-memory
        // shop is gone, but one DB row is still marked InShop=1.
        const int staleItemIndex = 4103;
        var staleUid = store.AddInventoryItem(
            second.AccountId,
            second.CharacterId,
            staleItemIndex,
            1);
        Check(
            staleUid > 0,
            "seed orphaned player-shop row for restart recovery");

        var reserved = store.SetPlayerShopAssetState(
            second.AccountId,
            second.CharacterId,
            staleUid,
            0,
            1,
            true);
        Check(
            reserved.Success &&
            HasReservedShopRows(
                store,
                second),
            "simulate crash leaving durable InShop reservation without memory shop");

        broadcasts.Unregister(
            registration.ConnectionId);

        var recoveryRegistration =
            broadcasts.Register();
        var recoverySession =
            new ClientSession
            {
                ConnectionId =
                    recoveryRegistration.ConnectionId
            };

        LoginAndSelect(
            router,
            recoverySession,
            second);

        Check(
            !HasReservedShopRows(
                store,
                second) &&
            !playerShop.GetPublicState(
                second.CharacterId).IsOpen,
            "character bootstrap recovers orphaned player-shop reservations after restart");

        router.DisconnectPresence(
            recoverySession);
        broadcasts.Unregister(
            recoveryRegistration.ConnectionId);
    }

    private static void LoginAndSelect(
        PacketRouter router,
        ClientSession session,
        Player player)
    {
        using (var login = Json(
            JsonSerializer.Serialize(new
            {
                Type = "LoginRequest",
                User = player.Username,
                Pass = player.Password
            })))
        {
            var responses = router.Handle(
                login.RootElement,
                session);
            Check(
                JsonSerializer.Serialize(responses)
                    .Contains("\"Success\":true"),
                $"login identity-cleanup player {player.Username}");
        }

        Select(
            router,
            session,
            player.CharacterId);
    }

    private static void Select(
        PacketRouter router,
        ClientSession session,
        long characterId)
    {
        using var select = Json(
            JsonSerializer.Serialize(new
            {
                Type = "SelectCharacterRequest",
                DBID = characterId
            }));

        var responses = router.Handle(
            select.RootElement,
            session);
        Check(
            JsonSerializer.Serialize(responses)
                .Contains("\"Type\":\"EnterWorldSuccess\""),
            $"select identity-cleanup character {characterId}");
    }

    private static void OpenShop(
        PacketRouter router,
        SqliteGameStore store,
        PlayerShopService playerShop,
        ClientSession session,
        Player player,
        string name)
    {
        const int itemIndex = 4103;
        var uid = store.AddInventoryItem(
            player.AccountId,
            player.CharacterId,
            itemIndex,
            1);
        Check(
            uid > 0,
            $"seed {name} item");

        using (var add = Json(
            JsonSerializer.Serialize(new
            {
                Type = "ShopAction",
                Action = "AddItem",
                UID = uid,
                TabType = 0,
                Count = 1,
                Price = 20
            })))
        {
            var responses = router.Handle(
                add.RootElement,
                session);
            Check(
                JsonSerializer.Serialize(responses)
                    .Contains("\"Type\":\"ShopSync\""),
                $"{name} item becomes reserved through router");
        }

        using (var open = Json(
            JsonSerializer.Serialize(new
            {
                Type = "ShopAction",
                Action = "OpenShop",
                ShopName = name
            })))
        {
            var responses = router.Handle(
                open.RootElement,
                session);
            Check(
                JsonSerializer.Serialize(responses)
                    .Contains("\"Type\":\"SyncShopBoard\""),
                $"{name} opens through router");
        }

        Check(
            playerShop.GetPublicState(
                player.CharacterId).IsOpen &&
            HasReservedShopRows(
                store,
                player),
            $"{name} is public and has durable reserved rows");
    }

    private static bool HasReservedShopRows(
        SqliteGameStore store,
        Player player)
    {
        var assets = store.GetCharacterAssets(
            player.AccountId,
            player.CharacterId);

        return assets.Inventory.Any(x =>
                   Convert.ToInt32(x["InShop"]) != 0) ||
               assets.Equips.Any(x =>
                   Convert.ToInt32(x["InShop"]) != 0) ||
               assets.Pets.Any(x =>
                   Convert.ToInt32(x["InShop"]) != 0) ||
               assets.HorseEquips.Any(x =>
                   Convert.ToInt32(x["InShop"]) != 0);
    }

    // The DB accepts 3..40 characters. A full 32-character GUID after
    // "identity_identity_a_" exceeded this and caused registration to fail
    // before the session cleanup scenario was ever exercised.
    private static string FixtureUsername(string suffix, string unique)
        => $"id_{suffix}_{unique}";

    private static void VerifyIdentityFixtureUsernames()
    {
        var first = FixtureUsername("identity_a", "00000000");
        var second = FixtureUsername("identity_b", "00000000");
        Check(
            new[] { first, second }.All(x => x.Length is >= 3 and <= 40) &&
            !string.Equals(first, second, StringComparison.OrdinalIgnoreCase),
            "identity-cleanup account fixtures meet server 40-character limit and remain unique");
    }

    private static Player CreatePlayer(
        SqliteGameStore store,
        string suffix)
    {
        var unique =
            Guid.NewGuid()
                .ToString("N")[..8];
        var username = FixtureUsername(suffix, unique);
        Check(username.Length is >= 3 and <= 40,
            $"identity-cleanup fixture account length is valid (suffix={suffix}, length={username.Length})");
        var password = "test-password";

        var registered = store.Register(
            username,
            password,
            "");
        Check(
            registered.Success,
            $"register identity-cleanup player {suffix} " +
            $"(username={username}, message={registered.Message})");

        var accountId = store.Login(
            username,
            password);
        Check(
            accountId.HasValue,
            $"login identity-cleanup fixture {suffix}");

        using var create = Json(
            JsonSerializer.Serialize(new
            {
                Name = ("I" + suffix)[..Math.Min(
                    12,
                    1 + suffix.Length)],
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
            $"create identity-cleanup character {suffix} (message={created.Message})");

        var dbid = store.Characters(
                accountId.Value)
            .Select(x =>
                Convert.ToInt64(x["DBID"]))
            .First(x => x > 0);

        return new Player(
            username,
            password,
            accountId.Value,
            dbid);
    }
}
