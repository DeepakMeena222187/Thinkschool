using Azure.Identity;
using Azure.Messaging.ServiceBus;

// On-demand poison-message trigger for the Day 19 DLQ demo: sends one message
// to quote-events tagged ForcePoison=true. AuditLogWorker checks that property
// before touching the database and abandons the message immediately, so after
// 3 delivery attempts (the audit-log subscription's maxDeliveryCount) Service
// Bus dead-letters it - deterministically, independent of payload shape.
//
// Usage: dotnet run [-- <namespace> [topicName]]
// Defaults to the Day 19 namespace/topic if no arguments are given.

var serviceBusNamespace = args.Length > 0 ? args[0] : "quotes-day19-bus.servicebus.windows.net";
var topicName = args.Length > 1 ? args[1] : "quote-events";

// Constrained to what a local `az login` provides - the same reasoning as
// QuotesApi.Services.ServiceBusCredentialFactory (this tool only ever runs
// against a dev box, never deployed, so there's no Production branch here).
// A bare DefaultAzureCredential() probes Managed Identity, Visual Studio, and
// Visual Studio Code credentials first, each a real multi-second-plus stall
// on a box where none of them can succeed.
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

var messageId = $"Poison:{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}";
var message = new ServiceBusMessage("""{"note":"deliberate poison test message"}""")
{
    MessageId = messageId,
    ContentType = "application/json",
    Subject = "QuoteCreated"
};
message.ApplicationProperties["ForcePoison"] = true;

await sender.SendMessageAsync(message);

Console.WriteLine($"Sent poison message MessageId={messageId} to {serviceBusNamespace}/{topicName}.");
Console.WriteLine("It will dead-letter on the audit-log subscription after 3 delivery attempts.");
