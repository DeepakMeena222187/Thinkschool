using Azure.Identity;
using Azure.Messaging.ServiceBus;
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

        services.AddSingleton<IEventQueue, EventQueue>();
        services.AddHostedService<EventLogDrainService>();

        if (environment.IsEnvironment("Testing"))
        {
            // No Service Bus namespace available to the test host - same reasoning
            // as skipping the SQL Server provider registration above.
            services.AddSingleton<IQuoteEventPublisher, NullQuoteEventPublisher>();
        }
        else
        {
            var serviceBusNamespace = configuration["ServiceBus:Namespace"]
                ?? throw new InvalidOperationException("Missing required configuration value: ServiceBus:Namespace.");

            // DefaultAzureCredential, no connection string / SAS key - same managed
            // identity approach as the Day 17 SQL access and Key Vault reads. See
            // ServiceBusCredentialFactory for why the chain is constrained per
            // environment rather than left at its full default.
            var credential = ServiceBusCredentialFactory.Create(environment.IsProduction());

            services.AddSingleton(_ => new ServiceBusClient(serviceBusNamespace, credential));
            services.AddSingleton<IQuoteEventPublisher, ServiceBusQuoteEventPublisher>();
        }

        return services;
    }
}
