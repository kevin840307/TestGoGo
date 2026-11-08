namespace SGO.Server.Infrastructure.GameData;

public sealed record HorseCardDefinition(
    int SourceItemIndex,
    int HorseIndex,
    string Name,
    int MaxBattery,
    string HorseType,
    int BodyId = 0,
    int WeaponId = 0,
    int EngineId = 0);

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

        // Current-client wasp packages are general items whose description
        // contains one mecha body plus the level-specific body/weapon/engine.
        // IDs are contiguous in the recovered client data:
        // body 50311..50318, engines 50335..50342, and heavy/magic weapons
        // 50319..50334 in alternating pairs.
        AddWaspPackage(
            current,
            2422,
            50011,
            levelIndex: 0,
            magic: false);
        AddWaspPackage(
            current,
            2423,
            50012,
            levelIndex: 0,
            magic: true);

        for (var levelIndex = 1;
             levelIndex <= 7;
             levelIndex++)
        {
            var heavySource =
                2399 + levelIndex * 2;
            var magicSource =
                heavySource + 1;

            AddWaspPackage(
                current,
                heavySource,
                50011,
                levelIndex,
                magic: false);
            AddWaspPackage(
                current,
                magicSource,
                50012,
                levelIndex,
                magic: true);
        }
    }

    private void AddWaspPackage(
        CurrentClientDataCatalog current,
        int sourceItemIndex,
        int horseIndex,
        int levelIndex,
        bool magic)
    {
        var source =
            current.FindItem(
                sourceItemIndex);
        var horse =
            current.FindItem(
                horseIndex);

        if (source is null ||
            horse is null ||
            !source.Category.Equals(
                "general",
                StringComparison.OrdinalIgnoreCase) ||
            !horse.Category.Equals(
                "horse",
                StringComparison.OrdinalIgnoreCase) ||
            !source.Name.Contains(
                "黃蜂",
                StringComparison.Ordinal) ||
            levelIndex is < 0 or > 7)
        {
            return;
        }

        var bodyId =
            50311 + levelIndex;
        var weaponId =
            levelIndex == 0
                ? (magic ? 50320 : 50319)
                : 50319 +
                  levelIndex * 2 +
                  (magic ? 1 : 0);
        var engineId =
            50335 + levelIndex;

        var body =
            current.FindItem(bodyId);
        var weapon =
            current.FindItem(weaponId);
        var engine =
            current.FindItem(engineId);

        if (body is null ||
            weapon is null ||
            engine is null ||
            !body.Category.Equals(
                "horseequip",
                StringComparison.OrdinalIgnoreCase) ||
            !weapon.Category.Equals(
                "horseequip",
                StringComparison.OrdinalIgnoreCase) ||
            !engine.Category.Equals(
                "horseequip",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _cards[sourceItemIndex] =
            new HorseCardDefinition(
                sourceItemIndex,
                horseIndex,
                horse.Name,
                Math.Max(
                    0,
                    horse.MaxBattery),
                string.IsNullOrWhiteSpace(
                    horse.HorseType)
                    ? "MA"
                    : horse.HorseType,
                bodyId,
                weaponId,
                engineId);
    }

    public HorseCardDefinition? Find(int itemIndex)
        => _cards.TryGetValue(itemIndex, out var value)
            ? value
            : null;
}
