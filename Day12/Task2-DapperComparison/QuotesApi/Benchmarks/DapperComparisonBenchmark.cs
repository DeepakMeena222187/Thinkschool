using System.Diagnostics;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Features.Quotes;

namespace QuotesApi.Benchmarks;

public static class DapperComparisonBenchmark
{
    private const int Iterations = 20;
    private const int CurrentUserId = 1;

    // Cast to BIT, not bare INT - see the matching comment in
    // GetQuoteListQueryDapperHandler.cs on why an uncast CASE breaks Dapper's
    // constructor-based materialization for QuoteListItemDto's bool parameter.
    private const string DapperSql = """
        SELECT Id, Author, Text, CreatedAtUtc,
               CAST(CASE WHEN OwnerId = @CurrentUserId THEN 1 ELSE 0 END AS BIT) AS IsOwnedByCurrentUser
        FROM Quotes
        ORDER BY CreatedAtUtc DESC
        """;

    public static void Run(string connectionString)
    {
        Console.WriteLine("=== EF Core vs Dapper: GetQuoteListQuery Benchmark ===");
        Console.WriteLine($"{Iterations} iterations each, CurrentUserId={CurrentUserId}, same live data for both");
        Console.WriteLine();

        var efResult = Measure("EF Core (LINQ projection)", () => RunEfCore(connectionString));
        var dapperResult = Measure("Dapper (raw SQL)", () => RunDapper(connectionString));

        PrintSummary(efResult, dapperResult);
    }

    private static List<QuoteListItemDto> RunEfCore(string connectionString)
    {
        var options = new DbContextOptionsBuilder<QuotesDbContext>().UseSqlServer(connectionString).Options;
        using var db = new QuotesDbContext(options);

        return db.Quotes
            .AsNoTracking()
            .OrderByDescending(q => q.CreatedAtUtc)
            .Select(q => new QuoteListItemDto(
                q.Id,
                q.Author,
                q.Text,
                q.CreatedAtUtc,
                q.OwnerId == CurrentUserId))
            .ToList(); // synchronous - see Measure()'s comment on why
    }

    private static List<QuoteListItemDto> RunDapper(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);

        // Query<T> (not QueryAsync<T>, which the real handler in
        // GetQuoteListQueryDapperHandler.cs uses) for the same reason the EF Core
        // side above uses ToList() and not ToListAsync() - see Measure().
        return connection.Query<QuoteListItemDto>(DapperSql, new { CurrentUserId }).AsList();
    }

    private static BenchmarkResult Measure(string label, Func<List<QuoteListItemDto>> run)
    {
        // Warm-up: JIT, connection pool, SQL Server query plan cache. Not measured.
        run();

        var elapsedMs = new double[Iterations];
        long totalAllocated = 0;
        var rowsRead = 0;

        for (var i = 0; i < Iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            // `run` is synchronous end-to-end (ToList()/Query<T>, not ToListAsync()/
            // QueryAsync<T>) so no await inside this bracket can hand the continuation
            // to a different thread-pool thread. GC.GetAllocatedBytesForCurrentThread()
            // is a per-thread counter - an awaited call resuming elsewhere would make
            // allocBefore/allocAfter read two different threads' counters and produce a
            // nonsensical (even negative) delta. Same pitfall Day10's
            // ChangeTrackerBenchmark hit with ToListAsync(); same fix here.
            var rows = run();

            sw.Stop();
            var allocAfter = GC.GetAllocatedBytesForCurrentThread();

            elapsedMs[i] = sw.Elapsed.TotalMilliseconds;
            totalAllocated += allocAfter - allocBefore;
            rowsRead = rows.Count;
        }

        var result = new BenchmarkResult(label, rowsRead, elapsedMs.Average(), totalAllocated / Iterations);

        Console.WriteLine($"{label}: {result.RowsRead:N0} rows, avg {result.AvgElapsedMs:N2} ms over {Iterations} runs, avg {result.AvgAllocatedBytes:N0} bytes allocated/run");

        return result;
    }

    private static void PrintSummary(BenchmarkResult ef, BenchmarkResult dapper)
    {
        var elapsedDeltaMs = ef.AvgElapsedMs - dapper.AvgElapsedMs;
        var elapsedPct = ef.AvgElapsedMs == 0 ? 0 : elapsedDeltaMs / ef.AvgElapsedMs * 100;

        var allocDelta = ef.AvgAllocatedBytes - dapper.AvgAllocatedBytes;
        var allocPct = ef.AvgAllocatedBytes == 0 ? 0 : (double)allocDelta / ef.AvgAllocatedBytes * 100;

        Console.WriteLine();
        Console.WriteLine("--- Summary ---");
        Console.WriteLine($"{"Variant",-28} {"Avg elapsed (ms)",18} {"Avg allocated (bytes)",22}");
        Console.WriteLine($"{ef.Label,-28} {ef.AvgElapsedMs,18:N2} {ef.AvgAllocatedBytes,22:N0}");
        Console.WriteLine($"{dapper.Label,-28} {dapper.AvgElapsedMs,18:N2} {dapper.AvgAllocatedBytes,22:N0}");
        Console.WriteLine();
        Console.WriteLine($"Dapper saved {elapsedDeltaMs:N2} ms ({elapsedPct:N1}%) and {allocDelta:N0} bytes ({allocPct:N1}%) versus EF Core, per call.");
    }

    private sealed record BenchmarkResult(string Label, int RowsRead, double AvgElapsedMs, long AvgAllocatedBytes);
}
