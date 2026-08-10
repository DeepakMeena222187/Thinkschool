using Microsoft.AspNetCore.Mvc;
using OrderRefactor.DTOs;
using OrderRefactor.Services;

namespace OrderRefactor.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrderController(IOrderService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderResponse>> Create(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        try
        {
            var result = await service.CreateOrderAsync(
                request,
                cancellationToken);

            if (!result.Success)
            {
                return BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Order creation failed",
                    Detail = result.Error
                });
            }

            var response = new OrderResponse(
                result.OrderId!.Value,
                result.OrderNumber!.Value,
                "New",
                result.TotalAmount,
                request.Items.Count);

            return Created(
                $"/api/orders/{response.Id}",
                response);
        }
        catch (ArgumentException ex)
        {
            var details = new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    ["request"] = [ex.Message]
                })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed"
            };

            return BadRequest(details);
        }
    }
}
