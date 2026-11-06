namespace SGO.Server.Infrastructure.GameData;

public sealed record HorseCardDefinition(
    int ItemIndex,
    string Name,
    int MaxBattery,
    string HorseType);

public sealed class HorseCardCatalog
{
    private readonly Dictionary<int, HorseCardDefinition> _cards = new();

    public HorseCardCatalog(
        CurrentClientDataCatalog current)
    {
        foreach (var item in current.Items)
        {
            if (!item.Category.Equals(
                    "horse",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            _cards[item.ItemId] = new HorseCardDefinition(
                item.ItemId,
                string.IsNullOrWhiteSpace(item.Name)
                    ? $"Mecha {item.ItemId}"
                    : item.Name,
                Math.Max(0, item.MaxBattery),
                string.IsNullOrWhiteSpace(item.HorseType)
                    ? "MS"
                    : item.HorseType);
        }
    }

    public HorseCardDefinition? Find(int itemIndex)
        => _cards.TryGetValue(itemIndex, out var value)
            ? value
            : null;
}
