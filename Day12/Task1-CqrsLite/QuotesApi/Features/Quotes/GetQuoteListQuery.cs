using MediatR;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Features.Quotes;

public sealed record QuoteListItemDto(int Id, string Author, string Text, DateTime CreatedAtUtc, bool IsOwnedByCurrentUser);

public sealed record GetQuoteListQuery(int CurrentUserId) : IRequest<List<QuoteListItemDto>>;

public sealed class GetQuoteListQueryHandler(QuotesDbContext db) : IRequestHandler<GetQuoteListQuery, List<QuoteListItemDto>>
{
    public Task<List<QuoteListItemDto>> Handle(GetQuoteListQuery request, CancellationToken cancellationToken)
    {
        return db.Quotes
            .AsNoTracking()
            .OrderByDescending(q => q.CreatedAtUtc)
            .Select(q => new QuoteListItemDto(
                q.Id,
                q.Author,
                q.Text,
                q.CreatedAtUtc,
                q.OwnerId == request.CurrentUserId))
            .ToListAsync(cancellationToken);
    }
}
