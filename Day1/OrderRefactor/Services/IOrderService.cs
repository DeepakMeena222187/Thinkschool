using OrderRefactor.DTOs;

namespace OrderRefactor.Services;

public interface IOrderService
{
    Task<OrderResult> CreateOrderAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken);
}
