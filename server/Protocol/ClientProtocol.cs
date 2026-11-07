using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SGO.Server.Protocol;

public static class ClientProtocol
{
    // Compatibility with the recovered client. Static IV is retained ONLY to match
    // the existing wire protocol; not suitable for production security.
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("Paradise25DGameSecretKey2026Base");
    private static readonly byte[] Iv = Encoding.UTF8.GetBytes("InitVectorVector");

    public static JsonDocument Decode(string base64)
    {
        byte[] cipher = Convert.FromBase64String(base64);
        if (cipher.Length == 0 || cipher.Length % 16 != 0 || cipher.Length > 2_097_152)
            throw new FormatException("Invalid encrypted packet length");
        using var aes = Aes.Create();
        aes.Key = Key;
        aes.IV = Iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var decryptor = aes.CreateDecryptor();
        var json = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return JsonDocument.Parse(json);
    }

    public static string Encode(object payload)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var aes = Aes.Create();
        aes.Key = Key;
        aes.IV = Iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        return Convert.ToBase64String(encryptor.TransformFinalBlock(json, 0, json.Length));
    }

    public static string Str(this JsonElement json, string key, string fallback = "")
        => json.TryGetProperty(key, out var v) ? v.ToString() : fallback;

    public static int Int(this JsonElement json, string key, int fallback = 0)
        => json.TryGetProperty(key, out var v) && v.TryGetInt32(out int n) ? n : fallback;

    public static long Long(
        this JsonElement json,
        string key,
        long fallback = 0)
        => json.TryGetProperty(key, out var v) &&
           v.TryGetInt64(out long n)
            ? n
            : fallback;
}
