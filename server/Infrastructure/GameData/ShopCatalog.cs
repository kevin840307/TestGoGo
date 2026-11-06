using System.Buffers.Binary;
using System.Text;
using SGO.Server.Domain.Shops;

namespace SGO.Server.Infrastructure.GameData;

public sealed class ShopCatalog
{
    private readonly Dictionary<int, List<ShopItemDefinition>> _shops = new();
    private readonly Dictionary<int, ShopItemDefinition> _items = new();

    public ShopCatalog(string dataRoot)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        LoadRecovery(Path.Combine(dataRoot, "Item", "Recovery.itd"));
        LoadGeneral(Path.Combine(dataRoot, "Item", "General.itd"));
        LoadEquip(Path.Combine(dataRoot, "Item", "Equip.itd"));
        LoadWeapons(Path.Combine(dataRoot, "Item", "Weapon.itd"));

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
                Name: $"Item {itemIndex}");

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
                Name: name);

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
                Name: name));
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
                Name: name);

            Add(item);
            Add(item with { ShopId = 29 });
        }
    }

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
