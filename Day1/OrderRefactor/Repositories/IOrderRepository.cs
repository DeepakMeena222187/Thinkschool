using OrderRefactor.Models;

namespace OrderRefactor.Repositories;

public interface IOrderRepository
{
    Task<Customer?> GetCustomerAsync(
        string email,
        CancellationToken cancellationToken);

    Task<int> GetNextOrderNumberAsync(
        CancellationToken cancellationToken);

    Task AddAsync(
        Order order,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(
        CancellationToken cancellationToken);
}
