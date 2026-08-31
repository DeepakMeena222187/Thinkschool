using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using QuotesApi.Authentication;
using QuotesApi.Authorization;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using QuotesApi.Options;
using QuotesApi.Services;
using Serilog;
using Serilog.Context;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
}

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] TraceId={TraceId} {SourceContext}: {Message:lj}{NewLine}{Exception}"));

var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";
var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];

var openTelemetryBuilder = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("QuotesApi"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("QuotesApi")
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));

if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    openTelemetryBuilder.UseAzureMonitor(o => o.ConnectionString = appInsightsConnectionString);
}

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// Dev-only: lets a locally-running Angular dev server (ng serve on
// localhost:4200) call this API across origins. Not registered outside
// Development, so it has no effect on any deployed environment.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("LocalAngularDevServer", policy =>
            policy.WithOrigins("http://localhost:4200")
                .AllowAnyHeader()
                .AllowAnyMethod());
    });
}

// Day 17: the deployed frontend's origin is supplied via the App Service
// application setting Cors:AllowedOrigin, never hardcoded here - the same
// API image is meant to work regardless of which origin ends up serving the
// built frontend, without a source change to repoint it.
var deployedFrontendOrigin = builder.Configuration["Cors:AllowedOrigin"];
if (!string.IsNullOrWhiteSpace(deployedFrontendOrigin))
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("DeployedFrontend", policy =>
            policy.WithOrigins(deployedFrontendOrigin)
                .AllowAnyHeader()
                .AllowAnyMethod());
    });
}

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<FlakyEndpointState>();
builder.Services.AddProblemDetails();

var quoteSourceBaseUrl = builder.Configuration["QuoteSource:BaseUrl"] ?? "http://localhost:8080/";

builder.Services.AddHttpClient(ResilienceDemoEndpointExtensions.QuoteSourceHttpClientName, c =>
    {
        c.BaseAddress = new Uri(quoteSourceBaseUrl);
    })
    .AddResilienceHandler("default", (resilienceBuilder, context) =>
    {
        var retryLogger = context.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("QuotesApi.Resilience.QuoteSource");

        resilienceBuilder
            .AddRetry(new()
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(1),
                ShouldHandle = args => new ValueTask<bool>(
                    args.Outcome.Exception is not null || (int?)args.Outcome.Result?.StatusCode >= 500),
                OnRetry = args =>
                {
                    var outcome = args.Outcome.Exception is not null
                        ? args.Outcome.Exception.GetType().Name
                        : $"HTTP {(int)args.Outcome.Result!.StatusCode}";

                    retryLogger.LogWarning(
                        "Retry attempt {AttemptNumber}/{MaxRetryAttempts} after {DelayMs}ms due to {Outcome}",
                        args.AttemptNumber + 1,
                        3,
                        args.RetryDelay.TotalMilliseconds,
                        outcome);

                    return default;
                }
            })
            .AddCircuitBreaker(new()
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 4,
                BreakDuration = TimeSpan.FromSeconds(15),
                ShouldHandle = args => new ValueTask<bool>(
                    args.Outcome.Exception is not null || (int?)args.Outcome.Result?.StatusCode >= 500)
            })
            .AddTimeout(TimeSpan.FromSeconds(10));
    });

// "live" check never touches the database, so the container's liveness probe
// stays healthy while SQL Server is still starting up. The unnamed default
// tag set (used by the plain /health check) includes the DB check, so /health
// reflects real readiness to serve data.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<QuotesDbContext>(name: "database")
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(QuotePolicies.CanEditQuotes, policy =>
        policy.RequireClaim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
});
builder.Services.AddSingleton<IAuthorizationHandler, QuoteOwnerAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, CollectionOwnerAuthorizationHandler>();
var internalIssuer = builder.Configuration["Jwt:Issuer"] ?? "https://localhost";
var internalAudience = builder.Configuration["Jwt:Audience"] ?? "quotes-api";

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = JwtSchemes.Policy;
        options.DefaultChallengeScheme = JwtSchemes.Policy;
    })
    .AddPolicyScheme(JwtSchemes.Policy, "Select the JWT bearer scheme by issuer", options =>
    {
        // Entra config is read lazily here (not into an outer variable) because
        // WebApplicationFactory-based tests inject configuration overrides at
        // builder.Build() time, which runs after this fluent chain executes.
        options.ForwardDefaultSelector = context =>
            SelectJwtScheme(context, internalIssuer, TryGetEntraAuthority(builder.Configuration));
    })
    .AddJwtBearer(JwtSchemes.Internal)
    .AddJwtBearer(JwtSchemes.Entra, options =>
    {
        var entraAuthority = GetEntraAuthority(builder.Configuration);
        options.Authority = entraAuthority;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidIssuer = entraAuthority,
            ValidateAudience = true,
            ValidAudience = GetRequiredConfigurationValue(builder.Configuration, "Entra:Audience", "Entra:ClientId"),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

// JwtBearerOptions is configured via IOptions<JwtOptions> rather than raw
// IConfiguration, so signing key/issuer/audience come from the validated,
// typed options instance instead of ad-hoc config lookups.
builder.Services.AddOptions<JwtBearerOptions>(JwtSchemes.Internal)
    .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        bearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddScoped<AuthService>();

// Demonstrates IOptionsMonitor<T> in a singleton: the hosted service lives for
// the app's lifetime and reacts if Jwt config changes at runtime (e.g. Key
// Vault secret rotation via reloadOnChange), rather than reading a value once.
builder.Services.AddHostedService<JwtOptionsChangeMonitor>();

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

app.Use((ctx, next) =>
{
    using (LogContext.PushProperty("TraceId", ctx.TraceIdentifier))
    {
        app.Logger.LogInformation("Request received {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        return next();
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseCors("LocalAngularDevServer");
}
else if (!string.IsNullOrWhiteSpace(deployedFrontendOrigin))
{
    app.UseCors("DeployedFrontend");
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
}).AllowAnonymous();

// Migration and dev-seeding touch SQL Server, which in a container may not be
// reachable yet (orchestrator start order) or at all (misconfigured
// connection string). Both are guarded so a DB failure here logs and lets the
// app finish starting, rather than crashing the whole host before app.Run()
// - otherwise even /health/live would never come up.
//
// Also excluded from Production (Day 17): the deployed app authenticates to
// SQL as a managed identity scoped to db_datareader/db_datawriter only, on
// purpose - it has no ALTER/DDL rights. Running MigrateAsync() there would
// fail this way on every cold start rather than once - Migrations run
// manually, under an Entra admin identity, against the real database.
if (!app.Environment.IsEnvironment("Testing") && !app.Environment.IsProduction())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        await db.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Database migration failed at startup; continuing so the app can still start. /health will report unhealthy until the database is reachable.");
    }
}

try
{
    await app.Services.SeedDevelopmentUserAsync(app.Environment);
    await app.Services.SeedDevelopmentCollectionsAsync(app.Environment);
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Development seeding failed at startup; continuing without seed data.");
}

app.MapQuoteEndpoints();
app.MapResilienceDemoEndpoints();

app.Run();

static string GetRequiredConfigurationValue(IConfiguration configuration, params string[] keys)
{
    foreach (var key in keys)
    {
        var value = configuration[key];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    throw new InvalidOperationException($"Missing required configuration value. Set one of: {string.Join(", ", keys)}.");
}

static string GetEntraAuthority(IConfiguration configuration)
    => JwtSchemes.GetEntraAuthority(GetRequiredConfigurationValue(configuration, "Entra:TenantId"));

// Entra is treated as not-yet-provisioned (rather than a startup failure) until
// Entra:TenantId is supplied, so internal-only deployments and tests that don't
// configure Entra keep working exactly as before.
static string? TryGetEntraAuthority(IConfiguration configuration)
{
    var tenantId = configuration["Entra:TenantId"];
    return string.IsNullOrWhiteSpace(tenantId) ? null : JwtSchemes.GetEntraAuthority(tenantId);
}

static string SelectJwtScheme(HttpContext context, string internalIssuer, string? entraAuthority)
{
    var authorization = context.Request.Headers.Authorization.ToString();
    if (string.IsNullOrWhiteSpace(authorization) ||
        !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return JwtSchemes.Internal;
    }

    var token = authorization["Bearer ".Length..].Trim();
    if (string.IsNullOrWhiteSpace(token))
    {
        return JwtSchemes.Internal;
    }

    try
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        if (entraAuthority is not null &&
            string.Equals(jwt.Issuer, entraAuthority, StringComparison.OrdinalIgnoreCase))
        {
            return JwtSchemes.Entra;
        }

        if (string.Equals(jwt.Issuer, internalIssuer, StringComparison.OrdinalIgnoreCase))
        {
            return JwtSchemes.Internal;
        }
    }
    catch
    {
    }

    return JwtSchemes.Internal;
}
