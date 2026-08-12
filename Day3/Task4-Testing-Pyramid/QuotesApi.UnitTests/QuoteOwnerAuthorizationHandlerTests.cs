using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.Authorization;
using QuotesApi.Models;
using Xunit;

namespace QuotesApi.UnitTests;

// AuthorizationTests (QuotesApi.Tests) keeps one HTTP round trip per outcome to
// prove the endpoint really calls into this handler. The edge cases below --
// which claim wins, non-numeric ids, anonymous users -- don't need a running
// app or HTTP pipeline to verify: they're a direct call into the handler's
// HandleAsync, which is exactly what the ASP.NET Core authorization
// middleware calls at runtime.
public sealed class QuoteOwnerAuthorizationHandlerTests
{
    private static readonly QuoteOwnerAuthorizationHandler Handler = new();

    private static async Task<AuthorizationHandlerContext> AuthorizeAsync(ClaimsPrincipal user, Quote resource)
    {
        var context = new AuthorizationHandlerContext(new[] { new QuoteOwnerRequirement() }, user, resource);
        await Handler.HandleAsync(context);
        return context;
    }

    private static ClaimsPrincipal UserWithSubClaim(string userId) =>
        new(new ClaimsIdentity(new[] { new Claim(JwtRegisteredClaimNames.Sub, userId) }, "TestAuth"));

    private static ClaimsPrincipal UserWithNameIdentifierClaim(string userId) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth"));

    [Fact]
    public async Task Owner_WithMatchingSubClaim_Succeeds()
    {
        var context = await AuthorizeAsync(UserWithSubClaim("101"), new Quote { Id = 1, OwnerId = 101 });

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task NonOwner_WithMismatchedSubClaim_DoesNotSucceed()
    {
        var context = await AuthorizeAsync(UserWithSubClaim("202"), new Quote { Id = 1, OwnerId = 101 });

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Owner_FallsBackToNameIdentifierClaimWhenSubIsAbsent()
    {
        var context = await AuthorizeAsync(UserWithNameIdentifierClaim("101"), new Quote { Id = 1, OwnerId = 101 });

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task NonNumericUserId_DoesNotSucceed()
    {
        var context = await AuthorizeAsync(UserWithSubClaim("not-a-number"), new Quote { Id = 1, OwnerId = 101 });

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task AnonymousUser_DoesNotSucceed()
    {
        var context = await AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), new Quote { Id = 1, OwnerId = 101 });

        Assert.False(context.HasSucceeded);
    }
}
