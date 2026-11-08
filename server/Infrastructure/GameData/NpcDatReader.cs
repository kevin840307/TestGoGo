namespace SGO.Server.Infrastructure.GameData;

public sealed record NpcSpawnRecord(
    int NpcTypeId,
    int X,
    int Y,
    string Script,
    byte[] NameBytes);

public static class NpcDatReader
{
    public static IReadOnlyList<NpcSpawnRecord> Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        var count = reader.ReadInt32();
        var result = new List<NpcSpawnRecord>(Math.Max(0, count));

        for (var i = 0; i < count; i++)
        {
            var id = reader.ReadInt32();
            var x = reader.ReadInt32();
            var y = reader.ReadInt32();

            // Legacy format includes one reserved byte before the script string.
            _ = reader.ReadByte();

            var script = ReadNullTerminatedAscii(reader);
            var name = ReadNullTerminatedBytes(reader);

            result.Add(new NpcSpawnRecord(id, x, y, script, name));
        }

        return result;
    }

    private static string ReadNullTerminatedAscii(BinaryReader reader)
    {
        var bytes = ReadNullTerminatedBytes(reader);
        return System.Text.Encoding.ASCII.GetString(bytes);
    }

    private static byte[] ReadNullTerminatedBytes(BinaryReader reader)
    {
        using var ms = new MemoryStream();
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var b = reader.ReadByte();
            if (b == 0) break;
            ms.WriteByte(b);
        }
        return ms.ToArray();
    }
}
