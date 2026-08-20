using Microsoft.EntityFrameworkCore;
using QuotesApi.Contracts;
using QuotesApi.Data;

namespace QuotesApi.QueryTranslation;

public static class QueryTranslationDemo
{
    public static async Task RunAsync(string connectionString)
    {
        Console.WriteLine("=== EF Core Query Translation Demo ===");
        Console.WriteLine();

        // EnableSensitiveDataLogging() puts parameter *values* (not just parameter
        // names) into the logged SQL. That's fine for this local diagnostic run
        // against non-production data - it should never be turned on for a
        // production app, since it would leak real data values into logs.
        var options = new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlServer(connectionString)
            .LogTo(Console.WriteLine, LogLevel.Information)
            .EnableSensitiveDataLogging()
            .Options;

        await RunFullEntityQueryAsync(options);
        await RunProjectedQueryAsync(options);
        await RunClientEvaluationDemoAsync(options);
        await RunFixedQueryAsync(options);
    }

    private static async Task RunFullEntityQueryAsync(DbContextOptions<QuotesDbContext> options)
    {
        Console.WriteLine();
        Console.WriteLine("--- Full-entity query (selects every column) ---");
        Console.WriteLine();

        await using var context = new QuotesDbContext(options);

        var rows = await context.EventLogs
            .Where(e => e.EventType == "Login")
            .Take(100)
            .ToListAsync();

        Console.WriteLine($"Full-entity query returned {rows.Count} rows.");
    }

    private static async Task RunProjectedQueryAsync(DbContextOptions<QuotesDbContext> options)
    {
        Console.WriteLine();
        Console.WriteLine("--- Projected query (selects only Id, UserId, CreatedAtUtc) ---");
        Console.WriteLine();

        await using var context = new QuotesDbContext(options);

        var rows = await context.EventLogs
            .Where(e => e.EventType == "Login")
            .Select(e => new EventLogSummaryDto
            {
                Id = e.Id,
                UserId = e.UserId,
                CreatedAtUtc = e.CreatedAtUtc
            })
            .Take(100)
            .ToListAsync();

        Console.WriteLine($"Projected query returned {rows.Count} rows.");
    }

    private static async Task RunClientEvaluationDemoAsync(DbContextOptions<QuotesDbContext> options)
    {
        Console.WriteLine();
        Console.WriteLine("--- Client-side evaluation demo (expected to fail translation) ---");
        Console.WriteLine();

        await using var context = new QuotesDbContext(options);

        try
        {
            var rows = await context.EventLogs
                .Where(e => IsRecentEvent(e.CreatedAtUtc))
                .Take(10)
                .ToListAsync();

            Console.WriteLine($"Unexpected: query succeeded and returned {rows.Count} rows without throwing. " +
                "This would mean EF Core partially translated the query - inspect the logged SQL above to see what actually ran server-side versus what got evaluated client-side.");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine("Query threw InvalidOperationException, as expected for a non-translatable predicate:");
            Console.WriteLine(ex.Message);
        }
    }

    private static async Task RunFixedQueryAsync(DbContextOptions<QuotesDbContext> options)
    {
        Console.WriteLine();
        Console.WriteLine("--- Fixed query (same intent, fully translatable) ---");
        Console.WriteLine();

        await using var context = new QuotesDbContext(options);

        var cutoff = DateTime.UtcNow.AddDays(-7);

        var rows = await context.EventLogs
            .Where(e => e.CreatedAtUtc > cutoff)
            .Take(10)
            .ToListAsync();

        Console.WriteLine($"Fixed query succeeded and returned {rows.Count} rows.");
    }

    // Deliberately not translatable: EF Core has no SQL equivalent for an
    // arbitrary local method call, so any query that calls this inside a
    // Where() predicate fails at translation time.
    private static bool IsRecentEvent(DateTime createdAtUtc)
        => (DateTime.UtcNow - createdAtUtc).TotalDays < 7;
}
