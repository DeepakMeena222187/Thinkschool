using QuotesApi.Models;

namespace QuotesApi.Services;

// Used only in the "Testing" environment, where the test host has no Service Bus
// namespace to talk to - mirrors how the test host owns QuotesDbContext registration
// instead of this project wiring up SQL Server (see InfrastructureExtensions).
public sealed class NullQuoteEventPublisher : IQuoteEventPublisher
{
    public Task PublishQuoteCreatedAsync(Quote quote, CancellationToken ct) => Task.CompletedTask;
}
