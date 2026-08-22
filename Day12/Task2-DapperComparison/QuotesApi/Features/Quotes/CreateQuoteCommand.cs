using MediatR;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Services;

namespace QuotesApi.Features.Quotes;

public sealed record CreateQuoteCommand(string Author, string Text, int OwnerId) : IRequest<int>;

public sealed class CreateQuoteCommandHandler(QuotesDbContext db, IClock clock) : IRequestHandler<CreateQuoteCommand, int>
{
    public async Task<int> Handle(CreateQuoteCommand request, CancellationToken cancellationToken)
    {
        var quote = Quote.Create(request.Author, request.Text, request.OwnerId, clock.UtcNow.UtcDateTime);

        db.Quotes.Add(quote);
        await db.SaveChangesAsync(cancellationToken);

        return quote.Id;
    }
}
