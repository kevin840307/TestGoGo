using System.Text.Json;
using SGO.Server.Application;
using SGO.Server.Domain.Sessions;
using static SGO.Server.SmokeTests.SmokeAssert;

namespace SGO.Server.SmokeTests.Protocol;

/// <summary>Integration tests for packet routing before any user is authenticated.</summary>
internal static class PreloginPacketSmokeTests
{
    public static void Run(PacketRouter router, ClientSession session)
    {
        using (var malformedLogin = Json(
            """{"Type":"LoginRequest","User":"offline"}"""))
        {
            var packets = router.Handle(malformedLogin.RootElement, session);
            var json = JsonSerializer.Serialize(packets);
            Check(json.Contains("LoginResponse") &&
                  json.Contains("false") &&
                  !session.AccountId.HasValue,
                "malformed login gets LoginResponse failure without session mutation");
        }
        using (var malformedRegister = Json(
            """{"Type":"RegisterAccountRequest","LoginName":"n","Password":"p","EMAIL":17}"""))
        {
            var packets = router.Handle(malformedRegister.RootElement, session);
            var json = JsonSerializer.Serialize(packets);
            Check(json.Contains("RegisterAccountResponse") &&
                  json.Contains("false"),
                "invalid registration email type returns RegisterAccountResponse failure");
        }
        using (var nonObject = Json("""["not-a-packet"]"""))
        {
            var packets = router.Handle(nonObject.RootElement, session);
            Check(JsonSerializer.Serialize(packets).Contains("INVALID_PACKET"),
                "non-object JSON payload yields explicit error, not an exception");
        }

        using (var invalidPacketType = Json("""{"Type":42}"""))
        {
            var packets = router.Handle(invalidPacketType.RootElement, session);
            Check(JsonSerializer.Serialize(packets).Contains("INVALID_PACKET"),
                "non-string Type is rejected before packet routing");
        }

        var pongBefore = session.LastPongAt;
        using (var pongRequest = Json(
            """{"Type":"Pong"}"""))
        {
            var responses = router.Handle(
                pongRequest.RootElement,
                session);
            Check(responses.Count == 0 &&
                  session.LastPongAt >= pongBefore,
                "Pong is accepted before authentication for login-screen heartbeat");
        }

        using (var announcementRequest = Json(
            """{"Type":"AnnouncementListRequest"}"""))
        {
            var responses = router.Handle(
                announcementRequest.RootElement,
                session);

            var json = JsonSerializer.Serialize(responses);
            Check(json.Contains(
                      "\"Type\":\"AnnouncementListResponse\"") &&
                  json.Contains("\"Success\":true") &&
                  json.Contains("\"Announcements\":[]"),
                "announcement list returns a successful empty response when no announcements are configured");
        }
    }
}
