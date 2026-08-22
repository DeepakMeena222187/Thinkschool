using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using QuotesApi.Features.Quotes;

namespace QuotesApi.Extensions;

public static class CqrsQuoteEndpointExtensions
{
    private static int? TryGetUserId(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var idClaim = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(idClaim, out var userId) ? userId : null;
    }

    public static void MapCqrsQuoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/cqrs/quotes");

        group.MapPost("", async (
            CreateQuoteCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var id = await mediator.Send(command, ct);

            return Results.Created($"/api/cqrs/quotes/{id}", new { id });
        });

        group.MapGet("", async (
            ClaimsPrincipal user,
            int? currentUserId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var resolvedUserId = TryGetUserId(user) ?? currentUserId;

            if (resolvedUserId is null)
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Status = 400,
                    Title = "Missing current user",
                    Detail = "Provide a bearer token, or a currentUserId query parameter for this exercise."
                });
            }

            var quotes = await mediator.Send(new GetQuoteListQuery(resolvedUserId.Value), ct);

            return Results.Ok(quotes);
        });
    }
}
