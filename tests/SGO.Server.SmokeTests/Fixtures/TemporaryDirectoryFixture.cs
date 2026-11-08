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
            throw new ArgumentException("A directory prefix is required.", nameof(prefix));
        Root = Path.Combine(
            Path.GetTempPath(),
            prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}
