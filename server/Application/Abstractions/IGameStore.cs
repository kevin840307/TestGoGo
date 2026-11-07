namespace SGO.Server.Application.Abstractions;

// Compatibility aggregate for code that still needs several persistence
// features at once. New feature services should inject the smallest port
// from GameStorePorts.cs instead of depending on this whole interface.
public interface IGameStore :
    IAccountStore,
    ICharacterStore,
    IInventoryStore,
    IEquipmentStore,
    IPetStore,
    IMechaStore,
    ISkillStore,
    IShortcutStore,
    IBankStore,
    IItemDiscardStore,
    IWorldGroundItemStore,
    IShopStore
{
}
