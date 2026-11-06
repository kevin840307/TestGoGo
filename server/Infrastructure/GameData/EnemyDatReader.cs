namespace SGO.Server.Infrastructure.GameData;

public sealed record EnemySpawnRecord(
    int SourceFlags,
    int EnemyId,
    int X,
    int Y,
    byte[] Tail);

public static class EnemyDatReader
{
    // Record layout used by the map Enemy.dat files:
    // int32 legacy field, zero-terminated ASCII enemy id, int32 X, int32 Y,
    // followed by a fixed 207-byte source payload.
    private const int TailSize = 207;

    public static IReadOnlyList<EnemySpawnRecord> Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        var count = reader.ReadInt32();
        var result = new List<EnemySpawnRecord>(Math.Max(0, count));

        for (var i = 0; i < count; i++)
        {
            var sourceFlags = reader.ReadInt32();
            var enemyIdText = ReadNullTerminatedAscii(reader);
            if (!int.TryParse(enemyIdText, out var enemyId))
                throw new InvalidDataException(
                    $"Invalid enemy id '{enemyIdText}' at record {i} in {path}");

            var x = reader.ReadInt32();
            var y = reader.ReadInt32();
            var tail = reader.ReadBytes(TailSize);
            if (tail.Length != TailSize)
                throw new EndOfStreamException(
                    $"Enemy.dat record {i} ended early in {path}");

            result.Add(new EnemySpawnRecord(
                sourceFlags,
                enemyId,
                x,
                y,
                tail));
        }

        if (stream.Position != stream.Length)
            throw new InvalidDataException(
                $"Enemy.dat has {stream.Length - stream.Position} trailing bytes: {path}");

        return result;
    }

    private static string ReadNullTerminatedAscii(BinaryReader reader)
    {
        var chars = new List<byte>(4);
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var b = reader.ReadByte();
            if (b == 0) break;
            chars.Add(b);
            if (chars.Count > 10)
                throw new InvalidDataException("Enemy id string is unexpectedly long.");
        }

        return System.Text.Encoding.ASCII.GetString(chars.ToArray());
    }
}
