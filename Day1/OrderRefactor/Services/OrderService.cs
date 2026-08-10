using Microsoft.Extensions.Logging;
using OrderRefactor.DTOs;
using OrderRefactor.Models;
using OrderRefactor.Repositories;

namespace OrderRefactor.Services;

public sealed class OrderService(
    IOrderRepository repository,
    ILogger<OrderService> logger,
    IOrderPricingStrategy pricingStrategy) : IOrderService
{

    public async Task<OrderResult> CreateOrderAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var customer = await repository.GetCustomerAsync(
            request.CustomerEmail,
            cancellationToken);

        if (customer is null)
        {
            customer = new Customer
            {
                Name = request.CustomerName.Trim(),
                Email = request.CustomerEmail.Trim(),
                Address = request.Address?.Trim(),
                Region = request.Region.Trim()
            };
        }
        else
        {
            customer.Name = request.CustomerName.Trim();
            customer.Address = request.Address?.Trim();
            customer.Region = request.Region.Trim();
        }

        var orderNumber = await repository.GetNextOrderNumberAsync(
            cancellationToken);

        var items = request.Items
            .Select(item => new OrderItem
            {
                ProductCode = item.ProductCode.Trim(),
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice
            })
            .ToList();

        var subtotal = items.Sum(x => x.Quantity * x.UnitPrice);
        var pricing = pricingStrategy.Calculate(
            subtotal,
            request.RushShipping);

        var total = pricing.Total;

        var order = new Order
        {
            Customer = customer,
            OrderNumber = orderNumber,
            Status = "New",
            Notes = request.RushShipping
                ? "Rush shipping requested"
                : "",
            TotalAmount = total,
            CreatedAt = DateTime.UtcNow,
            Items = items
        };

        await repository.AddAsync(order, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Created order {OrderNumber} with {ItemCount} items and total {Total}",
            order.OrderNumber,
            items.Count,
            total);

        return new OrderResult(
            true,
            order.Id,
            order.OrderNumber,
            null,
            total);
    }

    private static void ValidateRequest(CreateOrderRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new ArgumentException("At least one order item is required.");

        if (request.ItemCount != request.Items.Count)
            throw new ArgumentException(
                "ItemCount must match the number of supplied items.");

        if (request.Items.Any(item => item.Quantity <= 0))
            throw new ArgumentException("Order item quantity must be greater than zero.");

        if (string.IsNullOrWhiteSpace(request.CustomerName))
            throw new ArgumentException("Customer name is required.");

        if (string.IsNullOrWhiteSpace(request.CustomerEmail))
            throw new ArgumentException("Customer email is required.");

        if (string.IsNullOrWhiteSpace(request.Region))
            throw new ArgumentException("Region is required.");
    }
}
