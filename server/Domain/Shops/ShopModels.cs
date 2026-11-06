namespace SGO.Server.Domain.Shops;

public sealed record ShopItemDefinition(
    int ShopId,
    int ItemIndex,
    int TabType,
    int Price,
    bool CanSell,
    string Name);

public sealed record ShopMutationResult(
    bool Success,
    string Message,
    int Money);
