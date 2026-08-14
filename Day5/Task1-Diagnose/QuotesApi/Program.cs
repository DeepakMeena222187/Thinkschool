using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
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
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddProblemDetails();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(QuotePolicies.CanEditQuotes, policy =>
        policy.RequireClaim(QuotePolicies.ScopeClaimType, QuotePolicies.WriteScope));
});
builder.Services.AddSingleton<IAuthorizationHandler, QuoteOwnerAuthorizationHandler>();
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

app.UseAuthentication();
app.UseAuthorization();

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
    await db.Database.MigrateAsync();
}

await app.Services.SeedDevelopmentUserAsync();
await app.Services.SeedDevelopmentCollectionsAsync(app.Environment);

app.MapQuoteEndpoints();

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
