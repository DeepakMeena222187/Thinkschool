using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Repositories;

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
            services.AddDbContext<QuotesDbContext>(options =>
                options.UseInMemoryDatabase("QuotesApiTests"));
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

        return services;
    }
}
