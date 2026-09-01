using System.Text.Json;
using Azure.Messaging.ServiceBus;
using QuotesApi.Models;

namespace QuotesApi.Services;

// MessageId is "{EventType}:{Quote.Id}" - stable and derivable from the quote's own
// identity (assigned by SQL Server on insert), not a random GUID. A quote is created
// exactly once, so its id is already the natural dedupe key; the EventType prefix keeps
// it from colliding with a future event type keyed off the same integer. This id is
// what AuditWorker's unique index dedupes on - see AuditLogEntry.
public sealed class ServiceBusQuoteEventPublisher : IQuoteEventPublisher
{
    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusQuoteEventPublisher> _logger;

    public ServiceBusQuoteEventPublisher(ServiceBusClient client, IConfiguration configuration, ILogger<ServiceBusQuoteEventPublisher> logger)
    {
        var topicName = configuration["ServiceBus:TopicName"] ?? "quote-events";
        _sender = client.CreateSender(topicName);
        _logger = logger;
    }

    public async Task PublishQuoteCreatedAsync(Quote quote, CancellationToken ct)
    {
        const string eventType = "QuoteCreated";

        var payload = JsonSerializer.Serialize(new
        {
            quoteId = quote.Id,
            author = quote.Author,
            text = quote.Text,
            ownerId = quote.OwnerId,
            createdAtUtc = quote.CreatedAtUtc
        });

        var message = new ServiceBusMessage(payload)
        {
            MessageId = $"{eventType}:{quote.Id}",
            ContentType = "application/json",
            Subject = eventType
        };
        message.ApplicationProperties["EventType"] = eventType;

        // Logged before the actual network call so a stall (e.g. credential
        // acquisition hanging) is visible as "Publishing..." with no matching
        // "Published..." or catch-side error - distinct from the call never
        // having been reached at all, and from a thrown-and-logged failure.
        _logger.LogInformation(
            "Publishing {EventType} QuoteId={QuoteId} MessageId={MessageId} to topic {TopicName}",
            eventType, quote.Id, message.MessageId, _sender.EntityPath);

        await _sender.SendMessageAsync(message, ct);

        _logger.LogInformation(
            "Published {EventType} QuoteId={QuoteId} MessageId={MessageId}",
            eventType, quote.Id, message.MessageId);
    }
}
