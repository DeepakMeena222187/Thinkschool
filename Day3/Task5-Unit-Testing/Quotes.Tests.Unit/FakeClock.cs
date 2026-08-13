using QuotesApi.Services;

namespace Quotes.Tests.Unit;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }
}
