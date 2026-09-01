using Azure.Identity;
using Azure.Messaging.ServiceBus;

// On-demand idempotency trigger: sends a brand-new Service Bus message reusing
// the MessageId of a quote that was already published and processed. There is
// no "republish" button in the app itself - the publisher only ever sends a
// QuoteCreated message once, at creation time - so this tool exists purely to
// let the dedupe path (the unique index on AuditLogEntries.MessageId) be
// exercised on demand instead of only implicitly, via a crash/redelivery.
//
// Usage: dotnet run -- <quoteId> [namespace] [topicName]

if (args.Length < 1 || !int.TryParse(args[0], out var quoteId))
{
    Console.Error.WriteLine("Usage: dotnet run -- <quoteId> [namespace] [topicName]");
    return 1;
}

var serviceBusNamespace = args.Length > 1 ? args[1] : "quotes-day19-bus.servicebus.windows.net";
var topicName = args.Length > 2 ? args[2] : "quote-events";

// Constrained to what a local `az login` provides - see the matching comment
// in PoisonMessageSender/Program.cs and QuotesApi.Services.ServiceBusCredentialFactory.
var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ExcludeEnvironmentCredential = false,
    ExcludeManagedIdentityCredential = true,
    ExcludeWorkloadIdentityCredential = true,
    ExcludeVisualStudioCredential = true,
    ExcludeVisualStudioCodeCredential = true,
    ExcludeAzureCliCredential = false,
    ExcludeAzurePowerShellCredential = true,
    ExcludeAzureDeveloperCliCredential = true,
    ExcludeInteractiveBrowserCredential = true
});

await using var client = new ServiceBusClient(serviceBusNamespace, credential);
await using var sender = client.CreateSender(topicName);

var messageId = $"QuoteCreated:{quoteId}";
var message = new ServiceBusMessage($$"""{"quoteId":{{quoteId}},"note":"manual republish for idempotency test"}""")
{
    MessageId = messageId,
    ContentType = "application/json",
    Subject = "QuoteCreated"
};

await sender.SendMessageAsync(message);

Console.WriteLine($"Sent republish MessageId={messageId} to {serviceBusNamespace}/{topicName}.");
Console.WriteLine("AuditLogWorker should hit the unique-constraint branch and complete it without a new row.");
return 0;
