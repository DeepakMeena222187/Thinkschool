using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QuotesApi.Authorization;
using QuotesApi.Contracts;
using QuotesApi.Models;
using Xunit;

namespace Quotes.Tests.Integration;

public sealed class QuotesEndpointTests
{
    [Fact]
    public async Task GetQuotes_WhenDatabaseEmpty_ReturnsEmptyPage()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quotes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("total").GetInt32().Should().Be(0);
        body.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetQuotes_AfterQuoteCreated_ReturnsQuoteInPagedResult()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();
        var token = TestAuth.CreateInternalToken(1, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Marcus Aurelius", "You have power over your mind."));
        createResponse.EnsureSuccessStatusCode();

        var listResponse = await client.GetAsync("/api/quotes");
        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("total").GetInt32().Should().Be(1);
        body.GetProperty("items")[0].GetProperty("author").GetString().Should().Be("Marcus Aurelius");
    }

    [Fact]
    public async Task GetQuoteById_WhenQuoteDoesNotExist_ReturnsNotFoundProblemDetails()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quotes/999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Quote not found");
    }

    [Fact]
    public async Task GetQuoteById_WhenQuoteExists_ReturnsQuote()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();
        var token = TestAuth.CreateInternalToken(1, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResponse = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Seneca", "Luck is what happens when preparation meets opportunity."));
        var created = await createResponse.Content.ReadFromJsonAsync<Quote>();

        var response = await client.GetAsync($"/api/quotes/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = await response.Content.ReadFromJsonAsync<Quote>();
        quote!.Author.Should().Be("Seneca");
    }

    [Fact]
    public async Task PostQuote_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Anonymous", "Should not be created."));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostQuote_WithValidTokenAndScope_ReturnsCreatedWithFakeClockTimestamp()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();
        var token = TestAuth.CreateInternalToken(1, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Epictetus", "It's not what happens to you, but how you react."));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var quote = await response.Content.ReadFromJsonAsync<Quote>();
        quote!.CreatedAtUtc.Should().Be(factory.Clock.UtcNow.UtcDateTime);
    }

    [Fact]
    public async Task PostQuote_WithoutWriteScope_ReturnsForbidden()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();
        var token = TestAuth.CreateInternalToken(1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Missing Scope", "Should be forbidden."));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostQuote_WithInvalidPayload_ReturnsValidationProblemDetails()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();
        var token = TestAuth.CreateInternalToken(1, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("", ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Errors.Should().ContainKey("Author");
        problem.Errors.Should().ContainKey("Text");
    }

    [Fact]
    public async Task DeleteQuote_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/quotes/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteQuote_AsOwner_ReturnsNoContent()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();
        var ownerToken = TestAuth.CreateInternalToken(101, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var createResponse = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Owner", "Owner's quote."));
        var quote = await createResponse.Content.ReadFromJsonAsync<Quote>();

        var response = await client.DeleteAsync($"/api/quotes/{quote!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var getAfterDelete = await client.GetAsync($"/api/quotes/{quote.Id}");
        getAfterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteQuote_AsNonOwner_ReturnsForbidden()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();
        var ownerToken = TestAuth.CreateInternalToken(201, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var createResponse = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Owner", "Owner's quote."));
        var quote = await createResponse.Content.ReadFromJsonAsync<Quote>();

        var otherUserToken = TestAuth.CreateInternalToken(202, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherUserToken);

        var response = await client.DeleteAsync($"/api/quotes/{quote!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteQuote_WhenQuoteDoesNotExist_ReturnsNotFound()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();
        var token = TestAuth.CreateInternalToken(1, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.DeleteAsync("/api/quotes/999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
