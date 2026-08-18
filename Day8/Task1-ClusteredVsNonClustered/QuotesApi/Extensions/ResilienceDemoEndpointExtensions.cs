using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class ResilienceDemoEndpointExtensions
{
    public const string QuoteSourceHttpClientName = "quote-source";

    public static void MapResilienceDemoEndpoints(this WebApplication app)
    {
        app.MapGet("/api/flaky", (FlakyEndpointState state, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Flaky");

            if (state.ShouldFail())
            {
                logger.LogWarning("Flaky endpoint returning 503");
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            logger.LogInformation("Flaky endpoint returning 200");
            return Results.Ok(new { message = "ok" });
        });

        app.MapGet("/api/quote-of-the-day", async (
            IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.QuoteOfTheDay");
            var client = httpClientFactory.CreateClient(QuoteSourceHttpClientName);

            logger.LogInformation("Calling quote source through resilience pipeline");

            // No try/catch here: if the resilience pipeline exhausts its
            // retries (or the circuit is open), the exception must propagate
            // so ExceptionMiddleware surfaces the real failure instead of the
            // caller silently getting a fabricated success response.
            var response = await client.GetAsync("/api/flaky", ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            logger.LogInformation("Quote source responded with {StatusCode}", (int)response.StatusCode);

            return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
        });
    }
}
