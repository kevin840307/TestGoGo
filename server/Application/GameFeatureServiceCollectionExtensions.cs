using SGO.Server.Application.Bank;
using SGO.Server.Application.Character;
using SGO.Server.Application.Combat;
using SGO.Server.Application.Gm;
using SGO.Server.Application.Items;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.Packets;
using SGO.Server.Application.Shops;

namespace SGO.Server.Application;

public static class GameFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddGameFeatures(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<CombatService>();
        services.AddSingleton<ResourceRecoveryService>();
        services.AddSingleton<ServerBroadcastHub>();
        services.AddSingleton<DollService>();
        services.AddSingleton<ShopService>();
        services.AddSingleton<GmCommandService>();

        if (IsEnabled(configuration, "Bank", defaultValue: true))
        {
            services.AddSingleton<BankService>();
            services.AddSingleton<IClientPacketHandler, BankPacketHandler>();
        }

        services.AddSingleton<PacketRouter>();
        return services;
    }

    private static bool IsEnabled(
        IConfiguration configuration,
        string feature,
        bool defaultValue)
    {
        var raw = configuration[$"Features:{feature}"];
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        return bool.TryParse(raw, out var enabled)
            ? enabled
            : defaultValue;
    }
}
