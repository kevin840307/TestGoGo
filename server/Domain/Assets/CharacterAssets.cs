namespace SGO.Server.Domain.Assets;

public sealed class CharacterAssets
{
    public List<Dictionary<string, object>> Inventory { get; } = new();
    public List<Dictionary<string, object>> Equips { get; } = new();
    public List<Dictionary<string, object>> Pets { get; } = new();
    public List<Dictionary<string, object>> Horses { get; } = new();
    public List<Dictionary<string, object>> HorseEquips { get; } = new();

    public int InventoryMaxSize { get; init; } = 200;
    public int EquipMaxSize { get; init; } = 200;
    public int PetMaxSize { get; init; } = 50;
    public int PetSummonMax { get; init; } = 1;
    public int HorseEquipMaxSize { get; init; } = 100;
}
