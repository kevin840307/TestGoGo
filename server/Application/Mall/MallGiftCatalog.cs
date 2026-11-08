using Microsoft.Extensions.Configuration;
using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Mall;

public sealed record MallGiftCatalogItem(
    int RewardId,
    int ItemIndex,
    string Name,
    long RequiredDonation,
    int Count,
    bool IsActive,
    AssetPurchaseKind Kind,
    int ItemCategory,
    string AssetName,
    string HorseType,
    int InitialEnergy,
    int ItemPart,
    int ItemDamage);

public static class MallGiftCatalog
{
    public static MallGiftCatalogItem[] Read(
        IConfiguration? config,
        CurrentClientDataCatalog? current)
    {
        if (config is null || current is null)
            return [];

        var result = new List<MallGiftCatalogItem>();
        var usedIds = new HashSet<int>();

        foreach (var section in config
                     .GetSection("Gameplay:Mall:Gifts")
                     .GetChildren()
                     .Take(200))
        {
            var rewardId = section.GetValue<int?>("RewardID") ?? 0;
            var itemIndex = section.GetValue<int?>("ItemIndex") ?? 0;
            var requiredDonation =
                section.GetValue<long?>("RequiredDonation") ?? 0;
            var count = section.GetValue<int?>("Count") ?? 1;
            var active = section.GetValue<bool?>("IsActive") ?? true;
            var kindText = section["Kind"]?.Trim() ?? "";

            if (rewardId <= 0 ||
                itemIndex <= 0 ||
                requiredDonation <= 0 ||
                count <= 0 ||
                count > 999 ||
                !usedIds.Add(rewardId) ||
                !Enum.TryParse<AssetPurchaseKind>(
                    kindText,
                    ignoreCase: true,
                    out var kind))
            {
                continue;
            }

            var item = current.FindItem(itemIndex);
            var pet = kind == AssetPurchaseKind.Pet
                ? current.FindPet(itemIndex)
                : null;

            if (kind == AssetPurchaseKind.Pet)
            {
                if (pet is null)
                    continue;
            }
            else if (item is null ||
                     !IsKindCompatible(kind, item.Category))
            {
                continue;
            }

            if ((kind is AssetPurchaseKind.Equipment
                    or AssetPurchaseKind.Horse
                    or AssetPurchaseKind.HorseEquipment) &&
                count != 1)
            {
                continue;
            }

            var fallbackName =
                pet?.Name ??
                item?.Name ??
                $"Item {itemIndex}";
            var name = Limit(section["Name"], 120);
            if (string.IsNullOrWhiteSpace(name))
                name = fallbackName;

            var assetName = Limit(section["AssetName"], 80);
            if (string.IsNullOrWhiteSpace(assetName))
                assetName = fallbackName;

            var horseType = Limit(section["HorseType"], 16);
            if (string.IsNullOrWhiteSpace(horseType))
                horseType = item?.HorseType ?? "MS";

            var initialEnergy =
                section.GetValue<int?>("InitialEnergy")
                ?? Math.Max(item?.Battery ?? 0, 0);
            var itemPart =
                section.GetValue<int?>("ItemPart")
                ?? item?.EquipSlot
                ?? 255;
            var itemDamage =
                section.GetValue<int?>("ItemDamage")
                ?? item?.Durability
                ?? 0;

            result.Add(new(
                rewardId,
                itemIndex,
                name,
                requiredDonation,
                count,
                active,
                kind,
                ToClientCategory(kind),
                assetName,
                horseType,
                Math.Max(0, initialEnergy),
                itemPart,
                Math.Max(0, itemDamage)));
        }

        return result
            .OrderBy(x => x.RequiredDonation)
            .ThenBy(x => x.RewardId)
            .ToArray();
    }

    private static int ToClientCategory(AssetPurchaseKind kind)
        => kind switch
        {
            AssetPurchaseKind.Equipment => 1,
            AssetPurchaseKind.Pet => 2,
            AssetPurchaseKind.Horse => 3,
            AssetPurchaseKind.HorseEquipment => 4,
            _ => 0
        };

    private static bool IsKindCompatible(
        AssetPurchaseKind kind,
        string category)
    {
        category = category.Trim().ToLowerInvariant();
        return kind switch
        {
            AssetPurchaseKind.General =>
                category is "general" or "recovery" or "booster" or "horse",
            AssetPurchaseKind.HorseBattery =>
                category == "horsebattery",
            AssetPurchaseKind.Equipment =>
                category is "equip" or "weapon",
            AssetPurchaseKind.Horse =>
                category == "horse",
            AssetPurchaseKind.HorseEquipment =>
                category == "horseequip",
            _ => false
        };
    }

    private static string Limit(string? value, int maxLength)
    {
        var text = (value ?? "").Trim();
        return text.Length <= maxLength
            ? text
            : text[..maxLength];
    }
}
