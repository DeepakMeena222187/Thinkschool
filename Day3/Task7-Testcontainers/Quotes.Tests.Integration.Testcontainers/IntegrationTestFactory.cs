using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.Data;
using QuotesApi.Services;

namespace Quotes.Tests.Integration.Testcontainers;

/// <summary>
/// Boots the real QuotesApi pipeline (real DI, real middleware, real EF Core, real
/// authentication/authorization) over a dedicated, uniquely named database on the shared
/// Testcontainers SQL Server instance. One instance = one database: tests must construct
/// their own factory (never share one via IClassFixture) so that state never leaks between
/// tests, even though the underlying server process is shared for speed.
/// </summary>
public sealed class IntegrationTestFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSecret = "integration-tests-jwt-signing-secret-32-bytes-min";
    public const string TestEntraTenantId = "33333333-3333-3333-3333-333333333333";
    public const string TestEntraClientId = "44444444-4444-4444-4444-444444444444";

    private readonly string _connectionString;

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero));

    /// <summary>This test's own isolated database on the shared Testcontainers SQL Server instance.</summary>
    public string ConnectionString => _connectionString;

    public IntegrationTestFactory(string containerConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(containerConnectionString)
        {
            InitialCatalog = $"QuotesApiTests_{Guid.NewGuid():N}"
        };
        _connectionString = builder.ConnectionString;

        // Program.cs seeds the default admin user as part of its top-level statements, which
        // run synchronously while the host starts (before ConfigureWebHost's caller gets
        // control back). The schema must already exist by then, so migrations are applied
        // here - against this test's own database - via a throwaway context rather than
        // after the host is built.
        var options = new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlServer(_connectionString)
            .Options;
        using var db = new QuotesDbContext(options);
        db.Database.Migrate();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = TestJwtSecret,
                ["Jwt:Issuer"] = "https://localhost",
                ["Jwt:Audience"] = "quotes-api",
                ["Entra:TenantId"] = TestEntraTenantId,
                ["Entra:ClientId"] = TestEntraClientId
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<QuotesDbContext>>();
            services.RemoveAll<QuotesDbContext>();
            services.AddDbContext<QuotesDbContext>(options => options.UseSqlServer(_connectionString));

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            // Drop this test's database so the shared container doesn't accumulate one
            // database per test for the lifetime of the run.
            var options = new DbContextOptionsBuilder<QuotesDbContext>()
                .UseSqlServer(_connectionString)
                .Options;
            using var db = new QuotesDbContext(options);
            db.Database.EnsureDeleted();
        }
    }
}
