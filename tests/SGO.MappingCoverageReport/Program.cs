using System.Text.Json;
using SGO.Server.Infrastructure.GameData;

var repoRoot = FindRepoRoot(
    Directory.GetCurrentDirectory());
var output = ParseOutput(args);

var clientRoot =
    Path.Combine(repoRoot, "client");
var gameDataRoot =
    Path.Combine(repoRoot, "Old", "服務端", "Dat");

var current =
    new CurrentClientDataCatalog(clientRoot);
var legacy =
    new LegacyClientDataCatalog(clientRoot);
var shops =
    new ShopCatalog(
        gameDataRoot,
        current,
        legacy);
var drops =
    new EnemyDropCatalog(
        gameDataRoot,
        current);

var warningRows = shops.MappingWarnings
    .Select(ParseWarning)
    .ToArray();
var infoRows = shops.MappingInfo
    .Select(ParseInfo)
    .ToArray();

var knownDropIds =
    EnumerateLegacyDropIds(
        Path.Combine(
            gameDataRoot,
            "Enemy",
            "Enemy_Data.csv"))
    .Distinct()
    .OrderBy(x => x)
    .ToArray();

var dropRows = knownDropIds
    .Select(id =>
    {
        var mapped = drops.ResolveItem(id);
        return new
        {
            LegacyItemId = id,
            Status = mapped is null
                ? "UNMAPPED"
                : "RESOLVED",
            CurrentItemId =
                mapped?.ItemId,
            Name =
                mapped?.Name ?? "",
            Category =
                mapped?.Category ?? ""
        };
    })
    .ToArray();

var report = new
{
    GeneratedUtc = DateTimeOffset.UtcNow,
    CurrentItemCount =
        current.Items.Count,
    LegacyConfirmedItemNameCount =
        legacy.ConfirmedItemNameCount,
    Shop = new
    {
        InfoCount = infoRows.Length,
        WarningCount = warningRows.Length,
        SkippedCount = warningRows.Count(x =>
            x.Kind.StartsWith(
                "SKIPPED",
                StringComparison.Ordinal)),
        CollisionCount = warningRows.Count(x =>
            x.Kind ==
            "LEGACY_ID_COLLISION"),
        Info = infoRows,
        Warnings = warningRows
    },
    Drops = new
    {
        ReferencedLegacyItemCount =
            dropRows.Length,
        ResolvedCount =
            dropRows.Count(x =>
                x.Status ==
                "RESOLVED"),
        UnmappedCount =
            dropRows.Count(x =>
                x.Status ==
                "UNMAPPED"),
        Items = dropRows
    },
    Policy = new[]
    {
        "Legacy IDs are never trusted when the current item name/category disagrees.",
        "Name remaps are accepted only when the current catalog resolves an unambiguous compatible item.",
        "Unknown, missing-name and unresolved historical resources remain SKIPPED/UNMAPPED instead of being guessed."
    }
};

var json =
    JsonSerializer.Serialize(
        report,
        new JsonSerializerOptions
        {
            WriteIndented = true
        });

if (!string.IsNullOrWhiteSpace(output))
{
    var full =
        Path.GetFullPath(
            output,
            repoRoot);
    Directory.CreateDirectory(
        Path.GetDirectoryName(full)!);
    File.WriteAllText(
        full,
        json + Environment.NewLine);
}

Console.WriteLine(
    $"Mapping coverage: shop_info={infoRows.Length}, " +
    $"shop_warnings={warningRows.Length}, " +
    $"drop_resolved={dropRows.Count(x => x.Status == "RESOLVED")}, " +
    $"drop_unmapped={dropRows.Count(x => x.Status == "UNMAPPED")}");

foreach (var row in warningRows.Take(50))
{
    Console.WriteLine(
        $"SHOP {row.Kind}: {row.Raw}");
}

foreach (var row in dropRows
             .Where(x =>
                 x.Status ==
                 "UNMAPPED")
             .Take(50))
{
    Console.WriteLine(
        $"DROP UNMAPPED: LegacyItemID={row.LegacyItemId}");
}

static MappingRow ParseWarning(
    string value)
{
    var kind =
        value.Contains(
            "[SKIPPED][",
            StringComparison.Ordinal)
            ? "SKIPPED:" +
              Between(
                  value,
                  "[SKIPPED][",
                  "]")
            : value.Contains(
                "[LEGACY_ID_COLLISION]",
                StringComparison.Ordinal)
                ? "LEGACY_ID_COLLISION"
                : "WARNING";

    return new MappingRow(
        kind,
        value);
}

static MappingRow ParseInfo(
    string value)
{
    var kind =
        value.StartsWith(
            "[SAFE_SAME_ID]",
            StringComparison.Ordinal)
            ? "SAFE_SAME_ID"
            : value.StartsWith(
                "[SAFE_SAME_ID_NO_NAME]",
                StringComparison.Ordinal)
                ? "SAFE_SAME_ID_NO_NAME"
                : value.StartsWith(
                    "[NAME_REMAP]",
                    StringComparison.Ordinal)
                    ? "NAME_REMAP"
                    : "INFO";

    return new MappingRow(
        kind,
        value);
}

static string Between(
    string value,
    string start,
    string end)
{
    var from =
        value.IndexOf(
            start,
            StringComparison.Ordinal);
    if (from < 0)
        return "";

    from += start.Length;
    var to =
        value.IndexOf(
            end,
            from,
            StringComparison.Ordinal);
    return to < 0
        ? value[from..]
        : value[from..to];
}

static IEnumerable<int> EnumerateLegacyDropIds(
    string path)
{
    if (!File.Exists(path))
        yield break;

    System.Text.Encoding.RegisterProvider(
        System.Text.CodePagesEncodingProvider.Instance);
    var big5 =
        System.Text.Encoding.GetEncoding(950);

    const int firstDropColumn = 74;
    const int totalSlots = 60;

    foreach (var raw in File.ReadLines(
                 path,
                 big5))
    {
        if (string.IsNullOrWhiteSpace(raw))
            continue;

        var cols = raw.Split(',');
        for (var slot = 0;
             slot < totalSlots;
             slot++)
        {
            var column =
                firstDropColumn +
                slot * 2;
            if (column >= cols.Length)
                break;

            if (int.TryParse(
                    cols[column].Trim(),
                    out var id) &&
                id > 0)
            {
                yield return id;
            }
        }
    }
}

static string? ParseOutput(
    string[] args)
{
    for (var i = 0;
         i + 1 < args.Length;
         i++)
    {
        if (args[i] == "--output")
            return args[i + 1];
    }

    return null;
}

static string FindRepoRoot(
    string start)
{
    var current =
        new DirectoryInfo(
            Path.GetFullPath(start));

    while (current is not null)
    {
        if (Directory.Exists(
                Path.Combine(
                    current.FullName,
                    "server")) &&
            Directory.Exists(
                Path.Combine(
                    current.FullName,
                    "client")))
        {
            return current.FullName;
        }

        current =
            current.Parent;
    }

    throw new DirectoryNotFoundException(
        "Could not locate SGO repository root.");
}

internal sealed record MappingRow(
    string Kind,
    string Raw);
