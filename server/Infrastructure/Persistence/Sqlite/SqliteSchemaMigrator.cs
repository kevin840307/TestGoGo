using Microsoft.Data.Sqlite;

namespace SGO.Server.Infrastructure.Persistence.Sqlite;

internal static class SqliteSchemaMigrator
{
    private static readonly string[] InitScripts =
    [
        "001_schema.sql",
        "002_indexes.sql"
    ];

    public static void Apply(SqliteConnection db)
    {
        foreach (var file in InitScripts)
            ExecuteSqlFile(db, ResolveSqlPath(file));

        ApplyEnsureColumnDirectives(
            db,
            ResolveSqlPath("003_character_columns.sql"));

        ApplyEnsureColumnDirectives(
            db,
            ResolveSqlPath("004_ground_item_lifecycle.sql"));

        ExecuteSqlFile(
            db,
            ResolveSqlPath("005_rank.sql"));

        ExecuteSqlFile(
            db,
            ResolveSqlPath("006_club.sql"));
    }

    private static void ExecuteSqlFile(
        SqliteConnection db,
        string path)
    {
        var sql = File.ReadAllText(path);
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void ApplyEnsureColumnDirectives(
        SqliteConnection db,
        string path)
    {
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            const string prefix = "-- @ensure-column ";

            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var payload = line[prefix.Length..];
            var firstSpace = payload.IndexOf(' ');
            if (firstSpace <= 0)
                throw new InvalidDataException(
                    $"Invalid ensure-column directive: {line}");

            var table = payload[..firstSpace].Trim();
            var rest = payload[(firstSpace + 1)..].Trim();
            var secondSpace = rest.IndexOf(' ');

            if (secondSpace <= 0)
                throw new InvalidDataException(
                    $"Invalid ensure-column directive: {line}");

            var column = rest[..secondSpace].Trim();
            var definition = rest[(secondSpace + 1)..].Trim();

            // Ignore documentation/examples accidentally matching the directive
            // prefix. Only simple SQLite identifiers are accepted here.
            if (!IsSafeIdentifier(table) || !IsSafeIdentifier(column))
                continue;

            if (ColumnExists(db, table, column))
                continue;

            using var alter = db.CreateCommand();
            alter.CommandText =
                $"ALTER TABLE [{table}] ADD COLUMN [{column}] {definition};";
            alter.ExecuteNonQuery();
        }
    }

    private static bool IsSafeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var ch in value)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                return false;
        }

        return true;
    }

    private static bool ColumnExists(
        SqliteConnection db,
        string table,
        string column)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info([{table}]);";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(
                    reader.GetString(1),
                    column,
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string ResolveSqlPath(string file)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Database",
            "Sqlite",
            file);

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"SQLite initialization SQL not found: {path}",
                path);

        return path;
    }
}
