using Microsoft.Data.Sqlite;

namespace SGO.Server.SmokeTests.Fixtures;

/// <summary>
/// Owns the test's temporary directory and deletes it even if an assertion
/// fails. Keep game fixture lifecycle independent between test suites.
/// </summary>
internal sealed class TemporaryDirectoryFixture : IDisposable
{
    public string Root { get; }

    public TemporaryDirectoryFixture(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException(
                "A directory prefix is required.",
                nameof(prefix));

        Root = Path.Combine(
            Path.GetTempPath(),
            prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public void Dispose()
    {
        if (!Directory.Exists(Root))
            return;

        // Microsoft.Data.Sqlite pools closed connections by default. On
        // Windows an idle pooled handle can keep game.db/WAL/SHM locked even
        // though every test command/connection was correctly disposed.
        // This is test-fixture cleanup only; production pooling remains
        // enabled for normal server performance.
        SqliteConnection.ClearAllPools();

        IOException? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(
                    Root,
                    recursive: true);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
                Thread.Sleep(25 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(25 * (attempt + 1));
            }
        }

        throw new IOException(
            $"Failed to clean temporary test directory after clearing SQLite pools: {Root}",
            lastError);
    }
}
