using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Authentication;
using QuotesApi.Contracts;
using Xunit;

namespace QuotesApi.Tests;

public sealed class AuthTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenPayload()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "admin@quotes.local",
            Password = "meena@123"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(body.RefreshToken));
        Assert.True(body.ExpiresIn > 0);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "admin@quotes.local",
            Password = "wrong"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithValidToken_RotatesRefreshToken()
    {
        using var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "admin@quotes.local",
            Password = "meena@123"
        });

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginBody);

        var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = loginBody.RefreshToken
        });

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshedBody = await refreshResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(refreshedBody);
        Assert.False(string.IsNullOrWhiteSpace(refreshedBody.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshedBody.RefreshToken));
        Assert.NotEqual(loginBody.RefreshToken, refreshedBody.RefreshToken);
    }

    [Fact]
    public async Task Refresh_WithReusedToken_Returns401AndRevokesFamily()
    {
        using var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "admin@quotes.local",
            Password = "meena@123"
        });

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginBody);

        var firstRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = loginBody.RefreshToken
        });
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);

        var replayResponse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = loginBody.RefreshToken
        });

        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        using var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "admin@quotes.local",
            Password = "meena@123"
        });

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginBody);

        var logoutResponse = await client.PostAsJsonAsync("/api/auth/logout", new LogoutRequest
        {
            RefreshToken = loginBody.RefreshToken
        });

        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        var replayResponse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = loginBody.RefreshToken
        });

        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
    }

    [Fact]
    public async Task Authentication_RegistersPolicyInternalAndEntraSchemes()
    {
        using var scope = _factory.Services.CreateScope();
        var schemeProvider = scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>();
        var schemes = await schemeProvider.GetAllSchemesAsync();

        Assert.Contains(schemes, scheme => scheme.Name == JwtSchemes.Policy);
        Assert.Contains(schemes, scheme => scheme.Name == JwtSchemes.Internal);
        Assert.Contains(schemes, scheme => scheme.Name == JwtSchemes.Entra);

        var authOptions = scope.ServiceProvider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        Assert.Equal(JwtSchemes.Policy, authOptions.DefaultScheme);
        Assert.Equal(JwtSchemes.Policy, authOptions.DefaultChallengeScheme);
    }

    [Fact]
    public async Task PostQuote_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Test Author", "Test quote"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostQuote_WithToken_Succeeds()
    {
        using var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "admin@quotes.local",
            Password = "meena@123"
        });

        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Test Author", "Test quote"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync("/api/quotes/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidJwt_Returns401AndChallenge()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Test Author", "Test quote"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task InvalidEntraJwt_Returns401AndChallenge()
    {
        using var client = _factory.CreateClient();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomWebApplicationFactory.TestJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: CustomWebApplicationFactory.TestEntraAuthority,
            audience: CustomWebApplicationFactory.TestEntraClientId,
            claims: new[] { new Claim(ClaimTypes.NameIdentifier, "entra-user") },
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));

        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Test Author", "Test quote"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task ExpiredJwt_Returns401AndChallenge()
    {
        using var client = _factory.CreateClient();

        var secret = CustomWebApplicationFactory.TestJwtSecret;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "https://localhost",
            audience: "quotes-api",
            claims: new[] { new Claim(ClaimTypes.NameIdentifier, "1") },
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddMinutes(-1),
            signingCredentials: credentials);

        var expiredToken = new JwtSecurityTokenHandler().WriteToken(token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Test Author", "Test quote"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("Bearer", challenge);
        Assert.Contains("invalid", challenge, StringComparison.OrdinalIgnoreCase);
    }
}
