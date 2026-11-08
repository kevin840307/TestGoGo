using System.Text.Json;
using SGO.Server.Application.Items;
using SGO.Server.Infrastructure.GameData;

var repoRoot =
    FindRepoRoot(
        Directory.GetCurrentDirectory());

var output = ParseOutput(args);
var strict = args.Contains(
    "--strict",
    StringComparer.Ordinal);
var current =
    new CurrentClientDataCatalog(
        Path.Combine(
            repoRoot,
            "client"));
var horseCards =
    new HorseCardCatalog(
        current);
var festival =
    new FestivalRewardCatalog(
        current.Root);

var rows = current.Items
    .Where(item =>
        item.Category.Equals(
            "general",
            StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(
            item.ScriptName) &&
        !item.ScriptName.Equals(
            "無",
            StringComparison.Ordinal))
    .OrderBy(item => item.ItemId)
    .Select(Classify)
    .ToArray();

var summary = rows
    .GroupBy(row => row.Status)
    .OrderBy(group => group.Key)
    .ToDictionary(
        group => group.Key,
        group => group.Count(),
        StringComparer.Ordinal);

var report = new
{
    GeneratedUtc =
        DateTimeOffset.UtcNow,
    TotalScriptedGeneralItems =
        rows.Length,
    Summary =
        summary,
    UnknownCount =
        rows.Count(x =>
            x.Status ==
            "UNKNOWN_SCRIPT_EFFECT"),
    ChoiceRequiredCount =
        rows.Count(x =>
            x.Status ==
            "CHOICE_REQUIRED"),
    Items =
        rows,
    Limits = new[]
    {
        "Static/data-driven coverage: a SUPPORTED classification does not replace gameplay E2E.",
        "CHOICE_REQUIRED means the item needs an explicit player-selection flow and is intentionally not guessed.",
        "UNKNOWN_SCRIPT_EFFECT must remain a visible gap; the server must not consume it with a fake success response."
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
    $"Scripted general coverage: total={rows.Length}, " +
    string.Join(
        ", ",
        summary.Select(x =>
            $"{x.Key}={x.Value}")));

foreach (var row in rows.Where(x =>
             x.Status is
                 "UNKNOWN_SCRIPT_EFFECT" or
                 "CHOICE_REQUIRED"))
{
    Console.WriteLine(
        $"{row.Status}: {row.ItemId} {row.Name} " +
        $"script={row.ScriptName}");
}

if (strict &&
    rows.Any(x =>
        x.Status ==
        "UNKNOWN_SCRIPT_EFFECT"))
{
    Console.Error.WriteLine(
        "FAIL: one or more scripted general items still have UNKNOWN_SCRIPT_EFFECT.");
    Environment.ExitCode = 1;
}

CoverageRow Classify(
    CurrentItemDefinition item)
{
    if (item.ItemId is
        2000 or
        2301 or
        2302 or
        2303 or
        2304 or
        2305 or
        2459 or
        2616 or
        2803)
    {
        return Supported(
            item,
            "explicit UseItemRequest implementation");
    }

    if (item.ItemId ==
        LanternTransformationCatalog.SourceItemId)
    {
        return Supported(
            item,
            "equipped-weapon lantern appearance transformation");
    }

    if (item.ItemId is
        TransformationCatalog.TransformScrollItemId or
        TransformationCatalog.RestoreScrollItemId)
    {
        return Supported(
            item,
            "monster transformation/restore implementation");
    }

    if (item.ItemId ==
        DollService.HappinessDollItemId)
    {
        return Supported(
            item,
            "happiness doll reward service");
    }

    if (festival.Supports(
            item.ItemId))
    {
        return Supported(
            item,
            "current Festival_Data random reward service");
    }

    if (FixedRewardBoxCatalog.Find(
            item.ItemId) is not null)
    {
        return Supported(
            item,
            "fixed reward box catalog");
    }

    if (RecoveredBoxCatalog.Resolve(
            current,
            item.ItemId) is not null)
    {
        return Supported(
            item,
            "unambiguous current-client reward description");
    }

    if (ChoiceRewardCatalog.Resolve(
            current,
            item.ItemId) is not null)
    {
        return Supported(
            item,
            "explicit player-choice reward flow");
    }

    if (horseCards.Find(
            item.ItemId) is not null)
    {
        return Supported(
            item,
            "mecha activation package catalog");
    }

    if (CosmeticTransformationCatalog.Supports(
            item.ItemId))
    {
        return Supported(
            item,
            "persistent appearance-only transformation");
    }

    if (current.ResolveSkillBook(
            item.ItemId) is not null)
    {
        return Supported(
            item,
            "skill-book resolver");
    }

    if (current.ResolveExperienceCard(
            item.ItemId) is not null)
    {
        return Supported(
            item,
            "timed experience-card resolver");
    }

    if (IsCraftingMaterial(
            item))
    {
        return new(
            item.ItemId,
            item.Name,
            item.ScriptName,
            "NONCONSUMABLE_CRAFTING",
            "description identifies the item as crafting material/blueprint");
    }

    if (NeedsChoice(
            item))
    {
        return new(
            item.ItemId,
            item.Name,
            item.ScriptName,
            "CHOICE_REQUIRED",
            "description requires a player/random choice; server must not guess");
    }

    return new(
        item.ItemId,
        item.Name,
        item.ScriptName,
        "UNKNOWN_SCRIPT_EFFECT",
        "no current safe implementation/resolver matched");
}

static CoverageRow Supported(
    CurrentItemDefinition item,
    string reason)
    => new(
        item.ItemId,
        item.Name,
        item.ScriptName,
        "SUPPORTED",
        reason);

static bool IsCraftingMaterial(
    CurrentItemDefinition item)
    => item.Description.Contains(
           "合成",
           StringComparison.Ordinal) &&
       item.Description.Contains(
           "材料",
           StringComparison.Ordinal);

static bool NeedsChoice(
    CurrentItemDefinition item)
{
    var text =
        item.Description;
    return new[]
    {
        "自由選擇",
        "任君挑選",
        "選擇",
        "其中",
        "任一",
        "一款",
        "隨機"
    }.Any(marker =>
        text.Contains(
            marker,
            StringComparison.Ordinal));
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

internal sealed record CoverageRow(
    int ItemId,
    string Name,
    string ScriptName,
    string Status,
    string Reason);
