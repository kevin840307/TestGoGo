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

                services.AddSingleton<IGameStore>(
                    new SqliteGameStore(dbPath));
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
