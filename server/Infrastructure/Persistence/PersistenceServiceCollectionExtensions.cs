using SGO.Server.Application.Abstractions;
using SGO.Server.Infrastructure.Persistence.Sqlite;

namespace SGO.Server.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddGamePersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"]?.Trim()
                       ?? "Sqlite";

        switch (provider.ToLowerInvariant())
        {
            case "sqlite":
            {
                var dbPath = configuration["Database:Sqlite:Path"];
                if (string.IsNullOrWhiteSpace(dbPath))
                {
                    dbPath = Path.Combine(
                        AppContext.BaseDirectory,
                        "Data",
                        "game.db");
                }

                services.AddSingleton(
                    new SqliteGameStore(dbPath));
                RegisterStorePorts<SqliteGameStore>(services);
                break;
            }

            case "mysql":
                throw new InvalidOperationException(
                    "Database provider 'MySql' is not installed yet. " +
                    "Implement MySqlGameStore : IGameStore and register it here.");

            default:
                throw new InvalidOperationException(
                    $"Unsupported database provider: {provider}");
        }

        return services;
    }

    private static void RegisterStorePorts<TStore>(
        IServiceCollection services)
        where TStore : class, IGameStore
    {
        services.AddSingleton<IGameStore>(sp =>
            sp.GetRequiredService<TStore>());
        services.AddSingleton<IAccountStore>(sp =>
            sp.GetRequiredService<TStore>());
        services.AddSingleton<ICharacterStore>(sp =>
            sp.GetRequiredService<TStore>());
        services.AddSingleton<IInventoryStore>(sp =>
            sp.GetRequiredService<TStore>());
        services.AddSingleton<IEquipmentStore>(sp =>
            sp.GetRequiredService<TStore>());
        services.AddSingleton<IPetStore>(sp =>
            sp.GetRequiredService<TStore>());
        services.AddSingleton<IMechaStore>(sp =>
            sp.GetRequiredService<TStore>());
        services.AddSingleton<ISkillStore>(sp =>
            sp.GetRequiredService<TStore>());
        services.AddSingleton<IShortcutStore>(sp =>
            sp.GetRequiredService<TStore>());
        services.AddSingleton<IBankStore>(sp =>
            sp.GetRequiredService<TStore>());
        services.AddSingleton<IRepairStore>(sp =>
            sp.GetRequiredService<TStore>());
        services.AddSingleton<IItemDiscardStore>(sp =>
            sp.GetRequiredService<TStore>());
        services.AddSingleton<IShopStore>(sp =>
            sp.GetRequiredService<TStore>());
        services.AddSingleton<ICharacterAssetReader>(sp =>
            sp.GetRequiredService<TStore>());
    }
}
