using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using QuotesApi.Contracts;
using QuotesApi.Models;
using QuotesApi.Repositories;

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
                CreatedAtUtc = DateTime.UtcNow
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
    }
}
