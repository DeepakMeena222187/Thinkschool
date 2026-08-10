using Microsoft.Extensions.Logging;
using OrderRefactor.DTOs;
using OrderRefactor.Models;
using OrderRefactor.Repositories;

namespace OrderRefactor.Services;

public sealed class OrderService(
    IOrderRepository repository,
    ILogger<OrderService> logger) : IOrderService
{
    private const decimal TaxRate = 0.08m;
    private const decimal StandardShipping = 12.50m;
    private const decimal RushShipping = 25.00m;
    private const decimal FreeShippingThreshold = 100m;
    private const decimal DiscountThreshold = 500m;
    private const decimal DiscountRate = 0.10m;

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
        var discount = subtotal >= DiscountThreshold
            ? subtotal * DiscountRate
            : 0m;

        var discountedSubtotal = subtotal - discount;

        var shipping = request.RushShipping
            ? RushShipping
            : discountedSubtotal >= FreeShippingThreshold
                ? 0m
                : StandardShipping;

        var tax = discountedSubtotal * TaxRate;
        var total = discountedSubtotal + shipping + tax;

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

        if (string.IsNullOrWhiteSpace(request.CustomerName))
            throw new ArgumentException("Customer name is required.");

        if (string.IsNullOrWhiteSpace(request.CustomerEmail))
            throw new ArgumentException("Customer email is required.");

        if (string.IsNullOrWhiteSpace(request.Region))
            throw new ArgumentException("Region is required.");
    }
}
