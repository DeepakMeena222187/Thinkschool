using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using QuotesApi.Contracts;
using Xunit;

namespace Quotes.Tests.Integration;

public sealed class AuthEndpointTests
{
    private const string SeededAdminEmail = "admin@quotes.local";
    private const string SeededAdminPassword = "meena@123";

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenPayload()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = SeededAdminEmail,
            Password = SeededAdminPassword
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();
        body.ExpiresIn.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = SeededAdminEmail,
            Password = "wrong-password"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsUnauthorized()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "nobody@quotes.local",
            Password = "irrelevant"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithValidRefreshToken_ReturnsRotatedTokenPair()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = SeededAdminEmail,
            Password = SeededAdminPassword
        });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = loginBody!.RefreshToken
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = await response.Content.ReadFromJsonAsync<LoginResponse>();
        refreshed.Should().NotBeNull();
        refreshed!.RefreshToken.Should().NotBe(loginBody.RefreshToken);
    }

    [Fact]
    public async Task Refresh_WithReusedRefreshToken_ReturnsUnauthorized()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = SeededAdminEmail,
            Password = SeededAdminPassword
        });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var firstRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = loginBody!.RefreshToken
        });
        firstRefresh.EnsureSuccessStatusCode();

        var replay = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = loginBody.RefreshToken
        });

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithValidRefreshToken_RevokesTokenSoFurtherRefreshFails()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = SeededAdminEmail,
            Password = SeededAdminPassword
        });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        var logoutResponse = await client.PostAsJsonAsync("/api/auth/logout", new LogoutRequest
        {
            RefreshToken = loginBody!.RefreshToken
        });

        logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshAfterLogout = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = loginBody.RefreshToken
        });
        refreshAfterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithExpiredToken_ReturnsUnauthorizedChallenge()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();
        var expiredToken = TestAuth.CreateExpiredInternalToken(1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Late", "Too late."));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("Bearer");
    }

    [Fact]
    public async Task ProtectedEndpoint_WithMalformedToken_ReturnsUnauthorizedChallenge()
    {
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-jwt");

        var response = await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest("Bad Token", "Should fail."));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("Bearer");
    }
}
