using System.Text;

namespace SGO.Server.Infrastructure.GameData;

public sealed record EnemyDefinition(
    int EnemyId,
    string Name,
    int Level,
    string Sprite,
    int MaxHp);

public sealed class EnemyCatalog
{
    private readonly Dictionary<int, EnemyDefinition> _items = new();

    public EnemyCatalog(string csvPath)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (!File.Exists(csvPath)) return;

        var encoding = Encoding.GetEncoding(950);
        foreach (var line in File.ReadLines(csvPath, encoding))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = line.Split(',');
            if (cols.Length < 10) continue;
            if (!int.TryParse(cols[0], out var id)) continue;

            var name = cols[2].Trim();
            var level = int.TryParse(cols[3], out var lv) ? lv : 1;
            var sprite = cols[6].Trim();
            var maxHp = int.TryParse(cols[9], out var hp) ? hp : 1;

            _items[id] = new EnemyDefinition(
                id,
                string.IsNullOrWhiteSpace(name) ? $"Enemy{id}" : name,
                level,
                sprite,
                Math.Max(1, maxHp));
        }
    }

    public EnemyDefinition? Find(int enemyId)
        => _items.TryGetValue(enemyId, out var value) ? value : null;
}
