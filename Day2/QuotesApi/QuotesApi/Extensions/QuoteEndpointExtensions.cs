using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using QuotesApi.Contracts;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class QuoteEndpointExtensions
{
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
    ILoggerFactory loggerFactory,
    IClock clock,
    CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Quotes");

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

            var quote = new Quote
            {
                Author = request.Author.Trim(),
                Text = request.Text.Trim(),
                CreatedAtUtc = clock.UtcNow.UtcDateTime
            };

            await repository.AddAsync(quote, ct);

            logger.LogInformation(
                "Created quote Id={QuoteId} Author={Author}",
                quote.Id, quote.Author);

            return Results.Created($"/api/quotes/{quote.Id}", quote);
        });

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
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("QuotesApi.Quotes");
            var deleted = await repository.DeleteAsync(id, ct);

            if (!deleted)
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

            logger.LogInformation("Deleted quote Id={QuoteId}", id);

            return Results.NoContent();
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
        });

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
        });

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
        });
    }
}
