using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Extensions;

public static class CollectionSeedExtensions
{
    private const int SeedCollectionCount = 25;
    private const int ItemsPerCollection = 3;

    // Development-only, and only when the table is empty, so this never runs
    // against a shared or already-populated database. Seeds enough collections
    // for the GET /api/collections N+1 (Day 5 Task 1) to produce ~26 EF spans
    // in a single trace: 1 list query + 1 items query per collection.
    public static async Task SeedDevelopmentCollectionsAsync(this IServiceProvider services, IWebHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        if (await db.Collections.AnyAsync())
        {
            return;
        }

        var ownerId = await db.Users.OrderBy(u => u.Id).Select(u => u.Id).FirstOrDefaultAsync();
        if (ownerId == 0)
        {
            ownerId = 1;
        }

        for (var i = 1; i <= SeedCollectionCount; i++)
        {
            var collection = new Collection($"Seeded Collection {i:D2}", ownerId);
            for (var quoteId = 1; quoteId <= ItemsPerCollection; quoteId++)
            {
                collection.AddItem(quoteId);
            }

            db.Collections.Add(collection);
        }

        await db.SaveChangesAsync();
    }
}
