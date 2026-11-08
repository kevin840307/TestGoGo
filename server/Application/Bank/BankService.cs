using SGO.Server.Application.Abstractions;
using SGO.Server.Domain.Assets;

namespace SGO.Server.Application.Bank;

public sealed class BankService
{
    private readonly IBankStore _bank;

    public BankService(IBankStore bank)
    {
        _bank = bank;
    }

    public IReadOnlyList<object> Transfer(
        long accountId,
        long characterId,
        long uid,
        int tabType,
        int amount,
        bool toBank)
    {
        var result = _bank.BankTransfer(
            accountId,
            characterId,
            uid,
            tabType,
            amount,
            toBank);

        var assets = _bank.GetCharacterAssets(
            accountId,
            characterId);

        var packets = new List<object>
        {
            new
            {
                Type = "SystemMessageFlash",
                Message = result.Message
            }
        };

        switch (tabType)
        {
            case 0:
                packets.Add(new
                {
                    Type = "InventorySync",
                    Items = assets.Inventory,
                    MaxSize = assets.InventoryMaxSize
                });
                break;

            case 1:
                packets.Add(new
                {
                    Type = "EquipSync",
                    Equips = assets.Equips,
                    MaxSize = assets.EquipMaxSize
                });
                break;

            case 3:
                packets.Add(new
                {
                    Type = "HorseSync",
                    Horses = assets.Horses,
                    HorseEquips = assets.HorseEquips,
                    MaxHorseEquipSize = assets.HorseEquipMaxSize
                });
                break;
        }

        return packets;
    }
}
