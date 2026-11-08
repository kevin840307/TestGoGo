using SGO.Server.Application.Bank;
using SGO.Server.Application.Character;
using SGO.Server.Application.Club;
using SGO.Server.Application.Combat;
using SGO.Server.Application.Crafting;
using SGO.Server.Application.Gm;
using SGO.Server.Application.Items;
using SGO.Server.Application.Messaging;
using SGO.Server.Application.Mall;
using SGO.Server.Application.Packets;
using SGO.Server.Application.Party;
using SGO.Server.Application.Rank;
using SGO.Server.Application.Quest;
using SGO.Server.Application.Shops;
using SGO.Server.Application.Trade;
using SGO.Server.Application.World;

namespace SGO.Server.Application;

public static class GameFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddGameFeatures(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<WorldRuntimeService>();
        services.AddSingleton<SharedMapAiTicker>();
        services.AddHostedService(sp => sp.GetRequiredService<SharedMapAiTicker>());
        services.AddSingleton<PetEvolutionService>();
        services.AddSingleton<CombatService>();
        services.AddSingleton<CraftingService>();
        services.AddSingleton<ClubService>();
        services.AddSingleton<ResourceRecoveryService>();
        services.AddSingleton<ServerBroadcastHub>();
        services.AddSingleton<AccountSessionRegistry>();
        services.AddSingleton<IPayPalDonationProvider, NullPayPalDonationProvider>();
        services.AddSingleton<ILegacyPointProvider, NullLegacyPointProvider>();
        services.AddSingleton<MallService>();
        services.AddSingleton<PayPalWebhookService>();
        services.AddSingleton<PartyService>();
        services.AddSingleton<RankService>();
        services.AddSingleton<QuestProgressService>();
        services.AddSingleton<TradeService>();
        services.AddSingleton<DollService>();
        services.AddSingleton<ShopService>();
        services.AddSingleton<PlayerShopService>();
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
