using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public sealed class EfCollectionRepository(QuotesDbContext db) : ICollectionRepository
{
    public Task<Collection?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Collections.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == id, ct);

    // Deliberately no Include here: GetAllAsync loads bare collections so that
    // callers who need items are forced into a separate per-collection query
    // (see GetItemsAsync). This is the Day 5 Task 1 N+1 under diagnosis.
    public async Task<IReadOnlyList<Collection>> GetAllAsync(CancellationToken ct) =>
        await db.Collections.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<CollectionItem>> GetItemsAsync(int collectionId, CancellationToken ct) =>
        await db.Collections
            .Where(c => c.Id == collectionId)
            .SelectMany(c => c.Items)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task AddAsync(Collection collection, CancellationToken ct)
    {
        db.Collections.Add(collection);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Collection collection, CancellationToken ct)
    {
        db.Collections.Update(collection);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var collection = await db.Collections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (collection is null)
        {
            return;
        }

        db.Collections.Remove(collection);
        await db.SaveChangesAsync(ct);
    }
}
