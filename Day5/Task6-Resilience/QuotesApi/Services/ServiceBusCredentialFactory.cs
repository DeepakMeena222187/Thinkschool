using Azure.Identity;

namespace QuotesApi.Services;

// Single source of truth for the constrained DefaultAzureCredential chain used
// by every Service Bus client in this solution (QuotesApi's publisher and
// QuotesApi.AuditWorker's consumer). An unconstrained DefaultAzureCredential
// probes Managed Identity (an IMDS timeout on a box that isn't an Azure
// VM/App Service), Visual Studio, and Visual Studio Code credentials before
// ever reaching Azure CLI - each a multi-second-plus stall for a source that
// can't succeed on a local dev box, and with no logging around the token
// acquisition call site, that stall is indistinguishable from a hang.
// Production keeps Managed Identity (what the deployed App Service actually
// authenticates with); everywhere else is cut down to just what a local
// `az login` provides.
public static class ServiceBusCredentialFactory
{
    public static DefaultAzureCredential Create(bool isProduction) => isProduction
        ? new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeEnvironmentCredential = false,
            ExcludeManagedIdentityCredential = false,
            ExcludeWorkloadIdentityCredential = true,
            ExcludeVisualStudioCredential = true,
            ExcludeVisualStudioCodeCredential = true,
            ExcludeAzureCliCredential = true,
            ExcludeAzurePowerShellCredential = true,
            ExcludeAzureDeveloperCliCredential = true,
            ExcludeInteractiveBrowserCredential = true
        })
        : new DefaultAzureCredential(new DefaultAzureCredentialOptions
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
}
