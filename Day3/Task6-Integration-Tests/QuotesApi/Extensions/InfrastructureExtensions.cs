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
            // The test host (Quotes.Tests.Integration.IntegrationTestFactory) owns the
            // QuotesDbContext registration in this environment - it wires up a fresh,
            // isolated SQLite database per test run. Registering a provider here too
            // would make EF Core see two providers configured for the same context.
        }
        else
        {
            var connection = configuration.GetConnectionString("Quotes")
                ?? "Data Source=quotes.db";

            services.AddDbContext<QuotesDbContext>(options =>
                options.UseSqlite(connection));
        }

        services.AddScoped<IQuoteRepository, EfQuoteRepository>();
        services.AddScoped<ICollectionRepository, EfCollectionRepository>();
        services.AddScoped<AuthService>();

        return services;
    }
}
