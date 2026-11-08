using SGO.Server.Application.Abstractions;
using SGO.Server.Application.Messaging;
using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Items;

public sealed class DollService
{
    public const int HappinessDollItemId = 2126;

    // 2126 remains the only doll with global rare-reward announcements.
    // All 2126..2134 reward pools live in DollRewardCatalog.


    private readonly IInventoryStore _inventory;
    private readonly ICharacterStore _characters;
    private readonly CurrentClientDataCatalog _current;
    private readonly ServerBroadcastHub _broadcasts;
    private readonly IConfiguration _config;
    private readonly FestivalRewardCatalog _festivalRewards;

    public DollService(
        IInventoryStore inventory,
        ICharacterStore characters,
        CurrentClientDataCatalog current,
        ServerBroadcastHub broadcasts,
        IConfiguration config)
    {
        _inventory = inventory;
        _characters = characters;
        _current = current;
        _broadcasts = broadcasts;
        _config = config;
        _festivalRewards = new FestivalRewardCatalog(current.Root);
    }

    public bool Supports(int itemIndex)
        => DollRewardCatalog.Supports(itemIndex) ||
           _festivalRewards.Supports(itemIndex);

    private sealed record RewardCandidate(
        CurrentItemDefinition Item,
        AssetPurchaseKind Kind,
        int Part,
        int MinCount,
        int MaxCount);

    public IReadOnlyList<object> Open(
        long accountId,
        long characterId,
        long dollUid,
        int dollItemIndex)
    {
        if (!Supports(dollItemIndex))
            return [];

        var availableRewards =
            BuildAvailableRewards(dollItemIndex);

        if (availableRewards.Length == 0)
        {
            var emptyPoolSourceName =
                _current.FindItem(dollItemIndex)?.Name ??
                $"道具 {dollItemIndex}";
            return [Flash(
                $"{emptyPoolSourceName}獎池目前沒有可安全發放的物品")];
        }

        var selected = availableRewards[
            Random.Shared.Next(availableRewards.Length)];

        var rewardCount = selected.MinCount == selected.MaxCount
            ? selected.MinCount
            : Random.Shared.Next(
                selected.MinCount,
                selected.MaxCount + 1);

        // Non-stackable assets are represented by one DB row per item.
        // Current festival data does not declare multi-count equipment;
        // reject it defensively rather than silently granting the wrong
        // number of rows.
        if ((selected.Kind is AssetPurchaseKind.Equipment
                or AssetPurchaseKind.HorseEquipment) &&
            rewardCount != 1)
        {
            return [Flash(
                $"獎勵數量設定無效：{selected.Item.Name}")];
        }

        var result = _inventory.OpenDoll(
            accountId,
            characterId,
            dollUid,
            dollItemIndex,
            selected.Item.ItemId,
            selected.Kind,
            rewardCount,
            selected.Kind == AssetPurchaseKind.Equipment
                ? 255
                : selected.Part,
            selected.Item.Durability);

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

        if (selected.Kind == AssetPurchaseKind.Equipment)
        {
            packets.Add(new
            {
                Type = "EquipSync",
                Equips = assets.Equips,
                MaxSize = assets.EquipMaxSize
            });
        }
        else if (selected.Kind == AssetPurchaseKind.HorseEquipment)
        {
            packets.Add(new
            {
                Type = "HorseSync",
                Horses = assets.Horses,
                HorseEquips = assets.HorseEquips,
                MaxHorseEquipSize = assets.HorseEquipMaxSize
            });
        }

        var effectPacket = new
        {
            Type = "PlayItemEffect",
            EntityID = characterId,
            ItemIndex = dollItemIndex
        };
        packets.Add(effectPacket);

        var presence =
            _broadcasts.FindCharacter(characterId);
        if (presence is not null &&
            presence.MapId > 0)
        {
            _broadcasts.PublishToMap(
                presence.MapId,
                effectPacket,
                presence.ConnectionId);
        }

        var sourceItem = _current.FindItem(dollItemIndex);
        var sourceName = sourceItem?.Name ??
                         $"道具 {dollItemIndex}";
        packets.Add(Flash(
            $"{sourceName}送給你：{selected.Item.Name}" +
            (rewardCount > 1 ? $" x {rewardCount}" : "")));

        if (dollItemIndex == HappinessDollItemId &&
            ShouldAnnounce(selected.Item))
        {
            var character = _characters.GetCharacter(
                accountId,
                characterId);
            var characterName = character is not null &&
                                character.TryGetValue("NAME", out var name)
                ? name?.ToString() ?? $"角色{characterId}"
                : $"角色{characterId}";

            _broadcasts.Publish(new
            {
                Type = "SystemMessageBroadcast",
                Message =
                    $"[幸福娃娃] 恭喜 {characterName} 抽中了稀有寶物「{selected.Item.Name}」！"
            });
        }

        return packets;
    }

    private RewardCandidate[] BuildAvailableRewards(
        int sourceItemId)
    {
        var rewardSpecs =
            DollRewardCatalog.Supports(sourceItemId)
                ? DollRewardCatalog.GetRewards(sourceItemId)
                : _festivalRewards.GetRewards(sourceItemId);

        return rewardSpecs
            .Select(TryBuildCandidate)
            .Where(x => x is not null)
            .Cast<RewardCandidate>()
            .ToArray();
    }

    private RewardCandidate? TryBuildCandidate(
        FestivalRandomReward rewardSpec)
    {
        var reward = _current.FindItem(
            rewardSpec.ItemId);
        if (reward is null ||
            !TryResolveRewardKind(
                reward,
                out var kind,
                out var part))
            return null;

        var min = Math.Clamp(
            rewardSpec.MinCount, 1, 999);
        var max = Math.Clamp(
            rewardSpec.MaxCount, min, 999);

        if (kind is AssetPurchaseKind.Equipment
                or AssetPurchaseKind.HorseEquipment)
        {
            min = 1;
            max = 1;
        }

        return new(
            reward,
            kind,
            part,
            min,
            max);
    }

    private bool ShouldAnnounce(CurrentItemDefinition reward)
    {
        var configured =
            _config["Gameplay:Dolls:Happiness:AnnouncementRewardIds"];

        var ids = string.IsNullOrWhiteSpace(configured)
            ? new HashSet<int> { 19059, 18021 }
            : configured
                .Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var id) ? id : 0)
                .Where(x => x > 0)
                .ToHashSet();

        if (ids.Contains(reward.ItemId))
            return true;

        var minimumPrice =
            _config.GetValue<int?>(
                "Gameplay:Dolls:Happiness:AnnouncementMinimumPrice")
            ?? 100000;

        return minimumPrice > 0 &&
               reward.Price >= minimumPrice;
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
