using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using QuotesApi.AuditWorker;
using QuotesApi.Data;
using QuotesApi.Services;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Quotes")
    ?? throw new InvalidOperationException("Missing required configuration value: ConnectionStrings:Quotes.");

builder.Services.AddDbContext<QuotesDbContext>(options => options.UseSqlServer(connectionString));

var serviceBusNamespace = builder.Configuration["ServiceBus:Namespace"]
    ?? throw new InvalidOperationException("Missing required configuration value: ServiceBus:Namespace.");

// DefaultAzureCredential, no connection string / SAS key - same managed identity
// approach as QuotesApi's own Service Bus and SQL access. Shared factory with
// the API (see ServiceBusCredentialFactory) so this can't independently drift
// back to an unconstrained chain that stalls on credential sources that can't
// apply to a local worker process.
var credential = ServiceBusCredentialFactory.Create(builder.Environment.IsProduction());
builder.Services.AddSingleton(_ => new ServiceBusClient(serviceBusNamespace, credential));

// Default BackgroundService behavior on an unhandled exception from
// ExecuteAsync is to do nothing - the process stays alive, looking healthy,
// while the worker is silently dead. This worker has exactly one job, so
// there's no reason to keep the process up if it can't do it: an unhandled
// failure now stops the host, so a real crash is at least visible as a
// process exit rather than more indistinguishable silence.
builder.Services.Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);

builder.Services.AddHostedService<AuditLogWorker>();

var host = builder.Build();
host.Run();
