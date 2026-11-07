namespace SGO.Server.Domain.Assets;

public enum AssetPurchaseKind
{
    General = 0,
    Equipment = 1,
    Pet = 2,
    Horse = 3,
    HorseEquipment = 4,
    HorseBattery = 5
}

public sealed record AssetPurchaseResult(
    bool Success,
    string Message,
    long CreatedUid,
    long Money);
