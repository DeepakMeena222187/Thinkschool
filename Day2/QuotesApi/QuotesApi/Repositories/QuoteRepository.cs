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
        var query = db.Quotes.AsNoTracking()
            .Where(q => !q.IsDeleted)
            .OrderBy(q => q.Id);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
        return (items, total);
    }

    public Task<Quote?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Quotes.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, ct);

    public async Task<Quote> AddAsync(Quote quote, CancellationToken ct)
    {
        db.Quotes.Add(quote);
        await db.SaveChangesAsync(ct);
        return quote;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == id && !q.IsDeleted, ct);
        if (quote is null) return false;

        quote.Delete();
        await db.SaveChangesAsync(ct);
        return true;
    }
}
