using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using QuotesApi.Models;
using QuotesApi.Services;

namespace Quotes.Tests.Unit;

public sealed class AuthServiceTests
{
    private static IConfiguration BuildConfiguration(string? secret) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(secret is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["Jwt:Secret"] = secret })
            .Build();

    // EvaluateRefreshTokenStatus only reads its RefreshToken argument and the
    // injected IClock, never the QuotesDbContext, so a null context is safe here
    // and keeps this a true unit test with no real database.
    private static AuthService CreateSut(IClock clock) =>
        new(db: null!, configuration: BuildConfiguration(null), clock);

    private static RefreshToken ValidToken(DateTime expiresAtUtc) => new()
    {
        Id = 1,
        UserId = 1,
        TokenHash = "hash",
        FamilyId = "family-1",
        CreatedAtUtc = expiresAtUtc.AddDays(-7),
        ExpiresAtUtc = expiresAtUtc,
        RevokedAtUtc = null,
        ReplacedByTokenHash = null
    };

    [Fact]
    public void GetJwtSecret_WithConfiguredSecret_ReturnsIt()
    {
        // Arrange
        var configuration = BuildConfiguration("this-is-a-test-secret-long-enough-for-hs256-1234");

        // Act
        var secret = AuthService.GetJwtSecret(configuration);

        // Assert
        secret.Should().Be("this-is-a-test-secret-long-enough-for-hs256-1234");
    }

    [Fact]
    public void GetJwtSecret_WithoutSecretConfiguredAnywhere_ThrowsInvalidOperationException()
    {
        // Arrange
        Environment.SetEnvironmentVariable("Jwt__Secret", null);
        var configuration = BuildConfiguration(null);

        // Act
        var action = () => AuthService.GetJwtSecret(configuration);

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*JWT secret not configured*");
    }

    [Fact]
    public void GetJwtSecret_ShorterThan32Bytes_ThrowsInvalidOperationException()
    {
        // Arrange
        var configuration = BuildConfiguration("too-short");

        // Act
        var action = () => AuthService.GetJwtSecret(configuration);

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least 32 bytes*");
    }

    [Fact]
    public void HashToken_WithSameInputTwice_IsDeterministic()
    {
        // Arrange
        var first = AuthService.HashToken("sample-refresh-token");

        // Act
        var second = AuthService.HashToken("sample-refresh-token");

        // Assert
        second.Should().Be(first);
    }

    [Fact]
    public void HashToken_WithDifferentInputs_ProducesDifferentHashes()
    {
        // Arrange
        var first = AuthService.HashToken("token-a");

        // Act
        var second = AuthService.HashToken("token-b");

        // Assert
        second.Should().NotBe(first);
    }

    [Fact]
    public void CreateRefreshToken_CalledTwice_Produces64ByteUniqueValues()
    {
        // Arrange & Act
        var first = AuthService.CreateRefreshToken();
        var second = AuthService.CreateRefreshToken();

        // Assert
        Convert.FromBase64String(first).Should().HaveCount(64);
        second.Should().NotBe(first);
    }

    [Fact]
    public void CreateAccessToken_WithConfiguredIssuerAndAudience_EmbedsUserIdentityClaims()
    {
        // Arrange: IConfiguration is substituted so this test isolates AuthService
        // from the real configuration system (files, env vars) entirely.
        var configuration = Substitute.For<IConfiguration>();
        configuration["Jwt:Secret"].Returns("this-is-a-test-secret-long-enough-for-hs256-1234");
        configuration["Jwt:Issuer"].Returns("https://issuer.test");
        configuration["Jwt:Audience"].Returns("quotes-api-test");
        var sut = new AuthService(db: null!, configuration, clock: new FakeClock());
        var user = new User { Id = 42, Email = "reader@example.test" };

        // Act
        var accessToken = sut.CreateAccessToken(user);

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        jwt.Issuer.Should().Be("https://issuer.test");
        jwt.Audiences.Should().Contain("quotes-api-test");
        jwt.Subject.Should().Be("42");
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "reader@example.test");
    }

    [Fact]
    public void EvaluateRefreshTokenStatus_WithUnexpiredUnrevokedUnreplacedToken_ReturnsValid()
    {
        // Arrange
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero) };
        var sut = CreateSut(clock);
        var token = ValidToken(expiresAtUtc: new DateTime(2026, 1, 22, 10, 0, 0, DateTimeKind.Utc));

        // Act
        var status = sut.EvaluateRefreshTokenStatus(token);

        // Assert
        status.Should().Be(RefreshTokenStatus.Valid);
    }

    [Fact]
    public void EvaluateRefreshTokenStatus_WhenClockIsPastExpiry_ReturnsInvalid()
    {
        // Arrange
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 1, 23, 0, 0, 0, TimeSpan.Zero) };
        var sut = CreateSut(clock);
        var token = ValidToken(expiresAtUtc: new DateTime(2026, 1, 22, 10, 0, 0, DateTimeKind.Utc));

        // Act
        var status = sut.EvaluateRefreshTokenStatus(token);

        // Assert
        status.Should().Be(RefreshTokenStatus.Invalid);
    }

    [Fact]
    public void EvaluateRefreshTokenStatus_WhenClockExactlyAtExpiry_ReturnsInvalid()
    {
        // Arrange
        var expiresAtUtc = new DateTime(2026, 1, 22, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock { UtcNow = new DateTimeOffset(expiresAtUtc, TimeSpan.Zero) };
        var sut = CreateSut(clock);
        var token = ValidToken(expiresAtUtc);

        // Act
        var status = sut.EvaluateRefreshTokenStatus(token);

        // Assert
        status.Should().Be(RefreshTokenStatus.Invalid);
    }

    [Fact]
    public void EvaluateRefreshTokenStatus_WhenTokenIsRevoked_ReturnsInvalid()
    {
        // Arrange
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero) };
        var sut = CreateSut(clock);
        var token = ValidToken(expiresAtUtc: new DateTime(2026, 1, 22, 10, 0, 0, DateTimeKind.Utc));
        token.RevokedAtUtc = new DateTime(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var status = sut.EvaluateRefreshTokenStatus(token);

        // Assert
        status.Should().Be(RefreshTokenStatus.Invalid);
    }

    [Fact]
    public void EvaluateRefreshTokenStatus_WhenTokenHasAlreadyBeenReplaced_ReturnsReused()
    {
        // Arrange
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero) };
        var sut = CreateSut(clock);
        var token = ValidToken(expiresAtUtc: new DateTime(2026, 1, 22, 10, 0, 0, DateTimeKind.Utc));
        token.ReplacedByTokenHash = "some-newer-hash";

        // Act
        var status = sut.EvaluateRefreshTokenStatus(token);

        // Assert
        status.Should().Be(RefreshTokenStatus.Reused);
    }

    [Fact]
    public void EvaluateRefreshTokenStatus_WhenRevokedAndReplaced_ReturnsInvalidBeforeReused()
    {
        // Arrange: revocation takes priority over reuse so a revoked family stays dead.
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero) };
        var sut = CreateSut(clock);
        var token = ValidToken(expiresAtUtc: new DateTime(2026, 1, 22, 10, 0, 0, DateTimeKind.Utc));
        token.RevokedAtUtc = new DateTime(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);
        token.ReplacedByTokenHash = "some-newer-hash";

        // Act
        var status = sut.EvaluateRefreshTokenStatus(token);

        // Assert
        status.Should().Be(RefreshTokenStatus.Invalid);
    }
}
