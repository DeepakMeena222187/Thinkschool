using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        if (environment.IsEnvironment("Testing"))
        {
            // The test host (Quotes.Tests.Integration.Testcontainers.IntegrationTestFactory) owns
            // the QuotesDbContext registration in this environment - it wires up a fresh,
            // isolated SQL Server database (on a shared Testcontainers instance) per test.
            // Registering a provider here too would make EF Core see two providers configured
            // for the same context.
        }
        else
        {
            var connection = configuration.GetConnectionString("Quotes")
                ?? throw new InvalidOperationException("Missing required configuration value: ConnectionStrings:Quotes.");

            services.AddDbContext<QuotesDbContext>(options =>
                options.UseSqlServer(connection));
        }

        services.AddScoped<IQuoteRepository, EfQuoteRepository>();
        services.AddScoped<ICollectionRepository, EfCollectionRepository>();
        services.AddScoped<AuthService>();

        return services;
    }
}
