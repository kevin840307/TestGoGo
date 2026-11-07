using SGO.Server.Application.Abstractions;
using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Items;

public sealed class DollService
{
    public const int HappinessDollItemId = 2126;

    // Mirrors client/scripts/game_info_window.gd DOLL_INFOS for 幸福娃娃.
    // The recovered client exposes no per-item weights, so currently every
    // available reward is equally likely.
    private static readonly int[] HappinessRewards =
    [
        1011, 1012, 1013, 50101, 50102, 50103, 2615, 2616,
        19179, 19178, 19183, 19180, 19059, 19109, 19110, 19112,
        19003, 19008, 19094, 19093, 19111, 18101, 19019, 18029,
        18030, 18031, 18032, 18037, 18038, 18039, 19085, 19086,
        19087, 19088, 2130, 2128, 2129, 19061, 19062, 19063,
        19064, 19065, 19066, 19067, 19068, 19182, 19023, 19026,
        19029, 2401, 2402, 2403, 2404, 18021
    ];

    private readonly IInventoryStore _inventory;
    private readonly CurrentClientDataCatalog _current;

    public DollService(
        IInventoryStore inventory,
        CurrentClientDataCatalog current)
    {
        _inventory = inventory;
        _current = current;
    }

    public bool Supports(int itemIndex)
        => itemIndex == HappinessDollItemId;

    public IReadOnlyList<object> Open(
        long accountId,
        long characterId,
        long dollUid,
        int dollItemIndex)
    {
        if (!Supports(dollItemIndex))
            return [];

        var availableRewards = HappinessRewards
            .Select(id => _current.FindItem(id))
            .Where(x => x is not null)
            .Cast<CurrentItemDefinition>()
            .ToArray();

        if (availableRewards.Length == 0)
            return [Flash("幸福娃娃獎池目前沒有可用物品")];

        var reward = availableRewards[
            Random.Shared.Next(availableRewards.Length)];

        if (!TryResolveRewardKind(
                reward,
                out var rewardKind,
                out var rewardPart))
        {
            return [Flash(
                $"幸福娃娃抽到尚未支援的獎勵類型：{reward.Name}")];
        }

        var result = _inventory.OpenDoll(
            accountId,
            characterId,
            dollUid,
            dollItemIndex,
            reward.ItemId,
            rewardKind,
            1,
            rewardPart);

        if (!result.Success)
            return [Flash(result.Message)];

        var assets = _inventory.GetCharacterAssets(
            accountId,
            characterId);

        var packets = new List<object>
        {
            new
            {
                Type = "InventorySync",
                Items = assets.Inventory,
                MaxSize = assets.InventoryMaxSize
            }
        };

        if (rewardKind == AssetPurchaseKind.Equipment)
        {
            packets.Add(new
            {
                Type = "EquipSync",
                Equips = assets.Equips,
                MaxSize = assets.EquipMaxSize
            });
        }
        else if (rewardKind == AssetPurchaseKind.HorseEquipment)
        {
            packets.Add(new
            {
                Type = "HorseSync",
                Horses = assets.Horses,
                HorseEquips = assets.HorseEquips,
                MaxHorseEquipSize = assets.HorseEquipMaxSize
            });
        }

        packets.Add(new
        {
            Type = "PlayItemEffect",
            EntityID = characterId,
            ItemIndex = dollItemIndex
        });

        packets.Add(Flash(
            $"幸福娃娃送給你：{reward.Name}"));

        return packets;
    }

    private static bool TryResolveRewardKind(
        CurrentItemDefinition reward,
        out AssetPurchaseKind kind,
        out int part)
    {
        part = reward.EquipSlot;

        switch (reward.Category.ToLowerInvariant())
        {
            case "general":
            case "recovery":
            case "booster":
            case "horse":
                kind = AssetPurchaseKind.General;
                return true;

            case "horsebattery":
                kind = AssetPurchaseKind.HorseBattery;
                return true;

            case "equip":
            case "weapon":
                kind = AssetPurchaseKind.Equipment;
                return true;

            case "horseequip":
                kind = AssetPurchaseKind.HorseEquipment;
                return true;

            default:
                kind = default;
                return false;
        }
    }

    private static object Flash(string message)
        => new
        {
            Type = "SystemMessageFlash",
            Message = message
        };
}
