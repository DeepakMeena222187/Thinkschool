namespace OrderRefactor.Services;

public interface IOrderPricingStrategy
{
    OrderPricingResult Calculate(decimal subtotal, bool rushShipping);
}

public sealed record OrderPricingResult(
    decimal Discount,
    decimal DiscountedSubtotal,
    decimal Shipping,
    decimal Tax,
    decimal Total);

public sealed class DefaultOrderPricingStrategy : IOrderPricingStrategy
{
    private const decimal TaxRate = 0.08m;
    private const decimal StandardShipping = 12.50m;
    private const decimal RushShipping = 25.00m;
    private const decimal FreeShippingThreshold = 100m;
    private const decimal DiscountThreshold = 500m;
    private const decimal DiscountRate = 0.10m;

    public OrderPricingResult Calculate(decimal subtotal, bool rushShipping)
    {
        var discount = subtotal >= DiscountThreshold
            ? subtotal * DiscountRate
            : 0m;

        var discountedSubtotal = subtotal - discount;

        var shipping = rushShipping
            ? RushShipping
            : discountedSubtotal >= FreeShippingThreshold
                ? 0m
                : StandardShipping;

        var tax = discountedSubtotal * TaxRate;
        var total = discountedSubtotal + shipping + tax;

        return new OrderPricingResult(
            discount,
            discountedSubtotal,
            shipping,
            tax,
            total);
    }
}
