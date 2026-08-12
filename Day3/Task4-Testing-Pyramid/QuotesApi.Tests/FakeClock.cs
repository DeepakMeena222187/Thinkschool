using QuotesApi.Services;

namespace QuotesApi.Tests;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }
}
