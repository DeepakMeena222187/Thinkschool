using QuotesApi.Models;

namespace QuotesApi.Services;

public interface IQuoteEventPublisher
{
    Task PublishQuoteCreatedAsync(Quote quote, CancellationToken ct);
}
