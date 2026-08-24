using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public interface IQuoteRepository
{
    Task<(IReadOnlyList<Quote> Items, int Total)> GetPageAsync(int page, int size, CancellationToken ct);
    Task<Quote?> GetByIdAsync(int id, CancellationToken ct);
    Task<Quote> AddAsync(Quote quote, CancellationToken ct);
    Task<bool> DeleteAsync(int id, CancellationToken ct);
}

public sealed class EfQuoteRepository(QuotesDbContext db) : IQuoteRepository
{
    public async Task<(IReadOnlyList<Quote> Items, int Total)> GetPageAsync(
        int page, int size, CancellationToken ct)
    {
        var query = db.Quotes.AsNoTracking().OrderBy(q => q.Id);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return (items, total);
    }

    public Task<Quote?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Quotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id, ct);

    public async Task<Quote> AddAsync(Quote quote, CancellationToken ct)
    {
        db.Quotes.Add(quote);
        await db.SaveChangesAsync(ct);
        return quote;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null) return false;

        // QuoteTags isn't mapped in this project's EF model (no QuoteTag
        // entity/DbSet exists anywhere in this codebase) - it was added to
        // the shared database by a later day's migration this snapshot
        // never modeled, so its rows have to be removed via raw SQL before
        // the Quote row can be deleted, or SQL Server rejects the delete
        // via FK_QuoteTags_Quotes. Both deletes run in one transaction so
        // a failure partway through can't leave the Quote gone with its
        // tags still lingering, or vice versa.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM QuoteTags WHERE QuoteId = {id}", ct);

        db.Quotes.Remove(quote);
        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        return true;
    }
}
