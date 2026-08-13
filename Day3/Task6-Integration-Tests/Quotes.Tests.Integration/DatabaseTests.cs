using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Authorization;
using QuotesApi.Contracts;
using QuotesApi.Data;
using Xunit;

namespace Quotes.Tests.Integration;

public sealed class DatabaseTests
{
    [Fact]
    public async Task Database_OnStartup_AppliesAllMigrations()
    {
        using var factory = new IntegrationTestFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var declared = db.Database.GetMigrations().ToList();

        applied.Should().NotBeEmpty();
        applied.Should().BeEquivalentTo(declared);
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Database_OnStartup_SeedsDefaultAdminUser()
    {
        using var factory = new IntegrationTestFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        var admin = await db.Users.SingleOrDefaultAsync(u => u.Email == "admin@quotes.local");

        admin.Should().NotBeNull();
        admin!.PasswordHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateQuote_ViaApi_PersistsThroughRealEfCoreRoundTrip()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();
        var token = TestAuth.CreateInternalToken(1, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Aristotle", "Knowing yourself is the beginning of all wisdom."));
        response.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        var storedQuotes = await db.Quotes.AsNoTracking().ToListAsync();

        storedQuotes.Should().ContainSingle();
        storedQuotes[0].Author.Should().Be("Aristotle");
        storedQuotes[0].OwnerId.Should().Be(1);
    }

    [Fact]
    public async Task Database_IsIsolatedAcrossFactoryInstances()
    {
        using var firstFactory = new IntegrationTestFactory();
        using var firstClient = firstFactory.CreateClient();
        var token = TestAuth.CreateInternalToken(1, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        firstClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResponse = await firstClient.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Only In Factory One", "This must not leak."));
        createResponse.EnsureSuccessStatusCode();

        using var secondFactory = new IntegrationTestFactory();
        using var secondScope = secondFactory.Services.CreateScope();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        (await secondDb.Quotes.AnyAsync()).Should().BeFalse();

        using var firstScope = firstFactory.Services.CreateScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        (await firstDb.Quotes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Database_UsesFakeClockRatherThanSystemTime()
    {
        using var factory = new IntegrationTestFactory();
        factory.Clock.UtcNow = new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero);
        using var client = factory.CreateClient();
        var token = TestAuth.CreateInternalToken(1, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Time Traveler", "Set by the fake clock."));

        response.EnsureSuccessStatusCode();
        var quote = await response.Content.ReadFromJsonAsync<QuotesApi.Models.Quote>();
        quote!.CreatedAtUtc.Should().Be(new DateTime(2030, 6, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
