using System.Text;
using SGO.Server.Application.Mall;

namespace SGO.Server.Transport.Http;

public static class MallIntegrationEndpoint
{
    private const int MaxWebhookBodyChars =
        1_048_576;

    public static WebApplication MapMallIntegrationEndpoints(
        this WebApplication app)
    {
        app.MapPost(
            "/integrations/paypal/webhook",
            async (
                HttpRequest request,
                PayPalWebhookService service,
                CancellationToken cancellationToken) =>
            {
                if (request.ContentLength >
                    MaxWebhookBodyChars)
                {
                    return Results.StatusCode(
                        StatusCodes.Status413PayloadTooLarge);
                }

                using var reader =
                    new StreamReader(
                        request.Body,
                        Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: false,
                        leaveOpen: true);

                var body =
                    await reader.ReadToEndAsync(
                        cancellationToken);

                if (body.Length >
                    MaxWebhookBodyChars)
                {
                    return Results.StatusCode(
                        StatusCodes.Status413PayloadTooLarge);
                }

                var headers =
                    request.Headers.ToDictionary(
                        x => x.Key,
                        x => x.Value.ToString(),
                        StringComparer.OrdinalIgnoreCase);

                var result =
                    await service.HandleAsync(
                        headers,
                        body,
                        cancellationToken);

                return result.Outcome switch
                {
                    PayPalWebhookOutcome.Applied or
                    PayPalWebhookOutcome.Duplicate =>
                        Results.Ok(new
                        {
                            success = true,
                            duplicate =
                                result.Outcome ==
                                PayPalWebhookOutcome.Duplicate,
                            message = result.Message,
                            points = result.NewBalance,
                            lifetimeDonation =
                                result.LifetimeDonation
                        }),

                    PayPalWebhookOutcome.Unavailable =>
                        Results.Json(
                            new
                            {
                                success = false,
                                message = result.Message
                            },
                            statusCode:
                                StatusCodes.Status503ServiceUnavailable),

                    PayPalWebhookOutcome.SettlementFailed =>
                        Results.Conflict(new
                        {
                            success = false,
                            message = result.Message
                        }),

                    _ =>
                        Results.BadRequest(new
                        {
                            success = false,
                            message = result.Message
                        })
                };
            });

        return app;
    }
}
