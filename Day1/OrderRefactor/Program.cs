using Microsoft.EntityFrameworkCore;
using OrderRefactor.Data;
using OrderRefactor.Repositories;
using OrderRefactor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseInMemoryDatabase("Orders"));

builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IOrderPricingStrategy, DefaultOrderPricingStrategy>();

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseMiddleware<OrderRefactor.Middleware.ExceptionMiddleware>();

app.MapControllers();

app.Run();

public partial class Program;

