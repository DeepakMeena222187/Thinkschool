using System.ComponentModel.DataAnnotations;

namespace OrderRefactor.DTOs;

public sealed record CreateOrderRequest(
    [param: Required] string CustomerName,
    [param: Required, EmailAddress] string CustomerEmail,
    string? Address,
    [param: Required] string Region,
    [param: Range(1, 100)] int ItemCount,
    [param: Required] List<CreateOrderItemRequest> Items,
    bool RushShipping = false);

public sealed record CreateOrderItemRequest(
    [param: Required] string ProductCode,
    [param: Range(1, 1000)] int Quantity,
    [param: Range(typeof(decimal), "0.01", "1000000")] decimal UnitPrice);

public sealed record OrderResponse(
    int Id,
    int OrderNumber,
    string Status,
    decimal TotalAmount,
    int ItemCount);

public sealed record OrderResult(
    bool Success,
    int? OrderId,
    int? OrderNumber,
    string? Error,
    decimal TotalAmount);
