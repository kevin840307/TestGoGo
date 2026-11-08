using System.Text.Json;
using SGO.Server.Application;
using SGO.Server.Application.Items;
using SGO.Server.Domain.Sessions;
using SGO.Server.Infrastructure.GameData;
using SGO.Server.Infrastructure.Persistence.Sqlite;

using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Integration;

internal static class LanternTransformationSmokeTests
{
    public static void Run(
        PacketRouter router,
        SqliteGameStore store,
        CurrentClientDataCatalog current)
    {
        var username =
            $"lantern-{Guid.NewGuid():N}";
        var registered =
            store.Register(
                username,
                "test-password",
                "");

        Check(
            registered.Success,
            "register isolated lantern transformation account");

        var accountId =
            store.Login(
                username,
                "test-password");

        Check(
            accountId.HasValue,
            "login isolated lantern transformation account");

        using var create = Json(
            JsonSerializer.Serialize(new
            {
                Name = "LanternTest",
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
                create.RootElement).Success,
            "create isolated lantern transformation character");

        var characterId =
            store.Characters(
                    accountId.Value)
                .Select(x =>
                    Convert.ToInt64(
                        x["DBID"]))
                .First(x => x > 0);

        var weaponUid =
            store.AddEquipment(
                accountId.Value,
                characterId,
                31548,
                5,
                0,
                0);
        var sourceUid =
            store.AddInventoryItem(
                accountId.Value,
                characterId,
                LanternTransformationCatalog.SourceItemId,
                1);

        Check(
            weaponUid > 0 &&
            sourceUid > 0,
            "seed equipped sword and lantern transformer");

        var session =
            new ClientSession
            {
                ConnectionId =
                    Guid.NewGuid()
            };

        try
        {
            using (var login = Json(
                JsonSerializer.Serialize(new
                {
                    Type = "LoginRequest",
                    User = username,
                    Pass = "test-password"
                })))
            {
                var packets =
                    router.Handle(
                        login.RootElement,
                        session);

                Check(
                    session.AccountId ==
                        accountId &&
                    packets.Any(x =>
                        JsonSerializer.Serialize(x)
                            .Contains(
                                ""Success":true")),
                    "isolated lantern session logs in through packet router");
            }

            using (var select = Json(
                JsonSerializer.Serialize(new
                {
                    Type =
                        "SelectCharacterRequest",
                    DBID =
                        characterId
                })))
            {
                router.Handle(
                    select.RootElement,
                    session);

                Check(
                    session.CharacterId ==
                        characterId,
                    "isolated lantern session selects character");
            }

            var beforeAssets =
                store.GetCharacterAssets(
                    accountId.Value,
                    characterId);
            var beforeWeapon =
                beforeAssets.Equips.Single(x =>
                    Convert.ToInt64(
                        x["ItemID"]) ==
                    weaponUid);

            Check(
                Convert.ToInt32(
                    beforeWeapon["ItemIdx"]) ==
                    31548 &&
                Convert.ToInt32(
                    beforeWeapon["Part"]) ==
                    5,
                "real weapon is equipped in appearance slot 5 before lantern transform");

            using (var use = Json(
                JsonSerializer.Serialize(new
                {
                    Type =
                        "UseItemRequest",
                    ItemUID =
                        sourceUid
                })))
            {
                var packets =
                    router.Handle(
                        use.RootElement,
                        session);
                var json =
                    JsonSerializer.Serialize(
                        packets);

                Check(
                    json.Contains(
                        ""Type":"UpdateAppearance"") &&
                    json.Contains(
                        ""Weapon":31639") &&
                    json.Contains(
                        "已套用燈籠武器外觀"),
                    "lantern transformer broadcasts melee lantern appearance through normal router path");
            }

            var afterAssets =
                store.GetCharacterAssets(
                    accountId.Value,
                    characterId);
            var overrides =
                store.GetCosmeticOverrides(
                    accountId.Value,
                    characterId);

            Check(
                !afterAssets.Inventory.Any(x =>
                    Convert.ToInt64(
                        x["ItemUID"]) ==
                    sourceUid) &&
                afterAssets.Equips.Any(x =>
                    Convert.ToInt64(
                        x["ItemID"]) ==
                        weaponUid &&
                    Convert.ToInt32(
                        x["ItemIdx"]) ==
                        31548 &&
                    Convert.ToInt32(
                        x["Part"]) ==
                        5) &&
                overrides.Any(x =>
                    x.Part == 5 &&
                    x.ItemIndex ==
                        LanternTransformationCatalog.MeleeLanternItemId),
                "lantern transform consumes source while preserving the authoritative real weapon");

            using (var replay = Json(
                JsonSerializer.Serialize(new
                {
                    Type =
                        "UseItemRequest",
                    ItemUID =
                        sourceUid
                })))
            {
                var packets =
                    router.Handle(
                        replay.RootElement,
                        session);

                Check(
                    packets.Any(x =>
                        JsonSerializer.Serialize(x)
                            .Contains(
                                "找不到物品")) &&
                    store.GetCosmeticOverrides(
                            accountId.Value,
                            characterId)
                        .Count(x =>
                            x.Part == 5) ==
                        1,
                    "replaying consumed lantern source cannot duplicate cosmetic mutation");
            }
        }
        finally
        {
            router.DisconnectPresence(
                session);
        }
    }
}
