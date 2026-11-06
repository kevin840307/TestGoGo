using System.Text;

namespace SGO.Server.Infrastructure.GameData;

public sealed record HorseCardDefinition(
    int ItemIndex,
    string Name,
    int MaxBattery,
    string HorseType);

public sealed class HorseCardCatalog
{
    private readonly Dictionary<int, HorseCardDefinition> _cards = new();

    public HorseCardCatalog(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (!File.Exists(path)) return;

        foreach (var line in File.ReadLines(path, Encoding.GetEncoding(950)))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = line.Split(',');
            if (cols.Length <= 29) continue;
            if (!int.TryParse(cols[0].Trim(), out var itemIndex)) continue;

            var name = cols[1].Trim();
            var maxBattery =
                int.TryParse(cols[28].Trim(), out var battery)
                    ? Math.Max(0, battery)
                    : 0;

            // Source value 0 is the normal mechanical/mecha type. Other values
            // are kept as HS so the recovered client can distinguish forms.
            var rawType =
                int.TryParse(cols[29].Trim(), out var t) ? t : 0;
            var horseType = rawType == 0 ? "MS" : "HS";

            _cards[itemIndex] = new HorseCardDefinition(
                itemIndex,
                string.IsNullOrWhiteSpace(name) ? $"Mecha {itemIndex}" : name,
                maxBattery,
                horseType);
        }
    }

    public HorseCardDefinition? Find(int itemIndex)
        => _cards.TryGetValue(itemIndex, out var value) ? value : null;
}
