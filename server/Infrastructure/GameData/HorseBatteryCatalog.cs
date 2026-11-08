namespace SGO.Server.Infrastructure.GameData;

public sealed class HorseBatteryCatalog
{
    private readonly Dictionary<int, int> _energyByItem = new();

    public HorseBatteryCatalog(
        CurrentClientDataCatalog current)
    {
        foreach (var item in current.Items)
        {
            if (!item.Category.Equals(
                    "horsebattery",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            if (item.Battery > 0)
                _energyByItem[item.ItemId] = item.Battery;
        }
    }

    public int GetEnergy(int itemIndex)
        => _energyByItem.TryGetValue(itemIndex, out var value)
            ? value
            : 0;
}
