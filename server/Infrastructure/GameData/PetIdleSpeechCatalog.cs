using System.Text.Json;

namespace SGO.Server.Infrastructure.GameData;

public sealed class PetIdleSpeechCatalog
{
    private sealed class SpeechConfig
    {
        public string[] Default { get; set; } = [];
        public Dictionary<string, string[]> ByPetId { get; set; } = [];
    }

    private static readonly string[] BuiltInDefault =
    [
        "我會跟緊你的。",
        "需要我幫忙嗎？",
        "一起繼續冒險吧！"
    ];

    private readonly string[] _default;
    private readonly Dictionary<int, string[]> _byPetId = new();

    public PetIdleSpeechCatalog(string path)
    {
        var fallback = BuiltInDefault;

        if (!File.Exists(path))
        {
            _default = fallback;
            return;
        }

        try
        {
            var config = JsonSerializer.Deserialize<SpeechConfig>(
                File.ReadAllText(path),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            _default = Normalize(config?.Default) is { Length: > 0 } loaded
                ? loaded
                : fallback;

            if (config?.ByPetId is null)
                return;

            foreach (var pair in config.ByPetId)
            {
                if (!int.TryParse(pair.Key, out var petId) ||
                    petId <= 0)
                    continue;

                var lines = Normalize(pair.Value);
                if (lines.Length > 0)
                    _byPetId[petId] = lines;
            }
        }
        catch
        {
            _default = fallback;
        }
    }

    public string Pick(
        int petId,
        long petUid,
        int sequence)
    {
        var lines = _byPetId.TryGetValue(
                petId,
                out var specific) &&
            specific.Length > 0
                ? specific
                : _default;

        if (lines.Length == 0)
            return "";

        var seed = unchecked(
            (long)petId * 73856093L ^
            petUid * 19349663L ^
            (long)sequence * 83492791L);

        var index = (int)(
            (ulong)seed % (ulong)lines.Length);
        return lines[index];
    }

    private static string[] Normalize(
        IEnumerable<string>? values)
        => values?
            .Select(x => x?.Trim() ?? "")
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray()
           ?? [];
}
