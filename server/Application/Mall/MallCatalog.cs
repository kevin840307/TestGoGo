using Microsoft.Extensions.Configuration;
using SGO.Server.Domain.Assets;
using SGO.Server.Infrastructure.GameData;

namespace SGO.Server.Application.Mall;

public sealed record MallCatalogItem(
    int MallId,
    int ItemIndex,
    string Name,
    string Category,
    long Price,
    long OriginalPrice,
    string Tag,
    int Count,
    bool IsActive,
    AssetPurchaseKind Kind,
    int MaxQuantity,
    string AssetName,
    string HorseType,
    int InitialEnergy,
    int ItemPart,
    int ItemDamage);

public static class MallCatalog
{
    public static MallCatalogItem[] Read(
        IConfiguration? config,
        CurrentClientDataCatalog? current)
    {
        if (config is null || current is null)
            return [];

        var result = new List<MallCatalogItem>();
        var usedIds = new HashSet<int>();

        foreach (var section in config
                     .GetSection("Gameplay:Mall:Items")
                     .GetChildren()
                     .Take(500))
        {
            var mallId = section.GetValue<int?>("MallID") ?? 0;
            var itemIndex = section.GetValue<int?>("ItemIndex") ?? 0;
            var price = section.GetValue<long?>("Price") ?? 0;
            var count = section.GetValue<int?>("Count") ?? 1;
            var maxQuantity = section.GetValue<int?>("MaxQuantity") ?? 99;
            var kindText = section["Kind"]?.Trim() ?? "";

            if (mallId <= 0 ||
                itemIndex <= 0 ||
                price <= 0 ||
                count <= 0 ||
                count > 999 ||
                maxQuantity <= 0 ||
                maxQuantity > 99 ||
                !usedIds.Add(mallId) ||
                !Enum.TryParse<AssetPurchaseKind>(
                    kindText, ignoreCase: true, out var kind))
            {
                continue;
            }

            var item = current.FindItem(itemIndex);
            var pet = kind == AssetPurchaseKind.Pet
                ? current.FindPet(itemIndex)
                : null;

            if (kind == AssetPurchaseKind.Pet)
            {
                if (pet is null || count != 1)
                    continue;
            }
            else if (item is null)
            {
                continue;
            }

            if (kind is AssetPurchaseKind.Equipment
                    or AssetPurchaseKind.Horse
                    or AssetPurchaseKind.HorseEquipment &&
                count != 1)
            {
                continue;
            }

            var fallbackName = pet?.Name ?? item?.Name ?? $"Item {itemIndex}";
            var name = Limit(section["Name"], 120);
            if (string.IsNullOrWhiteSpace(name))
                name = fallbackName;

            var category = Limit(section["Category"], 40);
            if (string.IsNullOrWhiteSpace(category))
            {
                category = kind switch
                {
                    AssetPurchaseKind.Equipment => "護符",
                    AssetPurchaseKind.Pet => "寵物",
                    AssetPurchaseKind.Horse => "機甲",
                    AssetPurchaseKind.HorseEquipment => "機甲裝備",
                    AssetPurchaseKind.HorseBattery => "機甲電池",
                    _ => "消耗品"
                };
            }

            var originalPrice =
                section.GetValue<long?>("OriginalPrice") ?? price;
            originalPrice = Math.Max(price, originalPrice);

            var tag = Limit(section["Tag"], 16);
            var active = section.GetValue<bool?>("IsActive") ?? true;
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
                mallId,
                itemIndex,
                name,
                category,
                price,
                originalPrice,
                tag,
                count,
                active,
                kind,
                maxQuantity,
                assetName,
                horseType,
                Math.Max(0, initialEnergy),
                itemPart,
                Math.Max(0, itemDamage)));
        }

        return result
            .OrderBy(x => x.MallId)
            .ToArray();
    }

    private static string Limit(string? value, int maxLength)
    {
        var text = (value ?? "").Trim();
        return text.Length <= maxLength
            ? text
            : text[..maxLength];
    }
}
