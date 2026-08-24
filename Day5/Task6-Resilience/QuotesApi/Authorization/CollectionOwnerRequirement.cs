using Microsoft.AspNetCore.Authorization;

namespace QuotesApi.Authorization;

public sealed class CollectionOwnerRequirement : IAuthorizationRequirement
{
}
