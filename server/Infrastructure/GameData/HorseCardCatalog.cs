namespace SGO.Server.Infrastructure.GameData;

public sealed record HorseCardDefinition(
    int SourceItemIndex,
    int HorseIndex,
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
                item.ItemId,
                string.IsNullOrWhiteSpace(item.Name)
                    ? $"Mecha {item.ItemId}"
                    : item.Name,
                Math.Max(0, item.MaxBattery),
                string.IsNullOrWhiteSpace(item.HorseType)
                    ? "MS"
                    : item.HorseType);
        }

        // Recovered current-client data has one general-item summon card
        // whose source ItemID is intentionally different from the mecha ID:
        // 2418 流星猛獅召喚卡 -> 50015 流星機動猛獅.
        var lionSummonCard = current.FindItem(2418);
        var lion = current.FindItem(50015);
        if (lionSummonCard is not null &&
            lion is not null &&
            lionSummonCard.Category.Equals(
                "general",
                StringComparison.OrdinalIgnoreCase) &&
            lion.Category.Equals(
                "horse",
                StringComparison.OrdinalIgnoreCase) &&
            lionSummonCard.Name.Contains(
                "流星猛獅召喚卡",
                StringComparison.Ordinal) &&
            lion.Name.Contains(
                "流星機動猛獅",
                StringComparison.Ordinal))
        {
            _cards[lionSummonCard.ItemId] =
                new HorseCardDefinition(
                    lionSummonCard.ItemId,
                    lion.ItemId,
                    lion.Name,
                    Math.Max(0, lion.MaxBattery),
                    string.IsNullOrWhiteSpace(
                        lion.HorseType)
                        ? "MA"
                        : lion.HorseType);
        }
    }

    public HorseCardDefinition? Find(int itemIndex)
        => _cards.TryGetValue(itemIndex, out var value)
            ? value
            : null;
}
