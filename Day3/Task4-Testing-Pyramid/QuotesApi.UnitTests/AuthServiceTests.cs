using Microsoft.Extensions.Configuration;
using QuotesApi.Services;
using Xunit;

namespace QuotesApi.UnitTests;

// AuthService's crypto/token helpers are pure functions (no DbContext, no HTTP).
// The full login/refresh/logout flows stay covered by QuotesApi.Tests, but these
// building blocks previously had no direct coverage of their own -- only an
// indirect signal through the HTTP round trips.
public sealed class AuthServiceTests
{
    private static IConfiguration BuildConfiguration(string? secret) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(secret is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["Jwt:Secret"] = secret })
            .Build();

    [Fact]
    public void GetJwtSecret_WithConfiguredSecret_ReturnsIt()
    {
        var configuration = BuildConfiguration("this-is-a-test-secret-long-enough-for-hs256-1234");

        var secret = AuthService.GetJwtSecret(configuration);

        Assert.Equal("this-is-a-test-secret-long-enough-for-hs256-1234", secret);
    }

    [Fact]
    public void GetJwtSecret_WithoutSecretConfiguredAnywhere_Throws()
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", null);
        var configuration = BuildConfiguration(null);

        var ex = Assert.Throws<InvalidOperationException>(() => AuthService.GetJwtSecret(configuration));
        Assert.Contains("JWT secret not configured", ex.Message);
    }

    [Fact]
    public void GetJwtSecret_ShorterThan32Bytes_Throws()
    {
        var configuration = BuildConfiguration("too-short");

        var ex = Assert.Throws<InvalidOperationException>(() => AuthService.GetJwtSecret(configuration));
        Assert.Contains("at least 32 bytes", ex.Message);
    }

    [Fact]
    public void HashToken_IsDeterministicForTheSameInput()
    {
        var first = AuthService.HashToken("sample-refresh-token");
        var second = AuthService.HashToken("sample-refresh-token");

        Assert.Equal(first, second);
    }

    [Fact]
    public void HashToken_DiffersForDifferentInput()
    {
        var first = AuthService.HashToken("token-a");
        var second = AuthService.HashToken("token-b");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CreateRefreshToken_ProducesA64ByteRandomValueEachTime()
    {
        var first = AuthService.CreateRefreshToken();
        var second = AuthService.CreateRefreshToken();

        Assert.Equal(64, Convert.FromBase64String(first).Length);
        Assert.NotEqual(first, second);
    }
}
