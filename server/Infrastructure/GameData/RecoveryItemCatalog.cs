namespace SGO.Server.Infrastructure.GameData;

public sealed record RecoveryItemEffect(
    int ItemIndex,
    int RecoverHp,
    int RecoverMp,
    int RecoverSp);

public sealed class RecoveryItemCatalog
{
    private readonly Dictionary<int, RecoveryItemEffect> _items = new();

    public RecoveryItemCatalog(
        CurrentClientDataCatalog current)
    {
        foreach (var item in current.Items)
        {
            if (!item.Category.Equals(
                    "recovery",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            _items[item.ItemId] = new RecoveryItemEffect(
                item.ItemId,
                item.RecoverHp,
                item.RecoverMp,
                item.RecoverSp);
        }
    }

    public RecoveryItemEffect? Find(int itemIndex)
        => _items.TryGetValue(itemIndex, out var value)
            ? value
            : null;
}
