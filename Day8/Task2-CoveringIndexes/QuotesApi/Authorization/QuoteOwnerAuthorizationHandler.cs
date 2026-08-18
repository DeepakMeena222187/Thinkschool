using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.Models;

namespace QuotesApi.Authorization;

public sealed class QuoteOwnerAuthorizationHandler(ILogger<QuoteOwnerAuthorizationHandler> logger)
    : AuthorizationHandler<QuoteOwnerRequirement, Quote>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        QuoteOwnerRequirement requirement,
        Quote resource)
    {
        var userId = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(userId, out var currentUserId) && currentUserId == resource.OwnerId)
        {
            context.Succeed(requirement);
        }
        else
        {
            logger.LogWarning(
                "Authorization denied for QuoteId={QuoteId} UserId={UserId}",
                resource.Id, userId);
        }

        return Task.CompletedTask;
    }
}
