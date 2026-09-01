using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.AuditWorker;

// Competing consumers: run two instances of this process pointed at the same
// "audit-log" subscription and Service Bus load-balances messages across
// whichever instance currently holds each message's lock. Nothing here is
// instance-specific - the only coordination is the subscription itself.
public sealed class AuditLogWorker : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditLogWorker> _logger;
    private readonly string _topicName;
    private readonly string _subscriptionName;
    private ServiceBusProcessor? _processor;

    public AuditLogWorker(
        ServiceBusClient client, IServiceScopeFactory scopeFactory, ILogger<AuditLogWorker> logger, IConfiguration configuration)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _topicName = configuration["ServiceBus:TopicName"] ?? "quote-events";
        _subscriptionName = configuration["ServiceBus:SubscriptionName"] ?? "audit-log";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // AutoCompleteMessages is off: completion only happens once the
        // AuditLogEntries row is actually committed (see ProcessMessageAsync),
        // never implicitly just because the handler returned without throwing.
        _processor = _client.CreateProcessor(_topicName, _subscriptionName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 4
        });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        // This call is the only network-touching step before any message can
        // be pumped - it's where token acquisition actually happens (the
        // ServiceBusClient built in Program.cs doesn't connect at
        // construction). Logged on both sides of the await so a stall here
        // (credential chain, network, wrong entity name) is visible as
        // "Starting..." with no matching "started" - instead of silence.
        _logger.LogInformation(
            "Starting Service Bus processor Topic={TopicName} Subscription={SubscriptionName}",
            _topicName, _subscriptionName);

        try
        {
            await _processor.StartProcessingAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "Failed to start Service Bus processor Topic={TopicName} Subscription={SubscriptionName}",
                _topicName, _subscriptionName);
            throw;
        }

        _logger.LogInformation(
            "Service Bus processor started; waiting for messages Topic={TopicName} Subscription={SubscriptionName}",
            _topicName, _subscriptionName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message;

        // Unconditional, before any branch below - proves the processor is
        // actually receiving off the subscription, independent of whether
        // this particular message goes on to succeed, dedupe, or fail.
        _logger.LogInformation(
            "Received MessageId={MessageId} DeliveryCount={DeliveryCount}",
            message.MessageId, message.DeliveryCount);

        // On-demand poison path: a message tagged ForcePoison (see the
        // PoisonMessageSender tool) fails deterministically before any DB
        // work, regardless of payload shape, so the dead-letter demo doesn't
        // depend on crafting invalid data.
        if (message.ApplicationProperties.TryGetValue("ForcePoison", out var forcePoison) && forcePoison is true)
        {
            _logger.LogWarning(
                "Deliberately failing MessageId={MessageId} DeliveryCount={DeliveryCount} (ForcePoison)",
                message.MessageId, message.DeliveryCount);
            await args.AbandonMessageAsync(message, cancellationToken: CancellationToken.None);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        try
        {
            var payload = JsonDocument.Parse(message.Body.ToString());
            var quoteId = payload.RootElement.GetProperty("quoteId").GetInt32();

            db.AuditLogEntries.Add(new AuditLogEntry
            {
                MessageId = message.MessageId,
                EventType = message.Subject ?? "Unknown",
                QuoteId = quoteId,
                Payload = message.Body.ToString(),
                ProcessedAtUtc = DateTime.UtcNow
            });

            // The insert commit IS the "processed" checkpoint - it happens
            // before CompleteMessageAsync, so a crash between the two just
            // means the redelivery hits the unique-constraint branch below
            // and completes without redoing the work.
            await db.SaveChangesAsync(CancellationToken.None);

            await args.CompleteMessageAsync(message, CancellationToken.None);

            _logger.LogInformation(
                "Processed MessageId={MessageId} QuoteId={QuoteId}", message.MessageId, quoteId);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Unique index on MessageId already has a row: this exact message
            // was already fully processed, either as a genuine duplicate
            // delivery or a redelivery after a crash that happened after the
            // prior commit but before that prior attempt completed the
            // message. Either way, nothing left to do but acknowledge it.
            _logger.LogInformation(
                "MessageId={MessageId} already processed; skipping duplicate", message.MessageId);
            await args.CompleteMessageAsync(message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Abandon immediately (rather than letting the PT1M lock expire)
            // so 3 delivery attempts - and dead-lettering - happen quickly
            // enough to demo on demand.
            _logger.LogError(ex,
                "Failed to process MessageId={MessageId} DeliveryCount={DeliveryCount}",
                message.MessageId, message.DeliveryCount);
            await args.AbandonMessageAsync(message, cancellationToken: CancellationToken.None);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        // Full exception (not just its message) via the ILogger exception
        // overload - this is the processor's own error channel, separate
        // from per-message failures handled in ProcessMessageAsync, and
        // fires for things like connection drops or auth failures that
        // happen after StartProcessingAsync already succeeded once.
        _logger.LogError(args.Exception,
            "Service Bus processor error Source={ErrorSource} Namespace={FullyQualifiedNamespace} Entity={EntityPath} Identifier={Identifier}",
            args.ErrorSource, args.FullyQualifiedNamespace, args.EntityPath, args.Identifier);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
