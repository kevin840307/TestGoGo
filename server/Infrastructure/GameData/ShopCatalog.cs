using System.Buffers.Binary;
using System.Text;
using SGO.Server.Domain.Shops;

namespace SGO.Server.Infrastructure.GameData;

public sealed class ShopCatalog
{
    private readonly Dictionary<int, List<ShopItemDefinition>> _shops = new();
    private readonly Dictionary<int, ShopItemDefinition> _items = new();
    private readonly CurrentClientDataCatalog _current;
    private readonly LegacyClientDataCatalog _legacyClient;
    private readonly List<string> _mappingWarnings = [];
    private readonly List<string> _mappingInfo = [];

    public ShopCatalog(
        string dataRoot,
        CurrentClientDataCatalog current,
        LegacyClientDataCatalog legacyClient)
    {
        _current = current;
        _legacyClient = legacyClient;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        LoadRecovery(Path.Combine(dataRoot, "Item", "Recovery.itd"));
        LoadGeneral(Path.Combine(dataRoot, "Item", "General.itd"));
        LoadEquip(Path.Combine(dataRoot, "Item", "Equip.itd"));
        LoadWeapons(Path.Combine(dataRoot, "Item", "Weapon.itd"));

        // Freeze the legacy definitions before producing current-client shop
        // entries. Legacy and current IDs can collide with different meanings,
        // so they must never share the same dictionary while mapping.
        var legacyItems = _items.ToDictionary(
            x => x.Key,
            x => x.Value);

        _items.Clear();
        _shops.Clear();

        // Shop.csv is authoritative only for legacy shop membership. Every
        // legacy entry is resolved to a current-client ItemID before exposure.
        LoadShopMap(
            Path.Combine(dataRoot, "Shop", "Shop.csv"),
            legacyItems);

        foreach (var list in _shops.Values)
            list.Sort((a, b) => a.ItemIndex.CompareTo(b.ItemIndex));
    }

    public IReadOnlyList<ShopItemDefinition> GetShopItems(int shopId)
        => _shops.TryGetValue(shopId, out var items)
            ? items
            : Array.Empty<ShopItemDefinition>();

    public ShopItemDefinition? FindInShop(int shopId, int itemIndex)
        => GetShopItems(shopId).FirstOrDefault(x => x.ItemIndex == itemIndex);

    public ShopItemDefinition? FindItem(int itemIndex)
        => _items.TryGetValue(itemIndex, out var value) ? value : null;

    public IReadOnlyList<string> MappingWarnings => _mappingWarnings;
    public IReadOnlyList<string> MappingInfo => _mappingInfo;

    private void LoadRecovery(string path)
    {
        const int recordSize = 1175;
        if (!File.Exists(path)) return;

        var bytes = File.ReadAllBytes(path);
        for (var offset = 0; offset + recordSize <= bytes.Length; offset += recordSize)
        {
            if (bytes[offset] != 4) continue;

            var itemIndex = ReadU16(bytes, offset + 15);
            var price = NormalizePrice(ReadI32(bytes, offset + 43), 30);
            if (itemIndex <= 0) continue;

            var item = new ShopItemDefinition(
                ShopId: 1,
                ItemIndex: itemIndex,
                TabType: 0,
                Price: price,
                CanSell: bytes[offset + 2] != 0,
                Name: $"Item {itemIndex}",
                SourceCategory: "recovery");

            Add(item);
            Add(item with { ShopId = 24 });
        }
    }

    private void LoadGeneral(string path)
    {
        const int recordSize = 342;
        const int nameOffset = 46;
        const int nameLength = 20;

        if (!File.Exists(path)) return;

        var bytes = File.ReadAllBytes(path);
        var big5 = Encoding.GetEncoding(950);

        for (var offset = 0; offset + recordSize <= bytes.Length; offset += recordSize)
        {
            if (bytes[offset] != 6) continue;

            var itemIndex = ReadI32(bytes, offset + 8);
            var price = NormalizePrice(ReadI32(bytes, offset + 66), 10);
            if (itemIndex <= 0) continue;

            var name = ReadFixedString(big5, bytes, offset + nameOffset, nameLength);
            var item = new ShopItemDefinition(
                ShopId: 1,
                ItemIndex: itemIndex,
                TabType: 0,
                Price: price,
                CanSell: bytes[offset + 2] != 0,
                Name: name,
                SourceCategory: "general");

            // KingCity_All_Shop: 1=items, 8=materials.
            // Keep 13 for other legacy shop scripts that reference the broad
            // general-goods catalog.
            Add(item);
            Add(item with { ShopId = 8 });
            Add(item with { ShopId = 13 });

            if (name.Contains("彈", StringComparison.Ordinal))
                Add(item with { ShopId = 7 });

            if (name.Contains("卷", StringComparison.Ordinal) ||
                name.Contains("書", StringComparison.Ordinal) ||
                name.Contains("技能", StringComparison.Ordinal))
                Add(item with { ShopId = 12 });
        }
    }

    private void LoadEquip(string path)
    {
        const int recordSize = 1123;
        const int nameOffset = 79;
        const int nameLength = 20;

        if (!File.Exists(path)) return;

        var bytes = File.ReadAllBytes(path);
        var big5 = Encoding.GetEncoding(950);

        for (var offset = 0; offset + recordSize <= bytes.Length; offset += recordSize)
        {
            if (bytes[offset] != 0) continue;

            var slot = bytes[offset + 8];
            var itemIndex = ReadU16(bytes, offset + 23);
            var price = NormalizePrice(ReadI32(bytes, offset + 75), 50);
            if (itemIndex <= 0) continue;

            // KingCity_All_Shop mapping:
            // 3/4=hands, 5=upper, 6=lower, 7=shield, 10=mechanical head.
            var shopId = slot switch
            {
                1 => 3,
                2 => 4,
                3 => 5,
                4 => 6,
                6 => 7,
                0 => 10,
                _ => 0
            };

            if (shopId == 0) continue;

            var name = ReadFixedString(big5, bytes, offset + nameOffset, nameLength);
            Add(new ShopItemDefinition(
                ShopId: shopId,
                ItemIndex: itemIndex,
                TabType: 1,
                Price: price,
                CanSell: bytes[offset + 2] != 0,
                Name: name,
                SourceCategory: "equip"));
        }
    }

    private void LoadWeapons(string path)
    {
        const int recordSize = 1247;
        const int nameOffset = 103;
        const int nameLength = 20;

        if (!File.Exists(path)) return;

        var bytes = File.ReadAllBytes(path);
        var big5 = Encoding.GetEncoding(950);

        for (var offset = 0; offset + recordSize <= bytes.Length; offset += recordSize)
        {
            if (bytes[offset] != 1) continue;

            var itemIndex = ReadU16(bytes, offset + 43);
            var price = NormalizePrice(ReadI32(bytes, offset + 98), 100);
            if (itemIndex <= 0) continue;

            var name = ReadFixedString(big5, bytes, offset + nameOffset, nameLength);
            var item = new ShopItemDefinition(
                ShopId: 2,
                ItemIndex: itemIndex,
                TabType: 1,
                Price: price,
                CanSell: bytes[offset + 2] != 0,
                Name: name,
                SourceCategory: "weapon");

            Add(item);
            // Legacy scripts use both Shop2 and Shop10 for weapon views.
            Add(item with { ShopId = 10 });
            Add(item with { ShopId = 29 });
        }
    }

    private void LoadShopMap(
        string path,
        IReadOnlyDictionary<int, ShopItemDefinition> legacyItems)
    {
        if (!File.Exists(path)) return;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) ||
                !line.Contains(','))
                continue;

            var cols = line.Split(',');
            if (cols.Length < 3 ||
                !int.TryParse(cols[0], out var shopId) ||
                !int.TryParse(cols[1], out var declaredCount))
                continue;

            var itemIds = cols
                .Skip(2)
                .Select(x => int.TryParse(x.Trim(), out var id) ? id : 0)
                .Where(x => x > 0)
                .Take(Math.Max(0, declaredCount));

            foreach (var legacyItemIndex in itemIds)
            {
                if (!legacyItems.TryGetValue(legacyItemIndex, out var legacy))
                {
                    _mappingWarnings.Add(
                        $"[SKIPPED][LEGACY_ITEM_UNKNOWN] Shop={shopId} LegacyItemID={legacyItemIndex}");
                    continue;
                }

                var confirmedLegacyName =
                    _legacyClient.FindItemName(
                        legacyItemIndex,
                        legacy.SourceCategory);

                var sourceName =
                    !string.IsNullOrWhiteSpace(confirmedLegacyName)
                        ? confirmedLegacyName
                        : HasUsableLegacyServerName(legacy)
                            ? legacy.Name
                            : null;

                var sourceNameKind =
                    !string.IsNullOrWhiteSpace(confirmedLegacyName)
                        ? "OldClient"
                        : sourceName is not null
                            ? "OldServer"
                            : "Unknown";

                CurrentItemDefinition? current = null;
                var sameId = _current.FindItem(legacyItemIndex);

                if (!string.IsNullOrWhiteSpace(sourceName))
                {
                    // ID is only accepted when the current item still has the
                    // same meaning. If the same ID now points at another name,
                    // treat it as an ID collision and remap by name.
                    if (sameId is not null &&
                        CategoryCompatible(
                            sameId.Category,
                            legacy.SourceCategory) &&
                        SameName(
                            sameId.Name,
                            sourceName))
                    {
                        current = sameId;
                        _mappingInfo.Add(
                            $"[SAFE_SAME_ID] Shop={shopId} LegacyItemID={legacyItemIndex} Name={sourceName} Source={sourceNameKind}");
                    }
                    else if (sameId is not null)
                    {
                        _mappingWarnings.Add(
                            $"[LEGACY_ID_COLLISION] Shop={shopId} LegacyItemID={legacyItemIndex} LegacyName={sourceName} CurrentName={sameId.Name}");
                    }

                    current ??= _current.FindItemByName(
                        sourceName,
                        PreferredCurrentCategory(legacy));

                    if (current is null)
                    {
                        _mappingWarnings.Add(
                            $"[SKIPPED][CURRENT_ITEM_NOT_FOUND] Shop={shopId} LegacyItemID={legacyItemIndex} LegacyName={sourceName} Source={sourceNameKind} Category={legacy.SourceCategory}");
                        continue;
                    }

                    if (current.ItemId != legacyItemIndex)
                    {
                        _mappingInfo.Add(
                            $"[NAME_REMAP] Shop={shopId} LegacyItemID={legacyItemIndex} LegacyName={sourceName} -> CurrentItemID={current.ItemId} CurrentName={current.Name} Source={sourceNameKind}");
                    }
                }
                else
                {
                    // No reliable old name exists. Only identical current ID
                    // with compatible category is safe enough to expose.
                    if (sameId is null ||
                        !CategoryCompatible(
                            sameId.Category,
                            legacy.SourceCategory))
                    {
                        _mappingWarnings.Add(
                            $"[SKIPPED][OLD_NAME_MISSING] Shop={shopId} LegacyItemID={legacyItemIndex} Category={legacy.SourceCategory}");
                        continue;
                    }

                    current = sameId;
                    _mappingInfo.Add(
                        $"[SAFE_SAME_ID_NO_NAME] Shop={shopId} ItemID={legacyItemIndex} Category={legacy.SourceCategory} CurrentName={current.Name}");
                }

                Add(new ShopItemDefinition(
                    ShopId: shopId,
                    ItemIndex: current.ItemId,
                    TabType: CurrentTabType(current.Category),
                    Price: current.Price > 0
                        ? current.Price
                        : legacy.Price,
                    CanSell: current.CanSell != 0,
                    Name: current.Name,
                    SourceCategory: current.Category));
            }
        }
    }

    private static string? PreferredCurrentCategory(
        ShopItemDefinition legacy)
        => string.IsNullOrWhiteSpace(legacy.SourceCategory)
            ? null
            : legacy.SourceCategory;

    private static bool HasUsableLegacyServerName(
        ShopItemDefinition legacy)
        => !string.IsNullOrWhiteSpace(legacy.Name) &&
           !legacy.Name.StartsWith(
               "Item ",
               StringComparison.OrdinalIgnoreCase);

    private static bool CategoryCompatible(
        string currentCategory,
        string legacyCategory)
    {
        var current = currentCategory.Trim().ToLowerInvariant();
        var legacy = legacyCategory.Trim().ToLowerInvariant();

        if (current == legacy)
            return true;

        return legacy switch
        {
            "general" => current is "general" or "booster",
            "equip" => current == "equip",
            "weapon" => current == "weapon",
            "recovery" => current == "recovery",
            "horse" => current == "horse",
            "horseequip" => current == "horseequip",
            "horsebattery" => current == "horsebattery",
            _ => false
        };
    }

    private static bool SameName(string a, string b)
        => CurrentClientDataCatalog.NormalizeName(a) ==
           CurrentClientDataCatalog.NormalizeName(b);

    private static int CurrentTabType(string category)
        => category.ToLowerInvariant() switch
        {
            // Godot SharedInventoryPanel exposes only
            // GENERAL=0, EQUIP=1, PET=2 and MECHA=3.
            // Batteries live in the normal inventory; horse bodies and
            // horse equipment are both presented through the MECHA tab.
            "booster" or "general" or "recovery" or "horsebattery" => 0,
            "equip" or "weapon" => 1,
            "horse" or "horseequip" => 3,
            _ => 0
        };

    private void Add(ShopItemDefinition item)
    {
        if (!_shops.TryGetValue(item.ShopId, out var list))
        {
            list = new List<ShopItemDefinition>();
            _shops[item.ShopId] = list;
        }

        if (list.Any(x => x.ItemIndex == item.ItemIndex))
            return;

        list.Add(item);
        _items.TryAdd(item.ItemIndex, item);
    }

    private static string ReadFixedString(
        Encoding encoding,
        byte[] bytes,
        int offset,
        int length)
    {
        var end = offset;
        var max = Math.Min(bytes.Length, offset + length);
        while (end < max && bytes[end] != 0)
            end++;

        if (end <= offset)
            return string.Empty;

        return encoding.GetString(bytes, offset, end - offset).Trim();
    }

    private static int NormalizePrice(int value, int fallback)
        => value is > 0 and <= 10_000_000 ? value : fallback;

    private static int ReadI32(byte[] bytes, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static int ReadU16(byte[] bytes, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
}
