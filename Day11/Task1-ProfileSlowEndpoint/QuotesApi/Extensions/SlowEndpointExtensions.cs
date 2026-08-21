using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Extensions;

public static class SlowEndpointExtensions
{
    // Bounds a single request to a fixed, still-clearly-N+1 number of
    // round-trips: enough to make the per-request DB round-trip count show up
    // as a wall of spans in tracing and as real latency under load, without
    // making a single request (or a load test against it) impractically slow.
    private const int MaxDistinctUsers = 200;

    public static void MapSlowEndpoints(this WebApplication app)
    {
        app.MapGet("/api/users-with-events-slow", async (
            QuotesDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.SlowEndpoint");

            // Query 1 of (up to) 201: get the distinct UserIds to fan out over.
            var userIds = await db.EventLogs
                .Select(e => e.UserId)
                .Distinct()
                .Take(MaxDistinctUsers)
                .ToListAsync(ct);

            logger.LogInformation(
                "users-with-events-slow: fanning out over {UserCount} distinct UserIds, one query each",
                userIds.Count);

            var result = new List<object>(userIds.Count);

            // Deliberate N+1: a separate round-trip per UserId instead of one
            // query with `WHERE UserId IN (...)` or a GROUP BY. This is the
            // shape being profiled, not an oversight - see README.md.
            foreach (var userId in userIds)
            {
                var events = await db.EventLogs
                    .Where(e => e.UserId == userId)
                    .ToListAsync(ct);

                result.Add(new
                {
                    userId,
                    eventCount = events.Count,
                    events
                });
            }

            return Results.Ok(result);
        });
    }
}
