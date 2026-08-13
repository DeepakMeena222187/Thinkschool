using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;
using Xunit;

namespace Quotes.Tests.Integration.Testcontainers;

/// <summary>
/// Behavior that only shows up against a real SQL Server engine - constraints enforced by
/// the server itself, and data surviving a brand-new physical connection - rather than the
/// forgiving, single-connection semantics of in-memory SQLite.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerSpecificTests(SqlServerContainerFixture _fixture)
{
    [Fact]
    public async Task UniqueIndex_OnUserEmail_IsEnforcedByTheServer()
    {
        using var factory = new IntegrationTestFactory(_fixture.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        db.Users.Add(new User { Email = "duplicate@quotes.local", PasswordHash = "hash-one" });
        await db.SaveChangesAsync();

        db.Users.Add(new User { Email = "duplicate@quotes.local", PasswordHash = "hash-two" });
        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Quote_WrittenThroughApi_IsReadableFromABrandNewPhysicalConnection()
    {
        using var factory = new IntegrationTestFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();
        var token = TestAuth.CreateInternalToken(1, new System.Security.Claims.Claim(
            QuotesApi.Authorization.QuotePolicies.ScopeClaimType, QuotesApi.Authorization.QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/api/quotes", new QuotesApi.Contracts.CreateQuoteRequest("Persisted Author", "Durable across connections."));
        response.EnsureSuccessStatusCode();

        // A fresh options/connection independent of anything the running host holds open -
        // proves the row is durably stored on the server, not just visible on a shared handle.
        var freshOptions = new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlServer(factory.ConnectionString)
            .Options;
        await using var freshDb = new QuotesDbContext(freshOptions);
        var stored = await freshDb.Quotes.AsNoTracking().SingleAsync(q => q.Author == "Persisted Author");

        stored.Text.Should().Be("Durable across connections.");
    }

    [Fact]
    public async Task Database_RunsOnRealSqlServerEngine()
    {
        using var factory = new IntegrationTestFactory(_fixture.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        await using var connection = (SqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@VERSION";
        var version = (string)(await command.ExecuteScalarAsync())!;

        version.Should().Contain("Microsoft SQL Server");
    }
}
