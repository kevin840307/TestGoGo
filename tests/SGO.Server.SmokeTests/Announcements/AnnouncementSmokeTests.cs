using Microsoft.Extensions.Configuration;
using SGO.Server.Application.Announcements;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Announcements;

/// <summary>Independent, deterministic smoke cases; invoke Run from the suite runner.</summary>
internal static class AnnouncementSmokeTests
{
    public static void Run()
    {
        // Operator-configured announcements use the exact recovered client
        // Date/Title/Content fields, but do not depend on accounts or SQLite.
        var announcementConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Game:Announcements:0:Date"] = "2026-11-07",
                ["Game:Announcements:0:Title"] = "測試公告",
                ["Game:Announcements:0:Content"] = "伺服器公告已啟用",
                ["Game:Announcements:1:Date"] = "2026-11-08",
                ["Game:Announcements:1:Title"] = "   ",
                ["Game:Announcements:1:Content"] = "空標題應忽略"
            })
            .Build();
        var announcements = AnnouncementCatalog.Read(announcementConfig);
        Check(announcements.Length == 1 &&
              announcements[0].Date == "2026-11-07" &&
              announcements[0].Title == "測試公告" &&
              announcements[0].Content == "伺服器公告已啟用",
            "configured announcements match the recovered client list fields");
        Check(AnnouncementCatalog.Read(null).Length == 0,
            "announcements default safely to empty without configuration");
        var oversizedAnnouncementConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Game:Announcements:0:Title"] = new string('A', 500),
                ["Game:Announcements:0:Content"] = new string('B', 5000)
            })
            .Build();
        var boundedAnnouncements = AnnouncementCatalog.Read(oversizedAnnouncementConfig);
        Check(boundedAnnouncements.Length == 1 &&
              boundedAnnouncements[0].Title.Length == 120 &&
              boundedAnnouncements[0].Content.Length == 4000,
            "configured announcement lengths are bounded");
    }
}
