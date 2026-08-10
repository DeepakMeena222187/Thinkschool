using Microsoft.Extensions.Logging.Abstractions;
using OrderRefactor.DTOs;
using OrderRefactor.Models;
using OrderRefactor.Repositories;
using OrderRefactor.Services;

namespace OrderRefactor.Tests;

public sealed class OrderServiceTests
{
    [Fact]
    public async Task CreateOrderAsync_CalculatesTaxAndShipping()
    {
        var repository = new FakeOrderRepository();
        var service = new OrderService(
            repository,
            NullLogger<OrderService>.Instance);

        var request = new CreateOrderRequest(
            "Alice",
            "alice@example.com",
            "123 Main St",
            "US",
            1,
            [
                new CreateOrderItemRequest("ABC", 2, 10m)
            ]);

        var result = await service.CreateOrderAsync(
            request,
            CancellationToken.None);

        Assert.True(result.Success);

        // Subtotal = 20
        // Shipping = 12.50
        // Tax = 1.60
        // Total = 34.10
        Assert.Equal(34.10m, result.TotalAmount);
    }

    [Fact]
    public async Task CreateOrderAsync_AppliesDiscount_WhenSubtotalReaches500()
    {
        var repository = new FakeOrderRepository();
        var service = new OrderService(
            repository,
            NullLogger<OrderService>.Instance);

        var request = new CreateOrderRequest(
            "Bob",
            "bob@example.com",
            "456 Main St",
            "US",
            1,
            [
                new CreateOrderItemRequest("EXPENSIVE", 1, 500m)
            ]);

        var result = await service.CreateOrderAsync(
            request,
            CancellationToken.None);

        Assert.True(result.Success);

        // 500 - 10% discount = 450
        // 450 + 12.50 shipping + 36 tax = 498.50
        Assert.Equal(486m, result.TotalAmount);
    }

    [Fact]
    public async Task CreateOrderAsync_Throws_WhenItemCountDoesNotMatchItems()
    {
        var repository = new FakeOrderRepository();
        var service = new OrderService(
            repository,
            NullLogger<OrderService>.Instance);

        var request = new CreateOrderRequest(
            "Charlie",
            "charlie@example.com",
            null,
            "US",
            2,
            [
                new CreateOrderItemRequest("ABC", 1, 10m)
            ]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateOrderAsync(
                request,
                CancellationToken.None));
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        private readonly List<Order> orders = [];

        public Task<Customer?> GetCustomerAsync(
            string email,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<Customer?>(null);
        }

        public Task<int> GetNextOrderNumberAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(1001);
        }

        public Task AddAsync(
            Order order,
            CancellationToken cancellationToken)
        {
            orders.Add(order);
            order.Id = orders.Count;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

