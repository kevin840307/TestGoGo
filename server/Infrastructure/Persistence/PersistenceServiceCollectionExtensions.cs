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

                services.AddSingleton<IGameStore>(sp =>
                    sp.GetRequiredService<SqliteGameStore>());
                services.AddSingleton<IAccountStore>(sp =>
                    sp.GetRequiredService<SqliteGameStore>());
                services.AddSingleton<ICharacterStore>(sp =>
                    sp.GetRequiredService<SqliteGameStore>());
                services.AddSingleton<IInventoryStore>(sp =>
                    sp.GetRequiredService<SqliteGameStore>());
                services.AddSingleton<IEquipmentStore>(sp =>
                    sp.GetRequiredService<SqliteGameStore>());
                services.AddSingleton<IPetStore>(sp =>
                    sp.GetRequiredService<SqliteGameStore>());
                services.AddSingleton<IMechaStore>(sp =>
                    sp.GetRequiredService<SqliteGameStore>());
                services.AddSingleton<ISkillStore>(sp =>
                    sp.GetRequiredService<SqliteGameStore>());
                services.AddSingleton<IShortcutStore>(sp =>
                    sp.GetRequiredService<SqliteGameStore>());
                services.AddSingleton<IBankStore>(sp =>
                    sp.GetRequiredService<SqliteGameStore>());
                services.AddSingleton<IShopStore>(sp =>
                    sp.GetRequiredService<SqliteGameStore>());
                services.AddSingleton<ICharacterAssetReader>(sp =>
                    sp.GetRequiredService<SqliteGameStore>());
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
}
