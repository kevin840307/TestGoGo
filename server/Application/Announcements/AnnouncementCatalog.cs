using Microsoft.Extensions.Configuration;

namespace SGO.Server.Application.Announcements;

/// <summary>
/// Reads operator-authored announcements from Game:Announcements in the
/// existing configuration (including environment overrides). No database
/// or payment integration is required.
/// </summary>
public static class AnnouncementCatalog
{
    public sealed record Announcement(string Date, string Title, string Content);

    public static Announcement[] Read(IConfiguration? config)
    {
        if (config is null)
            return [];

        return config.GetSection("Game:Announcements")
            .GetChildren()
            .Take(50)
            .Select(section => new Announcement(
                Limit(section["Date"], 32),
                Limit(section["Title"], 120),
                Limit(section["Content"], 4000)))
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Title) &&
                !string.IsNullOrWhiteSpace(item.Content))
            .ToArray();
    }

    private static string Limit(string? value, int maxLength)
    {
        var text = (value ?? "").Trim();
        return text.Length > maxLength
            ? text[..maxLength]
            : text;
    }
}
