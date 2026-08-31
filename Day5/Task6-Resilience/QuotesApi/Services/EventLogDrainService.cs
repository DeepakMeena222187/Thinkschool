using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Services;

public sealed class EventLogDrainService : BackgroundService
{
    // Bounded independently of, and comfortably inside, the host's own
    // shutdown timeout (Generic Host's HostOptions.ShutdownTimeout defaults
    // to 30s and covers every hosted service's StopAsync collectively) - so
    // this service always hands control back to the host before the host's
    // harder cutoff would forcibly abort the process mid-drain.
    private static readonly TimeSpan DrainGracePeriod = TimeSpan.FromSeconds(10);

    private readonly IEventQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventLogDrainService> _logger;

    public EventLogDrainService(IEventQueue queue, IServiceScopeFactory scopeFactory, ILogger<EventLogDrainService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Deliberately not passing stoppingToken here: the host cancels it
        // the instant shutdown begins, which would abort draining of
        // everything already queued. This loop should end only when the
        // queue itself completes and runs dry (see StopAsync) - that's what
        // "finish what's queued" actually means, as opposed to "stop dead
        // the moment shutdown starts."
        await foreach (var entry in _queue.ReadAllAsync(CancellationToken.None))
        {
            await PersistAsync(entry);
        }
    }

    private async Task PersistAsync(EventLogItem entry)
    {
        // A singleton background service can't hold a scoped QuotesDbContext
        // directly - a fresh scope per item is the only safe way to resolve
        // one, and it's disposed as soon as this item is done.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        try
        {
            db.EventLogs.Add(new EventLog
            {
                EventType = entry.EventType,
                UserId = entry.UserId,
                CreatedAtUtc = entry.CreatedAtUtc,
                Payload = entry.Payload
            });

            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // One bad item must not take down the drain loop - log it and
            // move on so the rest of the queue still gets processed.
            _logger.LogError(ex,
                "Failed to persist EventLog entry EventType={EventType} UserId={UserId}",
                entry.EventType, entry.UserId);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // "Stop accepting new work": close the queue to writers immediately.
        // Any producer still calling TryEnqueue after this point gets false
        // back (see EventQueue.TryEnqueue) - the caller's HTTP response is
        // never affected either way.
        _queue.Complete();

        // "Finish what's already queued", bounded: give the drain loop a
        // grace period to flush whatever is still buffered, capped well
        // under the host's shutdown timeout so we always return control to
        // the host ourselves rather than being force-killed mid-drain.
        using var grace = new CancellationTokenSource(DrainGracePeriod);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, grace.Token);

        try
        {
            await base.StopAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "EventLog drain did not finish within {GracePeriod}; any events still queued were not persisted",
                DrainGracePeriod);
        }
    }
}
