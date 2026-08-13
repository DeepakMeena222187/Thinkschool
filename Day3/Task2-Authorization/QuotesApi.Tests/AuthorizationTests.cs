using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Authorization;
using QuotesApi.Contracts;
using QuotesApi.Models;
using Xunit;

namespace QuotesApi.Tests;

public sealed class AuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string InternalIssuer = "https://localhost";
    private const string InternalAudience = "quotes-api";

    private readonly CustomWebApplicationFactory _factory;

    public AuthorizationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string CreateInternalToken(int userId, params Claim[] extraClaims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomWebApplicationFactory.TestJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString())
        };
        claims.AddRange(extraClaims);

        var token = new JwtSecurityToken(
            issuer: InternalIssuer,
            audience: InternalAudience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Requirement: claim-based "can-edit-quotes" policy (scope = quotes.write) succeeds when the claim is present.
    [Fact]
    public async Task PostQuote_WithWriteScopeClaim_Succeeds()
    {
        using var client = _factory.CreateClient();
        var token = CreateInternalToken(1, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Test Author", "Test quote"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // Requirement: missing claim results in HTTP 403 (authenticated, but not authorized).
    [Fact]
    public async Task PostQuote_WithoutWriteScopeClaim_Returns403()
    {
        using var client = _factory.CreateClient();
        var token = CreateInternalToken(1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Test Author", "Test quote"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Requirement: incorrect claim value results in HTTP 403.
    [Fact]
    public async Task PostQuote_WithWrongScopeClaim_Returns403()
    {
        using var client = _factory.CreateClient();
        var token = CreateInternalToken(1, new Claim(QuotePolicies.ScopeClaimType, "quotes.read"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Test Author", "Test quote"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Requirement: custom ownership requirement succeeds when the caller owns the quote.
    [Fact]
    public async Task DeleteQuote_AsOwner_Succeeds()
    {
        using var client = _factory.CreateClient();
        var ownerToken = CreateInternalToken(101, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createResponse = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Owner", "Owner's quote"));
        createResponse.EnsureSuccessStatusCode();
        var quote = await createResponse.Content.ReadFromJsonAsync<Quote>();
        Assert.NotNull(quote);

        var deleteResponse = await client.DeleteAsync($"/api/quotes/{quote.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    // Requirement: custom ownership requirement returns HTTP 403 when a different user attempts the delete.
    [Fact]
    public async Task DeleteQuote_AsNonOwner_Returns403()
    {
        using var client = _factory.CreateClient();
        var ownerToken = CreateInternalToken(201, new Claim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createResponse = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Owner", "Owner's quote"));
        createResponse.EnsureSuccessStatusCode();
        var quote = await createResponse.Content.ReadFromJsonAsync<Quote>();
        Assert.NotNull(quote);

        var otherUserToken = CreateInternalToken(202);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherUserToken);

        var deleteResponse = await client.DeleteAsync($"/api/quotes/{quote.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }
}
