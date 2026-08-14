using Microsoft.Extensions.Options;
using QuotesApi.Options;

namespace QuotesApi.Services;

public sealed class JwtOptionsChangeMonitor : IHostedService
{
    private readonly ILogger<JwtOptionsChangeMonitor> _logger;
    private IDisposable? _subscription;

    public JwtOptionsChangeMonitor(IOptionsMonitor<JwtOptions> jwtOptionsMonitor, ILogger<JwtOptionsChangeMonitor> logger)
    {
        _logger = logger;
        _subscription = jwtOptionsMonitor.OnChange(options =>
            _logger.LogWarning(
                "Jwt configuration changed at runtime (Issuer={Issuer}, Audience={Audience}); restart or re-issue tokens if the signing key rotated",
                options.Issuer, options.Audience));
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;
        return Task.CompletedTask;
    }
}
