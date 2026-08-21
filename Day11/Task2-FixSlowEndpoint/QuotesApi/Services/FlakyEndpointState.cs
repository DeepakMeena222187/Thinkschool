namespace QuotesApi.Services;

// Backs the /api/flaky demo endpoint: fails the first two calls in every
// rolling 30-second window, then succeeds, so the Polly retry pipeline in
// front of it has real transient failures to recover from.
public sealed class FlakyEndpointState
{
    private static readonly TimeSpan WindowDuration = TimeSpan.FromSeconds(30);
    private readonly IClock _clock;
    private readonly Lock _lock = new();
    private DateTimeOffset _windowStart = DateTimeOffset.MinValue;
    private int _callsInWindow;

    public FlakyEndpointState(IClock clock)
    {
        _clock = clock;
    }

    public bool ShouldFail()
    {
        var now = _clock.UtcNow;

        lock (_lock)
        {
            if (now - _windowStart >= WindowDuration)
            {
                _windowStart = now;
                _callsInWindow = 0;
            }

            _callsInWindow++;
            return _callsInWindow <= 2;
        }
    }
}
