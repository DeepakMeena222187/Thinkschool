using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Benchmarks;

public static class ChangeTrackerBenchmark
{
    private const int RowCount = 10_000;

    public static async Task RunAsync(string connectionString)
    {
        Console.WriteLine("=== EF Core Change Tracker Benchmark ===");
        Console.WriteLine();

        await RunIdentityResolutionDemoAsync(connectionString);

        Console.WriteLine();
        Console.WriteLine($"--- Timing/allocation: reading {RowCount:N0} EventLog rows ---");
        Console.WriteLine();

        var tracked = await MeasureAsync(connectionString, "Tracked (default)", noTracking: false);
        var untracked = await MeasureAsync(connectionString, "AsNoTracking", noTracking: true);

        PrintSummary(tracked, untracked);
    }

    private static async Task RunIdentityResolutionDemoAsync(string connectionString)
    {
        Console.WriteLine("--- Identity resolution demo ---");

        var options = BuildOptions(connectionString);

        await using (var context = new QuotesDbContext(options))
        {
            var first = await context.EventLogs.FirstAsync(e => e.Id == 1);
            var second = await context.EventLogs.FirstAsync(e => e.Id == 1);

            Console.WriteLine($"Tracked query, same DbContext: ReferenceEquals(first, second) = {ReferenceEquals(first, second)}");
        }

        await using (var context = new QuotesDbContext(options))
        {
            var first = await context.EventLogs.AsNoTracking().FirstAsync(e => e.Id == 1);
            var second = await context.EventLogs.AsNoTracking().FirstAsync(e => e.Id == 1);

            Console.WriteLine($"AsNoTracking query, same DbContext: ReferenceEquals(first, second) = {ReferenceEquals(first, second)}");
        }
    }

    private static async Task<BenchmarkResult> MeasureAsync(string connectionString, string label, bool noTracking)
    {
        var options = BuildOptions(connectionString);

        await using var context = new QuotesDbContext(options);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();

        // ToList() (not ToListAsync()) deliberately - GC.GetAllocatedBytesForCurrentThread()
        // is a per-thread counter, and an awaited call can resume its continuation on a
        // different thread-pool thread, making the "after" reading come from a different
        // thread's counter than "before" and producing a nonsensical (even negative) delta.
        // Keeping this span synchronous guarantees allocBefore/allocAfter are the same thread.
        var query = context.EventLogs.OrderBy(e => e.Id).Take(RowCount);
        var rows = (noTracking ? query.AsNoTracking() : query).ToList();

        sw.Stop();
        var allocAfter = GC.GetAllocatedBytesForCurrentThread();

        var result = new BenchmarkResult(label, rows.Count, sw.Elapsed.TotalMilliseconds, allocAfter - allocBefore);

        Console.WriteLine($"{label}: {result.RowsRead:N0} rows, {result.ElapsedMs:N1} ms, {result.AllocatedBytes:N0} bytes allocated");

        return result;
    }

    private static void PrintSummary(BenchmarkResult tracked, BenchmarkResult untracked)
    {
        var elapsedDeltaMs = tracked.ElapsedMs - untracked.ElapsedMs;
        var elapsedPct = tracked.ElapsedMs == 0 ? 0 : elapsedDeltaMs / tracked.ElapsedMs * 100;

        var allocDelta = tracked.AllocatedBytes - untracked.AllocatedBytes;
        var allocPct = tracked.AllocatedBytes == 0 ? 0 : (double)allocDelta / tracked.AllocatedBytes * 100;

        Console.WriteLine();
        Console.WriteLine("--- Summary ---");
        Console.WriteLine($"{"Variant",-20} {"Elapsed (ms)",15} {"Allocated (bytes)",20}");
        Console.WriteLine($"{tracked.Label,-20} {tracked.ElapsedMs,15:N1} {tracked.AllocatedBytes,20:N0}");
        Console.WriteLine($"{untracked.Label,-20} {untracked.ElapsedMs,15:N1} {untracked.AllocatedBytes,20:N0}");
        Console.WriteLine();
        Console.WriteLine($"AsNoTracking saved {elapsedDeltaMs:N1} ms ({elapsedPct:N1}%) and {allocDelta:N0} bytes ({allocPct:N1}%) versus tracked.");
    }

    private static DbContextOptions<QuotesDbContext> BuildOptions(string connectionString)
        => new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlServer(connectionString)
            .Options;

    private sealed record BenchmarkResult(string Label, int RowsRead, double ElapsedMs, long AllocatedBytes);
}
