using System.Text;

namespace SGO.Server.Infrastructure.GameData;

public sealed class HorseBatteryCatalog
{
    private readonly Dictionary<int, int> _energyByItem = new();

    public HorseBatteryCatalog(string csvPath)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (!File.Exists(csvPath)) return;

        var encoding = Encoding.GetEncoding(950);
        foreach (var line in File.ReadLines(csvPath, encoding))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = line.Split(',');
            if (cols.Length < 2) continue;
            if (!int.TryParse(cols[0].Trim(), out var itemIndex)) continue;

            // HorseBattery.csv stores usable battery duration in the
            // final numeric column (seconds).
            var energy = 0;
            for (var i = cols.Length - 1; i >= 1; i--)
            {
                if (int.TryParse(cols[i].Trim(), out var value))
                {
                    energy = value;
                    break;
                }
            }

            if (energy > 0)
                _energyByItem[itemIndex] = energy;
        }
    }

    public int GetEnergy(int itemIndex)
        => _energyByItem.TryGetValue(itemIndex, out var value)
            ? value
            : 0;
}
