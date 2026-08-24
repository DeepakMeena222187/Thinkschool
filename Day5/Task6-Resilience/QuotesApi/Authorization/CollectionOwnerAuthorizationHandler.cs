using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.Models;

namespace QuotesApi.Authorization;

public sealed class CollectionOwnerAuthorizationHandler(ILogger<CollectionOwnerAuthorizationHandler> logger)
    : AuthorizationHandler<CollectionOwnerRequirement, Collection>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CollectionOwnerRequirement requirement,
        Collection resource)
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
                "Authorization denied for CollectionId={CollectionId} UserId={UserId}",
                resource.Id, userId);
        }

        return Task.CompletedTask;
    }
}
