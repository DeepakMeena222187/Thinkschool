using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OrderRefactor.Tests;

public sealed class OrderApiIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public OrderApiIntegrationTests(
        WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task PostOrders_ReturnsCreatedOrder()
    {
        var request = new
        {
            customerName = "Integration Test Customer",
            customerEmail = "integration@example.com",
            address = "123 Test Street",
            region = "US",
            itemCount = 1,
            items = new[]
            {
                new
                {
                    productCode = "TEST-001",
                    quantity = 2,
                    unitPrice = 10.00m
                }
            },
            rushShipping = false
        };

        var response = await client.PostAsJsonAsync(
            "/api/orders",
            request);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<OrderResponseDto>();

        Assert.NotNull(body);
        Assert.True(body!.Id > 0);
        Assert.True(body.OrderNumber > 0);
        Assert.Equal(34.10m, body.TotalAmount);
        Assert.Equal(1, body.ItemCount);
    }

    private sealed record OrderResponseDto(
        int Id,
        int OrderNumber,
        decimal TotalAmount,
        int ItemCount);
}
