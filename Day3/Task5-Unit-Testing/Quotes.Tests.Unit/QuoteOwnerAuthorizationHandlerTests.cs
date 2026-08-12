using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.Authorization;
using QuotesApi.Models;

namespace Quotes.Tests.Unit;

public sealed class QuoteOwnerAuthorizationHandlerTests
{
    private static async Task<AuthorizationHandlerContext> HandleAsync(ClaimsPrincipal user, Quote resource)
    {
        var handler = new QuoteOwnerAuthorizationHandler();
        var context = new AuthorizationHandlerContext(new[] { new QuoteOwnerRequirement() }, user, resource);

        await handler.HandleAsync(context);

        return context;
    }

    private static ClaimsPrincipal UserWithSubClaim(string userId) =>
        new(new ClaimsIdentity(new[] { new Claim(JwtRegisteredClaimNames.Sub, userId) }, "TestAuth"));

    private static ClaimsPrincipal UserWithNameIdentifierClaim(string userId) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth"));

    [Fact]
    public async Task HandleRequirementAsync_UserIdMatchesQuoteOwnerViaSubClaim_Succeeds()
    {
        // Arrange
        var user = UserWithSubClaim("101");
        var quote = new Quote { Id = 1, OwnerId = 101 };

        // Act
        var context = await HandleAsync(user, quote);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_UserIdDoesNotMatchQuoteOwner_DoesNotSucceed()
    {
        // Arrange
        var user = UserWithSubClaim("202");
        var quote = new Quote { Id = 1, OwnerId = 101 };

        // Act
        var context = await HandleAsync(user, quote);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_SubClaimAbsentButNameIdentifierMatches_Succeeds()
    {
        // Arrange
        var user = UserWithNameIdentifierClaim("101");
        var quote = new Quote { Id = 1, OwnerId = 101 };

        // Act
        var context = await HandleAsync(user, quote);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_UserIdClaimIsNonNumeric_DoesNotSucceed()
    {
        // Arrange
        var user = UserWithSubClaim("not-a-number");
        var quote = new Quote { Id = 1, OwnerId = 101 };

        // Act
        var context = await HandleAsync(user, quote);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_AnonymousUser_DoesNotSucceed()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity());
        var quote = new Quote { Id = 1, OwnerId = 101 };

        // Act
        var context = await HandleAsync(user, quote);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }
}
