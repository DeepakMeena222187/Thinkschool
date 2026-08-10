using Microsoft.EntityFrameworkCore;
using OrderRefactor.Data;
using OrderRefactor.Models;

namespace OrderRefactor.Repositories;

public sealed class OrderRepository(OrderDbContext db) : IOrderRepository
{
    public Task<Customer?> GetCustomerAsync(
        string email,
        CancellationToken cancellationToken) =>
        db.Customers.FirstOrDefaultAsync(
            x => x.Email == email,
            cancellationToken);

    public async Task<int> GetNextOrderNumberAsync(
        CancellationToken cancellationToken)
    {
        var maximum = await db.Orders
            .Select(x => (int?)x.OrderNumber)
            .MaxAsync(cancellationToken);

        return (maximum ?? 1000) + 1;
    }

    public async Task AddAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        await db.Orders.AddAsync(order, cancellationToken);
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);
}
