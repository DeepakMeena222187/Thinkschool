using QuotesApi.Models;

namespace QuotesApi.Repositories;

public interface ICollectionRepository
{
    Task<Collection?> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<Collection>> GetAllAsync(CancellationToken ct);
    Task AddAsync(Collection collection, CancellationToken ct);
    Task UpdateAsync(Collection collection, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
