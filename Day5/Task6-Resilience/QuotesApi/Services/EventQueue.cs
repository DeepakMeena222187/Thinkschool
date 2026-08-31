using System.Threading.Channels;

namespace QuotesApi.Services;

public sealed record EventLogItem(string EventType, int UserId, string Payload, DateTime CreatedAtUtc);

public interface IEventQueue
{
    bool TryEnqueue(EventLogItem entry);
    IAsyncEnumerable<EventLogItem> ReadAllAsync(CancellationToken ct);
    void Complete();
}

public sealed class EventQueue : IEventQueue
{
    // Bounded to cap worst-case memory if the drain falls behind - each entry
    // is a handful of small fields, so even a full queue is trivial memory,
    // while still large enough to absorb realistic bursts between drains.
    private const int Capacity = 1000;

    private readonly Channel<EventLogItem> _channel = Channel.CreateBounded<EventLogItem>(
        new BoundedChannelOptions(Capacity)
        {
            // Wait would block the enqueuing request on a slow/stalled drain,
            // defeating the point of moving this off the request path.
            // DropOldest/DropNewest would silently discard an entry some
            // other, already-returned request believed had been queued.
            // DropWrite instead sacrifices only the write happening right
            // now, at the one call site that can see and log it - nothing
            // already accepted into the queue is ever disturbed.
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ILogger<EventQueue> _logger;

    public EventQueue(ILogger<EventQueue> logger)
    {
        _logger = logger;
    }

    public bool TryEnqueue(EventLogItem entry)
    {
        // DropWrite makes TryWrite always report success - the runtime just
        // discards the incoming item instead of returning false. Check depth
        // immediately beforehand so a drop is still observable in logs
        // rather than vanishing without a trace. A small race under heavy
        // concurrent writers is acceptable for an observability signal.
        if (_channel.Reader.Count >= Capacity)
        {
            _logger.LogError(
                "EventLog queue is full (capacity={Capacity}); dropping EventType={EventType} UserId={UserId}",
                Capacity, entry.EventType, entry.UserId);
        }

        try
        {
            return _channel.Writer.TryWrite(entry);
        }
        catch (ChannelClosedException)
        {
            _logger.LogWarning(
                "EventLog queue is closed for writes (shutdown in progress); dropping EventType={EventType} UserId={UserId}",
                entry.EventType, entry.UserId);
            return false;
        }
    }

    public IAsyncEnumerable<EventLogItem> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    public void Complete() => _channel.Writer.TryComplete();
}
