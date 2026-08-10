using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Quotes")
            ?? "Data Source=quotes.db";

        services.AddDbContext<QuotesDbContext>(options =>
            options.UseSqlite(connection));

        services.AddScoped<IQuoteRepository, EfQuoteRepository>();
        return services;
    }
}
