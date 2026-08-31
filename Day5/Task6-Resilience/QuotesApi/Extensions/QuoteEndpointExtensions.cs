using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuotesApi.Authorization;
using QuotesApi.Contracts;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace QuotesApi.Extensions;

public static class QuoteEndpointExtensions
{
    private static readonly ActivitySource ActivitySource = new("QuotesApi");

    private static int GetUserId(ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static void MapQuoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/quotes");

        group.MapGet("", async (
            int? page,
            int? size,
            IQuoteRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Quotes");
            var currentPage = Math.Max(page ?? 1, 1);
            var pageSize = Math.Clamp(size ?? 10, 1, 100);

            logger.LogInformation(
                "Getting quotes Page={Page} Size={Size}",
                currentPage, pageSize);

            var result = await repository.GetPageAsync(currentPage, pageSize, ct);

            return Results.Ok(new
            {
                page = currentPage,
                size = pageSize,
                total = result.Total,
                items = result.Items
            });
        });

        group.MapPost("", async (
    CreateQuoteRequest request,
    IQuoteRepository repository,
    IEventQueue eventQueue,
    ILoggerFactory loggerFactory,
    IClock clock,
    ClaimsPrincipal user,
    CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Quotes");
            var requestingUserId = GetUserId(user);

            logger.LogInformation("Quote creation requested by UserId={UserId}", requestingUserId);

            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(request);

            Validator.TryValidateObject(
                request, validationContext, validationResults, true);

            if (validationResults.Count > 0)
            {
                var errors = validationResults
                    .SelectMany(x => x.MemberNames.DefaultIfEmpty("request")
                        .Select(name => new
                        {
                            name,
                            message = x.ErrorMessage ?? "Invalid value."
                        }))
                    .GroupBy(x => x.name)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(x => x.message).ToArray());

                return Results.ValidationProblem(errors);
            }

            using var activity = ActivitySource.StartActivity("Quote.Create");
            activity?.SetTag("user.id", requestingUserId);

            var quote = Quote.Create(request.Author, request.Text, requestingUserId, clock.UtcNow.UtcDateTime);

            await repository.AddAsync(quote, ct);

            activity?.SetTag("quote.id", quote.Id);

            logger.LogInformation(
                "Quote created QuoteId={QuoteId} UserId={UserId}",
                quote.Id, quote.OwnerId);

            // Auditing must never affect the response that's already been
            // decided above - enqueue is fire-and-forget from the request's
            // point of view, so any failure here is only ever logged.
            try
            {
                var enqueued = eventQueue.TryEnqueue(new EventLogItem(
                    "QuoteCreated",
                    requestingUserId,
                    JsonSerializer.Serialize(new { quoteId = quote.Id }),
                    clock.UtcNow.UtcDateTime));

                if (!enqueued)
                {
                    logger.LogWarning("Failed to enqueue QuoteCreated event for QuoteId={QuoteId}", quote.Id);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error enqueueing QuoteCreated event for QuoteId={QuoteId}", quote.Id);
            }

            return Results.Created($"/api/quotes/{quote.Id}", quote);
        }).RequireAuthorization(QuotePolicies.CanEditQuotes);

        group.MapGet("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Quotes");
            var quote = await repository.GetByIdAsync(id, ct);

            if (quote is null)
            {
                logger.LogWarning("Quote not found Id={QuoteId}", id);

                return Results.NotFound(new ProblemDetails
                {
                    Status = 404,
                    Title = "Quote not found",
                    Detail = $"No quote exists with id {id}."
                });
            }

            return Results.Ok(quote);
        });

        group.MapDelete("/{id:int}", async (
            int id,
            IQuoteRepository repository,
            IAuthorizationService authorizationService,
            ClaimsPrincipal user,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Quotes");
            var quote = await repository.GetByIdAsync(id, ct);

            if (quote is null)
            {
                logger.LogWarning(
                    "Quote not found for deletion Id={QuoteId}", id);

                return Results.NotFound(new ProblemDetails
                {
                    Status = 404,
                    Title = "Quote not found",
                    Detail = $"No quote exists with id {id}."
                });
            }

            var authorizationResult = await authorizationService.AuthorizeAsync(user, quote, new QuoteOwnerRequirement());
            if (!authorizationResult.Succeeded)
            {
                logger.LogWarning(
                    "User {UserId} is not authorized to delete quote Id={QuoteId}", GetUserId(user), id);

                return Results.Forbid();
            }

            await repository.DeleteAsync(id, ct);

            logger.LogInformation("Deleted quote Id={QuoteId}", id);

            return Results.NoContent();
        }).RequireAuthorization();

        app.MapPost("/api/auth/login", async (
            LoginRequest request,
            AuthService authService,
            CancellationToken ct) =>
        {
            var (success, _, accessToken, refreshToken, expiresIn) = await authService.LoginAsync(request.Email, request.Password, ct);

            if (!success)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new LoginResponse
            {
                AccessToken = accessToken!,
                RefreshToken = refreshToken!,
                ExpiresIn = expiresIn
            });
        });

        app.MapPost("/api/auth/register", async (
            RegisterRequest request,
            AuthService authService,
            CancellationToken ct) =>
        {
            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(request);

            Validator.TryValidateObject(
                request, validationContext, validationResults, true);

            if (validationResults.Count > 0)
            {
                var errors = validationResults
                    .SelectMany(x => x.MemberNames.DefaultIfEmpty("request")
                        .Select(name => new
                        {
                            name,
                            message = x.ErrorMessage ?? "Invalid value."
                        }))
                    .GroupBy(x => x.name)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(x => x.message).ToArray());

                return Results.ValidationProblem(errors);
            }

            var (success, error, accessToken, refreshToken, expiresIn) = await authService.RegisterAsync(request.Email, request.Password, ct);

            if (!success)
            {
                return Results.Conflict(new ProblemDetails
                {
                    Status = 409,
                    Title = "Registration failed",
                    Detail = error
                });
            }

            return Results.Created("/api/auth/login", new LoginResponse
            {
                AccessToken = accessToken!,
                RefreshToken = refreshToken!,
                ExpiresIn = expiresIn
            });
        });

        app.MapPost("/api/auth/refresh", async (
            RefreshTokenRequest request,
            AuthService authService,
            CancellationToken ct) =>
        {
            var (success, _, accessToken, refreshToken, expiresIn) = await authService.RefreshAsync(request.RefreshToken, ct);

            if (!success)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new LoginResponse
            {
                AccessToken = accessToken!,
                RefreshToken = refreshToken!,
                ExpiresIn = expiresIn
            });
        });

        app.MapPost("/api/auth/logout", async (
            LogoutRequest request,
            AuthService authService,
            CancellationToken ct) =>
        {
            var loggedOut = await authService.LogoutAsync(request.RefreshToken, ct);

            return loggedOut ? Results.Ok() : Results.Unauthorized();
        });

        app.MapGet("/api/collections", async (
            ICollectionRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Collections");

            // Collections and their items load in one round trip via Include
            // (see EfCollectionRepository.GetAllAsync) instead of the earlier
            // per-collection N+1.
            var collections = await repository.GetAllAsync(ct);

            var response = collections.Select(collection => new
            {
                id = collection.Id,
                name = collection.Name,
                ownerId = collection.OwnerId,
                items = collection.Items.Select(i => new { quoteId = i.QuoteId, addedAt = i.AddedAt })
            });

            logger.LogInformation("Listed {Count} collections", collections.Count);

            return Results.Ok(response);
        });

        app.MapPost("/api/collections", async (
            CreateCollectionRequest request,
            ICollectionRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Collections");

            try
            {
                var collection = new Collection(request.Name, request.OwnerId);
                await repository.AddAsync(collection, ct);

                logger.LogInformation("Created collection Id={CollectionId}", collection.Id);

                return Results.Created($"/api/collections/{collection.Id}", collection);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Status = 400,
                    Title = "Invalid collection",
                    Detail = ex.Message
                });
            }
        }).RequireAuthorization(QuotePolicies.CanEditQuotes);

        app.MapPost("/api/collections/{id:int}/items", async (
            int id,
            AddCollectionItemRequest request,
            ICollectionRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Collections");
            var collection = await repository.GetByIdAsync(id, ct);

            if (collection is null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Status = 404,
                    Title = "Collection not found",
                    Detail = $"No collection exists with id {id}."
                });
            }

            try
            {
                collection.AddItem(request.QuoteId);
                await repository.UpdateAsync(collection, ct);

                logger.LogInformation("Added quote {QuoteId} to collection {CollectionId}", request.QuoteId, id);

                return Results.Ok(collection);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Status = 400,
                    Title = "Invalid collection operation",
                    Detail = ex.Message
                });
            }
        }).RequireAuthorization(QuotePolicies.CanEditQuotes);

        app.MapDelete("/api/collections/{id:int}/items/{quoteId:int}", async (
            int id,
            int quoteId,
            ICollectionRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Collections");
            var collection = await repository.GetByIdAsync(id, ct);

            if (collection is null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Status = 404,
                    Title = "Collection not found",
                    Detail = $"No collection exists with id {id}."
                });
            }

            try
            {
                collection.RemoveItem(quoteId);
                await repository.UpdateAsync(collection, ct);

                logger.LogInformation("Removed quote {QuoteId} from collection {CollectionId}", quoteId, id);

                return Results.Ok(collection);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Status = 400,
                    Title = "Invalid collection operation",
                    Detail = ex.Message
                });
            }
        }).RequireAuthorization(QuotePolicies.CanEditQuotes);

        app.MapDelete("/api/collections/{id:int}", async (
            int id,
            ICollectionRepository repository,
            IAuthorizationService authorizationService,
            ClaimsPrincipal user,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Collections");
            var collection = await repository.GetByIdAsync(id, ct);

            if (collection is null)
            {
                logger.LogWarning(
                    "Collection not found for deletion Id={CollectionId}", id);

                return Results.NotFound(new ProblemDetails
                {
                    Status = 404,
                    Title = "Collection not found",
                    Detail = $"No collection exists with id {id}."
                });
            }

            var authorizationResult = await authorizationService.AuthorizeAsync(user, collection, new CollectionOwnerRequirement());
            if (!authorizationResult.Succeeded)
            {
                logger.LogWarning(
                    "User {UserId} is not authorized to delete collection Id={CollectionId}", GetUserId(user), id);

                return Results.Forbid();
            }

            await repository.DeleteAsync(id, ct);

            logger.LogInformation("Deleted collection Id={CollectionId}", id);

            return Results.NoContent();
        }).RequireAuthorization();
    }
}
