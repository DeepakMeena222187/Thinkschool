using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Services;

namespace QuotesApi.Tests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSecret = "this-is-a-test-secret-long-enough-for-hs256-1234";

    public CustomWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
    }

    public FakeClock Clock { get; } = new()
    {
        UtcNow = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero)
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = TestJwtSecret,
                ["Jwt:Issuer"] = "https://localhost",
                ["Jwt:Audience"] = "quotes-api"
            });
        });

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
