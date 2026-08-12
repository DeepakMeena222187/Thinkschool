using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.Data;
using QuotesApi.Services;

namespace Quotes.Tests.Integration;

/// <summary>
/// Boots the real QuotesApi pipeline (real DI, real middleware, real EF Core) over an
/// isolated in-memory SQLite database and a fake clock. One instance = one database:
/// tests must construct their own factory (never share one via IClassFixture) so that
/// state never leaks between tests.
/// </summary>
public sealed class IntegrationTestFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSecret = "integration-tests-jwt-signing-secret-32-bytes-min";
    public const string TestEntraTenantId = "33333333-3333-3333-3333-333333333333";
    public const string TestEntraClientId = "44444444-4444-4444-4444-444444444444";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero));

    public IntegrationTestFactory()
    {
        // The SQLite in-memory provider tears down the database the instant the last
        // connection to it closes, so the connection is opened here and held for the
        // lifetime of the factory to keep this instance's database alive and private.
        _connection.Open();

        // Program.cs seeds the default admin user as part of its top-level statements,
        // which run synchronously while the host starts (before ConfigureWebHost's
        // caller gets control back). The schema must already exist on this connection
        // by then, so migrations are applied here via a throwaway context rather than
        // after the host is built.
        var options = new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlite(_connection)
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
            services.AddDbContext<QuotesDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
