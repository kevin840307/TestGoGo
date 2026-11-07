using System.Text;

namespace SGO.Server.Infrastructure.GameData;

public sealed class LegacyClientDataCatalog
{
    private readonly Dictionary<(string Category, int Id), string> _itemNames =
        new();

    private readonly Dictionary<int, string> _skillNames =
        new();

    public LegacyClientDataCatalog(string root)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        LoadCsvNames(
            Path.Combine(root, "MS", "HorseMA.csv"),
            "horse");
        LoadCsvNames(
            Path.Combine(root, "MS", "HorseEquipMA.csv"),
            "horseequip");
        LoadCsvNames(
            Path.Combine(root, "MS", "HorseBattery.csv"),
            "horsebattery");

        LoadSkillNames(
            Path.Combine(root, "Interface", "skill_cht.dat"));
    }

    public LegacyClientDataCatalog(
        IEnumerable<(string Category, int Id, string Name)> items,
        IEnumerable<(int Id, string Name)> skills)
    {
        foreach (var item in items)
        {
            if (item.Id > 0 &&
                !string.IsNullOrWhiteSpace(item.Name))
            {
                _itemNames[
                    (NormalizeCategory(item.Category), item.Id)] =
                    item.Name.Trim();
            }
        }

        foreach (var skill in skills)
        {
            if (skill.Id > 0 &&
                !string.IsNullOrWhiteSpace(skill.Name))
            {
                _skillNames[skill.Id] = skill.Name.Trim();
            }
        }
    }

    public string? FindItemName(
        int id,
        string category)
        => _itemNames.TryGetValue(
                (NormalizeCategory(category), id),
                out var name)
            ? name
            : null;

    public string? FindSkillName(int id)
        => _skillNames.TryGetValue(id, out var name)
            ? name
            : null;

    public int ConfirmedItemNameCount => _itemNames.Count;
    public int ConfirmedSkillNameCount => _skillNames.Count;

    private void LoadCsvNames(
        string path,
        string category)
    {
        if (!File.Exists(path))
            return;

        foreach (var raw in File.ReadLines(
                     path,
                     Encoding.GetEncoding(950)))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var cols = raw.Split(',');
            if (cols.Length < 2 ||
                !int.TryParse(cols[0].Trim(), out var id))
                continue;

            var name = cols[1].Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            _itemNames[
                (NormalizeCategory(category), id)] = name;
        }
    }

    private void LoadSkillNames(string path)
    {
        if (!File.Exists(path))
            return;

        foreach (var raw in File.ReadLines(
                     path,
                     Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var cols = raw.Split(',');
            if (cols.Length < 2 ||
                !int.TryParse(cols[0].Trim(), out var id))
                continue;

            var name = cols[1].Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            _skillNames[id] = name;
        }
    }

    private static string NormalizeCategory(string category)
        => (category ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
}
