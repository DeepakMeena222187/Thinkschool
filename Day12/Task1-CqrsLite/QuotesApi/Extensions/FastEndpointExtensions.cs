using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Extensions;

public static class FastEndpointExtensions
{
    // Same fan-out bound as the slow endpoint - see SlowEndpointExtensions.cs.
    // Kept identical so the two endpoints are an apples-to-apples comparison
    // over the same set of UserIds.
    private const int MaxDistinctUsers = 200;

    public static void MapFastEndpoints(this WebApplication app)
    {
        app.MapGet("/api/users-with-events-fast", async (
            QuotesDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.FastEndpoint");

            // Query 1 of 2: get the distinct UserIds to fan out over - identical
            // to the slow endpoint's first query.
            var userIds = await db.EventLogs
                .Select(e => e.UserId)
                .Distinct()
                .Take(MaxDistinctUsers)
                .ToListAsync(ct);

            // Query 2 of 2: every matching EventLog row for all of those UserIds
            // in a single round-trip (WHERE UserId IN (...)), projected down to
            // only the columns the response needs instead of full entities.
            var rows = await db.EventLogs
                .Where(e => userIds.Contains(e.UserId))
                .Select(e => new
                {
                    e.Id,
                    e.UserId,
                    e.EventType,
                    e.CreatedAtUtc,
                    e.Payload
                })
                .ToListAsync(ct);

            logger.LogInformation(
                "users-with-events-fast: fetched {RowCount} rows for {UserCount} distinct UserIds in one database round-trip instead of {UserCount} separate ones",
                rows.Count,
                userIds.Count,
                userIds.Count);

            // Grouping happens here, in memory over the already-materialized
            // List<T> (LINQ-to-Objects) - not translated to SQL, since EF Core
            // cannot translate a GroupBy that feeds a projection like this one
            // into a single round-trip without changing the query shape.
            var eventsByUser = rows
                .GroupBy(r => r.UserId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var result = new List<object>(userIds.Count);

            foreach (var userId in userIds)
            {
                var events = eventsByUser.TryGetValue(userId, out var userEvents)
                    ? userEvents
                    : [];

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
