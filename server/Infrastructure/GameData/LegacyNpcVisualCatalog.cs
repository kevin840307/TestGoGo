namespace SGO.Server.Infrastructure.GameData;

public sealed class LegacyNpcVisualCatalog
{
    private readonly Dictionary<int, string> _sprites = new();

    public LegacyNpcVisualCatalog(string? legacyClientRoot)
    {
        if (string.IsNullOrWhiteSpace(legacyClientRoot))
            return;

        var path = Path.Combine(
            legacyClientRoot,
            "NPC",
            "NPC_Table.dat");

        if (!File.Exists(path))
            return;

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        if (stream.Length < 4)
            return;

        var count = reader.ReadInt32();
        if (count <= 0 || count > 10000)
            return;

        for (var index = 0; index < count; index++)
        {
            var fields = new string[6];
            for (var field = 0; field < fields.Length; field++)
                fields[field] = ReadNullTerminatedAscii(reader);

            var sprite = NormalizeSprite(fields[0]);
            if (!string.IsNullOrWhiteSpace(sprite))
                _sprites[index] = sprite;
        }
    }

    public string? FindSprite(int npcTypeId)
        => _sprites.TryGetValue(npcTypeId, out var sprite)
            ? sprite
            : null;

    public int Count => _sprites.Count;

    private static string ReadNullTerminatedAscii(BinaryReader reader)
    {
        var bytes = new List<byte>(32);

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var value = reader.ReadByte();
            if (value == 0)
                break;

            bytes.Add(value);
        }

        return bytes.Count == 0
            ? string.Empty
            : System.Text.Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static string NormalizeSprite(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var name = Path.GetFileName(
            path.Replace('\\', Path.DirectorySeparatorChar));

        if (name.EndsWith(
                ".spr",
                StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : name + ".png";
    }
}
