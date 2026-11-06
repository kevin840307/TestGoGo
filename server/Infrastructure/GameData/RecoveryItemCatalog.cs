using System.Buffers.Binary;

namespace SGO.Server.Infrastructure.GameData;

public sealed record RecoveryItemEffect(
    int ItemIndex,
    int RecoverHp,
    int RecoverMp,
    int RecoverSp);

public sealed class RecoveryItemCatalog
{
    private readonly Dictionary<int, RecoveryItemEffect> _items = new();

    public RecoveryItemCatalog(string path)
    {
        const int recordSize = 1175;
        if (!File.Exists(path)) return;

        var bytes = File.ReadAllBytes(path);
        for (var offset = 0; offset + recordSize <= bytes.Length; offset += recordSize)
        {
            if (bytes[offset] != 4) continue;

            var itemIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                bytes.AsSpan(offset + 15, 2));
            if (itemIndex <= 0) continue;

            var recoveryType = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(offset + 8, 4));
            var amount = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(offset + 47, 4));
            if (amount <= 0 || amount > 1_000_000) continue;

            var hp = 0;
            var mp = 0;
            var sp = 0;

            // Source Recovery.itd uses 0=HP, 2=MP, 3=SP for the recovered
            // consumable records. Unknown types are ignored safely.
            switch (recoveryType)
            {
                case 0: hp = amount; break;
                case 2: mp = amount; break;
                case 3: sp = amount; break;
                default: continue;
            }

            _items[itemIndex] =
                new RecoveryItemEffect(itemIndex, hp, mp, sp);
        }
    }

    public RecoveryItemEffect? Find(int itemIndex)
        => _items.TryGetValue(itemIndex, out var value) ? value : null;
}
