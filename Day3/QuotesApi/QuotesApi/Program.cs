using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Authentication;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using QuotesApi.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddProblemDetails();
builder.Services.AddAuthorization();
var internalIssuer = builder.Configuration["Jwt:Issuer"] ?? "https://localhost";
var internalAudience = builder.Configuration["Jwt:Audience"] ?? "quotes-api";

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
    .AddJwtBearer(JwtSchemes.Internal, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthService.GetJwtSecret(builder.Configuration))),
            ValidateIssuer = true,
            ValidIssuer = internalIssuer,
            ValidateAudience = true,
            ValidAudience = internalAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    })
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

builder.Services.AddScoped<AuthService>();

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
    await db.Database.MigrateAsync();
}

await app.Services.SeedDevelopmentUserAsync();

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
