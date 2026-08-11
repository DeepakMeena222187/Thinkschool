using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Services;

namespace QuotesApi.Tests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public FakeClock Clock { get; } = new()
    {
        UtcNow = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero)
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IClock));

            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IClock>(Clock);
        });
    }
}
