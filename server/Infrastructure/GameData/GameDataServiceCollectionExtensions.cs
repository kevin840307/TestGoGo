using SGO.Server.Infrastructure.Quest;
using SGO.Server.Infrastructure.World;

namespace SGO.Server.Infrastructure.GameData;

public static class GameDataServiceCollectionExtensions
{
    public static IServiceCollection AddGameData(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var gameDataRoot = Path.Combine(
            AppContext.BaseDirectory,
            "GameData");

        var currentClientRoot =
            configuration["Game:ClientRoot"]
            ?? Environment.GetEnvironmentVariable("SGO_CLIENT_ROOT");

        var legacyClientRoot =
            configuration["Game:LegacyClientDataRoot"]
            ?? Environment.GetEnvironmentVariable("SGO_LEGACY_CLIENT_ROOT")
            ?? @"C:\SGO\Recovered\Old\ClientData";

        services.AddSingleton(
            new CurrentClientDataCatalog(currentClientRoot));
        services.AddSingleton(
            new LegacyClientDataCatalog(legacyClientRoot));
        services.AddSingleton(
            new LegacyNpcVisualCatalog(legacyClientRoot));
        services.AddSingleton(
            new LegacyPlayerGrowthCatalog(gameDataRoot));

        services.AddSingleton(sp =>
            new WorldDataService(
                gameDataRoot,
                sp.GetRequiredService<LegacyNpcVisualCatalog>()));

        services.AddSingleton(sp =>
            new ShopCatalog(
                gameDataRoot,
                sp.GetRequiredService<CurrentClientDataCatalog>(),
                sp.GetRequiredService<LegacyClientDataCatalog>()));

        services.AddSingleton(sp =>
            new HorseBatteryCatalog(
                sp.GetRequiredService<CurrentClientDataCatalog>()));
        services.AddSingleton(sp =>
            new RecoveryItemCatalog(
                sp.GetRequiredService<CurrentClientDataCatalog>()));
        services.AddSingleton(sp =>
            new HorseCardCatalog(
                sp.GetRequiredService<CurrentClientDataCatalog>()));
        services.AddSingleton(sp =>
            new SkillCatalog(
                sp.GetRequiredService<CurrentClientDataCatalog>()));

        services.AddSingleton(sp =>
            new QuestRuntime(
                Path.Combine(gameDataRoot, "Quest"),
                sp.GetRequiredService<ShopCatalog>()));

        return services;
    }
}
